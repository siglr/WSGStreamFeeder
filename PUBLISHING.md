# WSG Stream Feeder releases

The first update-enabled release is 1.2.0. Source builds have automatic updates disabled. Production builds require explicit `-OfficialRelease` and packaging with the matching private signing key.

Public source: `https://github.com/siglr/WSGStreamFeeder`. Releases use tags such as `v1.2.0`. The source is licensed under PolyForm Noncommercial 1.0.0; preserve `LICENSE.md` and `NOTICE.md`.

The embedded public key is `src/Updates/ReleasePublicKey.cs`. Its newly created private counterpart is DPAPI-protected for this Windows user at `%LOCALAPPDATA%\WSG Stream Feeder\signing\release-key.dpapi`. It is never shipped, printed, or stored in the repository. Keep a protected, tested Windows profile/system backup: the encrypted file alone is not portable recovery. Do not regenerate this key after publishing; existing installations trust its public counterpart.

For a future release:

1. Increment `Version` in `Directory.Build.props`. Never silently replace a published version. Move the previous `dist/upload` folder to your release archive before packaging; the script refuses to overwrite an existing ZIP.
2. Run `./package-release.ps1 -SigningKey "$env:LOCALAPPDATA\WSG Stream Feeder\signing\release-key.dpapi"`. Tests run before publishing. The script builds the app and updater, writes and signs `app-files.json`, then packages and signs the release metadata. It performs no network upload.
3. Run `./tools/verify-release.ps1` to verify both production signatures and package contents. Copy `web/index.html`, `web/WSG-Stream-Feeder.html`, `web/tools.css`, `web/feeder.js`, `web/UPLOAD-INSTRUCTIONS.txt`, and `web/assets/` into `dist/upload`. Keep existing tools' files on the server.
4. Scan the final package and record its exact SHA-256. The release signatures do not replace Authenticode or antivirus testing.
5. Upload to `https://wesimglide.org/otherdata/tools/`: the ZIP first, assets/pages next, and **WSG-Stream-Feeder.version.json last**. That last file activates the release for installed clients.

Fixed release URLs:

- `https://wesimglide.org/otherdata/tools/WSG-Stream-Feeder.version.json`
- `https://wesimglide.org/otherdata/tools/WSG-Stream-Feeder.zip`
- `https://wesimglide.org/otherdata/tools/WSG-Stream-Feeder.html`

The new `index.html` replaces the old redirect with a two-tool directory. It continues linking to `Standalone-DLSS-Glass-Cockpit-Refresh.html`; leave that existing page, ZIP, manifest, and other assets in place. The Glass Cockpit Refresh screenshot is reused from its existing local development preview, captioned as its control window.

Normal releases include only the allowlisted application files and `app-files.json`. `settings.example.json` is a sample; actual `settings.json`, `Output`, theme preferences, logs, signing tools, keys, and source are excluded. A first launch without `settings.json` defaults to its own `Output` folder. To customize the path, copy the example to `settings.json` and edit it.

For installation on this development PC, close the old feeder and copy the signed files from `dist/release-<version>` into its existing application folder. Preserve `settings.json` and `Output`. Older v1.1.x copies need this one manual upgrade to gain auto-updates.

The adapted updater's PolyForm Noncommercial notice and license accompany every package. Microsoft binaries and branding retain their existing terms. Nothing in the package claims Microsoft endorsement or a Windows trusted-publisher certificate.

## GitHub release

Commit the source, documentation, website, and final scan report. Keep signing keys, research folders, generated binaries, and user settings out of Git. Push the commit and an annotated version tag to the public repository. Create a draft GitHub release from that exact tag, attach the verified application ZIP, signed version JSON, and a SHA-256 text file, then publish after checking the asset names and sizes. GitHub supplies source archives automatically; these do not include the Microsoft build dependencies.

Use the same ZIP and signed JSON for GitHub and WeSimGlide. Publishing on GitHub does not activate the website-hosted update channel: upload the ZIP and website files to WeSimGlide, then its version JSON last. The website's Source code section points to the public repository.
