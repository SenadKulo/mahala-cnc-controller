# The Mahala Workshop - CNC Controller

A free Windows program for CNC and laser machines built with an **Arduino and Grbl** (0.8, 0.9 and 1.1).
It is the program we use to run our own laser CNC at [The Mahala Workshop](https://mahalaworkshop.com), shared with
everyone who built their own machine.

**Download, screenshots and a step-by-step guide: <https://mahalaworkshop.com/Cnc>**

The program speaks English and Bosnian (one click switches the language).

## What it does

- **Grbl 0.8, 0.9 and 1.1**: recognises the version, explains errors and alarms, reads, changes and saves the settings to a file (`$$`).
- **Jobs**: loads G-code, shows the drawing with the finished part and the laser live, a frame to check the job fits
  on the material, and an estimate of the time left. Original ArtCAM files are converted for the laser.
- **From your phone, in your home network**: the same page on the phone, opened by scanning a QR code; a 6-digit
  code keeps others on the network out. No account, no internet.
- **Resume after a restart**: if the Arduino restarts in the middle of a job (power glitches, USB), the program
  restores the position and continues where the laser stopped, by itself if you allow it.
- **Scheduled break**: stops with the laser off at known intervals (e.g. while compressors that can restart the
  Arduino switch on), off until you turn it on.
- **Laser**: laser power `S` for Grbl 1.1, laser mode, switching the laser on and off.
- **Simulator**: a built-in simulated Grbl 0.8 and 1.1, to try everything without a machine.
- **Installer with automatic updates** (Velopack).

> **Lasers are dangerous.** Wear safety glasses for your laser's wavelength, keep a fire extinguisher within reach
> and work with fume extraction. The program's "Emergency stop" does not replace a physical emergency stop switch on
> the machine. The program comes without any warranty (see the licence).

## Building from source

Requirements: Windows 10/11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) (Visual Studio 2026 with
".NET desktop development" brings it), and the Microsoft Edge WebView2 Runtime (part of Windows 10/11).

```powershell
dotnet build CncController.slnx
dotnet test CncController.slnx
dotnet run --project src/CncController.WinForms
```

No machine yet? Choose the port **Simulator** or **Simulator 1.1** and click Connect.

The installer: `.\installer\build.ps1 -Version 1.2.3` (self-contained publish and `vpk pack`; `vpk` is a local tool of
this repo, restored by `dotnet tool restore`). The result is in `artifacts\releases`.

## Structure

- `src/GrblCore`: the serial connection and Grbl's protocol, G-code parsing, the toolpath, time estimate,
  resuming a job, and the simulated Grbl (`Simulation/`). No user interface and no texts for people.
- `src/CncController.App`: everything the program does with the machine, without a window (`MachineSession`): the
  connection, jobs, the remembered position, automatic resume, the scheduled break, the log, settings, texts in
  both languages (`Texts`), and the page in the local network (`LanServer`).
- `src/CncController.Page`: the machine page (HTML, JS, CSS), the same in the desktop window and on the phone.
- `src/CncController.WinForms`: the desktop window; it shows the page in WebView2.
- `src/CncController.Remote`: the messages between the page and the program.
- `tests/`: xUnit tests; most run the program against the simulator.

## Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by
[SignPath Foundation](https://signpath.org).

- The installer and the program are built from this repository by GitHub Actions
  ([release.yml](.github/workflows/release.yml)) when a version tag is pushed; only these builds are signed.
- Every signing request is approved by hand.
- Committers and reviewers: [Senad Kulo](https://github.com/SenadKulo)
- Approvers: [Senad Kulo](https://github.com/SenadKulo)

Releases up to and including 1.0.1 are not signed.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it, with one exception: it checks `mahalaworkshop.com` for a new version at
start and every 12 hours. That request contains the program's name, version and type of Windows; the server sees
the IP address, as with any web request. The check is described at the first start, where it can be turned off,
and later in `%AppData%\CncController\settings.json` (`"CheckForUpdates": false`).

- The page in the local network runs only when the user turns it on, and is reachable only inside that network.
- Uninstalling: Windows Settings → Apps → "The Mahala Workshop - CNC Controller" → Uninstall. The settings in
  `%AppData%\CncController` stay; delete that folder to remove them too.

## Licence

GPL-3.0: you may use, study, change and share the program; if you share a changed version, share its source under
the same licence. See `LICENSE`.

Grbl itself is a separate project: <https://github.com/gnea/grbl>.
