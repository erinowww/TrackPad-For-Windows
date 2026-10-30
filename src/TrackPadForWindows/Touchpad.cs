using System.Runtime.InteropServices;
using static TrackPadForWindows.Native;

namespace TrackPadForWindows;

internal readonly record struct Contact(int Id, bool Tip, int X, int Y);

// Precision touchpad'den gelen ham HID raporlarını parmak listesine (frame) çevirir.
internal sealed class TouchpadReader : IDisposable
{
    private readonly Dictionary<IntPtr, DeviceParser?> _devices = new();

    // Her tamamlanan frame için: cihazın X/Y aralığı ve parmaklar.
    public event Action<DeviceParser, List<Contact>>? FrameReceived;

    public static bool Register(IntPtr hwnd)
    {
        var rid = new[]
        {
            new RAWINPUTDEVICE
            {
                usUsagePage = HID_USAGE_PAGE_DIGITIZER,
                usUsage = HID_USAGE_DIGITIZER_TOUCHPAD,
                dwFlags = RIDEV_INPUTSINK, // uygulama arka plandayken de veri al
                hwndTarget = hwnd,
            },
        };
        return RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }

    // Sistemde Precision touchpad olarak görünen cihaz var mı?
    public static bool PrecisionTouchpadExists()
    {
        uint count = 0;
        uint itemSize = (uint)Marshal.SizeOf<RAWINPUTDEVICELIST>();
        GetRawInputDeviceList(null, ref count, itemSize);
        if (count == 0) return false;
        var list = new RAWINPUTDEVICELIST[count];
        if (GetRawInputDeviceList(list, ref count, itemSize) == uint.MaxValue) return false;

        foreach (var d in list)
        {
            if (d.dwType != RIM_TYPEHID) continue;
            uint size = 32;
            IntPtr buf = Marshal.AllocHGlobal((int)size);
            try
            {
                Marshal.WriteInt32(buf, 0, (int)size); // cbSize
                if (GetRawInputDeviceInfo(d.hDevice, RIDI_DEVICEINFO, buf, ref size) == uint.MaxValue) continue;
                ushort usagePage = (ushort)Marshal.ReadInt16(buf, 20);
                ushort usage = (ushort)Marshal.ReadInt16(buf, 22);
                if (usagePage == HID_USAGE_PAGE_DIGITIZER && usage == HID_USAGE_DIGITIZER_TOUCHPAD) return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
        return false;
    }

    public void HandleWmInput(IntPtr lParam)
    {
        int headerSize = 8 + 2 * IntPtr.Size; // RAWINPUTHEADER
        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, null, ref size, (uint)headerSize);
        if (size == 0) return;
        var data = new byte[size];
        if (GetRawInputData(lParam, RID_INPUT, data, ref size, (uint)headerSize) != size) return;

        uint type = BitConverter.ToUInt32(data, 0);
        if (type != RIM_TYPEHID) return;
        IntPtr hDevice = IntPtr.Size == 8 ? (IntPtr)BitConverter.ToInt64(data, 8) : (IntPtr)BitConverter.ToInt32(data, 8);

        if (!_devices.TryGetValue(hDevice, out var parser))
        {
            parser = DeviceParser.TryCreate(hDevice);
            _devices[hDevice] = parser;
        }
        if (parser == null) return;

        // RAWHID: dwSizeHid, dwCount, ardından dwCount adet rapor.
        int reportSize = BitConverter.ToInt32(data, headerSize);
        int reportCount = BitConverter.ToInt32(data, headerSize + 4);
        int offset = headerSize + 8;
        for (int i = 0; i < reportCount; i++)
        {
            if (offset + reportSize > data.Length) break;
            var report = new byte[reportSize];
            Buffer.BlockCopy(data, offset, report, 0, reportSize);
            offset += reportSize;

            var frame = parser.Feed(report);
            if (frame != null) FrameReceived?.Invoke(parser, frame);
        }
    }

    public void Dispose()
    {
        foreach (var p in _devices.Values) p?.Dispose();
        _devices.Clear();
    }
}

internal sealed class DeviceParser : IDisposable
{
    private readonly IntPtr _preparsed;
    private readonly List<ushort> _fingerLinks = new();
    private readonly HashSet<ushort> _linksWithId = new();
    private readonly ushort? _contactCountLink;

    // Birden fazla rapora bölünen frame'in birikimi ("hybrid" mod).
    private List<Contact>? _pending;
    private int _expected;

    public int XMax { get; private set; } = 1;
    public int YMax { get; private set; } = 1;

    private DeviceParser(IntPtr preparsed)
    {
        _preparsed = preparsed;
        HidP_GetCaps(preparsed, out var caps);

        ushort n = caps.NumberInputValueCaps;
        var raw = new byte[n * HIDP_VALUE_CAPS_SIZE];
        HidP_GetValueCaps(HidP_Input, raw, ref n, preparsed);

        var links = new SortedSet<ushort>();
        for (int i = 0; i < n; i++)
        {
            int o = i * HIDP_VALUE_CAPS_SIZE;
            ushort usagePage = BitConverter.ToUInt16(raw, o);
            ushort link = BitConverter.ToUInt16(raw, o + 6);
            bool isRange = raw[o + 12] != 0;
            int logicalMax = BitConverter.ToInt32(raw, o + 44);
            ushort usage = BitConverter.ToUInt16(raw, o + 56); // NotRange.Usage veya Range.UsageMin
            if (isRange) continue;

            if (usagePage == HID_USAGE_PAGE_GENERIC && usage == HID_USAGE_GENERIC_X)
            {
                links.Add(link);
                if (logicalMax > 0) XMax = logicalMax;
            }
            else if (usagePage == HID_USAGE_PAGE_GENERIC && usage == HID_USAGE_GENERIC_Y)
            {
                if (logicalMax > 0) YMax = logicalMax;
            }
            else if (usagePage == HID_USAGE_PAGE_DIGITIZER && usage == HID_USAGE_DIGITIZER_CONTACT_ID)
            {
                _linksWithId.Add(link);
            }
            else if (usagePage == HID_USAGE_PAGE_DIGITIZER && usage == HID_USAGE_DIGITIZER_CONTACT_COUNT)
            {
                _contactCountLink = link;
            }
        }
        _fingerLinks.AddRange(links);
    }

    public static DeviceParser? TryCreate(IntPtr hDevice)
    {
        uint size = 0;
        GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, IntPtr.Zero, ref size);
        if (size == 0) return null;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        if (GetRawInputDeviceInfo(hDevice, RIDI_PREPARSEDDATA, buf, ref size) == uint.MaxValue)
        {
            Marshal.FreeHGlobal(buf);
            return null;
        }
        var parser = new DeviceParser(buf);
        if (parser._fingerLinks.Count == 0)
        {
            parser.Dispose();
            return null;
        }
        return parser;
    }

    // Bir raporu işler; frame tamamlandıysa parmak listesini döndürür.
    public List<Contact>? Feed(byte[] report)
    {
        uint len = (uint)report.Length;
        int count = -1;
        if (_contactCountLink is ushort ccLink &&
            HidP_GetUsageValue(HidP_Input, HID_USAGE_PAGE_DIGITIZER, ccLink, HID_USAGE_DIGITIZER_CONTACT_COUNT,
                out uint cc, _preparsed, report, len) == HIDP_STATUS_SUCCESS)
        {
            count = (int)cc;
        }

        if (count > 0)
        {
            _pending = new List<Contact>(count);
            _expected = count;
        }
        else if (count < 0)
        {
            // Contact Count desteklenmiyor: her rapor kendi başına bir frame.
            _pending = new List<Contact>();
            _expected = _fingerLinks.Count;
        }
        else if (_pending == null)
        {
            return null; // devam raporu ama başlamış frame yok
        }

        var usages = new ushort[32];
        foreach (var link in _fingerLinks)
        {
            if (_pending.Count >= _expected) break;
            if (HidP_GetUsageValue(HidP_Input, HID_USAGE_PAGE_GENERIC, link, HID_USAGE_GENERIC_X,
                    out uint x, _preparsed, report, len) != HIDP_STATUS_SUCCESS) continue;
            HidP_GetUsageValue(HidP_Input, HID_USAGE_PAGE_GENERIC, link, HID_USAGE_GENERIC_Y,
                out uint y, _preparsed, report, len);

            int id = link;
            if (_linksWithId.Contains(link) &&
                HidP_GetUsageValue(HidP_Input, HID_USAGE_PAGE_DIGITIZER, link, HID_USAGE_DIGITIZER_CONTACT_ID,
                    out uint cid, _preparsed, report, len) == HIDP_STATUS_SUCCESS)
            {
                id = (int)cid;
            }

            bool tip = false;
            uint usageLen = (uint)usages.Length;
            if (HidP_GetUsages(HidP_Input, HID_USAGE_PAGE_DIGITIZER, link, usages, ref usageLen,
                    _preparsed, report, len) == HIDP_STATUS_SUCCESS)
            {
                for (int i = 0; i < usageLen; i++)
                {
                    if (usages[i] == HID_USAGE_DIGITIZER_TIP_SWITCH) { tip = true; break; }
                }
            }

            _pending.Add(new Contact(id, tip, (int)x, (int)y));
        }

        if (count < 0 || _pending.Count >= _expected)
        {
            var frame = _pending;
            _pending = null;
            return frame;
        }
        return null;
    }

    public void Dispose() => Marshal.FreeHGlobal(_preparsed);
}
