# Contributing

Bug reports and pull requests are welcome. Keep to the shape:

- Every byte that leaves the PC is ciphertext, and the format is Stash for Mac's, byte for byte: `tests/fixtures` holds a stash made by the Mac app, and the interop suite restores it. Change the format on both apps or not at all.
- Every data source stays behind an interface (`IChunkStore`, the placeholder check) so the self-tests run in temp folders and never touch a real destination. `dotnet run --project src/Stash.Selftest` must end in `0 failed` before a pull request.
- Plain English in the window; the command line may be terse. No jargon a person cannot act on.
- No network. The only bytes that leave the PC go to the destination folders the user chose, and they are encrypted first.
