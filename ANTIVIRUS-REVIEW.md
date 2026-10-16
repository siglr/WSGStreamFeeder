# WSG Stream Feeder 1.3.8 — local antivirus review

Reviewed 2026-10-16 (America/Toronto) after packaging the shortened glider heading change. Microsoft Defender reported **no threats** for both the final release directory and ZIP. Both scans completed with exit code 0.

| Evidence | Result |
| --- | --- |
| Engine | Microsoft Defender 1.1.26090.9 |
| Signatures | 1.461.53.0, updated 2026-10-16 00:15 EDT |
| Protection | Antivirus and real-time protection enabled |
| ZIP size | 1,093,664 bytes |
| ZIP SHA-256 | `90749ff96415c701ccece73f06baf3b47720b943c43d05b77a5a772d99b6d5d8` |
| App and updater Authenticode status | NotSigned |
| Release authenticity | Both RSA signatures and all 15 managed file hashes verified; ZIP entries verified against inventory |
| Automated checks | 135 offline telemetry/output/identity/window-placement checks, 13 WinForms lifecycle checks and 39 updater checks passed |

Scanned `dist/release-1.3.8` and `dist/upload/WSG-Stream-Feeder.zip` with `MpCmdRun.exe -Scan -ScanType 3 -File <path> -DisableRemediation`. The custom scan reports detections without remediation; it does not turn off real-time protection. Raw console results are retained under `build/defender-release-1.3.8-scan.txt` and `build/defender-zip-1.3.8-scan.txt`. No antivirus settings or exclusions were changed. No sample was submitted to an external scanning website.

## Changes made to reduce avoidable suspicion

- Ship ordinary .NET application files, without packing, obfuscation or single-file runtime extraction.
- Use a descriptively named `WSG Stream Feeder.Updater.exe` with consistent product/company metadata.
- Use a compiled updater with no downloaded scripts, PowerShell execution, elevation, service installation or startup persistence.
- Restrict update downloads to two fixed HTTPS addresses; use a separate bounded HTTPS aircraft-reference lookup as described in SECURITY.md. Require product-specific signatures, ZIP/file hashes, safe filenames and size limits before replacement.
- Replace only the allowlisted application files. Preserve settings and OBS output. Support rollback and process-interruption recovery.

The feeder uses SimConnect to read telemetry. The reviewed application has no game-process injection, credential collection, keyboard recording, screen capture or flight-data upload. Its ordinary file writes, executable update download and helper launch can still overlap with behaviors that heuristic engines watch. The review found no need for those behaviors outside the described app functionality.

## What remains

This is evidence for this exact release and this Defender engine/signature set, not a verdict from all antivirus vendors or a prediction of runtime/cloud detections. Live MSFS plus OBS performance was not validated during this release verification. Offline tests cover telemetry formatting, selection migration, gauges, file behavior and update recovery; the prior v1.3.6 production-endpoint checks verified the unchanged metadata lookup.

The most useful next distribution improvement is a consistent, trusted **Authenticode publisher signature** on our EXEs and DLLs, applied before inventory signing and packaging. The update-signing key already implemented is a different mechanism; it does not establish a Windows publisher identity. A self-signed certificate would not solve public publisher trust.

Even a trusted code signature does not guarantee that a new binary avoids SmartScreen warnings: Microsoft documents file and publisher reputation as separate signals. See [Microsoft's SmartScreen guidance for application developers](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/smartscreen-reputation).

If an actual detection occurs, collect the exact vendor, detection name, version and SHA-256; investigate that release before using the vendor's false-positive review process. Do not disable protection or add blanket exclusions. There is no known detection in this local scan to submit or to reproduce at present.
