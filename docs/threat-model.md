# Threat model

What Stash for Windows protects, against whom, and what it does not. Adapted from Stash for Mac's, which was written before the code that
moves data, so the code has something to be measured against.

## Assets

1. The contents of the folders you chose.
2. The names, structure, sizes and dates of those files (the manifest).
3. The key.

## Adversaries and outcome

| Adversary | Gets | Learns |
|---|---|---|
| The storage provider | every chunk blob and manifest blob | sizes of blobs (padded to 4 MB steps in a later version), when uploads happen, roughly how much data; nothing about names or contents |
| Someone with your provider account (phished password, subpoena) | same as the provider | same; can also delete everything, which is why two destinations |
| A thief with your PC, locked | the key wrapped by Windows Data Protection | nothing without your Windows password; BitLocker or device encryption is assumed |
| A thief with your PC, unlocked and app running | the key in memory | everything; no backup tool survives this |
| Someone who finds the recovery card | the key | everything; the card must be treated like a passport |
| A network observer | encrypted provider traffic | timing and volume only |

## Guarantees the code must keep

- Every byte written to a destination is either `STSH1` ciphertext under ChaChaPoly with a fresh
  random nonce, or the encrypted manifest under a separate derived key.
- Chunk names are HMAC-SHA256 of plaintext under a derived naming key. Never a plain hash.
- Authentication failure on any chunk aborts the restore of that file and reports it. Nothing
  partial is written where a good file should go.
- The key is generated with the operating system's random number generator, never derived from a password, never written
  anywhere but Windows Data Protection for this account and the card the user explicitly renders.
- The app never phones home with anything about the user. The only network traffic is the destination the user configured and nothing else: there is no update check.
- Reading a source folder never causes a cloud download: dataless files are skipped and listed.

## Explicitly not provided

- Deniability. The provider can tell these are Stash for Windows blobs.
- Protection from a compromised PC.
- Key recovery. There is none, by design.
- Multi-user sharing of one stash. One key, one person.
