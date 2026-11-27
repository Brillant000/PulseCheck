# Changelog

## 1.4.1

- The Solutions tab no longer says "PulseCheck changes nothing for you", which was out of date since the Fix buttons. It now says nothing is changed unless you press Fix and confirm (four languages).
- **Fixed: "Remove preinstalled Store apps" offered things it cannot remove.** It listed leftovers and classic programs that registered an identity from their own folder (for example Norton in Program Files), and then failed with only the program's name. It now offers only real Store packages, the scan warning uses the same list, and a failure shows the reason. Found by testing the packaged app.
- Microsoft Store package: the build script reads the version from the project and the manifest turns off registry/file write virtualization, so the fixes act on the real Windows settings. Not published on the Store yet.

## 1.4.0

Hardening release. No new feature: it fixes what a review and a bug hunt found.

- **Safer temporary-file cleanup.** It now refuses any folder that is not clearly a temp folder (a drive root, Documents, the user profile or a system folder), never follows a folder junction or symbolic link out of the temp folder, and its preview stops counting after 20,000 files or 3 seconds instead of freezing the window.
- **No more silent crashes.** An unexpected error now shows a clear message, keeps the app running and saves technical details in a local `error.log`. Nothing is sent.
- **Faster scans when the connection is down.** A host that does not answer the first three pings is declared unreachable at once, and the DNS lookup has a 5-second limit. Before, an offline scan could look stuck for over a minute.
- **Type-safe registry reads** for Secure Boot, the proxy switch and game capture, so an unexpected value no longer aborts a whole check.
- **Website:** a "Windows or my browser blocks the download" answer in four languages, shorter search descriptions, an updated privacy policy.
- **Tests:** 102 automated tests, including 5,000 random PCs thrown at the advisor, real apply/undo of every fix against a scratch registry, junction and path-safety cases, and an offline-ping test. Tests run sequentially because they share global state.

## 1.3.1

- Fixed "The calling thread cannot access this object" when applying the *Remove preinstalled Store apps* and *Startup programs* fixes. The dialog selection is now read on the UI thread before the background work starts.

## 1.3.0

- **One-click fixes** next to warnings, with a confirmation that lists exactly what will change, a local log (`changes.log`) and **Undo** for most of them: game capture, Game Mode, proxy, power plan, refresh rate, startup programs, restart, temporary files, preinstalled Store apps.
- Where PulseCheck cannot fix something safely (antivirus, firewall, drivers, BIOS), the button opens the right Windows page instead.

## 1.2.0

- **Solutions tab:** quick wins, settings for your hardware, and upgrades explained "A + B", for four usage profiles (gaming, video/3D/photo, work, development).

## 1.1.0

- New design (light and dark themes, score gauge, "needs your attention" list, language switcher), four languages, unit tests, and GitHub Actions builds with a SHA-256 hash and a signed build attestation.

## 1.0.0

- First release: PC and network health check for new computers.
