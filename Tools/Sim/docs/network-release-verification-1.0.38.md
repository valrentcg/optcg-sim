# v1.0.38 network release verification

Date: 2026-09-19

Two isolated Windows development players were built from the v1.0.38 release candidate. Each process used its own Unity Authentication profile. The temporary QA harness and its compile symbol were removed after the tests and are not part of the production build.

| Network mode | Live path exercised | Result |
| --- | --- | --- |
| Custom Constructed | Private session creation, join by code, Relay peer connection, player name, 50-entry deck payload, ready state, lobby state, chat, and match-start delivery | Pass on host and guest |
| Custom Sealed | Private session creation, join by session ID, Relay peer connection, Sealed settings, Leader exchange, build-start payload, build acknowledgement, built-deck ready payload, start request/acceptance, chat, and match-start delivery | Pass on host and guest |
| Casual | Live Cloudflare matchmaking join, proposal, bilateral ready check, host role assignment, UGS session publication, intended-opponent identity check, guest join by session ID, Relay peer connection, deck/name/chat exchange, and match-start delivery | Pass on host and guest |
| Ranked | Live Cloudflare matchmaking join, proposal, bilateral ready check, host role assignment, UGS session publication, intended-opponent identity check, guest join by session ID, Relay peer connection, deck/name/chat exchange, ranked match-start delivery | Pass on host and guest |

The test proves that each mode can reach a connected two-client match-start state through its production services and wire messages. It does not claim that two humans played every mode through an entire match to its win screen.
