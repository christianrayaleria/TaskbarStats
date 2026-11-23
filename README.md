# TaskbarStats

A small Windows app that shows live system stats directly on the taskbar, next to the system tray:

```
CPU 64°C   GPU 44°C   ↓ 14.6 KB/s    RAM 69%          CPU 53 W   🖱 69%
CPU 9%     GPU 19%    ↑ 15.8 KB/s    21.5 / 31.1 GB   GPU 80 W
```

| Shown | Where it comes from |
|---|---|
| CPU temperature, usage, power (W) | LibreHardwareMonitor, through the PawnIO driver |
| GPU temperature, usage, power (W) | LibreHardwareMonitor (AMD, NVIDIA, Intel). The dedicated card is picked automatically over integrated graphics |
| Download / upload speed | Windows network counters, on adapters that have an internet gateway |
| RAM usage % and used / total GB | Windows memory counters |
| Device batteries % (mouse, keyboard, headset, ...) | One icon + % per device that reports a battery. Devices that can't report simply don't appear. Hover over the bar to see each device's name |

**Which devices can show a battery:**

| Device | How it's read |
|---|---|
| Logitech wireless mice, keyboards, headsets on a LIGHTSPEED / Unifying / Bolt receiver | Logitech's HID++ protocol (read-only). Shows the real name, e.g. "PRO X 2". Several devices on one receiver all appear |
| Bluetooth devices that show a battery % in Windows Settings | The battery level Windows already stores for the device |
| USB / 2.4 GHz devices that use the standard HID "Battery Strength" report | Read-only standard HID report |
| Most gaming / enthusiast 2.4 GHz keyboards and mice from other brands (e.g. HERO 68 HE) | **Not possible.** Each brand uses its own unpublished protocol |

Only the Logitech path has been tested on real hardware. The Bluetooth and standard-HID paths follow the Windows and USB specifications but haven't been tried with a device.

Behaviour:

- Updates once per second. Batteries are checked every 30 seconds, and newly connected Logitech devices are picked up within about 5 minutes.
- Follows an auto-hide taskbar: it slides in when the taskbar shows and hides when the taskbar hides.
- Hides itself during fullscreen games, videos and presentations.
- Right-clicking the stats on the taskbar does nothing on purpose. All options are on the **TaskbarStats tray icon** in the **^** hidden-icons area. Right-click it for:
  - **Show**: tick or untick each section (CPU, GPU, Download / upload, RAM, Power, Device batteries). The menu stays open so you can change several at once. Your choices are saved and kept across restarts and updates. If you untick everything, the bar hides.
  - **Start with Windows**
  - **Exit**

---

## What the installer does

Download `TaskbarStats-Setup.exe` from the **Releases** page. Running it asks for admin (UAC), then shows one dialog: **Install / Cancel**, with a **Start with Windows** checkbox.

When you click **Install**, it makes exactly these changes:

| # | Change | Location |
|---|---|---|
| 1 | Copies the app (one exe) | `C:\Program Files\TaskbarStats\TaskbarStats.exe` |
| 2 | Adds an entry to the Apps list, so it appears in **Settings → Apps → Installed apps** with an Uninstall button | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\TaskbarStats` |
| 3 | Adds a Start menu shortcut | `C:\ProgramData\Microsoft\Windows\Start Menu\Programs\TaskbarStats.lnk` |
| 4 | *(Only if "Start with Windows" is ticked)* Adds a scheduled task that starts the app at login with admin rights, so there's no UAC prompt every boot | Task Scheduler → `TaskbarStats` |
| 5 | *(Only if not already present)* Installs the **PawnIO** driver silently, from the official setup bundled inside the exe | `C:\Program Files\PawnIO`, and its own entry in Installed apps |

Nothing else is installed. There are no services of its own, no browser extensions, no background updater, and no telemetry.
Your **Show** choices are stored per user in `HKCU\Software\TaskbarStats`.

**Running the setup when TaskbarStats is already installed:**

- **Older version installed:** it updates without asking. It closes the running copy, replaces the exe, keeps your Start-with-Windows choice and starts the new version, then tells you which version it updated from and to.
- **Same or newer version installed:** it changes nothing and tells you that version is already installed.

## What uninstalling removes

**Settings → Apps → Installed apps → TaskbarStats → Uninstall** removes:

- The app and its `Program Files` folder
- The Apps-list entry
- The Start menu shortcut
- The startup task
- **PawnIO**, but only if this installer was the one that installed it. If PawnIO was already on the PC (for example from FanControl or LibreHardwareMonitor), it is left alone so those apps keep working.

---

## Why it's safe

**It doesn't use the internet.** The app never sends or downloads anything. The network speed column only reads Windows' own byte counters. The PawnIO setup is built into the exe, so installing it needs no download either.

**It only reads, never writes, hardware settings.** No fan curves, no overclocking, no voltage changes. It reads temperature, load and power sensors, nothing more.

**The battery checks are read-only.**
- For Logitech devices, the app sends only the HID++ "which features do you have?", "what is your name/type?" and "what is your battery level?" queries, the same ones Logitech G HUB uses. It never sends configuration commands.
- Bluetooth batteries are read from what Windows already stores. The devices aren't contacted.
- Other USB devices are only read if they declare the standard battery report, and only that report is requested.
- Brand-specific protocols (used by many gaming keyboards) are never touched.

**Why it needs admin and a driver.** Windows doesn't let normal programs read CPU temperature or CPU power. That data sits in CPU registers that only a kernel driver can reach. Every hardware monitor (HWiNFO, LibreHardwareMonitor, FanControl, Ryzen Master) needs a driver for this. TaskbarStats uses **PawnIO** because:

- It is **digitally signed** (publisher: namazso) and loads with Windows' driver signature enforcement and Secure Boot / HVCI enabled.
- It is the driver **LibreHardwareMonitor** and **FanControl** moved to in 2025. Their old driver, WinRing0, had a known security hole and is flagged by Microsoft Defender.
- It only runs small, signed modules for specific jobs, such as reading AMD/Intel CPU sensors, instead of giving programs raw access to memory and hardware.
- The bundled copy is the official **PawnIO 2.2.0** setup. It is byte-for-byte identical to the one in the Microsoft winget catalog:
  `SHA256 1F519A22E47187F70A1379A48CA604981C4FCF694F4E65B734AAA74A9FBA3032`

Honest caveat: any kernel driver runs with the highest trust on the PC. PawnIO is the most locked-down option available for this, but if you don't want any driver, uninstall PawnIO from Installed apps. TaskbarStats keeps working and shows **"—"** for CPU temperature and CPU watts; everything else still works.

**The source code is in this repository.** Everything the exe does is in the files listed below, and you can rebuild the exe yourself (see *Building*).

**It's light.** Measured on a Ryzen 7 7800X3D: about **0.2 % CPU** and about **30 MB RAM**.

---

## Things to know

- **SmartScreen warning:** the exe isn't code-signed (that needs a paid certificate), so the first run shows *"Windows protected your PC"*. Click **More info → Run anyway**. Compare the exe's hash with the one below if you want to be sure it's the file built here.
- **Size (about 74 MB):** the whole .NET runtime is inside, so nothing else needs to be installed.
- **UAC when starting it by hand:** opening it from the Start menu asks for admin every time, because CPU sensors need it. The startup task starts it without asking.
- **Supported:** Windows 10/11 x64 with AMD or Intel CPUs and AMD, NVIDIA or Intel GPUs. It has only been tested on AMD (Ryzen 7 7800X3D + RX 7800 XT); on other hardware, any sensor it can't find shows "—".
- **Not supported:** batteries of devices that only use a brand-specific protocol (such as the HERO 68 HE), ARM PCs, taskbars on a second monitor, taskbars at the top or side of the screen, and light taskbar themes (text edges look dark).

`TaskbarStats-Setup.exe` from the v1.2.0 release, SHA256:
`5CA283C6F6E4D402B345437374F891393F76212A2C25C3356C7A3E603920A5E0`

---

## Files

| File | Purpose |
|---|---|
| `Program.cs` | Entry point, sensor reading, network speed, and the taskbar bar with its tray icon |
| `Installer.cs` | Install, uninstall and the startup task |
| `Batteries.cs` | Read-only battery readers: Logitech HID++, Bluetooth (via Windows) and standard HID |
| `Native.cs` | The few Windows API calls used to find and sit next to the taskbar |
| `Probe.cs` | Diagnostic dump of all sensors (`TaskbarStats.exe --probe` → `probe.txt`) |
| `app.manifest` | Asks Windows for admin rights |
| `app.ico`, `make-icon.ps1` | The app icon and the script that draws it |
| `.gitignore` | Keeps build output and the PawnIO setup out of git |
| `TaskbarStats.csproj` | Build settings: one self-contained exe |

## Building

Requires the .NET 8 SDK. From this folder:

```powershell
dotnet publish -c Release -o publish
```

The first build downloads the official PawnIO 2.2.0 setup from its GitHub release and refuses to build if its SHA256 doesn't match the one in `TaskbarStats.csproj`. The PawnIO setup is not stored in this repository. The result is `publish\TaskbarStats.exe`; rename it to `TaskbarStats-Setup.exe` for a release. Raise `<Version>` in `TaskbarStats.csproj` for every release, because the installer uses it to decide whether to update. The exe is both the installer and the app: run it from anywhere and it installs. The copy in `Program Files` runs as the app.

Command-line switches:

- `--uninstall`: remove everything (this is what the Apps list runs)
- `--run`: run the app without installing it
- `--probe`: write all detected sensors to `probe.txt`
- `--selftest`: run the built-in checks and write `selftest.txt`
