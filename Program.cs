using System.Diagnostics;
using System.Net.NetworkInformation;
using LibreHardwareMonitor.Hardware;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--probe")) { Probe.Run(); return; }
        if (args.Contains("--selftest")) { SelfTest(); return; }
        ApplicationConfiguration.Initialize();
        if (args.Contains("--uninstall")) { Installer.Uninstall(); return; }
        // any copy outside Program Files acts as the setup program (--run skips that for development)
        if (!Installer.IsInstalledCopy && !args.Contains("--run")) { Installer.RunSetup(); return; }
        using var single = new Mutex(true, @"Global\TaskbarStats", out bool first);
        if (!first) return;
        Application.Run(new StatsBar());
    }

    static void SelfTest()
    {
        Trace.Assert(Fmt.Speed(33587) == "32.8 KB/s");
        Trace.Assert(Fmt.Speed(4.3 * 1048576) == "4.3 MB/s");
        Trace.Assert(Fmt.Temp(null) == "—");
        byte[] req = [0x11, 1, 0, 0x0A, 0x10, 0x04];
        Trace.Assert(LogitechBatteries.Match([0x11, 1, 0, 0x0A, 6, 0, 0], req).payload![0] == 6);
        Trace.Assert(LogitechBatteries.Match([0x11, 1, 0xFF, 0, 0x0A, 5, 0], req) == (true, null));
        Trace.Assert(!LogitechBatteries.Match([0x11, 2, 0, 0x0A, 6, 0, 0], req).done);
        // HID field: 7-bit value 85 starting at bit 9 (spans two bytes)
        Trace.Assert(HidBatteries.ReadBits([0x00, 0xAA, 0x00], 9, 7) == 85);
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest.txt"), "ok");
    }
}

static class Fmt
{
    public static string Temp(float? v) => v is float f ? $"{f:0}°C" : "—";
    public static string Pct(float? v) => v is float f ? $"{f:0}%" : "—";
    public static string Watt(float? v) => v is float f ? $"{f:0} W" : "—";
    public static string Speed(double b) => b < 1048576 ? $"{b / 1024:0.0} KB/s" : $"{b / 1048576:0.0} MB/s";
}

sealed class Sensors
{
    readonly Computer pc = new() { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
    readonly IHardware? cpu, gpu, mem;
    public float? CpuTemp, CpuLoad, CpuPower, GpuTemp, GpuLoad, GpuPower, RamLoad, RamUsed, RamAvail;

    public Sensors()
    {
        pc.Open();
        cpu = pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        mem = pc.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory && !h.Name.Contains("Virtual"));
        // the discrete card has the most dedicated VRAM; this skips the Ryzen iGPU
        gpu = pc.Hardware.Where(h => h.HardwareType is HardwareType.GpuAmd or HardwareType.GpuNvidia or HardwareType.GpuIntel)
            .OrderByDescending(h => { h.Update(); return Find(h, SensorType.SmallData, "GPU Memory Total", "D3D Dedicated Memory Total") ?? 0; })
            .FirstOrDefault();
    }

    public void Update()
    {
        cpu?.Update(); gpu?.Update(); mem?.Update();
        // sensor names: AMD Ryzen first, then Intel; GPU names cover AMD, NVIDIA and Intel Arc/iGPU
        CpuTemp = Find(cpu, SensorType.Temperature, "Core (Tctl/Tdie)", "Core (Tctl)", "CPU Package", "Core (Tdie)", "Core Max", "Core Average");
        CpuLoad = Find(cpu, SensorType.Load, "CPU Total");
        CpuPower = Find(cpu, SensorType.Power, "Package", "CPU Package", "CPU Cores");
        GpuTemp = Find(gpu, SensorType.Temperature, "GPU Core", "GPU Package", "GPU Hot Spot");
        GpuLoad = Find(gpu, SensorType.Load, "GPU Core", "D3D 3D");
        GpuPower = Find(gpu, SensorType.Power, "GPU Package", "GPU Power", "GPU Board Power", "GPU Core");
        RamLoad = Find(mem, SensorType.Load, "Memory");
        RamUsed = Find(mem, SensorType.Data, "Memory Used");
        RamAvail = Find(mem, SensorType.Data, "Memory Available");
    }

    static float? Find(IHardware? h, SensorType t, params string[] names)
    {
        if (h == null) return null;
        // CPU temp/power read 0 (not null) when the driver isn't loaded, so 0 means "unavailable" for those
        bool zeroIsMissing = t is SensorType.Temperature or SensorType.Power;
        var all = h.Sensors.Concat(h.SubHardware.SelectMany(s => s.Sensors))
            .Where(s => s.SensorType == t && s.Value.HasValue && !(zeroIsMissing && s.Value == 0)).ToList();
        foreach (var n in names)
            if (all.FirstOrDefault(s => s.Name == n) is { } s) return s.Value;
        return null;
    }

    public void Close() => pc.Close();
}

sealed class Net
{
    long rx, tx, at;
    public double Down, Up;

    public void Update()
    {
        long r = 0, s = 0;
        // only adapters with a gateway = real internet links, so virtual adapters don't double count
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
            if (n.OperationalStatus == OperationalStatus.Up && n.GetIPProperties().GatewayAddresses.Count > 0)
            { var st = n.GetIPStatistics(); r += st.BytesReceived; s += st.BytesSent; }
        long now = Environment.TickCount64;
        if (at != 0 && now > at)
        {
            double dt = (now - at) / 1000.0;
            Down = Math.Max(0, (r - rx) / dt); Up = Math.Max(0, (s - tx) / dt);
        }
        rx = r; tx = s; at = now;
    }
}

// Per-user display choices, HKCU\Software\TaskbarStats (survives reinstalls; everything shown by default).
static class Settings
{
    const string Key = @"Software\TaskbarStats";
    public static bool Show(string section) => Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)?.GetValue("Show" + section) is not 0;
    public static void SetShow(string section, bool on)
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key);
        k.SetValue("Show" + section, on ? 1 : 0);
    }
}

sealed class StatsBar : Form
{
    readonly Sensors sensors = new();
    readonly Net net = new();
    readonly Batteries batteries = new();
    readonly System.Threading.Timer batteryTimer;
    readonly Font font, iconFont;
    readonly ToolTip tip = new();
    readonly NotifyIcon tray;
    readonly Graphics measure = Graphics.FromImage(new Bitmap(1, 1));
    readonly StringFormat typo = (StringFormat)StringFormat.GenericTypographic.Clone();
    Color fg = Color.White;
    bool dirty = true;
    Rectangle drawn;
    // text column = [top, bottom, widest top, widest bottom]; widest values keep the bar from jittering
    List<string[]> cols = [];
    // Segoe Fluent Icons code points: keyboard, mouse, headphones, battery
    static string Glyph(string kind) => ((char)(kind switch { "Keyboard" => 0xE765, "Mouse" => 0xE962, "Headset" => 0xE7F6, _ => 0xE83F })).ToString();

    static readonly (string key, string label)[] Sections =
    [
        ("Cpu", "CPU (temperature, usage)"), ("Gpu", "GPU (temperature, usage)"), ("Net", "Download / upload"),
        ("Ram", "RAM"), ("Power", "Power (CPU / GPU watts)"), ("Batteries", "Device batteries (mouse, keyboard, ...)"),
    ];

    public StatsBar()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        // same font and size as the Windows 11 taskbar clock: the "Small" optical cut of Segoe UI Variable, 12px at 100%
        // shortcut: sized for the DPI at startup; moving to a monitor with another scale keeps the old size until restart
        float scale = DeviceDpi / 96f;
        string family = FontFamily.Families.Any(f => f.Name == "Segoe UI Variable Small") ? "Segoe UI Variable Small" : "Segoe UI";
        font = new Font(family, 12f * scale, GraphicsUnit.Pixel);
        iconFont = new Font("Segoe Fluent Icons", 15f * scale, GraphicsUnit.Pixel);
        typo.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;

        var menu = new ContextMenuStrip();
        var startup = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Enabled = Installer.IsInstalledCopy };
        startup.Checked = Installer.IsInstalledCopy && Installer.StartWithWindows;
        startup.CheckedChanged += (_, _) =>
        {
            try { Installer.StartWithWindows = startup.Checked; }
            catch (Exception e) { MessageBox.Show(e.Message, "TaskbarStats"); }
        };
        var show = new ToolStripMenuItem("Show");
        foreach (var (key, label) in Sections)
        {
            var item = new ToolStripMenuItem(label) { CheckOnClick = true, Checked = Settings.Show(key) };
            item.CheckedChanged += (_, _) => { Settings.SetShow(key, item.Checked); Tick(); };
            show.DropDownItems.Add(item);
        }
        // keep the submenu open so several items can be toggled in a row
        show.DropDown.Closing += (_, e) => e.Cancel = e.CloseReason == ToolStripDropDownCloseReason.ItemClicked;
        menu.Items.Add(show);
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Close());
        // menu lives only on the tray icon (^ overflow); right-clicking the stats on the taskbar does nothing
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        tray = new NotifyIcon { Icon = Icon, Text = "TaskbarStats", ContextMenuStrip = menu, Visible = true };

        batteryTimer = new System.Threading.Timer(_ => batteries.Poll(), null, 0, 30_000);
        var t = new System.Windows.Forms.Timer { Interval = 1000 };
        t.Tick += (_, _) => Tick();
        t.Start();
        // fast position-only timer so the bar slides in with an auto-hide taskbar
        var follow = new System.Windows.Forms.Timer { Interval = 50 };
        follow.Tick += (_, _) => Place();
        follow.Start();
        Tick();
    }

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/ | 0x80000 /*LAYERED*/; return cp; }
    }

    void Tick()
    {
        sensors.Update();
        net.Update();
        var s = sensors;
        string ram = s.RamUsed is float u && s.RamAvail is float a ? $"{u:0.0} / {u + a:0.0} GB" : "—";
        cols = [];
        if (Settings.Show("Cpu")) cols.Add([$"CPU {Fmt.Temp(s.CpuTemp)}", $"CPU {Fmt.Pct(s.CpuLoad)}", "CPU 100°C", "CPU 100%"]);
        if (Settings.Show("Gpu")) cols.Add([$"GPU {Fmt.Temp(s.GpuTemp)}", $"GPU {Fmt.Pct(s.GpuLoad)}", "GPU 100°C", "GPU 100%"]);
        if (Settings.Show("Net")) cols.Add([$"↓ {Fmt.Speed(net.Down)}", $"↑ {Fmt.Speed(net.Up)}", "↓ 999.9 KB/s", "↑ 999.9 KB/s"]);
        if (Settings.Show("Ram")) cols.Add([$"RAM {Fmt.Pct(s.RamLoad)}", ram, "RAM 100%", "99.9 / 99.9 GB"]);
        if (Settings.Show("Power")) cols.Add([$"CPU {Fmt.Watt(s.CpuPower)}", $"GPU {Fmt.Watt(s.GpuPower)}", "CPU 999 W", "GPU 999 W"]);
        // one icon + % per device that reported a battery; devices that can't report never appear
        var bats = batteries.Current;
        if (Settings.Show("Batteries")) foreach (var b in bats) cols.Add([Glyph(b.Kind), $"{b.Percent}%"]);

        var tipText = bats.Count > 0 ? string.Join("\n", bats.Select(b => $"{b.Name} ({b.Link})   {b.Percent}%")) : "TaskbarStats";
        if (s.CpuTemp == null) tipText += "\nCPU temp / watts unavailable (PawnIO driver not loaded)";
        if (tip.GetToolTip(this) != tipText) tip.SetToolTip(this, tipText);

        // follow the taskbar theme: white text on a dark taskbar, black on a light one
        bool light = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")?.GetValue("SystemUsesLightTheme") is 1;
        fg = light ? Color.Black : Color.White;
        dirty = true;
        Place();
    }

    void Place()
    {
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        Native.GetWindowRect(tray, out var tb);
        // auto-hide taskbar parks itself almost fully below the screen; game/fullscreen = QUNS 2..4
        bool taskbarHidden = tb.Top >= Screen.FromHandle(tray).Bounds.Bottom - 4;
        bool fullscreen = Native.SHQueryUserNotificationState(out int q) == 0 && q is 2 or 3 or 4;
        if (taskbarHidden || fullscreen || cols.Count == 0) { if (Visible) Hide(); return; }
        if (!Visible) Show();

        var notify = Native.FindWindowEx(tray, 0, "TrayNotifyWnd", null);
        int right = notify != 0 && Native.GetWindowRect(notify, out var nr) && nr.Right > nr.Left ? nr.Left : tb.Right - 300;
        int h = tb.Bottom - tb.Top, w = DrawColumns(null, h);
        var r = new Rectangle(right - w - 8, tb.Top, w, h);
        if (dirty || r.Size != drawn.Size) Render(r);
        // re-asserted every tick because clicking the taskbar raises it above us
        Native.SetWindowPos(Handle, Native.HWND_TOPMOST, r.X, r.Y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    // Draws into a transparent bitmap and hands it to Windows with per-pixel alpha, so text stays smooth on any background.
    void Render(Rectangle r)
    {
        using var bmp = new Bitmap(r.Width, r.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; // grayscale, like the taskbar clock
            g.TextContrast = 0; // fuller strokes; side-by-side with the clock this matched its weight best
            DrawColumns(g, r.Height);
        }
        nint screen = Native.GetDC(0), mem = Native.CreateCompatibleDC(screen), hbmp = bmp.GetHbitmap(Color.FromArgb(0)), old = Native.SelectObject(mem, hbmp);
        try
        {
            var pos = new Native.POINT { X = r.X, Y = r.Y };
            var size = new Native.SIZE { W = r.Width, H = r.Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { SourceConstantAlpha = 255, AlphaFormat = 1 /*AC_SRC_ALPHA*/ };
            Native.UpdateLayeredWindow(Handle, screen, ref pos, ref size, mem, ref src, 0, ref blend, 2 /*ULW_ALPHA*/);
        }
        finally
        {
            Native.SelectObject(mem, old);
            Native.DeleteObject(hbmp);
            Native.DeleteDC(mem);
            Native.ReleaseDC(0, screen);
        }
        drawn = r;
        dirty = false;
    }

    int TextWidth(string s, Font f) => (int)Math.Ceiling(measure.MeasureString(s, f, PointF.Empty, typo).Width);

    // Measures (g == null) or draws all columns; returns total width.
    int DrawColumns(Graphics? g, int h)
    {
        const int gap = 18;
        int x = 4, lh = (int)Math.Ceiling(font.GetHeight());
        using var brush = new SolidBrush(fg);
        foreach (var c in cols)
        {
            if (c.Length == 2) // battery: icon + %
            {
                int iw = TextWidth(c[0], iconFont), tw = TextWidth("100%", font);
                if (g != null)
                {
                    g.DrawString(c[0], iconFont, brush, x, (h - iconFont.GetHeight()) / 2, typo);
                    g.DrawString(c[1], font, brush, x + iw + 6, (h - lh) / 2f, typo);
                }
                x += iw + 6 + tw + gap;
                continue;
            }
            int w = c.Max(t => TextWidth(t, font));
            if (g != null)
            {
                g.DrawString(c[0], font, brush, x, h / 2f - lh, typo);
                g.DrawString(c[1], font, brush, x, h / 2f, typo);
            }
            x += w + gap;
        }
        return Math.Max(1, x - gap + 4);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        batteryTimer.Dispose();
        tray.Dispose(); // otherwise a dead icon lingers in the tray until hovered
        sensors.Close();
        base.OnFormClosed(e);
    }
}

