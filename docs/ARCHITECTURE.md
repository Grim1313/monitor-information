# Monitor Information - Architecture

Last reviewed: 2026-06-29.

## Goal

Build a lightweight open source Windows desktop application that identifies
connected monitors, decodes locally available hardware data, and optionally
matches the monitor against authoritative online specifications.

The application must remain useful without internet access. Online lookup is an
opt-in feature, disabled by default, and must never be required for the main
screen to work.

## Product Principles

- Fast startup: show locally decoded monitor data first, then enrich in the
  background only when the user enables online lookup.
- Low dependency count: prefer .NET and Win32 APIs already available on Windows;
  avoid browser engines, embedded databases, web UI frameworks, telemetry SDKs,
  and heavyweight UI libraries.
- Transparent confidence: every field must show whether it came from EDID,
  Windows display APIs, a local catalog, or an online provider.
- No silent internet access: the main screen contains a visible "Online specs"
  switch with a short status label.
- Multilingual UI from the start: ship English (US), Russian, and Spanish,
  with a resource-based localization system that allows adding new languages
  without changing application code.
- Theme support from the start: ship light and dark themes, auto-detect
  the Windows app theme, and allow manual switching.
- Portable first: ZIP builds should work without installation; installer can be
  added later for users who want Start menu integration and auto-update.

## Recommended Technology

Use C# on .NET 10 LTS with WPF.

Reasons:

- .NET 10 is the current LTS line as of 2026-06-29, supported until
  2028-11-14.
- WPF keeps the dependency surface smaller than WinUI 3 because it does not
  require the Windows App Runtime.
- WPF is mature, supports accessibility and high DPI, and can be styled into a
  modern Windows 11-like interface without third-party UI frameworks.
- Direct Win32 interop is straightforward for DisplayConfig and monitor APIs.

WinUI 3 / Windows App SDK 2.2.0 is current and modern, but it adds runtime
packaging concerns. It is a good later option only if the project needs App SDK
features that WPF cannot provide.

Target runtimes:

- primary: `win-x64`;
- secondary: `win-arm64`;
- no separate x86 build by default.

32-bit Windows is no longer a good default target for this type of app. The
monitor APIs do not require 64-bit, but a separate x86 build increases release
and test cost for a shrinking audience. Add x86 only if users request it.

## High-Level Modules

```text
src/
  MonitorInformation/        WPF UI, view models, app settings
  MonitorInformation.Core/       EDID, DisplayID, matching, data models
  MonitorInformation.Windows/    WMI, registry, DisplayConfig, DDC/CI adapters
  MonitorInformation.Catalogs/   local catalog readers and generated indexes
  MonitorInformation.Online/     optional online providers and cache
  MonitorInformation.Localization/
                                  language resources and localization loader
  MonitorInformation.Cli/        diagnostics and catalog update validation
tests/
  MonitorInformation.Tests/      parser, matcher, catalog, provider tests
data/
  pnp-vendors.csv                generated from UEFI PNP ID registry
  vendor-aliases.csv             project-maintained aliases
  monitor-models.csv             curated optional local model catalog
  panel-models.csv               curated optional panel catalog
docs/
```

The UI must depend on service interfaces, not directly on WMI, HTTP, or parsing
internals.

## Localization

Initial languages:

- `en-US` - English (United States), default and fallback language;
- `ru-RU` - Russian;
- `es-ES` - Spanish.

Use resource files loaded through a small localization service. The app must not
hard-code user-facing strings in views, view models, providers, or error
messages.

Recommended layout:

```text
resources/
  i18n/
    en-US.json
    ru-RU.json
    es-ES.json
```

Each language file contains stable string keys and localized values. Adding a
new language should mean adding one new file, for example `de-DE.json`, plus
optional screenshots/docs updates. No code changes should be required.

Runtime behavior:

- detect Windows UI culture on first launch;
- use the best matching supported language;
- fall back to `en-US` for missing languages or missing keys;
- allow manual language selection in settings;
- apply language changes without requiring a restart where practical;
- format dates, numbers, sizes, decimal separators, and time remaining through
  `CultureInfo`, not through string concatenation;
- keep technical identifiers, EDID fields, vendor IDs, model numbers, and raw
  hex data untranslated.

String quality rules:

- avoid embedding variables through manual concatenation;
- use named placeholders, for example `{modelName}` and `{sourceName}`;
- support plural forms for counts and time units;
- keep UI labels short enough for Russian and Spanish without clipping;
- add automated checks for missing keys, unused keys, placeholder mismatches,
  and invalid JSON.

Licensing note: translation files are part of the open source project. External
translation contributions must be accepted under the same project license.

## Local Data Collection

Use a layered reader. Each layer returns partial data plus source metadata.

1. Display path discovery:
   - call `QueryDisplayConfig` with active paths;
   - call `DisplayConfigGetDeviceInfo` for target/source friendly names,
     adapter names, preferred modes, and stable path identifiers.

2. EDID block retrieval:
   - preferred: `WmiMonitorDescriptorMethods.WmiGetMonitorRawEEdidV1Block`
     from `root\wmi`;
   - fallback: monitor registry EDID under
     `HKLM\SYSTEM\CurrentControlSet\Enum\DISPLAY`;
   - fallback for summary fields: `WmiMonitorID`.

3. Optional physical monitor layer:
   - use DDC/CI only when the user opens advanced diagnostics;
   - never poll DDC/CI on startup because some monitors/KVMs are slow or flaky.

The app should handle duplicate monitors, missing serial numbers, virtual
displays, docks, KVMs, MST hubs, and EDID overrides.

## EDID and DisplayID Parser

Core parser responsibilities:

- EDID header and checksum validation;
- manufacturer ID, product code, serial, manufacture week/year;
- descriptor blocks: display name, serial text, range limits;
- established, standard, detailed, CTA extension timings;
- physical size, gamma, color primaries, chromaticity;
- HDR/static metadata if present in CTA blocks;
- DisplayID product identification blocks, especially for newer displays using
  `CID` plus IEEE CID/OUI instead of a unique three-letter PNP vendor ID.

Parser output must preserve raw values and decoded values. If a value is
ambiguous or invalid, keep it visible as a warning instead of silently fixing it.

## Local Catalog Design

There are two different catalog types.

### Official Vendor IDs

The UEFI PNP ID registry is the authoritative source for existing three-letter
PNP vendor IDs used by EDID. The local app ships a generated compact CSV or
binary index derived from that registry.

Important current constraint: UEFI Forum says new three-letter PNP vendor IDs
are no longer issued after the end of 2024. Newer EDID records may use `CID`
combined with IEEE CID/OUI in DisplayID or CTA product information blocks. The
local catalog must support both old PNP IDs and the newer `CID` path.

### Model and Panel Catalogs

There does not appear to be a single complete, open, authoritative database for
all monitor models and internal LCD/OLED panels. The design must not pretend
otherwise.

Use local catalogs as best-effort enrichment:

- `monitor-models.csv`: curated mappings from EDID manufacturer/product/name to
  marketed model names;
- `panel-models.csv`: curated panel model data when a panel identifier is
  locally observable or confidently documented;
- `vendor-aliases.csv`: normalized brand and manufacturer aliases.

Every row needs:

- source URL;
- source type: manufacturer, regulator, community, manual, inferred;
- license/usage note;
- last reviewed date;
- confidence level.

Implementation detail: do not use SQLite initially. Load small catalog files
through generated indexes and `FrozenDictionary`/sorted arrays. If the catalog
grows large enough to justify a database, add it later behind the same catalog
interface.

## Optional Online Specification Lookup

Default state: disabled.

Main screen control:

- visible switch: `Online specs`;
- status text: `Off`, `Ready`, `Searching`, `Found`, `No trusted match`, or
  `Error`;
- link/button to settings for provider selection.

Online lookup should be provider-based:

```text
ISpecProvider
  Name
  IsEnabled
  PrivacyNotice
  SearchAsync(MonitorIdentity identity, CancellationToken ct)
```

Provider priority:

1. Manufacturer official product/support pages.
2. EPREL public API for EU energy-label data.
3. ENERGY STAR public datasets/API for certified displays.
4. Other sources only if their license and terms allow reuse.

The app should avoid scraping sites that forbid automated use. If a source has
no API and no clear permission, offer a browser link instead of importing data.

Online matching rules:

- require at least two strong identifiers when possible: brand + model, EDID
  name + product code, serial prefix, EAN/UPC, or regulatory model;
- never overwrite local EDID data with online specs;
- show conflicts explicitly;
- cache successful lookups locally with source URL and timestamp;
- allow clearing the cache.

## Privacy and Network Policy

- First launch does not contact the internet.
- Online specs require an explicit user switch.
- Send only the minimal matching query: manufacturer, model/name, product code,
  and optional region setting. Do not send serial number by default.
- Settings must include:
  - enable/disable online lookup;
  - region for regulatory sources;
  - cache retention;
  - clear online cache;
  - export diagnostic report with optional raw EDID.

## GUI Structure

Main window layout:

- left column: connected monitors list with short names and connection status;
- main panel:
  - large readable monitor name;
  - key facts: resolution, refresh rate, size, interface, HDR/VRR indicators;
  - `Online specs` switch at top right;
  - source badges for each value;
  - warnings section for invalid checksum, missing serial, duplicate EDID,
    virtual display, or uncertain match.

Tabs:

- Overview: beginner-friendly summary.
- Hardware: decoded EDID/DisplayID values.
- Modes: timing list, preferred mode, active mode.
- Specs: local/online enriched specifications.
- Raw: hex EDID blocks, copy/export actions.

Avoid card-heavy marketing UI. This is a utility app: dense, calm, readable,
keyboard-accessible, and fast.

All visible UI text must come from the localization layer. The language selector
belongs in settings, while first-run language selection should follow the
Windows UI language automatically.

## Theme System

Initial themes:

- Light: default bright UI with clear contrast and restrained accent color.
- Dark: low-glare dark UI intended for dark rooms and OLED displays.

Theme modes:

- System: default mode, follows the Windows app theme setting.
- Light: forces the light theme.
- Dark: forces the dark theme.

The main window should expose theme state through a compact theme button or menu
near settings. Settings must include the same choice as a persistent option.

Implementation guidance:

- use WPF merged resource dictionaries for theme tokens;
- define semantic brushes such as `WindowBackground`, `PanelBackground`,
  `TextPrimary`, `TextSecondary`, `BorderSubtle`, `Accent`, `Warning`, and
  `Success`;
- avoid hard-coded colors in views;
- update theme resources at runtime without requiring a restart;
- listen for Windows theme changes while in System mode;
- keep the dark theme low-glare, but use slightly
  lifted surfaces for panels so borders and focus states remain visible;
- avoid large saturated areas and bright pure-white text in dark mode;
- keep warning/error colors readable in both themes;
- verify keyboard focus, selection, disabled states, charts, badges, and raw EDID
  hex view in both themes.

The theme system is separate from localization. Language and theme can be changed
independently and must persist separately in app settings.

## Performance Targets

- cold start to first local monitor list: under 500 ms on a typical Windows 11
  desktop;
- no network work before user opt-in;
- no DDC/CI probing at startup;
- memory target after idle startup: under 120 MB for framework-dependent build;
- catalog load under 50 ms for normal catalog size;
- parsing must allocate minimally and work from `ReadOnlySpan<byte>` where
  practical.

## Packaging

Release artifacts:

- `MonitorInformation-win-x64-portable.zip`;
- `MonitorInformation-win-arm64-portable.zip`;
- optional MSIX or installer later.

Build modes:

- framework-dependent ZIP for smallest download when .NET Desktop Runtime is
  installed;
- self-contained ZIP for true portability.

Single-file publishing can be tested, but normal folder ZIPs are easier to
debug and avoid surprises with native interop.

## Testing Strategy

Unit tests:

- EDID base block parser;
- CTA extension parser;
- DisplayID identification parser;
- checksum and malformed data handling;
- vendor ID decoding including legacy PNP IDs and `CID` handling;
- model matching confidence rules.
- localization resource validation for all supported languages;
- placeholder and pluralization validation.
- theme resource validation for required semantic brushes in light and dark
  themes.

Integration tests:

- WMI reader contract tests with mocked WMI output;
- registry EDID fallback;
- provider matching with recorded HTTP fixtures.
- culture switching in the WPF shell without restarting where practical.
- theme switching and system theme change handling in the WPF shell.

Manual test matrix:

- single monitor;
- two identical monitors;
- laptop internal display plus external monitor;
- USB-C dock;
- MST hub;
- KVM;
- remote desktop/virtual display;
- monitor with invalid or overridden EDID.

## Initial Roadmap

1. Create solution layout and core domain models.
2. Add localization resource structure with `en-US`, `ru-RU`, and `es-ES`.
3. Add light/dark theme resources and system theme detection.
4. Implement EDID base parser with golden test fixtures.
5. Implement Windows reader using WMI and DisplayConfig.
6. Build WPF main window with local-only data.
7. Add UEFI PNP vendor catalog generator.
8. Add raw EDID export and diagnostics CLI.
9. Add optional online provider interface and disabled-by-default UI switch.
10. Implement EPREL and ENERGY STAR providers before any HTML scraping.
11. Add manufacturer provider adapters only where terms/API allow it.

## Current Source Notes

- Microsoft documents `WmiMonitorDescriptorMethods` as the WMI class for raw
  128-byte VESA E-EDID v1.x blocks.
- Microsoft documents `WmiGetMonitorRawEEdidV1Block` as returning raw EDID block
  content, and `WmiMonitorID` as exposing identifying EDID fields.
- Microsoft documents `QueryDisplayConfig` and `DisplayConfigGetDeviceInfo` for
  active display paths and display-friendly names.
- UEFI hosts the PNP ID registry and documents the post-2024 shift away from
  new three-letter PNP VIDs for new EDID identification.
- EPREL and ENERGY STAR provide official public product data, but they are not
  complete monitor specification databases.

Reference links:

- https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitordescriptormethods
- https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmigetmonitorraweedidv1block-wmimonitordescriptormethods
- https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmimonitorid
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-querydisplayconfig
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-displayconfiggetdeviceinfo
- https://uefi.org/PNP_ID_List
- https://uefi.org/PNP_ACPI_Registry
- https://eprel.ec.europa.eu/
- https://www.energystar.gov/productfinder/advanced
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- https://dotnet.microsoft.com/en-us/download/dotnet/10.0
- https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads
