<p align="center"><img src="app.ico" width="64" alt=""></p>

# TaskbarStats

See your PC's temperatures, usage and battery levels **right on the Windows taskbar**, without opening any app.

```
CPU 64°C   GPU 44°C   ↓ 14.6 KB/s    RAM 69%          CPU 53 W   🖱 69%
CPU 9%     GPU 19%    ↑ 15.8 KB/s    21.5 / 31.1 GB   GPU 80 W
```

| It shows | Meaning |
|---|---|
| CPU 64°C / 9% | Processor temperature and how busy it is |
| GPU 44°C / 19% | Graphics card temperature and how busy it is |
| ↓ ↑ | Download and upload speed |
| RAM | Memory in use |
| CPU / GPU W | How much power the processor and graphics card are drawing |
| 🖱 ⌨ 🎧 | Battery level of wireless devices that report it |

---

## Install

1. Go to **[Releases](../../releases)** and download **`TaskbarStats-Setup.exe`**.
2. Run it. If Windows shows *"Windows protected your PC"*, click **More info → Run anyway**. This appears because the app isn't signed with a paid certificate.
3. Click **Yes** on the admin prompt, then **Install**.

That's it. The stats appear on your taskbar, and the app starts automatically with Windows (you can untick this during install).

**Updating:** download the newer `TaskbarStats-Setup.exe` and run it. It replaces the old version and restarts by itself. If you already have that version, it just tells you so.

**Uninstalling:** **Settings → Apps → Installed apps → TaskbarStats → Uninstall**. Everything it added is removed.

---

## Using it

Everything is in the **TaskbarStats icon** in the hidden-icons area (the **^** arrow next to the clock). Right-click it:

- **Show**: pick which stats appear (CPU, GPU, download/upload, RAM, power, device batteries).
- **Start with Windows**: on or off.
- **Exit**

Other things worth knowing:

- Hover over the stats to see the full name of each battery device.
- Right-clicking the stats themselves does nothing, on purpose.
- If your taskbar auto-hides, the stats hide and appear with it.
- The stats hide during fullscreen games and videos.

---

## Is it safe?

- **No internet.** The app never sends or downloads anything.
- **Read-only.** It only *reads* temperatures, usage and battery levels. It never changes fan speeds, clock speeds, voltages or device settings.
- **Light.** It uses about 0.2% CPU and 30 MB of memory.
- **Open source.** Everything it does is in this repository, so you can check it yourself.

### Why does it ask for admin?

Windows doesn't let normal apps read **CPU temperature and CPU power**. Every monitoring tool (HWiNFO, Ryzen Master, FanControl and others) needs a small helper **driver** for this.

TaskbarStats uses **PawnIO**, the driver that LibreHardwareMonitor and FanControl use:
- It is digitally signed.
- It works with Windows security features (Secure Boot, Memory Integrity) turned on.
- It replaced an older driver that Microsoft Defender flags as unsafe.

The installer adds PawnIO only if it isn't already on your PC. Uninstalling TaskbarStats removes PawnIO too, unless another app had installed it first.

Don't want a driver at all? Uninstall **PawnIO** from Installed apps. TaskbarStats keeps working; CPU temperature and CPU power just show **"—"**.

---

## What gets installed

| What | Where |
|---|---|
| The app | `C:\Program Files\TaskbarStats` |
| An entry in Installed apps (with Uninstall) | Windows Settings |
| A Start menu shortcut | Start menu |
| Start with Windows *(if ticked)* | Task Scheduler, task named `TaskbarStats` |
| PawnIO driver *(only if missing)* | `C:\Program Files\PawnIO` |
| Your **Show** choices | `HKEY_CURRENT_USER\Software\TaskbarStats` |

Nothing else: no background services of its own, no updater, no tracking.

---

## Which device batteries can it show?

A device only appears if it can report its battery. Devices that can't report are simply left out.

| Device | Works? |
|---|---|
| Logitech wireless mice, keyboards and headsets (LIGHTSPEED, Unifying, Bolt receivers) | ✅ Yes, tested |
| Bluetooth devices that show a battery % in Windows Settings | ✅ Should work (not yet tested on a real device) |
| USB / 2.4 GHz devices using the standard USB battery report | ✅ Should work (not yet tested on a real device) |
| Most gaming keyboards and mice from other brands | ❌ No. Each brand keeps its battery protocol private |

---

## Requirements and limits

- Windows 10 or 11, 64-bit (not ARM).
- AMD or Intel processors; AMD, NVIDIA or Intel graphics. Tested on AMD Ryzen 7 7800X3D + Radeon RX 7800 XT. On other hardware, any reading it can't find shows "—".
- Shows on the main monitor's taskbar, at the bottom of the screen.
- Made for a dark taskbar. On a light taskbar the text edges look rough.
- Opening it from the Start menu asks for admin each time. Starting with Windows doesn't.

---

## Credits

- Made by Christian Ray Aleria.
- Sensor readings: [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
- CPU sensor access: [PawnIO](https://pawnio.eu/) driver
