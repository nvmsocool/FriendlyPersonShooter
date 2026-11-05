# Raid dev plan

How raids grow from one boss fight into runs that feel different every time. The design rules that
result go in `GameDesignDocument.md` (the raid section) as each phase lands; this file tracks the build
order and status.

## The shape of a run

- A run is **3 random rooms, then a boss**, re-rolled every time the door opens: room order and
  kinds, the boss, and one run modifier.
- Each room is its own floating platform. Solving it opens a **portal**; when someone steps in, a
  short countdown starts and then everyone moves on together (anyone down is picked back up).
- Failing a room resets it and costs time. Only a wipe or the run timer ends the raid.
- Rooms scale to the group (4–8 players): more plates, longer sequences, more targets.
- Each solved room pays everyone inside a little XP and essence, so a run that times out at the boss
  still paid something. Clearing the boss pays as before (XP, Yellow, a rare-or-better item).
- Every room must work for strangers without voice chat (HUD and in-world cues say what to do),
  must reward shooting allies or accurate aim, and must be clearable by bots so it can be tested.

## Room pool

| Room | Kind | The idea | Phase |
|---|---|---|---|
| Color Locks | Puzzle | Crystals (Yellow, Red, Green) must be shot in the order an altar shows; each hit must come from a different player than the last | 1 |
| Hold the Plates | Puzzle | About half the group holds plates at once to fill a bar while enemies go for whoever is standing on them | 1 |
| Sweeper Gauntlet | Skill | Jump across stepping stones while low sweeper arms turn; everyone up must reach the far pad | 1 |
| Wisp Storm | Skill | Kill a target count of Wisps before the timer runs out (the Wisps don't shoot: a pure aim check) | 1 |
| Echo Runes | Puzzle | Runes flash a sequence to shoot back, two sets at once so the group splits | 2 |
| Hold the Core | Skill | Defend a crystal for 60 s against waves from three sides | 2 |
| Lanterns | Puzzle | A dark room: payload hits make allies glow, and enemies can only be hurt near a glowing player | 3 |
| Sentinel Eye | Skill | A small eye opens for half a second at random spots on a turning shell; precision under fire | 3 |

## Bosses

| Boss | The idea | Phase |
|---|---|---|
| Bulwark | The current boss: an armored shell with a turning plate that opens for full damage, add waves at 75/50/25% | 1 (as the finale) |
| The Twins | Two bosses that must die within 10 s of each other or the first revives | 2 |
| The Weaver | The platform falls away tile by tile; tethers between players must be shot apart | 3 |

## Run modifiers (one per run)

| Modifier | Effect | Phase |
|---|---|---|
| Swarming | Twice the adds; the Wisp Storm needs 4 more kills | 1 |
| Swift Bolts | Enemy bolts fly 40% faster | 1 |
| Brittle | Players take 30% more damage; rooms pay 50% more | 1 |
| Low Gravity | Higher, floatier jumps | 3 |
| Fog | Short sight lines | 3 |

## Phases

### Phase 1: the framework and four rooms (for the Nov 5 playtest)
- [x] Core: `RaidRun` (seeded pick of 3 rooms from the pool, boss stage, modifier, portal, room payouts) with tests.
- [x] Core: rules for Color Locks, Hold the Plates, Sweeper Gauntlet and Wisp Storm, with tests.
- [x] Core: room layouts (platforms, crystals, plates, stones, sweepers, portal), one fixed platform per room kind.
- [x] Server: room steps, the portal, crystal hits from shots, room payouts, modifiers, the Bulwark as the last stage.
- [x] Client: room platforms and features drawn, HUD stage line and objectives, announcements.
- [x] Bots: shoot crystals in order, hold plates, cross the gauntlet, walk into portals. They solve Color Locks and the plates on their own.
- [x] Run timer 8 → 12 minutes. Design doc raid section updated.
- [x] Full runs in the Editor with 3 bots: Gauntlet 25 s, Hold the Plates 27 s, Color Locks 13 s, Wisp Storm 67 s. The bots reached the Bulwark but wiped at about 25% boss health (3 bots against a boss scaled for 4, Swift Bolts on), so the boss clear is left to the playtest.

Tuning after the first Editor runs: the storm needs 8 + 2 per player kills in 90 s (was 10 + 3 in 75 s) and its Wisps don't shoot. Gauntlet hits are 6 and 5 (was 10 and 8) and can't take anyone below 10 health. Every portal heals everyone, so a hard room doesn't doom the next. Sweepers turn around their stone's centre, and standing on the pivot always gets you hit, so cross beside it (bots aim 1.3 m off centre).

### Phase 2: more variety
- Echo Runes and Hold the Core rooms.
- The Twins boss; the boss is picked per run.
- Room layout variants (seeded placement of crystals, plates and stones), so a room repeats with a new shape.

### Phase 3: the strange rooms
- Lanterns and Sentinel Eye rooms.
- The Weaver boss.
- Client-side modifiers: Low Gravity and Fog.

### Phase 4: tuning from playtests
- Room timings, payouts and group scaling from playtest notes.
- Sounds and stronger in-world cues for each room.
- Harder room tiers for deeper layers.
