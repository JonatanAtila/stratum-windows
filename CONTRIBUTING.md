# Contribution Guide

## Development setup 🛠️

Stratum for Windows is built with .NET 10 and WinUI 3 (Windows App SDK).

* Windows 10 version 1809 (build 17763) or later / Windows 11
* [.NET 10 SDK](https://dotnet.microsoft.com/download) (or Visual Studio 2022 17.12+ with the .NET and WinUI workloads)
* No extra workloads needed: `SQLitePCLRaw`, WinUI and SQLCipher come from NuGet

```powershell
# restore + build (x64)
dotnet build Stratum.Windows/Stratum.Windows.csproj -c Debug -p:Platform=x64

# run the Core test suite
dotnet test Stratum.Test/Stratum.Test.csproj -c Debug

# publish a self-contained build (unpackaged exe)
dotnet publish Stratum.Windows/Stratum.Windows.csproj -c Release -r win-x64 --self-contained
```

Project layout:

* `Stratum.Core/` — shared core: OTP generation, backup format, converters, persistence contracts (also used by the Android app)
* `Stratum.Windows/` — the WinUI 3 app (views, dialogs, database, settings)
* `Stratum.Windows.Tray/` — system tray icon (isolated Win32 helper)
* `Stratum.Test/` — xUnit tests for the core
* `icons/` — brand icons shared with the Android app (`<name>.png`, optional `<name>_dark.png`)

## Icons 🎨

To add a brand icon, first check the criteria below, then place a square 128x128 transparent PNG in the `icons/` directory. Both the Android and the Windows app pick it up automatically (the Windows app also honours `_dark` variants).

### Icon criteria:

Not every service needs an icon. To prevent the app having hundreds of icons from obscure and rarely used platforms, we limit what icons can be added. If a service doesn't meet the criteria we encourage the use of custom icons from within the app.

- Platforms that use a 'Single Sign-On' should have the icon added for the sign-on account and not for the individual platforms. Eg: instead of a YouTube icon a Google icon would be needed.
- Web based platforms should be within a top global rank (see Similarweb top 200,000 as a reference).
- Mobile platforms should have a significant install base (100k+ downloads as a reference).
- If the service fits none of the above it will have to be reviewed individually — open an issue first.

### How to add an icon:

* Fork the repo
* Find a high-quality icon for the service (prefer flat, original brand artwork, no text, no unnecessary frames/backgrounds)
* Save it as a square 128x128 png with transparent background, filling as much space as possible
* Name it lowercase with spaces and special characters removed. Eg: My Service -> myservice
* Place the file in the `icons/` directory
* If the icon needs a dark theme variant, repeat the process and append `_dark` to the name
* Commit your changes and open a pull request

## Translations 🌐

The Windows app currently ships in Portuguese only — there is no Crowdin pipeline wired up for it (unlike the Android app). Contributions adding WinUI localization resources are welcome; please open an issue first to agree on the approach.

## Code / Features ⚙️

Before submitting any code, please open an issue to discuss if the feature is relevant.

### AI pull requests

Any pull requests containing code generated primarily by a LLM will be closed. This applies if the author's contribution can be summed up as merely providing a prompt to a LLM without any real understanding of what they're doing.
