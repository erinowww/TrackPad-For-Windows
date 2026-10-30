namespace TrackPadForWindows;

// Parmakların yüzeye değip kalkmasını izler: kısa ve hareketsiz dokunuşları "tap",
// birlikte belli bir mesafe kayan parmakları "swipe" olarak bildirir.
internal sealed class TapDetector
{
    private readonly Dictionary<int, (int X, int Y)> _startPositions = new();
    private bool _active;
    private bool _swiped;
    private long _startMs;
    private int _maxFingers;
    private double _maxMove;

    public int MaxTapMs { get; set; } = 350;
    public double MaxMove { get; set; } = 0.05;
    public double SwipeDistance { get; set; } = 0.12;

    // fingers, durationMs, maxMove (yüzey genişliğine oranla)
    public event Action<int, long, double>? Tapped;
    public event Action<int, long, double>? TouchEnded;
    // fingers, direction (up/down/left/right)
    public event Action<int, string>? Swiped;

    public void OnFrame(DeviceParser device, List<Contact> frame)
    {
        long now = Environment.TickCount64;
        int fingers = frame.Count(c => c.Tip);

        if (!_active)
        {
            if (fingers == 0) return;
            _active = true;
            _swiped = false;
            _startMs = now;
            _maxFingers = 0;
            _maxMove = 0;
            _startPositions.Clear();
        }

        _maxFingers = Math.Max(_maxFingers, fingers);
        double sumDx = 0, sumDy = 0;
        int moving = 0;
        foreach (var c in frame)
        {
            if (!c.Tip) continue;
            if (!_startPositions.TryGetValue(c.Id, out var start))
            {
                _startPositions[c.Id] = (c.X, c.Y);
                continue;
            }
            double dx = (c.X - start.X) / (double)device.XMax;
            double dy = (c.Y - start.Y) / (double)device.YMax;
            _maxMove = Math.Max(_maxMove, Math.Max(Math.Abs(dx), Math.Abs(dy)));
            sumDx += dx;
            sumDy += dy;
            moving++;
        }

        // Tüm parmaklar yüzeydeyken ortalama kayma eşiği geçince bir kez swipe bildir.
        if (!_swiped && fingers >= 3 && fingers == _maxFingers && moving == fingers)
        {
            double ax = sumDx / moving, ay = sumDy / moving;
            if (Math.Max(Math.Abs(ax), Math.Abs(ay)) >= SwipeDistance)
            {
                _swiped = true;
                string dir = Math.Abs(ax) > Math.Abs(ay) ? (ax > 0 ? "right" : "left") : (ay > 0 ? "down" : "up");
                Swiped?.Invoke(fingers, dir);
            }
        }

        if (fingers == 0)
        {
            _active = false;
            long duration = now - _startMs;
            TouchEnded?.Invoke(_maxFingers, duration, _maxMove);
            if (!_swiped && duration <= MaxTapMs && _maxMove <= MaxMove)
                Tapped?.Invoke(_maxFingers, duration, _maxMove);
        }
    }
}
