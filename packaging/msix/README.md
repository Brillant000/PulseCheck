# Microsoft Store package (MSIX)

Why: Windows 11 **Smart App Control** blocks programs that are not signed or not known to Microsoft, with no
"run anyway" option. An app installed from the Microsoft Store is signed by Microsoft, so it is not blocked.

Status: the package builds in CI (`.github/workflows/msix.yml`) with **test identity values**. It is not on the
Store yet. Nothing here is published until you submit it.

## What you have to do yourself

These steps need your identity and a payment, so only you can do them.

1. Create a developer account at <https://partner.microsoft.com/dashboard/registration/developer> (individual
   account; one-time registration fee, around 19 USD at the time of writing: check the page). Identity
   verification is required.
2. In Partner Center: **Apps and games > New product > MSIX or PWA app**, reserve the name **PulseCheck**
   (it may be taken: pick another, e.g. "PulseCheck PC Health").
3. Open **Product management > Product identity** and note three values:
   `Package/Identity/Name`, `Package/Identity/Publisher` (starts with `CN=`) and
   `Package/Properties/PublisherDisplayName`.
4. Build the package with those values (needs the Windows SDK and the .NET 8 SDK):

   ```powershell
   ./packaging/msix/build-msix.ps1 -Name "<Identity Name>" -Publisher "<CN=...>" -PublisherDisplayName "<display name>"
   ```

   The result is `dist-msix/PulseCheck.msix`. Do not sign it: the Store signs it.
   Version must be `major.minor.build.0`; the Store requires the last number to be `0`, and each submission needs a higher version.
5. In the submission: upload the `.msix`, choose pricing **Free**, fill the age rating questionnaire, add the
   description and screenshots (`docs/shots/`), and set the privacy policy URL to
   `https://brillant000.github.io/PulseCheck/privacy.html`.
6. Certification takes from a few hours to a few days. The app uses the `runFullTrust` capability (it reads
   system information and can change settings after confirmation), so reviewers may ask why: say it is a desktop
   utility that reads hardware/network state and applies fixes only after the user confirms.

## Test the package on your own PC (optional)

```powershell
./packaging/msix/build-msix.ps1 -SignTest
# then, in an administrator PowerShell, trust the throw-away certificate and install:
Import-Certificate -FilePath dist-msix/test.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
Add-AppxPackage dist-msix/PulseCheck.msix
```

Remove the test certificate afterwards from `Cert:\LocalMachine\TrustedPeople`.

## Findings from testing the installed package (read this before submitting)

Measured on Windows 11 builds 26300 (a real PC) and 26100 (CI), with `probe-virtualization.ps1`:

- **WRITE:** a registry change made from inside the package to an existing HKCU value never reaches Windows. This
  holds with no declaration, with `desktop6:RegistryWriteVirtualization=disabled` + `unvirtualizedResources`, and with
  the Windows 11 `virtualization:ExcludedKeys` syntax, and through `reg.exe`, WMI and `HKEY_USERS\<SID>`.
  So the app (`PackageInfo.cs`) shows "Open settings" instead of Fix for the registry-based fixes when it runs from
  the package, and the manifest requests only `runFullTrust`.
- **READ:** inside the package, HKCU does not reflect changes Windows makes after the package's first run (a key
  created outside after install is invisible; a value changed outside keeps its old value). The "Open settings,
  change it, rescan" loop can therefore show stale results for HKCU-based findings (game capture, Game Mode, proxy,
  startup entries). Not fixed. This must be resolved or accepted explicitly before submitting.
- Files under `%LOCALAPPDATA%` written by the package land in `%LOCALAPPDATA%\Packages\<PFN>\LocalCache\Local`
  (the app shows that real path for its logs).
- The Store route "EXE or MSI app" avoids all of this (no package) but requires an installer signed by a trusted CA.

## Why the manifest once turned off write virtualization (historical)

By default a packaged app writes to a private copy of the registry and file system, so a fix such as "Game
capture off" would change nothing in Windows. The manifest sets `RegistryWriteVirtualization` and
`FileSystemWriteVirtualization` to `disabled`, which needs the restricted capability `unvirtualizedResources`
(like `runFullTrust`, it is reviewed at certification). Justification to give if asked: "PulseCheck is a
system-health utility. Its optional fixes change the user's own Windows settings (HKCU) after an explicit
confirmation, and must be visible to Windows." Minimum supported Windows is 10 version 2004 (build 19041).

## Known unknowns

- Store certification outcome: not guaranteed, especially for a tool that changes system settings.
- Packaged apps run with a virtualised registry and file system for writes to some locations. The fixes that
  change settings (registry under `HKCU`, power plan, display mode, Store app removal) have **not** been tested
  inside the package. Test the installed package before submitting.
