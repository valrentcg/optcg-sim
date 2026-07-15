# Imported meta decks

Drop one **`.deck`** (or `.txt`) file per deck here. On any `dotnet run`, every file is parsed,
validated against the card library, and registered by id — then usable in `run`/`heuristics`
configs (and crossed against the starter decks automatically when a config leaves `decks` empty).

## Format (tolerant — paste from onepiecetopdecks / sim exports)

```
# name: Purple Luffy Kingdom
# id: op16-purple-luffy          (optional; else derived from the leader/filename)
OP01-060 (Leader)
4 OP01-016
4x ST01-002
OP03-114 x3
...
```

- One card per line: `COUNT CARDID`, `COUNTxCARDID`, `CARDID xCOUNT`, or bare `CARDID` (count 1).
- The single **leader-type** card (or any line tagged `(Leader)`) becomes the deck's leader.
- Lines starting `#` or `//` are comments; `# name:` / `# id:` set metadata.
- Mixed sets are expected — meta decks pull from OP01–OP16, EB, ST, P. All are in the library.

## Check before running

```
dotnet run -c Release -- deckcheck        # validates every file here, reports errors/warnings
```

Errors (unknown card, missing/duplicate leader) block a deck; a non-50 mainboard or >4 copies are
warnings only. Effect coverage across the whole library is near-total (2 unimplemented cards), so
imported meta decks simulate faithfully.
