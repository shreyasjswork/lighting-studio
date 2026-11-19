# Lighting Studio

A Windows audio-reactive lighting controller for the HP GK320, Lenovo Legion
keyboard lighting, and Razer Basilisk V3. The project splits the browser control
panel from the audio analysis, device packet encoding, and Windows audio bridge.

This public snapshot contains the original control panel and controller source.
The C# code targets Windows x64. Device behavior depends on the hardware and
firmware. The offline tests use simulated state and do not require a device.

## Inspect and test

```powershell
npm ci
npm test
```

The JavaScript test simulates the local controller and checks the control panel.
On Windows, compile the audio bridge and local control server with the included
build scripts. Compilation does not start either process.

```powershell
powershell -NoProfile -File .\Build-Bridge.ps1
powershell -NoProfile -File .\Build-ControlPanel.ps1
```

## What I built

- Audio spectrum and equalizer motion with separate response settings for each
  lighting style.
- Device-specific packet encoders and a Windows audio loopback bridge.
- A local control panel with saved styles, preview states, and offline tests.

The controller can write lighting state when run against compatible hardware.
Review the source and connect supported devices before launching the binaries.
