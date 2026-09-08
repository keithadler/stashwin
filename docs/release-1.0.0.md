**[Download Stash-for-Windows-1.0.0-x64.exe](https://github.com/keithadler/stashwin/releases/download/v1.0.0/Stash-for-Windows-1.0.0-x64.exe)** for Intel and AMD PCs, or **[Stash-for-Windows-1.0.0-arm64.exe](https://github.com/keithadler/stashwin/releases/download/v1.0.0/Stash-for-Windows-1.0.0-arm64.exe)** for Windows on ARM. One exe, no installer, never an administrator. The first time, SmartScreen says it does not recognise the app: **More info**, then **Run anyway**. That is once. The console twin `stash-1.0.0-<arch>.exe` goes next to the app as `stash.exe` so the schedule can run it.

First release. Encrypted backup of the folders that matter into the storage you already have: the OneDrive that comes with 365, the Google Drive that comes with Gmail, Dropbox, iCloud, a disk, a NAS. Several at once, on a schedule, uploading only what changed. The provider only ever holds scrambled blobs. Your key is 24 words on a card. No password, no recovery, no server.

- The same format and the same card as Stash for Mac, byte for byte: a stash made by the Mac app is a test fixture here and restores identically.
- A recovery card with 24 words, a QR code a phone or the Mac app can scan, and a fingerprint; save it as an image or text, print it, and prove you have it with three words.
- Several destinations at once, a Task Scheduler schedule that runs whether or not the window is open, a weekly self-check that restores a random file and compares it byte for byte, snapshots with two retention policies, a restore browser, Stop.
- Cloud placeholders are listed and never downloaded behind your back. Unchanged files are carried over from the previous snapshot without being read.
- A tray icon with the last and next backup, open at sign-in, an optional daily update check, the window and Help in English and Spanish, and the console twin `stash.exe`.

Measured on a Windows 11 ARM64 virtual machine, 250 files of 4 MB: first backup 5.1 s, unchanged backup 1.8 s, verify 3.0 s, restore 4.1 s. Tested on Windows 11 ARM64; the x64 build so far only under emulation. Signed by its author. MIT.
