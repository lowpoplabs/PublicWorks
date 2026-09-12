# Changelog

All notable changes to PublicWorks are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/),
and versions follow [Semantic Versioning](https://semver.org/).

## [2.8.2] - 2026-09-12

First public release on GitHub — no gameplay changes.

### Added
- One-line load message naming the author and tip jar (`PublicWorks v2.8.2 loaded - by LowPopLabs - ko-fi.com/lowpoplabs`).
- README Support section with the Ko-fi button; support posture is as-is, issues welcome.

### Changed
- README skin credits name the creators and link their Workshop items, without Steam profile links.

## [2.8.1] - 2026-09-03

### Fixed
- Compiles again on the September 3, 2026 Rust update (build 2633.288). The game removed the per-stage `powerlineAvailablePower` value that the pole output multiplier scaled.
- Water service pressure now uses the renamed `WaterTreatmentWaterTank.maximumPressure` (the game raised it from 300 to 360), so a fully paid water service fills the tank to the new maximum.

### Changed
- The **pole output multiplier** now scales the game's two new server convars instead: `powergrid.powerlinebasepoweroutput` (output with one heavy fuse, default 5) and `powergrid.powerlinemaxpoweroutput` (output with every fuse, default 50). Poles compute their output live from the power plant's fuse count between those two numbers, so the boost applies at every stage as before. Both convars are saved by the game, so the plugin restores the originals on unload; if the server is stopped mid-boost, check `serverauto.cfg` for scaled values.

## [2.8.0] - 2026-08-30

### Added
- The office boombox setting now accepts a **station name** from the game's boombox station lists (case-insensitive, Island Taxi convention — e.g. `WEFUNK`) as well as a raw stream URL. The config key changed to `Office boombox radio station (station-list name or stream URL; empty = no boombox)`; a value stored under the old key is migrated automatically. Default is now the station name `Smooth Jazz Florida` instead of its hardcoded stream URL, so it tracks whatever URL Facepunch currently publishes for that station.
- An admin retune through the vanilla boombox UI now saves the readable station name to config (instead of the stream URL) when the picked URL maps to a listed station.

### Changed
- Default clerk outfit is now the department uniform used on the reference install: MrM's Hi-Vis Hoodie (`hoodie@3435148685`), MissLisa's Brown Hair balaclava (`mask.balaclava@3141449679`), pants, boots (was collared shirt, pants, boots). Skin creators are credited in the README.
- Default clerk face seed now ships the department's stock clerk face instead of `0` (random each spawn); set it back to `0` for a random face.

## [2.7.9] - 2026-08-25

### Fixed
- Killed drone marketplace terminals no longer leak entries in the game's save list, which spammed "Entity is NULL but is still in saveList - not destroyed properly? marketterminal" on every server save after the Markets drone service closed (lapse, fault, or plugin reload). The terminals auto-spawn with saving enabled when the marketplace spawns; the plugin now removes them from the save list with `EnableSaving(false)` instead of writing the field, which left them registered.

## [2.7.8] - 2026-08-25

### Fixed
- An invalid config file (JSON typo) is no longer overwritten with defaults, which silently wiped every customized setting. The plugin now runs on defaults for the session, leaves the broken file untouched on disk, logs an error naming the file, and refuses all config writes (including from admin commands like `/pw setoffice`) until the file is repaired and the plugin reloaded.

## [2.7.7] - 2026-08-25

### Added
- Services can be disabled via the new `Disabled services` config list (service keys, e.g. `["gas", "internet"]`). A disabled service still shows in the office CUI, but grayed out with "Cobalt has deemed this service non-essential" in place of its description and NOT ESSENTIAL in place of the gauge — no pay button. Disabled services can't be purchased or admin-faulted, never fault randomly, and any banked time freezes (no drain) until the service is re-enabled. Stored faults on a service that gets disabled are dropped on load.

## [2.7.6] - 2026-08-23

### Fixed
- The Airfield airdrop terminal's "residual" meter stays pinned at 240/240 while Airport is paid instead of counting down through its hour-long active window (the 2× airdrop rate never lapses).

## [2.7.5] - 2026-08-23

### Added
- Airport: resupply (chinook) calls now put the air traffic controller on a break (`AirportResupplyCooldownMinutes`, default 10, 0 = vanilla 3-hour window). The call is announced island-wide with the minutes until the next locked crate; presses during the break are refused with the time left; when it ends the terminal is re-armed and "Air traffic controller is back" is announced. A running break survives plugin reloads.
- Airport: a locked "Public Works Fuse" sits in each tower fuse box while the service is paid — the boxes stop sparking and light up, and pulling the fuse gives vanilla's "Item is locked in!".

### Fixed
- Airport: the tower terminals now actually show powered while paid. They hang off two vanilla puzzle fuse boxes (not the powergrid), so the service only ever filled their charge meters — screens read NO POWER, and the chinook call button only worked for about a second after each tick because the meter bled below 600 between top-ups. The plugin now feeds each terminal's power input directly, holds it against the real wiring (two real fuses would otherwise XOR the airdrop terminal dark), and hands the real upstream state back on lapse, major fault, or unload.

### Changed
- Airport service description is now "Airfield terminals powered and charged, no fuses needed".

## [2.7.4] - 2026-08-22

### Fixed
- Protection now covers bot players spawned as real `BasePlayer`s (e.g. FakeFriends in native-bot mode) — the patrol heli was still locking onto and killing them while the service was paid. `ProtectionCoversBots` (default on) can restore the old Steam-players-only behaviour.

## [2.7.3] - 2026-08-22

### Changed
- A bad batch of vodka now comes back up the way the game's own pickle jar does: no heal, calories, hydration, or blur land; the vomit gesture plays, calories and hydration drop by a configurable 50, and poison plus the snakebite debuffs apply. Bad-batch chat line updated.

## [2.7.2] - 2026-08-22

### Added
- `/pw perf` prints last/average/max service-tick cost and tracked entity counts; ticks over a configurable threshold (default 25 ms) log a per-phase breakdown.

## [2.7.1] - 2026-08-22

### Fixed
- Dying clears your Protection hostile flag (`ProtectionClearOnDeath`, default on) — the heli no longer follows a flagged player to their respawn point. Teammates keep their own flags.

## [2.7.0] - 2026-08-22

### Added
- Protection service (ninth service): while paid, the patrol helicopter and Bradley APC only engage players flagged hostile — shoot the heli/Bradley, or hit another player within 150 m of one, and you (plus your team) are fair game for 2 minutes — and Bradley never deploys its scientist ground crew. Hooks-only, so everything reverts to vanilla on expiry, major fault, or unload.
- Protection faults are a "billing hold" at the office: minor = air cover lapses (heli vanilla, Bradley still reactive), major = account frozen (both vanilla); the panel row gets a PAY BILL button (50/100 scrap). New config keys for radius, hostile seconds, team share, friendly fire, and billing fees; `/pw hostile [clear]`; a "flagged as hostile" chat notice.

### Changed
- Office panel rows shrink so nine services fit.
- Docs: the default garage pump spots sit behind the station building, not on the forecourt (the forecourt pumps are client-side scenery).

## [2.6.0] - 2026-08-21

### Added
- Belt swig: empty-handed right-click with a drinkable on the tool belt takes a drink.
- `/drink info` prints the menu; the purchase hint includes each item's stats.

### Changed
- Drink stats split into instant health plus bandage-style heal over time (vodka 25/25/125 cals, spice 10/10/60 cals/80 hydration).
- Garage pump fuel is banked across plugin reloads and restored per pump position.
- Garages UI description mentions the corner store.

### Fixed
- Pump drain targets the pump under your crosshair instead of the first pump in range (three pumps 1.5 m apart often drained an empty neighbour).
- The office boombox and grocery machines survive "ground missing" destruction from loot-crate colliders through walls; the boombox respawns itself if killed.
- Admin boombox retunes persist.

## [2.5.1] - 2026-08-21

### Added
- Supermarket and gas-station grocers carry separate config-driven stock lists (fresh food and meds at the market, road snacks at the corner store, vodka/soda/syringe at both).
- Admins can retune the office boombox through the vanilla radio UI; the pick persists and playback auto-restarts.

### Fixed
- Non-admin players can no longer change the office radio station mid-play.

## [2.5.0] - 2026-08-21

### Added
- Garages: a corner-store grocery vending machine at every gas station, with its own service gating and fault ladder.
- Drinkables: `mrspice.can` and `bottle.vodka` are consumable via `/drink` and stocked in every grocery — configurable health/calories/hydration, vodka applies a vision blur, and a 15% bad batch adds poison plus the snake's bite debuffs. Items auto-rename to advertise `/drink`; first purchase gets a chat hint.
- `/pw removepump` deletes the pump spot you stand next to; `/pw setgrocer` works at either monument type.

### Changed
- Default pump layout is a row of three per gas station.

## [2.4.0] - 2026-08-21

### Added
- Garages: working fuel pumps at every gas station that produce low grade (hose tool + USE to drain, 250 cap).
- Markets: a self-restocking NPC grocery vending machine (config-driven stock, scrap sink) and a real drone marketplace on the supermarket roof; both persist while closed. Fault ladder: minor grounds the drones, major shutters the store.
- Admin commands: `setpump`/`clearpumps`/`pumpinfo`, `setgrocer`/`cleargrocer`, `setmarket`/`clearmarket`, `whatsthis`.

### Changed
- Gas: Dome pumps capped at 500 crude; flow now scales the collect interval instead of the rounded per-collect amount, so fractional multipliers work.

## [2.3.0] - 2026-08-18

### Added
- Electricity service also powers green recyclers at non-service monuments (50% → 60% recycle efficiency, plus the max-stage speed buff); config toggle, default on. A major electricity outage drops them back to 50%.

## [2.2.1] - 2026-08-16

### Changed
- Radtown office/boombox anchors ship as defaults; vanilla Smooth Jazz Florida is the default station.
- Added the 512×512 plugin icon for the uMod submission.

## [2.2.0] - 2026-08-16

### Changed
- uMod compliance: all player/admin chat and UI text moved to the Lang API (translatable via `oxide/lang`); zero reflection (pole boost rewritten on the public stage-config API, `/pw satreset` split out into the private PWSatReset plugin); MIT license added.
- Office map marker radius halved.
- Clerk NPC prefab paths are configurable; `/pw grant` parses days safely.

### Fixed
- Gas deactivation pushes zero flow (self-heals on the next real rig-switch broadcast).
- Performance: entity-spawn indexing filters by type before scheduling, and the expiry warning check no longer allocates per tick.

## [2.1.1] - 2026-08-16

### Fixed
- `/pwinfo` is strictly read-only away from the office; map marker visibility fixes; the office hint names the monument.

## [2.1.0] - 2026-08-16

### Added
- `/pwinfo` read-only status panel usable from anywhere.

## [2.0.0] - 2026-08-16

### Added
- Random fault events with open repair contracts: faults break out at service monuments (minor = 50% output, major = full outage), anyone can take the contract at the office, and the department's own crew auto-fixes at the deadline.
- PowerTripControl companion plugin (admin powergrid control panel).
