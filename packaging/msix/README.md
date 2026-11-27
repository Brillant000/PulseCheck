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
   ./packaging/msix/build-msix.ps1 -Name "<Identity Name>" -Publisher "<CN=...>" -PublisherDisplayName "<display name>" -Version 1.4.0.0
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

## Known unknowns

- Store certification outcome: not guaranteed, especially for a tool that changes system settings.
- Packaged apps run with a virtualised registry and file system for writes to some locations. The fixes that
  change settings (registry under `HKCU`, power plan, display mode, Store app removal) have **not** been tested
  inside the package. Test the installed package before submitting.
