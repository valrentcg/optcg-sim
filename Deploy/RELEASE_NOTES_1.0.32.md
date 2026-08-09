# One Piece TCG Simulator 1.0.32

## Accounts and sign-in

- Accounts now use Unity Player Accounts. Sign-up, email verification, sign-in, and Forgot Password use Unity's hosted pages; the game never receives or stores your password.
- Email is used to sign in while the unique display name remains what other players see.
- Because the identity system changed, every account starts fresh in this version: decks, replays, ranked rating, and friends do not carry over. Guest play remains available offline.

## Decks

- Legal decks are no longer rejected while the card database is still loading. This was most visible when OP16-060 Sengoku was incorrectly reported as not being a Leader.

## Replays

- New replays record the engine version that created them. Incompatible recordings can now be identified instead of silently stopping partway through playback.
