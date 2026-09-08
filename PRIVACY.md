# Privacy

Stash for Windows reads the folders you chose, encrypts each 4 MB piece on this PC with a key only you hold, and writes the pieces and an encrypted list of file names into the destination folders you chose. That is the only data that leaves the PC, and it is ciphertext before it leaves.

The key is kept on this PC wrapped by Windows Data Protection for your account, in `%LocalAppData%\Stash for Windows`. The 24 words on the card are the only other copy; the app never keeps or sends them. There is no password, no recovery and no server.

When the schedule is set to run with nobody signed in, the key is wrapped for this PC instead of for your account, because Windows cannot open an account-wrapped key without that account's credentials; Help says what that trades away. Settings (which folders, which destinations, the schedule) are a plain JSON file in the same folder. `stash.log` records when backups ran and whether they succeeded, nothing about file contents.

Cloud placeholders (files a provider has not actually downloaded to this PC) are listed in the snapshot as skipped and never opened, so the app never makes a provider download anything on your behalf.

Nothing about you is sent anywhere. No analytics, no crash reporting, no account. The one optional network request is the daily update check: one GET to GitHub's releases API for a version number, with no identifiers, on by default and a switch in Settings. The app never asks to run as an administrator.
