# One Piece TCG Simulator 1.0.34

## Online lobby connection hotfix

### Ranked and Casual

- Fixed both players getting stuck on Connecting to opponent after accepting the Ready check.
- The Relay handshake now waits for a real peer connection and retries deck and player-name exchange until the host receives both.
- Starting a new queue clears connection state from the previous opponent.

### Custom and Custom Sealed

- Fixed a joined player's selected Leader and Ready state failing to appear for the host.
- Leader, Ready, and player-name state now resynchronizes while the waiting room is open, including after returning from the Leader or set selector.
- Lobby message handlers recover when the network connection finishes before the waiting-room screen is ready.
