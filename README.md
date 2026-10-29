# The Mahala Workshop - CNC Controller

A free Windows program for CNC and laser machines built with an **Arduino and Grbl** (0.8, 0.9 and 1.1).
It is the program we use to run our own laser CNC at [The Mahala Workshop](https://mahalaworkshop.com), shared with
everyone who built their own machine.

**Download, screenshots and a step-by-step guide: <https://mahalaworkshop.com/Cnc>**

The program speaks English and Bosnian (one click switches the language).

## What it does

- **Grbl 0.8, 0.9 and 1.1**: recognises the version, explains errors and alarms, reads and changes the settings (`$$`).
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

## Licence

GPL-3.0: you may use, study, change and share the program; if you share a changed version, share its source under
the same licence. See `LICENSE`.

Grbl itself is a separate project: <https://github.com/gnea/grbl>.
