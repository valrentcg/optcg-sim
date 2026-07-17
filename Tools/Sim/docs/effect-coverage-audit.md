# Effect-coverage operability sweep

- Generated: 2026-07-17 14:00
- Cards in library: **2636** (2278 carry a tagged effect clause)
- Event-timed bodies scanned: **2177**
- **Unrecognized event bodies (silent no-op candidates): 256** across 252 cards
- Recognized-but-risky bodies (target/condition): 1060 across 1024 cards

Method: a card's event body (On Play / On K.O. / Activate: Main / When Attacking / Trigger / On Block / On Opponent's Attack / End-of-turn) is run through the engine's own recognition gate (`GameEngine.AuditEffectRecognized`). Unrecognized ⇒ the engine queues the effect and drops it unresolved. Passive auras and keyword grants use other paths and are excluded. Candidates are recall-biased; verify each against special-case handlers before fixing.

## A. Unrecognized event bodies — likely silent no-ops (by set)

### EB01  (8 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| EB01-020 | Chambres | [Trigger] | Activate this card's [Main] effect. |
| EB01-028 | Gum-Gum Champion Rifle | [Trigger] | Return up to 1 Character with a cost of 3 or less to the bottom of the owner's deck. |
| EB01-030 | Loguetown | [Trigger] | Play this card. |
| EB01-035 | Ms. Monday | [Trigger] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play this card. |
| EB01-051 | Finger Pistol | [Trigger] | Activate this card's [Main] effect. |
| EB01-053 | Gastino | [On Play] | Place up to 1 of your opponent's Characters with a cost of 3 or less at the top or bottom of your opponent's Life cards face-up. |
| EB01-059 | Kingdom Come | [Trigger] | K.O. up to 1 of your opponent's Characters with a cost equal to or less than the total of your and your opponent's Life cards. |
| EB01-061 | Mr.2.Bon.Kurei(Bentham) | [When Attacking] | Select up to 1 of your opponent's Characters. This Character's base power becomes the same as the selected Character's power during this turn. |

### EB02  (7 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| EB02-008 | The Peak | [Trigger] | Activate this card's [Main] effect. |
| EB02-009 | Thousand Sunny | [Activate: Main] | You may rest this Stage: Give up to 1 of your currently given DON!! cards to 1 of your {Straw Hat Crew} type Characters. |
| EB02-020 | We Are! | [Trigger] | Activate this card's [Main] effect. |
| EB02-031 | Hope | [Trigger] | Activate this card's [Main] effect. |
| EB02-040 | BRAND NEW WORLD | [Trigger] | Activate this card's [Main] effect. |
| EB02-050 | Kokoro no Chizu | [Trigger] | Activate this card's [Main] effect. |
| EB02-058 | UUUUUS! | [Trigger] | Activate this card's [Main] effect. |

### EB03  (7 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| EB03-008 | Hibari | [On Play] [When Attacking] | Up to 1 of your {SWORD} type Leader or Character cards can also attack active Characters during this turn. |
| EB03-012 | Otama | [Activate: Main] | You may rest this Character: Rest up to 1 of your opponent's DON!! cards or {Animal} or {SMILE} type Characters with a cost of 3 or less. |
| EB03-020 | There You Are, Sore Loser! | [Trigger] | Set up to 1 of your Characters as active. |
| EB03-032 | Charlotte Flampe | [Your Turn] [On Play] | Up to 1 of your [Charlotte Katakuri] cards gains +2000 power during this turn. |
| EB03-036 | Baby 5 | [On Play] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 2 of your opponent's Characters with a base cos… |
| EB03-055 | Nico Robin | [Opponent's Turn] [On K.O.] | You may deal 1 damage to your opponent. |
| EB03-060 | Will You Be My Servant? | [Trigger] | Activate this card's [Main] effect. |

### EB04  (5 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| EB04-004 | Zeff | [When Attacking] | Your Leader's base power becomes 7000 until the end of your opponent's next End Phase. |
| EB04-010 | Lulucia Kingdom | [On Play] | Set the power of up to 1 of your opponent's Characters to 0 during this turn. |
| EB04-049 | Finger Pistol Yellow Lotus | [Trigger] | Activate this card's [Main] effect. |
| EB04-052 | Sanji | [When Attacking] | This Character's base power becomes the same as your opponent's Leader during this turn. |
| EB04-054 | Bartholomew Kuma | [On K.O.] | Add up to 1 card from the top of your opponent's Life cards to the owner's hand. |

### OP01  (19 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP01-009 | Carrot | [Trigger] | Play this card. |
| OP01-024 | Monkey.D.Luffy | [Activate: Main] [Once Per Turn] | Give this Character up to 2 rested DON!! cards. |
| OP01-028 | Green Star Rafflesia | [Trigger] | Activate this card's [Counter] effect. |
| OP01-030 | In Two Years!! At the Sabaody Archipelago!! | [Trigger] | Activate this card's [Main] effect. |
| OP01-037 | Kawamatsu | [Trigger] | Play this card. |
| OP01-038 | Kanjuro | [On K.O.] | Your opponent chooses 1 card from your hand; trash that card. |
| OP01-040 | Kin'emon | [DON!! x1] [When Attacking] [Once Per Turn] | Set up to 1 of your {The Akazaya Nine} type Character cards with a cost of 3 or less as active. |
| OP01-069 | Caesar Clown | [On K.O.] | Play up to 1 [Smiley] from your deck, then shuffle your deck. |
| OP01-071 | Jinbe | [Trigger] | Play this card. |
| OP01-082 | Monet | [Trigger] | Play this card. |
| OP01-087 | Officer Agents | [Trigger] | Activate this card's [Counter] effect. |
| OP01-102 | Jack | [When Attacking] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Your opponent trashes 1 card from their hand. |
| OP01-104 | Speed | [Trigger] | Play this card. |
| OP01-105 | Bao Huang | [On Play] | Choose 2 cards from your opponent's hand; your opponent reveals those cards. |
| OP01-106 | Basil Hawkins | [Trigger] | Play this card. |
| OP01-112 | Page One | [Activate: Main] [Once Per Turn] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): This Character can also attack your opponent's active Char… |
| OP01-114 | X.Drake | [On Play] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Your opponent trashes 1 card from their hand. |
| OP01-115 | Elephant's Marchoo | [Trigger] | Activate this card's [Main] effect. |
| OP01-116 | Artificial Devil Fruit SMILE | [Trigger] | Activate this card's [Main] effect. |

### OP02  (7 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP02-022 | Whitebeard Pirates | [Trigger] | Activate this card's [Main] effect. |
| OP02-024 | Moby Dick | [Trigger] | Play this card. |
| OP02-067 | Arabesque Brick Fist | [Trigger] | Activate this card's [Main] effect. |
| OP02-075 | Shiki | [Trigger] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play this card. |
| OP02-085 | Magellan | [On Play] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Your opponent returns 1 DON!! card from their field to the… |
| OP02-104 | Sentomaru | [Trigger] | Play this card. |
| OP02-113 | Helmeppo | [Trigger] | Play this card. |

### OP03  (14 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP03-017 | Cross Fire | [Trigger] | Activate this card's [Main] effect. |
| OP03-026 | Kuroobi | [Trigger] | Play this card. |
| OP03-029 | Chew | [Trigger] | Play this card. |
| OP03-030 | Nami | [Trigger] | Play this card. |
| OP03-056 | Sanji's Pilaf | [Trigger] | Activate this card's [Main] effect. |
| OP03-073 | Hull Dismantler Slash | [Trigger] | Activate this card's [Main] effect. |
| OP03-074 | Top Knot | [Trigger] | Activate this card's [Main] effect. |
| OP03-091 | Helmeppo | [On Play] | Set the cost of up to 1 of your opponent's Characters with no base effect to 0 during this turn. |
| OP03-095 | Soap Sheep | [Trigger] | Your opponent trashes 1 card from their hand. |
| OP03-098 | Enies Lobby | [Trigger] | Play this card. |
| OP03-116 | Shirahoshi | [Trigger] | Play this card. |
| OP03-117 | Napoleon | [Trigger] | Play this card. |
| OP03-120 | Tropical Torment | [Trigger] | Activate this card's [Main] effect. |
| OP03-123 | Charlotte Katakuri | [On Play] | Add up to 1 Character with a cost of 8 or less to the top or bottom of the owner's Life cards face-up. |

### OP04  (18 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP04-004 | Karoo | [Activate: Main] | You may rest this Character: Give up to 1 rested DON!! card to each of your {Alabasta} type Characters. |
| OP04-018 | Enchanting Vertigo Dance | [Trigger] | Activate this card's [Main] effect. |
| OP04-021 | Viola | [On Your Opponent's Attack] | ➁ (You may rest the specified number of DON!! cards in your cost area.): Rest up to 1 of your opponent's DON!! cards. |
| OP04-036 | Donquixote Family | [Trigger] | Activate this card's [Counter] effect. |
| OP04-052 | Black Maria | [Trigger] | Play this card. |
| OP04-055 | Plague Rounds | [Trigger] | Activate this card's [Main] effect. |
| OP04-064 | Ms. All Sunday | [Trigger] | DON!! −2 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play this card. |
| OP04-065 | Miss.Goldenweek(Marianne) | [Trigger] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play this card. |
| OP04-066 | Miss.Valentine(Mikita) | [Trigger] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play this card. |
| OP04-067 | Miss.MerryChristmas(Drophy) | [Trigger] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play this card. |
| OP04-069 | Mr.2.Bon.Kurei(Bentham) | [On Your Opponent's Attack] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): This Character's base power becomes the same as the power … |
| OP04-069 | Mr.2.Bon.Kurei(Bentham) | [Trigger] | DON!! −1: Play this card. |
| OP04-073 | Mr.13 & Ms.Friday | [Trigger] | Play this card. |
| OP04-080 | Gyats | [On Play] | Up to 1 of your {Dressrosa} type Characters can also attack active Characters during this turn. |
| OP04-097 | Otama | [On Play] | Add up to 1 of your opponent's {Animal} or {SMILE} type Characters with a cost of 3 or less to the top of your opponent's Life cards face-up. |
| OP04-103 | Kouzuki Hiyori | [Trigger] | Play this card. |
| OP04-110 | Pound | [On K.O.] | Add up to 1 of your opponent's Characters with a cost of 3 or less to the top or bottom of your opponent's Life cards face-up. |
| OP04-111 | Hera | [Activate: Main] | You may trash 1 of your {Homies} type Characters other than this Character and rest this Character: Set up to 1 of your [Charlotte Linlin] Characters as active. |
| OP04-111 | Hera | [Trigger] | Play this card. |
| OP04-113 | Rabiyan | [Trigger] | Play this card. |

### OP05  (11 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP05-007 | Sabo | [On Play] | K.O. up to 2 of your opponent's Characters with a total power of 4000 or less. |
| OP05-019 | Fire Fist | [Trigger] | Activate this card's [Main] effect. |
| OP05-058 | It's a Waste of Human Life!! | [Trigger] | Place all Characters with a cost of 2 or less at the bottom of the owner's deck. |
| OP05-073 | Miss Doublefinger(Zala) | [Trigger] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play this card. |
| OP05-076 | When You're at Sea You Fight against Pirates!! | [Trigger] | Activate this card's [Main] effect. |
| OP05-079 | Viola | [On Play] | Your opponent places 3 cards from their trash at the bottom of their deck in any order. |
| OP05-102 | Gedatsu | [On Play] | K.O. up to 1 of your opponent's Characters with a cost equal to or less than the number of your opponent's Life cards. |
| OP05-106 | Shura | [Trigger] | Play this card. |
| OP05-111 | Hotori | [On Play] | You may play 1 [Kotori] from your hand: Add up to 1 of your opponent's Characters with a cost of 3 or less to the top or bottom of your opponent's Life cards… |
| OP05-114 | El Thor | [Trigger] | K.O. up to 1 of your opponent's Characters with a cost equal to or less than the number of your opponent's Life cards. |
| OP05-116 | Hino Bird Zap | [Trigger] | Activate this card's [Main] effect. |

### OP06  (14 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP06-009 | Shuraiya | [When Attacking] [On Block] [Once Per Turn] | This Character's base power becomes the same as your opponent's Leader until the start of your next turn. |
| OP06-013 | Monkey.D.Luffy | [Trigger] | Activate this card's [On Play] effect. |
| OP06-039 | You Ain't Even Worth Killing Time!! | [Trigger] | Activate this card's [Main] effect. |
| OP06-040 | Shark Arrows | [Trigger] | Activate this card's [Main] effect. |
| OP06-041 | The Ark Noah | [On Play] | Rest all of your opponent's Characters. |
| OP06-041 | The Ark Noah | [Trigger] | Play this card. |
| OP06-056 | Ama no Murakumo Sword | [Trigger] | Activate this card's [Main] effect. |
| OP06-062 | Vinsmoke Judge | [Activate: Main] [Once Per Turn] | DON!! −1: Rest up to 1 of your opponent's DON!! cards. |
| OP06-081 | Absalom | [On Play] | You may return 2 cards from your trash to the bottom of your deck in any order: K.O. up to 1 Character with a cost of 2 or less. |
| OP06-083 | Oars | [Activate: Main] | You may K.O. 1 of your {Thriller Bark Pirates} type Characters: This Character's effect is negated during this turn. |
| OP06-086 | Gecko Moria | [On Play] | Choose up to 1 Character card with a cost of 4 or less and up to 1 Character card with a cost of 2 or less from your trash. Play 1 card and play the other ca… |
| OP06-096 | ...Nothing...at All!!! | [Trigger] | Activate this card's [Counter] effect. |
| OP06-097 | Negative Hollow | [Trigger] | Activate this card's [Main] effect. |
| OP06-107 | Kouzuki Momonosuke | [On Play] | Add up to 1 of your {Land of Wano} type Characters other than [Kouzuki Momonosuke] to the top or bottom of the owner's Life cards face-up. |
| OP06-117 | The Ark Maxim | [Activate: Main] [Once Per Turn] | You may rest this card and 1 of your [Enel] cards: K.O. all of your opponent's Characters with a cost of 2 or less. |

### OP07  (13 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP07-001 | Monkey.D.Dragon | [Activate: Main] [Once Per Turn] | Give up to 2 total of your currently given DON!! cards to 1 of your Characters. |
| OP07-002 | Ain | [On Play] | Set the power of up to 1 of your opponent's Characters to 0 during this turn. |
| OP07-008 | Mr. Tanaka | [Trigger] | Play this card. |
| OP07-014 | Moda | [Your Turn] [On Play] | Up to 1 of your [Portgas.D.Ace] cards gains +2000 power during this turn. |
| OP07-016 | Galaxy Wink | [Trigger] | Activate this card's [Main] effect. |
| OP07-017 | Dragon Breath | [Trigger] | Activate this card's [Main] effect. |
| OP07-018 | KEEP OUT | [Trigger] | Activate this card's [Counter] effect. |
| OP07-043 | Salome | [Your Turn] [On Play] | Up to 1 of your [Boa Hancock] cards gains +2000 power during this turn. |
| OP07-077 | We're Going to Claim the One Piece!!! | [Trigger] | Activate this card's [Main] effect. |
| OP07-085 | Stussy | [On Play] | You may trash 1 of your Characters: K.O. up to 1 of your opponent's Characters. |
| OP07-088 | Hattori | [Your Turn] [On Play] | Up to 1 of your [Rob Lucci] cards gains +2000 power during this turn. |
| OP07-097 | Vegapunk | [Activate: Main] [Once Per Turn] | ① (You may rest the specified number of DON!! cards in your cost area.): Select up to 1 {Egghead} type card with a cost of 5 or less from your hand and play … |
| OP07-117 | Egghead | [Trigger] | Play this card. |

### OP08  (8 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP08-031 | Miyagi | [On Play] | Set up to 1 of your {Minks} type Characters with a cost of 2 or less as active. |
| OP08-039 | Zou | [End of Your Turn] | Set up to 1 of your {Minks} type Characters as active. |
| OP08-051 | Buckin | [Your Turn] [On Play] | Up to 1 of your [Edward Weevil] cards gains +2000 power during this turn. |
| OP08-056 | Moby Dick | [Trigger] | Play this card. |
| OP08-068 | Charlotte Perospero | [Trigger] | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play this card. |
| OP08-094 | Imperial Flame | [Trigger] | Activate this card's [Main] effect. |
| OP08-106 | Nami | [Trigger] | Activate this card's [On Play] effect. |
| OP08-112 | S-Snake | [Trigger] | Activate this card's [On Play] effect. |

### OP09  (8 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP09-029 | Tony Tony.Chopper | [End of Your Turn] | Set up to 1 of your {ODYSSEY} type Characters with a cost of 4 or less as active. |
| OP09-057 | Cross Guild | [Trigger] | Activate this card's [Main] effect. |
| OP09-096 | My Era...Begins!! | [Trigger] | Activate this card's [Main] effect. |
| OP09-097 | Black Vortex | [Trigger] | Negate the effect of up to 1 of your opponent's Leader or Character cards during this turn. |
| OP09-098 | Black Hole | [Trigger] | Negate the effect of up to 1 of your opponent's Leader or Character cards during this turn. |
| OP09-101 | Kuzan | [On Play] | Place 1 of your opponent's Characters with a cost of 3 or less at the top or bottom of your opponent's Life cards face-up: Your opponent trashes 1 card from … |
| OP09-102 | Professor Clover | [Trigger] | Activate this card's [On Play] effect. |
| OP09-110 | Pierre | [Trigger] | Play this card. |

### OP10  (6 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP10-037 | Lim | [End of Your Turn] | Set up to 1 of your {ODYSSEY} type Characters as active. |
| OP10-059 | Fo...llow...Me...and...I...Will...Gui...de...You | [Trigger] | Activate this card's [Main] effect. |
| OP10-060 | Barrier-Barrier Pistol | [Trigger] | Activate this card's [Main] effect. |
| OP10-070 | Trebol | [On Play] | All of your Characters with 1000 base power or less cannot be K.O.'d by your opponent's effects until the end of your opponent's next turn. |
| OP10-098 | Liberation | [Trigger] | Negate the effect of up to 1 of each of your opponent's Leader and Character cards during this turn. |
| OP10-115 | Let's Meet Again in the New World | [Trigger] | K.O. up to 1 of your opponent's Characters with a cost equal to or less than the number of your opponent's Life cards. |

### OP11  (10 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP11-014 | Borsalino | [Activate: Main] | You may rest this Character: Up to 1 of your {Navy} type Leader or Character cards can also attack active Characters during this turn. |
| OP11-031 | Jinbe | [Activate: Main] [Once Per Turn] | Up to 1 of your {Fish-Man} or {Merfolk} type Characters can attack Characters on the turn in which it is played. |
| OP11-060 | Let's Crash This Wedding!!! | [Trigger] | Activate this card's [Main] effect. |
| OP11-070 | Charlotte Pudding | [Activate: Main] | DON!! −1, You may rest this Character: Look at 1 card from the top of your opponent's deck. |
| OP11-075 | Jaguar.D.Saul | [Trigger] | Activate this card's [On Play] effect. |
| OP11-084 | Kuzan | [When Attacking] | Up to 1 of your {Navy} type Leader or Character cards can also attack active Characters during this turn. |
| OP11-091 | Berry Good | [On Play] | Your opponent places 3 Events from their trash at the bottom of their deck in any order. |
| OP11-099 | I'm Gonna Be a Navy Officer!!! | [Trigger] | Activate this card's [Main] effect. |
| OP11-116 | Merman Combat Ultramarine | [Trigger] | Add up to 1 of your opponent's Characters with a cost of 4 or less to the top or bottom of the owner's Life cards face-up. |
| OP11-119 | Koby | [On Play] | Up to 1 of your Characters can also attack active Characters during this turn. |

### OP12  (6 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP12-041 | Sanji | [Activate: Main] [Once Per Turn] | DON!! −1: Activate up to 1 {Straw Hat Crew} type Event with a base cost of 3 or less from your hand. |
| OP12-061 | Donquixote Rosinante | [Activate: Main] [Once Per Turn] | DON!! −1: The next time you play [Trafalgar Law] with a cost of 4 or more from your hand during this turn, the cost will be reduced by 2. |
| OP12-075 | Ms. All Sunday | [Trigger] | DON!! −1: Play this card. |
| OP12-080 | Baratie | [Trigger] | Play this card. |
| OP12-097 | Captains Assembled | [Trigger] | Activate this card's [Main] effect. |
| OP12-105 | Trafalgar Lammy | [Your Turn] [On Play] | Up to 1 of your [Trafalgar Law] cards gains +2000 power during this turn. |

### OP13  (9 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP13-006 | Woop Slap | [On Play] | Give up to 2 rested DON!! cards to 1 of your [Monkey.D.Luffy] cards. |
| OP13-014 | Portgas.D.Rouge | [Trigger] | Up to 1 of your [Portgas.D.Ace] cards gains +3000 power during this turn. |
| OP13-020 | Meteor Fist | [Trigger] | Activate this card's [Main] effect. |
| OP13-035 | Bepo | [End of Your Turn] | Set this Character or up to 1 of your DON!! cards as active. |
| OP13-039 | Gum-Gum Snake Shot | [Trigger] | Activate this card's [Counter] effect. |
| OP13-096 | The Five Elders Are at Your Service!!! | [Trigger] | Activate this card's [Main] effect. |
| OP13-106 | Conney | [Trigger] | Play this card. |
| OP13-113 | Lilith | [Trigger] | Activate this card's [On Play] effect. |
| OP13-116 | The One Who Is the Most Free Is the Pirate King!!! | [Trigger] | Activate this card's [Main] effect. |

### OP14  (10 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP14-001 | Trafalgar Law | [Activate: Main] [Once Per Turn] | Select 2 of your {Supernovas} or {Heart Pirates} type Characters. Swap the base power of the selected Characters with each other during this turn. |
| OP14-054 | Fisher Tiger | [End of Your Turn] | Trash cards from your hand until you have 5 cards in your hand. |
| OP14-060 | Donquixote Doflamingo | [On Your Opponent's Attack] [Once Per Turn] | DON!! −1: Select your Leader or 1 of your {Donquixote Pirates} type Characters. Change the attack target to the selected card. |
| OP14-065 | Senor Pink | [On K.O.] | Your opponent returns 1 DON!! card from their field to their DON!! deck. |
| OP14-097 | Hurry Up and Make Me the Pirate King! | [Trigger] | Activate this card's [Main] effect. |
| OP14-099 | Disappointed? | [Trigger] | Activate this card's [Main] effect. |
| OP14-103 | Gloriosa (Grandma Nyon) | [Trigger] | Play this card. |
| OP14-104 | Gecko Moria | [On Play] | Select up to 1 {Thriller Bark Pirates} type Character with a cost of 4 or less from your trash and play it or add it to the top of your Life cards face-up. |
| OP14-106 | Salome | [Trigger] | Play this card. |
| OP14-108 | Silvers Rayleigh | [Trigger] | Activate this card's [On Play] effect. |

### OP15  (9 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP15-014 | Bartolomeo | [On Play] | Activate up to 1 {Dressrosa} type Event with a base cost of 3 or less from your hand. |
| OP15-026 | Jango | [Activate: Main] | You may trash this Character: Give up to 1 of your opponent's rested DON!! cards to 1 of your opponent's Characters. |
| OP15-031 | Purinpurin | [On Play] | Select up to 1 of your opponent's rested Characters. If the chosen Character has a cost equal to the number of DON!! cards given to it, K.O. it. |
| OP15-032 | Brook | [Activate: Main] | You may trash this Character: If your Leader has the {Straw Hat Crew} type, set up to 1 of your Characters with a base cost of 8 or less as active. |
| OP15-034 | Yorki | [Your Turn] [On Play] | Up to 1 of your [Brook] cards gains +2000 power during this turn. |
| OP15-048 | Chinjao | [Opponent's Turn] [On K.O.] | Your opponent places 1 card from their hand at the bottom of their deck. |
| OP15-079 | Absalom | [Trigger] | Activate this card's [On K.O.] effect. |
| OP15-093 | The Risky Brothers | [Activate: Main] | You may trash this Character: If you have 15 or more cards in your trash, up to 1 of your [Monkey.D.Luffy] Characters gains [Rush: Character] and the ＜Slash＞… |
| OP15-097 | I Find It Embarrassing as a Human Being | [Trigger] | Activate this card's [Main] effect. |

### OP16  (18 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| OP16-011 | Vista | [DON!! x1] [When Attacking] | K.O. up to 2 of your opponent's Characters with 2000 base power or less. |
| OP16-030 | Trafalgar Law | [End of Your Turn] | Set all of your green Characters with a cost of 5 or less as active. |
| OP16-036 | Mr.2.Bon.Kurei(Bentham) | [When Attacking] | This Character's base power becomes the same as your opponent's Leader during this turn. |
| OP16-039 | Gum-Gum Twin Jet Pistol | [Trigger] | Rest your opponent's Leader. |
| OP16-047 | Donquixote Doflamingo | [Activate: Main] | You may rest this Character: If your opponent has 8 or more cards in their hand, they place 2 cards from their hand at the bottom of their deck in any order. |
| OP16-055 | Mr.2.Bon.Kurei(Bentham) | [DON!! x1] [When Attacking] | This Character's base power becomes the same as your opponent's Leader's power during this turn. |
| OP16-060 | Sengoku | [Activate: Main] | You may return 8 of your active DON!! cards to your DON!! deck: Play up to 3 {Admiral} type Character cards with different card names from your hand. |
| OP16-074 | Magellan | [On K.O.] | Your opponent returns 4 DON!! cards from their field to their DON!! deck. |
| OP16-094 | Portgas.D.Ace | [On K.O.] | Your opponent trashes 2 cards from their hand. |
| OP16-102 | Avalo Pizarro | [Trigger] | Activate this card's [On K.O.] effect. |
| OP16-103 | Van Augur | [Trigger] | Activate this card's [On K.O.] effect. |
| OP16-104 | Catarina Devon | [When Attacking] | Select up to 1 of your opponent's Characters. This Character's base power becomes the same as the selected Character's power during this turn. |
| OP16-106 | Sanjuan.Wolf | [Trigger] | Activate this card's [On K.O.] effect. |
| OP16-107 | Jesus Burgess | [On K.O.] | Add up to 1 card from the top of your opponent's Life cards to the owner's hand. |
| OP16-109 | Doc Q | [Trigger] | Activate this card's [On K.O.] effect. |
| OP16-110 | Vasco Shot | [Trigger] | Activate this card's [On K.O.] effect. |
| OP16-114 | Laffitte | [Trigger] | Activate this card's [On K.O.] effect. |
| OP16-115 | Black Vortex | [Trigger] | Negate the effect of up to 1 of your opponent's Leader or Character cards during this turn. |

### P  (11 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| P-002 | I Smell Adventure!!! | [Trigger] | Activate this card's [Main] effect. |
| P-008 | Yamato | [Activate: Main] | You may rest this Character: Rest 1 of your opponent's Characters with a cost of 2 or less. |
| P-014 | Koby | [Trigger] | Play this card. |
| P-029 | Bartolomeo | [End of Your Turn] | You may rest this Character: Set up to 1 of your {FILM} type Characters other than [Bartolomeo] as active. |
| P-057 | Fleeting Lullaby | [Trigger] | Activate this card's [Main] effect. |
| P-058 | Where the Wind Blows | [Trigger] | Set all of your {FILM} type Characters as active. |
| P-071 | Marco | [On K.O.] | You may add this Character card to your hand. |
| P-091 | Shirahoshi | [Activate: Main] | You may rest this Character: Up to 1 of your {Neptunian} type Characters can attack Characters on the turn in which it is played. |
| P-096 | Girl | [Activate: Main] [Once Per Turn] | Give up to 1 rested DON!! card to 1 of your [Nami] cards. |
| P-100 | Marshall.D.Teach | [When Attacking] | Negate the effects of your opponent's Leader and all of their Characters during this turn. |
| P-106 | Monkey.D.Luffy | [End of Your Turn] | You may turn 1 card from the top of your Life cards face-up: Set up to 1 of your {Egghead} type Characters as active. |

### PRB02  (1 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| PRB02-012 | Nami | [Trigger] | Play this card. |

### ST01  (2 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST01-002 | Usopp | [Trigger] | Play this card. |
| ST01-015 | Gum-Gum Jet Pistol | [Trigger] | Activate this card's [Main] effect. |

### ST02  (2 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST02-005 | Killer | [Trigger] | Play this card. |
| ST02-008 | Scratchmen Apoo | [DON!! x1] [When Attacking] | Rest up to 1 of your opponent's DON!! cards. |

### ST03  (5 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST03-007 | Sentomaru | [DON!! x1] [Activate: Main] [Once Per Turn] | ➁ (You may rest the specified number of DON!! cards in your cost area.): Play up to 1 [Pacifista] with a cost of 4 or less from your deck, then shuffle your … |
| ST03-010 | Bartholomew Kuma | [Trigger] | Play this card. |
| ST03-013 | Boa Hancock | [Trigger] | Play this card. |
| ST03-015 | Sables | [Trigger] | Activate this card's [Main] effect. |
| ST03-016 | Thrust Pad Cannon | [Trigger] | Activate this card's [Counter] effect. |

### ST04  (2 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST04-010 | Who's.Who | [Trigger] | Play this card. |
| ST04-014 | Lead Performer "Disaster" | [Trigger] | Activate this card's [Main] effect. |

### ST05  (1 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST05-009 | Scarlet | [Trigger] | Play this card. |

### ST06  (1 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST06-015 | Great Eruption | [Trigger] | Your opponent chooses 1 card from their hand and trashes it. |

### ST07  (5 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST07-007 | Charlotte Brulee | [Trigger] | Play this card. |
| ST07-010 | Charlotte Linlin | [On Play] | Your opponent chooses one: |
| ST07-011 | Zeus | [Trigger] | Play this card. |
| ST07-013 | Prometheus | [Trigger] | Play this card. |
| ST07-015 | Soul Pocus | [Trigger] | Activate this card's [Main] effect. |

### ST08  (1 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST08-007 | Nefeltari Vivi | [Trigger] | Play this card. |

### ST12  (2 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST12-002 | Kuina | [Trigger] | Play this card. |
| ST12-016 | Lion Strike | [Trigger] | Activate this card's [Main] effect. |

### ST13  (1 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST13-019 | The Three Brothers' Bond | [Trigger] | Activate this card's [Main] effect. |

### ST16  (1 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST16-004 | Shanks | [On Play] | K.O. up to 1 of your opponent's rested Characters. |

### ST17  (1 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST17-003 | Buggy | [On Play] | Look at 3 cards from the top of your deck and place them at the top of your deck in any order. |

### ST21  (2 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST21-001 | Monkey.D.Luffy | [DON!! x1] [Activate: Main] [Once Per Turn] | Give up to 2 rested DON!! cards to 1 of your Characters. |
| ST21-017 | Gum-Gum Mole Pistol | [Trigger] | Activate this card's [Main] effect. |

### ST26  (1 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST26-002 | Tony Tony.Chopper | [On Play] | DON!! −2 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Rest up to 1 of your opponent's DON!! cards or Characters … |

### ST27  (1 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST27-005 | Marshall.D.Teach | [Activate: Main] | You may rest this Character: K.O. up to 1 Character with a cost of 3 or less. |

### ST29  (3 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST29-007 | Tony Tony.Chopper | [Trigger] | Up to 1 of your [Monkey.D.Luffy] cards gains +2000 power during this turn. |
| ST29-012 | Monkey.D.Luffy | [Activate: Main] [Once Per Turn] | Give up to 1 rested DON!! card to 1 of your [Monkey.D.Luffy] cards. |
| ST29-012 | Monkey.D.Luffy | [Trigger] | Play this card. |
| ST29-013 | Rob Lucci | [Trigger] | K.O. up to 1 of your opponent's Characters with a cost equal to or less than the total of your and your opponent's Life cards. |

### ST30  (2 cards)

| Card | Name | Tag | Unrecognized body |
|---|---|---|---|
| ST30-012 | Monkey.D.Luffy | [When Attacking] | Rest up to 1 of your opponent's [Blocker] Characters. |
| ST30-017 | And You Get Yourself in Big Trouble!! | [Trigger] | Activate this card's [Main] effect. |

## B. Recognized but carrying a targeting/condition risk (fires, but may fire wrong)

| Card | Name | Tag / risk | Body |
|---|---|---|---|
| EB01-001 | Kouzuki Oden | [DON!! x1] [When Attacking] ⚠targeting-qualifier | If you have a {Land of Wano} type Character with a cost of 5 or more, this Leader gains +1000 power until the start of your next turn. |
| EB01-002 | Izo | [On Your Opponent's Attack] [Once Per Turn] ⚠embedded-condition | You may trash 1 card from your hand: If your Leader has the {Land of Wano} or {Whitebeard Pirates} type, give up to 1 of your opponent's Leader or Character … |
| EB01-003 | Kid & Killer | [When Attacking] ⚠embedded-condition | If your opponent has 2 or less Life cards, this Character gains +2000 power during this turn. |
| EB01-012 | Cavendish | [On Play] [When Attacking] ⚠embedded-condition | If your Leader has the {Supernovas} type and you have no other [Cavendish] Characters, set up to 2 of your DON!! cards as active. |
| EB01-013 | Kouzuki Hiyori | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Play up to 1 {Land of Wano} type Character card with a cost of 5 or less other than [Kouzuki Hiyori] from your hand. Then, draw… |
| EB01-015 | Scratchmen Apoo | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 2 or less. |
| EB01-016 | Bingoh | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: K.O. up to 1 of your opponent's rested Characters with a cost of 1 or less. |
| EB01-021 | Hannyabal | [End of Your Turn] ⚠targeting-qualifier | You may return 1 of your {Impel Down} type Characters with a cost of 2 or more to the owner's hand: Add up to 1 DON!! card from your DON!! deck and set it as… |
| EB01-022 | Inazuma | [End of Your Turn] ⚠embedded-condition | If you have 2 or less cards in your hand, draw 2 cards. |
| EB01-026 | Prince Bellett | [DON!! x1] [When Attacking] ⚠targeting-qualifier | If you have 1 or less cards in your hand, return up to 1 Character with a cost of 3 or less to the owner's hand. |
| EB01-029 | Sorry. I'm a Goner. | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 8 or less to the owner's hand. |
| EB01-031 | Kalifa | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {Water Seven} type, add up to 2 Cha… |
| EB01-033 | Blueno | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {Water Seven} type, play up to 1 {W… |
| EB01-034 | Ms. Wednesday | [On Your Opponent's Attack] [Once Per Turn] ⚠embedded-condition | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader's type includes "Baroque Works", add up to … |
| EB01-035 | Ms. Monday | [On Play] ⚠embedded-condition | If your Leader's type includes "Baroque Works", up to 1 of your Leader or Character cards gains +1000 power during this turn. |
| EB01-036 | Minochihuahua | [On K.O.] ⚠embedded-condition | If your Leader has the {Impel Down} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| EB01-037 | Mr. 9 | [On Your Opponent's Attack] [Once Per Turn] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| EB01-040 | Kyros | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may turn 1 card from the top of your Life cards face-up: K.O. up to 1 of your opponent's Characters with a cost of 0. |
| EB01-042 | Scarlet | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Play up to 1 {Dressrosa} type Character card with a cost of 3 or less other than [Scarlet] from your hand rested. Then, give up… |
| EB01-045 | Brook | [On Play] ⚠targeting-qualifier | If your opponent has a Character with a cost of 0, this Character gains [Rush] during this turn. |
| EB01-046 | Brook | [On Play] [When Attacking] ⚠targeting-qualifier | Give up to 1 of your opponent's Characters −1 cost during this turn. Then, K.O. up to 1 of your opponent's Characters with a cost of 0. |
| EB01-049 | T-Bone | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 2 or less. |
| EB01-054 | Gan.Fall | [On Play] ⚠targeting-qualifier | If your opponent has 1 or less Life cards, K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| EB02-003 | Tony Tony.Chopper | [On Play] ⚠embedded-condition | If your Leader has the {Straw Hat Crew} type, give up to 1 rested DON!! card to your Leader or 1 of your Characters. |
| EB02-006 | Yamato | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your Leader has the {Land of Wano} type or is [Portgas.D.Ace], give up to 1 rested DON!! card to 1 of your Leader. Then, this Character gains [Rush] durin… |
| EB02-007 | Cloven Rose Blizzard | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 4000 power or less. |
| EB02-010 | Monkey.D.Luffy | [Activate: Main] [Once Per Turn] ⚠embedded-condition | DON!! −2: If the only Characters on your field are {Straw Hat Crew} type Characters, set up to 2 of your DON!! cards as active. Then, this Leader gains +1000… |
| EB02-011 | Arlong | [On Play] ⚠targeting-qualifier | If your Leader has the {Fish-Man} or {East Blue} type, give up to 1 rested DON!! card to 1 of your Leader. Then, up to 1 of your opponent's Characters with a… |
| EB02-013 | Carrot | [On Play] ⚠embedded-condition | If you have 3 or more DON!! cards on your field, look at 7 cards from the top of your deck; reveal up to 1 [Zou] and add it to your hand. Then, place the res… |
| EB02-016 | Chopperman | [On Play] ⚠targeting-qualifier | Play up to 1 {Animal} type Character card with a cost of 3 or less from your hand. |
| EB02-018 | Buggy | [On Play] ⚠embedded-condition | If you have no other [Buggy] Characters, up to 1 of your Leader gains [Double Attack] during this turn. |
| EB02-018 | Buggy | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| EB02-019 | Roronoa Zoro | [On Play] ⚠targeting-qualifier | If your Leader has the {Straw Hat Crew} type, rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| EB02-021 | Gum-Gum Giant Pistol | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| EB02-022 | Usopp | [On Play] ⚠targeting-qualifier | If you have 2 or less Characters with 5000 power or more, play up to 1 Character card with 6000 power or less and no base effect from your hand. |
| EB02-024 | Sogeking | [On Play] ⚠targeting-qualifier | Draw 2 cards and place 2 cards from your hand at the bottom of your deck in any order. Then, return up to 1 Character with a cost of 1 or less to the owner's… |
| EB02-025 | Donquixote Rosinante | [Activate: Main] ⚠targeting-qualifier | You may rest 1 of your DON!! cards and this Character: If your Leader is [Donquixote Rosinante], look at 5 cards from the top of your deck; play up to 1 Char… |
| EB02-026 | Nefeltari Vivi | [On Play] ⚠embedded-condition | If your Leader is multicolored and you have 5 or less cards in your hand, draw 2 cards. |
| EB02-027 | Vista | [On Play] ⚠targeting-qualifier | Place up to 1 of your opponent's Characters with 1000 power or less at the bottom of the owner's deck. |
| EB02-028 | Portgas.D.Ace | [On Play] ⚠targeting-qualifier | If your Leader's type includes "Whitebeard Pirates", look at 5 cards from the top of your deck; reveal up to 1 Character card with a cost of 2 and add it to … |
| EB02-032 | Iceburg | [On Play] ⚠embedded-condition | If you have 3 or more DON!! cards on your field, look at 7 cards from the top of your deck; reveal up to 1 [Galley-La Company] and add it to your hand. Then,… |
| EB02-035 | Sanji & Pudding | [On Play] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, draw 1 card. |
| EB02-037 | Franky | [On Play] [When Attacking] ⚠embedded-condition | If your Leader has the {Straw Hat Crew} type and the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up… |
| EB02-038 | Magellan | [On Play] ⚠targeting-qualifier | Play up to 1 {Impel Down} type Character card with a cost of 2 or less from your hand. |
| EB02-041 | Merry Go | [On Play] ⚠embedded-condition | If your Leader has the {Straw Hat Crew} type, draw 1 card. |
| EB02-041 | Merry Go | [Activate: Main] ⚠embedded-condition | You may rest this Stage: If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, up to 1 of your {Straw Hat … |
| EB02-044 | Sengoku | [On Play] ⚠targeting-qualifier | Play up to 1 black {Navy} type Character card with a cost of 4 or less from your trash rested. |
| EB02-048 | Brook | [On K.O.] ⚠targeting-qualifier | Play up to 1 [Laboon] with a cost of 4 or less from your hand. |
| EB02-049 | Monkey.D.Garp | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: If your Leader is [Monkey.D.Garp], K.O. up to 1 of your opponent's Characters with a cost of 1 or less. |
| EB02-052 | Enel | [When Attacking] ⚠embedded-condition | You may trash 1 card from your hand: If you have 1 or less Life cards, add up to 1 card from the top of your deck to the top of your Life cards. Then, this C… |
| EB02-054 | Sanji | [On Play] ⚠embedded-condition | If you have 2 or less Life cards, draw 2 cards and trash 1 card from your hand. |
| EB02-055 | Jinbe | [Trigger] ⚠embedded-condition | If your Leader has the {Fish-Man} or {Merfolk} type and you have 2 or less Life cards, play this card. |
| EB02-056 | Vegapunk | [On Play] ⚠targeting-qualifier | Look at 5 cards from the top of your deck; play up to 1 {Scientist} type Character card with a cost of 5 or less other than [Vegapunk]. Then, place the rest … |
| EB02-057 | Mad Treasure | [When Attacking] ⚠targeting-qualifier | You may add 1 card from the top or bottom of your Life cards to your hand: Add up to 1 of your opponent's Characters with a cost of 3 or less to the top or b… |
| EB03-003 | Uta | [On Play] ⚠targeting-qualifier | If your Leader is [Uta], draw 2 cards. Then, play up to 1 Character card with 6000 power or less and no base effect from your hand. |
| EB03-005 | Sugar | [On Play] ⚠targeting-qualifier | If your Leader is [Sugar], play up to 1 {Donquixote Pirates} type Character card with 6000 power or less from your hand rested. |
| EB03-006 | Nami | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your Leader has the {Alabasta} type, give up to 1 of your opponent's Characters −1000 power during this turn. |
| EB03-007 | Baccarat | [On K.O.] ⚠targeting-qualifier | Play up to 1 Character card with 6000 power or less and no base effect from your hand. |
| EB03-010 | Monet | [On Play] ⚠targeting-qualifier | Look at 5 cards from the top of your deck; reveal up to 1 Character card with 1000 power or less or up to 1 Event card and add it to your hand. Then, place t… |
| EB03-013 | Carrot | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | If this Character was played on this turn, K.O. up to 1 of your opponent's rested Characters with a cost of 5 or less. Then, play up to 1 [Zou] from your hand. |
| EB03-015 | Camie | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: Give up to 1 rested DON!! card to 1 of your {Fish-Man} or {Merfolk} type Leader or Character cards. Then, rest up to 1 of your o… |
| EB03-016 | Kouzuki Hiyori | [On Play] ⚠embedded-condition | If your Leader is [Kouzuki Oden], draw 1 card. |
| EB03-017 | Jewelry Bonney | [On Play] ⚠targeting-qualifier | If your Leader has the {Supernovas} type, set up to 1 of your DON!! cards as active. Then, up to 1 of your opponent's Characters with a cost of 8 or less can… |
| EB03-022 | Isuka | [On Play] ⚠targeting-qualifier | Place up to 1 Character with a cost of 4 or less at the bottom of the owner's deck. |
| EB03-024 | Nefeltari Vivi | [On Play] ⚠targeting-qualifier | Play up to 1 {Alabasta} or {Straw Hat Crew} type Character card with a cost of 5 or less from your hand. Then, you cannot play any Character cards on your fi… |
| EB03-026 | Boa Hancock | [On Play] ⚠embedded-condition | If your opponent has 5 or more cards in their hand, your opponent places 1 card from their hand at the bottom of their deck. |
| EB03-028 | Yu | [Activate: Main] ⚠embedded-condition | You may trash this Character: If you have 4 or less cards in your hand, draw 2 cards. |
| EB03-031 | Vinsmoke Reiju | [Your Turn] [On Play] ⚠targeting-qualifier | DON!! −1: If your Leader is [Sanji], activate the [Main] effect of up to 1 Event card with a cost of 7 or less in your trash. |
| EB03-035 | Charlotte Pudding | [On Play] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up to 1 DON!! card from your DON!! deck and rest… |
| EB03-037 | Lim | [On Play] ⚠embedded-condition | If you have 7 or more DON!! cards on your field, all of your {ODYSSEY} type Leader and Character cards gain +1000 power until the end of your opponent's next… |
| EB03-039 | Ulti | [On Play] ⚠targeting-qualifier | If your Leader has the {Animal Kingdom Pirates} type, draw 1 card and trash 1 card from your hand. Then, play up to 1 Character card with 6000 power or less … |
| EB03-042 | Koala | [Opponent's Turn] [On K.O.] ⚠targeting-qualifier | Play up to 1 {Revolutionary Army} type Character card with a cost of 6 or less other than [Koala] or up to 1 [Nico Robin] with a cost of 6 or less from your … |
| EB03-043 | Stussy | [On Play] ⚠targeting-qualifier | You may place 2 cards with a type including "CP" from your trash at the bottom of your deck in any order: K.O. up to 1 of your opponent's Characters with a c… |
| EB03-045 | Perona | [On Play] ⚠targeting-qualifier | Give up to 1 rested DON!! card to your Leader or 1 of your Characters. Then, if you have 10 or more cards in your trash, play up to 1 {Thriller Bark Pirates}… |
| EB03-046 | Miss Doublefinger(Zala) | [On Play] ⚠targeting-qualifier | If there is a Character with a cost of 0 or with a cost of 8 or more, draw 1 card. |
| EB03-048 | Rebecca | [On Play] ⚠targeting-qualifier | Look at 5 cards from the top of your deck; reveal up to 1 {Dressrosa} type Stage card and add it to your hand. Then, place the rest at the bottom of your dec… |
| EB03-051 | Charlotte Smoothie | [On Play] ⚠targeting-qualifier | If you have a face-up Life card, K.O. up to 1 of your opponent's Characters with a cost of 2 or less. Then, turn all of your Life cards face-down. |
| EB03-052 | Shirahoshi | [Activate: Main] ⚠embedded-condition | You may trash this Character: If your Leader is [Shirahoshi], add 1 card from the top of your deck to the top of your Life cards. Then, all of your {Neptunia… |
| EB03-053 | Nami | [On Play] ⚠embedded-condition | Give up to 1 rested DON!! card to your Leader. Then, if your opponent has 3 or more Life cards, add up to 1 card from the top of your opponent's Life cards t… |
| EB03-053 | Nami | [On K.O.] ⚠targeting-qualifier | You may turn 1 card from the top of your Life cards face-up: Play up to 1 Character card with 6000 power or less from your hand. |
| EB03-055 | Nico Robin | [On Play] ⚠embedded-condition | You may trash 1 card from the top of your Life cards: If your Leader has the {Straw Hat Crew} type, add up to 2 cards from the top of your deck to the top of… |
| EB03-058 | Lilith | [Your Turn] [On Play] ⚠embedded-condition | If you have 2 or less Life cards, draw 1 card. |
| EB03-058 | Lilith | [Trigger] ⚠embedded-condition | If your Leader is [Vegapunk], play this card. |
| EB03-059 | S-Snake | [On Play] ⚠embedded-condition | If your Leader has the {Egghead} type and you have 2 or more Life cards, add up to 1 Character card with a [Trigger] from your hand to the top of your Life c… |
| EB03-059 | S-Snake | [Trigger] ⚠targeting-qualifier | Up to 1 of your opponent's Characters with a cost of 6 or less other than [Monkey.D.Luffy] cannot attack during this turn. |
| EB03-061 | Uta | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | Set up to 1 of your DON!! cards as active. Then, rest up to 1 of your opponent's DON!! cards or Characters with a cost of 4 or less. |
| EB03-062 | Trafalgar Law | [Activate: Main] ⚠targeting-qualifier | You may trash 1 card from your hand and trash this Character: Add up to 1 card from the top of your deck to the top of your Life cards. Then, play up to 1 [T… |
| EB04-001 | Jewelry Bonney | [Activate: Main] [Once Per Turn] ⚠embedded-condition | Give up to 1 of your opponent's Characters −1000 power during this turn. Then, if you have 2 or more Life cards, you may add 1 card from the top of your Life… |
| EB04-007 | Roronoa Zoro | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | If your opponent has a Character with 8000 power or more, this Character gains [Rush: Character] during this turn. |
| EB04-012 | Kikunojo | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If this Character was played on this turn, set your {Land of Wano} type Leader as active. |
| EB04-013 | Carrot | [On Play] ⚠embedded-condition | If your Leader has the {Minks} type, set up to 2 of your {Minks} type Characters and your Leader as active. |
| EB04-015 | Jinbe | [On K.O.] ⚠targeting-qualifier | You may rest 1 of your cards: If your Leader has the {Fish-Man} or {Merfolk} type, play up to 1 green Character card with a cost of 6 or less from your hand. |
| EB04-016 | Bird Neptunian | [When Attacking] ⚠targeting-qualifier | If you have 3 or more {Neptunian} type Characters, rest up to 1 of your opponent's Characters with a cost of 8 or less. |
| EB04-017 | Mystoms | [On Play] ⚠targeting-qualifier | If your Leader has the {Minks} type, play up to 1 {Minks} type Character card with a cost of 5 or less from your hand. |
| EB04-018 | Megalo | [On Play] ⚠targeting-qualifier | You may rest this Character: K.O. up to 1 of your opponent's rested Characters with 8000 power or less. |
| EB04-020 | Shark Brick Fist | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| EB04-021 | Igaram | [On Play] ⚠embedded-condition | If your Leader is [Nefeltari Vivi], draw 2 cards and trash 1 card from your hand. |
| EB04-022 | Issho | [On Play] ⚠embedded-condition | You may trash 2 cards from your hand: If your opponent has 6 or more cards in their hand, your opponent places 2 cards from their hand at the bottom of their… |
| EB04-025 | Nefeltari Vivi | [On Play] ⚠targeting-qualifier | Play up to 1 {Alabasta} type Character card with a cost of 8 or less other than [Nefeltari Vivi] from your hand. Then, your opponent places 1 card from their… |
| EB04-026 | Bluegrass | [On Play] ⚠targeting-qualifier | Place up to 1 of your opponent's Characters with a cost of 1 or less at the bottom of the owner's deck. |
| EB04-027 | Boa Hancock | [Trigger] ⚠targeting-qualifier | Play up to 1 Character card with 5000 power or less and a [Trigger] from your hand. |
| EB04-028 | Ice Time | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 5 or less to the owner's hand. |
| EB04-030 | Kaido | [On Play] ⚠targeting-qualifier | DON!! −2: If your Leader has the {Animal Kingdom Pirates} type, this Character gains [Rush] during this turn. Then, rest up to 1 of your opponent's Character… |
| EB04-031 | King | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your Leader has the {Animal Kingdom Pirates} type and you have no other [King] Characters, add up to 1 DON!! card from your DON!! deck and set it as activ… |
| EB04-032 | Queen | [Activate: Main] [Once Per Turn] ⚠embedded-condition | You may rest 2 of your DON!! cards: If your Leader has the {Animal Kingdom Pirates} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| EB04-033 | Groggy Monsters | [On Play] ⚠embedded-condition | DON!! −1: If you have 3 or more {Foxy Pirates} type Characters, K.O. up to 1 of your opponent's Characters with 6000 base power or less. |
| EB04-034 | Charlotte Pudding | [On Your Opponent's Attack] [Once Per Turn] ⚠embedded-condition | You may trash 1 card from your hand: If you have 4 or more Events in your trash, up to 1 of your Leader or Character cards gains +2000 power during this battle. |
| EB04-036 | Foxy | [On Play] ⚠targeting-qualifier | DON!! −1: If your Leader has the {Foxy Pirates} type, draw 2 cards and trash 1 card from your hand. Then, rest up to 1 of your opponent's Characters with a c… |
| EB04-037 | Porche | [On Play] ⚠embedded-condition | If your Leader has the {Foxy Pirates} type, look at 5 cards from the top of your deck; reveal up to 1 {Foxy Pirates} type card and add it to your hand. Then,… |
| EB04-038 | Rosinante & Law | [On Play] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, draw 1 card. Then, add up to 1 DON!! card from your … |
| EB04-039 | Eustass"Captain"Kid | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Play up to 1 {Kid Pirates} type Character card with a cost of 5 or less from your hand. |
| EB04-045 | Ginny | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: If there are 2 or more Characters with a cost of 8 or more, up to 1 of your {Revolutionary Army} type Leader or Character cards … |
| EB04-047 | Helmeppo | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Play up to 1 {SWORD} type Character card with a cost of 3 or less other than [Helmeppo] from your hand or trash. |
| EB04-051 | Emet | [Trigger] ⚠embedded-condition | Give all of your opponent's Characters −3000 power during this turn. Then, if you have 0 Life cards, play this card. |
| EB04-052 | Sanji | [On K.O.] ⚠targeting-qualifier | If you have 2 or less Life cards, play up to 1 yellow Character card with 6000 power or less from your hand. |
| EB04-053 | Sentomaru | [On Block] ⚠embedded-condition | If you have 2 or less Life cards, draw 1 card. |
| EB04-054 | Bartholomew Kuma | [On Play] ⚠embedded-condition | If you have 2 or less Life cards, add up to 1 card from the top of your deck to the top of your Life cards. |
| EB04-055 | Bartholomew Kuma | [On K.O.] ⚠targeting-qualifier | Play up to 1 {Revolutionary Army} type Character card with a cost of 4 or less from your hand. |
| EB04-055 | Bartholomew Kuma | [Trigger] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type and you and your opponent have a total of 5 or less Life cards, play this card. |
| EB04-058 | Borsalino | [On Play] ⚠embedded-condition | If you have 2 or less Life cards, add up to 1 card from the top of your deck to the top of your Life cards. |
| OP01-002 | Trafalgar Law | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | ➁ (You may rest the specified number of DON!! cards in your cost area.): If you have 5 Characters, return 1 of your Characters to the owner's hand. Then, pla… |
| OP01-003 | Monkey.D.Luffy | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | ➃ (You may rest the specified number of DON!! cards in your cost area.): Set up to 1 of your {Supernovas} or {Straw Hat Crew} type Character cards with a cos… |
| OP01-005 | Uta | [On Play] ⚠targeting-qualifier | Add up to 1 red Character card other than [Uta] with a cost of 3 or less from your trash to your hand. |
| OP01-007 | Caribou | [On K.O.] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 4000 power or less. |
| OP01-014 | Jinbe | [DON!! x1] [On Block] ⚠targeting-qualifier | Play up to 1 red Character card with a cost of 2 or less from your hand. |
| OP01-015 | Tony Tony.Chopper | [DON!! x1] [When Attacking] ⚠targeting-qualifier | You may trash 1 card from your hand: Add up to 1 {Straw Hat Crew} type Character card other than [Tony Tony.Chopper] with a cost of 4 or less from your trash… |
| OP01-017 | Nico Robin | [DON!! x1] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 3000 power or less. |
| OP01-033 | Izo | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP01-035 | Okiku | [DON!! x1] [When Attacking] [Once Per Turn] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP01-038 | Kanjuro | [DON!! x1] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 2 or less. |
| OP01-039 | Killer | [DON!! x1] [On Block] ⚠embedded-condition | If you have 3 or more Characters, draw 1 card. |
| OP01-040 | Kin'emon | [On Play] ⚠targeting-qualifier | If your Leader is [Kouzuki Oden], play up to 1 {The Akazaya Nine} type Character card with a cost of 3 or less from your hand. |
| OP01-042 | Komurasaki | [On Play] ⚠targeting-qualifier | ③ (You may rest the specified number of DON!! cards in your cost area.): If your Leader is [Kouzuki Oden], set up to 1 of your {Land of Wano} type Character … |
| OP01-044 | Shachi | [On Play] ⚠embedded-condition | If you don't have [Penguin], play up to 1 [Penguin] from your hand. |
| OP01-046 | Denjiro | [DON!! x1] [When Attacking] ⚠embedded-condition | If your Leader is [Kouzuki Oden], set up to 2 of your DON!! cards as active. |
| OP01-047 | Trafalgar Law | [On Play] ⚠targeting-qualifier | You may return 1 Character to your hand: Play up to 1 Character card with a cost of 3 or less from your hand. |
| OP01-048 | Nekomamushi | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP01-049 | Bepo | [DON!! x1] [When Attacking] ⚠targeting-qualifier | Play up to 1 {Heart Pirates} type Character card other than [Bepo] with a cost of 4 or less from your hand. |
| OP01-050 | Penguin | [On Play] ⚠embedded-condition | If you don't have [Shachi], play up to 1 [Shachi] from your hand. |
| OP01-051 | Eustass"Captain"Kid | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may rest this Character: Play up to 1 Character card with a cost of 3 or less from your hand. |
| OP01-052 | Raizo | [When Attacking] [Once Per Turn] ⚠embedded-condition | If you have 2 or more rested Characters, draw 1 card. |
| OP01-054 | X.Drake | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 4 or less. |
| OP01-057 | Paradise Waterfall | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 4 or less. |
| OP01-060 | Donquixote Doflamingo | [DON!! x2] [When Attacking] ⚠targeting-qualifier | ➀ (You may rest the specified number of DON!! cards in your cost area.): Reveal 1 card from the top of your deck. If that card is a {The Seven Warlords of th… |
| OP01-063 | Arlong | [DON!! x1] [Activate: Main] ⚠embedded-condition | You may rest this Character: Choose 1 card from your opponent's hand; your opponent reveals that card. If the revealed card is an Event, place up to 1 card f… |
| OP01-064 | Alvida | [DON!! x1] [When Attacking] ⚠targeting-qualifier | You may trash 1 card from your hand: Return up to 1 of your opponent's Characters with a cost of 3 or less to the owner's hand. |
| OP01-070 | Dracule Mihawk | [On Play] ⚠targeting-qualifier | Place up to 1 Character with a cost of 7 or less at the bottom of the owner's deck. |
| OP01-071 | Jinbe | [On Play] ⚠targeting-qualifier | Place up to 1 Character with a cost of 3 or less at the bottom of the owner's deck. |
| OP01-074 | Bartholomew Kuma | [On K.O.] ⚠targeting-qualifier | Play up to 1 [Pacifista] with a cost of 4 or less from your hand. |
| OP01-078 | Boa Hancock | [DON!! x1] [When Attacking] [On Block] ⚠embedded-condition | Draw 1 card if you have 5 or less cards in your hand. |
| OP01-079 | Ms. All Sunday | [On K.O.] ⚠embedded-condition | If your Leader has the {Baroque Works} type, add up to 1 Event from your trash to your hand. |
| OP01-085 | Mr.3(Galdino) | [On Play] ⚠targeting-qualifier | If your Leader has the {Baroque Works} type, select up to 1 of your opponent's Characters with a cost of 4 or less. The selected Character cannot attack unti… |
| OP01-086 | Overheat | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 4 or less to the owner's hand. |
| OP01-094 | Kaido | [On Play] ⚠embedded-condition | DON!! −6 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {Animal Kingdom Pirates} type, K.O.… |
| OP01-095 | Kyoshirou | [On Play] ⚠embedded-condition | Draw 1 card if you have 8 or more DON!! cards on your field. |
| OP01-096 | King | [On Play] ⚠targeting-qualifier | DON!! −2 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| OP01-108 | Hitokiri Kamazo | [On K.O.] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| OP02-004 | Edward.Newgate | [DON!! x2] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 3000 power or less. |
| OP02-005 | Curly.Dadan | [On Play] ⚠targeting-qualifier | Look at up to 5 cards from the top of your deck; reveal up to 1 red Character with a cost of 1 and add it to your hand. Then, place the rest at the bottom of… |
| OP02-009 | Squard | [On Play] ⚠embedded-condition | If your Leader's type includes "Whitebeard Pirates", give up to 1 of your opponent's Characters −4000 power during this turn and add 1 card from the top of y… |
| OP02-010 | Dogura | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: Play up to 1 red Character other than [Dogura] with a cost of 1 from your hand. |
| OP02-011 | Vista | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 3000 power or less. |
| OP02-013 | Portgas.D.Ace | [On Play] ⚠embedded-condition | Give up to 2 of your opponent's Characters −3000 power during this turn. Then, if your Leader's type includes "Whitebeard Pirates", this Character gains [Rus… |
| OP02-015 | Makino | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: Up to 1 of your red Characters with a cost of 1 gains +3000 power during this turn. |
| OP02-016 | Magura | [On Play] ⚠targeting-qualifier | Up to 1 of your red Characters with a cost of 1 gains +3000 power during this turn. |
| OP02-017 | Masked Deuce | [DON!! x2] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 2000 power or less. |
| OP02-018 | Marco | [On K.O.] ⚠embedded-condition | You may trash 1 card with a type including "Whitebeard Pirates" from your hand: If you have 2 or less Life cards, play this Character card from your trash re… |
| OP02-025 | Kin'emon | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | If you have 1 or less Characters, the next time you play a {Land of Wano} type Character card with a cost of 3 or more from your hand during this turn, the c… |
| OP02-030 | Kouzuki Oden | [On K.O.] ⚠targeting-qualifier | Play up to 1 green {Land of Wano} type Character card with a cost of 3 from your deck. Then, shuffle your deck. |
| OP02-032 | Shishilian | [On Play] ⚠targeting-qualifier | ② (You may rest the specified number of DON!! cards in your cost area.): Set up to 1 of your {Minks} type Characters with a cost of 5 or less as active. |
| OP02-034 | Tony Tony.Chopper | [DON!! x1] [When Attacking] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 2 or less. |
| OP02-035 | Trafalgar Law | [Activate: Main] ⚠targeting-qualifier | ➀ (You may rest the specified number of DON!! cards in your cost area.) You may return this Character to the owner's hand: Play up to 1 Character with a cost… |
| OP02-037 | Nico Robin | [On Play] ⚠targeting-qualifier | Play up to 1 {FILM} or {Straw Hat Crew} type Character card with a cost of 2 or less from your hand. |
| OP02-040 | Brook | [On Play] ⚠targeting-qualifier | Play up to 1 {FILM} or {Straw Hat Crew} type Character card with a cost of 3 or less from your hand. |
| OP02-041 | Monkey.D.Luffy | [On Play] ⚠targeting-qualifier | Play up to 1 {FILM} or {Straw Hat Crew} type Character card with a cost of 4 or less from your hand. |
| OP02-042 | Yamato | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 6 or less. |
| OP02-044 | Wanda | [On Play] ⚠targeting-qualifier | Play up to 1 {Minks} type Character card other than [Wanda] with a cost of 3 or less from your hand. |
| OP02-045 | Three Sword Style Oni Giri | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Leader or Character cards with a cost of 5 or less. |
| OP02-046 | Diable Jambe Venaison Shoot | [Trigger] ⚠targeting-qualifier | Play up to 1 Character card with a cost of 4 or less and no base effect from your hand. |
| OP02-047 | Paradise Totsuka | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 3 or less. |
| OP02-049 | Emporio.Ivankov | [End of Your Turn] ⚠embedded-condition | If you have 0 cards in your hand, draw 2 cards. |
| OP02-051 | Emporio.Ivankov | [On Play] ⚠targeting-qualifier | Draw card(s) so that you have 3 cards in your hand and then play up to 1 blue {Impel Down} type Character card with a cost of 6 or less from your hand. |
| OP02-052 | Cabaji | [On Play] ⚠embedded-condition | If you have [Mohji], draw 2 cards and trash 1 card from your hand. |
| OP02-056 | Donquixote Doflamingo | [DON!! x1] [When Attacking] ⚠targeting-qualifier | You may trash 1 card from your hand: Place up to 1 of your opponent's Characters with a cost of 1 or less at the bottom of the owner's deck. |
| OP02-061 | Morley | [When Attacking] ⚠targeting-qualifier | If you have 1 or less cards in your hand, your opponent cannot activate the [Blocker] of any Character with a cost of 5 or less during this battle. |
| OP02-062 | Monkey.D.Luffy | [On Play] [When Attacking] ⚠targeting-qualifier | You may trash 2 cards from your hand: Return up to 1 Character with a cost of 4 or less to the owner's hand. Then, this Character gains [Double Attack] durin… |
| OP02-063 | Mr.1(Daz.Bonez) | [On Play] ⚠targeting-qualifier | Add up to 1 blue Event card with a cost of 1 from your trash to your hand. |
| OP02-064 | Mr.2.Bon.Kurei(Bentham) | [DON!! x1] [When Attacking] ⚠targeting-qualifier | You may trash 1 card from your hand: Place up to 1 Character with a cost of 2 or less at the bottom of the owner's deck. Then, at the end of this battle, pla… |
| OP02-068 | Gum-Gum Rain | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 2 or less to the owner's hand. |
| OP02-069 | DEATH WINK | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 7 or less to the owner's hand. |
| OP02-070 | New Kama Land | [Activate: Main] ⚠embedded-condition | You may rest this Stage: If your Leader is [Emporio.Ivankov], draw 1 card and trash 1 card from your hand. Then, trash up to 3 cards from your hand. |
| OP02-072 | Zephyr | [When Attacking] ⚠targeting-qualifier | DON!! −4 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| OP02-076 | Shiryu | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| OP02-078 | Daifugo | [On Play] ⚠targeting-qualifier | DON!! −2 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play up to 1 {SMILE} type Character card other than [Daifu… |
| OP02-079 | Douglas Bullet | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Rest up to 1 of your opponent's Characters with a cost of … |
| OP02-086 | Minokoala | [On K.O.] ⚠embedded-condition | If your Leader has the {Impel Down} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP02-087 | Minotaur | [On K.O.] ⚠embedded-condition | If your Leader has the {Impel Down} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP02-089 | Judgment of Hell | [Trigger] ⚠embedded-condition | If your opponent has 6 or more DON!! cards on their field, your opponent returns 1 DON!! card from their field to their DON!! deck. |
| OP02-090 | Hydra | [Trigger] ⚠embedded-condition | If your opponent has 6 or more DON!! cards on their field, your opponent returns 1 DON!! card from their field to their DON!! deck. |
| OP02-091 | Venom Road | [Trigger] ⚠embedded-condition | If your opponent has 6 or more DON!! cards on their field, your opponent returns 1 DON!! card from their field to their DON!! deck. |
| OP02-093 | Smoker | [DON!! x1] [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | Give up to 1 of your opponent's Characters −1 cost during this turn. Then, if there is a Character with a cost of 0, this Leader gains +1000 power during thi… |
| OP02-098 | Koby | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP02-099 | Sakazuki | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP02-101 | Strawberry | [When Attacking] ⚠targeting-qualifier | If there is a Character with a cost of 0, your opponent cannot activate the [Blocker] of any Character with a cost of 5 or less during this battle. |
| OP02-102 | Smoker | [When Attacking] ⚠targeting-qualifier | If there is a Character with a cost of 0, this Character gains +2000 power during this battle. |
| OP02-110 | Hina | [On Block] ⚠targeting-qualifier | Select up to 1 of your opponent's Characters with a cost of 6 or less. The selected Character cannot attack during this turn. |
| OP02-111 | Fullbody | [When Attacking] ⚠embedded-condition | If you have [Jango], this card gains +3000 power during this battle. |
| OP02-113 | Helmeppo | [When Attacking] ⚠targeting-qualifier | Give up to 1 of your opponent's Characters −2 cost during this turn. Then, if there is a Character with a cost of 0, this Character gains +2000 power during … |
| OP02-115 | Monkey.D.Garp | [DON!! x2] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 0. |
| OP02-117 | Ice Age | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP02-118 | Yasakani Sacred Jewel | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Stages with a cost of 3 or less. |
| OP02-121 | Kuzan | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 0. |
| OP03-012 | Marshall.D.Teach | [When Attacking] ⚠targeting-qualifier | You may trash 1 of your red Characters with 4000 power or more: Draw 1 card. Then, this Character gains +1000 power during this battle. |
| OP03-013 | Marco | [Your Turn] [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 3000 power or less. |
| OP03-014 | Monkey.D.Garp | [When Attacking] ⚠targeting-qualifier | Play up to 1 red Character card with a cost of 1 from your hand. |
| OP03-016 | Flame Emperor | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 6000 power or less. |
| OP03-018 | Fire Fist | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 5000 power or less. |
| OP03-020 | Striker | [Activate: Main] ⚠embedded-condition | ② (You may rest the specified number of DON!! cards in your cost area.) You may rest this Stage: If your Leader is [Portgas.D.Ace], look at 5 cards from the … |
| OP03-021 | Kuro | [Activate: Main] ⚠targeting-qualifier | ③ (You may rest the specified number of DON!! cards in your cost area.) You may rest 2 of your {East Blue} type Characters: Set this Leader as active, and re… |
| OP03-022 | Arlong | [DON!! x2] [When Attacking] ⚠targeting-qualifier | ① (You may rest the specified number of DON!! cards in your cost area.): Play up to 1 Character card with a cost of 4 or less and a [Trigger] from your hand. |
| OP03-024 | Gin | [On Play] ⚠targeting-qualifier | If your Leader has the {East Blue} type, rest up to 2 of your opponent's Characters with a cost of 4 or less. |
| OP03-025 | Krieg | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: K.O. up to 2 of your opponent's rested Characters with a cost of 4 or less. |
| OP03-026 | Kuroobi | [On Play] ⚠embedded-condition | If your Leader has the {East Blue} type, rest up to 1 of your opponent's Characters. |
| OP03-027 | Sham | [On Play] ⚠targeting-qualifier | If your Leader has the {East Blue} type, rest up to 1 of your opponent's Characters with a cost of 2 or less and, if you don't have [Buchi], play up to 1 [Bu… |
| OP03-029 | Chew | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 4 or less. |
| OP03-033 | Hatchan | [Trigger] ⚠embedded-condition | If your Leader has the {East Blue} type, play this card. |
| OP03-034 | Buchi | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 2 or less. |
| OP03-036 | Out-of-the-Bag | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 3 or less. |
| OP03-037 | Tooth Attack | [Trigger] ⚠targeting-qualifier | Play up to 1 Character card with a cost of 4 or less and a [Trigger] from your hand. |
| OP03-038 | Deathly Poison Gas Bomb MH5 | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP03-039 | One, Two, Jango | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP03-047 | Zeff | [On Play] ⚠targeting-qualifier | Return up to 1 Character with a cost of 3 or less to the owner's hand, and you may trash 2 cards from the top of your deck. |
| OP03-048 | Nojiko | [On Play] ⚠targeting-qualifier | If your Leader is [Nami], return up to 1 of your opponent's Characters with a cost of 5 or less to the owner's hand. |
| OP03-049 | Patty | [On Play] ⚠targeting-qualifier | If you have 20 or less cards in your deck, return up to 1 Character with a cost of 3 or less to the owner's hand. |
| OP03-055 | Gum-Gum Giant Gavel | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 4 or less to the owner's hand. |
| OP03-057 | Three Thousand Worlds | [Trigger] ⚠targeting-qualifier | Place up to 1 Character with a cost of 3 or less at the bottom of the owner's deck. |
| OP03-058 | Iceburg | [Activate: Main] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.) You may rest this Leader: Play up to 1 {Galley-La Company} … |
| OP03-063 | Zambai | [On Play] ⚠embedded-condition | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {Water Seven} type, draw 1 card. |
| OP03-064 | Tilestone | [On K.O.] ⚠embedded-condition | If your Leader has the {Galley-La Company} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP03-066 | Paulie | [On Play] ⚠targeting-qualifier | ➁ (You may rest the specified number of DON!! cards in your cost area.): Add up to 1 DON!! card from your DON!! deck and set it as active. Then, if you have … |
| OP03-067 | Peepley Lulu | [DON!! x1] [When Attacking] ⚠embedded-condition | If your Leader has the {Galley-La Company} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP03-068 | Minozebra | [On K.O.] ⚠embedded-condition | If your Leader has the {Impel Down} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP03-069 | Minorhinoceros | [On K.O.] ⚠embedded-condition | If your Leader has the {Impel Down} type, draw 2 cards and trash 1 card from your hand. |
| OP03-070 | Monkey.D.Luffy | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.) You may trash 1 Character card with a cost of 5 from your h… |
| OP03-071 | Rob Lucci | [When Attacking] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Rest up to 1 of your opponent's Characters with a cost of … |
| OP03-075 | Galley-La Company | [Activate: Main] ⚠embedded-condition | You may rest this Stage: If your Leader is [Iceburg], add up to 1 DON!! card from your DON!! deck and rest it. |
| OP03-077 | Charlotte Linlin | [DON!! x2] [When Attacking] ⚠embedded-condition | ② (You may rest the specified number of DON!! cards in your cost area.) You may trash 1 card from your hand: If you have 1 or less Life cards, add up to 1 ca… |
| OP03-078 | Issho | [On Play] ⚠embedded-condition | If your opponent has 6 or more cards in their hand, trash 2 cards from your opponent's hand. |
| OP03-080 | Kaku | [On Play] ⚠targeting-qualifier | You may place 2 cards with a type including "CP" from your trash at the bottom of your deck in any order: K.O. up to 1 of your opponent's Characters with a c… |
| OP03-086 | Spandam | [On Play] ⚠embedded-condition | If your Leader's type includes "CP", look at 3 cards from the top of your deck; reveal up to 1 card with a type including "CP" other than [Spandam] and add i… |
| OP03-093 | Wanze | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: If your Leader's type includes "CP", K.O. up to 1 of your opponent's Characters with a cost of 1 or less. |
| OP03-094 | Air Door | [Trigger] ⚠targeting-qualifier | Play up to 1 black Character card with a cost of 3 or less from your trash. |
| OP03-097 | Six King Pistol | [Trigger] ⚠targeting-qualifier | Draw 1 card. Then, K.O. up to 1 of your opponent's Characters with a cost of 1 or less. |
| OP03-098 | Enies Lobby | [Activate: Main] ⚠embedded-condition | You may rest this Stage: If your Leader's type includes "CP", give up to 1 of your opponent's Characters −2 cost during this turn. |
| OP03-114 | Charlotte Linlin | [On Play] ⚠embedded-condition | If your Leader has the {Big Mom Pirates} type, add up to 1 card from the top of your deck to the top of your Life cards. Then, trash up to 1 card from the to… |
| OP03-115 | Streusen | [On Play] ⚠targeting-qualifier | You may trash 1 card with a [Trigger] from your hand: K.O. up to 1 of your opponent's Characters with a cost of 1 or less. |
| OP03-119 | Buzz Cut Mochi | [Trigger] ⚠targeting-qualifier | Play up to 1 Character card with a cost of 4 or less and a [Trigger] from your hand. |
| OP03-121 | Thunder Bolt | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP03-122 | Sogeking | [On Play] ⚠targeting-qualifier | Return up to 1 Character with a cost of 6 or less to the owner's hand. Then, draw 2 cards and trash 2 cards from your hand. |
| OP04-008 | Chaka | [DON!! x1] [When Attacking] ⚠embedded-condition | If your Leader is [Nefeltari Vivi], give up to 1 of your opponent's Characters −3000 power during this turn. Then, K.O. up to 1 of your opponent's Characters… |
| OP04-010 | Tony Tony.Chopper | [On Play] ⚠targeting-qualifier | Play up to 1 {Animal} type Character card with 3000 power or less from your hand. |
| OP04-011 | Nami | [When Attacking] ⚠targeting-qualifier | Reveal 1 card from the top of your deck. If the revealed card is a Character card with 6000 power or more, this Character gains +3000 power during this turn.… |
| OP04-013 | Pell | [DON!! x1] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 4000 power or less. |
| OP04-020 | Issho | [End of Your Turn] ⚠targeting-qualifier | ➀ (You may rest the specified number of DON!! cards in your cost area.): Set up to 1 of your Characters with a cost of 5 or less as active. |
| OP04-022 | Eric | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: Rest up to 1 of your opponent's Characters with a cost of 1 or less. |
| OP04-024 | Sugar | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP04-025 | Giolla | [On Your Opponent's Attack] ⚠targeting-qualifier | ➁ (You may rest the specified number of DON!! cards in your cost area.): Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP04-026 | Senor Pink | [When Attacking] ⚠targeting-qualifier | ➀ (You may rest the specified number of DON!! cards in your cost area.): If your Leader has the {Donquixote Pirates} type, rest up to 1 of your opponent's Ch… |
| OP04-028 | Diamante | [DON!! x1] [End of Your Turn] ⚠embedded-condition | If you have 2 or more active DON!! cards, set this Character as active. |
| OP04-030 | Trebol | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 5 or less. |
| OP04-030 | Trebol | [On Your Opponent's Attack] ⚠targeting-qualifier | ➁ (You may rest the specified number of DON!! cards in your cost area.): Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP04-033 | Machvise | [On Play] ⚠targeting-qualifier | If your Leader has the {Donquixote Pirates} type, rest up to 1 of your opponent's Characters with a cost of 5 or less. Then, set up to 1 of your DON!! cards … |
| OP04-034 | Lao.G | [End of Your Turn] ⚠targeting-qualifier | If you have 3 or more active DON!! cards, K.O. up to 1 of your opponent's rested Characters with a cost of 3 or less. |
| OP04-037 | Flapping Thread | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 4 or less. |
| OP04-039 | Rebecca | [Activate: Main] [Once Per Turn] ⚠embedded-condition | ➀ (You may rest the specified number of DON!! cards in your cost area.): If you have 6 or less cards in your hand, look at 2 cards from the top of your deck;… |
| OP04-040 | Queen | [DON!! x1] [When Attacking] ⚠targeting-qualifier | If you have a total of 4 or less cards in your Life area and hand, draw 1 card. If you have a Character with a cost of 8 or more, you may add up to 1 card fr… |
| OP04-043 | Ulti | [DON!! x1] [When Attacking] ⚠targeting-qualifier | Return up to 1 Character with a cost of 2 or less to the owner's hand or the bottom of their deck. |
| OP04-044 | Kaido | [On Play] ⚠targeting-qualifier | Return up to 1 Character with a cost of 8 or less and up to 1 Character with a cost of 3 or less to the owner's hand. |
| OP04-046 | Queen | [On Play] ⚠embedded-condition | If your Leader has the {Animal Kingdom Pirates} type, look at 7 cards from the top of your deck; reveal a total of up to 2 [Plague Rounds] or [Ice Oni] cards… |
| OP04-056 | Gum-Gum Red Roc | [Trigger] ⚠targeting-qualifier | Place up to 1 Character with a cost of 4 or less at the bottom of the owner's deck. |
| OP04-057 | Dragon Twister Demolition Breath | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 6 or less to the owner's hand. |
| OP04-059 | Iceburg | [On Your Opponent's Attack] ⚠embedded-condition | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {Water Seven} type, this Character … |
| OP04-060 | Crocodile | [On Play] ⚠embedded-condition | DON!! −2 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader's type includes "Baroque Works", add up to … |
| OP04-061 | Tom | [Activate: Main] ⚠embedded-condition | You may trash this Character: If your Leader has the {Water Seven} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP04-063 | Franky | [On Your Opponent's Attack] [Once Per Turn] ⚠embedded-condition | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {Water Seven} type, up to 1 of your… |
| OP04-064 | Ms. All Sunday | [On Play] ⚠embedded-condition | Add up to 1 DON!! card from your DON!! deck and rest it. Then, if you have 6 or more DON!! cards on your field, draw 1 card. |
| OP04-065 | Miss.Goldenweek(Marianne) | [On Play] ⚠targeting-qualifier | If your Leader's type includes "Baroque Works", up to 1 of your opponent's Characters with a cost of 5 or less cannot attack until the start of your next turn. |
| OP04-068 | Yokozuna | [On Your Opponent's Attack] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Return up to 1 of your opponent's Characters with a cost o… |
| OP04-072 | Mr.5(Gem) | [On Your Opponent's Attack] [Once Per Turn] ⚠targeting-qualifier | DON!! −2 (You may return the specified number of DON!! cards from your field to your DON!! deck.) You may rest this Character: K.O. up to 1 of your opponent'… |
| OP04-081 | Cavendish | [When Attacking] ⚠targeting-qualifier | You may rest your Leader: K.O. up to 1 of your opponent's Characters with a cost of 1 or less. Then, trash 2 cards from the top of your deck. |
| OP04-082 | Kyros | [On Play] ⚠targeting-qualifier | If your Leader is [Rebecca], K.O. up to 1 of your opponent's Characters with a cost of 1 or less. Then, trash 1 card from the top of your deck. |
| OP04-085 | Suleiman | [On Play] [When Attacking] ⚠embedded-condition | If your Leader has the {Dressrosa} type, give up to 1 of your opponent's Characters −2 cost during this turn. Then, trash 1 card from the top of your deck. |
| OP04-091 | Leo | [On Play] ⚠targeting-qualifier | You may rest your 1 Leader: If your Leader has the {Dressrosa} type, K.O. up to 1 of your opponent's Characters with a cost of 1 or less. Then, trash 2 cards… |
| OP04-094 | Trueno Bastardo | [Trigger] ⚠targeting-qualifier | You may rest your Leader: K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP04-098 | Toko | [On Play] ⚠embedded-condition | You may trash 2 {Land of Wano} type cards from your hand: If you have 1 or less Life cards, add 1 card from the top of your deck to the top of your Life cards. |
| OP04-099 | Olin | [Trigger] ⚠embedded-condition | If you have 1 or less Life cards, play this card. |
| OP04-101 | Carmel | [Trigger] ⚠targeting-qualifier | Play this card. Then, K.O. up to 1 of your opponent's Characters with a cost of 2 or less. |
| OP04-105 | Charlotte Amande | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may trash 1 card with a [Trigger] from your hand: Rest up to 1 of your opponent's Characters with a cost of 2 or less. |
| OP04-112 | Yamato | [On Play] ⚠embedded-condition | K.O. up to 1 of your opponent's Characters with a cost equal to or less than the total of your and your opponent's Life cards. Then, if you have 1 or less Li… |
| OP04-119 | Donquixote Rosinante | [On Play] ⚠targeting-qualifier | You may rest this Character: Play up to 1 green Character card with a cost of 5 from your hand. |
| OP05-004 | Emporio.Ivankov | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | If this Character has 7000 power or more, play up to 1 {Revolutionary Army} type Character card with 5000 power or less other than [Emporio.Ivankov] from you… |
| OP05-005 | Karasu | [On Play] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type, give up to 1 of your opponent's Leader or Character cards −1000 power during this turn. |
| OP05-005 | Karasu | [When Attacking] ⚠embedded-condition | If this Character has 7000 power or more, give up to 1 of your opponent's Leader or Character cards −1000 power during this turn. |
| OP05-006 | Koala | [On Play] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type, give up to 1 of your opponent's Characters −3000 power during this turn. |
| OP05-009 | Toh-Toh | [On Play] ⚠embedded-condition | Draw 1 card if your Leader has 0 power or less. |
| OP05-010 | Nico Robin | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 1000 power or less. |
| OP05-011 | Bartholomew Kuma | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 2000 power or less. |
| OP05-011 | Bartholomew Kuma | [Trigger] ⚠embedded-condition | If your Leader is multicolored, play this card. |
| OP05-016 | Morley | [When Attacking] ⚠embedded-condition | If this Character has 7000 power or more, your opponent cannot activate [Blocker] during this battle. |
| OP05-016 | Morley | [Trigger] ⚠embedded-condition | You may trash 1 card from your hand: If your Leader is multicolored, play this card. |
| OP05-017 | Lindbergh | [When Attacking] ⚠targeting-qualifier | If this Character has 7000 power or more, K.O. up to 1 of your opponent's Characters with 3000 power or less. |
| OP05-017 | Lindbergh | [Trigger] ⚠embedded-condition | You may trash 1 card from your hand: If your Leader is multicolored, play this card. |
| OP05-018 | Emporio Energy Hormone | [Trigger] ⚠targeting-qualifier | Play up to 1 {Revolutionary Army} type Character card with 5000 power or less from your hand. |
| OP05-022 | Donquixote Rosinante | [End of Your Turn] ⚠embedded-condition | If you have 6 or less cards in your hand, set this Leader as active. |
| OP05-023 | Vergo | [DON!! x1] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 3 or less. |
| OP05-025 | Gladius | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: Rest up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP05-026 | Sarquiss | [DON!! x1] [When Attacking] [Once Per Turn] ⚠targeting-qualifier | You may rest 1 of your Characters with a cost of 3 or more: Set this Character as active. |
| OP05-027 | Trafalgar Law | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Rest up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP05-028 | Donquixote Doflamingo | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: K.O. up to 1 of your opponent's rested Characters with a cost of 2 or less. |
| OP05-029 | Donquixote Doflamingo | [On Your Opponent's Attack] [Once Per Turn] ⚠targeting-qualifier | ➀ (You may rest the specified number of DON!! cards in your cost area.): Rest up to 1 of your opponent's Characters with a cost of 6 or less. |
| OP05-031 | Buffalo | [When Attacking] [Once Per Turn] ⚠targeting-qualifier | If you have 2 or more rested Characters, set up to 1 of your rested Characters with a cost of 1 as active. |
| OP05-033 | Baby 5 | [Activate: Main] ⚠targeting-qualifier | ➀ (You may rest the specified number of DON!! cards in your cost area.) You may rest this Character: Play up to 1 {Donquixote Pirates} type Character card wi… |
| OP05-036 | Monet | [On Block] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP05-037 | Because the Side of Justice Will Be Whichever Side Wins!! | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP05-038 | Charlestone | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Leader or Character cards with a cost of 3 or less. |
| OP05-039 | Stick-Stickem Meteora | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 5 or less. |
| OP05-040 | Birdcage | [End of Your Turn] ⚠targeting-qualifier | If you have 10 DON!! cards on your field, K.O. all rested Characters with a cost of 5 or less. Then, trash this Stage. |
| OP05-042 | Issho | [On Play] ⚠targeting-qualifier | Up to 1 of your opponent's Characters with a cost of 7 or less cannot attack until the start of your next turn. |
| OP05-043 | Ulti | [On Play] ⚠embedded-condition | If your Leader is multicolored, look at 3 cards from the top of your deck and add up to 1 card to your hand. Then, place the rest at the top or bottom of the… |
| OP05-045 | Stainless | [Activate: Main] ⚠targeting-qualifier | You may trash 1 card from your hand and rest this Character: Place up to 1 Character with a cost of 2 or less at the bottom of the owner's deck. |
| OP05-047 | Basil Hawkins | [On Block] ⚠embedded-condition | Draw 1 card if you have 3 or less cards in your hand. Then, this Character gains +1000 power during this battle. |
| OP05-048 | Bastille | [DON!! x1] [When Attacking] ⚠targeting-qualifier | Place up to 1 Character with a cost of 2 or less at the bottom of the owner's deck. |
| OP05-049 | Haccha | [DON!! x1] [When Attacking] ⚠targeting-qualifier | Return up to 1 Character with a cost of 3 or less to the owner's hand. |
| OP05-050 | Hina | [On Play] ⚠embedded-condition | Draw 1 card if you have 5 or less cards in your hand. |
| OP05-051 | Borsalino | [On Play] ⚠targeting-qualifier | Place up to 1 Character with a cost of 4 or less at the bottom of the owner's deck. |
| OP05-057 | Hound Blaze | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 3 or less to the owner's hand. |
| OP05-059 | Let Us Begin the World of Violence!!! | [Trigger] ⚠embedded-condition | If your Leader is multicolored, draw 2 cards. |
| OP05-060 | Monkey.D.Luffy | [Activate: Main] [Once Per Turn] ⚠embedded-condition | You may add 1 card from the top of your Life cards to your hand: If you have 0 or 3 or more DON!! cards on your field, add up to 1 DON!! card from your DON!!… |
| OP05-061 | Uso-Hachi | [DON!! x1] [When Attacking] ⚠targeting-qualifier | If you have 8 or more DON!! cards on your field, rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP05-063 | O-Robi | [On Play] ⚠targeting-qualifier | If you have 8 or more DON!! cards on your field, K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP05-067 | Zoro-Juurou | [When Attacking] ⚠embedded-condition | If you have 3 or less Life cards, add up to 1 DON!! card from your DON!! deck and set it as active. |
| OP05-068 | Chopa-Emon | [On Play] ⚠targeting-qualifier | If you have 8 or more DON!! cards on your field, set up to 1 of your purple {Straw Hat Crew} type Characters with 6000 power or less as active. |
| OP05-069 | Trafalgar Law | [When Attacking] ⚠embedded-condition | If your opponent has more DON!! cards on their field than you, look at 5 cards from the top of your deck; reveal up to 1 {Heart Pirates} type card and add it… |
| OP05-071 | Bepo | [When Attacking] ⚠embedded-condition | If your opponent has more DON!! cards on their field than you, give up to 1 of your opponent's Characters −2000 power during this turn. |
| OP05-072 | Hone-Kichi | [On Play] ⚠embedded-condition | If you have 8 or more DON!! cards on your field, give up to 2 of your opponent's Characters −2000 power during this turn. |
| OP05-075 | Mr.1(Daz.Bonez) | [On Your Opponent's Attack] [Once Per Turn] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play up to 1 {Baroque Works} type Character card with a co… |
| OP05-082 | Shirahoshi | [Activate: Main] ⚠embedded-condition | You may rest this Character and place 2 cards from your trash at the bottom of your deck in any order: If your opponent has 6 or more cards in their hand, yo… |
| OP05-088 | Mansherry | [Activate: Main] ⚠targeting-qualifier | ➀ (You may rest the specified number of DON!! cards in your cost area.) You may rest this Character and place 2 cards from your trash at the bottom of your d… |
| OP05-089 | Saint Mjosgard | [Activate: Main] ⚠targeting-qualifier | ➀ (You may rest the specified number of DON!! cards in your cost area.) You may rest this Character and 1 of your Characters: Add up to 1 black Character car… |
| OP05-091 | Rebecca | [On Play] ⚠targeting-qualifier | Add up to 1 black Character card with a cost of 3 to 7 other than [Rebecca] from your trash to your hand. Then, play up to 1 black Character card with a cost… |
| OP05-093 | Rob Lucci | [On Play] ⚠targeting-qualifier | You may place 3 cards from your trash at the bottom of your deck in any order: K.O. up to 1 of your opponent's Characters with a cost of 2 or less and up to … |
| OP05-096 | I Bid 500 Million!! | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 6 or less, or return it to the owner's hand. |
| OP05-103 | Kotori | [On Play] ⚠embedded-condition | If you have [Hotori], K.O. up to 1 of your opponent's Characters with a cost equal to or less than the number of your opponent's Life cards. |
| OP05-112 | Captain McKinley | [On K.O.] ⚠targeting-qualifier | Play up to 1 {Sky Island} type Character card with a cost of 1 from your hand. |
| OP05-118 | Kaido | [On Play] ⚠embedded-condition | Draw 4 cards if your opponent has 3 or less Life cards. |
| OP06-003 | Emporio.Ivankov | [On Play] ⚠targeting-qualifier | Look at 3 cards from the top of your deck and play up to 1 {Revolutionary Army} type Character card with 5000 power or less. Then, place the rest at the bott… |
| OP06-007 | Shanks | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 10000 power or less. |
| OP06-015 | Lily Carnation | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may trash 1 of your Characters with 6000 power or more: Play up to 1 {FILM} type Character card with 2000 to 5000 power from your trash rested. |
| OP06-018 | Gum-Gum King Kong Gatling | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 5000 power or less. |
| OP06-019 | Blue Dragon Seal Water Stream | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 4000 power or less. |
| OP06-020 | Hody Jones | [Activate: Main] ⚠targeting-qualifier | You may rest this Leader: Rest up to 1 of your opponent's DON!! cards or Characters with a cost of 3 or less. Then, you cannot add Life cards to your hand us… |
| OP06-022 | Yamato | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your opponent has 3 or less Life cards, give up to 2 rested DON!! cards to 1 of your Characters. |
| OP06-023 | Arlong | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP06-024 | Ikaros Much | [On Play] ⚠targeting-qualifier | If your Leader has the {New Fish-Man Pirates} type, play up to 1 {Fish-Man} type Character card with a cost of 4 or less from your hand. Then, add 1 card fro… |
| OP06-026 | Koushirou | [On Play] ⚠targeting-qualifier | Set up to 1 of your ＜Slash＞ attribute Characters with a cost of 4 or less as active. Then, you cannot attack a Leader during this turn. |
| OP06-027 | Gyro | [On K.O.] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP06-028 | Zeo | [DON!! x1] [When Attacking] ⚠embedded-condition | If your Leader has the {New Fish-Man Pirates} type, set up to 1 of your DON!! cards as active and this Character gains +1000 power during this turn. Then, ad… |
| OP06-029 | Daruma | [DON!! x1] [When Attacking] [Once Per Turn] ⚠embedded-condition | If your Leader has the {New Fish-Man Pirates} type, set this Character as active and this Character gains +1000 power during this turn. Then, add 1 card from… |
| OP06-030 | Dosun | [When Attacking] ⚠embedded-condition | If your Leader has the {New Fish-Man Pirates} type, this Character cannot be K.O.'d in battle and gains +2000 power until the start of your next turn. Then, … |
| OP06-031 | Hatchan | [Trigger] ⚠targeting-qualifier | Play up to 1 {Fish-Man} or {Merfolk} type Character card with a cost of 3 or less from your hand. |
| OP06-034 | Hyouzou | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less and this Character gains +1000 power during this turn. Then, add 1 card from the top of y… |
| OP06-036 | Ryuma | [On Play] [On K.O.] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 4 or less. |
| OP06-038 | The Billion-fold World Trichiliocosm | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 3 or less. |
| OP06-043 | Aramaki | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may trash 1 card from your hand and place 1 Character with a cost of 2 or less at the bottom of the owner's deck: This Character gains +3000 power during… |
| OP06-046 | Sakazuki | [On Play] ⚠targeting-qualifier | Place up to 1 Character with a cost of 2 or less at the bottom of the owner's deck. |
| OP06-053 | Jaguar.D.Saul | [On K.O.] ⚠targeting-qualifier | Place up to 1 Character with a cost of 2 or less at the bottom of the owner's deck. |
| OP06-055 | Monkey.D.Garp | [DON!! x2] [When Attacking] ⚠embedded-condition | If you have 4 or less cards in your hand, your opponent cannot activate [Blocker] during this battle. |
| OP06-057 | But I Will Never Doubt a Woman's Tears!!!! | [Trigger] ⚠targeting-qualifier | Play up to 1 Character card with a cost of 2 from your hand. |
| OP06-058 | Gravity Blade Raging Tiger | [Trigger] ⚠targeting-qualifier | Place up to 1 Character with a cost of 5 or less at the bottom of the owner's deck. |
| OP06-060 | Vinsmoke Ichiji | [Activate: Main] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.) You may trash this Character: If your Leader has the {GERMA… |
| OP06-061 | Vinsmoke Ichiji | [On Play] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, give up to 1 of your opponent's Characters −2000 pow… |
| OP06-062 | Vinsmoke Judge | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.) You may trash 2 cards from your hand: Play up to 4 {GERMA 6… |
| OP06-063 | Vinsmoke Sora | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up to 1 {Th… |
| OP06-064 | Vinsmoke Niji | [Activate: Main] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.) You may trash this Character: If your Leader has the {GERMA… |
| OP06-065 | Vinsmoke Niji | [On Play] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, choose one: |
| OP06-066 | Vinsmoke Yonji | [Activate: Main] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.) You may trash this Character: If your Leader has the {GERMA… |
| OP06-068 | Vinsmoke Reiju | [Activate: Main] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.) You may trash this Character: If your Leader has the {GERMA… |
| OP06-069 | Vinsmoke Reiju | [On Play] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field and you have 5 or less cards in your hand, draw 2 cards. |
| OP06-071 | Gild Tesoro | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {FILM} type, add up to 2 {FILM} typ… |
| OP06-073 | Shiki | [On Play] ⚠embedded-condition | If you have 8 or more DON!! cards on your field, draw 1 card and trash 1 card from your hand. |
| OP06-075 | Count Battler | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Rest up to 2 of your opponent's Characters with a cost of … |
| OP06-077 | Black Bug | [Trigger] ⚠targeting-qualifier | Place up to 1 of your opponent's Characters with a cost of 4 or less at the bottom of the owner's deck. |
| OP06-080 | Gecko Moria | [DON!! x1] [When Attacking] ⚠targeting-qualifier | ➁ (You may rest the specified number of DON!! cards in your cost area.) You may trash 1 card from your hand: Trash 2 cards from the top of your deck and play… |
| OP06-082 | Inuppe | [On Play] [On K.O.] ⚠embedded-condition | If your Leader has the {Thriller Bark Pirates} type, draw 2 cards and trash 2 cards from your hand. |
| OP06-091 | Victoria Cindry | [On Play] ⚠embedded-condition | If your Leader has the {Thriller Bark Pirates} type, trash 5 cards from the top of your deck. |
| OP06-093 | Perona | [On Play] ⚠embedded-condition | If your opponent has 5 or more cards in their hand, choose one: |
| OP06-098 | Thriller Bark | [Activate: Main] ⚠targeting-qualifier | ➀ (You may rest the specified number of DON!! cards in your cost area.) You may rest this Stage: If your Leader has the {Thriller Bark Pirates} type, play up… |
| OP06-100 | Inuarashi | [Trigger] ⚠embedded-condition | If your opponent has 3 or less Life cards, play this card. |
| OP06-101 | O-Nami | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP06-102 | Kamakiri | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may place 1 Stage with a cost of 1 at the bottom of the owner's deck: K.O. up to 1 of your opponent's Characters with a cost of 2 or less. |
| OP06-102 | Kamakiri | [Trigger] ⚠embedded-condition | If you have 2 or less Life cards, play this card. |
| OP06-103 | Kawamatsu | [Trigger] ⚠embedded-condition | If your opponent has 3 or less Life cards, play this card. |
| OP06-104 | Kikunojo | [On K.O.] ⚠embedded-condition | If your opponent has 3 or less Life cards, add up to 1 card from the top of your deck to the top of your Life cards. |
| OP06-104 | Kikunojo | [Trigger] ⚠embedded-condition | If your opponent has 3 or less Life cards, play this card. |
| OP06-109 | Denjiro | [Trigger] ⚠embedded-condition | If your opponent has 3 or less Life cards, play this card. |
| OP06-110 | Nekomamushi | [Trigger] ⚠embedded-condition | If your opponent has 3 or less Life cards, play this card. |
| OP06-111 | Braham | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may place 1 Stage with a cost of 1 at the bottom of the owner's deck: Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP06-111 | Braham | [Trigger] ⚠embedded-condition | If you have 2 or less Life cards, play this card. |
| OP06-112 | Raizo | [Trigger] ⚠embedded-condition | If your opponent has 3 or less Life cards, play this card. |
| OP06-114 | Wyper | [On Play] ⚠targeting-qualifier | You may place 1 Stage with a cost of 1 at the bottom of the owner's deck: Look at 5 cards from the top of your deck; reveal up to 1 [Upper Yard] or {Shandian… |
| OP06-115 | You're the One Who Should Disappear. | [Trigger] ⚠embedded-condition | If you have 0 Life cards, you may add up to 1 card from the top of your deck to the top of your Life cards. Then, trash 1 card from your hand. |
| OP06-119 | Sanji | [On Play] ⚠targeting-qualifier | Reveal 1 card from the top of your deck and play up to 1 Character with a cost of 9 or less other than [Sanji]. Then, place the rest at the bottom of your deck. |
| OP07-004 | Curly.Dadan | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: Look at 5 cards from the top of your deck; reveal up to 1 Character card with 2000 power or less and add it to your hand… |
| OP07-009 | Dogura & Magura | [On Play] ⚠targeting-qualifier | Up to 1 of your red Characters with a cost of 1 gains [Double Attack] during this turn. |
| OP07-011 | Bluejam | [DON!! x1] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 2000 power or less. |
| OP07-013 | Masked Deuce | [On Play] ⚠embedded-condition | If your Leader is [Portgas.D.Ace], look at 5 cards from the top of your deck; reveal up to 1 [Portgas.D.Ace] or red Event and add it to your hand. Then, plac… |
| OP07-020 | Aladine | [On K.O.] ⚠targeting-qualifier | If your Leader has the {Fish-Man} type, play up to 1 {Fish-Man} or {Merfolk} type Character card with a cost of 3 or less from your hand. |
| OP07-024 | Koala | [On Your Opponent's Attack] ⚠targeting-qualifier | You may rest this Character: Up to 1 of your {Fish-Man} type Characters with a cost of 5 or less gains [Blocker] during this turn. |
| OP07-025 | Coribou | [On Play] ⚠targeting-qualifier | Play up to 1 [Caribou] with a cost of 4 or less from your hand rested. |
| OP07-032 | Fisher Tiger | [On Play] ⚠targeting-qualifier | If your Leader has the {Fish-Man} or {Merfolk} type, rest up to 1 of your opponent's Characters with a cost of 6 or less. |
| OP07-034 | Roronoa Zoro | [When Attacking] ⚠embedded-condition | If you have 3 or more Characters, this Character gains +2000 power during this turn. |
| OP07-035 | Karmic Punishment | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 4 or less. |
| OP07-036 | Demonic Aura Nine-Sword Style Asura Demon Nine Flash | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP07-040 | Crocodile | [On Play] ⚠targeting-qualifier | ① (You may rest the specified number of DON!! cards in your cost area.): Return up to 1 Character with a cost of 2 or less to the owner's hand. |
| OP07-045 | Jinbe | [On Play] ⚠targeting-qualifier | Play up to 1 {The Seven Warlords of the Sea} type Character card with a cost of 4 or less other than [Jinbe] from your hand. |
| OP07-047 | Trafalgar Law | [Activate: Main] ⚠embedded-condition | You may return this Character to the owner's hand: If your opponent has 6 or more cards in their hand, your opponent places 1 card from their hand at the bot… |
| OP07-048 | Donquixote Doflamingo | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | ➁ (You may rest the specified number of DON!! cards in your cost area.): Reveal 1 card from the top of your deck. If that card is a {The Seven Warlords of th… |
| OP07-049 | Buckin | [On Play] ⚠targeting-qualifier | Play up to 1 [Edward Weevil] with a cost of 4 or less from your hand rested. |
| OP07-050 | Boa Sandersonia | [On Play] ⚠targeting-qualifier | If you have 2 or more {Amazon Lily} or {Kuja Pirates} type Characters on your field, return up to 1 of your opponent's Characters with a cost of 3 or less to… |
| OP07-051 | Boa Hancock | [On Play] ⚠targeting-qualifier | Up to 1 of your opponent's Characters other than [Monkey.D.Luffy] cannot attack until the end of your opponent's next turn. Then, place up to 1 Character wit… |
| OP07-052 | Boa Marigold | [On Play] ⚠targeting-qualifier | If you have 2 or more {Amazon Lily} or {Kuja Pirates} type Characters on your field, place up to 1 Character with a cost of 2 or less at the bottom of the ow… |
| OP07-055 | Snake Dance | [Trigger] ⚠targeting-qualifier | You may return 1 of your Characters to the owner's hand: Return up to 1 of your opponent's Characters with a cost of 5 or less to the owner's hand. |
| OP07-058 | Island of Women | [Activate: Main] ⚠embedded-condition | You may trash 1 card from your hand and rest this Stage: If your Leader has the {Kuja Pirates} type, return up to 1 of your {Amazon Lily} or {Kuja Pirates} t… |
| OP07-059 | Foxy | [When Attacking] ⚠embedded-condition | DON!! −3 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If you have 3 or more {Foxy Pirates} type Characters, sele… |
| OP07-060 | Itomimizu | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your Leader has the {Foxy Pirates} type and you have no other [Itomimizu], add up to 1 DON!! card from your DON!! deck and rest it. |
| OP07-061 | Vinsmoke Sanji | [On Play] ⚠embedded-condition | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {The Vinsmoke Family} type, draw 1 … |
| OP07-062 | Vinsmoke Reiju | [On Play] ⚠targeting-qualifier | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, return up to 1 of your {The Vinsmoke Family} type Ch… |
| OP07-063 | Capote | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {Foxy Pirates} type, up to 1 of you… |
| OP07-065 | Gina | [On Play] ⚠embedded-condition | If your Leader has the {Foxy Pirates} type and the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up t… |
| OP07-066 | Tony Tony.Chopper | [On Play] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up to 1 DON!! card from your DON!! deck and rest… |
| OP07-068 | Hamburg | [DON!! x1] [When Attacking] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up to 1 DON!! card from your DON!! deck and rest… |
| OP07-070 | Big Bun | [On Play] ⚠targeting-qualifier | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, play up to 1 {Foxy Pirates} type card with a cost of… |
| OP07-072 | Porche | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Look at 5 cards from the top of your deck; reveal up to 1 … |
| OP07-073 | Monkey.D.Luffy | [Activate: Main] [Once Per Turn] ⚠embedded-condition | DON!! −3 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your opponent has 3 or more Characters, set this Charac… |
| OP07-074 | Monda | [Activate: Main] ⚠embedded-condition | You may trash this Character: If your Leader has the {Foxy Pirates} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP07-091 | Monkey.D.Luffy | [When Attacking] ⚠targeting-qualifier | Trash up to 1 of your opponent's Characters with a cost of 2 or less. Then, place any number of Character cards with a cost of 4 or more from your trash at t… |
| OP07-092 | Joseph | [On Play] ⚠targeting-qualifier | You may place 2 cards with a type including "CP" from your trash at the bottom of your deck in any order: K.O. up to 1 of your opponent's Characters with a c… |
| OP07-096 | Tempest Kick | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP07-098 | Atlas | [Trigger] ⚠embedded-condition | If your Leader is [Vegapunk], play this card. |
| OP07-100 | Edison | [On Play] ⚠embedded-condition | If you have 2 or less Life cards, draw 2 cards and trash 2 card from your hand. |
| OP07-100 | Edison | [Trigger] ⚠embedded-condition | If your Leader is [Vegapunk], play this card. |
| OP07-101 | Shaka | [Trigger] ⚠embedded-condition | If your Leader is [Vegapunk], play this card. |
| OP07-102 | Jinbe | [Trigger] ⚠targeting-qualifier | Return up to 1 of your opponent's Characters with a cost of 4 or less to the owner's hand and add this card to your hand. |
| OP07-104 | Nico Robin | [Trigger] ⚠embedded-condition | If your Leader has the {Egghead} type, draw 2 cards. |
| OP07-105 | Pythagoras | [On K.O.] ⚠targeting-qualifier | If you have 2 or less Life cards, play up to 1 {Egghead} type Character card with a cost of 4 or less from your trash rested. |
| OP07-105 | Pythagoras | [Trigger] ⚠embedded-condition | If your Leader is [Vegapunk], play this card. |
| OP07-106 | Fuza | [DON!! x1] [When Attacking] ⚠targeting-qualifier | If you have 1 or less Life cards, K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP07-107 | Franky | [Trigger] ⚠embedded-condition | Draw 1 card. Then, if you have 1 or less Life cards, play this card. |
| OP07-109 | Monkey.D.Luffy | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: If you have 2 or less Life cards, K.O. up to 1 of your opponent's Characters with a cost of 4 or less. Then, draw 1 card. |
| OP07-109 | Monkey.D.Luffy | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP07-110 | York | [On Play] ⚠targeting-qualifier | You may add 1 card from the top or bottom of your Life cards to your hand: K.O. up to 1 of your opponent's Characters with a cost of 2 or less. |
| OP07-110 | York | [Trigger] ⚠embedded-condition | If your Leader is [Vegapunk], play this card. |
| OP07-111 | Lilith | [Trigger] ⚠embedded-condition | If your Leader is [Vegapunk], play this card. |
| OP07-112 | Lucy | [When Attacking] [Once Per Turn] ⚠targeting-qualifier | You may add 1 card from the top or bottom of your Life cards to your hand: You may rest up to 1 of your opponent's Characters with a cost of 4 or less. Then,… |
| OP07-113 | Roronoa Zoro | [Trigger] ⚠embedded-condition | If your Leader has the {Egghead} type, rest up to 1 of your opponent's Leader or Character cards. |
| OP07-115 | I Re-Quasar Helllp!! | [Trigger] ⚠targeting-qualifier | Play up to 1 of your {Egghead} type Character cards with a cost of 5 or less from your trash. |
| OP07-116 | Blaze Slice | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP07-117 | Egghead | [End of Your Turn] ⚠targeting-qualifier | If you have 3 or less Life cards, set up to 1 {Egghead} type Character with a cost of 5 or less as active. |
| OP07-118 | Sabo | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: K.O. up to 1 of your opponent's Characters with a cost of 5 or less and up to 1 of your opponent's Characters with a cos… |
| OP07-119 | Portgas.D.Ace | [On Play] ⚠embedded-condition | Add up to 1 card from the top of your deck to the top of your Life cards. Then, if you have 2 or less Life cards, this Character gains [Rush] during this turn. |
| OP08-004 | Kuromarimo | [On Play] ⚠targeting-qualifier | If you have [Chess], K.O. up to 1 of your opponent's Characters with 3000 power or less. |
| OP08-005 | Chess | [On Play] ⚠embedded-condition | Give up to 1 of your opponent's Characters −2000 power during this turn. Then, if you don't have [Kuromarimo], play up to 1 [Kuromarimo] from your hand. |
| OP08-007 | Tony Tony.Chopper | [Your Turn] [On Play] [When Attacking] ⚠targeting-qualifier | Look at 5 cards from the top of your deck and play up to 1 {Animal} type Character card with 4000 power or less rested. Then, place the rest at the bottom of… |
| OP08-012 | Lapins | [DON!! x2] [When Attacking] ⚠targeting-qualifier | If your Leader has the {Drum Kingdom} type, K.O. up to 1 of your opponent's Characters with 4000 power or less. |
| OP08-016 | Dr.Hiriluk | [Activate: Main] ⚠embedded-condition | You may rest this Character: If your Leader is [Tony Tony.Chopper], all of your [Tony Tony.Chopper] Characters gain +2000 power during this turn. |
| OP08-019 | Munch-Munch Mutation | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 5000 power or less. |
| OP08-021 | Carrot | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | If you have a {Minks} type Character, rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP08-022 | Inuarashi | [On Play] ⚠targeting-qualifier | If your Leader has the {Minks} type, up to 2 of your opponent's rested Characters with a cost of 5 or less will not become active in your opponent's next Ref… |
| OP08-023 | Carrot | [On Play] [When Attacking] ⚠targeting-qualifier | Up to 1 of your opponent's rested Characters with a cost of 7 or less will not become active in your opponent's next Refresh Phase. |
| OP08-024 | Concelot | [When Attacking] ⚠targeting-qualifier | Up to 1 of your opponent's rested Characters with a cost of 4 or less will not become active in your opponent's next Refresh Phase. |
| OP08-025 | Shishilian | [On Play] ⚠targeting-qualifier | Up to 1 of your opponent's rested Characters with a cost of 3 or less will not become active in your opponent's next Refresh Phase. |
| OP08-026 | Giovanni | [DON!! x1] [When Attacking] ⚠targeting-qualifier | Up to 1 of your opponent's rested Characters with a cost of 1 or less will not become active in your opponent's next Refresh Phase. |
| OP08-028 | Nekomamushi | [On Play] ⚠embedded-condition | If your opponent has 7 or more rested cards, this Character gains [Rush] during this turn. |
| OP08-032 | Milky | [Activate: Main] ⚠embedded-condition | You may rest this Character: If your Leader has the {Minks} type, set up to 1 of your DON!! cards as active. |
| OP08-033 | Roddy | [On Play] ⚠targeting-qualifier | If your Leader has the {Minks} type and your opponent has 7 or more rested cards, K.O. up to 1 of your opponent's rested Characters with a cost of 2 or less. |
| OP08-038 | We Would Never Sell a Comrade to an Enemy!!! | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP08-039 | Zou | [Activate: Main] ⚠embedded-condition | You may rest this Stage: If your Leader has the {Minks} type, set up to 1 of your DON!! cards as active. |
| OP08-040 | Atmos | [On Play] ⚠targeting-qualifier | You may reveal 2 cards with a type including "Whitebeard Pirates" from your hand: If your Leader's type includes "Whitebeard Pirates", return up to 1 of your… |
| OP08-041 | Aphelandra | [Activate: Main] ⚠targeting-qualifier | You may return this Character to the owner's hand: If your Leader has the {Kuja Pirates} type, place up to 1 of your opponent's Characters with a cost of 1 o… |
| OP08-042 | Edward Weevil | [DON!! x1] [When Attacking] ⚠targeting-qualifier | Return up to 1 Character with a cost of 3 or less to the owner's hand. |
| OP08-043 | Edward.Newgate | [On Play] ⚠embedded-condition | If your Leader's type includes "Whitebeard Pirates" and you have 2 or less Life cards, select all of your opponent's Characters on their field. Until the end… |
| OP08-047 | Jozu | [On Play] ⚠targeting-qualifier | You may return 1 of your Characters other than this Character to the owner's hand: Return up to 1 Character with a cost of 6 or less to the owner's hand. |
| OP08-049 | Speed Jil | [On Play] ⚠embedded-condition | Reveal 1 card from the top of your deck and place it at the top or bottom of your deck. If the revealed card's type includes "Whitebeard Pirates", this Chara… |
| OP08-059 | Alber | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: If your Leader has the {Animal Kingdom Pirates} type and you have 10 DON!! cards on your field, play up to 1 [King] with a cost… |
| OP08-060 | King | [On Play] ⚠embedded-condition | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your opponent has 5 or more DON!! cards on their field,… |
| OP08-061 | Charlotte Oven | [When Attacking] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| OP08-062 | Charlotte Katakuri | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: If your Leader has the {Big Mom Pirates} type, play up to 1 [Charlotte Katakuri] from your hand with a cost of 3 or more that i… |
| OP08-069 | Charlotte Linlin | [On Play] ⚠targeting-qualifier | DON!! −1, You may trash 1 card from your hand: Add up to 1 card from the top of your deck to the top of your Life cards. Then, add up to 1 of your opponent's… |
| OP08-070 | Baron Tamago | [On K.O.] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play up to 1 [Viscount Hiyoko] with a cost of 5 or less fr… |
| OP08-071 | Count Niwatori | [Opponent's Turn] [On K.O.] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play up to 1 [Baron Tamago] with a cost of 4 or less from … |
| OP08-073 | Viscount Hiyoko | [Opponent's Turn] [On K.O.] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play up to 1 [Count Niwatori] with a cost of 6 or less fro… |
| OP08-074 | Black Maria | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If you have no other [Black Maria] Characters, add up to 5 DON!! cards from your DON!! deck and rest them. Then, at the end of this turn, return DON!! cards … |
| OP08-079 | Kaido | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may trash 1 card from your hand: If this Character was played on this turn, trash up to 1 of your opponent's Characters with a cost of 7 or less. Then, y… |
| OP08-081 | Guernica | [When Attacking] ⚠targeting-qualifier | You may place 3 cards with a type including "CP" from your trash at the bottom of your deck in any order: K.O. up to 1 of your opponent's Characters with a c… |
| OP08-084 | Jack | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: Draw 1 card and trash 1 card from your hand. Then, K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP08-085 | Jinbe | [DON!! x1] [When Attacking] ⚠targeting-qualifier | If you have a Character with a cost of 8 or more, K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP08-086 | Ginrummy | [On Play] ⚠targeting-qualifier | If your opponent has a Character with a cost of 0, draw 2 cards and trash 2 cards from your hand. |
| OP08-090 | Hamlet | [On Play] ⚠targeting-qualifier | Play up to 1 {SMILE} type Character card with a cost of 2 or less from your trash. |
| OP08-091 | Who's.Who | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP08-091 | Who's.Who | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP08-092 | Page One | [On Play] ⚠targeting-qualifier | Play up to 1 [Ulti] with a cost of 4 or less from your trash. |
| OP08-096 | People's Dreams Don't Ever End!! | [Trigger] ⚠targeting-qualifier | Play up to 1 black Character card with a cost of 3 or less from your trash. |
| OP08-097 | Heliceratops | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP08-098 | Kalgara | [DON!! x1] [When Attacking] ⚠embedded-condition | Play up to 1 {Shandian Warrior} type Character card from your hand with a cost equal to or less than the number of DON!! cards on your field. If you do, add … |
| OP08-101 | Charlotte Angel | [Activate: Main] [Once Per Turn] ⚠embedded-condition | You may trash 1 card from the top of your Life cards: If your Leader has the {Big Mom Pirates} type, add 1 card from the top of your deck to the top of your … |
| OP08-106 | Nami | [On Play] ⚠targeting-qualifier | You may trash 1 card with a [Trigger] from your hand: K.O. up to 1 of your opponent's Characters with a cost of 5 or less. Then, if you have 3 or less cards … |
| OP08-109 | Mont Blanc Noland | [On Play] ⚠embedded-condition | If your Leader has the {Shandian Warrior} type and you have a [Kalgara] Character, add up to 1 card from the top of your deck to the top of your Life cards. |
| OP08-111 | S-Shark | [Trigger] ⚠embedded-condition | You may trash 1 card from your hand: If you have 2 or less Life cards, play this card. |
| OP08-112 | S-Snake | [On Play] ⚠targeting-qualifier | Up to 1 of your opponent's Characters with a cost of 6 or less other than [Monkey.D.Luffy] cannot attack until the end of your opponent's next turn. |
| OP08-113 | S-Bear | [Trigger] ⚠targeting-qualifier | You may trash 1 card from your hand: If you have 2 or less Life cards, play this card and K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP08-114 | S-Hawk | [Trigger] ⚠embedded-condition | You may trash 1 card from your hand: If you have 2 or less Life cards, play this card. |
| OP08-118 | Silvers Rayleigh | [On Play] ⚠targeting-qualifier | Select up to 2 of your opponent's Characters, and give 1 Character −3000 power and the other −2000 power until the end of your opponent's next turn. Then, K.… |
| OP09-005 | Silvers Rayleigh | [On Play] ⚠embedded-condition | If your opponent has 2 or more Characters with a base power of 5000 or more, draw 2 cards and trash 1 card from your hand. |
| OP09-007 | Heat | [On Play] ⚠targeting-qualifier | Up to 1 of your Leader with 4000 power or less gains +1000 power during this turn. |
| OP09-009 | Benn.Beckman | [On Play] ⚠targeting-qualifier | Trash up to 1 of your opponent's Characters with 6000 power or less. |
| OP09-011 | Hongo | [Activate: Main] ⚠embedded-condition | You may rest this Character: If your Leader has the {Red-Haired Pirates} type, give up to 1 of your opponent's Characters −2000 power during this turn. |
| OP09-015 | Lucky.Roux | [On K.O.] ⚠embedded-condition | If your Leader has the {Red-Haired Pirates} type, K.O. up to 1 of your opponent's Characters with a base power of 6000 or less. |
| OP09-021 | Red Force | [Activate: Main] ⚠embedded-condition | You may rest this Stage: If your Leader has the {Red-Haired Pirates} type, give up to 1 of your opponent's Characters −1000 power during this turn. |
| OP09-022 | Lim | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may rest 3 of your DON!! cards: Add up to 1 DON!! card from your DON!! deck and rest it, and play up to 1 {ODYSSEY} type Character card with a cost of 5 … |
| OP09-023 | Adio | [On Play] ⚠embedded-condition | If your Leader has the {ODYSSEY} type, set up to 3 of your DON!! cards as active. |
| OP09-024 | Usopp | [On Play] ⚠embedded-condition | If you have 2 or more rested Characters, draw 2 cards and trash 2 cards from your hand. |
| OP09-026 | Sakazuki | [On Play] ⚠targeting-qualifier | If you have 2 or more rested Characters, K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP09-027 | Sabo | [When Attacking] [Once Per Turn] ⚠embedded-condition | If you have 3 or more rested Characters, draw 1 card. |
| OP09-028 | Sanji | [On K.O.] ⚠targeting-qualifier | You may add 1 card from the top or bottom of your Life cards to your hand: Play up to 1 {ODYSSEY} or {Straw Hat Crew} type Character card with a cost of 4 or… |
| OP09-030 | Trafalgar Law | [On Play] ⚠targeting-qualifier | You may return 1 of your Characters to the owner's hand: Play up to 1 {ODYSSEY} type Character card with a cost of 3 or less other than [Trafalgar Law] from … |
| OP09-031 | Donquixote Doflamingo | [End of Your Turn] ⚠embedded-condition | If you have 2 or more rested Characters, set this Character as active. |
| OP09-033 | Nico Robin | [On Play] ⚠embedded-condition | If you have 2 or more rested Characters, none of your {ODYSSEY} or {Straw Hat Crew} type Characters can be K.O.'d by effects until the end of your opponent's… |
| OP09-035 | Portgas.D.Ace | [On Play] ⚠targeting-qualifier | If you have 2 or more rested Characters, rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP09-036 | Monkey.D.Luffy | [On Play] ⚠targeting-qualifier | If you have 2 or more rested Characters, rest up to 1 of your opponent's DON!! cards or Characters with a cost of 6 or less. |
| OP09-037 | Lim | [End of Your Turn] ⚠embedded-condition | If you have 3 or more rested Characters, set this Character as active. |
| OP09-039 | Gum-Gum Cuatro Jet Cross Shock Bazooka | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 4 or less. |
| OP09-040 | Thunder Lance Flip Caliber Phoenix Shot | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP09-041 | Soul Franky Swing Arm Boxing Solid | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP09-043 | Alvida | [On K.O.] ⚠targeting-qualifier | If your Leader has the {Cross Guild} type, play up to 1 Character card with a cost of 5 or less other than [Alvida] from your hand. |
| OP09-046 | Crocodile | [On Play] ⚠targeting-qualifier | Play up to 1 {Cross Guild} type Character card or Character card with a type including "Baroque Works" with a cost of 5 or less from your hand. |
| OP09-051 | Buggy | [On Play] ⚠targeting-qualifier | Place up to 1 of your opponent's Characters at the bottom of the owner's deck. Then, if you do not have 5 Characters with a cost of 5 or more, place this Cha… |
| OP09-058 | Special Muggy Ball | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 3 or less to the owner's hand. |
| OP09-060 | Emptee Bluffs Island | [Activate: Main] ⚠embedded-condition | You may place 2 cards from your hand at the bottom of your deck in any order and rest this Stage: If your Leader has the {Cross Guild} type, draw 2 cards. |
| OP09-065 | Sanji | [On Play] ⚠targeting-qualifier | You may return 1 or more DON!! cards from your field to your DON!! deck: This Character gains [Rush] during this turn. Then, rest up to 1 of your opponent's … |
| OP09-066 | Jean Bart | [On Play] ⚠targeting-qualifier | If your opponent has more DON!! cards on their field than you, K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP09-069 | Trafalgar Law | [On Play] ⚠targeting-qualifier | Look at 4 cards from the top of your deck; reveal up to 1 {Straw Hat Crew} or {Heart Pirates} type card with a cost of 2 or more and add it to your hand. The… |
| OP09-075 | Eustass"Captain"Kid | [On Play] ⚠embedded-condition | You may add 1 card from the top of your Life cards to your hand: If your Leader has the {Kid Pirates} type, add up to 1 DON!! card from your DON!! deck and s… |
| OP09-083 | Van Augur | [Activate: Main] ⚠embedded-condition | You may rest this Character: If your Leader has the {Blackbeard Pirates} type, give up to 1 of your opponent's Characters −3 cost during this turn. |
| OP09-084 | Catarina Devon | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your Leader has the {Blackbeard Pirates} type, this Character gains [Double Attack], [Banish] or [Blocker] until the end of your opponent's next turn. |
| OP09-085 | Gecko Moria | [On Play] ⚠targeting-qualifier | Play up to 1 {Thriller Bark Pirates} type Character card with a cost of 2 or less from your trash rested. |
| OP09-087 | Charlotte Pudding | [On Play] ⚠embedded-condition | If your opponent has 5 or more cards in their hand, your opponent trashes 1 card from their hand. |
| OP09-089 | Stronger | [Activate: Main] ⚠embedded-condition | You may trash 1 card from your hand and trash this Character: If your Leader has the {Blackbeard Pirates} type, draw 1 card. Then, give up to 1 of your oppon… |
| OP09-090 | Doc Q | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: If your Leader has the {Blackbeard Pirates} type, K.O. up to 1 of your opponent's Characters with a cost of 1 or less. |
| OP09-092 | Marshall.D.Teach | [Activate: Main] ⚠embedded-condition | You may rest this Character: If the number of cards in your hand is at least 3 less than the number in your opponent's hand, draw 2 cards and trash 1 card fr… |
| OP09-093 | Marshall.D.Teach | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your Leader has the {Blackbeard Pirates} type and this Character was played on this turn, negate the effect of up to 1 of your opponent's Leader during th… |
| OP09-100 | Karasu | [Trigger] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type and you and your opponent have a total of 5 or less Life cards, play this card. |
| OP09-102 | Professor Clover | [On Play] ⚠embedded-condition | If your Leader is [Nico Robin], look at 3 cards from the top of your deck; reveal up to 1 card with a [Trigger] and add it to your hand. Then, place the rest… |
| OP09-103 | Koala | [On Play] ⚠targeting-qualifier | You may add 1 card from the top or bottom of your Life cards to your hand: Play up to 1 {Revolutionary Army} type Character card with a cost of 4 or less fro… |
| OP09-104 | Sabo | [On Play] ⚠embedded-condition | Add up to 1 {Revolutionary Army} type Character card from your hand to the top of your Life cards face-up. Then, if you have 2 or more Life cards, add 1 card… |
| OP09-104 | Sabo | [Trigger] ⚠embedded-condition | If your Leader is multicolored, draw 2 cards. |
| OP09-105 | Sanji | [Trigger] ⚠embedded-condition | If your Leader has the {Egghead} type, add up to 1 card from the top of your deck to the top of your Life cards. Then, trash 2 cards from your hand. |
| OP09-106 | Nico Olvia | [Trigger] ⚠embedded-condition | If your Leader is [Nico Robin], draw 3 cards and trash 2 cards from your hand. |
| OP09-107 | Nico Robin | [On Play] ⚠embedded-condition | If your opponent has 3 or more Life cards, trash up to 1 card from the top of your opponent's Life cards. |
| OP09-107 | Nico Robin | [Trigger] ⚠targeting-qualifier | Play up to 1 yellow Character card with a cost of 3 or less from your hand. |
| OP09-108 | Bartholomew Kuma | [Trigger] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type and you and your opponent have a total of 5 or less Life cards, play this card. |
| OP09-109 | Jaguar.D.Saul | [Trigger] ⚠embedded-condition | If your Leader is [Nico Robin], play this card. |
| OP09-111 | Brook | [Trigger] ⚠embedded-condition | If your Leader has the {Egghead} type and your opponent has 6 or more cards in their hand, your opponent trashes 2 cards from their hand. |
| OP09-112 | Belo Betty | [On Play] ⚠embedded-condition | If you have 2 or less Life cards, draw 1 card. |
| OP09-112 | Belo Betty | [Trigger] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type and you and your opponent have a total of 5 or less Life cards, play this card. |
| OP09-114 | Lindbergh | [On Play] ⚠targeting-qualifier | If you and your opponent have a total of 5 or less Life cards, K.O. up to 1 of your opponent's Characters with 2000 power or less. |
| OP09-114 | Lindbergh | [Trigger] ⚠embedded-condition | If you and your opponent have a total of 5 or less Life cards, play this card. |
| OP09-116 | Never Underestimate the Power of Miracles!! | [Trigger] ⚠targeting-qualifier | Play up to 1 {Revolutionary Army} type Character card with a cost of 4 or less from your hand. |
| OP10-001 | Smoker | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | If you have a Character with 7000 power or more, set up to 2 of your DON!! cards as active. |
| OP10-002 | Caesar Clown | [DON!! x2] [When Attacking] ⚠targeting-qualifier | You may return 1 of your {Punk Hazard} type Characters with a cost of 2 or more to the owner's hand: K.O. up to 1 of your opponent's Characters with 4000 pow… |
| OP10-003 | Sugar | [End of Your Turn] ⚠targeting-qualifier | If you have a {Donquixote Pirates} type Character with 6000 power or more, set up to 1 of your DON!! cards as active. |
| OP10-007 | Ceaser Soldier | [On Play] ⚠targeting-qualifier | Play up to 1 {Punk Hazard} type Character card with a cost of 2 or less from your hand. |
| OP10-008 | Scotch | [On Play] ⚠embedded-condition | If you don't have [Rock], play up to 1 [Rock] from your hand. |
| OP10-009 | Smiley | [On Play] ⚠embedded-condition | If your Leader has the {Punk Hazard} type, give up to 1 of your opponent's Characters −3000 power during this turn. |
| OP10-010 | Chadros.Higelyges (Brownbeard) | [When Attacking] ⚠targeting-qualifier | If you have 1 or less Characters with 6000 power or more, this Character gains +1000 power during this turn. |
| OP10-017 | Rock | [On Play] ⚠embedded-condition | If you don't have [Scotch], play up to 1 [Scotch] from your hand. |
| OP10-020 | Gum-Gum UFO | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 3000 power or less. |
| OP10-021 | Punk Hazard | [Activate: Main] ⚠embedded-condition | You may rest this Stage: If your Leader is [Caesar Clown], give up to 1 rested DON!! card to your Leader or 1 of your Characters. |
| OP10-022 | Trafalgar Law | [DON!! x1] [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | If the total cost of your Characters is 5 or more, you may return 1 of your Characters to the owner's hand: Reveal 1 card from the top of your Life cards. If… |
| OP10-023 | Issho | [On Play] ⚠targeting-qualifier | If your Leader has the {Navy} type, rest up to 2 of your opponent's Characters with a cost of 5 or less. |
| OP10-024 | Edward.Newgate | [On Play] ⚠targeting-qualifier | If you have 2 or more rested Characters, rest up to 1 of your opponent's Characters with a cost of 5 or less. Then, K.O. up to 1 of your opponent's rested Ch… |
| OP10-025 | Enel | [On Play] ⚠embedded-condition | If you have 2 or more rested Characters, draw 3 cards and trash 2 cards from your hand. |
| OP10-026 | Kin'emon | [Activate: Main] ⚠targeting-qualifier | You may place this Character and 1 [Kin'emon] with 0 power from your trash at the bottom of your deck in any order: Play up to 1 [Kin'emon] with a cost of 6 … |
| OP10-027 | Kin'emon | [Activate: Main] ⚠targeting-qualifier | You may place this Character and 1 [Kin'emon] with 1000 power from your trash at the bottom of your deck in any order: Play up to 1 [Kin'emon] with a cost of… |
| OP10-029 | Dracule Mihawk | [On Play] ⚠targeting-qualifier | If you have 2 or more rested Characters, set up to 1 of your rested {ODYSSEY} type Characters with a cost of 5 or less as active. |
| OP10-033 | Nami | [On Play] ⚠embedded-condition | If you have 2 or more rested {ODYSSEY} type Characters, up to 1 of your opponent's rested DON!! cards will not become active in your opponent's next Refresh … |
| OP10-035 | Brook | [On K.O.] ⚠targeting-qualifier | Rest up to 1 of your opponent's Leader or Character cards with a cost of 5 or less. |
| OP10-039 | Gum-Gum Dragon Fire Pistol Twister Star | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP10-041 | Radio Knife | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP10-044 | Cub | [On Play] ⚠targeting-qualifier | You may rest 1 of your {Dressrosa} type Leader or Stage cards: Return up to 1 of your opponent's Characters with a cost of 1 or less to the owner's hand. |
| OP10-046 | Kyros | [On Play] ⚠targeting-qualifier | Return up to 1 Character with a cost of 5 or less to the owner's hand. |
| OP10-047 | Koala | [When Attacking] ⚠targeting-qualifier | You may return 1 of your {Revolutionary Army} type Characters with a cost of 3 or more to the owner's hand: This Character gains +3000 power during this turn. |
| OP10-048 | Sai | [On Play] ⚠targeting-qualifier | You may rest 1 of your {Dressrosa} type Leader or Stage cards: Return up to 1 of your opponent's Characters with a cost of 1 or less to the owner's hand. |
| OP10-052 | Bartolomeo | [On Play] ⚠targeting-qualifier | Place up to 1 Character with a cost of 1 or less at the bottom of the owner's deck. |
| OP10-055 | Marco | [On K.O.] ⚠targeting-qualifier | Return up to 1 of your opponent's Characters with a cost of 4 or less to the owner's hand. |
| OP10-056 | Mansherry | [On Play] ⚠targeting-qualifier | You may rest 1 of your {Dressrosa} type Leader or Stage cards, and return 1 of your {Dressrosa} type Characters with a cost of 4 or more to the owner's hand:… |
| OP10-057 | Leo | [On Play] ⚠embedded-condition | You may rest your Leader or 1 of your Stage cards: If your Leader is [Usopp], look at 5 cards from the top of your deck; reveal up to 2 {Dressrosa} type card… |
| OP10-058 | Rebecca | [On Play] ⚠targeting-qualifier | If there is a Character with a cost of 8 or more, draw 1 card. Then, reveal up to 2 {Dressrosa} type Character cards with a cost of 7 or less other than [Reb… |
| OP10-061 | Special Long-Range Attack!! Bagworm | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 2 or less to the owner's hand. |
| OP10-062 | Violet | [On K.O.] ⚠embedded-condition | DON!! −1: If your Leader has the {Donquixote Pirates} type, add up to 1 purple Event from your trash to your hand. |
| OP10-063 | Vinsmoke Sanji | [On Play] ⚠embedded-condition | If your Leader's type includes "GERMA", look at 5 cards from the top of your deck; reveal up to 1 card with a type including "GERMA" and add it to your hand.… |
| OP10-066 | Giolla | [On Your Opponent's Attack] [Once Per Turn] ⚠targeting-qualifier | You may rest 2 of your DON!! cards: Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP10-067 | Senor Pink | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Add up to 1 purple Event with a cost of 5 or less from you… |
| OP10-069 | Fighting Fish | [DON!! x1] [When Attacking] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| OP10-071 | Donquixote Doflamingo | [On Play] ⚠targeting-qualifier | DON!! −1: Play up to 1 {Donquixote Pirates} type Character card with a cost of 5 or less from your hand. |
| OP10-072 | Donquixote Rosinante | [End of Your Turn] ⚠embedded-condition | If you have 7 or more DON!! cards on your field, set up to 2 of your DON!! cards as active. |
| OP10-075 | Foxy | [Activate: Main] ⚠embedded-condition | You may trash this Character: If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, draw 1 card. |
| OP10-076 | Baby 5 | [On Play] ⚠embedded-condition | You may trash 1 card from your hand: If your Leader has the {Donquixote Pirates} type, add up to 1 DON!! card from your DON!! deck and set it as active. |
| OP10-081 | Usopp | [On Play] ⚠targeting-qualifier | You may rest 1 of your {Dressrosa} type Leader or Stage cards: K.O. up to 1 of your opponent's Characters with a cost of 2 or less. Then, trash 2 cards from … |
| OP10-082 | Kuzan | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Draw 1 card. Then, play up to 1 {Blackbeard Pirates} type Character card with a cost of 5 or less other than [Kuzan] from your … |
| OP10-086 | Shiryu | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your Leader has the {Blackbeard Pirates} type, and this Character was played on this turn, K.O. up to 1 of your opponent's Characters with a base cost of … |
| OP10-087 | Tony Tony.Chopper | [Activate: Main] ⚠embedded-condition | You may rest this Character and 1 of your {Dressrosa} type Leader or Stage cards: If your opponent has 5 or more cards in their hand, your opponent trashes 1… |
| OP10-090 | Franky | [On K.O.] ⚠targeting-qualifier | Play up to 1 {Dressrosa} type Character card with a cost of 3 or less from your trash rested. |
| OP10-091 | Brook | [Activate: Main] ⚠targeting-qualifier | You may rest this Character and 1 of your {Dressrosa} type Leader or Stage cards: K.O. up to 1 of your opponent's Characters with a cost of 1 or less. Then, … |
| OP10-095 | Roronoa Zoro | [On Play] ⚠targeting-qualifier | You may rest 1 of your {Dressrosa} type Leader or Stage cards: K.O. up to 1 of your opponent's Characters with a cost of 4 or less. Then, trash 2 cards from … |
| OP10-096 | There’s No Longer Any Need for the Seven Warlords of the Sea!!! | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's {The Seven Warlords of the Sea} type Characters with a cost of 4 or less. |
| OP10-099 | Eustass"Captain"Kid | [End of Your Turn] ⚠targeting-qualifier | You may turn 1 card from the top of your Life cards face-up: Set up to 1 of your {Supernovas} type Characters with a cost of 3 to 8 as active. That Character… |
| OP10-100 | Inazuma | [Trigger] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type and you and your opponent have a total of 5 or less Life cards, play this card. |
| OP10-106 | Killer | [On K.O.] ⚠embedded-condition | If your Leader has the {Supernovas} type, look at 3 cards from the top of your deck; reveal up to 1 {Supernovas} or {Kid Pirates} type card and add it to you… |
| OP10-107 | Jewelry Bonney | [On Play] ⚠targeting-qualifier | You may add 1 card from the top or bottom of your Life cards to your hand: Add up to 1 {Supernovas} type Character card with a cost of 5 from your hand to th… |
| OP10-110 | Heat & Wire | [Trigger] ⚠embedded-condition | If you have 2 or less Life cards, play this card. |
| OP10-112 | Eustass"Captain"Kid | [End of Your Turn] ⚠embedded-condition | If your opponent has 2 or less Life cards, draw 1 card and trash 1 card from your hand. |
| OP10-113 | Roronoa Zoro | [Trigger] ⚠embedded-condition | You may trash 1 card from your hand: If your Leader has the {Supernovas} type, play this card. |
| OP10-114 | X.Drake | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: If the number of your Life cards is equal to or less than the number of your opponent's Life cards, rest up to 1 of your opponen… |
| OP10-118 | Monkey.D.Luffy | [When Attacking] ⚠embedded-condition | You may place 3 cards from your trash at the bottom of your deck in any order: If your opponent has 5 or more cards in their hand, your opponent trashes 1 ca… |
| OP11-007 | Tashigi | [Activate: Main] ⚠embedded-condition | You may rest this Character: If your Leader has the {Navy} type, up to 1 of your {Navy} type Characters gains +2000 power during this turn. |
| OP11-008 | Doll | [On Play] ⚠embedded-condition | You may trash 1 card from your hand: If your Leader has the {Navy} type, give up to 1 of your opponent's Characters −6000 power during this turn. |
| OP11-013 | Prince Grus | [When Attacking] ⚠targeting-qualifier | All of your opponent's Characters with 2000 power or less cannot activate [Blocker] during this turn. |
| OP11-018 | Honesty Impact | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 6000 power or less. |
| OP11-021 | Jinbe | [End of Your Turn] ⚠embedded-condition | If you have 6 or less cards in your hand, set up to 1 of your {Fish-Man} or {Merfolk} type Characters and up to 1 of your DON!! cards as active. |
| OP11-023 | Arlong | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP11-028 | Lord of the Coast | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 3 or less. |
| OP11-029 | Charlotte Praline | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 1 or less. |
| OP11-031 | Jinbe | [On Play] ⚠targeting-qualifier | If your Leader has the {Fish-Man} or {Merfolk} type, rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP11-034 | Hatchan | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: If your Leader has the {Fish-Man} or {Merfolk} type, up to 1 of your opponent's Characters with a cost of 3 or less cannot be re… |
| OP11-036 | Spotted Neptunian | [On Play] ⚠embedded-condition | If your Leader is [Shirahoshi], look at 5 cards from the top of your deck; reveal up to 1 {Neptunian} type card or [Shirahoshi] and add it to your hand. Then… |
| OP11-039 | Vagabond Drill | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP11-047 | Vinsmoke Reiju | [On Play] ⚠embedded-condition | If your Leader has the {The Vinsmoke Family} type, look at 5 cards from the top of your deck; reveal up to 1 card with a type including "GERMA" and add it to… |
| OP11-048 | Capone"Gang"Bege | [On Play] ⚠targeting-qualifier | Look at 4 cards from the top of your deck; reveal up to 1 {Firetank Pirates} or {Straw Hat Crew} type card with a cost of 2 or more and add it to your hand. … |
| OP11-050 | Gotti | [When Attacking] ⚠targeting-qualifier | You may trash 1 {Firetank Pirates} type card from your hand: Return up to 1 Character with a cost of 1 or less to the owner's hand or place it at the bottom … |
| OP11-054 | Nami | [On Play] ⚠embedded-condition | If your Leader is multicolored, draw 3 cards and place 2 cards from your hand at the top or bottom of your deck in any order. |
| OP11-059 | Gum-Gum King Cobra | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 2 or less to the owner's hand. |
| OP11-061 | Gum-Gum Jet Culverin | [Trigger] ⚠targeting-qualifier | Place up to 1 Character with a cost of 1 or less at the bottom of the owner's deck. |
| OP11-063 | Little Sadi | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader has the {Impel Down} type, rest up to 1 of … |
| OP11-066 | Charlotte Oven | [Activate: Main] ⚠embedded-condition | You may rest this Character: Choose a cost and reveal 1 card from the top of your opponent's deck. If the revealed card has the chosen cost, K.O. up to 1 of … |
| OP11-067 | Charlotte Katakuri | [End of Your Turn] ⚠targeting-qualifier | Set up to 2 of your {Big Mom Pirates} type Characters with a cost of 3 or more as active. Then, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP11-069 | Charlotte Brulee | [On Play] ⚠embedded-condition | You may add 1 card from the top of your Life cards to your hand: If your Leader has the {Big Mom Pirates} type, add up to 1 DON!! card from your DON!! deck a… |
| OP11-070 | Charlotte Pudding | [On Play] ⚠targeting-qualifier | Look at 5 cards from the top of your deck; reveal up to 1 {Big Mom Pirates} type card with a cost of 2 or more and add it to your hand. Then, place the rest … |
| OP11-071 | Charlotte Perospero | [Activate: Main] [Once Per Turn] ⚠embedded-condition | You may trash 1 card from your hand: Choose a cost and reveal 1 card from the top of your opponent's deck. If the revealed card has the chosen cost, draw 1 c… |
| OP11-073 | Charlotte Linlin | [On Your Opponent's Attack] [Once Per Turn] ⚠embedded-condition | DON!! −5: Choose a cost and reveal 1 card from the top of your opponent's deck. If the revealed card has the chosen cost, up to 1 of your Leader gains +2000 … |
| OP11-074 | Streusen | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | DON!! −1, You may rest this Character: Choose a cost and reveal 1 card from the top of your opponent's deck. If the revealed card has the chosen cost, rest u… |
| OP11-075 | Jaguar.D.Saul | [On Play] ⚠embedded-condition | If your Leader is [Nico Robin] and you have 7 or more DON!! cards on your field, draw 2 cards. |
| OP11-076 | Hannyabal | [On Play] ⚠targeting-qualifier | If your Leader has the {Impel Down} type, play up to 1 {Impel Down} type Character card with a cost of 3 or less from your hand. |
| OP11-082 | Aramaki | [Activate: Main] ⚠embedded-condition | You may trash this Character: If your Leader has the {Navy} type, up to 1 of your {Navy} type Characters can also attack active Characters during this turn. … |
| OP11-085 | Kurozumi Orochi | [On Play] ⚠targeting-qualifier | Add up to 1 {SMILE} type card with a cost of 5 or less from your trash to your hand. |
| OP11-086 | Coribou | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Play up to 1 [Caribou] with a cost of 4 or less from your trash. |
| OP11-092 | Helmeppo | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: Draw 1 card and play up to 1 {SWORD} type Character card with a cost of 8 or less other than [Helmeppo] from your trash.… |
| OP11-095 | Monkey.D.Garp | [On Play] ⚠targeting-qualifier | You may place 3 {Navy} type cards from your trash at the bottom of your deck in any order: Give up to 1 rested DON!! card to 1 of your Leader. Then, if there… |
| OP11-100 | Otohime | [On Play] ⚠embedded-condition | If your Leader is [Shirahoshi], you may turn 1 card from the top of your Life cards face-down: Draw 1 card. |
| OP11-103 | Long-Jaw Neptunian | [Activate: Main] ⚠targeting-qualifier | If your Leader is [Shirahoshi], you may rest this Character and turn 1 card from the top of your Life cards face-down: K.O. up to 1 of your opponent's Charac… |
| OP11-106 | Zeus | [On Play] ⚠targeting-qualifier | You may add 1 card from the top or bottom of your Life cards to your hand: K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP11-107 | Topknot Neptunian | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your Leader is [Shirahoshi], you may turn 1 card from the top of your Life cards face-down: Set this Character as active at the end of this turn. |
| OP11-108 | Neptune | [On Play] ⚠embedded-condition | If your Leader is [Shirahoshi], you may turn 1 card from the top of your Life cards face-down: Draw 2 cards and trash 1 card from your hand. |
| OP11-109 | Pappag | [On Play] ⚠embedded-condition | If you have [Camie], draw 2 cards and trash 2 cards from your hand. |
| OP11-110 | Fukaboshi | [On Play] ⚠targeting-qualifier | You may add 1 card from the top or bottom of your Life cards to your hand: K.O. up to 1 of your opponent's Characters with a cost of 1 or less. |
| OP11-115 | You're Just Not My Type! | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 2 or less. |
| OP11-117 | Fish-Man Island | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If your Leader is [Shirahoshi], you may turn 1 card from the top of your Life cards face-up: Up to 1 of your {Neptunian}, {Fish-Man}, or {Merfolk} type Chara… |
| OP11-118 | Monkey.D.Luffy | [When Attacking] ⚠targeting-qualifier | You may trash 1 card from your hand: Return up to 1 Character with a cost of 4 or less to the owner's hand. Then, give up to 1 rested DON!! card to your Lead… |
| OP12-003 | Crocus | [On K.O.] ⚠targeting-qualifier | You may reveal 2 Events from your hand: Play up to 1 red Character card with 3000 power or less from your hand. |
| OP12-015 | Monkey.D.Luffy | [On Play] ⚠targeting-qualifier | You may reveal 2 Events from your hand: Play up to 1 red Character card with 3000 power or less from your hand. Then, give up to 1 rested DON!! card to your … |
| OP12-020 | Roronoa Zoro | [DON!! x3] [Activate: Main] [Once Per Turn] ⚠embedded-condition | If this Leader battles your opponent's Character during this turn, set this Leader as active. Then, this Leader cannot attack your opponent's Characters with… |
| OP12-022 | Inuarashi | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: Up to 1 of your opponent's rested Characters with a cost of 5 or less will not become active in your opponent's next Refresh Phase. |
| OP12-024 | Gyukimaru | [When Attacking] ⚠embedded-condition | If you have a total of 3 or more given DON!! cards, rest up to 1 of your opponent's Characters with a base cost of 6 or less. |
| OP12-028 | Kouzuki Hiyori | [Activate: Main] ⚠embedded-condition | You may rest 1 of your DON!! cards and this Character: If your Leader is [Roronoa Zoro], look at 5 cards from the top of your deck; reveal up to 1 ＜Slash＞ at… |
| OP12-029 | Shimotsuki Kouzaburou | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 2 or less. Then, K.O. up to 1 of your opponent's rested Characters with a base cost of 1 or less. |
| OP12-033 | Helmeppo | [On Block] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP12-034 | Perona | [On Play] ⚠embedded-condition | If your Leader has the ＜Slash＞ attribute, look at 5 cards from the top of your deck; reveal up to 1 ＜Slash＞ attribute card or green Event and add it to your … |
| OP12-041 | Sanji | [When Attacking] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up to 1 DON!! card from your DON!! deck and rest… |
| OP12-044 | Sakazuki | [On Play] ⚠embedded-condition | If your Leader has the {Navy} type, draw 2 cards. |
| OP12-046 | Zephyr(Navy) | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Return up to 1 Character with a cost of 5 or less to the owner's hand. |
| OP12-054 | Marshall.D.Teach | [On Play] ⚠targeting-qualifier | If your Leader has the {The Seven Warlords of the Sea} type, return up to 1 Character with a cost of 1 or less other than this Character to the owner's hand. |
| OP12-056 | Monkey.D.Garp | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: Play up to 1 blue {Navy} type Character card with 8000 power or less other than [Monkey.D.Garp] from your hand. |
| OP12-062 | Vinsmoke Sora | [On Play] ⚠embedded-condition | If your Leader is [Sanji] and the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up to 1 DON!! card fr… |
| OP12-069 | Crocodile | [On Your Opponent's Attack] [Once Per Turn] ⚠embedded-condition | DON!! −1: If your Leader's type includes "Baroque Works", up to 1 of your Leader or Character cards gains +2000 power during this battle. |
| OP12-073 | Trafalgar Law | [On Play] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up to 1 DON!! card from your DON!! deck and set … |
| OP12-074 | Patty | [On Play] ⚠embedded-condition | You may trash 1 Event from your hand: If your Leader is [Sanji], add up to 1 DON!! card from your DON!! deck and set it as active. |
| OP12-075 | Ms. All Sunday | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 3 or less. Then, your opponent may add 1 DON!! card from their DON!! deck and set it as active. |
| OP12-080 | Baratie | [Activate: Main] ⚠embedded-condition | You may place this Stage at the bottom of the owner's deck: If your Leader is [Sanji], look at 3 cards from the top of your deck; reveal up to 1 Event and ad… |
| OP12-084 | Emporio.Ivankov | [On Play] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type, trash 3 cards from the top of your deck. |
| OP12-085 | Karasu | [When Attacking] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type and your opponent has 5 or more cards in their hand, your opponent trashes 1 card from their hand. |
| OP12-086 | Koala | [On Play] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type, look at 3 cards from the top of your deck; reveal up to 1 {Revolutionary Army} type card other than [Koala]… |
| OP12-087 | Nico Robin | [On Play] ⚠embedded-condition | You may trash 1 card from your hand: If your opponent has 5 or more cards in their hand, your opponent trashes 2 cards from their hand. |
| OP12-089 | Hack | [On K.O.] ⚠embedded-condition | If your Leader has the {Revolutionary Army} type, K.O. up to 1 of your opponent's Characters with a base cost of 4 or less. |
| OP12-094 | Monkey.D.Dragon | [On Play] ⚠targeting-qualifier | You may place 3 {Revolutionary Army} type cards from your trash at the bottom of your deck in any order: If your Leader has the {Revolutionary Army} type, pl… |
| OP12-101 | Jewelry Bonney | [Trigger] ⚠embedded-condition | If your Leader has the {Supernovas} type, play this card. |
| OP12-104 | Sentomaru | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP12-109 | Pacifista | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 1 or less and add this card to your hand. |
| OP12-112 | Baby 5 | [Trigger] ⚠embedded-condition | If your Leader is multicolored, draw 2 cards. |
| OP12-113 | Roronoa Zoro | [On K.O.] ⚠targeting-qualifier | If your Leader has the {Supernovas} type, play up to 1 {Supernovas} type Character card with a cost of 4 or less from your hand rested. |
| OP12-113 | Roronoa Zoro | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 1 or less and add this card to your hand. |
| OP12-118 | Jewelry Bonney | [On Play] ⚠embedded-condition | If you have 8 or more rested cards, draw 2 cards and trash 1 card from your hand. Then, set up to 1 of your DON!! cards as active. |
| OP13-001 | Monkey.D.Luffy | [DON!! x1] [On Your Opponent's Attack] ⚠embedded-condition | If you have 5 or less active DON!! cards, you may rest any number of your DON!! cards. For every DON!! card rested this way, this Leader or up to 1 of your {… |
| OP13-012 | Nefeltari Vivi | [On Play] ⚠targeting-qualifier | Look at 4 cards from the top of your deck; reveal up to 1 {Alabasta} or {Straw Hat Crew} type card with a cost of 2 or more and add it to your hand. Then, pl… |
| OP13-016 | Monkey.D.Garp | [On Play] ⚠targeting-qualifier | If your Leader is [Sabo], [Portgas.D.Ace] or [Monkey.D.Luffy], look at 4 cards from the top of your deck; reveal up to 1 card with a cost of 3 or more and ad… |
| OP13-023 | Uta | [On K.O.] ⚠targeting-qualifier | Play up to 1 Character card with a cost of 5 or less from your hand rested. |
| OP13-025 | Koby | [On Play] ⚠embedded-condition | If your Leader has the {FILM} type or the ＜Strike＞ attribute, set up to 1 of your DON!! cards as active. |
| OP13-027 | Sanji | [End of Your Turn] ⚠embedded-condition | If your Leader has the {FILM} or {Straw Hat Crew} type, set up to 1 of your DON!! cards as active. |
| OP13-031 | Trafalgar Law | [On Play] ⚠targeting-qualifier | You may return 1 of your Characters to the owner's hand: Play up to 1 Character card with a cost of 5 or less from your hand rested. |
| OP13-032 | Nico Robin | [On Play] ⚠targeting-qualifier | Up to 1 of your opponent's Characters with a cost of 8 or less cannot be rested until the end of your opponent's next End Phase. |
| OP13-034 | Brook | [On Play] ⚠embedded-condition | If your Leader has the {FILM} or {Straw Hat Crew} type, set up to 1 of your DON!! cards as active. |
| OP13-037 | Roronoa Zoro | [On Play] ⚠embedded-condition | If your Leader has the {FILM} or {Straw Hat Crew} type, set up to 2 of your DON!! cards as active. |
| OP13-038 | Gum-Gum Elephant Gun | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP13-043 | Otama | [On Play] ⚠embedded-condition | If you have 3 or less Life cards, draw 2 cards and trash 1 card from your hand. |
| OP13-045 | Haruta | [When Attacking] ⚠embedded-condition | If you have 4 or less cards in your hand, draw 1 card. |
| OP13-050 | Boa Sandersonia | [On Play] ⚠targeting-qualifier | If your Leader is [Boa Hancock], play up to 1 [Boa Hancock] with a cost of 3 or less from your hand. |
| OP13-051 | Boa Hancock | [On K.O.] ⚠embedded-condition | If your Leader is [Boa Hancock] or multicolored, draw 2 cards. |
| OP13-052 | Boa Marigold | [On Play] ⚠targeting-qualifier | If your Leader is [Boa Hancock], play up to 1 [Boa Hancock] with a cost of 6 or less from your hand. |
| OP13-054 | Yamato | [On Play] ⚠embedded-condition | If you have 3 or less Life cards, draw 2 cards. Then, give up to 1 rested DON!! card to your Leader. |
| OP13-055 | Rakuyo | [When Attacking] ⚠embedded-condition | If you have 4 or less cards in your hand, all of your Characters with a type including "Whitebeard Pirates" gain +1000 power during this turn. |
| OP13-056 | LittleOars Jr. | [When Attacking] ⚠embedded-condition | If your Leader's type includes "Whitebeard Pirates", draw 1 card. |
| OP13-061 | Inuarashi | [On Play] ⚠targeting-qualifier | If you have any DON!! cards given, add up to 1 DON!! card from your DON!! deck and rest it. Then, K.O. up to 1 of your opponent's Characters with a cost of 1… |
| OP13-062 | Crocus | [On Play] ⚠embedded-condition | If you have any DON!! cards given, add up to 1 DON!! card from your DON!! deck and set it as active. |
| OP13-063 | Kouzuki Oden | [On Play] ⚠embedded-condition | If you have any DON!! cards given, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP13-066 | Silvers Rayleigh | [On Play] ⚠targeting-qualifier | If you have any DON!! cards given, rest up to 1 of your opponent's Characters with a cost of 5 or less. Then, add up to 1 DON!! card from your DON!! deck and… |
| OP13-067 | Scopper Gaban | [On Play] ⚠embedded-condition | If your Leader's type includes "Roger Pirates", draw 2 cards and trash 1 card from your hand. Then, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP13-068 | Douglas Bullet | [On Play] ⚠embedded-condition | If your Leader's type includes "Roger Pirates", add up to 1 DON!! card from your DON!! deck and rest it. |
| OP13-069 | Tom | [On Play] ⚠targeting-qualifier | DON!! −1: Add up to 1 Stage card with a cost of 3 or less from your trash to your hand. |
| OP13-071 | Nekomamushi | [On Play] ⚠embedded-condition | If you have 8 or more DON!! cards on your field, K.O. up to 1 of your opponent's Characters with 3000 base power or less. |
| OP13-072 | Buggy | [On Play] ⚠embedded-condition | If your Leader's type includes "Roger Pirates" and you have any DON!! cards given, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP13-074 | Hera | [On Play] ⚠targeting-qualifier | Play up to 1 {Homies} type Character card with 3000 power or less from your hand. |
| OP13-080 | St. Ethanbaron V. Nusjuro | [When Attacking] ⚠embedded-condition | If you have 10 or more cards in your trash, give up to 1 of your opponent's Characters −2000 power during this turn. |
| OP13-082 | Five Elders | [Activate: Main] ⚠targeting-qualifier | If your Leader is [Imu], you may rest 1 of your DON!! cards and trash 1 card from your hand: Trash all of your Characters and play up to 5 {Five Elders} type… |
| OP13-092 | Saint Mjosgard | [On Play] ⚠targeting-qualifier | If you have 3 or less Life cards, play up to 1 {Mary Geoise} type Stage card with a cost of 1 from your trash. |
| OP13-095 | Saint Rosward | [On Play] ⚠embedded-condition | You may trash 1 card from your hand: If you only have {Celestial Dragons} type Characters, K.O. up to 2 of your opponent's Characters with a base cost of 3 o… |
| OP13-102 | Edison | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: If the number of your Life cards is equal to or less than the number of your opponent's Life cards, draw 1 card. Then, rest up … |
| OP13-102 | Edison | [Trigger] ⚠targeting-qualifier | Draw 1 card and rest up to 1 of your opponent's Characters with a cost of 3 or less. |
| OP13-104 | Kouzuki Hiyori | [On K.O.] ⚠embedded-condition | You may trash 1 card from your hand: If your Leader is multicolored, add up to 1 card from the top of your deck to the top of your Life cards. |
| OP13-108 | Jewelry Bonney | [On Play] ⚠embedded-condition | If your Leader has the {Egghead} type, this Character gains [Rush] during this turn. Then, your opponent adds 1 card from the top of their Life cards to thei… |
| OP13-108 | Jewelry Bonney | [Trigger] ⚠targeting-qualifier | If you have 1 or less Life cards, rest up to 1 of your opponent's Characters with a cost of 7 or less. |
| OP13-110 | Stussy | [On Play] ⚠targeting-qualifier | If your Leader has the {Egghead} type, play up to 1 Character card with a cost of 5 or less and a [Trigger] from your hand. |
| OP13-118 | Monkey.D.Luffy | [On Play] ⚠embedded-condition | If your Leader is multicolored, set up to 4 of your DON!! cards as active. Then, you cannot play Character cards with a base cost of 5 or more during this turn. |
| OP13-119 | Portgas.D.Ace | [On Play] ⚠targeting-qualifier | Give up to 1 rested DON!! card to your Leader. Then, you may return up to 1 of your opponent's Characters with a cost of 5 or less to the owner's hand. If yo… |
| OP14-002 | Urouge | [When Attacking] ⚠embedded-condition | If this Character has 5000 power or more, draw 1 card and K.O. up to 1 of your opponent's Characters with 3000 base power or less. |
| OP14-006 | Shachi & Penguin | [When Attacking] ⚠embedded-condition | If this Character has 5000 power or more, give up to 1 of your opponent's Characters −2000 power during this turn. |
| OP14-010 | Basil Hawkins | [On K.O.] ⚠targeting-qualifier | Look at 5 cards from the top of your deck; play up to 1 {Supernovas} type Character card with 2000 power or less other than [Basil Hawkins]. Then, place the … |
| OP14-012 | Bepo | [When Attacking] ⚠embedded-condition | If this Character has 5000 power or more, give up to 2 rested DON!! cards to your Leader or 1 of your Characters. |
| OP14-014 | Eustass"Captain"Kid | [On Play] ⚠targeting-qualifier | If your Leader has the {Supernovas} type, play up to 1 red Character card with 2000 power or less from your hand. |
| OP14-018 | Time for the Counterattack | [Trigger] ⚠targeting-qualifier | Play up to 1 red Character card with 2000 power or less from your hand. |
| OP14-020 | Dracule Mihawk | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may rest 1 of your cards: If there is a Character with a cost of 5 or more, set up to 3 of your DON!! cards as active. Then, you cannot play Character ca… |
| OP14-022 | Usopp | [End of Your Turn] ⚠embedded-condition | If your Leader has the {FILM} or {Straw Hat Crew} type, set up to 2 of your DON!! cards as active. |
| OP14-025 | Kuro | [On Play] ⚠targeting-qualifier | If your Leader is [Kuro], play up to 1 {East Blue} type Character card with a cost of 6 or less from your hand. |
| OP14-031 | Nami | [On Play] ⚠targeting-qualifier | Rest up to 2 of your opponent's Characters with a cost of 8 or less. Then, set up to 5 of your DON!! cards as active at the end of this turn. |
| OP14-033 | Perona | [On Play] ⚠targeting-qualifier | Up to 2 of your opponent's Characters with a cost of 5 or less cannot be rested until the end of your opponent's next End Phase. |
| OP14-033 | Perona | [On K.O.] ⚠targeting-qualifier | You may rest 1 of your cards: Play up to 1 green Character card with a cost of 5 or less from your hand. |
| OP14-039 | Coffin Boat | [On Play] ⚠embedded-condition | If your Leader is [Dracule Mihawk], draw 1 card. |
| OP14-039 | Coffin Boat | [End of Your Turn] ⚠embedded-condition | If your Leader is [Dracule Mihawk], set up to 1 of your DON!! cards as active. |
| OP14-042 | Arlong | [On Play] ⚠targeting-qualifier | If your Leader has the {Fish-Man} type, look at 4 cards from the top of your deck; reveal up to 1 card with a cost of 2 or more and add it to your hand. Then… |
| OP14-043 | Aladine | [On Play] ⚠targeting-qualifier | Play up to 1 {Fish-Man} or {Merfolk} type Character card with a cost of 3 or less from your hand. |
| OP14-047 | Shirahoshi | [On Play] ⚠targeting-qualifier | Draw 1 card and play up to 1 {Fish-Man} or {Merfolk} type Character card with a cost of 3 or less from your hand. |
| OP14-049 | Jinbe | [On Play] ⚠targeting-qualifier | You may rest 2 of your DON!! cards: Draw 2 cards and return up to 1 Character with a cost of 7 or less to the owner's hand. |
| OP14-050 | Chew | [On Play] ⚠embedded-condition | If your Leader has the {Fish-Man} type, draw 1 card. |
| OP14-052 | Hannyabal | [On Play] ⚠targeting-qualifier | You may trash 3 cards from your hand: Play up to 1 {Impel Down} type Character card with a cost of 6 or less from your hand. |
| OP14-054 | Fisher Tiger | [On Play] ⚠embedded-condition | If your Leader has the {Fish-Man} type, draw 3 cards. |
| OP14-059 | Please Take Me with You!! I Can Be of Great Help to You!! | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 4 or less to the owner's hand. |
| OP14-063 | Sugar | [On K.O.] ⚠targeting-qualifier | If your opponent has 6 or more DON!! cards on their field, play up to 1 {Donquixote Pirates} type Character card with a cost of 5 or less from your hand. |
| OP14-071 | Pica | [End of Your Turn] ⚠embedded-condition | If your Leader has the {Donquixote Pirates} type, add up to 1 DON!! card from your DON!! deck and set it as active. |
| OP14-074 | Monet | [On Play] ⚠embedded-condition | If your Leader has the {Donquixote Pirates} type, add up to 1 DON!! card from your DON!! deck and set it as active. |
| OP14-082 | Oinkchuck | [Trigger] ⚠targeting-qualifier | Play up to 1 {Thriller Bark Pirates} type Character card with a cost of 2 or less from your trash rested. |
| OP14-084 | Ms. All Sunday | [On Play] ⚠embedded-condition | If your Leader's type includes "Baroque Works", play up to 1 Character card with a type including "Baroque Works" and a cost of 4 or less and up to 1 Charact… |
| OP14-087 | Miss.Valentine(Mikita) | [On Play] ⚠embedded-condition | If your Leader's type includes "Baroque Works", look at 4 cards from the top of your deck; reveal up to 1 card with a type including "Baroque Works" other th… |
| OP14-088 | Miss.MerryChristmas(Drophy) | [On K.O.] ⚠targeting-qualifier | If your Leader's type includes "Baroque Works", draw 1 card and K.O. up to 1 of your opponent's Stages with a cost of 1. |
| OP14-089 | Ryuma | [Trigger] ⚠targeting-qualifier | Play up to 1 {Thriller Bark Pirates} type Character card with a cost of 4 or less from your trash rested. |
| OP14-090 | Mr.1(Daz.Bonez) | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 0. |
| OP14-094 | Mr.5(Gem) | [On Play] ⚠targeting-qualifier | If there is a Character with a cost of 0 or with a cost of 8 or more, draw 2 cards and trash 1 card from your hand. |
| OP14-100 | Absalom | [Trigger] ⚠targeting-qualifier | Play up to 1 {Thriller Bark Pirates} type Character card with a cost of 4 or less from your trash rested. |
| OP14-102 | Kumacy | [Trigger] ⚠targeting-qualifier | Play up to 1 {Thriller Bark Pirates} type Character card with a cost of 4 or less from your trash rested. |
| OP14-104 | Gecko Moria | [Trigger] ⚠targeting-qualifier | Play up to 1 Character card with a cost of 4 or less from your trash. |
| OP14-105 | Gorgon Sisters | [Trigger] ⚠embedded-condition | If your Leader has the {Kuja Pirates} type, play this card. |
| OP14-107 | Shakuyaku | [On Play] ⚠embedded-condition | If your opponent has 3 or less Life cards, draw 2 cards and trash 2 cards from your hand. |
| OP14-107 | Shakuyaku | [Trigger] ⚠embedded-condition | If your Leader has the {Kuja Pirates} type, play this card. |
| OP14-108 | Silvers Rayleigh | [On Play] ⚠embedded-condition | If your Leader is multicolored and your opponent has 3 or less Life cards, K.O. up to 1 of your opponent's Characters with 7000 base power or less. |
| OP14-109 | Victoria Cindry | [Trigger] ⚠targeting-qualifier | Play up to 1 {Thriller Bark Pirates} type Character card with a cost of 4 or less from your trash rested. |
| OP14-110 | Dr. Hogback | [On K.O.] ⚠targeting-qualifier | Play up to 1 Character card with a cost of 4 or less and a [Trigger] other than [Dr. Hogback] from your trash. |
| OP14-110 | Dr. Hogback | [Trigger] ⚠targeting-qualifier | Play up to 1 {Thriller Bark Pirates} type Character card with a cost of 4 or less from your trash rested. |
| OP14-111 | Perona | [On Play] [On K.O.] ⚠targeting-qualifier | Up to 1 of your opponent's Characters with a cost of 6 or less cannot attack until the end of your opponent's next End Phase. |
| OP14-111 | Perona | [Trigger] ⚠targeting-qualifier | Play up to 1 {Thriller Bark Pirates} type Character card with a cost of 4 or less from your trash rested. |
| OP14-112 | Boa Hancock | [On Play] ⚠embedded-condition | If your Leader has the {The Seven Warlords of the Sea} type, add up to 1 card from the top of your deck to the top of your Life cards. Then, add up to 1 card… |
| OP14-112 | Boa Hancock | [Trigger] ⚠targeting-qualifier | Play up to 1 Character card with 6000 power or less and a [Trigger] from your hand. |
| OP14-113 | Marguerite | [Trigger] ⚠embedded-condition | If your Leader has the {Kuja Pirates} type, play this card. |
| OP14-114 | Ran | [Trigger] ⚠embedded-condition | If your Leader has the {Kuja Pirates} type, play this card. |
| OP14-115 | Rindo | [Trigger] ⚠embedded-condition | If your Leader has the {Kuja Pirates} type, play this card. |
| OP14-117 | Brick Bat | [Trigger] ⚠targeting-qualifier | Play up to 1 {Thriller Bark Pirates} type Character card with a cost of 4 or less from your trash rested. |
| OP14-118 | You'll Frighten Me... ♡ | [Trigger] ⚠targeting-qualifier | Play up to 1 Character card with 6000 power or less and a [Trigger] from your hand. |
| OP14-120 | Crocodile | [On Play] ⚠targeting-qualifier | Up to 1 of your opponent's Characters with a cost of 9 or less cannot attack until the end of your opponent's next End Phase. Then, if your opponent has a Ch… |
| OP15-002 | Lucy | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If you have activated an Event with a base cost of 3 or more during this turn, draw 1 card. |
| OP15-004 | Sea Cat | [On Play] ⚠embedded-condition | If your Leader has 0 power or less, give up to 1 of your opponent's Characters −3000 power during this turn. |
| OP15-005 | Cabaji | [When Attacking] ⚠embedded-condition | If your opponent has any DON!! cards given, this Character gains +2000 power during this turn. |
| OP15-007 | Gin | [On Play] ⚠targeting-qualifier | If your Leader has the {East Blue} type, play up to 1 Character card with a cost of 5 or less from your hand. |
| OP15-008 | Krieg | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If this Character was played on this turn, give all of your opponent's Characters −1000 power during this turn for every DON!! card given to that Character. |
| OP15-011 | Pearl | [On K.O.] ⚠embedded-condition | If your Leader has the {East Blue} type, K.O. up to 1 of your opponent's Characters with 6000 base power or less. |
| OP15-018 | Mohji | [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 3000 power or less with a DON!! card given. |
| OP15-022 | Brook | [Activate: Main] [Once Per Turn] ⚠embedded-condition | Trash 4 cards from the top of your deck. Then, if your deck has 0 cards, set up to 1 of your Characters as active. |
| OP15-024 | Usopp | [On K.O.] ⚠targeting-qualifier | Rest up to 1 of your opponent's Leader or Character cards with a cost of 7 or less. |
| OP15-028 | Meowban Brothers | [On Play] ⚠embedded-condition | If your Leader has the {East Blue} type, give up to 1 DON!! card from your opponent's cost area to 1 of your opponent's Characters. |
| OP15-029 | Bartholomew Kuma | [On Play] ⚠targeting-qualifier | Up to 1 of your opponent's Characters with a cost of 5 or less cannot be rested until the end of your opponent's next End Phase. |
| OP15-036 | Ryuma | [On Play] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 4 or less. |
| OP15-039 | Rebecca | [Activate: Main] ⚠targeting-qualifier | You may rest this Leader and return 1 of your {Dressrosa} type Characters to the owner's hand: Play up to 1 {Dressrosa} type Character card with a cost of 3 … |
| OP15-042 | Kyros | [On Play] ⚠embedded-condition | You may trash 1 card from your hand: If your Leader is [Rebecca], this Character gains [Rush] during this turn. |
| OP15-046 | Sabo | [On Play] ⚠embedded-condition | If your Leader has the {Dressrosa} type, activate up to 1 {Dressrosa} type Event from your hand. |
| OP15-057 | Dressrosa Kingdom | [On Play] ⚠embedded-condition | If your Leader has the {Dressrosa} type, draw 1 card. |
| OP15-058 | Enel | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If it is your second turn or later, add up to 1 DON!! card from your DON!! deck and set it as active, and add up to 4 additional DON!! cards and rest them. T… |
| OP15-061 | Ohm | [When Attacking] ⚠embedded-condition | If you have 6 or less DON!! cards on your field, give up to 1 of your opponent's Characters −1000 power during this turn. |
| OP15-063 | Gedatsu | [On K.O.] ⚠targeting-qualifier | If you have 6 or less DON!! cards on your field, K.O. up to 1 of your opponent's Characters with 2000 power or less. |
| OP15-064 | Kotori | [Activate: Main] ⚠targeting-qualifier | DON!! −2, You may rest this Character: If you have [Satori] and [Hotori], rest up to 1 of your opponent's Characters with 5000 power or less. |
| OP15-065 | Goro | [On Play] ⚠embedded-condition | Reveal 1 card from the top of your deck. If the revealed card has a cost of 2 or less, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP15-066 | Satori | [When Attacking] ⚠embedded-condition | If you have 6 or less DON!! cards on your field, look at 2 cards from the top of your deck and place them at the top or bottom of your deck in any order. |
| OP15-072 | Hotori | [Activate: Main] ⚠embedded-condition | DON!! −2, You may rest this Character: If you have [Kotori] and [Satori], give up to 1 of your opponent's Characters −3000 power during this turn. |
| OP15-073 | Yama | [On Play] ⚠targeting-qualifier | Play up to 1 [Heavenly Warriors] with a cost of 1 or up to 1 {Vassals} type Character card with a cost of 1 from your hand. |
| OP15-081 | Sanji | [On Play] ⚠embedded-condition | If your Leader has the {Straw Hat Crew} type, trash 5 cards from the top of your deck. |
| OP15-082 | Charlotte Lola | [On K.O.] ⚠targeting-qualifier | Add up to 1 of your Character cards with a cost of 8 or less from your trash to your hand. |
| OP15-083 | Spoil | [Activate: Main] ⚠embedded-condition | You may trash this Character: If you have 15 or more cards in your trash, give up to 1 rested DON!! card to 1 of your Leader or Character cards. |
| OP15-084 | Dr. Hogback | [On Play] ⚠embedded-condition | If your Leader has the {Thriller Bark Pirates} type, trash 5 cards from the top of your deck. |
| OP15-084 | Dr. Hogback | [On K.O.] ⚠embedded-condition | If you have 6 or less cards in your hand, draw 1 card. |
| OP15-085 | Tony Tony.Chopper | [Activate: Main] ⚠embedded-condition | You may trash this Character: If your Leader has the {Straw Hat Crew} type, add up to 1 {Straw Hat Crew} type Character card other than [Tony Tony.Chopper] f… |
| OP15-086 | Nami | [On Play] ⚠targeting-qualifier | If your Leader has the {Straw Hat Crew} type, play up to 1 {Straw Hat Crew} type Character with a cost of 7 or less from your trash. The Character played wit… |
| OP15-088 | Pirates Docking Six | [On Play] ⚠targeting-qualifier | You may trash 3 cards from the top of your deck: Play up to 1 {Straw Hat Crew} type Character card with a cost of 2 or less from your trash. |
| OP15-100 | Kamakiri | [On Play] ⚠targeting-qualifier | You may trash this Character and add 1 card from the top of your Life cards to your hand: K.O. up to 1 of your opponent's Characters with a cost of 6 or less. |
| OP15-103 | Genbo | [Trigger] ⚠embedded-condition | Draw 1 card. Then, if you have 2 or less Life cards, play this card. |
| OP15-104 | Conis | [On Play] ⚠embedded-condition | If you have less Life cards than your opponent, draw 2 cards and trash 2 cards from your hand. |
| OP15-106 | Octoballoon | [Trigger] ⚠targeting-qualifier | Draw 1 card. Then, play up to 1 yellow Character or Stage card with a cost of 2 or less from your hand. |
| OP15-109 | Nico Robin | [On Play] ⚠targeting-qualifier | You may add 1 card from the top of your Life cards to your hand: If your Leader has the {Straw Hat Crew} type, add up to 1 card from the top of your deck to … |
| OP15-110 | Braham | [On K.O.] ⚠embedded-condition | If your Leader has the {Shandian Warrior} type, add up to 1 card from the top of your deck to the top of your Life cards. |
| OP15-112 | Raki | [On Play] ⚠targeting-qualifier | Play up to 1 {Shandian Warrior} type Character card with a cost of 3 or less from your hand. |
| OP15-115 | Impact Dial | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP15-117 | Heso!! | [Trigger] ⚠embedded-condition | If your Leader has the {Sky Island} type, draw 2 cards. |
| OP16-001 | Portgas.D.Ace | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | Up to 1 of your [Monkey.D.Luffy] Characters or up to 1 of your Characters with a type including "Whitebeard Pirates", with 8000 power or more, gains [Rush] d… |
| OP16-002 | Izo | [On Play] ⚠targeting-qualifier | You may reveal 1 Character card with 8000 power from your hand: Draw 1 card. |
| OP16-003 | Edward.Newgate | [On Play] ⚠targeting-qualifier | You may reveal 2 Character cards with 8000 power from your hand: Give up to 1 of your opponent's Characters −6000 power during this turn. |
| OP16-006 | Shanks | [On Play] ⚠targeting-qualifier | You may rest 2 of your DON!! cards: K.O. up to 1 of your opponent's Characters with 4000 power or less. |
| OP16-007 | Jozu | [On Play] ⚠targeting-qualifier | You may reveal 1 Character card with 8000 power from your hand: Give up to 1 of your opponent's Characters −1000 power during this turn. |
| OP16-008 | Squard | [On Play] ⚠targeting-qualifier | You may trash 1 of your Characters with 10000 base power: K.O. up to 1 of your opponent's Characters with 8000 power or less. |
| OP16-009 | Speed Jil | [On Play] ⚠targeting-qualifier | You may trash 1 Character card with 8000 power from your hand: This Character gains [Rush] and +2000 power until the end of your opponent's next End Phase. |
| OP16-010 | Namule | [On Play] ⚠targeting-qualifier | You may reveal 1 Character card with 8000 power from your hand: K.O. up to 1 of your opponent's Characters with 2000 base power or less. |
| OP16-011 | Vista | [On Play] ⚠targeting-qualifier | You may reveal 1 Character card with 8000 power from your hand: Draw 1 card. |
| OP16-012 | Benn.Beckman | [On Play] ⚠embedded-condition | You may rest 1 of your DON!! cards: If your Leader has the {Red-Haired Pirates} type and you have 10 DON!! cards on your field, play up to 1 [Shanks] from yo… |
| OP16-014 | Marco | [On K.O.] ⚠targeting-qualifier | You may trash 1 Character card with 8000 power from your hand: Play this Character card from your trash. |
| OP16-015 | Monkey.D.Luffy | [On Your Opponent's Attack] ⚠targeting-qualifier | You may trash 1 Character card with 8000 power from your hand: Your Leader and this Character's base power becomes 7000 during this turn. |
| OP16-021 | Moby Dick | [On Play] ⚠embedded-condition | If your Leader has the {Whitebeard Pirates} type, look at 3 cards from the top of your deck and add up to 1 card to your hand. Then, place the rest at the bo… |
| OP16-022 | Monkey.D.Luffy | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If the only Characters on your field are {Impel Down} type Characters, set up to 2 of your DON!! cards as active. |
| OP16-025 | Bunkov | [When Attacking] ⚠targeting-qualifier | If you have [Antlerkov], play up to 1 Character card with a cost of 2 or less from your hand. |
| OP16-026 | Emporio.Ivankov | [On Play] ⚠targeting-qualifier | Look at 3 cards from the top of your deck; reveal up to 1 {Impel Down} type card, add it to your hand and place the rest at the bottom of your deck in any or… |
| OP16-029 | Antlerkov | [When Attacking] ⚠targeting-qualifier | If you have [Bunkov], play up to 1 Character card with a cost of 2 or less from your hand. |
| OP16-035 | Roronoa Zoro | [On Play] ⚠embedded-condition | Rest up to 1 of your opponent's cards. Then, you may trash 1 card from your hand. If you do, give up to 3 rested DON!! cards to your Leader. |
| OP16-036 | Mr.2.Bon.Kurei(Bentham) | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP16-037 | Mr.3(Galdino) | [On Play] ⚠targeting-qualifier | If your Leader has the {Impel Down} type, rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| OP16-043 | Usopp | [On K.O.] ⚠targeting-qualifier | You may rest 1 of your {Dressrosa} type Leader or Stage cards: Return up to 1 of your opponent's Characters with a cost of 5 or less to the owner's hand. |
| OP16-045 | Crocodile | [On Play] ⚠targeting-qualifier | You may return 1 of your Characters with a cost of 2 or more to the owner's hand: Play up to 1 {Impel Down} type Character card with a cost of 2 or less from… |
| OP16-048 | Buggy | [On Play] ⚠embedded-condition | If your Leader has the {Impel Down} type, draw 1 card and play up to 1 [Prisoner of Impel Down] card from your hand. |
| OP16-050 | Miss Olive | [On Play] ⚠targeting-qualifier | You may return 1 of your Characters with a cost of 2 or more to the owner's hand: Draw 2 cards and trash 1 card from your hand. |
| OP16-051 | Mohji & Cabaji | [On Play] ⚠embedded-condition | If you have 5 or less cards in your hand, draw 2 cards. |
| OP16-053 | Roronoa Zoro | [When Attacking] ⚠embedded-condition | If you have 6 or less cards in your hand, draw 1 card. |
| OP16-056 | Mr.3(Galdino) | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Draw 2 cards, and up to 1 of your opponent's Characters with a cost of 9 or less cannot attack until the end of your opponent's… |
| OP16-065 | Sakazuki | [Activate: Main] [Once Per Turn] ⚠embedded-condition | You may rest 1 of your DON!! cards: If your Leader has the {Navy} type, add up to 2 DON!! cards from your DON!! deck and set them as active. |
| OP16-066 | Sengoku | [On Play] ⚠embedded-condition | If your Leader has the {Navy} type, add up to 2 DON!! cards from your DON!! deck and rest them. Then, draw 2 cards and trash 2 cards from your hand. |
| OP16-068 | Trafalgar Law | [When Attacking] ⚠embedded-condition | If your Leader has the {Donquixote Pirates} type, this Character gains +3000 power during this turn. |
| OP16-070 | Donquixote Rosinante | [On Play] ⚠embedded-condition | You may rest 2 of your DON!! cards: If your Leader has the {Navy} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| OP16-074 | Magellan | [On Play] ⚠embedded-condition | If your Leader has the {Impel Down} type, your opponent returns 1 DON!! card from their field to their DON!! deck. |
| OP16-075 | Monkey.D.Garp | [On Play] ⚠embedded-condition | If your Leader has the {Navy} type, add up to 1 DON!! card from your DON!! deck and set it as active, and add up to 1 additional DON!! card and rest it. |
| OP16-081 | Otama | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: If you have a Character with a cost of 8 or more, give up to 1 of your opponent's Characters −2000 power during this turn. |
| OP16-082 | Kin'emon | [On Play] ⚠embedded-condition | If your Leader has the {Land of Wano} type, look at 5 cards from the top of your deck; reveal up to 1 {Land of Wano} type card and add it to your hand. Then,… |
| OP16-083 | Kouzuki Oden | [On Play] ⚠targeting-qualifier | You may trash 1 Character card with a cost of 8 or more from your hand: Draw 2 cards. |
| OP16-084 | Kouzuki Momonosuke | [Activate: Main] ⚠targeting-qualifier | You may trash this Character with a cost of 20 or more: If you have 9 or more DON!! cards on your field, play up to 1 [Kouzuki Momonosuke] with a cost of 9 f… |
| OP16-085 | Kouzuki Momonosuke | [On Play] ⚠targeting-qualifier | Play up to 1 {Land of Wano} type Character card with a cost of 6 or less other than [Kouzuki Momonosuke] from your trash. |
| OP16-087 | Shinobu | [On Play] ⚠embedded-condition | You may trash this Character: If your Leader has the {Land of Wano} type, draw 1 card and up to 1 of your [Kouzuki Momonosuke] gains +20 cost during this turn. |
| OP16-090 | Tony Tony.Chopper | [On Play] ⚠targeting-qualifier | Draw 2 cards and trash 2 cards from your hand. Then, K.O. up to 1 of your opponent's Characters with a cost of 1 or less. |
| OP16-091 | Nami | [On Play] ⚠embedded-condition | If your Leader has the {Land of Wano} type, look at 4 cards from the top of your deck; reveal up to 1 {Land of Wano} type card other than [Nami] and add it t… |
| OP16-092 | Nico Robin | [On Play] ⚠targeting-qualifier | You may trash 1 Character card with a cost of 8 or more from your hand: Draw 2 cards. |
| OP16-096 | Yamato | [On K.O.] ⚠targeting-qualifier | Play up to 1 [Yamato] with a cost of 6 or less from your trash. |
| OP16-097 | Yamato | [On Play] ⚠targeting-qualifier | Add up to 1 {Land of Wano} type Character card with a cost of 6 or less from your trash to your hand. Then, play up to 1 Character card with a cost of 2 or l… |
| OP16-098 | Yamato | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Play up to 1 black [Yamato] with a cost of 8 from your trash. |
| OP16-103 | Van Augur | [Opponent's Turn] [On K.O.] ⚠embedded-condition | If your Leader has the {Blackbeard Pirates} type, draw 1 card and give up to 1 of your opponent's Leader or Character cards −3000 power during this turn. |
| OP16-104 | Catarina Devon | [Trigger] ⚠targeting-qualifier | Draw 1 card and play up to 1 {Blackbeard Pirates} type Character with a cost of 1 from your trash. |
| OP16-105 | Gecko Moria | [Trigger] ⚠targeting-qualifier | If you have 1 or less Life cards, play up to 1 [Absalom], up to 1 [Dr. Hogback], and up to 1 [Perona], with a cost of 4 or less from your trash. |
| OP16-106 | Sanjuan.Wolf | [On K.O.] ⚠embedded-condition | If your Leader has the {Blackbeard Pirates} type, draw 1 card, then up to 1 of your Leader or Character cards' base power becomes 7000 during this turn. |
| OP16-108 | Shiryu | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: Add up to 1 {Blackbeard Pirates} type card with a cost of 6 or less from your trash to the top of your Life cards face-up. |
| OP16-109 | Doc Q | [On K.O.] ⚠targeting-qualifier | If your Leader has the {Blackbeard Pirates} type, draw 1 card and K.O. up to 2 of your opponent's Characters with a cost of 1 or less. |
| OP16-110 | Vasco Shot | [On K.O.] ⚠targeting-qualifier | Draw 1 card and rest up to 1 of your opponent's Characters with a cost of 6 or less. |
| OP16-111 | Boa Sandersonia | [Trigger] ⚠embedded-condition | If you have 2 or less Life cards, play this card. |
| OP16-113 | Boa Marigold | [Trigger] ⚠embedded-condition | If your Leader has the {Kuja Pirates} type, play this card. |
| OP16-114 | Laffitte | [On K.O.] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| OP16-119 | Marshall.D.Teach | [Trigger] ⚠targeting-qualifier | Negate the effect of up to 1 of your opponent's Characters during this turn. Then, K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| P-009 | Trafalgar Law | [On Play] ⚠embedded-condition | If your opponent has 6 or more cards in their hand, your opponent adds 1 card from their Life area to their hand. |
| P-019 | Bepo | [DON!! x1] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 3000 power or less. |
| P-030 | Jinbe | [On K.O.] ⚠targeting-qualifier | Place up to 1 Character with a cost of 3 or less at the bottom of the owner's deck. |
| P-035 | Monkey.D.Luffy | [DON!! x1] [When Attacking] ⚠targeting-qualifier | You may trash 1 card from your hand: K.O. up to 1 of your opponent's Characters with a cost of 0. |
| P-037 | Monkey.D.Luffy | [When Attacking] ⚠embedded-condition | If you have 2 or more rested Characters, this Character gains +1000 power during this turn. |
| P-042 | Roronoa Zoro | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| P-043 | Monkey.D.Luffy | [On Play] ⚠targeting-qualifier | Return up to 1 Character with a cost of 3 or less to the owner's hand. |
| P-046 | Yamato | [On Play] ⚠embedded-condition | You may place all cards in your hand at the bottom of your deck in any order. If you do, draw cards equal to the number you placed at the bottom of your deck. |
| P-047 | Monkey.D.Luffy | [DON!! x1] [When Attacking] ⚠embedded-condition | Draw 1 card if you have 3 or less cards in your hand. |
| P-048 | Arlong | [DON!! x1] [When Attacking] ⚠embedded-condition | If you have 4 or more Life cards, your opponent places 1 card from their hand at the bottom of their deck. |
| P-053 | Nami | [On Play] ⚠targeting-qualifier | If you have 3 or less cards in your hand, return up to 1 of your opponent's Characters with a cost of 3 or less to the owner's hand. |
| P-056 | Roronoa Zoro | [On Play] ⚠targeting-qualifier | ➁ (You may rest the specified number of DON!! cards in your cost area.): Return up to 1 Character with a cost of 5 or less to the owner's hand. |
| P-062 | Hody & Hyouzou | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less and this Character gains +1000 power during this turn. Then, add 1 card from the top of y… |
| P-063 | Jinbe | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 1 or less. |
| P-065 | Tony Tony.Chopper | [When Attacking] ⚠targeting-qualifier | If your opponent has a Character with a cost of 0, this Character gains +2000 power until the start of your next turn. |
| P-072 | Ryuma | [On Play] [On K.O.] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| P-075 | Monkey.D.Luffy | [When Attacking] ⚠targeting-qualifier | If you have a Character with a cost of 8 or more on your field, draw 1 card and trash 1 card from your hand. |
| P-079 | Lim | [End of Your Turn] ⚠embedded-condition | If you have 2 or more rested {ODYSSEY} type Characters, set this Character as active. |
| P-081 | Dracule Mihawk | [Activate: Main] ⚠targeting-qualifier | You may return this Character to the owner's hand: If you have 3 or more blue {Cross Guild} type Characters, play up to 1 {Cross Guild} type Character card w… |
| P-082 | Crocodile | [Your Turn] [On Play] ⚠targeting-qualifier | If your Leader has the {Cross Guild} type or a type including "Baroque Works", place up to 1 of your opponent's Characters with 2000 power or less at the bot… |
| P-084 | Buggy | [On Play] ⚠targeting-qualifier | Play up to 1 {Cross Guild} type Character card with a cost of 6 or less from your hand. |
| P-085 | Jewelry Bonney | [On Play] ⚠targeting-qualifier | If your Leader has the {Supernovas} type and the number of your Life cards is equal to or less than the number of your opponent's Life cards, add up to 1 of … |
| P-088 | Trafalgar Law | [Trigger] ⚠embedded-condition | If your Leader has the {Supernovas} type and you and your opponent have a total of 5 or less Life cards, play this card. |
| P-091 | Shirahoshi | [On Play] ⚠targeting-qualifier | Play up to 1 {Neptunian} or {Fish-Man Island} type Character card with a cost of 5 or less from your hand. |
| P-092 | Koby | [When Attacking] ⚠embedded-condition | If your Leader has the {Navy} type, your Leader's base power becomes 7000 until the end of your opponent's next turn. |
| P-093 | Trafalgar Law | [On Play] ⚠embedded-condition | If the number of DON!! cards on your field is equal to or less than the number on your opponent's field, add up to 1 DON!! card from your DON!! deck and rest… |
| P-098 | Buggy | [On Play] ⚠targeting-qualifier | If you do not have 5 Characters with a cost of 5 or more, place this Character at the bottom of the owner's deck. |
| P-102 | Nami | [On Play] ⚠embedded-condition | If your Leader has the {Straw Hat Crew} type, set up to 2 of your DON!! cards as active. |
| P-106 | Monkey.D.Luffy | [Trigger] ⚠targeting-qualifier | Draw 1 card and K.O. up to 1 of your opponent's Characters with a cost of 2 or less. |
| P-112 | Nami | [On Play] ⚠targeting-qualifier | If your Leader is [Nami], give up to 1 rested DON!! card to your Leader. Then, play up to 1 Character card with a cost of 2 or less from your hand. |
| P-113 | Jewelry Bonney | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| P-115 | Boa Hancock | [Trigger] ⚠targeting-qualifier | Play up to 1 yellow Character card with 5000 power or less and a [Trigger] from your hand. |
| P-135 | Monkey.D.Luffy | [On Play] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| PRB01-001 | Sanji | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | Up to 1 of your Characters without an [On Play] effect and with a cost of 8 or less gains [Rush] during this turn. |
| PRB02-001 | Koby | [When Attacking] ⚠embedded-condition | K.O. up to 1 of your opponent's Characters with 3000 base power or less. Then, if you have 6 or less cards in your hand, draw 1 card. |
| PRB02-003 | Lucky.Roux | [On Play] ⚠targeting-qualifier | You may trash 1 Character card with 6000 power or more from your hand: Draw 2 cards. |
| PRB02-005 | Monkey.D.Luffy | [Your Turn] [On Play] ⚠embedded-condition | If your Leader is multicolored and your opponent has 7 or less DON!! cards on their field, your opponent rests 1 of their active DON!! cards at the start of … |
| PRB02-007 | Jinbe | [When Attacking] ⚠targeting-qualifier | Place up to 1 Character with a cost of 1 or less at the bottom of the owner's deck. |
| PRB02-010 | Charlotte Pudding | [On Play] ⚠embedded-condition | DON!! −2: If your Leader has the {Big Mom Pirates} type and your opponent has 6 or more DON!! cards on their field, draw 2 cards. Then, play up to 1 {Big Mom… |
| PRB02-011 | Donquixote Doflamingo | [On Play] ⚠embedded-condition | If your Leader is multicolored, add up to 1 DON!! card from your DON!! deck and rest it. |
| PRB02-013 | Gecko Moria | [On Play] ⚠targeting-qualifier | If your Leader has the {Thriller Bark Pirates} type, play up to 1 Character card with a cost of 4 or less from your trash rested. Then, give up to 1 rested D… |
| PRB02-015 | Shiryu | [On K.O.] ⚠embedded-condition | If your Leader has the {Blackbeard Pirates} type, K.O. up to 1 of your opponent's Characters with a base cost of 4 or less. |
| PRB02-016 | Otama | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| PRB02-017 | Boa Hancock | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| PRB02-018 | Portgas.D.Ace | [On Play] ⚠targeting-qualifier | If you have a face-up Life card, play up to 1 [Sabo], [Portgas.D.Ace], or [Monkey.D.Luffy] with a cost of 2 from your hand or trash. |
| ST01-016 | Diable Jambe | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's [Blocker] Characters with a cost of 3 or less. |
| ST02-005 | Killer | [On Play] ⚠targeting-qualifier | K.O. up to 1 of your opponent's rested Characters with a cost of 3 or less. |
| ST02-009 | Trafalgar Law | [On Play] ⚠targeting-qualifier | Set up to 1 of your {Supernovas} or {Heart Pirates} type rested Characters with a cost of 5 or less as active. |
| ST02-017 | Straw Sword | [Trigger] ⚠targeting-qualifier | Play up to 1 {Supernovas} type card with a cost of 2 or less from your hand. |
| ST03-001 | Crocodile | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | DON!! −4 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Return up to 1 Character with a cost of 5 or less to the o… |
| ST03-003 | Crocodile | [DON!! x1] [On Block] ⚠targeting-qualifier | Place up to 1 Character with a cost of 2 or less at the bottom of the owner's deck. |
| ST03-004 | Gecko Moria | [On Play] ⚠targeting-qualifier | Add up to 1 {The Seven Warlords of the Sea} or {Thriller Bark Pirates} type Character with a cost of 4 or less other than [Gecko Moria] from your trash to yo… |
| ST03-009 | Donquixote Doflamingo | [On Play] ⚠targeting-qualifier | Return up to 1 Character with a cost of 7 or less to the owner's hand. |
| ST03-014 | Marshall.D.Teach | [On Play] ⚠targeting-qualifier | Return up to 1 Character with a cost of 3 or less to the owner's hand. |
| ST04-002 | Ulti | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play up to 1 [Page One] card with a cost of 4 or less from… |
| ST04-003 | Kaido | [On Play] ⚠targeting-qualifier | DON!! −5 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| ST04-004 | King | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| ST04-010 | Who's.Who | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): K.O. up to 1 of your opponent's Characters with a cost of … |
| ST04-017 | Onigashima Island | [Activate: Main] ⚠embedded-condition | You may rest this Stage: If your Leader has the {Animal Kingdom Pirates} type, add up to 1 DON!! card from your DON!! deck and rest it. |
| ST05-004 | Uta | [On Block] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Rest up to 1 of your opponent's Characters with a cost of … |
| ST05-005 | Carina | [Activate: Main] [Once Per Turn] ⚠embedded-condition | You may rest this Character and trash 1 {FILM} type card from your hand: If your opponent has more DON!! cards on their field than you, add 2 DON!! cards fro… |
| ST05-011 | Douglas Bullet | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | DON!! −4 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Rest up to 2 of your opponent's Characters with a cost of … |
| ST06-001 | Sakazuki | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | ③ (You may rest the specified number of DON!! cards in your cost area.) You may trash 1 card from your hand: K.O. up to 1 of your opponent's Characters with … |
| ST06-002 | Koby | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: K.O. up to 1 of your opponent's Characters with a cost of 0. |
| ST06-012 | Monkey.D.Garp | [Activate: Main] ⚠targeting-qualifier | You may trash 1 card from your hand and rest this Character: K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| ST06-014 | Shockwave | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| ST06-017 | Navy HQ | [Activate: Main] ⚠embedded-condition | You may rest this Stage: If your Leader has the {Navy} type, give up to 1 of your opponent's Characters −1 cost during this turn. |
| ST07-001 | Charlotte Linlin | [DON!! x2] [When Attacking] ⚠embedded-condition | You may add 1 card from the top or bottom of your Life cards to your hand: If you have 2 or less Life cards, add up to 1 card from your hand to the top of yo… |
| ST07-003 | Charlotte Katakuri | [On Play] ⚠embedded-condition | Look at up to 1 card from the top of your or your opponent's Life cards, and place it at the top or bottom of the Life cards. Then, if you have less Life car… |
| ST07-009 | Charlotte Mont-d'or | [Activate: Main] ⚠targeting-qualifier | You may rest this Character and add 1 card from the top or bottom of your Life cards to your hand: K.O. up to 1 of your opponent's Characters with a cost of … |
| ST07-017 | Queen Mama Chanter | [Activate: Main] ⚠targeting-qualifier | You may rest this Stage and add 1 card from the top or bottom of your Life cards to your hand: Add up to 1 of your Characters with a cost of 3 to the top of … |
| ST08-004 | Koby | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: K.O. up to 1 of your opponent's Characters with a cost of 2 or less. |
| ST08-005 | Shanks | [On Play] ⚠targeting-qualifier | You may trash 1 card from your hand: K.O. all Characters with a cost of 1 or less. |
| ST08-009 | Makino | [On Play] ⚠targeting-qualifier | If there is a Character with a cost of 0, draw 1 card. |
| ST08-014 | Gum-Gum Bell | [Trigger] ⚠targeting-qualifier | Add up to 1 black Character card with a cost of 2 or less from your trash to your hand. |
| ST09-002 | Uzuki Tempura | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 2 or less and add this card to your hand. |
| ST09-008 | Shimotsuki Ushimaru | [DON!! x1] [When Attacking] ⚠targeting-qualifier | You may add 1 card from the top or bottom of your Life cards to your hand: Play up to 1 yellow {Land of Wano} type Character card with a cost of 4 or less fr… |
| ST09-009 | Fugetsu Omusubi | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 1 or less and add this card to your hand. |
| ST10-001 | Trafalgar Law | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | DON!! −3 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Place up to 1 of your opponent's Characters with 3000 powe… |
| ST10-002 | Monkey.D.Luffy | [Activate: Main] [Once Per Turn] ⚠embedded-condition | If you have 0 DON!! cards on your field or 8 or more DON!! cards on your field, add up to 1 DON!! card from your DON!! deck and set it as active. |
| ST10-004 | Sanji | [On Play] ⚠embedded-condition | If your opponent has a Character with 5000 or more power, this Character gains [Rush] during this turn. |
| ST10-008 | Shachi & Penguin | [On Play] ⚠embedded-condition | If you have 3 or less DON!! cards on your field, add up to 2 DON!! cards from your DON!! deck and rest them. |
| ST10-010 | Trafalgar Law | [On Play] ⚠embedded-condition | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your opponent has 7 or more cards in their hand, trash … |
| ST10-012 | Bepo | [On Play] [When Attacking] ⚠embedded-condition | If your opponent has more DON!! cards on their field than you, add up to 1 DON!! card from your DON!! deck and rest it. |
| ST12-001 | Roronoa Zoro & Sanji | [DON!! x1] [When Attacking] [Once Per Turn] ⚠targeting-qualifier | You may return 1 of your Characters with a cost of 2 or more to the owner's hand: Set up to 1 of your Characters with 7000 power or less as active. |
| ST12-002 | Kuina | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: Rest up to 1 of your opponent's Characters with a cost of 4 or less. |
| ST12-003 | Dracule Mihawk | [On Play] ⚠targeting-qualifier | If you have 2 or less Characters, play up to 1 {Muggy Kingdom} type or ＜Slash＞ attribute Character card with a cost of 4 or less other than [Dracule Mihawk] … |
| ST12-007 | Rika | [On Play] ⚠targeting-qualifier | ➁ (You may rest the specified number of DON!! cards in your cost area.): If your opponent has 3 or more Life cards, set up to 1 of your ＜Slash＞ attribute Cha… |
| ST12-008 | Roronoa Zoro | [DON!! x1] [When Attacking] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 6 or less. |
| ST12-010 | Emporio.Ivankov | [On Play] ⚠targeting-qualifier | Reveal 1 card from the top of your deck and play up to 1 Character card with a cost of 2. Then, place the rest at the top or bottom of your deck. |
| ST12-010 | Emporio.Ivankov | [When Attacking] [Once Per Turn] ⚠embedded-condition | Draw 1 card if you have 6 or less cards in your hand. |
| ST12-011 | Sanji | [DON!! x1] [When Attacking] ⚠embedded-condition | If you have 5 or less cards in your hand, this Character gains +2000 power until the start of your next turn. |
| ST12-013 | Zeff | [When Attacking] ⚠targeting-qualifier | Reveal 1 card from the top of your deck and play up to 1 Character card with a cost of 2 rested. Then, place the rest at the top or bottom of your deck. |
| ST13-001 | Sabo | [DON!! x1] [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may add 1 of your Characters with a cost of 3 or more and 7000 power or more to the top of your Life cards face-up: Up to 1 of your Characters gains +200… |
| ST13-002 | Portgas.D.Ace | [DON!! x2] [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | Look at 5 cards from the top of your deck and add up to 1 Character card with a cost of 5 to the top of your Life cards face-up. Then, place the rest at the … |
| ST13-003 | Monkey.D.Luffy | [DON!! x2] [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | You may trash 1 card from your hand: If you have 0 Life cards, add up to 2 Character cards with a cost of 5 from your hand or trash to the top of your Life c… |
| ST13-005 | Emporio.Ivankov | [On Play] ⚠targeting-qualifier | You may trash 1 card from the top or bottom of your Life cards: Reveal up to 1 Character card with a cost of 5 from your hand and add it to the top of your L… |
| ST13-006 | Curly.Dadan | [On Play] ⚠targeting-qualifier | Play up to 1 each of [Sabo], [Portgas.D.Ace], and [Monkey.D.Luffy] with a cost of 2 from your hand. |
| ST13-007 | Sabo | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Reveal 1 card from the top of your Life cards. If that card is a [Sabo] with a cost of 5, you may play that card. If you do, up… |
| ST13-008 | Sabo | [On Play] ⚠targeting-qualifier | You may trash 1 card from the top or bottom of your Life cards: K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| ST13-009 | Shanks | [On Play] ⚠embedded-condition | You may turn 1 of your face-up Life cards face-down: If your opponent has 7 or more cards in their hand, trash up to 1 card from the top of your opponent's L… |
| ST13-010 | Portgas.D.Ace | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Reveal 1 card from the top of your Life cards. If that card is a [Portgas.D.Ace] with a cost of 5, you may play that card. If y… |
| ST13-011 | Portgas.D.Ace | [On Play] ⚠embedded-condition | If you have 2 or less Life cards, this Character gains [Rush]. |
| ST13-013 | Monkey.D.Garp | [On Play] ⚠targeting-qualifier | Look at 5 cards from the top of your deck; reveal up to 1 [Sabo], [Portgas.D.Ace], or [Monkey.D.Luffy] with a cost of 5 or less and add it to your hand. Then… |
| ST13-014 | Monkey.D.Luffy | [Activate: Main] ⚠targeting-qualifier | You may trash this Character: Reveal 1 card from the top of your Life cards. If that card is a [Monkey.D.Luffy] with a cost of 5, you may play that card. If … |
| ST13-015 | Monkey.D.Luffy | [Activate: Main] [Once Per Turn] ⚠embedded-condition | This Character gains +2000 power until the start of your next turn. Then, if you have 1 or more Life cards, draw 1 card and trash 1 card from the top of your… |
| ST14-002 | Usopp | [DON!! x1] [When Attacking] ⚠targeting-qualifier | If you have a Character with a cost of 8 or more, K.O. up to 1 of your opponent's Characters with a cost of 4 or less. |
| ST14-003 | Sanji | [On Play] ⚠targeting-qualifier | If you have a Character with a cost of 6 or more, K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| ST14-006 | Nami | [On Play] ⚠targeting-qualifier | If you have 6 or less cards in your hand and a Character with a cost of 8 or more, draw 1 card. |
| ST14-007 | Nico Robin | [On Play] [When Attacking] ⚠targeting-qualifier | If you have a Character with a cost of 8 or more, give up to 1 of your opponent's Characters −5 cost during this turn. |
| ST14-008 | Haredas | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: Up to 1 of your black {Straw Hat Crew} type Characters gains +2 cost until the end of your opponent's next turn. Then, if you ha… |
| ST14-014 | Gum-Gum Giant Rifle | [Trigger] ⚠targeting-qualifier | Add up to 1 of your Character cards with a cost of 2 or less from your trash to your hand. |
| ST14-015 | Gum-Gum Diable Three-Swords Style Mouten Jet Six Hundred Pound Phoenix Cannon | [Trigger] ⚠targeting-qualifier | If you have a Character with a cost of 8 or more, K.O. up to 1 of your opponent's Characters with a cost of 5 or less. |
| ST14-016 | I Have My Crew!! | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| ST14-017 | Thousand Sunny | [On Play] ⚠embedded-condition | If your Leader has the {Straw Hat Crew} type, draw 1 card. |
| ST15-001 | Atmos | [When Attacking] ⚠embedded-condition | If your Leader is [Edward.Newgate], you cannot add Life cards to your hand using your own effects during this turn. |
| ST15-002 | Edward.Newgate | [Activate: Main] ⚠targeting-qualifier | You may rest this Character: K.O. up to 1 of your opponent's Characters with 5000 power or less. |
| ST15-004 | Thatch | [On Play] ⚠embedded-condition | If your Leader's type includes "Whitebeard Pirates", give up to 1 of your opponent's Characters −2000 power during this turn. Then, add 1 card from the top o… |
| ST17-002 | Trafalgar Law | [On Play] ⚠targeting-qualifier | You may return 1 of your Characters to the owner's hand: If your Leader has the {The Seven Warlords of the Sea} type, return up to 1 Character with a cost of… |
| ST18-001 | Uso-Hachi | [On Play] ⚠targeting-qualifier | If you have 8 or more DON!! cards on your field, rest up to 1 of your opponent's Characters with a cost of 5 or less. |
| ST18-002 | O-Nami | [On Play] ⚠embedded-condition | If you have 8 or more DON!! cards on your field, trash 1 card from your hand and draw 2 cards. |
| ST18-003 | San-Gorou | [When Attacking] [Once Per Turn] ⚠embedded-condition | If you have 8 or more DON!! cards on your field, draw 1 card. |
| ST18-005 | Luffy-Tarou | [On Play] ⚠targeting-qualifier | DON!! −1 (You may return the specified number of DON!! cards from your field to your DON!! deck.): Play up to 1 purple {Straw Hat Crew} type Character card w… |
| ST19-001 | Smoker | [On Play] ⚠targeting-qualifier | You may trash 1 black {Navy} type card from your hand: Up to 2 of your opponent's Characters with a cost of 4 or less cannot attack until the end of your opp… |
| ST19-002 | Sengoku | [On Play] ⚠embedded-condition | You may trash 2 black {Navy} type cards from your hand: If your Leader has the {Navy} type, draw 3 cards. |
| ST19-003 | Tashigi | [On Play] ⚠embedded-condition | If your Leader is [Smoker], give up to 1 of your opponent's Characters −4 cost during this turn. |
| ST19-003 | Tashigi | [Activate: Main] [Once Per Turn] ⚠targeting-qualifier | If this Character was played on this turn, trash up to 1 of your opponent's Characters with a cost of 0. |
| ST20-004 | Charlotte Pudding | [On Play] ⚠targeting-qualifier | You may add 1 card from the top of your Life cards to your hand: Set up to 1 of your {Big Mom Pirates} type Characters with a cost of 3 or less as active. |
| ST20-004 | Charlotte Pudding | [Trigger] ⚠targeting-qualifier | Rest up to 1 of your opponent's Characters with a cost of 3 or less. |
| ST21-003 | Sanji | [On Play] ⚠targeting-qualifier | Select up to 1 of your {Straw Hat Crew} type Characters with 6000 power or more. If the selected Character attacks during this turn, your opponent cannot act… |
| ST21-010 | Nico Robin | [DON!! x2] [When Attacking] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 4000 power or less. |
| ST21-015 | Roronoa Zoro | [On K.O.] ⚠targeting-qualifier | Play up to 1 red Character card with 6000 power or less other than [Roronoa Zoro] from your hand. |
| ST21-016 | Gum-Gum Dawn Whip | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 4000 power or less. |
| ST22-017 | Fire Fist | [Trigger] ⚠targeting-qualifier | Return up to 1 Character with a cost of 3 or less to the owner's hand. |
| ST23-002 | Shanks | [On Play] ⚠embedded-condition | If your Leader has the {Red-Haired Pirates} type or is [Uta], your Leader gains +2000 power until the end of your opponent's next End Phase. |
| ST23-003 | Benn.Beckman | [On Play] ⚠embedded-condition | You may trash 1 card from your hand: If your Leader has the {Red-Haired Pirates} type, K.O. up to 1 of your opponent's Characters with 4000 base power or less. |
| ST24-001 | Capone"Gang"Bege | [On Play] ⚠embedded-condition | If you have 6 or more rested cards, draw 1 card and trash 1 card from your hand. |
| ST24-004 | Law & Bepo | [On Play] ⚠embedded-condition | Rest up to 1 of your opponent's Characters and that Character will not become active in your opponent's next Refresh Phase. Then, if your opponent has 2 or m… |
| ST24-005 | X.Drake | [On Play] ⚠targeting-qualifier | If your Leader has the {Supernovas} type, rest up to 1 of your opponent's Characters with a cost of 5 or less. Then, set up to 1 of your DON!! cards as activ… |
| ST25-001 | Alvida | [On Play] ⚠embedded-condition | If your Leader is [Buggy], draw 3 cards and trash 2 cards from your hand. |
| ST25-003 | Crocodile & Mihawk | [On Play] ⚠targeting-qualifier | Draw 2 cards and trash 1 card from your hand. Then, play up to 1 {Cross Guild} type Character card with a cost of 4 or less from your hand. |
| ST25-004 | Buggy | [Activate: Main] ⚠targeting-qualifier | You may trash 1 card from your hand and trash this Character: If your Leader is [Buggy], play up to 1 {Cross Guild} type Character card with a cost of 6 or l… |
| ST25-005 | Mohji | [On K.O.] ⚠embedded-condition | If your Leader is [Buggy] and you have 3 or less cards in your hand, draw 1 card. |
| ST26-005 | Monkey.D.Luffy | [On Play] [When Attacking] ⚠embedded-condition | DON!! −2 (You may return the specified number of DON!! cards from your field to your DON!! deck.): If your Leader is multicolored and your opponent has 5 or … |
| ST27-001 | Avalo Pizarro | [Activate: Main] [Once Per Turn] ⚠embedded-condition | You may rest 1 of your [Fullalead] cards: If your Leader has the {Blackbeard Pirates} type, this Character gains +4000 power during this turn. |
| ST27-002 | Catarina Devon | [Activate: Main] ⚠embedded-condition | You may trash this Character: If your Leader has the {Blackbeard Pirates} type, give up to 1 of your opponent's Characters −1 cost during this turn. |
| ST27-003 | Kuzan | [On K.O.] ⚠targeting-qualifier | Play up to 1 {Blackbeard Pirates} type Character card with a cost of 5 or less from your trash rested. |
| ST28-001 | Ashura Doji | [On Play] ⚠embedded-condition | If your Leader has the {Land of Wano} type and your opponent has 3 or more Life cards, K.O. up to 1 of your opponent's Characters with a base cost of 5 or less. |
| ST28-003 | Kin'emon | [Trigger] ⚠embedded-condition | If your Leader has the {Land of Wano} type and your opponent has 3 or less Life cards, play this card. |
| ST28-005 | Yamato | [On Play] ⚠targeting-qualifier | Look at 5 cards from the top of your deck; reveal up to 1 {Land of Wano} type card with a cost of 2 or more and add it to your hand. Then, place the rest at … |
| ST29-001 | Monkey.D.Luffy | [When Attacking] ⚠embedded-condition | If you have 2 or less Life cards, draw 1 card and trash 1 card from your hand. |
| ST29-003 | Kaku | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with a cost of 3 or less. |
| ST29-005 | Jinbe | [Trigger] ⚠embedded-condition | If your Leader is [Monkey.D.Luffy], play this card. |
| ST29-008 | Nami | [Trigger] ⚠embedded-condition | If your Leader is [Monkey.D.Luffy], play this card. |
| ST29-009 | Nico Robin | [Trigger] ⚠embedded-condition | If your Leader is [Monkey.D.Luffy], play this card. |
| ST30-002 | Inazuma | [On Play] ⚠targeting-qualifier | Look at 5 cards from the top of your deck; reveal up to 1 Character card with 6000 power and add it to your hand. Then, place the rest at the bottom of your … |
| ST30-004 | Emporio.Ivankov | [On Play] ⚠targeting-qualifier | You may reveal 2 Character cards with 6000 power from your hand: Draw 3 cards and trash 2 cards from your hand. |
| ST30-006 | Jinbe | [On Play] ⚠targeting-qualifier | You may trash 1 Character card with 6000 power from your hand: Draw 2 cards. |
| ST30-008 | Marco | [On K.O.] ⚠targeting-qualifier | You may trash 1 Character card with 6000 power from your hand: Play this Character card from your trash rested. |
| ST30-015 | The Name of This Era Is "Whitebeard"!! | [Trigger] ⚠targeting-qualifier | K.O. up to 1 of your opponent's Characters with 6000 power or less. |

