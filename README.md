# PKiller

PKiller is a local-only Windows 11 app for monitoring apps in the current signed-in session and closing their process trees. It uses C# and WinUI 3, and installs as a per-user MSIX.

## Build and install

Requirements: Windows 11, .NET 8 SDK, and internet access for the first NuGet restore.

The supplied PKiller logo sources are in `src\PKiller.App\Assets`. Run `.\scripts\Generate-PKillerAssets.ps1` after changing them to regenerate the Windows icon and tile images.

Run `.\scripts\Build-PKiller.ps1` in PowerShell. The script signs the MSIX with a self-signed development certificate stored in the current user's certificate store and exports the public certificate next to the package. Keep that certificate for future package updates; increment the package version in `src\PKiller.App\Package.appxmanifest` before producing an update.

Install or update the package for the current Windows user with:

```powershell
$package = Get-ChildItem .\artifacts\AppPackages -Filter 'PKiller.App_*.msix' -Recurse | Select-Object -First 1
.\scripts\Install-PKiller.ps1 -PackagePath $package.FullName
```

The install script trusts the matching publisher certificate in the current user's `TrustedPeople` store, then installs or updates the MSIX. Windows may show its normal package-installation prompts.

## Features

- Lists processes in the current interactive Windows session, groups them into app rows, and expands rows to show their process trees.
- Shows each app group’s executable icon when available, with a consistent app-glyph fallback when it is not.
- Displays app-level CPU, working-set memory, and cumulative process I/O read/write totals. Process I/O is not disk-only accounting.
- Does not collect, display, or estimate network usage.
- CPU is normalized to total logical processor capacity and bounded to 0–100% for both process and app rows; one fully busy logical processor on an eight-processor machine is 12.5%.
- Search, sortable columns, default highest-memory sort, remembered sort and refresh preference, and one-second refresh with pause/resume.
- One-click graceful close for one app; confirmation for batch close; a 10-second grace period before force-stop is offered for apps that remain open.
- Force-stop always requires confirmation and warns about unsaved work. Each action revalidates process start time and name to avoid PID reuse.
- No automatic elevation. Inaccessible processes are marked unavailable; the user may explicitly choose to relaunch as administrator.

## Implementation notes

App groups are based on executable image paths. Processes with a visible top-level window are separated from a different parent app's process tree; other descendants stay grouped with their app. Processes whose image path or metrics are inaccessible are still represented when their process identity can be queried, but their row reports the access limitation. Windows core processes are excluded by name, and processes outside the current Windows session are not shown. Refresh keeps its one-second cadence while process enumeration, CPU sampling, grouping, filtering, and sorting run off the UI dispatcher; detailed metrics are collected only for processes eligible for display.

The app is a standalone window sized to 800 × 820 effective pixels. App and child-process names share a consistent alignment beneath the App / process header. Drag a header grip to reorder columns; the selected order is saved per user and restored on the next launch. Drag a subtle separator to resize a column. Headers expose sort and drag guidance on hover. App and child-process row context menus contain only “Open file location” and “Search online”. The app has no tray mode, background service, telemetry, cloud sync, startup management, file-cleanup features, or other file-modifying features.

Run the focused tests with `dotnet test tests\PKiller.Tests\PKiller.Tests.csproj`.
