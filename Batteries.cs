using System.Runtime.InteropServices;
using System.Text;
using HidSharp;
using HidSharp.Reports;

sealed record BatteryInfo(string Name, string Kind, string Link, int Percent);

// Collects battery levels from every source that can report one. Devices that can't report simply don't appear.
sealed class Batteries
{
    readonly LogitechBatteries logitech = new();
    int busy;
    public volatile IReadOnlyList<BatteryInfo> Current = [];

    public void Poll()
    {
        if (Interlocked.Exchange(ref busy, 1) == 1) return; // previous poll still running
        try
        {
            var all = new List<BatteryInfo>();
            foreach (var source in new Func<IEnumerable<BatteryInfo>>[] { logitech.Poll, BluetoothBatteries.Poll, HidBatteries.Poll })
                try { all.AddRange(source()); } catch { }
            Current = all;
        }
        finally { busy = 0; }
    }
}

// Logitech LIGHTSPEED / Unifying / Bolt receivers over HID++ 2.0: every paired mouse, keyboard, etc.
sealed class LogitechBatteries
{
    const byte SwId = 0x0A;
    sealed class Dev
    {
        public required HidStream Stream;
        public byte Index, Feat;
        public ushort FeatId;
        public string Name = "", Kind = "Device";
        public int? Last;
    }
    List<Dev> devs = [];
    int polls;

    public IEnumerable<BatteryInfo> Poll()
    {
        if (polls++ % 10 == 0) Rescan(); // pick up newly paired / switched-on devices every ~5 min
        var result = new List<BatteryInfo>();
        foreach (var d in devs)
        {
            try
            {
                var r = Call(d.Stream, d.Index, d.Feat, d.FeatId == 0x1004 ? 1 : 0); // UnifiedBattery.get_status / BatteryStatus.get_level
                if (r != null) d.Last = r[0];
            }
            catch { }
            // shortcut: a sleeping device keeps its last reading; a switched-off one drops out at the next rescan
            if (d.Last is int p) result.Add(new(d.Name, d.Kind, "2.4 GHz", p));
        }
        return result;
    }

    void Rescan()
    {
        var old = devs.ToDictionary(d => (d.Stream.Device.DevicePath, d.Index), d => d.Last);
        foreach (var s in devs.Select(d => d.Stream).Distinct()) s.Dispose();
        var found = new List<Dev>();
        foreach (var hid in DeviceList.Local.GetHidDevices(0x046D).Where(d => d.GetMaxOutputReportLength() == 20))
        {
            if (!hid.TryOpen(out var s)) continue;
            s.ReadTimeout = 1000; // a sleeping device takes several hundred ms to answer
            int before = found.Count;
            for (byte i = 1; i <= 6; i++)
            {
                var r = Call(s, i, 0, 0, 0x10, 0x04); // IRoot.getFeature(UNIFIED_BATTERY)
                if (r == null) continue;               // empty slot or device off
                ushort id = 0x1004;
                if (r[0] == 0) { r = Call(s, i, 0, 0, 0x10, 0x00); id = 0x1000; } // older BATTERY_STATUS
                if (r == null || r[0] == 0) continue;
                var (name, kind) = Describe(s, i);
                found.Add(new Dev { Stream = s, Index = i, Feat = r[0], FeatId = id, Name = name, Kind = kind,
                    Last = old.GetValueOrDefault((hid.DevicePath, i)) });
            }
            if (found.Count == before) s.Dispose();
        }
        devs = found;
    }

    // DEVICE_NAME (0x0005): marketing name + type, e.g. "PRO X 2" / Mouse
    static (string name, string kind) Describe(HidStream s, byte idx)
    {
        var f = Call(s, idx, 0, 0, 0x00, 0x05);
        if (f == null || f[0] == 0) return ("Logitech device", "Device");
        byte fi = f[0];
        string kind = Call(s, idx, fi, 2)?[0] switch { 0 or 2 => "Keyboard", 3 or 4 or 5 => "Mouse", 8 => "Headset", _ => "Device" };
        int len = Call(s, idx, fi, 0)?[0] ?? 0;
        var name = new StringBuilder();
        while (name.Length < len)
        {
            var c = Call(s, idx, fi, 1, (byte)name.Length);
            int added = 0;
            if (c != null) foreach (var b in c) { if (b == 0 || name.Length >= len) break; name.Append((char)b); added++; }
            if (added == 0) break;
        }
        return (name.Length > 0 ? name.ToString() : "Logitech " + kind.ToLowerInvariant(), kind);
    }

    static byte[]? Call(HidStream s, byte dev, byte featIdx, int func, params byte[] p)
    {
        var req = new byte[20];
        req[0] = 0x11; req[1] = dev; req[2] = featIdx; req[3] = (byte)(func << 4 | SwId);
        p.CopyTo(req, 4);
        s.Write(req);
        var buf = new byte[s.Device.GetMaxInputReportLength()];
        var until = Environment.TickCount64 + 1500;
        while (Environment.TickCount64 < until)
        {
            int n;
            try { n = s.Read(buf); } catch (TimeoutException) { return null; }
            var m = Match(buf.AsSpan(0, n), req);
            if (m.done) return m.payload;
        }
        return null;
    }

    // Pure response matcher: (done, payload). done + null payload = error reply to our request.
    public static (bool done, byte[]? payload) Match(ReadOnlySpan<byte> buf, byte[] req)
    {
        if (buf.Length < 7 || buf[1] != req[1]) return (false, null);
        if ((buf[2] == 0xFF || buf[2] == 0x8F) && buf[3] == req[2] && buf[4] == req[3]) return (true, null);
        if (buf[2] == req[2] && buf[3] == req[3]) return (true, buf[4..].ToArray());
        return (false, null);
    }
}

// Bluetooth devices: Windows already reads their battery (the % shown in Settings > Bluetooth & devices).
static class BluetoothBatteries
{
    [StructLayout(LayoutKind.Sequential)] struct DEVPROPKEY { public Guid Fmtid; public uint Pid; }
    static DEVPROPKEY battery = new() { Fmtid = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), Pid = 2 };
    static DEVPROPKEY friendlyName = new() { Fmtid = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Pid = 14 };

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_List_SizeW(out int len, string filter, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_Device_ID_ListW(string filter, char[] buf, int len, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Locate_DevNodeW(out int inst, string id, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] static extern int CM_Get_DevNode_PropertyW(int inst, ref DEVPROPKEY key, out int type, byte[]? buf, ref int size, int flags);

    const int FilterEnumerator = 0x1, FilterPresent = 0x100;

    public static IEnumerable<BatteryInfo> Poll()
    {
        var result = new Dictionary<string, BatteryInfo>(); // one entry per name: classic BT devices expose several nodes
        foreach (var enumerator in new[] { "BTHLE", "BTHENUM" })
        {
            if (CM_Get_Device_ID_List_SizeW(out int len, enumerator, FilterEnumerator | FilterPresent) != 0 || len <= 1) continue;
            var buf = new char[len];
            if (CM_Get_Device_ID_ListW(enumerator, buf, len, FilterEnumerator | FilterPresent) != 0) continue;
            foreach (var id in new string(buf).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                if (CM_Locate_DevNodeW(out int inst, id, 0) != 0) continue;
                var b = Prop(inst, ref battery);
                if (b is not { Length: > 0 }) continue;
                var nameBytes = Prop(inst, ref friendlyName);
                string name = nameBytes == null ? "Bluetooth device" : Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
                result[name] = new(name, KindFromName(name), "Bluetooth", Math.Min((int)b[0], 100));
            }
        }
        // shortcut: a paired but disconnected device can keep its last % here; add a connection check if that shows up
        return result.Values;
    }

    static byte[]? Prop(int inst, ref DEVPROPKEY key)
    {
        int size = 0;
        CM_Get_DevNode_PropertyW(inst, ref key, out _, null, ref size, 0);
        if (size == 0) return null;
        var buf = new byte[size];
        return CM_Get_DevNode_PropertyW(inst, ref key, out _, buf, ref size, 0) == 0 ? buf : null;
    }

    static string KindFromName(string n) =>
        n.Contains("key", StringComparison.OrdinalIgnoreCase) ? "Keyboard"
        : n.Contains("mouse", StringComparison.OrdinalIgnoreCase) || n.Contains("mx ", StringComparison.OrdinalIgnoreCase) ? "Mouse"
        : "Device";
}

// USB / 2.4 GHz devices that use the standard HID "Battery Strength" usage.
static class HidBatteries
{
    const uint BatteryStrength = 0x00060020;

    public static IEnumerable<BatteryInfo> Poll()
    {
        var result = new List<BatteryInfo>();
        foreach (var d in DeviceList.Local.GetHidDevices())
        {
            if (d.VendorID == 0x046D) continue; // Logitech is read over HID++ above
            ReportDescriptor rd;
            try { rd = d.GetReportDescriptor(); } catch { continue; }
            var reports = rd.Reports.Where(r => r.DataItems.Any(i => i.Usages.GetAllValues().Contains(BatteryStrength))).ToList();
            if (reports.Count == 0 || !d.TryOpen(out var s)) continue;
            int? pct = null;
            using (s)
            {
                s.ReadTimeout = 250;
                foreach (var r in reports)
                {
                    var buf = new byte[r.Length];
                    buf[0] = r.ReportID;
                    try
                    {
                        if (r.ReportType == ReportType.Feature) s.GetFeature(buf);
                        // shortcut: input reports arrive on the device's own schedule; we only catch one sent within 250 ms
                        else if (r.ReportType != ReportType.Input || s.Read(buf) < 1 || buf[0] != r.ReportID) continue;
                    }
                    catch { continue; }
                    pct = Parse(buf, r);
                    if (pct != null) break;
                }
            }
            if (pct is not int p) continue;
            var usages = rd.DeviceItems.SelectMany(i => i.Usages.GetAllValues()).ToHashSet();
            string kind = usages.Contains(0x00010006) ? "Keyboard" : usages.Contains(0x00010002) ? "Mouse" : "Device";
            string name;
            try { name = d.GetProductName(); } catch { name = kind; }
            result.Add(new(name, kind, "USB", p));
        }
        return result;
    }

    static int? Parse(byte[] buf, Report r)
    {
        int bit = 8; // after the report ID byte
        foreach (var di in r.DataItems)
        {
            if (di.Usages.GetAllValues().Contains(BatteryStrength))
            {
                long v = ReadBits(buf, bit, di.ElementBits);
                int min = di.LogicalMinimum, max = di.LogicalMaximum;
                return max > min ? (int)Math.Clamp((v - min) * 100 / (max - min), 0, 100) : null;
            }
            bit += di.TotalBits;
        }
        return null;
    }

    // little-endian bit field, as HID packs report fields
    public static long ReadBits(byte[] b, int start, int count)
    {
        long v = 0;
        for (int i = 0; i < count; i++)
        {
            int p = start + i;
            if (p / 8 < b.Length && (b[p / 8] >> (p % 8) & 1) != 0) v |= 1L << i;
        }
        return v;
    }
}
