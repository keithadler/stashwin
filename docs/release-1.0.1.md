Stash for Windows 1.0.1

The schedule now runs whether or not you are signed in, if you want it to.

- **Run even when I am not signed in**, a Settings toggle (or `stash schedule hourly|daily --when-signed-out`). Task Scheduler runs the backup with nobody signed in, without storing your password. Windows cannot open a key protected for your account under that kind of logon, so with the toggle on the key is protected for this PC instead, which an administrator on this PC could read. The toggle, Help, PRIVACY, the threat model and `stash status` all say so; turn it off and the key goes back to your account. Verified in a VM with nobody signed in.
- The schedule is registered through Task Scheduler's XML: a run the PC slept through happens when it is next awake, and running on battery is allowed. Neither was true of the 1.0.0 registration.
- Key files record which way they are wrapped; files from 1.0.0 still open.
- Integration tests register under "Stash for Windows (test)" and never touch the real schedule.

Downloads: `Stash-for-Windows-1.0.1-x64.exe` for ordinary Intel and AMD PCs, `Stash-for-Windows-1.0.1-arm64.exe` for Windows on ARM; `stash-1.0.1-*.exe` is the console twin the schedule runs. One exe, no installer. Free, MIT, nothing phones home except the optional daily version check.
