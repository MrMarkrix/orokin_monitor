using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace OrokinMonitor
{
    public partial class MainWindow : Window
    {
        private readonly SensorService _sensors = new();
        private readonly VolumeControl _volume = new();
        private readonly WeatherService _weatherSvc = new();
        private readonly Config _cfg = Config.Load();

        private Theme _theme;
        private readonly DispatcherTimer _fast = new();   // sensors + clock + volume
        private readonly DispatcherTimer _slow = new();    // weather
        private Weather _weather = new();

        private Forms.NotifyIcon? _tray;
        private bool _reallyExit = false;

        public MainWindow()
        {
            InitializeComponent();
            _theme = Theme.ByKey(_cfg.Theme);

            Loaded += (_, _) =>
            {
                ApplyTheme();
                SetupTray();
                StartTimers();
                _ = RefreshWeatherAsync();
            };

            // Drag-to-move on the title bar.
            TitleBar.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };

            // Minimize hides to tray instead of taskbar.
            StateChanged += (_, _) =>
            {
                if (WindowState == WindowState.Minimized) HideToTray();
            };

            // The window's X (CloseBtn) hides to tray; only the tray "Exit"
            // or _reallyExit actually closes. Keeps it running in background.
            Closing += (s, e) =>
            {
                if (!_reallyExit)
                {
                    e.Cancel = true;
                    HideToTray();
                }
            };

            Closed += (_, _) =>
            {
                _cfg.Save();
                _tray?.Dispose();
                _sensors.Dispose();
                _volume.Dispose();
            };
        }

        private void SetupTray()
        {
            _tray = new Forms.NotifyIcon
            {
                Text = "Orokin Monitor",
                Visible = true,
            };

            // Load the bundled icon; fall back to a generic one if missing.
            try
            {
                string ico = System.IO.Path.Combine(AppContext.BaseDirectory, "orokin.ico");
                _tray.Icon = System.IO.File.Exists(ico)
                    ? new Drawing.Icon(ico)
                    : Drawing.SystemIcons.Application;
            }
            catch { _tray.Icon = Drawing.SystemIcons.Application; }

            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("Show", null, (_, _) => RestoreFromTray());
            menu.Items.Add("Reset power min/max", null, (_, _) => _sensors.ResetPowerEnvelope());
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, _) => { _reallyExit = true; Close(); });
            _tray.ContextMenuStrip = menu;

            _tray.DoubleClick += (_, _) => RestoreFromTray();
        }

        private bool _trayHintShown = false;

        private void HideToTray()
        {
            Hide();                 // remove from taskbar; timers keep running
            ShowInTaskbar = false;
            if (!_trayHintShown && _tray != null)
            {
                _trayHintShown = true;
                _tray.BalloonTipTitle = "Orokin Monitor";
                _tray.BalloonTipText = "Still running here. Double-click to reopen, right-click to exit.";
                _tray.ShowBalloonTip(2500);
            }
        }

        private void RestoreFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true; Topmost = false;   // bring to front
        }

        private void StartTimers()
        {
            _fast.Interval = TimeSpan.FromMilliseconds(Math.Max(250, _cfg.RefreshMs));
            _fast.Tick += (_, _) => Tick();
            _fast.Start();
            Tick(); // immediate first paint

            _slow.Interval = TimeSpan.FromMinutes(10);
            _slow.Tick += async (_, _) => await RefreshWeatherAsync();
            _slow.Start();
        }

        // ---------------- per-tick update ----------------

        private void Tick()
        {
            Snapshot s = _sensors.Read();

            // CPU
            CpuName.Text = s.CpuName;
            CpuTemp.Text = Temp(s.CpuTemp);
            CpuTemp.Foreground = HotIf(s.CpuTemp, _cfg.CpuTempWarn);
            CpuClock.Text = s.CpuClock is float cc ? $"{cc / 1000f:0.0} GHz" : "—";
            CpuLoad.Text = Pct(s.CpuLoad);
            SetBar(CpuBar, CpuBarBack, s.CpuLoad);
            CpuPower.Text = s.CpuPower is float cp ? $"{cp:0} W package" : "power —";

            // GPU
            GpuName.Text = s.GpuName;
            GpuTemp.Text = Temp(s.GpuTemp);
            GpuTemp.Foreground = HotIf(s.GpuTemp, _cfg.GpuTempWarn);
            GpuClock.Text = s.GpuClock is float gc ? $"{gc:0} MHz" : "—";
            GpuLoad.Text = Pct(s.GpuLoad);
            SetBar(GpuBar, GpuBarBack, s.GpuLoad);
            GpuExtra.Text = GpuExtraLine(s);

            // RAM
            RamLoad.Text = Pct(s.RamLoad);
            RamUsed.Text = s.RamUsedGb is float rg ? $"{rg:0.0} GB used" : "—";
            SetBar(RamBar, RamBarBack, s.RamLoad);
            RamTemp.Text = s.RamTemp is float rt ? $"temp {rt:0}°C" : "";

            // Power
            PowerNow.Text = s.TotalPower is float tp ? $"{tp:0} W" : "—";
            PowerIdle.Text = s.TotalPowerMin is float mn ? $"idle {mn:0} W" : "idle —";
            PowerLoad.Text = s.TotalPowerMax is float mx ? $"load {mx:0} W" : "load —";

            // Clock
            DateTime now = DateTime.Now;
            ClockText.Text = now.ToString("HH:mm");
            DateText.Text = now.ToString("ddd dd MMM");

            // Weather (refreshed on slow timer; just render cached)
            WeatherTemp.Text = _weather.TempC is float wt
                ? (_cfg.UseFahrenheit ? $"{wt * 9 / 5 + 32:0}°F" : $"{wt:0}°C")
                : "—";
            WeatherLoc.Text = _cfg.LocationName;
            WeatherDesc.Text = _weather.Description;

            // Volume
            if (_volume.Available)
            {
                int v = _volume.GetVolume();
                bool muted = _volume.GetMute();
                VolText.Text = muted ? "muted" : $"{v}%";
                SetBar(VolBar, VolBarBack, muted ? 0 : v);
            }
            else
            {
                VolText.Text = "—";
            }
        }

        private string Temp(float? c)
        {
            if (c is not float v) return "—";
            return _cfg.UseFahrenheit ? $"{v * 9 / 5 + 32:0}°F" : $"{v:0}°C";
        }

        private static string Pct(float? v) => v is float f ? $"{f:0}%" : "—";

        private Brush HotIf(float? value, int warn)
            => value is float v && v >= warn ? _theme.Hot : _theme.Text;

        private static string GpuExtraLine(Snapshot s)
        {
            var parts = new System.Collections.Generic.List<string>();
            parts.Add(s.GpuPower is float p ? $"{p:0} W" : "— W");
            parts.Add(s.GpuVramUsed is float m ? $"{m / 1024f:0.0} GB" : "— GB");
            if (s.GpuFan is float f)
                parts.Add(s.GpuFanIsPercent ? $"fan {f:0}%" : $"fan {f:0} rpm");
            if (s.GpuHotspot is float h)
                parts.Add($"hot {h:0}\u00B0");
            return string.Join(" \u00B7 ", parts);
        }

        private void SetBar(Border bar, Border back, float? pct)
        {
            double frac = Math.Clamp((pct ?? 0) / 100.0, 0, 1);
            double avail = back.ActualWidth;
            if (avail <= 0) avail = 100;
            bar.Width = avail * frac;
        }

        // ---------------- theme ----------------

        private void ApplyTheme()
        {
            Background = _theme.Window;
            TitleText.Foreground = _theme.Accent;

            foreach (var head in new[] { CpuHead, GpuHead })
                head.Foreground = _theme.Accent;

            // panels
            foreach (var p in new[] { CpuPanel, GpuPanel, RamPanel, PowerPanel,
                                       WeatherPanel, TimePanel, VolumePanel })
            {
                p.Background = _theme.Panel;
                p.BorderBrush = _theme.PanelEdge;
            }

            // bars
            foreach (var bk in new[] { CpuBarBack, GpuBarBack, RamBarBack, VolBarBack })
                bk.Background = _theme.BarBack;
            foreach (var b in new[] { CpuBar, GpuBar, RamBar, VolBar })
                b.Background = _theme.Accent;

            // primary readouts
            foreach (var t in new[] { CpuTemp, CpuClock, CpuLoad, CpuPower,
                                       GpuTemp, GpuClock, GpuLoad, GpuExtra,
                                       RamLoad, RamUsed, PowerNow, PowerIdle, PowerLoad,
                                       WeatherTemp, WeatherLoc, ClockText, DateText, VolText })
                t.Foreground = _theme.Text;

            // dim labels
            foreach (var t in new[] { CpuName, GpuName, RamTemp, WeatherDesc })
                t.Foreground = _theme.TextDim;
        }

        // ---------------- handlers ----------------

        private void ThemeBtn_Click(object sender, RoutedEventArgs e)
        {
            int i = Theme.All.FindIndex(t => t.Key == _theme.Key);
            _theme = Theme.All[(i + 1) % Theme.All.Count];
            _cfg.Theme = _theme.Key;
            _cfg.Save();
            ApplyTheme();
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e) => _sensors.ResetPowerEnvelope();
        private void MinBtn_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void VolDown_Click(object sender, RoutedEventArgs e) => _volume.Step(-5);
        private void VolUp_Click(object sender, RoutedEventArgs e) => _volume.Step(+5);
        private void VolMute_Click(object sender, RoutedEventArgs e) => _volume.ToggleMute();

        private async System.Threading.Tasks.Task RefreshWeatherAsync()
        {
            try { _weather = await _weatherSvc.GetAsync(_cfg.Latitude, _cfg.Longitude); }
            catch { /* keep last */ }
        }
    }
}
