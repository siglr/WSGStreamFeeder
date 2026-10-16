# WSG Stream Feeder

A small Windows x64 application that reads MSFS 2024 through Microsoft's SimConnect API and feeds four OBS text sources. No StreamFlight application, FSUIPC, flight plan, web service, account, or OBS plugin is required.

[Download the latest release](https://github.com/siglr/WSGStreamFeeder/releases/latest) · [WeSimGlide download page](https://wesimglide.org/otherdata/tools/WSG-Stream-Feeder.html) · [License](LICENSE.md)

![WSG Stream Feeder in Dark mode](web/assets/wsg-stream-feeder.png)

## License

Public, source-available software licensed under **PolyForm Noncommercial 1.0.0**, matching Standalone DLSS Glass Cockpit Refresh. You may use, modify, and share the original code for noncommercial purposes under its terms. Commercial use is not permitted. See [LICENSE.md](LICENSE.md) and [NOTICE.md](NOTICE.md). Microsoft components and supplied branding retain their separate terms.

## Run

Download and extract the complete release ZIP into a writable folder. Windows x64 and the .NET 8 Desktop Runtime (x64) are required.

Launch `WSG Stream Feeder.exe`. It automatically connects when MSFS is available, retries every five seconds, and requests telemetry once per second. Minimize it to the regular Windows taskbar and restore it from there. **Exit** or the window's close button stops the feeder and writes `N/A` to its four files. It continues feeding OBS while minimized.

The window uses WSG Client's colors and original app logo. Choose **Dark** or **Light** under **Appearance**; your selection is remembered in `%LOCALAPPDATA%\WSG Stream Feeder\theme.txt`. The **About** link opens [WeSimGlide.org](https://WeSimGlide.org/) in your default browser.

## Automatic updates

Official releases from v1.2.0 check for updates when opened. **Check for updates** runs another check. Downloads are verified against a product-specific RSA-3072 signature and SHA-256 file inventory before anything is replaced. An unavailable server leaves the feeder running normally.

If MSFS is connected, an update is staged and the button changes to **Restart to update**. Finish your stream before using it, or close and reopen the feeder later. If MSFS is disconnected, the verified update installs and reopens automatically. Your `settings.json`, four output files, custom output path, minimized state, and saved theme are preserved. The updater works in the feeder folder, never in the simulator installation.

Keep the complete release together, including `WSG Stream Feeder.Updater.exe`, its three companion files, and `app-files.json`. Updating modified release files is deliberately refused; extract a fresh official package if you have changed managed files. Recovery data in `.app-update` is retained after an interrupted update and processed on the next launch. Do not manually remove it during recovery. Details are in `PUBLISHING.md` and `SECURITY.md` in the source.

Output defaults to an `Output` subfolder beside the EXE. To customize it, copy the included `settings.example.json` to `settings.json` and change `OutputDirectory`. Development builds include `settings.json` directly.

In OBS, edit each of your four text sources, keep **Read from file** enabled, and browse to its matching file in this new folder. The filenames are unchanged. StreamFlight can be uninstalled; the feeder and its output are independent of the StreamFlight folder.

To use another output location, edit `OutputDirectory` in `settings.json` and update the four OBS text sources to that folder. Alternatively launch with `--output "C:\some\folder"`. Relative paths are resolved beside the EXE. Launch with `--minimized` to start minimized on the taskbar. A second feeder using the same output path is rejected. If you explicitly choose the old `C:\MSFS2024\Apps and Mods\StreamFlight\Output` location, the feeder waits while StreamFlight is running to prevent conflicting writes.

## File contract

| File | Simulator value | Example |
| --- | --- | --- |
| `altitude.txt` | Indicated altitude in feet; geometric altitude fallback | `12,346` |
| `heading_true.txt` | True heading in degrees | `274°` |
| `speed.txt` | Ground speed in knots (not indicated airspeed) | `124` |
| `vspeed.txt` | Vertical speed in feet per minute | `▴ 1,235`, `▾ 1,235`, or `0` |

Files use UTF-8 without BOM, without a trailing newline, and whole-number rounding with midpoint ties to even. Altitude and vertical speed use commas for thousands. Positive vertical speed has U+25B4 plus one space; negative has U+25BE plus one space and an unsigned magnitude. Heading is not zero-padded; a rounded near-north heading can be `360°`. Files say `N/A` before telemetry and after disconnect or normal exit. Values remain unchanged between simulator samples, including a paused simulator that still responds to the connection heartbeat.

Only changed values are replaced. Completed temporary files are moved over the destination, preventing partial values; if OBS or another reader blocks replacement, the window shows an error and the next tick retries. No unrelated output files are deleted or modified. Diagnostic logs live at `%LOCALAPPDATA%\WSG Stream Feeder\feeder.log`, outside the OBS folder.

## Compatibility finding

Inspection of the locally installed StreamFlight DLL found that it **deletes `heading_true.txt` during output-folder setup**, and its update loop writes only `heading_magnetic.txt`. This explains the missing heading in OBS. This feeder deliberately repairs that behavior: the requested `heading_true.txt` is maintained using the true-heading SimVar and the same whole-degree/degree-symbol style. It does not reproduce the deletion bug.

The other three formats and their SimVars were verified against the installed application. Invalid/infinite numbers are handled defensively. StreamFlight's obsolete heading-file behavior cannot be claimed as byte-for-byte parity for live true heading; that output is an intentional fix.

## Build and dependencies

Source is in `src`. On Windows, obtain the two SimConnect dependencies described in [lib/README.md](lib/README.md), then run `./build.ps1` with a .NET SDK supporting .NET 8. The published program needs the .NET 8 **Desktop Runtime (x64)**. The SDK is only needed for building.

Ordinary source builds disable network updates. Run `package-release.ps1 -SigningKey <protected-key-file>` to explicitly build an official signed package; see `PUBLISHING.md`. Never publish an ordinary development build as a release.

The original logo and icon in `assets` were copied from the installed WSG Client's `Web\assets` folder. They are embedded in the executable, so WSG Client does not need to be installed for them to appear. The theme palettes in `src\Appearance.cs` match WSG Client's `Web\styles.css`.

Keep `SimConnect.dll` and `Microsoft.FlightSimulator.SimConnect.dll` beside the EXE. The local build uses copies of Microsoft's SimConnect components from the existing StreamFlight installation, saved under `lib`; it does not load or ship `StreamFlight.dll`. These Microsoft binaries retain their original licensing. For a fresh checkout, obtain the corresponding x64 components from the Microsoft Flight Simulator SDK and place them in `lib`. Do not assume Microsoft binaries can be relicensed as this application's source.

`build.ps1` runs the executable compatibility and updater checks and publishes to `dist\WSG Stream Feeder`. Tests cover rounding, triangles, units/format selections, invalid input, locale independence, exact bytes, file locks, retry, deletion recovery, disconnect output, signature verification, unsafe archives, rollback, process interruptions, and an actual updater-process handoff. Updater fixtures use ephemeral test keys and temporary folders, never a real MSFS installation. To smoke-test the actual executable without changing OBS files:

```powershell
& ".\WSG Stream Feeder.exe" --output "C:\Temp\WSG Stream Feeder-smoke" --diagnostic-seconds 10 --minimized
```

That mode runs the normal application for ten seconds and exits; it never generates fictional flight telemetry. The log reports startup and simulator connection status. A live flight remains necessary to validate reception against MSFS on this PC.

Microsoft references: [SimConnect Open](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/general/simconnect_open/), [System-state heartbeat](https://docs.flightsimulator.com/msfs2024/retail/programming-apis/simconnect/api-reference/general/simconnect_requestsystemstate/), [Aircraft SimVars](https://docs.flightsimulator.com/msfs2024/html/6_Programming_APIs/SimVars/Aircraft_SimVars/Aircraft_Misc_Variables.htm).
