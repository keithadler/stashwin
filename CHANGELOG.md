# Changelog

## 1.0.1, 2026-09-08

- The schedule is registered through Task Scheduler's XML, so a run the PC slept through happens when it is next awake and running on battery is allowed (the `schtasks` defaults were neither).
- Settings gains "Run even when I am not signed in" (`stash schedule ... --when-signed-out`): a service-for-user logon, no password stored. Windows cannot open the account-wrapped key under that logon, so the key is then wrapped for this PC instead, which an administrator on this PC could read; the toggle, Help and `stash status` all say so, and turning it off wraps the key for the account again. Verified in a VM with nobody signed in.
- Key files now record which way they are wrapped (files from 1.0.0 still open).
- Integration tests register under "Stash for Windows (test)" and never touch the real schedule.

## 1.0.0, 2026-09-08

First working version. Key and recovery card with a three-word check, encrypted pieces and manifests in exactly Stash for Mac's format (a Mac-made stash is a test fixture and restores byte for byte), backup into any folder with deduplication, a restore browser, verify, snapshot management with two retention policies, exclusions, cloud placeholder detection for OneDrive, Google Drive, Dropbox and iCloud, provider folder discovery, a Task Scheduler schedule, a window that follows the Windows light or dark setting, built-in Help in English and Spanish, and the console twin `stash.exe`. On par with Stash for Mac 1.0: the QR code on the recovery card (a phone or the Mac app reads it), a card image and print, a tray icon with the last and next backup and one balloon when a scheduled run fails, open at sign-in, the weekly self-check on the schedule, an optional daily update check, integration tests and CI. Faster than the first cut: Windows' own ChaCha20-Poly1305 when available (three times faster on the same VM), unchanged files carried over from the previous snapshot without being read (with a two-second guard against same-second rewrites), a Stop button with real cancellation, throughput on the progress card, and Verify comparing its sample byte for byte with the live file.
