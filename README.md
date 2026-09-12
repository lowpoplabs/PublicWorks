# PublicWorks

An Oxide plugin that adds a **Public Works office** to the island. Players pool their
scrap at a clerk NPC to keep municipal utilities running — built on the monument
power systems from Rust's **Power Trip** (Aug 2026) update.

The payment model is communal and demand-based: each service is a shared **bucket of
banked time**. Anyone can pay to top up any service and everyone benefits — but the
bucket drains faster the more players are online (they're using more resources).
With the default settings, up to 2 players drain at 1×; each extra player adds +25%,
capped at 3×. Bots/NPC players never count toward usage.

<!-- lpl:links -->
**[Download v2.8.2](https://github.com/lowpoplabs/PublicWorks/releases/latest)** · **Flyer:** [web](https://lowpoplabs.github.io/flyers/PublicWorks.html) / [PDF](PublicWorks-Flyer.pdf) · **[Changelog](CHANGELOG.md)** · **[Ko-fi](https://ko-fi.com/lowpoplabs)**
<!-- /lpl:links -->

## Services (100 scrap / day each, configurable)

| Service | Effect | Mechanism |
|---|---|---|
| Electricity | Power Plant & street grid stay running, poles output extra power, green recyclers run at full 60% efficiency | Force-powers Power Plant + all roadside powerline access points + (config-toggleable) the green recyclers at non-service monuments, which run at 50% recycle efficiency unpowered and 60% powered since Power Trip; scales `powerlineAvailablePower` in the shared stage config by the `PoleOutputMultiplier` (default 2×, so 60 power at max stage instead of 30) |
| Water | Water Treatment stays pressurized | Force-powers WTP + pins `WaterTreatmentWaterTank.Pressure` to max |
| Gas | Dome crude pumps stay active | Force-powers all 3 Dome pumps + synthetic oil-rig switch signal (`OnOilSwitchToggled`), no rig trip required; each pump holds up to 500 crude (configurable) and pauses until players collect |
| Markets | Supermarket freezers stay stocked, grocery shop & drone delivery open | Force-powers supermarket generators + spawns a self-restocking NPC grocery vending machine (scrap prices, config-driven stock) and a working drone marketplace at every supermarket |
| Garages | Gas station lifts, doors, fuel pumps & corner store stay powered | Force-powers Oxum's generators + spawns working fuel pumps at every gas station that produce low grade (drain with a **hose tool**) and a corner-store grocery vending machine |
| Airport | Airfield tower terminals powered and charged with no fuses; resupply (chinook) calls on a 10-min cooldown | The tower's terminals hang off two vanilla puzzle fuse boxes (generators → fuse boxes → XOR → airdrop terminal, AND → chinook terminal), none of it on the grid — so the plugin feeds each terminal's power input directly (held against the real wiring via `OnInputUpdate`), seats a locked "Public Works Fuse" in each box for the visuals, and keeps the charge meters full. A chinook call starts the air traffic controller's break (`AirportResupplyCooldownMinutes`, default 10; announced island-wide); when it ends, vanilla's 3-hour "active" window is released so the next crate can be called. Also force-powers the Airfield's grid entities |
| Internet | Satellite uplink online, no items needed | `satellite.require_powerplant false` + `free_power`/`free_fuel` (no tech trash, aiming modules, or thruster fuel; config-toggleable) |
| Transportation | Trains run without fuel | Keeps every `TrainEngine` tank fueled (instant top-up on mount); fuel hatches are sealed while active, and tanks drain to 20 on expiry so it can't be farmed for low grade |
| Protection | Patrol heli & Bradley only engage aggressors; Bradley deploys no ground crew | Hooks-only target filtering (`CanHelicopterTarget`, `OnHelicopterTarget`, `OnHelicopterStrafeEnter`, `CanBradleyApcTarget`, `CanDeployScientists`) against an in-memory hostility tracker: shoot the heli/Bradley, or hit another player within 150 m of one, and you (plus your team) are fair game for 2 min |

"Force-power" = calling `IPowergridEntity.Server_OnPowergridStageChanged(maxStage)` on
that monument's entities, re-asserted every 30s. No saved entity fields are modified;
everything reverts to vanilla on expiry or plugin unload. Vanilla heavy-fuse gameplay
keeps working in parallel — paid services just guarantee their monument stays on.

## Random fault events (v2)

Roughly once a day (configurable), something **breaks** at a random *active* service:
a transformer blows on the power grid, a water main bursts, the uplink array loses
alignment, a signal box fails at the Train Yard... A **minor** fault (75% of them by
default) drops that service to 50% output; a **major** fault is a full outage. The
bucket keeps draining at full rate either way — that's the pressure to go fix it.

- The break is announced in chat with the grid square, and a **red dot** appears on
  the map at the fault site.
- A Public Works repair crew auto-fixes it after a while (default 45 min minor /
  90 min major) — the safety valve for empty-server hours.
- Or a player beats them to it: visit the office, hit **TAKE JOB** on the faulted
  service row (open contract — everyone can take it, first to finish wins), bring
  the listed repair materials to the red dot, and press **USE** at the site.
  Materials are consumed, the service snaps back to 100%, and the fixer is paid
  **scrap** (default 75/150) plus a few bonus hours banked on that service.
- If the service expires mid-fault, the crews stand down and the contract is void.

Fault effects per service: electricity = grid stage drops (major: dark, pole boost
off); water = pressure halved/zero; gas = pump flow halved/off; markets = drone
delivery offline on a minor fault, grocery closed too on a major; garages = fuel
pump flow halved/off (plus the monument partially/fully dark); airport = monument
partially dark (major: tower terminals go dark too, back to vanilla fuses);
internet = free-items perk suspended (major: satellite fully down); transportation = trains barely get fuel (major: no fuel service, but train
fuel hatches unseal so you can refuel by hand).

Repair materials are configurable per service (`'shortname amount'` lists — majors
scale amounts by 2× by default): metal frags + gears for electricity, pipes for
water, propane tanks for gas, fuses for markets/airport, tech trash for internet, etc.

Faults survive plugin reloads (persisted in the data file with their deadlines);
a stale fault whose deadline passed while the server was down resolves on load.

## Gas station fuel pumps (v2.4)

While the **Garages** service is active, working fuel pumps run at every gas
station — spawned crude-producer entities (the Dome pump machinery) retargeted
to make **low grade fuel**. Each pump accumulates fuel on the Dome pumps'
production cadence, scaled by the `Garage pump low grade flow multiplier`
(default 0.5 = half an oil-rig-switch worth, ~1 fuel every 2 minutes), and
holds up to `Max low grade a garage pump holds` (default 250) before pausing
until someone collects.

To collect: **hold a hose tool, look at the pump, press USE** — the stored fuel
transfers to your inventory. Pressing USE without the hose tool tells the player
what they need. Stored fuel survives plugin reloads and server restarts (banked
in the data file per pump position, restored on respawn). A row of three pumps **behind the station building** ships as the
station-local default on every gas station (the same anchoring as the office — the
forecourt pumps out front are client-side scenery with no server entity, so the
working ones live round the back); `/pw setpump` adds more spots,
`/pw removepump` deletes the one you stand next to, `/pw clearpumps` removes
them all, `/pw pumpinfo` shows live production state.

The Dome pumps got matching balance controls: `Gas service: max crude a Dome
pump holds` (default 500, 0 = vanilla unlimited) stops the pumps from
accumulating thousands of crude overnight, and the flow multiplier now scales
the production *interval* rather than the per-collect amount, so fractional
values (0.25, 0.1, ...) work without integer-rounding losses.

## Shops (v2.4–2.5)

The grocery chain and drone delivery turn the retail monuments into working
economy stops. Every shop stands permanently (like the pumps) and is simply
closed to customers while its service is down:

- **Public Works Grocery** (supermarket, Markets service) — a self-restocking
  NPC vending machine selling food, drink, and meds for scrap (spent scrap is
  destroyed — same sink as the office). Fresh-food stock by default: eggs,
  bread, apples, honey, a syringe, and the drinkables.
- **Corner store** (every gas station, Garages service) — the same grocery
  machine at the station shopfront with its own road-trip stock (chocolate,
  granola, canned goods), opening and closing with the station's own service
  and fault ladder.

Each location has its own config stock list (`'shortname sellAmount
scrapPrice'` lines, capped at the game's 7 sell-order limit per machine).
- **Drone delivery** (supermarket roof, Markets service) — a real marketplace
  with terminal kiosks and delivery drones, the same machinery as the fishing
  village. Players order from any broadcasting vending machine on the map and
  a drone delivers to the supermarket. The terminal kiosks physically despawn
  while the service is down or *any* fault is active (minor faults ground the
  drones first; the walk-in grocery stays open until a major).

Default spots ship for all three (grocer inside the store, corner store at the
station, marketplace on the roof). `/pw setgrocer` saves the spot for whichever
monument type you stand in; `/pw setmarket` places the marketplace;
`/pw cleargrocer` / `/pw clearmarket` remove them.

## Drinkables (v2.5)

The **Mr Spice Can** and **Vodka Bottle** junk items are inert collectibles in
vanilla (`isUsable: false`) — PublicWorks makes them consumable and stocks them
in every grocery. The inventory panel can't grow a Drink button (that UI is
client-side item data), so there are two ways to drink:

- **`/drink`** (or `/drink vodka`) — consumes one from anywhere in the
  inventory. `/drink info` prints the menu with each drink's stats.
- **The belt swig** — put a drinkable on the tool belt, holster (empty
  hands), and **right-click**. Empty-handed right-click is unused in vanilla,
  so it's a clean chug gesture. Belt only — the backpack stash is safe from
  misclicks.

To keep it discoverable, drinkables are renamed to "Vodka Bottle (/drink)"
the moment they land in an inventory, and the first purchase from a grocer
gets a chat hint with that drink's stats.

Effects are config lines — `'shortname health healOverTime calories hydration
poisonChance [drunkSeconds]'` (healOverTime is bandage-style pending health):

- **Mr Spice Can** — +10 health, +10 healing over time, +60 calories, +80
  hydration.
- **Vodka Bottle** — +25 health, +25 healing over time, +125 calories, barely
  any hydration, ~20s of drunk screen-blur (the incapacitate-dart
  `ObscureVision` modifier, intensity configurable), and a **15% bad-batch
  chance**. A bad batch is pickle-jar roulette: you **vomit it straight back
  up** (the game's `drink_vomit` gesture, same as spoiled food) — none of the
  heal/calories/hydration/buzz land, the stomach empties (−50 calories and
  hydration, configurable), poison sets in, and the *actual snakebite debuff
  set* rides along, copied off the game's snake prefab at runtime so it looks
  identical to a jungle bite.

Note for admins: god mode blocks all negative-source modifiers, so the drunk
blur and venom won't apply while it's on — `/drink` says so when it happens.

## Protection (v2.7)

Cobalt sells air defence too. While the **Protection** service is paid, the patrol
helicopter and the Bradley APC become **reactive only** — they ignore everyone and
engage only players who provoke them — and Bradley stops deploying its scientist
ground crew when damaged. Unpaid, expired, or unloaded = pure vanilla (heli targets
anyone within 150 m holding a gun or wearing more than two clothing items; Bradley
targets any visible player within 100 m and drops scientists at its health thresholds).

"Player" here means any non-NPC `BasePlayer` — humans **and** plugin bots built on
`player.prefab` (FakeFriends' native bots carry sub-Steam ids; `ProtectionCoversBots`,
default on, keeps them covered). Scientists and dwellers are `IsNpc` and stay vanilla.

A real (non-NPC) player becomes **hostile** when they:

1. damage a patrol helicopter or a Bradley (any amount), or
2. damage another real player (sleepers count; same-team victims only if
   `ProtectionFriendlyFireCounts`) while within `ProtectionRadius` (150 m) of any
   live heli or Bradley.

Being flagged applies to **both** vehicles — Cobalt shares intel — and to every
current member of the aggressor's team ("consequences for everyone"). The flag
lasts `ProtectionHostileSeconds` (120 s), refreshed by every qualifying act, and
is **cleared when you die** (`ProtectionClearOnDeath`, default on — otherwise the
heli that just killed you is waiting over the respawn point; teammates keep their
own flags). The first time you're flagged (and once
per window after that) chat tells you why: *"Cobalt air defence has flagged you as
hostile for 2m (shot the patrol helicopter)."* Hostile players are still subject to
the vanilla rules (line of sight, the 40 m night rule) — the service only ever
*removes* targets, never adds them. The tracker is memory-only and is wiped on
expiry, on unload, and by `/pw hostile clear`.

**Billing hold** — Protection's fault has no physical site. A minor fault is a
*payment delay*: air cover lapses (heli back to vanilla) while Bradley stays
reactive-only with no ground crew. A major fault is a *frozen account*: both
vehicles vanilla. The red dot lands on the office; open the panel there and hit
**PAY BILL** on the Protection row to settle it for scrap
(`ProtectionBillingFeeMinor` / `Major`, default 50 / 100). No payout and no bonus
hours — it's a bill, nobody gets paid by Cobalt for paying Cobalt. The usual
auto-repair timers apply (Accounts clears the hold on its own eventually), and
Protection is in the random fault roll like any other active service.

## Airport tower (v2.7.5–2.7.6)

Power Trip's Airfield control tower has two terminals: the **expedited supply
dispatcher** (while active, the airdrop event ticks at 2× rate) and the **resupply
terminal** (call a Chinook to drop a locked crate on the airfield). In vanilla they
hang off two puzzle fuse boxes fed by always-on generators — fuse boxes → XOR →
airdrop terminal, AND → chinook terminal — so one fuse runs the dispatcher and two
run the resupply call (and darken the dispatcher). None of that is on the powergrid,
which is why earlier versions' "force-power the Airfield" never lit the tower.

While **Airport** is paid:

- Both terminals are **powered with no fuses** — the plugin feeds each terminal's
  power input directly and holds it against the real wiring (`OnInputUpdate`), so
  a player dropping real fuses in can't XOR the dispatcher dark. Charge meters stay
  full: the dispatcher is permanently active and the resupply terminal is ready
  the moment the controller's break ends.
- A locked **"Public Works Fuse"** sits in each tower fuse box for the visuals — the
  boxes stop sparking and light up; pulling one gives vanilla's "Item is locked in!".
  It's topped up every tick and removed on lapse.
- Pressing the resupply button calls the Chinook as usual and puts the **air
  traffic controller on a break** (`AirportResupplyCooldownMinutes`, default 10;
  0 = vanilla's 3-hour window): everyone hears *"X called in a resupply at the
  Airfield — the air traffic controller is on break, next locked crate ready in
  10 minutes."* Presses during the break are refused with the minutes left; when
  it ends the terminal is re-armed and *"Air traffic controller is back"* is
  announced. A running break survives plugin reloads.
- Minor fault: the monument dims but the tower stays up. Major fault, expiry, or
  unload: the plugin hands the real upstream state back and pulls its fuses —
  vanilla fuse play resumes exactly where it stands.

The tower's 5-light grid-stage panel reads the *island-wide* Power Plant stage
(client-side) and isn't per-monument, so a paid Airport doesn't light it.

## Player experience

- Walk up to the clerk (orange dot on the map) and press **USE (E)** — the services
  panel opens: a fuel-gauge bar per service (banked time vs the 7-day cap), the
  estimated time left at current usage, and a PAY button. The footer shows the
  current usage rate (`×2.00 (6 online)`) and your scrap balance.
- `/pw` — shows the office grid location (or opens the panel if you're at the office).
- `/pwinfo` — opens a **read-only** status panel from anywhere: every service's state,
  banked time, and any active fault (with its grid square). Paying and taking repair
  contracts still require standing at the office; the footer points you there.
- Each 100-scrap payment banks 24 hours at baseline usage; bank up to 7 days
  (configurable cap). Higher population burns the banked time faster.
- Chat broadcasts on activation ("paid for by X"), expiry, and **low-bucket warnings**
  based on time-left-at-current-usage (default 60 and 10 minutes).

## Setup

1. Copy `PublicWorks.cs` into your server's `oxide/plugins/` folder.
2. On maps with a **Radtown**, that's it — by default the office opens inside
   Radtown's interior office automatically (clerk + boombox playing the game's
   built-in "Smooth Jazz Florida" station), and re-opens there after every wipe.
3. To move it (or on maps without a Radtown), stand where the clerk should be
   as admin and run `/pw setoffice`.

The clerk is an invincible, disarmed, AI-disabled scientist. Interaction works by
USE-key detection (distance + view angle), no vending machine required.

### Monument anchoring

Placing the office (or boombox) **inside a monument** stores the spot relative to
that monument — after every map wipe it re-places itself in the same room of the
new map's instance of that monument (e.g. the Radtown office). Placements outside
monuments use absolute world coordinates and need re-placing per map.

### Clerk appearance & behavior

- **Outfit**: `Clerk outfit` config list, `shortname` or `shortname@skinId` format
  (same as RustQuests trader kits). Unknown items are skipped with a log warning.
  Default: skinned hoodie + balaclava, pants, boots.
- **Face**: `Clerk face seed` — a Steam-ID-style number picks a fixed face/gender
  (clients derive appearance from it deterministically). The default ships the
  department's stock clerk face; `0` = random each spawn. Nudge the seed and
  `/pw setclerk` to audition faces.
- **Facing**: the clerk turns toward the nearest player within `Clerk turns to
  face players within this range` meters (0 disables) and returns to his placed
  rotation when alone.
- The clerk is pinned to his exact placement spot — the scientist prefab's nav
  agent would otherwise warp him to the nearest navmesh (roofs, roads).

### Office boombox

`/pw setboombox` where the radio should sit — it spawns a deployed boombox
(monument-anchored, indestructible, auto-restarting if toggled off) tuned to the
`Office boombox radio station` setting. That takes either a **station name** from
the game's boombox station lists (case-insensitive, e.g. `WEFUNK` or
`Smooth Jazz Florida` — the default) or a raw stream URL. Names are resolved
against the built-in and server station lists at spawn time; for a custom stream
URL, also add it to the server's `boombox.serverurllist` convar so clients are
allowed to play it. `Office boombox height offset` raises the spawn point
(e.g. ~1.1 to sit on a file cabinet).

Admins can also retune it in-game: open the boombox's vanilla radio UI and pick
a station — the choice is saved to config (as the station name when the pick
maps to one) and playback restarts on the new tune. Non-admins who try get told
the radio is department property.

### Office building (CopyPaste)

Requires the [CopyPaste](https://umod.org/plugins/copy-paste) plugin. Build the
office once, stand at its center, `/copy publicworks_office`, then:

- `/pw pastefile publicworks_office` — tell PublicWorks which build to use
- `/pw setoffice` — pastes the building at your position/facing AND places the clerk
- `/pw setclerk` — move just the clerk (e.g. behind the office desk)

Pasted entities are tracked in the data file; `/pw removeoffice` removes the clerk,
marker, and the entire pasted building, even across restarts.

## Permissions

- `publicworks.admin` — grants access to all admin commands below for players who
  aren't built-in server admins (auth level admins always have access):

  ```
  oxide.grant user "PlayerName" publicworks.admin
  oxide.grant group admin publicworks.admin
  ```

- Players need **no permission** to use the clerk, open the panel, or pay for
  services — only admin commands are gated.

## Admin commands

- `/pw setoffice` — paste office building (if configured) + place clerk where you stand
- `/pw setclerk` — reposition only the clerk
- `/pw setboombox` — place/move the office boombox where you stand
- `/pw pastefile <name>` — set/clear the CopyPaste build used for the office
- `/pw removeoffice` — remove clerk, map marker, boombox, and pasted building
- `/pw setpump` — add a fuel pump spot where you stand (inside a gas station; applies to all stations)
- `/pw removepump` — delete the pump spot you stand next to
- `/pw clearpumps` — remove all fuel pump spots
- `/pw pumpinfo` — live production state of the nearest fuel pump
- `/pw setgrocer` / `/pw cleargrocer` — place/remove the grocery machine (supermarket or gas station, whichever you stand in)
- `/pw setmarket` / `/pw clearmarket` — place/remove the supermarket drone marketplace
- `/pw whatsthis` — identify the entity (or static scenery) you're looking at
- `/pw grant <service|all> [days]` / `/pw revoke <service|all>` — free grant/revoke
- `/pw fault <service> [minor|major]` — force a fault event (testing; service must be active)
- `/pw clearfault <service|all>` — resolve fault(s) immediately (announced as a crew fix)
- `/pw hostile` — list players currently flagged hostile by Protection (seconds remaining); `/pw hostile clear` wipes the tracker
- `/pw perf` — service-tick cost stats (last/avg/max ms, per-phase breakdown of the last tick, tracked entity counts); ticks slower than `Log a warning when a service tick takes longer than this many ms` (default 25) are also logged

The old `/pw satreset` (clears the satellite computer's "RECALIBRATING" cooldown)
now lives in the separate **PWSatReset** plugin (`/satreset`, permission
`pwsatreset.use`). It needs reflection into private game state, which uMod
disallows — so it stays a private server utility and is not part of the
PublicWorks submission.

## Configuration

`oxide/config/PublicWorks.json`: price per day, hours per payment, prepay cap,
clerk name, interaction range, map marker, broadcasts, warning lead times, tick
rate, gas flow multiplier and Dome pump crude cap, the garage pump toggles
(flow multiplier, per-pump fuel cap, station-local spots), the shop extras
(grocer toggle/name, per-location stock lists, supermarket + gas station
spots, drone marketplace toggle/spot), the drinkables (effect lines, poison
amount, drunk blur intensity), satellite
item-free toggle, powerline pole output multiplier, the green-recycler power
ride-along toggle, the Airfield resupply cooldown (`AirportResupplyCooldownMinutes`), the Protection knobs (`ProtectionRadius`,
`ProtectionHostileSeconds`, `ProtectionShareWithTeam`,
`ProtectionFriendlyFireCounts`, `ProtectionClearOnDeath`, `ProtectionCoversBots`,
`ProtectionBillingFeeMinor/Major`), and the burn model — free-player threshold, extra burn per
player, max burn multiplier, and the bot Steam ID threshold.

Fault events add: average hours between faults (0 disables), max concurrent,
minimum gap, major-fault chance, auto-repair minutes per severity, scrap rewards
per severity, banked-hours bonus, major material multiplier, repair interaction
range, and the per-service repair material lists.

Banked service time, pasted building entity IDs, active faults, and a running
Airfield resupply break persist in `oxide/data/PublicWorks.json` (pre-1.4 expiry timers migrate automatically).

## Localization

All chat and panel text goes through the Oxide **Lang API**. English defaults are
generated at `oxide/lang/en/PublicWorks.json` on first load — edit that file (or
add other language folders) to customize or translate every message, including
the per-service fault flavor lines ("a transformer blew", ...), service names,
and UI button labels.

## Support

Provided as-is. Bug reports welcome via GitHub Issues. No Discord, no custom work, no promises on turnaround. If it saved you time or you and your players enjoy it:

[![Ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/lowpoplabs)

## License

MIT — see [LICENSE](LICENSE).

## Compatibility notes

- Built against the September 3, 2026 Rust update (build 2633.288). Versions from 2.8.1 onward need that build or newer; the previous version is the last one that compiles on August builds.
- Maps without an Airfield: the Airport service is simply inert until a map has one.
- The 5-light grid-stage panel in the Airfield tower reads the *island-wide* Power Plant
  stage (client-side); it isn't per-monument, so a paid Airport doesn't light it.
- The Launch Site satellite computer checks *global* grid stage; the Internet service
  bypasses it via convar rather than local power.
- Fuse-related convar changes are restored to their pre-plugin values on unload.
- No reflection into game internals: the pole-output boost uses the public
  `PowergridStageConfig.TryGetStageDataForStage` API, and the drone terminals
  are managed through the public `MarketTerminal.Setup` path. When the Gas
  service deactivates, dome pump flow is set to 0 (and the prefab collect
  interval restored) and self-heals to the true value on the next genuine
  oil-rig switch transition.
- All spawned entities (pumps, grocer, marketplace, clerk, boombox) are
  `enableSaving = false` — nothing leaks into the server save; everything is
  killed on unload and respawned on load (pump fuel is banked in the data file
  across the gap).
- The boombox and grocery machines are immune to ground-missing destruction
  (raw-spawned deployables can ground-snap onto a loot crate collider through
  a wall and die when it despawns), and the boombox respawns itself within a
  tick if anything unforeseen kills it.
- Protection is hooks-only (`CanHelicopterTarget`, `OnHelicopterTarget`,
  `OnHelicopterStrafeEnter`, `CanBradleyApcTarget`, `CanDeployScientists`) plus
  the existing `OnEntityTakeDamage` handler for hostility detection — no Harmony,
  no saved entity fields, no convar changes. Every hook reads the live service and
  fault state, so heli and Bradley revert to vanilla the instant the service
  expires, a major fault lands, or the plugin unloads; the hostility tracker is
  memory-only and is cleared at the same moments. Hostile players are never
  force-targeted — the hooks only *remove* targets, so the vanilla line-of-sight
  and night rules stay in charge.
- Optional soft dependency: [CopyPaste](https://umod.org/plugins/copy-paste), only
  needed if you want the office building paste feature.

## Clothing skin creators

The clerk's default uniform is built from Steam Workshop clothing skins, shipped
in the default `Clerk outfit` config (`shortname@skinId` entries — swap them
there to recast him). Shoutout to the creators whose work dresses the clerk:

- **MrM** —
  [Hi-Vis Hoodie](https://steamcommunity.com/sharedfiles/filedetails/?id=3435148685),
  the department's regulation high-visibility workwear
- **MissLisa** —
  [Brown Hair](https://steamcommunity.com/sharedfiles/filedetails/?id=3141449679)
  skinned balaclava, the clerk's hair

---

*An Oxide plugin by LowPopLabs.*
