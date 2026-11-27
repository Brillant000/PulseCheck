# Store listing text (copy into Partner Center > PulseCheck > Store listing)

Submitted on 2026-11-27 (version 1.4.1.0, submission 1): EN/FR/ES/DE listings use this text, with the
"Guided fixes" bullet adapted to the Store package. The certification note is the 500-character short form.

## English (en-us)

**Product name:** PulseCheck

**Short description (max 100 characters):**
Free PC and network health check for new computers, with fixes you confirm one by one.

**Description:**
Just unboxed a new PC? PulseCheck checks it in a few minutes, so you can spot problems while you can still return it.

- Hardware and new-PC checks: Windows activation, memory speed and channels (XMP/EXPO), battery wear, preinstalled extras, drive health, display refresh rate.
- Health and security: CPU and memory load, free space, drivers in error, startup programs, Defender, firewall, Secure Boot, TPM, Windows updates, crashes.
- Network: router, ping, packet loss, jitter, Wi-Fi signal, DNS, and a verdict on where the connection fails (PC, router, provider or DNS).
- Solutions: quick free wins, settings for your graphics card and screen, and upgrade advice explained as "A + B" for gaming, video/3D/photo, work or development.
- Guided fixes: next to some warnings (power plan, display refresh rate, temporary files, preinstalled apps) press Fix. PulseCheck shows exactly what it will change and asks you to confirm. For Windows settings such as game capture, Game Mode, startup apps or proxy, it opens the right Settings page for you.

Private by design: everything runs on your PC. No account, no telemetry, no ads, nothing uploaded. Scanning is read-only. PulseCheck never changes your antivirus, firewall, drivers or BIOS and never installs software.

Available in English, French, Spanish and German. Free and open source (MIT).

**What's new in this version:**
First Microsoft Store release.

**Keywords:** PC health, new PC, diagnostic, network, ping, Wi-Fi, FPS, startup programs, benchmark, system check

**Privacy policy URL:** https://brillant000.github.io/PulseCheck/privacy.html
**Website:** https://brillant000.github.io/PulseCheck/
**Support contact:** https://github.com/Brillant000/PulseCheck/issues

## Français (fr-fr)

**Nom :** PulseCheck

**Description courte :**
Diagnostic gratuit du PC et du réseau, pensé pour les ordinateurs neufs, avec des corrections que vous confirmez une à une.

**Description :**
Vous venez de déballer un PC neuf ? PulseCheck le vérifie en quelques minutes, pour repérer les problèmes tant que vous pouvez encore le retourner.

- Matériel et contrôles PC neuf : activation de Windows, vitesse et canaux de la mémoire (XMP/EXPO), usure de la batterie, logiciels préinstallés, état des disques, fréquence de l'écran.
- Santé et sécurité : charge du processeur et de la mémoire, espace libre, pilotes en erreur, programmes au démarrage, Defender, pare-feu, Secure Boot, TPM, mises à jour, plantages.
- Réseau : routeur, ping, perte de paquets, gigue, signal Wi-Fi, DNS, et un verdict sur l'endroit où la connexion échoue (PC, routeur, fournisseur ou DNS).
- Solutions : gains rapides gratuits, réglages adaptés à votre carte graphique et à votre écran, conseils de mise à niveau expliqués en « A + B » pour le jeu, la vidéo/3D/photo, le travail ou le développement.
- Corrections en un clic : à côté d'un avertissement, cliquez sur Corriger. PulseCheck montre exactement ce qu'il va modifier et vous demande de confirmer. La plupart des corrections peuvent être annulées.

Respect de la vie privée : tout se passe sur votre PC. Pas de compte, pas de télémétrie, pas de publicité, rien n'est envoyé. L'analyse est en lecture seule. PulseCheck ne modifie jamais votre antivirus, votre pare-feu, vos pilotes ou le BIOS et n'installe aucun logiciel.

Disponible en français, anglais, espagnol et allemand. Gratuit et open source (MIT).

**Nouveautés :**
Première version sur le Microsoft Store.

## Screenshots to upload (already in the repo)

`docs/shots/app-en-light.png` and `sol-en-light.png` (1004 x 721), `fix-en-light.png` (564 x 518).
The Partner Center form states its own minimum size and format: check it when you upload. If these are too
small, take new screenshots of the installed app at a larger window size (about 1366 x 768 or more).

## Notes for certification (field "Notes for certification")

PulseCheck is a desktop system-health utility (packaged with runFullTrust). Scanning is read-only. Optional fixes
change the signed-in user's own Windows settings (HKCU registry values, power plan, display mode, startup entries,
removal of Store apps the user ticks) only after an explicit confirmation dialog, and each change is logged locally.
In the packaged version, registry-based settings are not changed by the app (it opens Windows Settings instead).
No data leaves the device except the connectivity test (ping to the router, 1.1.1.1, 8.8.8.8,
HTTPS to microsoft.com and msftconnecttest.com; an optional 10 MB download from speed.cloudflare.com).
No sign-in is needed. Source: https://github.com/Brillant000/PulseCheck
