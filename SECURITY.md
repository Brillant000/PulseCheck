# Security policy

PulseCheck reads information about the computer it runs on. We take that seriously.

## What the app does and does not do

- **Scanning is read-only.** A scan never changes anything. Changes happen only when you press **Fix** and confirm a dialog that lists exactly what will change. Every change is logged locally (`%LOCALAPPDATA%\PulseCheck\changes.log`) and most can be undone. PulseCheck never installs software and never changes your antivirus, firewall, drivers or BIOS.
- **Local.** There is no account, telemetry or upload. The only network traffic is the connection test, listed in the [privacy policy](https://brillant000.github.io/PulseCheck/privacy.html).

## Verifying a release

Each release is built by GitHub Actions from the public source (see `.github/workflows/release.yml`) and published with:

- a SHA-256 file, `PulseCheck.exe.sha256`;
- a signed build-provenance attestation.

```powershell
Get-FileHash .\PulseCheck.exe -Algorithm SHA256
gh attestation verify .\PulseCheck.exe --repo Brillant000/PulseCheck
```

The executable is **not yet code-signed**, so Windows SmartScreen may warn on first run.

## Reporting a vulnerability

Please do **not** open a public issue for a security problem.
Use GitHub's private reporting: **Security → Report a vulnerability** on this repository.
Include the version, what you found and how to reproduce it. We aim to acknowledge reports within 7 days.

## Supported versions

Only the latest release receives fixes.
