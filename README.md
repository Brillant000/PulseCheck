# PulseCheck

**Free, open-source PC and network health check for Windows, built for brand-new computers.**
Run it the day you unbox a PC, before the return window closes, or any time something feels slow or the connection drops.

**[Website and download](https://brillant000.github.io/PulseCheck/)** · [Latest release](https://github.com/Brillant000/PulseCheck/releases/latest) · [Privacy](https://brillant000.github.io/PulseCheck/privacy.html)

![PulseCheck main window](docs/shots/app-en-light.png)

## What it checks

| Area | Checks |
|---|---|
| **Hardware** | CPU, memory, graphics, drives, display resolution and refresh rate, BIOS and serial number |
| **New-PC checks** | Windows activation, memory speed and channels (XMP/EXPO), battery wear, preinstalled extras, drive type and health |
| **Health and security** | CPU and memory load, free space, drive health, drivers in error, startup programs, Defender, firewall, Secure Boot, TPM, Windows updates, crashes and disk errors |
| **Network** | Router, ping, packet loss, jitter, Wi-Fi signal, DNS, web access, proxy and VPN, optional speed test, and a verdict on *where* the connection fails (PC, router, provider or DNS) |

Every finding comes with a plain-language fix. You also get a health score out of 100, a "needs your attention" list, a one-click summary to copy, a shareable HTML report and a full-screen dead-pixel test.

English, Français, Español and Deutsch. Light and dark themes follow Windows.

## Download and verify

Download `PulseCheck.exe` from the [latest release](https://github.com/Brillant000/PulseCheck/releases/latest). Nothing to install (Windows 10/11, 64-bit, .NET is bundled).

The file is not code-signed yet, so Windows SmartScreen may show "Windows protected your PC": choose **More info, then Run anyway**.
Releases are built by GitHub Actions from this source. To check yours:

```powershell
Get-FileHash .\PulseCheck.exe -Algorithm SHA256      # compare with PulseCheck.exe.sha256
gh attestation verify .\PulseCheck.exe --repo Brillant000/PulseCheck
```

For the most complete results, use **Run as administrator** (the TPM check needs it).

## Privacy

100 % local. No account, no telemetry, nothing uploaded, and the app is read-only. The only network traffic is the connection test: ping to your router, `1.1.1.1` and `8.8.8.8`; a DNS lookup and an HTTPS connection to `www.microsoft.com`; one small request to `www.msftconnecttest.com`; and, if you tick it, a 10 MB download from `speed.cloudflare.com`.
Details: [privacy policy](https://brillant000.github.io/PulseCheck/privacy.html). To report a vulnerability, see [SECURITY.md](SECURITY.md).

## Build from source

Requires Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```powershell
dotnet test PulseCheck.sln -c Release
cd src; dotnet run -c Release
```

Contributions, translations and new checks are welcome: see [CONTRIBUTING.md](CONTRIBUTING.md).

## Limitations

- Windows only. A few checks need administrator rights.
- Heuristics, not guarantees: battery wear, preinstalled-software detection and similar checks are indicators, not proof. The score is a rough guide.
- Not affiliated with Microsoft. "PC Health Check" is a separate Microsoft product.

## License

[MIT](LICENSE). Provided "as is", without warranty.
