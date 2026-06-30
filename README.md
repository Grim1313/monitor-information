# Monitor Information

Monitor Information is a lightweight open source Windows desktop application for
viewing information about connected monitors.

## Features

- Shows active displays detected by Windows.
- Reads EDID from the Windows monitor registry path when available.
- Decodes basic EDID data: display name, manufacturer ID, product code, serial,
  manufacture date, physical size, gamma, EDID version, extension count, and
  checksum status.
- Shows current resolution, refresh rate, graphics adapter, and raw EDID hex.
- Supports English (US), Russian, and Spanish interface languages.
- Supports System, Light, and Dark themes.
- Includes opt-in online lookup through ENERGY STAR and panel lookup sources.
- Includes opt-in Panelook lookup for internal laptop panels such as
  `ATNA56YX03-0`.
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

1. Download `MonitorInformation-0.2.2-win-x64.zip` from the GitHub Release.
2. Extract the archive.
3. Run `MonitorInformation.exe`.

The app stores portable settings and online cache next to the executable when
the folder is writable.

## Smart App Control

The application is currently unsigned. Windows 11 Smart App Control may block
unsigned apps downloaded from the Internet, especially on first releases with no
publisher reputation.

Do not disable Smart App Control only for this app unless you understand the
risk. A proper fix requires a trusted code-signing certificate for release
binaries.

## Localization

Interface strings are stored as JSON files in:

```text
src/MonitorInformation/resources/languages/
```

To edit an existing language, update the matching file, for example
`ru-RU.json`.

To add a language:

1. Copy `en-US.json` to a new culture file, for example `de-DE.json`.
2. Translate values, keeping keys unchanged.
3. Add the culture code to `SupportedCultures` in
   `src/MonitorInformation/Services/LocalizationService.cs`.
4. Add a localized `lang.<culture>` display name to each language file.

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
dotnet publish src\MonitorInformation\MonitorInformation.csproj -c Release -r win-x64 --self-contained false -o dist\MonitorInformation-0.2.2-win-x64
```

Create the release archive:

```powershell
Compress-Archive -Path dist\MonitorInformation-0.2.2-win-x64\* -DestinationPath dist\MonitorInformation-0.2.2-win-x64.zip -Force
```

## Architecture

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## License

MIT License. See [LICENSE](LICENSE).
