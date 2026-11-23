using HidSharp;
using LibreHardwareMonitor.Hardware;

// Diagnostic dump: run with --probe, writes probe.txt next to the exe.
static class Probe
{
    public static void Run()
    {
        var o = new StringWriter();
        var pc = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
        pc.Open();
        foreach (var h in pc.Hardware)
        {
            h.Update();
            o.WriteLine($"[{h.HardwareType}] {h.Name}");
            foreach (var s in h.Sensors.Concat(h.SubHardware.SelectMany(x => { x.Update(); return x.Sensors; })))
                o.WriteLine($"   {s.SensorType,-12} {s.Name,-35} {s.Value}");
        }
        pc.Close();

        var tray = Native.FindWindow("Shell_TrayWnd", null);
        Native.GetWindowRect(tray, out var tb);
        var notify = Native.FindWindowEx(tray, 0, "TrayNotifyWnd", null);
        Native.GetWindowRect(notify, out var nr);
        o.WriteLine($"taskbar {tb.Left},{tb.Top},{tb.Right},{tb.Bottom}  notify {notify} {nr.Left},{nr.Top},{nr.Right},{nr.Bottom}");

        foreach (var d in DeviceList.Local.GetHidDevices().Where(d => d.VendorID is 0x046D or 0x372E))
        {
            string pages = "";
            try { pages = string.Join(" ", d.GetReportDescriptor().DeviceItems.SelectMany(i => i.Usages.GetAllValues()).Select(u => u.ToString("X8")).Distinct()); } catch { }
            o.WriteLine($"HID {d.VendorID:X4}:{d.ProductID:X4} in{d.GetMaxInputReportLength()} out{d.GetMaxOutputReportLength()} feat{d.GetMaxFeatureReportLength()} [{pages}] {d.DevicePath}");
        }

        var bats = new Batteries();
        bats.Poll();
        foreach (var b in bats.Current) o.WriteLine($"battery: {b.Name} | {b.Kind} | {b.Link} | {b.Percent}%");
        if (bats.Current.Count == 0) o.WriteLine("battery: none reported");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe.txt"), o.ToString());
    }
}
