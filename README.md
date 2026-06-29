# Monitor Information

Monitor Information is a lightweight open source Windows desktop application for
viewing information about connected monitors.

Current version: `0.1.0`.

## Features

- Shows active displays detected by Windows.
- Reads EDID from the Windows monitor registry path when available.
- Decodes basic EDID data: display name, manufacturer ID, product code, serial,
  manufacture date, physical size, gamma, EDID version, extension count, and
  checksum status.
- Shows current resolution, refresh rate, graphics adapter, and raw EDID hex.
- Supports English (US), Russian, and Spanish interface languages.
- Supports System, Light, and Dark themes.
- Includes opt-in online lookup through the official ENERGY STAR Certified
  Displays dataset.
- Keeps online lookup disabled by default.

## Requirements

- Windows x64.
- .NET 10 Desktop Runtime for Windows x64.

Download the runtime from Microsoft:

https://dotnet.microsoft.com/en-us/download/dotnet/10.0

The release build is framework-dependent. If the required .NET runtime is not
installed, `MonitorInformation.exe` uses the native .NET apphost check and shows
Microsoft's runtime installation link before the app starts.

## Download And Run

1. Download `MonitorInformation-0.1.0-win-x64.zip` from the GitHub Release.
2. Extract the archive.
3. Run `MonitorInformation.exe`.

The app stores portable settings and online cache next to the executable when
the folder is writable.

## Version 0.1.0

What's new:

- Initial release

Implemented:

- WPF GUI for Windows.
- Local display enumeration through Win32 display APIs.
- EDID registry fallback.
- Raw EDID viewer with copy action.
- Light and dark UI themes.
- English (US), Russian, and Spanish UI resources.
- Optional ENERGY STAR online specification lookup with a local 30-day cache.

Not implemented yet:

- EPREL provider.
- Manufacturer-specific online providers.
- Full UEFI PNP vendor catalog generator.
- DisplayID and CTA extension decoding beyond base EDID metadata.
- Signed installer or auto-update.

## Build From Source

Requirements:

- Windows.
- .NET 10 SDK with WindowsDesktop workload.

Build:

```powershell
dotnet build MonitorInformation.slnx -c Release
```

Publish a compact framework-dependent x64 build:

```powershell
dotnet publish src\MonitorInformation\MonitorInformation.csproj -c Release -r win-x64 --self-contained false -o dist\MonitorInformation-0.1.0-win-x64
```

Create the release archive:

```powershell
Compress-Archive -Path dist\MonitorInformation-0.1.0-win-x64\* -DestinationPath dist\MonitorInformation-0.1.0-win-x64.zip -Force
```

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## License

MIT License. See [LICENSE](LICENSE).
