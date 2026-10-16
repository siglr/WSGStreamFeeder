# Behavior and antivirus review

WSG Stream Feeder reads simulator telemetry through Microsoft's SimConnect API. It does not inject into MSFS, patch simulator files, capture keys or screens, read browser data, install services or drivers, add startup tasks, request administrator rights, change antivirus settings, or execute downloaded scripts.

## Update behavior

Official builds request two fixed HTTPS resources under `wesimglide.org/otherdata/tools`. They do not send flight telemetry, user identifiers, settings, or file contents. Requests have the app/version User-Agent and standard HTTP headers. TLS certificate validation uses Windows/.NET defaults; redirects, extra query parameters, and alternate endpoints are rejected. Responses have size and time limits.

Both release metadata and the file inventory require a product-specific RSA-3072/SHA-256 signature. ZIP bytes and individual managed files must match SHA-256 values. Paths are restricted to a fixed set of app-owned filenames. Linked filesystem paths, ZIP traversal, case-duplicate entries, extra files, excessive expansion, and attempts to manage user settings/outputs are rejected. The installed package must still match its signed inventory before updating.

The existing, locally verified `WSG Stream Feeder.Updater.exe` and its three .NET companion files are copied into `.app-update`. These are ordinary framework-dependent .NET files: no script-based installer, obfuscator, executable packer, single-file bundling, or runtime code extraction is used in the shipping app or updater. The helper waits for the parent PID and creation time, acquires the installation lock, applies verified changes using complete-file replacement, and reopens the feeder. A journal and backup copies support rollback after errors or crashes. Completed staging is cleaned on the next launch when the helper is no longer locked.

The updater does not bypass permissions or elevate. It refuses automatic updates when run as administrator. If the folder is not writable, the existing installation is preserved. While connected to MSFS, the UI defers restart until the user chooses to restart or reopens the app.

## Remaining reputation risks

A newly built unsigned EXE has little reputation. Downloading an executable update, writing executable files in an application directory, and launching a helper from a staging folder can resemble malware behavior even when legitimate. Descriptive binary names, transparent source, bounded behavior, signed update manifests, ordinary .NET packaging, and an honest download page reduce ambiguity, but cannot guarantee every vendor's verdict.

The app currently has **no Authenticode publisher certificate**. Its release signatures are for update authenticity only and are not Windows trusted-publisher signatures. The next meaningful distribution improvement is a consistent Authenticode publisher identity for both EXEs and DLLs. Sign binaries before inventory generation and ZIP creation; changing binaries afterward invalidates the release hashes. Avoid changing publisher/product identities unnecessarily.

If a vendor flags a release, retain the exact hash and detection name, investigate first, then use that vendor's false-positive review process if the file is confirmed legitimate. Do not recommend disabling protection, adding blanket exclusions, disguising behavior, or changing the bytes merely to evade a detection.

Local scan results belong to the exact release hash and scanning engine/signature version. A local clean result is not a universal guarantee and does not predict SmartScreen reputation. No release is uploaded to an external scanning service without an explicit request.

## Optional aircraft reference lookup

Version 1.3 additionally reads `https://wesimglide.org/api/GetGliderReference.php` in development and official builds. It sends only the simulator aircraft folder identity (or TITLE fallback), measured wingspan and inferred competition class, URL-encoded within bounded parameters. It does not send file paths, config contents, positions or pilot identities. This request uses no cookies, credentials or authentication headers, does not follow redirects, accepts at most 64 KiB, and limits header/body waits. Unknown/partial metadata and network failures leave live telemetry functional.

One successful result is retained for the current aircraft/variant; there is no successful-lookup polling during flight. The short in-memory reconnect cache honors the endpoint TTL (matched at most 300 seconds, unknown at most 60 seconds); HTTP errors retry independently after five minutes. Old aircraft metadata is cleared on identity changes and out-of-order responses are ignored. Reference data is never executed and never supplies an executable, script or arbitrary file path.

The sample preview is only graphical. Text exports always use actual received telemetry or N/A. Disabled known telemetry files are set to N/A rather than removed, and unrelated filenames are untouched. Settings are saved atomically under LocalAppData. The app still has no system-tray hiding behavior, and all windows use the normal Windows taskbar.
