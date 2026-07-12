# v1.0.7 — Replay Viewer, Cloud Match Sharing & Windowed Mode

## New Features
- **Full Replay Viewer.** Watch any finished match play back turn-by-turn or action-by-action:
  - **Match Timeline** side panel — every action grouped by turn, expand/collapse per turn or
    all at once, click any turn or action to jump straight to it, current action highlighted and
    auto-scrolled into view.
  - **Three camera views** — a neutral tournament/broadcast layout (both players side by side,
    cards upright, facing each other across the middle), or either player's classic
    perspective (the same board layout live play uses, opponent's cards shown from your seat).
  - **Playback modes** — Real-Time (faithful to how long the actual match took, thinking pauses
    included), Action-by-Action (your own pace, adjustable delay from instant to 5s), and
    Turn-by-Turn (play a single turn, play through everything, or jump/rewind 1 or 5 turns at a
    time).
  - Adjustable speed (0.25x–4x), a scrubbable timeline with colored markers for turns/attacks/
    plays/effects/life changes, and a toggle to show/hide either player's hand.
  - Replay is watch-only — no dragging cards or clicking through decision prompts, just hover
    for card previews.
  - Linked in from both the new **Replays** screen and the existing **Match History** screen.
- **Local Replays screen.** Browse, watch, and delete your saved replay files, or import a
  replay someone sent you via a native file picker — separate from Match History so it stays
  simple to use for your own saved games.
- **Cloud match sharing.** Every finished match's replay now uploads automatically (alongside
  the existing match log) so it can be found and downloaded later. Search any player's username
  on the Replays screen, or use the new **Matches** button on a Friends entry, to browse their
  public match list and download replays straight into your own local Replays.
- **Windowed / Fullscreen toggle.** New display mode option in Settings — switch anytime, and
  your choice is remembered across launches.
- **Early ST-31 card data.** Added Monkey.D.Luffy and Sanji from the upcoming ST-31 "Red
  Monkey.D.Luffy" starter deck (releasing later this year), sourced from cross-corroborated
  pre-release previews. More to follow as the set fully releases and official card text is
  confirmed.

## Fixes
- Settings screen: the Audio volume slider was quietly overlapping the Sign Out button —
  repositioned with proper spacing.

## Compatibility
No match-presence protocol changes — compatible with v1.0.6 clients.
