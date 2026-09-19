# Unity card-mechanics PlayMode harness

`CardMechanicsEndToEndPlayModeTests` exercises player-visible mechanics through the production
`CardData -> GameEngine -> GameManager` path. `CardMechanicsPlayModeHarness` is the reusable fixture:
it stages deterministic zones, creates real `GameCommand` objects, calls the private client dispatch
and selection handlers, and invokes the production action-panel renderers.

The representative suite currently checks:

- OP13-035 Bepo: choose-one ownership, waiting/choice controls, and resolution for south and north.
- OP15-114 Wyper: current card text, named Life cost button, and face-down to face-up cost mutation
  for south and north. The top card is deterministic, so this path correctly uses the action button;
  effects saying "top or bottom" use the separate clickable Life picker.
- OP01-009 Carrot: only the defending network seat receives Trigger controls, and `useTrigger`
  plays the revealed Life card through `GameManager.Dispatch`.
- OP07-112 Lucy: declining the optional once-per-turn effect does not consume it, resolving it pays
  the Life cost and prevents another use that turn.
- OP01-016 Nami: a remote seat cannot select the owner's hidden deck cards, the owning card-click
  handler can, confirm closes the look, and every card instance remains in deck or hand.
- OP13-079 Imu: the current mixed board-or-hand cost exposes both legal alternatives through the
  Unity green-target affordance, rejects the wrong board type and opposite seat, blocks the remote
  network view, and resolves both the board and hand click paths without losing the draw.
- OP17-040 Edward.Newgate: when a Rocks Pirates Leader is attacked, only the defending owner sees
  payment controls; a hand-card click pays the cost, the Leader click applies +3000, and the
  once-per-turn key is consumed only after resolution.
- OP17-117 Maser Saber: Life Trigger controls belong to the defender, its optional three-card
  discard decision moves to the opponent, declining returns the K.O. continuation to the Trigger
  owner, and only that owner can click the highlighted target.

Add a new scenario when a player-facing card bug is fixed. Read the printing from `CardData` rather
than copying rules text into the test. Stage only the minimum legal state, enter through a production
selection handler or `GameManager.Dispatch`, and assert both the visible affordance and final zones.

## Verification boundary

Passing this suite proves that the tested definitions load in the Unity runtime and those exact
single-process interactions traverse production engine and client dispatch/render methods. Network
seat gating is checked by switching the local-seat view against the same deterministic state.

It does not prove a packaged Windows build, pointer-level hit testing in a rendered player, transport
serialization between two processes, host/client synchronization, reconnects, latency behavior, or
server/lobby services. Those remain separate packaged-player and two-client multiplayer tests.
