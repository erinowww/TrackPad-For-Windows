using Microsoft.Win32;
using static TrackPadForWindows.Native;

namespace TrackPadForWindows;

internal sealed class TrayApp : ApplicationContext
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "TrackPad For Windows";
    private const string RunValue = "TrackPadForWindows";
    private const string LegacyRunValue = "TouchMenu";

    private readonly InputWindow _window;
    private readonly TouchpadReader _reader = new();
    private readonly TapDetector _taps = new();
    private readonly NotifyIcon _tray;
    private AppConfig _config;
    private RadialMenu? _radial;
    private MediaWidget? _media;
    private SettingsForm? _settings;
    private IntPtr _previousForeground;
    private Point _menuAnchor;

    public TrayApp()
    {
        _config = LoadConfigOrDefault();
        ApplyTapSettings();
        MigrateStartupEntry();

        _tray = new NotifyIcon
        {
            Icon = AppIcon.Get(),
            Text = AppName,
            Visible = true,
        };
        RebuildTrayMenu();
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowRadialMenu();
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _reader.FrameReceived += _taps.OnFrame;
        _taps.TouchEnded += (fingers, ms, move) =>
            _tray.Text = $"{AppName} – son dokunuş: {fingers} parmak, {ms} ms";
        _taps.Tapped += OnTapped;
        _taps.Swiped += OnSwiped;

        _window = new InputWindow(_reader);
        if (!TouchpadReader.Register(_window.Handle))
            ShowError("Touchpad girişi için kayıt yapılamadı.");
        else if (!TouchpadReader.PrecisionTouchpadExists())
            _tray.ShowBalloonTip(5000, AppName,
                "Precision touchpad bulunamadı. Hareketler algılanmayabilir.", ToolTipIcon.Warning);
    }

    private void OnTapped(int fingers, long ms, double move)
    {
        var gesture = _config.Taps.FirstOrDefault(t => t.Fingers == fingers);
        if (gesture == null) return;

        if (gesture.Action.Kind == "menu") ShowRadialMenu();
        else Execute(gesture.Action, restoreFocus: false);
    }

    private void OnSwiped(int fingers, string direction)
    {
        var gesture = _config.Swipes.FirstOrDefault(s =>
            s.Fingers == fingers && string.Equals(s.Direction, direction, StringComparison.OrdinalIgnoreCase));
        if (gesture == null) return;

        if (gesture.Action.Kind == "menu") ShowRadialMenu();
        else Execute(gesture.Action, restoreFocus: false);
    }

    private void ShowRadialMenu()
    {
        if (_radial != null && !_radial.IsDisposed) return; // zaten açık

        var items = _config.Menu;
        if (items.Count == 0)
        {
            _tray.ShowBalloonTip(3000, AppName,
                "Menü boş. Saatin yanındaki simgeden Ayarlar'ı açıp parça ekleyebilirsin.", ToolTipIcon.Info);
            return;
        }

        _previousForeground = GetForegroundWindow();
        _menuAnchor = Cursor.Position;
        _radial = new RadialMenu(items, _menuAnchor, Theme.Load());
        _radial.ActionChosen += action => Execute(action, restoreFocus: true);
        _radial.FormClosed += (_, _) => _radial = null;
        _radial.Popup();
    }

    private void RebuildTrayMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Menüyü aç", null, (_, _) => ShowRadialMenu());

        menu.Items.Add("Ayarlar", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Menü dosyasını düzenle (menu.json)", null, (_, _) => OpenConfig());
        menu.Items.Add("Ayarları yeniden yükle", null, (_, _) => ReloadConfig());
        var startup = new ToolStripMenuItem("Windows ile başlat") { Checked = IsStartupEnabled() };
        startup.Click += (_, _) => SetStartup(!IsStartupEnabled());
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => ExitThread());

        var old = _tray.ContextMenuStrip;
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

    private void ShowSettings()
    {
        if (_settings != null && !_settings.IsDisposed)
        {
            _settings.Activate();
            return;
        }
        _settings = new SettingsForm(_config, Theme.Load(), () =>
        {
            RebuildTrayMenu();
            _tray.ShowBalloonTip(2000, AppName, "Menü kaydedildi.", ToolTipIcon.Info);
        });
        _settings.FormClosed += (_, _) => _settings = null;
        _settings.Show();
        Native.ForceForeground(_settings.Handle);
    }

    private void ShowMediaWidget()
    {
        _media?.Close();
        _media = new MediaWidget(_menuAnchor.IsEmpty ? Cursor.Position : _menuAnchor, Theme.Load());
        _media.FormClosed += (_, _) => _media = null;
        _media.Popup();
    }

    private async void Execute(ActionConfig action, bool restoreFocus)
    {
        if (action.Kind == "media")
        {
            ShowMediaWidget();
            return;
        }
        try
        {
            if (restoreFocus && action.Kind == "keys" && _previousForeground != IntPtr.Zero)
            {
                // Kısayol menüden önceki pencereye gitsin.
                SetForegroundWindow(_previousForeground);
                await Task.Delay(120);
            }
            ActionRunner.Run(action);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private AppConfig LoadConfigOrDefault()
    {
        try
        {
            return AppConfig.Load();
        }
        catch (Exception ex)
        {
            ShowError($"menu.json okunamadı, varsayılan menü kullanılıyor.\n\n{ex.Message}");
            return AppConfig.CreateDefault();
        }
    }

    private void SaveConfig()
    {
        try
        {
            _config.Save();
        }
        catch (Exception ex)
        {
            ShowError($"menu.json kaydedilemedi.\n\n{ex.Message}");
        }
    }

    private void ApplyTapSettings()
    {
        _taps.MaxTapMs = _config.MaxTapMs;
        _taps.MaxMove = _config.MaxMove;
        _taps.SwipeDistance = _config.SwipeDistance;
    }

    private void ReloadConfig()
    {
        _config = LoadConfigOrDefault();
        ApplyTapSettings();
        MigrateStartupEntry();
        RebuildTrayMenu();
        _tray.ShowBalloonTip(2000, AppName, "Ayarlar yeniden yüklendi.", ToolTipIcon.Info);
    }

    private void OpenConfig()
    {
        if (!File.Exists(AppConfig.FilePath)) SaveConfig();
        ActionRunner.Run(new ActionConfig { Type = "run", Target = "notepad.exe", Args = $"\"{AppConfig.FilePath}\"" });
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) != null;
    }

    private void SetStartup(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else key.DeleteValue(RunValue, throwOnMissingValue: false);
        RebuildTrayMenu();
    }

    // Eski addaki başlangıç kaydını yeni ada taşı.
    private static void MigrateStartupEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(LegacyRunValue) == null) return;
            key.DeleteValue(LegacyRunValue, throwOnMissingValue: false);
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        }
        catch
        {
            // Kayıt defterine erişilemezse başlangıç ayarı kullanıcıdan tekrar açılır.
        }
    }

    private static void ShowError(string message) =>
        MessageBox.Show(message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _radial?.Close();
        _media?.Close();
        _settings?.Close();
        _reader.Dispose();
        _window.DestroyHandle();
        base.ExitThreadCore();
    }

    // WM_INPUT mesajlarını alan görünmez pencere.
    private sealed class InputWindow : NativeWindow
    {
        private readonly TouchpadReader _reader;

        public InputWindow(TouchpadReader reader)
        {
            _reader = reader;
            CreateHandle(new CreateParams { Caption = "TrackPadForWindowsInput" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) _reader.HandleWmInput(m.LParam);
            base.WndProc(ref m);
        }
    }
}
