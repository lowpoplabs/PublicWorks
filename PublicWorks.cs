using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("PublicWorks", "LowPopLabs", "2.8.2")]
    [Description("A Public Works office: pay a clerk NPC scrap to keep island utilities running — power, water, gas, markets, garages, airport, internet, free trains, and Cobalt protection (reactive-only patrol heli & Bradley). Random faults break out at monuments; players take repair contracts from the office to fix them for scrap.")]
    public class PublicWorks : RustPlugin
    {
        [PluginReference] private Plugin CopyPaste;

        private const string PermAdmin = "publicworks.admin";
        private const string UiPanelName = "PublicWorks.Panel";
        private const string ScrapShortname = "scrap";

        private const string NpcPrefab = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_cinematic.prefab";
        private const string NpcPrefabFallback = "assets/rust.ai/agents/npcplayer/humannpc/scientist/scientistnpc_roamtethered.prefab";
        private const string MarkerPrefab = "assets/prefabs/tools/map/genericradiusmarker.prefab";

        #region Services

        private static readonly string[] ServiceKeys =
        {
            "electricity", "water", "gas", "markets", "garages", "airport", "internet", "transportation", "protection"
        };

        // Protection is billed, not repaired: its fault is a "billing hold" settled at
        // the office for a scrap fee (no site, no materials, no payout).
        private const string ProtectionKey = "protection";

        private static bool IsService(string key) => Array.IndexOf(ServiceKeys, key) >= 0;

        #endregion

        #region Lang

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["Prefix"] = "<color=#f9a825>Public Works</color>: ",

                ["Label.electricity"] = "Electricity",
                ["Label.water"] = "Water",
                ["Label.gas"] = "Gas",
                ["Label.markets"] = "Markets",
                ["Label.garages"] = "Garages",
                ["Label.airport"] = "Airport",
                ["Label.internet"] = "Internet",
                ["Label.transportation"] = "Transportation",
                ["Label.protection"] = "Protection",

                ["Desc.electricity"] = "Power Plant, street grid & recyclers stay powered",
                ["Desc.water"] = "Water Treatment stays pressurized",
                ["Desc.gas"] = "Dome crude pumps stay active",
                ["Desc.markets"] = "Supermarket freezers, grocery shop & drone delivery stay open",
                ["Desc.garages"] = "Gas station lifts, doors, fuel pumps & corner store stay powered",
                ["Desc.airport"] = "Airfield terminals powered and charged, no fuses needed",
                ["Desc.internet"] = "Satellite uplink stays online",
                ["Desc.transportation"] = "Trains run without fuel",
                ["Desc.protection"] = "Patrol heli & Bradley only engage aggressors; Bradley deploys no ground crew",

                ["Flavor.electricity"] = "a transformer blew",
                ["Flavor.water"] = "a water main burst",
                ["Flavor.gas"] = "a crude pump seized",
                ["Flavor.markets"] = "a freezer breaker tripped",
                ["Flavor.garages"] = "the shop wiring shorted out",
                ["Flavor.airport"] = "a terminal power feed failed",
                ["Flavor.internet"] = "the uplink array lost alignment",
                ["Flavor.transportation"] = "a signal box failed",
                ["Flavor.protection"] = "Cobalt Accounts put the protection account on hold",

                ["Site.electricity"] = "the power grid",
                ["Site.water"] = "Water Treatment Plant",
                ["Site.gas"] = "the Dome",
                ["Site.markets"] = "the Supermarket",
                ["Site.garages"] = "Oxum's Gas Station",
                ["Site.airport"] = "the Airfield",
                ["Site.internet"] = "Launch Site",
                ["Site.transportation"] = "the Train Yard",
                ["Site.protection"] = "the Public Works office",
                ["Site.unknown"] = "a service site",

                // Protection: billing hold (the fault), hostility notices.
                ["BillMinor"] = "payment delayed — air cover suspended (patrol helicopter back to vanilla; Bradley still reactive-only)",
                ["BillMajor"] = "account frozen — all protection suspended (patrol helicopter and Bradley back to vanilla)",
                ["BillAnnounce"] = "<color=#e57373>Cobalt Accounts</color> has put the <color=#ffb74d>Protection</color> account on hold — {0}. Settle the bill at the office (<color=#ffb74d>{1}</color>, grid {2}, red dot on the map): <color=#8bc34a>{3} scrap</color>. Accounts clears it automatically in ~{4}.",
                ["BillPaid"] = "<color=#8bc34a>Protection</color> restored — <color=#8bc34a>{0}</color> settled the Cobalt bill ({1} scrap).",
                ["BillCleared"] = "Cobalt Accounts has cleared the hold — <color=#8bc34a>Protection</color> is restored.",
                ["BillNeedScrap"] = "you need {0} scrap to settle the Cobalt bill.",
                ["BillNoFault"] = "there's no billing hold on the Protection account.",
                ["HostileFlagged"] = "Cobalt air defence has flagged you as hostile for {0} ({1}).",
                ["HostileTeammate"] = "teammate {0} {1}",
                ["Hostile.heli"] = "shot the patrol helicopter",
                ["Hostile.bradley"] = "shot the Bradley APC",
                ["Hostile.player"] = "attacked {0} near Cobalt assets",
                ["HostileNone"] = "No players are currently flagged hostile.",
                ["HostileHeader"] = "Hostile players ({0}):",
                ["HostileEntry"] = "  {0} — {1}s remaining",
                ["HostileCleared"] = "Cleared {0} hostile flag(s).",
                ["UsageHostile"] = "Usage: /pw hostile [clear]",

                ["ServiceNotEssential"] = "Cobalt has deemed this service non-essential",
                ["ServiceActive"] = "<color=#8bc34a>{0}</color> service is now ACTIVE — {1}. Usage scales with population.",
                ["ServiceActivePaid"] = "<color=#8bc34a>{0}</color> service is now ACTIVE — {1} (paid for by {2}). Usage scales with population.",
                ["ServiceExpired"] = "<color=#e57373>{0}</color> service has EXPIRED.",
                ["ServiceLow"] = "<color=#ffb74d>{0}</color> is running low — about {1} left at current usage. Visit the office to extend it!",
                ["ResupplyCalled"] = "{0} called in a resupply at the Airfield — the air traffic controller is on break, next locked crate ready in {1} minutes.",
                ["ResupplyBreak"] = "The Airfield air traffic controller is on break — next locked crate ready in {0} minutes.",
                ["ResupplyReady"] = "Air traffic controller is back — the Airfield resupply terminal is ready.",
                ["PaidMax"] = "{0} is already paid up to the {1}-day maximum.",
                ["NeedScrap"] = "you need {0} scrap to pay for a day of {1}.",
                ["ToppedUp"] = "{0} topped up — {1} banked.",
                ["TrainsSealed"] = "train fuel systems are sealed while the Transportation service is active.",
                ["OfficeAt"] = "visit the office clerk at grid <color=#8bc34a>{0}</color> (orange dot on your map) to pay for services. Use <color=#8bc34a>/pwinfo</color> to check service status from anywhere.",
                ["OfficeMissing"] = "the office hasn't been placed yet.",
                ["NoPermission"] = "You don't have permission for that.",

                ["FaultAnnounce"] = "<color=#e57373>{0}</color> at <color=#ffb74d>{1}</color> (grid {2}, red dot on the map) — {3} {4}. Repair crew ETA ~{5}. Repair contract at the office: <color=#8bc34a>{6} scrap</color>.",
                ["FaultDown"] = "is DOWN",
                ["FaultHalf"] = "is running at half capacity",
                ["FaultCrewFixed"] = "the repair crew has fixed <color=#8bc34a>{0}</color> at {1}.",
                ["FaultPlayerFixed"] = "<color=#8bc34a>{0}</color> restored — emergency repairs by <color=#8bc34a>{1}</color> (+{2} scrap).",
                ["ContractAccepted"] = "contract accepted — {0} at <color=#ffb74d>{1}</color>, grid {2} (red dot on the map). Bring <color=#8bc34a>{3}</color> to the site and press USE. Reward: <color=#8bc34a>{4} scrap</color>. Open contract — first to fix it gets paid. The crew arrives in {5}.",
                ["SiteNeedContract"] = "this is the {0} fault site — take the repair contract from the office clerk first.",
                ["SiteMissingMaterials"] = "you still need {0} to finish this repair.",
                ["NoMaterials"] = "no materials — just get there",

                ["UiTitle"] = "PUBLIC WORKS",
                ["UiSubtitle"] = "Utility services — {0} scrap per day each",
                ["UiTimeLeft"] = "~{0} left",
                ["UiInactive"] = "INACTIVE",
                ["UiNotEssential"] = "NOT ESSENTIAL",
                ["UiFaultCrew"] = "FAULT — crew {0}",
                ["UiFaultHold"] = "BILLING HOLD — {0}",
                ["UiPayBill"] = "PAY BILL {0}",
                ["UiPay"] = "PAY",
                ["UiTopUp"] = "+1 DAY",
                ["UiTakeJob"] = "TAKE JOB",
                ["UiOnJob"] = "ON JOB",
                ["UiFaultAt"] = "at {0}",
                ["UiPerDay"] = "{0}/day",
                ["UiFooter"] = "Current usage rate: ×{0} ({1} online) · bars show banked time vs the {2}-day cap",
                ["UiScrap"] = "Your scrap: {0}",
                ["OfficeHintMonument"] = "Visit the office at {0} (grid {1}) to pay your bill or take repair jobs.",
                ["OfficeHintGrid"] = "Visit the office at grid {0} to pay your bill or take repair jobs.",
                ["OfficeHintNone"] = "The Public Works office hasn't been placed yet.",

                ["SetOffice"] = "Public Works office placed here. Use /pw setclerk to fine-tune the clerk's spot.",
                ["SetOfficeAnchored"] = "Public Works office placed here, anchored to {0} — it will re-place itself after map wipes. Use /pw setclerk to fine-tune the clerk's spot.",
                ["ClerkMoved"] = "Clerk moved here.",
                ["PumpNeedHose"] = "hold a <color=#8bc34a>hose tool</color> and press USE to drain the pump.",
                ["PumpEmpty"] = "the pump is dry — it refills while the Garages service is active.",
                ["PumpDrained"] = "drained <color=#8bc34a>{0} low grade fuel</color> from the pump.",
                ["PumpNotAtStation"] = "Stand inside a gas station (Oxum's) to place a pump spot.",
                ["NotAtSupermarket"] = "Stand inside the Abandoned Supermarket to place this.",
                ["GrocerPlaced"] = "Grocery machine spot saved — it opens at every supermarket while the Markets service is active.",
                ["MarketPlaced"] = "Drone marketplace spot saved — it opens at every supermarket while the Markets service is active.",
                ["GrocerNotHere"] = "Stand inside the Abandoned Supermarket or a gas station (Oxum's) to place the grocery machine.",
                ["GrocerPlacedStation"] = "Corner-store grocery spot saved — it opens at every gas station while the Markets service is active.",
                ["PumpRemoved"] = "Removed pump spot #{0} — {1} spot(s) remain.",
                ["PumpNoneNear"] = "No pump spot within {0}m — stand next to the pump you want gone.",
                ["DrinkNothing"] = "nothing drinkable in your inventory — the grocery sells Mr Spice cans and vodka. Try <color=#8bc34a>/drink info</color>.",
                ["DrinkMenu"] = "drink menu:",
                ["Drank"] = "gulp — the {0} goes down the hatch.",
                ["DrinkBadBatch"] = "that batch was... off. It comes straight back up — and snake venom burns through your veins!",
                ["DrinkHint"] = "that's drinkable — type <color=#8bc34a>/drink</color>, or put it on your belt, holster, and <color=#8bc34a>right-click</color> to swig.",
                ["DrinkGodMode"] = "(god mode is blocking the drunk/venom effects — turn it off to feel your drinks)",
                ["GrocerCleared"] = "Grocery machine removed and its spot cleared.",
                ["MarketCleared"] = "Drone marketplace removed and its spot cleared.",
                ["MarketsClosed"] = "the shop is closed — the Markets service is down. Pay at the Public Works office to reopen it.",
                ["GaragesClosed"] = "the corner store is closed — the Garages service is down. Pay at the Public Works office to reopen it.",
                ["DroneDown"] = "drone delivery is offline until the Supermarket fault is repaired — the grocery inside is still open.",
                ["PumpAdded"] = "Pump spot #{0} saved — {1} fuel pump(s) now placed across the island's gas stations.",
                ["PumpsCleared"] = "All garage fuel pump spots cleared.",
                ["BoomboxPlaced"] = "Boombox placed, tuned to {0}. Change the station URL in the config.",
                ["BoomboxLocked"] = "the office radio is department property — only Public Works staff may retune it.",
                ["BoomboxRetuned"] = "Office radio retuned to {0} (saved to config).",
                ["PastefileSet"] = "Office building file set to '{0}'. Run /pw setoffice to paste it.",
                ["PastefileCleared"] = "Office building disabled (clerk only).",
                ["OfficeRemoved"] = "Public Works office (clerk + building) removed.",
                ["CopyPasteMissing"] = "CopyPaste plugin is not loaded — placed clerk only.",
                ["PasteFailed"] = "Office paste failed: {0}",
                ["Pasting"] = "Pasting office building '{0}'...",
                ["UsageGrant"] = "Usage: /pw grant <service|all> [days]",
                ["Granted"] = "Granted {0} day(s) of {1}.",
                ["UsageRevoke"] = "Usage: /pw revoke <service|all>",
                ["Revoked"] = "Revoked {0}.",
                ["UsageFault"] = "Usage: /pw fault <service> [minor|major]",
                ["UnknownService"] = "Unknown service '{0}'.",
                ["FaultNotActive"] = "{0} isn't active — faults only hit active services.",
                ["FaultExists"] = "{0} already has an active fault.",
                ["FaultStarted"] = "Started a {0} fault on {1}.",
                ["FaultNoSite"] = "No fault site available for {0} on this map.",
                ["UsageClearfault"] = "Usage: /pw clearfault <service|all>",
                ["FaultsCleared"] = "Resolved {0} fault(s).",
                ["NoFaultsMatch"] = "No matching active faults.",
                ["Usage"] = "Usage: /pw | /pw setoffice | /pw setclerk | /pw setboombox | /pw setpump | /pw removepump | /pw clearpumps | /pw pumpinfo | /pw setgrocer | /pw cleargrocer | /pw setmarket | /pw clearmarket | /pw pastefile <name> | /pw removeoffice | /pw grant <service|all> [days] | /pw revoke <service|all> | /pw fault <service> [minor|major] | /pw clearfault <service|all> | /pw hostile [clear] | /pw perf",
            }, this);
        }

        private string Msg(string key, string userId, params object[] args)
        {
            string text = lang.GetMessage(key, this, userId);
            return args != null && args.Length > 0 ? string.Format(text, args) : text;
        }

        private string LabelFor(string serviceKey, string userId) => Msg("Label." + serviceKey, userId);
        private string DescFor(string serviceKey, string userId) => Msg("Desc." + serviceKey, userId);

        // Prefixed player-facing reply.
        private void Reply(BasePlayer player, string key, params object[] args) =>
            SendReply(player, Msg("Prefix", player.UserIDString) + Msg(key, player.UserIDString, args));

        // Unprefixed reply (admin command feedback).
        private void ReplyRaw(BasePlayer player, string key, params object[] args) =>
            SendReply(player, Msg(key, player.UserIDString, args));

        // Localized broadcast; format args are resolved with the server's default
        // language, the surrounding sentence with each recipient's.
        private void AnnounceAll(string key, params object[] args)
        {
            if (!config.Broadcast) return;
            foreach (var player in BasePlayer.activePlayerList)
                if (player != null)
                    SendReply(player, Msg("Prefix", player.UserIDString) + Msg(key, player.UserIDString, args));
        }

        #endregion

        #region Config

        private Configuration config;

        // Set when the config file fails to parse: the plugin runs on defaults but
        // refuses to write, so a JSON typo can't wipe the user's customizations —
        // fix the file and reload.
        private bool configBroken;

        private class Configuration
        {
            [JsonProperty("Price in scrap per service per day")]
            public int PricePerDay = 100;

            [JsonProperty("Hours of service per payment (a 'day')")]
            public float HoursPerPayment = 24f;

            [JsonProperty("Maximum prepaid days per service")]
            public int MaxPrepaidDays = 7;

            // Disabled services still show in the office CUI, flagged as "not essential"
            // by Cobalt — they can't be paid for, never fault, and any banked time freezes.
            [JsonProperty("Disabled services (keys: electricity, water, gas, markets, garages, airport, internet, transportation, protection)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> DisabledServices = new List<string>();

            // Defaults open the office inside Radtown's interior office on any map that
            // has one (coords are monument-local, so they fit every Radtown instance).
            // Maps without a Radtown log a hint and wait for /pw setoffice.
            [JsonProperty("Office NPC position (empty = not placed yet, use /pw setoffice; local coords when anchored to a monument)")]
            public string OfficePosition = "-19.38 0.26 17.41";

            [JsonProperty("Office NPC rotation Y degrees (relative to monument when anchored)")]
            public float OfficeRotationY = 260.65918f;

            [JsonProperty("Office anchor monument (auto-set when placing inside a monument; survives map wipes)")]
            public string OfficeMonument = "assets/bundled/prefabs/autospawn/monument/roadside/radtown_1.prefab";

            [JsonProperty("Clerk display name")]
            public string ClerkName = "Public Works Clerk";

            [JsonProperty("Clerk NPC prefab path")]
            public string ClerkPrefab = NpcPrefab;

            [JsonProperty("Clerk NPC fallback prefab path")]
            public string ClerkPrefabFallback = NpcPrefabFallback;

            [JsonProperty("Clerk outfit (item shortnames, optional @skinId, same format as RustQuests trader kits)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ClerkOutfit = new List<string>
            {
                "hoodie@3435148685", "mask.balaclava@3141449679", "pants", "shoes.boots"
            };

            [JsonProperty("Clerk turns to face players within this range (meters, 0 = disabled)")]
            public float ClerkFaceRange = 5f;

            // Default seed picks the department's stock clerk face (paired with the
            // hi-vis uniform above); set 0 for a random face each spawn.
            [JsonProperty("Clerk face seed (steam-id-style number picks a fixed face; 0 = random each spawn)")]
            public ulong ClerkFaceSeed = 76561198714518433UL;

            // Accepts a station name from the game's boombox lists (case-insensitive,
            // Island Taxi convention) or a raw stream URL. The default names a station
            // from Rust's built-in list, so clients everywhere are allowed to play it.
            [JsonProperty("Office boombox radio station (station-list name or stream URL; empty = no boombox)")]
            public string BoomboxStation = "Smooth Jazz Florida";

            [JsonProperty("Office boombox position (set by /pw setboombox; local coords when anchored)")]
            public string BoomboxPosition = "-18.05 -0.30 18.61";

            [JsonProperty("Office boombox rotation Y degrees")]
            public float BoomboxRotationY = 113.658936f;

            [JsonProperty("Office boombox anchor monument (auto-set)")]
            public string BoomboxMonument = "assets/bundled/prefabs/autospawn/monument/roadside/radtown_1.prefab";

            [JsonProperty("Office boombox height offset above the placement spot (meters)")]
            public float BoomboxHeightOffset = 0.1f;

            [JsonProperty("Interaction range (meters)")]
            public float InteractRange = 3f;

            [JsonProperty("CopyPaste building file for the office (empty = clerk only)")]
            public string PasteFile = "";

            [JsonProperty("Office building position (set by /pw setoffice)")]
            public string PastePosition = "";

            [JsonProperty("Office building rotation Y degrees")]
            public float PasteRotationY = 0f;

            [JsonProperty("Show office map marker")]
            public bool ShowMapMarker = true;

            [JsonProperty("Broadcast service activations and expiries in chat")]
            public bool Broadcast = true;

            [JsonProperty("Service re-apply tick seconds")]
            public float TickSeconds = 30f;

            [JsonProperty("Log a warning when a service tick takes longer than this many ms (0 = off; /pw perf shows live stats)")]
            public float TickWarnMs = 25f;

            [JsonProperty("Gas service oil flow multiplier (1 = one oil rig switch worth of crude)")]
            public float GasFlowMultiplier = 1f;

            [JsonProperty("Gas service: max crude a Dome pump holds before it stops producing (0 = vanilla/unlimited)")]
            public int GasPumpMaxCrude = 500;

            [JsonProperty("Garages service runs working fuel pumps at gas stations (spawned crude pumps that produce low grade)")]
            public bool GaragePumpsEnabled = true;

            [JsonProperty("Garage pump low grade flow multiplier (1 = one oil rig switch worth)")]
            public float GarageFuelFlowMultiplier = 0.5f;

            [JsonProperty("Max low grade a garage pump holds before it stops producing")]
            public int GaragePumpMaxFuel = 250;

            // Spots are gas-station-local "x y z rotY"; one captured spot re-places at
            // every gas station on the map (all instances share the monument prefab).
            // Defaults are a row of three pumps behind the station building, captured in-game at Oxum's.
            [JsonProperty("Garage pump spots (local coords per gas station; add with /pw setpump)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> GaragePumpSpots = new List<string>
            {
                "0.36 3.25 32.01 88.9", "1.87 3.25 32.01 91.0", "3.40 3.25 32.01 91.7"
            };

            [JsonProperty("Markets service opens a grocery vending machine at supermarkets")]
            public bool MarketsGrocerEnabled = true;

            [JsonProperty("Grocer shop name")]
            public string GrocerName = "Public Works Grocery";

            // NPC vending machines cap at 7 sell orders (MaxVendingEntries).
            [JsonProperty("Supermarket grocer stock ('sellShortname sellAmount scrapPrice')", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> GrocerOrders = new List<string>
            {
                "bottle.vodka 1 25", "mrspice.can 1 15", "egg 2 5", "bread.loaf 1 8",
                "apple 2 5", "honey 1 20", "syringe.medical 1 30"
            };

            [JsonProperty("Gas station grocer stock ('sellShortname sellAmount scrapPrice')", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> GarageGrocerOrders = new List<string>
            {
                "bottle.vodka 1 25", "mrspice.can 1 15", "chocolate 1 5", "granolabar 1 10",
                "can.tuna 1 8", "can.beans 1 10", "syringe.medical 1 30"
            };

            // Default is inside the store, captured in-game beside the decorative
            // vending machine.
            [JsonProperty("Grocer spot (supermarket-local 'x y z rotY'; set with /pw setgrocer)")]
            public string GrocerSpot = "-6.50 0.08 3.25 89.8";

            // Default is the corner-store nook captured in-game at Oxum's.
            [JsonProperty("Gas station grocer spot (station-local 'x y z rotY'; set with /pw setgrocer at a gas station)")]
            public string GarageGrocerSpot = "-7.81 3.42 25.35 180.3";

            // The junk collectibles are inert in vanilla; /drink makes them consumable.
            // health = instant, healOverTime = pending health (bandage-style).
            // Optional 7th field: seconds of drunk screen-blur applied per drink.
            [JsonProperty("Drinkable junk items ('shortname health healOverTime calories hydration poisonChance [drunkSeconds]'; players use /drink)", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Drinkables = new List<string>
            {
                "mrspice.can 10 10 60 80 0", "bottle.vodka 25 25 125 5 0.15 20"
            };

            [JsonProperty("Poison applied by a bad drink (metabolism poison units)")]
            public float DrinkPoisonAmount = 15f;

            // Mirrors the pickle jar's bad roll (-50 calories / -50 hydration): you
            // throw the drink back up, so the benefits never land and the stomach empties.
            [JsonProperty("Calories and hydration lost when a bad drink comes back up (pickle-jar style; 0 = none)")]
            public float DrinkBadBatchStomachLoss = 50f;

            [JsonProperty("Drunk screen-blur intensity (0-1)")]
            public float DrunkIntensity = 0.5f;

            [JsonProperty("Markets service also opens a drone delivery terminal at supermarkets")]
            public bool MarketsDroneEnabled = true;

            // Default is on the supermarket roof — the stall building is out of the
            // way up there and the drone launch pad gets a clear flight path.
            [JsonProperty("Drone marketplace spot (supermarket-local 'x y z rotY'; set with /pw setmarket)")]
            public string MarketplaceSpot = "5.28 5.52 -1.29 0.6";

            [JsonProperty("Internet service also makes the satellite item-free (no tech trash / aiming modules / thruster fuel)")]
            public bool InternetFreeSatellite = true;

            [JsonProperty("Powerline pole output multiplier while Electricity service is active (1 = vanilla 12/18/24/30)")]
            public float PoleOutputMultiplier = 2f;

            [JsonProperty("Electricity service also powers green recyclers at other monuments (60% recycle efficiency instead of 50%)")]
            public bool ElectricityPowersRecyclers = true;

            [JsonProperty("Airport service: minutes the air traffic controller is on break after a resupply (chinook) call (0 = vanilla 3h window)")]
            public float AirportResupplyCooldownMinutes = 10f;

            // Protection service: the patrol heli and Bradley only engage players who
            // provoke them. "Provoke" = damage the heli/Bradley, or damage another
            // player within ProtectionRadius of one. Hostility is shared with the
            // aggressor's team and expires ProtectionHostileSeconds after the last act.
            [JsonProperty("Protection: hostility radius around a live patrol heli / Bradley for player-vs-player attacks (meters)")]
            public float ProtectionRadius = 150f;

            [JsonProperty("Protection: seconds a player stays hostile after their last aggressive act")]
            public float ProtectionHostileSeconds = 120f;

            [JsonProperty("Protection: hostility is shared with the aggressor's whole team")]
            public bool ProtectionShareWithTeam = true;

            [JsonProperty("Protection: attacking your own teammate counts as aggression")]
            public bool ProtectionFriendlyFireCounts = false;

            [JsonProperty("Protection: dying clears your hostile flag (otherwise the heli hunts you straight off the respawn)")]
            public bool ProtectionClearOnDeath = true;

            // Plugin bots built on player.prefab (FakeFriends etc.) are BasePlayers with
            // non-Steam ids; cover them like humans so the heli doesn't farm them.
            [JsonProperty("Protection: also covers non-NPC bot players (player.prefab bots such as FakeFriends)")]
            public bool ProtectionCoversBots = true;

            [JsonProperty("Protection: scrap fee to clear a minor billing hold at the office")]
            public int ProtectionBillingFeeMinor = 50;

            [JsonProperty("Protection: scrap fee to clear a major billing hold at the office")]
            public int ProtectionBillingFeeMajor = 100;

            [JsonProperty("Broadcast expiry warnings this many minutes before a service lapses", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<float> WarnMinutes = new List<float> { 60f, 10f };

            [JsonProperty("Players online before the burn rate increases")]
            public int FreePop = 2;

            [JsonProperty("Extra burn rate per player above the free threshold (0.25 = +25% each)")]
            public float PerExtraPlayer = 0.25f;

            [JsonProperty("Maximum burn rate multiplier")]
            public float MaxBurn = 3f;

            [JsonProperty("Steam IDs at or above this value are bots and don't count toward burn rate")]
            public ulong BotIdThreshold = 76561199900000000UL;

            [JsonProperty("Average hours between random faults (0 disables events)")]
            public float FaultAverageHours = 24f;

            [JsonProperty("Maximum concurrent faults")]
            public int FaultMaxConcurrent = 1;

            [JsonProperty("Minimum minutes between faults")]
            public float FaultMinGapMinutes = 90f;

            [JsonProperty("Chance a fault is major (0-1); minor = 50% output, major = full outage")]
            public float FaultMajorChance = 0.25f;

            [JsonProperty("Auto-repair minutes (minor fault)")]
            public float AutoRepairMinutesMinor = 45f;

            [JsonProperty("Auto-repair minutes (major fault)")]
            public float AutoRepairMinutesMajor = 90f;

            [JsonProperty("Repair reward scrap (minor fault)")]
            public int RepairRewardMinor = 75;

            [JsonProperty("Repair reward scrap (major fault)")]
            public int RepairRewardMajor = 150;

            [JsonProperty("Repair reward also banks this many hours on the fixed service")]
            public float RepairRewardBankedHours = 6f;

            [JsonProperty("Major faults multiply repair material amounts by")]
            public float FaultMajorMaterialMultiplier = 2f;

            [JsonProperty("Repair interaction range at the fault site (meters)")]
            public float FaultFixRange = 5f;

            [JsonProperty("Repair materials per service ('shortname amount')", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, List<string>> RepairMaterials = new Dictionary<string, List<string>>
            {
                { "electricity", new List<string> { "metal.fragments 50", "gears 2" } },
                { "water", new List<string> { "metal.fragments 40", "metalpipe 2" } },
                { "gas", new List<string> { "metal.fragments 40", "propanetank 2" } },
                { "markets", new List<string> { "metal.fragments 30", "fuse 2" } },
                { "garages", new List<string> { "metal.fragments 30", "gears 3" } },
                { "airport", new List<string> { "metal.fragments 50", "fuse 2" } },
                { "internet", new List<string> { "metal.fragments 25", "techparts 2" } },
                { "transportation", new List<string> { "metal.fragments 60", "gears 3" } },
            };
        }

        protected override void LoadDefaultConfig() => config = new Configuration();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if (config == null) throw new Exception();
                configBroken = false;

                // v2.8.0 renamed the boombox key when it learned station names —
                // carry an existing station over from the old key once.
                try
                {
                    var legacyStation = Config["Office boombox radio station URL (empty = no boombox)"] as string;
                    if (legacyStation != null) config.BoomboxStation = legacyStation;
                }
                catch { }
            }
            catch
            {
                PrintError($"Config file is invalid JSON — running on defaults until it's fixed. " +
                           $"The file was left untouched: repair oxide/config/{Name}.json (check quotes and commas) and run oxide.reload {Name}.");
                LoadDefaultConfig();
                configBroken = true;
            }
            SaveConfig();

            disabledServices.Clear();
            if (config.DisabledServices != null)
                foreach (var raw in config.DisabledServices)
                {
                    var key = raw?.Trim().ToLower();
                    if (IsService(key)) disabledServices.Add(key);
                    else PrintWarning($"Config lists unknown disabled service '{raw}' — valid keys: {string.Join(", ", ServiceKeys)}");
                }
        }

        protected override void SaveConfig()
        {
            if (configBroken)
            {
                PrintWarning($"Not saving config — oxide/config/{Name}.json is invalid JSON and writing would overwrite it. Fix the file and reload the plugin.");
                return;
            }
            Config.WriteObject(config);
        }

        #endregion

        #region Data (service buckets)

        // Each service is a bucket of banked seconds. Payments deposit; the tick drains
        // at a rate that scales with how many humans are online (see BurnRate).
        private class StoredData
        {
            public Dictionary<string, double> Expiry = new Dictionary<string, double>();  // legacy (pre-1.4), migrated on load
            public Dictionary<string, double> Banked = new Dictionary<string, double>();  // seconds banked at 1x burn

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> PastedEntityIds = new List<ulong>();

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<FaultData> Faults = new List<FaultData>();

            // Spawned pumps don't save (enableSaving=false), so their stored fuel is
            // banked here across plugin reloads, keyed by world position.
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public Dictionary<string, int> PumpFuel = new Dictionary<string, int>();

            public double LastFaultEpoch;
            public double AirportBreakUntil;   // epoch seconds the Airfield resupply break ends (0 = none)
        }

        private StoredData data;

        private void LoadData()
        {
            data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
            if (data.Banked == null) data.Banked = new Dictionary<string, double>();
            if (data.PumpFuel == null) data.PumpFuel = new Dictionary<string, int>();

            // Migrate pre-bucket expiry timestamps into banked seconds.
            if (data.Banked.Count == 0 && data.Expiry != null && data.Expiry.Count > 0)
            {
                foreach (var pair in data.Expiry)
                    if (pair.Value > Now)
                        data.Banked[pair.Key] = pair.Value - Now;
                data.Expiry.Clear();
                SaveData();
                Puts($"Migrated {data.Banked.Count} service timers to the bucket model.");
            }
        }

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, data);

        private static double Now => DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;

        private double GetBanked(string service)
        {
            double banked;
            return data.Banked.TryGetValue(service, out banked) ? banked : 0;
        }

        // Normalized copy of config.DisabledServices, rebuilt on every config load.
        private readonly HashSet<string> disabledServices = new HashSet<string>();

        private bool IsDisabled(string service) => disabledServices.Contains(service);

        private bool IsActive(string service) => !IsDisabled(service) && GetBanked(service) > 0;

        #endregion

        #region State

        private const string PrefabRecycler = "recycler_static";
        private const string PrefabPowerline = "powergrid_powerline_io.static";

        // service key -> powergrid entities that service force-powers
        private readonly Dictionary<string, List<IPowergridEntity>> serviceEntities =
            new Dictionary<string, List<IPowergridEntity>>();

        private readonly List<WaterTreatmentWaterTank> waterTanks = new List<WaterTreatmentWaterTank>();
        private readonly List<ChargeUpIOEntity> airfieldTerminals = new List<ChargeUpIOEntity>();
        private readonly List<TrainEngine> trains = new List<TrainEngine>();

        // services that were active last tick, to detect expiry transitions
        private readonly HashSet<string> lastActive = new HashSet<string>();

        // expiry-warning bookkeeping: how many thresholds have fired this charge cycle
        // (reset whenever a payment or grant tops the bucket up)
        private readonly Dictionary<string, int> warnLevel = new Dictionary<string, int>();

        private float lastDrainTime = -1f;

        // Tick cost diagnostics (/pw perf): the 30s tick is the plugin's only periodic
        // bulk work, so if the server hitches on a steady cadence this is where to look.
        private readonly System.Diagnostics.Stopwatch tickWatch = new System.Diagnostics.Stopwatch();
        private readonly List<KeyValuePair<string, double>> tickPhases = new List<KeyValuePair<string, double>>();
        private double tickLastMs, tickMaxMs, tickTotalMs;
        private int tickCount;

        // plugin-spawned fuel pumps at gas stations (driven directly, not via serviceEntities)
        private readonly List<WaterCatcher> garagePumps = new List<WaterCatcher>();
        private float garagePumpBaseInterval = 60f;

        // vanilla Dome crude pumps, plus their prefab state for restore on unload
        private readonly List<WaterCatcher> domePumps = new List<WaterCatcher>();
        private int origCrudePumpMaxStack = -1;
        private float domePumpPrefabInterval = -1f;

        // Markets extras: always standing, but closed to customers while the Markets
        // service is down or fully faulted (see GrocerOpen / CanUseVending). Station
        // grocers are tracked separately — they belong to the Garages service.
        private readonly List<NPCVendingMachine> grocers = new List<NPCVendingMachine>();
        private readonly List<NPCVendingMachine> stationGrocers = new List<NPCVendingMachine>();
        private NPCVendingOrder garageGrocerOrders;
        private readonly List<Marketplace> marketplaces = new List<Marketplace>();
        private NPCVendingOrder grocerOrders;

        // Protection service: who Cobalt's air defence currently considers hostile
        // (steam id -> epoch seconds the flag lapses). Memory only — cleared on
        // expiry/unload, so nothing carries over into vanilla. hostileNoticeAt
        // throttles the "you've been flagged" chat line per player.
        private readonly Dictionary<ulong, double> hostileUntil = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, double> hostileNoticeAt = new Dictionary<ulong, double>();
        private readonly List<PatrolHelicopter> patrolHelis = new List<PatrolHelicopter>();
        private readonly List<BradleyAPC> bradleys = new List<BradleyAPC>();

        private BasePlayer clerk;
        private MapMarkerGenericRadius marker;
        private DeployableBoomBox boombox;
        private Timer faceTimer;
        private float clerkHomeYaw;
        private float clerkLastYaw;
        private bool origSatelliteRequirePowerplant;
        private bool origSatelliteFreePower;
        private bool origSatelliteFreeFuel;
        private Timer tickTimer;

        private static PowergridManager Manager => PointEntity<PowergridManager>.ServerInstance;

        private int MaxStage
        {
            get
            {
                try { return PowergridStageConfig.instance != null ? PowergridStageConfig.instance.GetNumberOfStages() : 4; }
                catch { return 4; }
            }
        }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            LoadData();
            Puts($"PublicWorks v{Version} loaded - by LowPopLabs - ko-fi.com/lowpoplabs");
        }

        private void OnServerInitialized()
        {
            origSatelliteRequirePowerplant = ConVar.Satellite.require_powerplant;
            origSatelliteFreePower = ConVar.Satellite.free_power;
            origSatelliteFreeFuel = ConVar.Satellite.free_fuel;

            foreach (var networkable in BaseNetworkable.serverEntities)
                IndexEntity(networkable as BaseEntity);

            Puts($"Indexed powergrid entities per service: " +
                 string.Join(", ", serviceEntities.Select(s => $"{s.Key}={s.Value.Count}")) +
                 $"; {waterTanks.Count} water tanks, {airfieldTerminals.Count} airfield terminals, {trains.Count} trains.");

            SpawnOffice();
            SpawnBoombox();
            SpawnGaragePumps();

            // Faults on services Cobalt has shelved are moot — drop them before markers spawn.
            for (int i = data.Faults.Count - 1; i >= 0; i--)
                if (IsDisabled(data.Faults[i].Service)) CancelFault(data.Faults[i]);

            foreach (var fault in data.Faults)
                SpawnFaultMarker(fault);

            foreach (var key in ServiceKeys)
                if (IsActive(key)) lastActive.Add(key);

            // Ghost fuses ride along in the save; if Airport lapsed while the server was
            // down, pull them before the first tick. A resupply break survives reloads.
            ResolveAirfieldGraph();
            if (!IsActive("airport")) SeatGhostFuses(false);
            if (data.AirportBreakUntil > Now) ScheduleResupplyBreakEnd();

            tickTimer = timer.Every(Mathf.Max(10f, config.TickSeconds), Tick);
            Tick();
        }

        private void Unload()
        {
            RestorePoleOutput();
            tickTimer?.Destroy();
            resupplyBreakTimer?.Destroy();
            SaveData();
            ReleaseAirport();

            RunConVar("satellite.require_powerplant", origSatelliteRequirePowerplant ? "true" : "false");
            RunConVar("satellite.free_power", origSatelliteFreePower ? "true" : "false");
            RunConVar("satellite.free_fuel", origSatelliteFreeFuel ? "true" : "false");
            ApplyPoleMultiplier(false);
            int realStage = Manager != null ? Manager.CurrentStage : 0;
            foreach (var pair in serviceEntities)
                ApplyStage(pair.Value, realStage);

            foreach (var pump in domePumps)
            {
                if (pump == null || pump.IsDestroyed) continue;
                if (origCrudePumpMaxStack > 0 && pump.inventory != null)
                    pump.inventory.maxStackSize = origCrudePumpMaxStack;
                if (domePumpPrefabInterval >= 0f)
                    pump.overrideCollectInterval = domePumpPrefabInterval;
            }

            KillOffice();
            KillBoombox();
            KillGaragePumps();
            KillMarketsExtras();
            KillAllFaultMarkers();
            ClearHostility();

            foreach (var player in BasePlayer.activePlayerList)
                CuiHelper.DestroyUi(player, UiPanelName);
        }

        // Radius markers network their colors via SendUpdate, and clients that connect
        // after the spawn can miss it — re-push all live markers to each new joiner.
        private void OnPlayerConnected(BasePlayer player)
        {
            timer.Once(5f, () =>
            {
                if (marker != null && !marker.IsDestroyed) marker.SendUpdate();
                foreach (var faultMarker in faultMarkers.Values)
                    if (faultMarker != null && !faultMarker.IsDestroyed) faultMarker.SendUpdate();
            });
        }

        private void OnEntitySpawned(BaseNetworkable entity)
        {
            var baseEntity = entity as BaseEntity;
            if (baseEntity == null) return;
            // Only defer-index entity types the services actually track — this hook
            // fires for every spawn, so filter before allocating a timer.
            if (!(baseEntity is IPowergridEntity || baseEntity is TrainEngine ||
                  baseEntity is WaterTreatmentWaterTank ||
                  baseEntity is AirfieldAirdropTerminal || baseEntity is AirfieldCallChinookTerminal ||
                  baseEntity is PatrolHelicopter || baseEntity is BradleyAPC))
                return;
            timer.Once(1f, () => IndexEntity(baseEntity));
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (clerk != null && entity == clerk) return true;     // clerk is invincible
            if (boombox != null && entity == boombox) return true; // playing decays it otherwise
            var pump = entity as WaterCatcher;
            if (pump != null && garagePumps.Contains(pump)) return true;
            var machine = entity as NPCVendingMachine;
            if (machine != null && (grocers.Contains(machine) || stationGrocers.Contains(machine))) return true;
            // Marketplace itself isn't a combat entity; only its terminals can be hit.
            if (entity is MarketTerminal &&
                marketplaces.Contains(entity.GetParentEntity() as Marketplace)) return true;

            TrackHostility(entity, info);
            return null;
        }

        // Raw-spawned deployables ground-snap onto whatever collider is nearest —
        // sometimes a loot crate through a wall — and the ground check kills them
        // when that crate despawns. Department property doesn't fall over.
        private object OnEntityGroundMissing(BaseEntity entity)
        {
            if (entity == null) return null;
            if (boombox != null && entity == boombox) return false;
            var groundedMachine = entity as NPCVendingMachine;
            if (groundedMachine != null &&
                (grocers.Contains(groundedMachine) || stationGrocers.Contains(groundedMachine))) return false;
            return null;
        }

        private void OnPlayerInput(BasePlayer player, InputState input)
        {
            if (player == null) return;

            // Belt swig: drinkables can't be held (isHoldable=false client-side), but
            // an empty-handed right click is unused in vanilla — with a drinkable on
            // the belt, that's the drink gesture.
            if (input.WasJustPressed(BUTTON.FIRE_SECONDARY) && player.GetActiveItem() == null && TryBeltDrink(player))
                return;

            if (!input.WasJustPressed(BUTTON.USE)) return;

            if (clerk != null &&
                Vector3.Distance(player.transform.position, clerk.transform.position) <= config.InteractRange)
            {
                Vector3 toClerk = (clerk.transform.position + Vector3.up * 1.4f) - player.eyes.position;
                if (Vector3.Dot(player.eyes.HeadForward(), toClerk.normalized) >= 0.6f)
                {
                    ShowPanel(player);
                    return;
                }
            }

            if (TryDrainPump(player)) return;
            if (TryMarketClosedNotice(player)) return;

            TryRepairNearby(player);
        }

        #endregion

        #region Entity indexing

        private void IndexEntity(BaseEntity entity)
        {
            if (entity == null || entity.IsDestroyed) return;

            // Plugin-spawned garage pumps are driven directly — keep them out of the
            // service lists (a gas station within 200m of Dome would misfile them as "gas").
            var spawnedPump = entity as WaterCatcher;
            if (spawnedPump != null && garagePumps.Contains(spawnedPump)) return;

            var tank = entity as WaterTreatmentWaterTank;
            if (tank != null && !waterTanks.Contains(tank)) waterTanks.Add(tank);

            var train = entity as TrainEngine;
            if (train != null && !trains.Contains(train)) trains.Add(train);

            // Cobalt assets for the Protection service (pruned lazily when iterated).
            var heli = entity as PatrolHelicopter;
            if (heli != null && !patrolHelis.Contains(heli)) patrolHelis.Add(heli);
            var apc = entity as BradleyAPC;
            if (apc != null && !bradleys.Contains(apc)) bradleys.Add(apc);

            if ((entity is AirfieldAirdropTerminal || entity is AirfieldCallChinookTerminal))
            {
                var terminal = entity as ChargeUpIOEntity;
                if (terminal != null && !airfieldTerminals.Contains(terminal)) airfieldTerminals.Add(terminal);
            }

            var ipe = entity as IPowergridEntity;
            if (ipe == null) return;

            bool shouldConnect;
            try { shouldConnect = ipe.Server_ShouldConnectToPowergrid(); }
            catch { return; }
            if (!shouldConnect) return;

            string service = ClassifyService(entity);
            if (service == null) return;

            List<IPowergridEntity> list;
            if (!serviceEntities.TryGetValue(service, out list))
                serviceEntities[service] = list = new List<IPowergridEntity>();
            if (!list.Contains(ipe)) list.Add(ipe);

            if (service == "gas")
            {
                var domePump = entity as WaterCatcher;
                if (domePump != null && !domePumps.Contains(domePump))
                {
                    domePumps.Add(domePump);
                    if (domePumpPrefabInterval < 0f) domePumpPrefabInterval = domePump.overrideCollectInterval;
                    ApplyDomePumpCap(domePump);
                }
            }
        }

        // Vanilla Dome pumps accumulate crude until the container's stack cap (2000+
        // over a service day) — clamp the cap so a pump saturates and players have to
        // come collect to keep the crude flowing.
        private void ApplyDomePumpCap(WaterCatcher pump)
        {
            if (pump == null || config.GasPumpMaxCrude <= 0 || pump.inventory == null) return;
            if (origCrudePumpMaxStack < 0) origCrudePumpMaxStack = pump.inventory.maxStackSize;
            pump.inventory.maxStackSize = config.GasPumpMaxCrude;
        }

        private string ClassifyService(BaseEntity entity)
        {
            // Dome crude pumps: one of the three sits outside the Dome's monument bounds,
            // so classify all of them by proximity to a Dome monument instead.
            if (entity.ShortPrefabName == "crudeoilproducer" && IsNearMonument("dome", entity.transform.position, 200f))
                return "gas";

            string monument = GetMonumentName(entity.transform.position);
            if (monument != null)
            {
                string m = monument.ToLower();
                if (m.Contains("power plant")) return "electricity";
                if (m.Contains("water treatment")) return "water";
                if (m.Contains("dome")) return "gas";
                if (m.Contains("supermarket")) return "markets";
                if (m.Contains("oxum") || m.Contains("gas station")) return "garages";
                if (m.Contains("airfield")) return "airport";
                if (m.Contains("launch site")) return "internet";
            }

            // Outside a service monument: street powerlines belong to the electricity
            // service, and (config-gated) so do green recyclers — unpowered they run at
            // 50% recycle efficiency, powered they return to 60% plus a speed buff at
            // max stage. Everything else (oil rigs) stays vanilla.
            if (entity.ShortPrefabName == PrefabPowerline) return "electricity";
            if (config.ElectricityPowersRecyclers && entity.ShortPrefabName == PrefabRecycler) return "electricity";
            return null;
        }

        private bool IsNearMonument(string nameFragment, Vector3 position, float range)
        {
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return false;
            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null) continue;
                string name = monument.displayPhrase != null ? monument.displayPhrase.english : monument.name;
                if (string.IsNullOrEmpty(name) || !name.ToLower().Contains(nameFragment)) continue;
                if (Vector3.Distance(monument.transform.position, position) <= range) return true;
            }
            return false;
        }

        private string GetMonumentName(Vector3 position)
        {
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return null;
            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null || !monument.IsInBounds(position)) continue;
                string name = monument.displayPhrase != null ? monument.displayPhrase.english : null;
                if (string.IsNullOrEmpty(name))
                {
                    name = monument.name;
                    int slash = name.LastIndexOf('/');
                    if (slash >= 0) name = name.Substring(slash + 1).Replace(".prefab", "");
                }
                return name.Trim();
            }
            return null;
        }

        #endregion

        #region Service engine

        private void RunConVar(string convar, string value) =>
            ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), convar, value);

        private int HumanCount()
        {
            int humans = 0;
            foreach (var player in BasePlayer.activePlayerList)
                if (player != null && !player.IsNpc && player.userID < config.BotIdThreshold)
                    humans++;
            return humans;
        }

        // How fast the buckets drain: 1x with FreePop or fewer humans online, then
        // +PerExtraPlayer per extra human, clamped at MaxBurn. Bots never count.
        private float BurnRate()
        {
            float burn = 1f + config.PerExtraPlayer * Mathf.Max(0, HumanCount() - config.FreePop);
            return Mathf.Clamp(burn, 1f, Mathf.Max(1f, config.MaxBurn));
        }

        private double tickMark;

        private void Lap(string phase)
        {
            double now = tickWatch.Elapsed.TotalMilliseconds;
            tickPhases.Add(new KeyValuePair<string, double>(phase, now - tickMark));
            tickMark = now;
        }

        private void Tick()
        {
            tickWatch.Restart();
            tickMark = 0;
            tickPhases.Clear();

            // Drain the buckets by real elapsed time x burn rate.
            float now = UnityEngine.Time.realtimeSinceStartup;
            float elapsed = lastDrainTime < 0f ? 0f : Mathf.Clamp(now - lastDrainTime, 0f, 600f);
            lastDrainTime = now;
            float burn = BurnRate();

            bool dirty = false;
            foreach (var key in ServiceKeys)
            {
                if (IsDisabled(key)) continue; // banked time freezes while Cobalt shelves a service
                double banked = GetBanked(key);
                if (banked > 0 && elapsed > 0f)
                {
                    data.Banked[key] = Math.Max(0, banked - elapsed * burn);
                    dirty = true;
                }
            }
            if (dirty) SaveData();
            Lap("drain");

            // Players can toggle the boombox off (or the IO system can); keep the office
            // music on — and if something managed to destroy the box, replace it.
            if (!string.IsNullOrEmpty(config.BoomboxStation) && (boombox == null || boombox.IsDestroyed))
            {
                SpawnBoombox();
            }
            else if (boombox != null && !boombox.IsDestroyed && !boombox.HasFlag(BaseEntity.Flags.On))
            {
                try
                {
                    boombox.BoxController.CurrentRadioIp = ResolveStationUrl(config.BoomboxStation) ?? config.BoomboxStation;
                    boombox.BoxController.ServerTogglePlay(true);
                }
                catch { }
            }

            Lap("boombox");
            FaultTick(elapsed);
            Lap("faults");

            foreach (var key in ServiceKeys)
            {
                bool active = IsActive(key);
                bool wasActive = lastActive.Contains(key);

                if (active) { ApplyService(key); Lap(key); }
                if (active) CheckExpiryWarning(key, burn);
                if (active && !wasActive)
                {
                    lastActive.Add(key);
                    AnnounceAll("ServiceActive", LabelFor(key, null), DescFor(key, null));
                }
                else if (!active && wasActive)
                {
                    lastActive.Remove(key);
                    // A fault on a lapsed service is moot — crews stand down, no reward.
                    var lapsedFault = GetFault(key);
                    if (lapsedFault != null) CancelFault(lapsedFault);
                    DeactivateService(key);
                    AnnounceAll("ServiceExpired", LabelFor(key, null));
                }
            }

            // Spawned pumps also hear real oil-rig switch broadcasts; while Garages is
            // inactive, keep re-zeroing so a rig switch doesn't run them for free.
            if (!IsActive("garages")) SetPumpFlow(garagePumps, 0f, garagePumpBaseInterval, garagePumpBaseInterval);

            // Grocer + marketplace stay standing regardless of service state (like the
            // pumps); CanUseVending turns grocery customers away while it's down, and
            // the drone terminals only exist while the drone service is up.
            EnsureMarketsExtras();
            SetMarketplaceTerminals(DroneOpen());
            Lap("markets");

            tickWatch.Stop();
            tickLastMs = tickWatch.Elapsed.TotalMilliseconds;
            tickTotalMs += tickLastMs;
            tickCount++;
            if (tickLastMs > tickMaxMs) tickMaxMs = tickLastMs;
            if (config.TickWarnMs > 0f && tickLastMs > config.TickWarnMs)
                PrintWarning($"Service tick took {tickLastMs:F1}ms — {TickBreakdown()}");
        }

        private string TickBreakdown() =>
            string.Join(", ", tickPhases.Where(p => p.Value >= 0.05).OrderByDescending(p => p.Value)
                .Select(p => $"{p.Key} {p.Value:F1}ms"));

        private void CheckExpiryWarning(string key, float burn)
        {
            if (config.WarnMinutes == null || config.WarnMinutes.Count == 0) return;
            double eta = GetBanked(key) / Math.Max(0.01f, burn);

            if (!warnLevel.ContainsKey(key)) warnLevel[key] = 0;
            int due = 0;
            foreach (var minutes in config.WarnMinutes)
                if (eta <= minutes * 60.0) due++;
            if (due > warnLevel[key])
            {
                warnLevel[key] = due;
                AnnounceAll("ServiceLow", LabelFor(key, null), FormatSpan(eta));
            }
        }

        // Rust 2633.288 (2026-09-03): poles no longer read a per-stage power value. Each pole
        // computes its output live from the power plant's fuse count, lerping between two
        // server convars: powergrid.powerlinebasepoweroutput (one fuse) and
        // powergrid.powerlinemaxpoweroutput (all fuses). Scaling both raises every pole's
        // output; the poles pick it up on their next stage re-broadcast. Both convars are
        // Saved, so the originals are restored on unload to keep them out of serverauto.cfg.
        private int? origPoleBasePower;
        private int? origPoleMaxPower;

        private void ApplyPoleMultiplier(bool boosted)
        {
            try
            {
                if (origPoleBasePower == null || origPoleMaxPower == null)
                {
                    origPoleBasePower = Powergrid.powerlineBasePowerOutput;
                    origPoleMaxPower = Powergrid.powerlineMaxPowerOutput;
                }
                float mult = boosted ? Mathf.Max(1f, config.PoleOutputMultiplier) : 1f;
                Powergrid.powerlineBasePowerOutput = Mathf.Max(1, Mathf.RoundToInt(origPoleBasePower.Value * mult));
                Powergrid.powerlineMaxPowerOutput = Mathf.Max(1, Mathf.RoundToInt(origPoleMaxPower.Value * mult));
            }
            catch (Exception e) { PrintWarning($"Pole output multiplier failed: {e.Message}"); }
        }

        private void RestorePoleOutput()
        {
            if (origPoleBasePower != null) Powergrid.powerlineBasePowerOutput = origPoleBasePower.Value;
            if (origPoleMaxPower != null) Powergrid.powerlineMaxPowerOutput = origPoleMaxPower.Value;
        }

        private void ApplyService(string service)
        {
            // An active fault degrades the service: minor = 50% output, major = full outage.
            float level = FaultLevel(service);

            if (service == "electricity")
                ApplyPoleMultiplier(level >= 1f && config.PoleOutputMultiplier > 1f);

            List<IPowergridEntity> list;
            if (serviceEntities.TryGetValue(service, out list))
                ApplyStage(list, Mathf.CeilToInt(MaxStage * level));

            switch (service)
            {
                case "gas":
                    // Dome pumps also require an oil rig switch signal (requireOilSwitchActive);
                    // feed them a synthetic multiplier so they produce without the rig switch.
                    // Major fault: push 0 (the next real rig switch transition re-broadcasts).
                    SetDomeOilFlow(level <= 0f
                        ? 0f
                        : Mathf.Max(0.1f, config.GasFlowMultiplier) * level);
                    break;

                case "garages":
                    // Spawned gas-station pumps run on the same synthetic oil-switch
                    // signal as the Dome pumps, scaled by their own flow config.
                    SetPumpFlow(garagePumps,
                        level <= 0f ? 0f : Mathf.Max(0.1f, config.GarageFuelFlowMultiplier) * level,
                        garagePumpBaseInterval, garagePumpBaseInterval);
                    break;

                case "water":
                    foreach (var tank in waterTanks)
                    {
                        if (tank == null || tank.IsDestroyed) continue;
                        tank.Pressure = WaterTreatmentWaterTank.maximumPressure * level; // renamed from maxPressure in 2633.288 (now 360)
                        if (level >= 1f) tank.timeSinceLastPressureIncrease = 0f;
                    }
                    break;

                case "airport":
                    ApplyAirport(level);
                    break;

                case "internet":
                    if (level <= 0f)
                    {
                        RunConVar("satellite.require_powerplant", origSatelliteRequirePowerplant ? "true" : "false");
                        RunConVar("satellite.free_power", origSatelliteFreePower ? "true" : "false");
                        RunConVar("satellite.free_fuel", origSatelliteFreeFuel ? "true" : "false");
                    }
                    else
                    {
                        RunConVar("satellite.require_powerplant", "false");
                        if (config.InternetFreeSatellite)
                        {
                            // Minor fault: the dish stays online but the free-items perk is suspended.
                            RunConVar("satellite.free_power", level >= 1f || origSatelliteFreePower ? "true" : "false");
                            RunConVar("satellite.free_fuel", level >= 1f || origSatelliteFreeFuel ? "true" : "false");
                        }
                    }
                    break;

                case "transportation":
                    if (level <= 0f) break; // major fault: no fuel service (hatches unseal, see CanLootEntity)
                    int fuelThreshold = level >= 1f ? 50 : 10;
                    int fuelRefill = level >= 1f ? 100 : 25;
                    for (int i = trains.Count - 1; i >= 0; i--)
                    {
                        var train = trains[i];
                        if (train == null || train.IsDestroyed) { trains.RemoveAt(i); continue; }
                        try
                        {
                            var fuel = train.GetFuelSystem() as EntityFuelSystem;
                            if (fuel != null && fuel.GetFuelAmount() < fuelThreshold) fuel.AddFuel(fuelRefill);
                        }
                        catch (Exception e) { PrintWarning($"Train fuel top-up failed on {train.ShortPrefabName} at {train.transform.position}: {e.Message}"); }
                    }
                    break;

                case ProtectionKey:
                    // Nothing to push: the heli/Bradley hooks read the live service and
                    // fault state on every call. Just keep the tracker tidy.
                    PruneHostility();
                    break;
            }
        }

        private void DeactivateService(string service)
        {
            if (service == "electricity")
                ApplyPoleMultiplier(false);

            List<IPowergridEntity> list;
            if (serviceEntities.TryGetValue(service, out list) && Manager != null)
                ApplyStage(list, Manager.CurrentStage);

            switch (service)
            {
                case "gas":
                    // The rig switches' true broadcast level isn't publicly readable; push 0
                    // and let the next genuine switch transition re-broadcast the real value.
                    SetDomeOilFlow(0f);
                    break;
                case "garages":
                    SetPumpFlow(garagePumps, 0f, garagePumpBaseInterval, garagePumpBaseInterval);
                    break;
                case "transportation":
                    // Drain the subsidized fuel back down so expiry doesn't hand out free low grade.
                    foreach (var train in trains)
                    {
                        if (train == null || train.IsDestroyed) continue;
                        try
                        {
                            var fuel = train.GetFuelSystem() as EntityFuelSystem;
                            if (fuel == null) continue;
                            int excess = fuel.GetFuelAmount() - 20;
                            if (excess > 0) fuel.RemoveFuel(excess);
                        }
                        catch { }
                    }
                    break;
                case "water":
                    foreach (var tank in waterTanks)
                        if (tank != null && !tank.IsDestroyed) tank.Pressure = 0f;
                    break;
                case "airport":
                    ReleaseAirport();
                    break;
                case "internet":
                    RunConVar("satellite.require_powerplant", origSatelliteRequirePowerplant ? "true" : "false");
                    RunConVar("satellite.free_power", origSatelliteFreePower ? "true" : "false");
                    RunConVar("satellite.free_fuel", origSatelliteFreeFuel ? "true" : "false");
                    break;
                case ProtectionKey:
                    // Vanilla resumes instantly (hooks go inert); forget who was hostile.
                    ClearHostility();
                    break;
            }
        }

        private void SetDomeOilFlow(float flow)
        {
            float baseInterval = domePumpPrefabInterval > 0f ? domePumpPrefabInterval : 60f;
            // Idle restores the raw prefab interval so a real rig switch runs the
            // pumps at vanilla cadence while the Gas service is off.
            SetPumpFlow(domePumps, flow, baseInterval, Mathf.Max(0f, domePumpPrefabInterval));
        }

        // Fractional oil-switch multipliers quantize to nothing: the per-collect amount
        // is a small integer, and AddResource rounds amount x multiplier — at 0.5x a
        // 1-per-collect pump rounds to 0 every single collect. So run the pumps at a
        // full 1x multiplier and scale the collect INTERVAL instead (0.5 flow = one
        // collect every 2x the base interval).
        private void SetPumpFlow(List<WaterCatcher> pumps, float flow, float baseInterval, float idleInterval)
        {
            for (int i = pumps.Count - 1; i >= 0; i--)
            {
                var pump = pumps[i];
                if (pump == null || pump.IsDestroyed) { pumps.RemoveAt(i); continue; }
                try
                {
                    pump.overrideCollectInterval = flow > 0f ? baseInterval / flow : idleInterval;
                    pump.OnOilSwitchToggled(flow > 0f ? 1f : 0f);
                }
                catch (Exception e) { PrintWarning($"Pump flow override failed: {e.Message}"); }
            }
        }

        private void ApplyStage(List<IPowergridEntity> entities, int stage)
        {
            for (int i = entities.Count - 1; i >= 0; i--)
            {
                var ipe = entities[i];
                var entity = ipe as BaseEntity;
                if (entity == null || entity.IsDestroyed)
                {
                    entities.RemoveAt(i);
                    continue;
                }
                try { ipe.Server_OnPowergridStageChanged(stage); }
                catch (Exception e) { PrintWarning($"Stage override failed for {entity.ShortPrefabName}: {e.Message}"); }
            }
        }

        #endregion

        #region Airport (Airfield control tower)

        // The tower's two terminals hang off two vanilla puzzle fuse boxes — two always-on
        // generators -> fuse boxes -> XOR -> airdrop terminal, AND -> chinook terminal — none
        // of it on the powergrid, so force-powering the monument never reached them. Paid
        // Airport = the plugin feeds each terminal's power input directly, seats a locked
        // "ghost" fuse in each box for the visuals, and holds that against the real wiring
        // via OnInputUpdate (two real fuses would XOR the airdrop terminal dark). A chinook
        // call starts the controller's break; when it ends, vanilla's 3h "active" window is
        // released so the next call can go out. Everything reverts on lapse/fault/unload.
        private const string GhostFuseName = "Public Works Fuse";
        private const int TerminalForcedPower = 100;

        private readonly List<ItemBasedFlowRestrictor> airfieldFuseBoxes = new List<ItemBasedFlowRestrictor>();
        private readonly Dictionary<ItemBasedFlowRestrictor, bool> origFuseBoxLock = new Dictionary<ItemBasedFlowRestrictor, bool>();
        private PressButton airfieldCallButton;     // the compact button wired to the chinook terminal's trigger
        private bool airportForced;                 // terminals are currently on plugin power
        private bool pushingTerminalInput;          // re-entrancy guard for OnInputUpdate
        private Timer resupplyBreakTimer;

        private AirfieldCallChinookTerminal ChinookTerminal()
        {
            foreach (var terminal in airfieldTerminals)
            {
                var chinook = terminal as AirfieldCallChinookTerminal;
                if (chinook != null && !chinook.IsDestroyed) return chinook;
            }
            return null;
        }

        // Walk each terminal's power input back through the XOR/AND to the fuse boxes, and
        // the chinook terminal's trigger input to its call button. IO refs resolve lazily
        // after load, so this re-runs until everything is found.
        private void ResolveAirfieldGraph()
        {
            if (airfieldFuseBoxes.Count > 0 && airfieldCallButton != null) return;
            foreach (var terminal in airfieldTerminals)
            {
                if (terminal == null || terminal.IsDestroyed || terminal.inputs == null) continue;
                try
                {
                    if (terminal.inputs.Length > 0)
                        CollectUpstreamFuseBoxes(terminal.inputs[0].connectedTo?.Get(true), 0);
                    if (airfieldCallButton == null && terminal is AirfieldCallChinookTerminal && terminal.inputs.Length > 1)
                        airfieldCallButton = terminal.inputs[1].connectedTo?.Get(true) as PressButton;
                }
                catch { }
            }
        }

        private void CollectUpstreamFuseBoxes(IOEntity entity, int depth)
        {
            if (entity == null || depth > 3) return;
            var box = entity as ItemBasedFlowRestrictor;
            if (box != null)
            {
                if (!airfieldFuseBoxes.Contains(box)) airfieldFuseBoxes.Add(box);
                return;
            }
            if (entity.inputs == null) return;
            foreach (var slot in entity.inputs)
                CollectUpstreamFuseBoxes(slot.connectedTo?.Get(true), depth + 1);
        }

        private void ApplyAirport(float level)
        {
            ResolveAirfieldGraph();
            // Major fault: the tower goes dark and vanilla fuses are the only way in.
            if (level < 0.5f) { ReleaseAirport(); return; }

            airportForced = true;
            foreach (var terminal in airfieldTerminals)
            {
                if (terminal == null || terminal.IsDestroyed) continue;
                SetTerminalPower(terminal, true);
                // Keep them charged too. The airdrop terminal is re-armed every tick so its
                // meter stays pinned and the 2x rate never lapses (re-activation just resets
                // the hour-long active timer); the chinook terminal only while idle — its
                // meter mid-break is vanilla's active countdown and a top-up means nothing.
                try
                {
                    if (terminal is AirfieldAirdropTerminal || !terminal.IsOn())
                        terminal.AddCharge(terminal.chargeRequiredToBecomeActive);
                }
                catch { }
            }
            SeatGhostFuses(true);

            // Paid while the chinook terminal was already mid-window (vanilla call before the
            // service, or a press we didn't see): start the break so the window is released.
            var chinook = ChinookTerminal();
            if (chinook != null && chinook.IsOn() && data.AirportBreakUntil <= Now)
                StartResupplyBreak(null);
        }

        private void ReleaseAirport()
        {
            airportForced = false;
            SeatGhostFuses(false);
            foreach (var terminal in airfieldTerminals)
                if (terminal != null && !terminal.IsDestroyed) SetTerminalPower(terminal, false);
        }

        // on = a synthetic feed into the power slot; off = whatever the real wiring is
        // delivering right now, so vanilla resumes exactly where it stands.
        private void SetTerminalPower(ChargeUpIOEntity terminal, bool on)
        {
            if (terminal.inputs == null || terminal.inputs.Length == 0) return;
            int amount = on ? TerminalForcedPower : UpstreamAmount(terminal, 0);
            pushingTerminalInput = true;
            try { terminal.UpdateFromInput(amount, 0); }
            catch (Exception e) { PrintWarning($"Airfield terminal power push failed on {terminal.ShortPrefabName}: {e.Message}"); }
            finally { pushingTerminalInput = false; }
        }

        private int UpstreamAmount(IOEntity entity, int inputSlot)
        {
            try
            {
                var source = entity.inputs[inputSlot].connectedTo?.Get(true);
                if (source == null || source.outputs == null) return 0;
                for (int i = 0; i < source.outputs.Length; i++)
                {
                    var output = source.outputs[i];
                    if (output.connectedToSlot == inputSlot && output.connectedTo?.Get(true) == entity)
                        return source.GetPassthroughAmount(i);
                }
            }
            catch { }
            return 0;
        }

        // While forced, the XOR/AND upstream must not knock a terminal dark (two fuses in the
        // boxes = XOR outputs 0 to the airdrop terminal). Cheap: one bool for everyone else.
        private object OnInputUpdate(IOEntity entity, int inputAmount, int inputSlot)
        {
            if (!airportForced || pushingTerminalInput || inputSlot != 0) return null;
            var terminal = entity as ChargeUpIOEntity;
            if (terminal == null || !airfieldTerminals.Contains(terminal)) return null;
            return false;
        }

        // A plugin-owned fuse in each box: sparks stop, the box lights, the AND gate even
        // powers the chinook terminal for real. Locked in with vanilla's own lock flag
        // ("Item is locked in!") and topped up every tick (a fuse burns 1/s while live).
        private void SeatGhostFuses(bool seat)
        {
            foreach (var box in airfieldFuseBoxes)
            {
                if (box == null || box.IsDestroyed || box.inventory == null) continue;
                try
                {
                    var item = box.inventory.GetSlot(0);
                    if (seat)
                    {
                        if (item != null && item.name != GhostFuseName) continue;  // a player's own fuse — let it burn out first
                        if (item == null)
                        {
                            item = ItemManager.CreateByName("fuse", 1);
                            if (item == null) continue;
                            item.name = GhostFuseName;
                            if (!item.MoveToContainer(box.inventory, 0)) { item.Remove(); continue; }
                        }
                        item.condition = item.maxCondition;
                        if (!origFuseBoxLock.ContainsKey(box)) origFuseBoxLock[box] = box.lockInventoryWhenItemPresent;
                        box.lockInventoryWhenItemPresent = true;
                    }
                    else
                    {
                        bool origLock;
                        if (origFuseBoxLock.TryGetValue(box, out origLock))
                        {
                            box.lockInventoryWhenItemPresent = origLock;
                            origFuseBoxLock.Remove(box);
                        }
                        if (item != null && item.name == GhostFuseName)
                        {
                            item.RemoveFromContainer();
                            item.Remove();
                        }
                    }
                }
                catch (Exception e) { PrintWarning($"Airfield fuse box update failed: {e.Message}"); }
            }
        }

        // Resupply (chinook) calls: vanilla activates the terminal for 3h, during which
        // presses do nothing. Paid service shortens that to the controller's break.
        private void StartResupplyBreak(BasePlayer caller)
        {
            float minutes = Mathf.Max(0f, config.AirportResupplyCooldownMinutes);
            if (minutes <= 0f) return;   // vanilla window
            data.AirportBreakUntil = Now + minutes * 60.0;
            SaveData();
            ScheduleResupplyBreakEnd();
            int shown = Mathf.CeilToInt(minutes);
            if (caller != null) AnnounceAll("ResupplyCalled", caller.displayName, shown);
            else AnnounceAll("ResupplyBreak", shown);
        }

        private void ScheduleResupplyBreakEnd()
        {
            resupplyBreakTimer?.Destroy();
            double remaining = data.AirportBreakUntil - Now;
            if (remaining <= 0) { EndResupplyBreak(); return; }
            resupplyBreakTimer = timer.Once((float)remaining, EndResupplyBreak);
        }

        private void EndResupplyBreak()
        {
            resupplyBreakTimer = null;
            data.AirportBreakUntil = 0;
            SaveData();
            if (!airportForced) return;   // lapsed or dark mid-break: vanilla's window runs its course
            var chinook = ChinookTerminal();
            if (chinook == null) return;
            try
            {
                if (chinook.IsOn()) chinook.Deactivate();                 // release vanilla's 3h window
                chinook.AddCharge(chinook.chargeRequiredToBecomeActive);  // and it's ready right now
            }
            catch (Exception e) { PrintWarning($"Airfield resupply re-arm failed: {e.Message}"); }
            AnnounceAll("ResupplyReady");
        }

        private object OnButtonPress(PressButton button, BasePlayer player)
        {
            if (button == null || airfieldCallButton == null || button != airfieldCallButton) return null;
            if (!airportForced) return null;   // unpaid / dark: vanilla rules
            var chinook = ChinookTerminal();
            if (chinook == null) return null;

            double remaining = data.AirportBreakUntil - Now;
            if (remaining > 0)
            {
                if (player != null) Reply(player, "ResupplyBreak", Math.Max(1, (int)Math.Ceiling(remaining / 60.0)));
                return false;
            }
            // Vanilla only activates on a press when the meter is full and it isn't already
            // running — any other press is a no-op and owes no break.
            if (chinook.IsOn() || chinook.Charge < chinook.chargeRequiredToBecomeActive) return null;
            StartResupplyBreak(player);
            return null;
        }

        #endregion

        #region Events (random faults)

        // A fault degrades one active service until a player repairs it on site or the
        // department's own crew auto-fixes it at the deadline. Contracts are open:
        // anyone who accepts at the office can race to fix it — first to deliver wins.
        private class FaultData
        {
            public string Service;
            public string Severity;      // "minor" = 50% output, "major" = full outage
            public float X, Y, Z;        // world position of the fault site
            public string Monument;      // display label for announcements
            public double Started;
            public double Deadline;      // epoch seconds: the crew auto-fixes at this time

            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<ulong> Accepted = new List<ulong>();

            [JsonIgnore]
            public Vector3 Site { get { return new Vector3(X, Y, Z); } }
        }

        private readonly Dictionary<string, MapMarkerGenericRadius> faultMarkers =
            new Dictionary<string, MapMarkerGenericRadius>();

        private FaultData GetFault(string service)
        {
            foreach (var fault in data.Faults)
                if (fault.Service == service) return fault;
            return null;
        }

        private float FaultLevel(string service)
        {
            var fault = GetFault(service);
            if (fault == null) return 1f;
            return fault.Severity == "major" ? 0f : 0.5f;
        }

        // Protection faults are bills — nobody gets paid by Cobalt for paying Cobalt.
        private int RepairReward(FaultData fault) =>
            fault.Service == ProtectionKey ? 0 :
            fault.Severity == "major" ? config.RepairRewardMajor : config.RepairRewardMinor;

        private int BillingFee(FaultData fault) =>
            Mathf.Max(0, fault.Severity == "major" ? config.ProtectionBillingFeeMajor : config.ProtectionBillingFeeMinor);

        private void FaultTick(float elapsed)
        {
            // Overdue faults: the department's own crew finishes the job.
            for (int i = data.Faults.Count - 1; i >= 0; i--)
                if (Now >= data.Faults[i].Deadline)
                    ResolveFault(data.Faults[i], null);

            if (config.FaultAverageHours <= 0f || elapsed <= 0f) return;
            if (data.Faults.Count >= Mathf.Max(1, config.FaultMaxConcurrent)) return;
            if (Now - data.LastFaultEpoch < config.FaultMinGapMinutes * 60.0) return;

            // Per-tick roll sized so faults average one per FaultAverageHours of uptime.
            if (UnityEngine.Random.value >= elapsed / (config.FaultAverageHours * 3600f)) return;

            var candidates = new List<string>();
            foreach (var key in ServiceKeys)
                if (IsActive(key) && GetFault(key) == null)
                    candidates.Add(key);

            string severity = UnityEngine.Random.value < Mathf.Clamp01(config.FaultMajorChance) ? "major" : "minor";
            while (candidates.Count > 0)
            {
                int pick = UnityEngine.Random.Range(0, candidates.Count);
                if (StartFault(candidates[pick], severity)) return;
                candidates.RemoveAt(pick); // no fault site on this map — try another service
            }
        }

        private bool StartFault(string service, string severity)
        {
            if (!IsService(service)) return false;

            Vector3 site;
            string monument;
            if (!TryPickFaultSite(service, out site, out monument)) return false;

            float minutes = severity == "major" ? config.AutoRepairMinutesMajor : config.AutoRepairMinutesMinor;
            var fault = new FaultData
            {
                Service = service,
                Severity = severity,
                X = site.x, Y = site.y, Z = site.z,
                Monument = monument,
                Started = Now,
                Deadline = Now + Mathf.Max(1f, minutes) * 60.0
            };
            data.Faults.Add(fault);
            data.LastFaultEpoch = Now;
            SaveData();

            SpawnFaultMarker(fault);
            ApplyService(service);

            if (service == ProtectionKey)
            {
                AnnounceAll("BillAnnounce",
                    Msg(severity == "major" ? "BillMajor" : "BillMinor", null),
                    monument,
                    MapHelper.PositionToString(site),
                    BillingFee(fault),
                    FormatSpan(fault.Deadline - Now));
                return true;
            }

            AnnounceAll("FaultAnnounce",
                Msg("Flavor." + service, null),
                monument,
                MapHelper.PositionToString(site),
                LabelFor(service, null),
                Msg(severity == "major" ? "FaultDown" : "FaultHalf", null),
                FormatSpan(fault.Deadline - Now),
                RepairReward(fault));
            return true;
        }

        private bool TryPickFaultSite(string service, out Vector3 site, out string monument)
        {
            site = Vector3.zero;
            monument = null;

            // Protection has no physical site: Cobalt Accounts flags the account and
            // the bill is settled at the office, so the "fault site" is the clerk.
            if (service == ProtectionKey)
            {
                if (clerk == null || clerk.IsDestroyed) return false;
                site = clerk.transform.position;
                monument = GetMonumentName(site);
                if (string.IsNullOrEmpty(monument))
                    monument = Msg("Site." + service, null);
                return true;
            }

            // Trains roam, so the transportation fault site is the Train Yard itself.
            if (service == "transportation")
            {
                if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return false;
                foreach (var info in TerrainMeta.Path.Monuments)
                {
                    if (info == null) continue;
                    string name = info.displayPhrase != null ? info.displayPhrase.english : info.name;
                    if (string.IsNullOrEmpty(name) || !name.ToLower().Contains("train yard")) continue;
                    site = info.transform.position;
                    if (TerrainMeta.HeightMap != null)
                        site.y = Mathf.Max(site.y, TerrainMeta.HeightMap.GetHeight(site));
                    monument = name.Trim();
                    return true;
                }
                return false;
            }

            var spots = new List<Vector3>();
            List<IPowergridEntity> list;
            if (serviceEntities.TryGetValue(service, out list))
                foreach (var ipe in list)
                {
                    var entity = ipe as BaseEntity;
                    if (entity != null && !entity.IsDestroyed) spots.Add(entity.transform.position);
                }
            if (spots.Count == 0 && service == "water")
                foreach (var tank in waterTanks)
                    if (tank != null && !tank.IsDestroyed) spots.Add(tank.transform.position);
            if (spots.Count == 0 && service == "airport")
                foreach (var terminal in airfieldTerminals)
                    if (terminal != null && !terminal.IsDestroyed) spots.Add(terminal.transform.position);
            if (spots.Count == 0) return false;

            site = spots[UnityEngine.Random.Range(0, spots.Count)];
            // Fall back to a lang-provided label when the site sits outside any monument
            // bounds (roadside powerline poles, the Dome pump that overhangs the bounds).
            monument = GetMonumentName(site);
            if (string.IsNullOrEmpty(monument))
                monument = Msg("Site." + service, null);
            return true;
        }

        private void ResolveFault(FaultData fault, BasePlayer fixer)
        {
            data.Faults.Remove(fault);
            KillFaultMarker(fault.Service);

            if (fault.Service == ProtectionKey)
            {
                // Billing hold: the fee was already taken by TryPayBill. No payout, no
                // bonus hours — Accounts just lifts the hold.
                if (fixer != null)
                    AnnounceAll("BillPaid", fixer.displayName, BillingFee(fault));
                else
                    AnnounceAll("BillCleared");
            }
            else if (fixer != null)
            {
                int reward = RepairReward(fault);
                if (reward > 0)
                {
                    var scrap = ItemManager.CreateByName(ScrapShortname, reward);
                    if (scrap != null) fixer.GiveItem(scrap);
                }
                if (config.RepairRewardBankedHours > 0f)
                {
                    double cap = config.MaxPrepaidDays * 24.0 * 3600.0;
                    data.Banked[fault.Service] = Math.Min(cap, GetBanked(fault.Service) + config.RepairRewardBankedHours * 3600.0);
                    warnLevel[fault.Service] = 0;
                }
                AnnounceAll("FaultPlayerFixed", LabelFor(fault.Service, null), fixer.displayName, reward);
            }
            else
            {
                AnnounceAll("FaultCrewFixed", LabelFor(fault.Service, null), fault.Monument);
            }
            SaveData();

            if (IsActive(fault.Service)) ApplyService(fault.Service);
        }

        private void CancelFault(FaultData fault)
        {
            data.Faults.Remove(fault);
            SaveData();
            KillFaultMarker(fault.Service);
        }

        private void SpawnFaultMarker(FaultData fault)
        {
            KillFaultMarker(fault.Service);
            var m = GameManager.server.CreateEntity(MarkerPrefab, fault.Site) as MapMarkerGenericRadius;
            if (m == null) return;
            m.enableSaving = false;
            m.radius = 0.12f;
            m.color1 = new Color(0.90f, 0.22f, 0.12f);
            m.alpha = 0.9f;
            m.color2 = Color.black;
            m.Spawn();
            m.SendUpdate();
            timer.Once(2f, () =>
            {
                if (m != null && !m.IsDestroyed) m.SendUpdate();
            });
            faultMarkers[fault.Service] = m;
        }

        private void KillFaultMarker(string service)
        {
            MapMarkerGenericRadius m;
            if (faultMarkers.TryGetValue(service, out m) && m != null && !m.IsDestroyed) m.Kill();
            faultMarkers.Remove(service);
        }

        private void KillAllFaultMarkers()
        {
            foreach (var m in faultMarkers.Values)
                if (m != null && !m.IsDestroyed) m.Kill();
            faultMarkers.Clear();
        }

        private List<KeyValuePair<ItemDefinition, int>> RepairMaterialsFor(FaultData fault)
        {
            var result = new List<KeyValuePair<ItemDefinition, int>>();
            List<string> entries;
            if (config.RepairMaterials == null || !config.RepairMaterials.TryGetValue(fault.Service, out entries) || entries == null)
                return result;

            float multiplier = fault.Severity == "major" ? Mathf.Max(1f, config.FaultMajorMaterialMultiplier) : 1f;
            foreach (var entry in entries)
            {
                var parts = entry.Split(' ');
                int amount;
                if (parts.Length != 2 || !int.TryParse(parts[1], out amount))
                {
                    PrintWarning($"Bad repair material entry '{entry}' — expected 'shortname amount'.");
                    continue;
                }
                var itemDef = ItemManager.FindItemDefinition(parts[0]);
                if (itemDef == null)
                {
                    PrintWarning($"Repair material '{parts[0]}' unknown — skipped.");
                    continue;
                }
                result.Add(new KeyValuePair<ItemDefinition, int>(itemDef, Mathf.Max(1, Mathf.RoundToInt(amount * multiplier))));
            }
            return result;
        }

        private string MaterialsText(List<KeyValuePair<ItemDefinition, int>> materials, string userId)
        {
            if (materials.Count == 0) return Msg("NoMaterials", userId);
            return string.Join(", ", materials.Select(m => $"{m.Value} x {m.Key.displayName.english}"));
        }

        private void TryRepairNearby(BasePlayer player)
        {
            if (data.Faults.Count == 0) return;

            FaultData fault = null;
            foreach (var candidate in data.Faults)
            {
                if (candidate.Service == ProtectionKey) continue; // billing hold: paid via the panel, not USE
                if (Vector3.Distance(player.transform.position, candidate.Site) <= config.FaultFixRange)
                {
                    fault = candidate;
                    break;
                }
            }
            if (fault == null) return;

            if (!fault.Accepted.Contains((ulong)player.userID))
            {
                Reply(player, "SiteNeedContract", LabelFor(fault.Service, player.UserIDString));
                return;
            }

            var materials = RepairMaterialsFor(fault);
            var missing = new List<string>();
            foreach (var m in materials)
            {
                int held = player.inventory.GetAmount(m.Key.itemid);
                if (held < m.Value) missing.Add($"{m.Value - held} x {m.Key.displayName.english}");
            }
            if (missing.Count > 0)
            {
                Reply(player, "SiteMissingMaterials", string.Join(", ", missing));
                return;
            }

            foreach (var m in materials)
                player.inventory.Take(null, m.Key.itemid, m.Value);

            ResolveFault(fault, player);
        }

        #endregion

        #region Office NPC

        // Store a spot relative to the containing monument so placement survives map
        // wipes (the same monument gets a new world position each map).
        private void CaptureAnchoredSpot(BasePlayer admin, out string monumentName, out string position, out float rotationY)
        {
            Vector3 world = admin.transform.position;
            float worldYaw = admin.eyes.rotation.eulerAngles.y + 180f;

            MonumentInfo monument = FindMonumentAt(world);
            if (monument != null)
            {
                Vector3 local = monument.transform.InverseTransformPoint(world);
                monumentName = monument.name;
                position = $"{local.x:F2} {local.y:F2} {local.z:F2}";
                rotationY = worldYaw - monument.transform.eulerAngles.y;
            }
            else
            {
                monumentName = "";
                position = $"{world.x:F1} {world.y:F1} {world.z:F1}";
                rotationY = worldYaw;
            }
        }

        private void CaptureOfficeSpot(BasePlayer admin)
        {
            string monumentName, position;
            float rotationY;
            CaptureAnchoredSpot(admin, out monumentName, out position, out rotationY);
            config.OfficeMonument = monumentName;
            config.OfficePosition = position;
            config.OfficeRotationY = rotationY;
            SaveConfig();
        }

        private MonumentInfo FindMonumentAt(Vector3 position)
        {
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return null;
            foreach (var monument in TerrainMeta.Path.Monuments)
                if (monument != null && monument.IsInBounds(position))
                    return monument;
            return null;
        }

        private bool TryResolveAnchoredSpot(string monumentName, string storedPosition, float storedRotation,
            out Vector3 position, out float rotationY)
        {
            position = Vector3.zero;
            rotationY = storedRotation;
            Vector3 stored;
            if (!TryParsePosition(storedPosition, out stored)) return false;

            if (string.IsNullOrEmpty(monumentName))
            {
                position = stored;
                return true;
            }

            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return false;
            MonumentInfo anchor = null;
            foreach (var monument in TerrainMeta.Path.Monuments)
                if (monument != null && monument.name == monumentName) { anchor = monument; break; }
            if (anchor == null)
            {
                // Fall back to a filename match in case the monument prefab variant differs this map.
                string fileName = monumentName.Substring(monumentName.LastIndexOf('/') + 1);
                foreach (var monument in TerrainMeta.Path.Monuments)
                    if (monument != null && monument.name.EndsWith(fileName)) { anchor = monument; break; }
            }
            if (anchor == null)
            {
                PrintWarning($"Anchor monument '{monumentName}' not found on this map — re-place with /pw setoffice or /pw setboombox.");
                return false;
            }

            position = anchor.transform.TransformPoint(stored);
            rotationY = anchor.transform.eulerAngles.y + storedRotation;
            return true;
        }

        private bool TryResolveOfficeSpot(out Vector3 position, out float rotationY) =>
            TryResolveAnchoredSpot(config.OfficeMonument, config.OfficePosition, config.OfficeRotationY,
                out position, out rotationY);

        private void SpawnOffice()
        {
            KillOffice();
            Vector3 position;
            float rotationY;
            if (!TryResolveOfficeSpot(out position, out rotationY))
            {
                PrintWarning("Office not placed yet — an admin should stand at the desired spot and run /pw setoffice");
                return;
            }

            var rotation = Quaternion.Euler(0f, rotationY, 0f);
            var entity = GameManager.server.CreateEntity(
                string.IsNullOrEmpty(config.ClerkPrefab) ? NpcPrefab : config.ClerkPrefab, position, rotation);
            if (entity == null)
                entity = GameManager.server.CreateEntity(
                    string.IsNullOrEmpty(config.ClerkPrefabFallback) ? NpcPrefabFallback : config.ClerkPrefabFallback, position, rotation);
            if (entity == null)
            {
                PrintError("Failed to create clerk NPC from both prefabs");
                return;
            }

            clerk = entity as BasePlayer;

            // Clients derive face/gender deterministically from userID — set it BEFORE
            // Spawn() so the appearance seed networks with the create. Steam-range values
            // only: small values would flip the engine's IsBot logic.
            if (clerk != null && config.ClerkFaceSeed != 0)
                clerk.userID = config.ClerkFaceSeed;

            entity.enableSaving = false;
            entity.Spawn();
            if (clerk != null)
            {
                clerk.displayName = config.ClerkName;
                PacifyClerk();
                DressClerk();
                ForceClerkPosition(position);
                clerk.SendNetworkUpdateImmediate();
                // The nav agent can warp an NPC to the nearest navmesh (e.g. the roof above
                // an interior office) a moment after spawn — re-assert the spot once settled.
                var target = position;
                timer.Once(2f, () => ForceClerkPosition(target));

                clerkHomeYaw = rotationY;
                clerkLastYaw = rotationY;
                faceTimer?.Destroy();
                if (config.ClerkFaceRange > 0f)
                    faceTimer = timer.Every(0.3f, FaceTick);
            }
            Puts($"Clerk spawned from {entity.PrefabName}");

            if (config.ShowMapMarker)
            {
                marker = GameManager.server.CreateEntity(MarkerPrefab, position) as MapMarkerGenericRadius;
                if (marker != null)
                {
                    marker.enableSaving = false;
                    marker.radius = 0.125f;
                    marker.color1 = new Color(0.98f, 0.65f, 0.15f);
                    marker.alpha = 0.9f;
                    marker.color2 = Color.black;
                    marker.Spawn();
                    marker.SendUpdate();
                    // The color update sent in the same frame as Spawn() is often lost
                    // client-side, leaving an invisible marker — re-push it a beat later.
                    timer.Once(2f, () =>
                    {
                        if (marker != null && !marker.IsDestroyed) marker.SendUpdate();
                    });
                }
            }
        }

        // The scientist prefabs come with a combat brain — shut it down completely so the
        // clerk never targets, moves, or shoots, and take its weapons away for good measure.
        private void PacifyClerk()
        {
            try
            {
                var brain = clerk.GetComponent<BaseAIBrain>();
                if (brain != null)
                {
                    brain.SetThinkMode(AIThinkMode.None);
                    brain.SetEnabled(false);
                }
            }
            catch (Exception e) { PrintWarning($"Could not disable clerk brain: {e.Message}"); }

            try { clerk.inventory?.Strip(); }
            catch (Exception e) { PrintWarning($"Could not strip clerk inventory: {e.Message}"); }
        }

        // Same outfit convention as the RustQuests traders: "shortname" or "shortname@skinId".
        private void DressClerk()
        {
            if (clerk == null || config.ClerkOutfit == null) return;
            var wear = clerk.inventory?.containerWear;
            if (wear == null) return;

            foreach (var entry in config.ClerkOutfit)
            {
                string shortname = entry;
                ulong skin = 0;
                int at = entry.IndexOf('@');
                if (at > 0)
                {
                    shortname = entry.Substring(0, at);
                    ulong.TryParse(entry.Substring(at + 1), out skin);
                }
                var item = ItemManager.CreateByName(shortname, 1, skin);
                if (item == null) { PrintWarning($"Clerk outfit item '{shortname}' unknown — skipped."); continue; }
                if (!item.MoveToContainer(wear)) item.Remove();
            }
            clerk.SendNetworkUpdate();
        }

        // Pin the clerk exactly where the admin placed him: the scientist prefab's nav agent
        // snaps to the nearest navmesh otherwise (roofs, roads — anywhere but the office).
        private void ForceClerkPosition(Vector3 position)
        {
            if (clerk == null || clerk.IsDestroyed) return;
            try
            {
                var npc = clerk as NPCPlayer;
                if (npc != null && npc.NavAgent != null) npc.NavAgent.enabled = false;
            }
            catch { }
            clerk.ServerPosition = position;
            clerk.SendNetworkUpdate();
        }

        // Instant fuel on boarding, so a freshly spawned cart never has an empty tank
        // during the window before its first 30-second top-up tick.
        private void OnEntityMounted(BaseMountable mountable, BasePlayer player)
        {
            if (!IsActive("transportation") || mountable == null) return;
            float level = FaultLevel("transportation");
            if (level <= 0f) return;
            var train = mountable.GetParentEntity() as TrainEngine ?? mountable as TrainEngine;
            if (train == null || train.IsDestroyed) return;
            try
            {
                var fuel = train.GetFuelSystem() as EntityFuelSystem;
                int threshold = level >= 1f ? 50 : 10;
                if (fuel != null && fuel.GetFuelAmount() < threshold) fuel.AddFuel(level >= 1f ? 100 : 25);
            }
            catch (Exception e) { PrintWarning($"Mount fuel top-up failed: {e.Message}"); }
        }

        // While Transportation is active the plugin keeps train tanks fueled, which would
        // otherwise be an infinite low grade faucet — so the fuel hatch is sealed.
        // Grocer shop UI: supermarket grocers close with Markets, station grocers
        // (the corner store) close with Garages.
        private object CanUseVending(BasePlayer player, VendingMachine machine)
        {
            var grocer = machine as NPCVendingMachine;
            if (grocer == null) return null;
            if (grocers.Contains(grocer) && !GrocerOpen())
            {
                Reply(player, "MarketsClosed");
                return false;
            }
            if (stationGrocers.Contains(grocer) && !StationGrocerOpen())
            {
                Reply(player, "GaragesClosed");
                return false;
            }
            return null;
        }

        private object CanLootEntity(BasePlayer player, StorageContainer container)
        {
            if (container == null) return null;

            // Drone terminal: offline while the service is down or faulted at all.
            if (container is MarketTerminal && !DroneOpen() &&
                marketplaces.Contains(container.GetParentEntity() as Marketplace))
            {
                Reply(player, IsActive("markets") ? "DroneDown" : "MarketsClosed");
                return false;
            }

            if (!IsActive("transportation")) return null;
            if (!(container.GetParentEntity() is TrainEngine)) return null;
            if (FaultLevel("transportation") <= 0f) return null; // major fault: refuel by hand while the service is down
            Reply(player, "TrainsSealed");
            return false;
        }

        private object OnNpcTarget(BaseEntity npc, BaseEntity target)
        {
            if (clerk != null && (npc == clerk || target == clerk)) return true;
            return null;
        }

        // Turn the clerk toward the nearest player in range; drift back home when alone.
        private void FaceTick()
        {
            if (clerk == null || clerk.IsDestroyed) return;

            BasePlayer nearest = null;
            float bestSqr = config.ClerkFaceRange * config.ClerkFaceRange;
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || player == clerk || player.IsDead()) continue;
                float sqr = (player.transform.position - clerk.transform.position).sqrMagnitude;
                if (sqr < bestSqr) { bestSqr = sqr; nearest = player; }
            }

            float targetYaw = clerkHomeYaw;
            if (nearest != null)
            {
                Vector3 dir = nearest.transform.position - clerk.transform.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.01f)
                    targetYaw = Quaternion.LookRotation(dir.normalized).eulerAngles.y;
            }

            if (Mathf.Abs(Mathf.DeltaAngle(clerkLastYaw, targetYaw)) < 3f) return;
            clerkLastYaw = targetYaw;
            clerk.viewAngles = new Vector3(0f, targetYaw, 0f);
            clerk.ServerRotation = Quaternion.Euler(0f, targetYaw, 0f);
            clerk.SendNetworkUpdate();
        }

        private const string BoomboxPrefab = "assets/prefabs/voiceaudio/boombox/boombox.deployed.prefab";

        // Resolve the configured station against the game's station lists by name
        // (case-insensitive contains, same convention as Island Taxi), or accept a
        // raw URL/ip. Null = neither a known name nor URL-shaped.
        private string ResolveStationUrl(string wanted)
        {
            if (string.IsNullOrWhiteSpace(wanted)) return null;
            try { BoomBox.ParseServerUrlList(); } catch { }
            foreach (var stations in new[] { BoomBox.ValidStations, BoomBox.ServerValidStations })
            {
                if (stations == null) continue;
                foreach (var kv in stations)
                    if (kv.Key.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                        return kv.Value;
            }
            return wanted.Contains(".") ? wanted : null;
        }

        // Reverse lookup so an admin retune through the vanilla UI saves the readable
        // station name to config instead of its stream URL.
        private string StationNameForUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            foreach (var stations in new[] { BoomBox.ValidStations, BoomBox.ServerValidStations })
            {
                if (stations == null) continue;
                foreach (var kv in stations)
                    if (kv.Value == url) return kv.Key;
            }
            return null;
        }

        private void SpawnBoombox()
        {
            KillBoombox();
            if (string.IsNullOrEmpty(config.BoomboxStation)) return;

            Vector3 position;
            float rotationY;
            if (!TryResolveAnchoredSpot(config.BoomboxMonument, config.BoomboxPosition, config.BoomboxRotationY,
                out position, out rotationY))
                return;

            // Raw-spawned deployables can ground-snap through interior floors down to
            // terrain — lift the spawn slightly and pin the position after spawn.
            position += Vector3.up * config.BoomboxHeightOffset;

            string stationUrl = ResolveStationUrl(config.BoomboxStation);
            if (stationUrl == null)
            {
                PrintWarning($"Boombox station '{config.BoomboxStation}' matches nothing in the game's station lists " +
                             $"and doesn't look like a URL — using it as-is; expect silence. Use a station name from " +
                             $"the in-game boombox UI or a stream URL.");
                stationUrl = config.BoomboxStation;
            }

            var entity = GameManager.server.CreateEntity(BoomboxPrefab, position, Quaternion.Euler(0f, rotationY, 0f));
            if (entity == null)
            {
                PrintError($"Failed to create boombox from {BoomboxPrefab}");
                return;
            }
            entity.enableSaving = false;
            entity.Spawn();
            boombox = entity as DeployableBoomBox;
            if (boombox != null)
            {
                var target = position;
                boombox.ServerPosition = target;
                boombox.BoxController.CurrentRadioIp = stationUrl;
                boombox.BoxController.ServerTogglePlay(true);
                boombox.SendNetworkUpdate();
                timer.Once(2f, () =>
                {
                    if (boombox == null || boombox.IsDestroyed) return;
                    boombox.ServerPosition = target;
                    boombox.SendNetworkUpdate();
                });
                Puts($"Office boombox playing '{config.BoomboxStation}' ({stationUrl}) at {target}");

                // A station clients aren't allowed to play = a silently dead boombox; warn loudly.
                try
                {
                    bool approved = stationUrl == "rustradio.facepunch.com"
                        || (BoomBox.ValidStations != null && BoomBox.ValidStations.ContainsValue(stationUrl))
                        || (BoomBox.ServerValidStations != null && BoomBox.ServerValidStations.ContainsValue(stationUrl));
                    if (!approved)
                        PrintWarning($"Boombox station '{stationUrl}' is not in boombox.serverurllist — " +
                                     $"clients may hear silence. Fix: boombox.serverurllist \"Office Radio,{stationUrl}\"");
                }
                catch { }
            }
        }

        // The pumps themselves are Dome-style crude producers (the forecourt gas pumps
        // are client-baked scenery with no server entity), retargeted to produce low
        // grade. Each configured spot is gas-station-local, so one captured spot places
        // a pump at every gas station on the map.
        private const string CrudePumpPrefab = "assets/prefabs/resource/liquidproducer/crudeoilproducer.prefab";

        private void SpawnGaragePumps()
        {
            KillGaragePumps();
            if (!config.GaragePumpsEnabled || config.GaragePumpSpots == null || config.GaragePumpSpots.Count == 0)
                return;
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return;

            var fuelDef = ItemManager.FindItemDefinition("lowgradefuel");
            if (fuelDef == null)
            {
                PrintWarning("Item definition 'lowgradefuel' not found — garage pumps not spawned.");
                return;
            }

            int stations = 0;
            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (monument == null) continue;
                string name = monument.displayPhrase != null ? monument.displayPhrase.english : monument.name;
                if (string.IsNullOrEmpty(name)) continue;
                string lower = name.ToLower();
                if (!lower.Contains("oxum") && !lower.Contains("gas station")) continue;
                stations++;

                foreach (var spot in config.GaragePumpSpots)
                {
                    Vector3 local;
                    float rotationY;
                    if (!TryParsePumpSpot(spot, out local, out rotationY)) continue;

                    Vector3 world = monument.transform.TransformPoint(local);
                    var rotation = Quaternion.Euler(0f, monument.transform.eulerAngles.y + rotationY, 0f);
                    var entity = GameManager.server.CreateEntity(CrudePumpPrefab, world, rotation);
                    var pump = entity as WaterCatcher;
                    if (pump == null)
                    {
                        if (entity != null) entity.Kill();
                        PrintError($"Failed to create garage pump from {CrudePumpPrefab}");
                        return;
                    }

                    // Set before Spawn() so ServerInit sees the retargeted item; the
                    // pump keeps requireOilSwitchActive so our synthetic multiplier
                    // scales it, but drops conditionalSpawning so it never hides
                    // itself waiting on a powergrid stage.
                    pump.itemToCreate = fuelDef;
                    pump.ValidItems = new ItemDefinition[] { fuelDef };
                    pump.conditionalSpawning = false;
                    entity.enableSaving = false;
                    entity.Spawn();

                    // The holding cap: production stops once slot 0 reaches the
                    // container's stack cap (see WaterCatcher.IsFull). The container
                    // is also built liquid-only with a crude whitelist from the prefab
                    // — produced low grade would be silently destroyed on insert
                    // (AddItem removes anything MoveToContainer rejects), so open the
                    // contents type and retarget the whitelist to the fuel item.
                    if (pump.inventory != null)
                    {
                        pump.inventory.maxStackSize =
                            Mathf.Clamp(config.GaragePumpMaxFuel, 1, Mathf.Max(1, fuelDef.stackable));
                        pump.inventory.allowedContents =
                            ItemContainer.ContentsType.Generic | ItemContainer.ContentsType.Liquid;
                        pump.inventory.SetOnlyAllowedItem(fuelDef);
                    }

                    // The prefab's collect cadence is the 1x baseline SetPumpFlow scales.
                    garagePumpBaseInterval = pump.overrideCollectInterval > 0f ? pump.overrideCollectInterval : 60f;

                    // Restore fuel banked across the last reload (same spot = same key).
                    int savedFuel;
                    if (pump.inventory != null &&
                        data.PumpFuel.TryGetValue(PumpKey(world), out savedFuel) && savedFuel > 0)
                    {
                        var restored = ItemManager.Create(fuelDef, Mathf.Min(savedFuel, pump.inventory.maxStackSize), 0UL);
                        if (restored != null)
                        {
                            if (!restored.MoveToContainer(pump.inventory)) restored.Remove(0f);
                            else if (pump.lockInventory) restored.LockUnlock(true);
                        }
                    }

                    garagePumps.Add(pump);
                }
            }

            if (garagePumps.Count > 0)
                Puts($"Spawned {garagePumps.Count} garage fuel pump(s) across {stations} gas station(s).");
            else if (stations == 0)
                PrintWarning("No gas station monuments found on this map — garage pumps not spawned.");
        }

        // The producer's own loot panel is gated on a tanker hose hookup, so players
        // can't open it — instead, USE with a hose tool in hand drains the stored
        // fuel straight into the player's inventory.
        private bool TryDrainPump(BasePlayer player)
        {
            if (garagePumps.Count == 0) return false;

            // Pumps sit ~1.5m apart behind the station, so "first one in range" picks the
            // wrong neighbour. Exact crosshair hit first; then the best-aimed candidate.
            WaterCatcher pump = null;
            RaycastHit hit;
            if (Physics.Raycast(player.eyes.HeadRay(), out hit, config.InteractRange + 1f))
            {
                var hitPump = hit.GetEntity() as WaterCatcher;
                if (hitPump != null && garagePumps.Contains(hitPump)) pump = hitPump;
            }
            if (pump == null)
            {
                float bestDot = 0.5f;
                foreach (var candidate in garagePumps)
                {
                    if (candidate == null || candidate.IsDestroyed) continue;
                    if (Vector3.Distance(player.transform.position, candidate.transform.position) > config.InteractRange)
                        continue;
                    Vector3 toPump = (candidate.transform.position + Vector3.up * 1f) - player.eyes.position;
                    float dot = Vector3.Dot(player.eyes.HeadForward(), toPump.normalized);
                    if (dot > bestDot) { bestDot = dot; pump = candidate; }
                }
            }
            if (pump == null) return false;

            var held = player.GetActiveItem();
            if (held == null || held.info == null || held.info.shortname != "hosetool")
            {
                Reply(player, "PumpNeedHose");
                return true;
            }

            var stored = pump.inventory != null ? pump.inventory.GetSlot(0) : null;
            if (stored == null || stored.amount <= 0)
            {
                Reply(player, "PumpEmpty");
                return true;
            }

            int amount = stored.amount;
            if (stored.IsLocked()) stored.LockUnlock(false); // producer locks the stack against looting
            stored.RemoveFromContainer();
            player.GiveItem(stored, BaseEntity.GiveItemReason.PickedUp);
            Reply(player, "PumpDrained", amount);
            return true;
        }

        private static string PumpKey(Vector3 world) => $"{world.x:F0},{world.y:F0},{world.z:F0}";

        private void KillGaragePumps()
        {
            data.PumpFuel.Clear();
            foreach (var pump in garagePumps)
            {
                if (pump == null || pump.IsDestroyed) continue;
                var stored = pump.inventory != null ? pump.inventory.GetSlot(0) : null;
                if (stored != null && stored.amount > 0)
                    data.PumpFuel[PumpKey(pump.transform.position)] = stored.amount;
                pump.Kill();
            }
            garagePumps.Clear();
            SaveData();
        }

        #region Markets extras (grocer + drone marketplace)

        private const string GrocerPrefab = "assets/prefabs/deployable/vendingmachine/npcvendingmachine.prefab";
        private const string MarketplacePrefab = "assets/prefabs/misc/marketplace/marketplace.prefab";

        // Parse "shortname sellAmount scrapPrice" stock lines into a vending order set
        // the NPC machine restocks from on its own (players' scrap is destroyed on
        // purchase — same sink as the office).
        private NPCVendingOrder BuildOrderSet(List<string> lines)
        {
            var scrapDef = ItemManager.FindItemDefinition(ScrapShortname);
            if (scrapDef == null || lines == null) return null;

            var entries = new List<NPCVendingOrder.Entry>();
            foreach (var line in lines)
            {
                if (string.IsNullOrEmpty(line)) continue;
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                int sellAmount, price;
                if (parts.Length != 3 || !int.TryParse(parts[1], out sellAmount) || !int.TryParse(parts[2], out price))
                {
                    PrintWarning($"Bad grocer stock line '{line}' — expected 'shortname sellAmount scrapPrice'.");
                    continue;
                }
                var def = ItemManager.FindItemDefinition(parts[0]);
                if (def == null)
                {
                    PrintWarning($"Grocer stock item '{parts[0]}' not found — skipped.");
                    continue;
                }
                entries.Add(new NPCVendingOrder.Entry
                {
                    sellItem = def,
                    sellItemAmount = Mathf.Max(1, sellAmount),
                    currencyItem = scrapDef,
                    currencyAmount = Mathf.Max(1, price),
                    maxStock = 30,
                    refillAmount = 30,
                    refillDelay = 300f
                });
            }
            if (entries.Count == 0) return null;
            if (entries.Count > 7)
            {
                PrintWarning($"Grocer stock has {entries.Count} lines but NPC vending machines cap at 7 sell orders — extras dropped.");
                entries.RemoveRange(7, entries.Count - 7);
            }

            var orderSet = ScriptableObject.CreateInstance<NPCVendingOrder>();
            orderSet.orders = entries.ToArray();
            return orderSet;
        }

        // Degradation ladder: a minor fault takes the drone delivery offline first;
        // the walk-in grocer only closes on a major fault (or a lapsed service).
        // The gas-station corner store runs on the Garages service instead.
        private bool GrocerOpen() => IsActive("markets") && FaultLevel("markets") > 0f;
        private bool DroneOpen() => IsActive("markets") && FaultLevel("markets") >= 1f;
        private bool StationGrocerOpen() => IsActive("garages") && FaultLevel("garages") > 0f;

        // The terminal's market UI opens via raw RPCs with no Oxide hook to veto, so
        // "offline" is physical: while the drone service is down the terminal kiosks
        // (children of the marketplace) are removed and the stall building stays; on
        // reopen they respawn through the game's own Setup() path, exactly as the
        // marketplace spawns them itself. No reflection (uMod disallows it).
        private void SetMarketplaceTerminals(bool open)
        {
            foreach (var market in marketplaces)
            {
                if (market == null || market.IsDestroyed || market.terminalEntities == null) continue;

                if (!open)
                {
                    for (int i = 0; i < market.terminalEntities.Length; i++)
                    {
                        var terminal = market.terminalEntities[i].Get(true);
                        if (terminal != null && !terminal.IsDestroyed) terminal.Kill();
                    }
                    continue;
                }

                if (market.terminalPoints == null) continue;
                for (int i = 0; i < market.terminalPoints.Length && i < market.terminalEntities.Length; i++)
                {
                    var existing = market.terminalEntities[i].Get(true);
                    if (existing != null && !existing.IsDestroyed) continue;
                    var point = market.terminalPoints[i];
                    if (point == null) continue;

                    var entity = GameManager.server.CreateEntity(
                        market.terminalPrefab.resourcePath, point.position, point.rotation);
                    var terminal = entity as MarketTerminal;
                    if (terminal == null)
                    {
                        if (entity != null) entity.Kill();
                        continue;
                    }
                    entity.enableSaving = false;
                    entity.SetParent(market, true, false);
                    entity.Spawn();
                    terminal.Setup(market);
                    market.terminalEntities[i].Set(terminal);
                }
            }
        }

        // With the kiosks gone there's nothing to press E on, so USE at an empty
        // terminal point gets the explanation from us instead.
        private bool TryMarketClosedNotice(BasePlayer player)
        {
            if (DroneOpen() || marketplaces.Count == 0) return false;
            foreach (var market in marketplaces)
            {
                if (market == null || market.IsDestroyed || market.terminalPoints == null) continue;
                foreach (var point in market.terminalPoints)
                {
                    if (point == null) continue;
                    if (Vector3.Distance(player.transform.position, point.position) > config.InteractRange)
                        continue;
                    Reply(player, IsActive("markets") ? "DroneDown" : "MarketsClosed");
                    return true;
                }
            }
            return false;
        }

        // Idempotent: called every tick; spawns whatever is configured and missing.
        private void EnsureMarketsExtras()
        {
            grocers.RemoveAll(g => g == null || g.IsDestroyed);
            stationGrocers.RemoveAll(g => g == null || g.IsDestroyed);
            marketplaces.RemoveAll(m => m == null || m.IsDestroyed);

            if (config.MarketsGrocerEnabled)
            {
                if (grocers.Count == 0 && !string.IsNullOrEmpty(config.GrocerSpot))
                {
                    if (grocerOrders == null) grocerOrders = BuildOrderSet(config.GrocerOrders);
                    if (grocerOrders != null)
                        ForEachMonumentSpot(config.GrocerSpot, SupermarketKeywords,
                            (position, rotation) => SpawnGrocer(grocerOrders, position, rotation, grocers));
                }
                if (stationGrocers.Count == 0 && !string.IsNullOrEmpty(config.GarageGrocerSpot))
                {
                    if (garageGrocerOrders == null) garageGrocerOrders = BuildOrderSet(config.GarageGrocerOrders);
                    if (garageGrocerOrders != null)
                        ForEachMonumentSpot(config.GarageGrocerSpot, GasStationKeywords,
                            (position, rotation) => SpawnGrocer(garageGrocerOrders, position, rotation, stationGrocers));
                }
            }

            if (config.MarketsDroneEnabled && marketplaces.Count == 0 && !string.IsNullOrEmpty(config.MarketplaceSpot))
                ForEachMonumentSpot(config.MarketplaceSpot, SupermarketKeywords, (position, rotation) =>
                {
                    var entity = GameManager.server.CreateEntity(MarketplacePrefab, position, rotation);
                    var market = entity as Marketplace;
                    if (market == null)
                    {
                        if (entity != null) entity.Kill();
                        PrintError($"Failed to create marketplace from {MarketplacePrefab}");
                        return;
                    }
                    entity.enableSaving = false;
                    entity.Spawn(); // spawns its own terminals as parented children
                    // The prefab's terminals are already spawned (and in saveList) by the
                    // time Spawn() returns — EnableSaving(false) also pulls them out of
                    // saveList, where a raw field write would leak a null entry on kill.
                    if (market.terminalEntities != null)
                        foreach (var terminalRef in market.terminalEntities)
                        {
                            var terminal = terminalRef.Get(true);
                            if (terminal != null) terminal.EnableSaving(false);
                        }
                    marketplaces.Add(market);
                });
        }

        private void KillMarketsExtras()
        {
            foreach (var grocer in grocers)
                if (grocer != null && !grocer.IsDestroyed) grocer.Kill();
            grocers.Clear();
            foreach (var grocer in stationGrocers)
                if (grocer != null && !grocer.IsDestroyed) grocer.Kill();
            stationGrocers.Clear();
            foreach (var market in marketplaces)
                if (market != null && !market.IsDestroyed) market.Kill(); // children (terminals) die with it
            marketplaces.Clear();
        }

        private static readonly string[] SupermarketKeywords = { "supermarket" };
        private static readonly string[] GasStationKeywords = { "oxum", "gas station" };

        private void SpawnGrocer(NPCVendingOrder orders, Vector3 position, Quaternion rotation,
            List<NPCVendingMachine> trackIn)
        {
            var entity = GameManager.server.CreateEntity(GrocerPrefab, position, rotation);
            var machine = entity as NPCVendingMachine;
            if (machine == null)
            {
                if (entity != null) entity.Kill();
                PrintError($"Failed to create grocer from {GrocerPrefab}");
                return;
            }
            machine.vendingOrders = orders;
            machine.BypassDynamicPricing = true;
            machine.shopName = config.GrocerName;
            entity.enableSaving = false;
            entity.Spawn();
            trackIn.Add(machine);
        }

        private static bool MonumentMatches(MonumentInfo monument, string[] keywords)
        {
            if (monument == null) return false;
            string name = monument.displayPhrase != null ? monument.displayPhrase.english : monument.name;
            if (string.IsNullOrEmpty(name)) return false;
            string lower = name.ToLower();
            foreach (var keyword in keywords)
                if (lower.Contains(keyword)) return true;
            return false;
        }

        // ----- Drinkable junk items -----
        // mrspice.can and bottle.vodka are Food-category collectibles with no consume
        // action (isUsable=false), and consume buttons are client-side data a server
        // plugin can't add — so /drink is the interaction, advertised on purchase.

        private class Drinkable
        {
            public ItemDefinition Def;
            public float Health, HealOverTime, Calories, Hydration, PoisonChance, DrunkSeconds;
        }

        private List<Drinkable> drinkables;
        private readonly HashSet<ulong> drinkHintShown = new HashSet<ulong>();
        private const string DrinkEffect = "assets/bundled/prefabs/fx/gestures/drink_generic.prefab";
        // The vomit gesture the pickle jar / spoiled produce play on a bad roll.
        private const string VomitEffect = "assets/bundled/prefabs/fx/gestures/drink_vomit.prefab";

        // A bad batch applies the actual snakebite debuffs, copied off the snake
        // prefab so the visuals match a real bite exactly.
        private const string SnakePrefab = "assets/rust.ai/agents/snake/snake.entity.prefab";
        private List<ModifierDefintion> snakeBiteModifiers;
        private bool snakeBiteLookupDone;

        private List<ModifierDefintion> GetSnakeBiteModifiers()
        {
            if (snakeBiteLookupDone) return snakeBiteModifiers;
            snakeBiteLookupDone = true;
            try
            {
                var prefab = GameManager.server.FindPrefab(SnakePrefab);
                var hazard = prefab != null ? prefab.GetComponentInChildren<SnakeHazard>() : null;
                if (hazard != null && hazard.FailModifierEffects != null && hazard.FailModifierEffects.Count > 0)
                    snakeBiteModifiers = hazard.FailModifierEffects;
                else
                    PrintWarning("Snake prefab bite modifiers not found — bad drinks fall back to poison only.");
            }
            catch (Exception e) { PrintWarning($"Snake prefab lookup failed: {e.Message}"); }
            return snakeBiteModifiers;
        }

        private List<Drinkable> GetDrinkables()
        {
            if (drinkables != null) return drinkables;
            drinkables = new List<Drinkable>();
            if (config.Drinkables == null) return drinkables;
            foreach (var line in config.Drinkables)
            {
                if (string.IsNullOrEmpty(line)) continue;
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                float health, healOverTime, calories, hydration, poison;
                float drunkSeconds = 0f;
                if (parts.Length < 6 || !float.TryParse(parts[1], out health) ||
                    !float.TryParse(parts[2], out healOverTime) || !float.TryParse(parts[3], out calories) ||
                    !float.TryParse(parts[4], out hydration) || !float.TryParse(parts[5], out poison))
                {
                    PrintWarning($"Bad drinkable line '{line}' — expected 'shortname health healOverTime calories hydration poisonChance [drunkSeconds]'.");
                    continue;
                }
                if (parts.Length >= 7) float.TryParse(parts[6], out drunkSeconds);
                var def = ItemManager.FindItemDefinition(parts[0]);
                if (def == null)
                {
                    PrintWarning($"Drinkable item '{parts[0]}' not found — skipped.");
                    continue;
                }
                drinkables.Add(new Drinkable
                {
                    Def = def,
                    Health = health,
                    HealOverTime = healOverTime,
                    Calories = calories,
                    Hydration = hydration,
                    PoisonChance = Mathf.Clamp01(poison),
                    DrunkSeconds = drunkSeconds
                });
            }
            return drinkables;
        }

        // The inventory INFORMATION panel (healing/calories/hydration) is rendered
        // client-side from consumable data these items don't have — so the stats
        // live here instead, shown by /drink info and with the purchase hint.
        private string DescribeDrink(Drinkable drink)
        {
            var parts = new List<string>();
            if (drink.Health > 0f) parts.Add($"+{drink.Health:0} health");
            if (drink.HealOverTime > 0f) parts.Add($"+{drink.HealOverTime:0} healing");
            if (drink.Calories > 0f) parts.Add($"+{drink.Calories:0} calories");
            if (drink.Hydration > 0f) parts.Add($"+{drink.Hydration:0} hydration");
            if (drink.DrunkSeconds > 0f) parts.Add($"{drink.DrunkSeconds:0}s buzz");
            if (drink.PoisonChance > 0f) parts.Add($"{Mathf.RoundToInt(drink.PoisonChance * 100f)}% bad batch risk");
            return $"<color=#8bc34a>{drink.Def.displayName.english}</color> — {string.Join(", ", parts)}";
        }

        [ChatCommand("drink")]
        private void CmdDrink(BasePlayer player, string command, string[] args)
        {
            if (player == null || player.IsDead()) return;
            string filter = args != null && args.Length > 0 ? args[0].ToLower() : null;

            if (filter == "info" || filter == "list" || filter == "menu")
            {
                Reply(player, "DrinkMenu");
                foreach (var drink in GetDrinkables())
                    SendReply(player, DescribeDrink(drink));
                return;
            }

            foreach (var drink in GetDrinkables())
            {
                if (filter != null && !drink.Def.shortname.ToLower().Contains(filter) &&
                    !drink.Def.displayName.english.ToLower().Contains(filter)) continue;
                var item = player.inventory.FindItemByItemID(drink.Def.itemid);
                if (item == null) continue;
                ConsumeDrink(player, drink, item);
                return;
            }
            Reply(player, "DrinkNothing");
        }

        private bool TryBeltDrink(BasePlayer player)
        {
            if (player.IsDead() || player.inventory == null || player.inventory.containerBelt == null) return false;
            foreach (var drink in GetDrinkables())
                foreach (var item in player.inventory.containerBelt.itemList)
                {
                    if (item == null || item.info == null || item.info.itemid != drink.Def.itemid) continue;
                    ConsumeDrink(player, drink, item);
                    return true;
                }
            return false;
        }

        private void ConsumeDrink(BasePlayer player, Drinkable drink, Item item)
        {
            item.UseItem(1);

            // Bad batch: the pickle-jar roulette path (ItemModConsumeChance's secondary
            // consumable) — you retch it straight back up, so none of the drink's
            // benefits land; instead the stomach empties, poison sets in, and the
            // snakebite debuffs ride along. Same vomit gesture fx as spoiled food.
            if (drink.PoisonChance > 0f && UnityEngine.Random.value < drink.PoisonChance)
            {
                try { Effect.server.Run(VomitEffect, player, 0u, Vector3.zero, Vector3.zero); } catch { }
                float loss = Mathf.Max(0f, config.DrinkBadBatchStomachLoss);
                if (loss > 0f)
                {
                    player.metabolism.calories.Subtract(loss);
                    player.metabolism.hydration.Subtract(loss);
                }
                player.metabolism.poison.Add(Mathf.Max(0f, config.DrinkPoisonAmount));
                // The real snakebite debuff set, straight off the snake prefab.
                var bite = GetSnakeBiteModifiers();
                if (bite != null && player.modifiers != null)
                {
                    try { player.modifiers.Add(bite, 1f, 1f); }
                    catch (Exception e) { PrintWarning($"Snake bite effect failed: {e.Message}"); }
                }
                Reply(player, "DrinkBadBatch");
                try { if (player.IsGod()) ReplyRaw(player, "DrinkGodMode"); } catch { }
                return;
            }

            if (drink.Health > 0f) player.Heal(drink.Health);
            if (drink.HealOverTime > 0f) player.metabolism.pending_health.Add(drink.HealOverTime);
            if (drink.Calories > 0f) player.metabolism.calories.Add(drink.Calories);
            if (drink.Hydration > 0f) player.metabolism.hydration.Add(drink.Hydration);
            try { Effect.server.Run(DrinkEffect, player.transform.position); } catch { }
            Reply(player, "Drank", drink.Def.displayName.english);

            // God mode silently blocks all negative-source modifiers (drunk blur,
            // snakebite) — warn testers so it doesn't read as a broken feature.
            try { if (player.IsGod()) ReplyRaw(player, "DrinkGodMode"); } catch { }

            // The buzz: the incapacitate-dart vision blur, briefly.
            if (drink.DrunkSeconds > 0f && player.modifiers != null)
            {
                try
                {
                    player.modifiers.Add(new List<ModifierDefintion>
                    {
                        new ModifierDefintion
                        {
                            type = Modifier.ModifierType.ObscureVision,
                            source = Modifier.ModifierSource.NegativeEffect,
                            value = Mathf.Clamp01(config.DrunkIntensity),
                            duration = drink.DrunkSeconds
                        }
                    }, 1f, 1f);
                }
                catch (Exception e) { PrintWarning($"Drunk effect failed: {e.Message}"); }
            }
        }

        // The inventory panel can't grow a Drink button (client-side item data), so
        // advertise the command in the item's display name the moment a drinkable
        // lands in someone's inventory.
        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            if (item == null || !string.IsNullOrEmpty(item.name)) return;
            if (container == null || container.playerOwner == null) return;
            foreach (var drink in GetDrinkables())
            {
                if (drink.Def.itemid != item.info.itemid) continue;
                item.name = $"{drink.Def.displayName.english} (/drink)";
                item.MarkDirty();
                return;
            }
        }

        // First time a player buys a drinkable from a PW grocer, tell them about /drink.
        private void OnBuyVendingItem(VendingMachine machine, BasePlayer player, int sellOrderId, int amount)
        {
            var grocer = machine as NPCVendingMachine;
            if (grocer == null || player == null ||
                (!grocers.Contains(grocer) && !stationGrocers.Contains(grocer))) return;
            if (drinkHintShown.Contains(player.userID)) return;
            try
            {
                var order = machine.sellOrders.sellOrders[sellOrderId];
                foreach (var drink in GetDrinkables())
                {
                    if (drink.Def.itemid != order.itemToSellID) continue;
                    drinkHintShown.Add(player.userID);
                    Reply(player, "DrinkHint");
                    SendReply(player, DescribeDrink(drink));
                    return;
                }
            }
            catch { }
        }

        private void ForEachMonumentSpot(string spot, string[] keywords, Action<Vector3, Quaternion> spawn)
        {
            Vector3 local;
            float rotationY;
            if (!TryParsePumpSpot(spot, out local, out rotationY)) return;
            if (TerrainMeta.Path == null || TerrainMeta.Path.Monuments == null) return;

            foreach (var monument in TerrainMeta.Path.Monuments)
            {
                if (!MonumentMatches(monument, keywords)) continue;
                spawn(monument.transform.TransformPoint(local),
                    Quaternion.Euler(0f, monument.transform.eulerAngles.y + rotationY, 0f));
            }
        }

        #endregion

        private static bool TryParsePumpSpot(string spot, out Vector3 local, out float rotationY)
        {
            local = Vector3.zero;
            rotationY = 0f;
            if (string.IsNullOrEmpty(spot)) return false;
            var parts = spot.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3) return false;
            float x, y, z;
            if (!float.TryParse(parts[0], out x) || !float.TryParse(parts[1], out y) || !float.TryParse(parts[2], out z))
                return false;
            if (parts.Length >= 4) float.TryParse(parts[3], out rotationY);
            local = new Vector3(x, y, z);
            return true;
        }

        // Admins can retune the office radio through the vanilla boombox UI (noclip
        // up to it and press USE); the pick persists to config so the keep-alive and
        // wipe respawns stay on the new station. Everyone else is locked out. The
        // game validates stations against the allowed URL lists before these fire.
        private object OnBoomboxStationUpdate(BoomBox box, string newUrl, BasePlayer player)
        {
            if (box == null || boombox == null || boombox.IsDestroyed || box != boombox.BoxController) return null;
            if (IsAdmin(player)) return null;
            if (player != null) Reply(player, "BoomboxLocked");
            return false;
        }

        private void OnBoomboxStationUpdated(BoomBox box, string newUrl, BasePlayer player)
        {
            if (box == null || boombox == null || boombox.IsDestroyed || box != boombox.BoxController) return;
            if (string.IsNullOrEmpty(newUrl)) return;
            // Save the readable station name when the picked URL maps to one, so the
            // config stays in the same name-or-URL format admins type by hand.
            string saved = StationNameForUrl(newUrl) ?? newUrl;
            if (saved == config.BoomboxStation) return;
            config.BoomboxStation = saved;
            SaveConfig();
            // The vanilla station-change RPC stops playback; restart on the new tune.
            timer.Once(0.5f, () =>
            {
                if (boombox == null || boombox.IsDestroyed) return;
                try { boombox.BoxController.ServerTogglePlay(true); } catch { }
            });
            if (player != null) ReplyRaw(player, "BoomboxRetuned", saved);
        }

        private void KillBoombox()
        {
            if (boombox != null && !boombox.IsDestroyed) boombox.Kill();
            boombox = null;
        }

        private void KillOffice()
        {
            faceTimer?.Destroy();
            faceTimer = null;
            if (clerk != null && !clerk.IsDestroyed) clerk.Kill();
            clerk = null;
            if (marker != null && !marker.IsDestroyed) marker.Kill();
            marker = null;
        }

        private void PasteBuilding(BasePlayer admin)
        {
            if (string.IsNullOrEmpty(config.PasteFile)) return;
            if (CopyPaste == null || !CopyPaste.IsLoaded)
            {
                ReplyRaw(admin, "CopyPasteMissing");
                return;
            }

            RemoveBuilding();

            Vector3 position = admin.transform.position;
            float rotationRad = admin.eyes.rotation.eulerAngles.y * Mathf.Deg2Rad;
            config.PastePosition = $"{position.x:F1} {position.y:F1} {position.z:F1}";
            config.PasteRotationY = admin.eyes.rotation.eulerAngles.y;
            SaveConfig();

            var result = CopyPaste.Call("TryPasteFromVector3", position, rotationRad, config.PasteFile,
                new[] { "stability", "false", "deployables", "true", "autoheight", "false" });
            if (result is string)
                ReplyRaw(admin, "PasteFailed", result);
            else
                ReplyRaw(admin, "Pasting", config.PasteFile);
        }

        private void OnPasteFinished(List<BaseEntity> pastedEntities, string filename, Oxide.Core.Libraries.Covalence.IPlayer player, Vector3 startPos)
        {
            if (pastedEntities == null || string.IsNullOrEmpty(config.PasteFile)) return;
            if (!string.Equals(filename, config.PasteFile, StringComparison.OrdinalIgnoreCase)) return;

            foreach (var entity in pastedEntities)
                if (entity != null && entity.net != null)
                    data.PastedEntityIds.Add(entity.net.ID.Value);
            SaveData();
            Puts($"Office building pasted: tracking {data.PastedEntityIds.Count} entities.");
        }

        private void RemoveBuilding()
        {
            int killed = 0;
            foreach (var id in data.PastedEntityIds)
            {
                var entity = BaseNetworkable.serverEntities.Find(new NetworkableId(id)) as BaseEntity;
                if (entity != null && !entity.IsDestroyed)
                {
                    entity.Kill();
                    killed++;
                }
            }
            if (killed > 0) Puts($"Removed {killed} office building entities.");
            data.PastedEntityIds.Clear();
            config.PastePosition = "";
            SaveConfig();
            SaveData();
        }

        private static bool TryParsePosition(string text, out Vector3 position)
        {
            position = Vector3.zero;
            if (string.IsNullOrEmpty(text)) return false;
            var parts = text.Split(' ');
            if (parts.Length != 3) return false;
            float x, y, z;
            if (!float.TryParse(parts[0], out x) || !float.TryParse(parts[1], out y) || !float.TryParse(parts[2], out z))
                return false;
            position = new Vector3(x, y, z);
            return true;
        }

        #endregion

        #region Protection (reactive patrol heli & Bradley)

        // While paid, the patrol helicopter and Bradley APC only engage players Cobalt
        // considers hostile, and Bradley never drops its scientist crew. Everything is
        // hooks-only and reads live state, so the moment the service lapses (expiry,
        // major fault, unload) the vehicles are pure vanilla again.
        //
        // Fault ladder: minor = "billing hold" on air cover (heli vanilla, Bradley
        // still reactive, still no scientists); major = account frozen (both vanilla).
        private bool HeliProtected() => IsActive(ProtectionKey) && FaultLevel(ProtectionKey) >= 1f;
        private bool BradleyProtected() => IsActive(ProtectionKey) && FaultLevel(ProtectionKey) > 0f;

        // A "player" for Protection purposes: any non-NPC BasePlayer — humans and
        // player.prefab bots alike (FakeFriends' native bots carry sub-Steam ids, so a
        // Steam-range check would strip their cover). Scientists/dwellers are IsNpc.
        private bool IsRealPlayer(BasePlayer player) =>
            player != null && !player.IsNpc &&
            (config.ProtectionCoversBots || (ulong)player.userID >= 76561197960265728UL);

        private bool IsHostile(ulong userId)
        {
            double until;
            if (!hostileUntil.TryGetValue(userId, out until)) return false;
            if (until > Now) return true;
            hostileUntil.Remove(userId);
            return false;
        }

        private void PruneHostility()
        {
            if (hostileUntil.Count == 0) return;
            foreach (var id in hostileUntil.Keys.ToList())
                if (hostileUntil[id] <= Now) hostileUntil.Remove(id);
        }

        private void ClearHostility()
        {
            hostileUntil.Clear();
            hostileNoticeAt.Clear();
        }

        // Any live Cobalt asset within the protection radius of a position?
        private bool NearCobaltAsset(Vector3 position)
        {
            float radius = Mathf.Max(0f, config.ProtectionRadius);
            float sqr = radius * radius;
            for (int i = patrolHelis.Count - 1; i >= 0; i--)
            {
                var heli = patrolHelis[i];
                if (heli == null || heli.IsDestroyed) { patrolHelis.RemoveAt(i); continue; }
                if ((heli.transform.position - position).sqrMagnitude <= sqr) return true;
            }
            var live = PatrolHelicopterAI.heliInstance;
            if (live != null && live.helicopterBase != null && !live.helicopterBase.IsDestroyed &&
                (live.helicopterBase.transform.position - position).sqrMagnitude <= sqr) return true;
            for (int i = bradleys.Count - 1; i >= 0; i--)
            {
                var apc = bradleys[i];
                if (apc == null || apc.IsDestroyed) { bradleys.RemoveAt(i); continue; }
                if ((apc.transform.position - position).sqrMagnitude <= sqr) return true;
            }
            return false;
        }

        // Called from OnEntityTakeDamage: a real player becomes hostile by shooting the
        // heli/Bradley, or by hitting another real player near one.
        private void TrackHostility(BaseCombatEntity entity, HitInfo info)
        {
            if (info == null || !IsActive(ProtectionKey)) return;
            var attacker = info.InitiatorPlayer;
            if (!IsRealPlayer(attacker)) return;
            if (info.damageTypes == null || info.damageTypes.Total() <= 0f) return;

            if (entity is PatrolHelicopter) { MarkHostile(attacker, "Hostile.heli"); return; }
            if (entity is BradleyAPC) { MarkHostile(attacker, "Hostile.bradley"); return; }

            var victim = entity as BasePlayer;
            if (!IsRealPlayer(victim) || victim == attacker) return;
            if (!config.ProtectionFriendlyFireCounts &&
                attacker.currentTeam != 0UL && attacker.currentTeam == victim.currentTeam) return;
            if (!NearCobaltAsset(attacker.transform.position)) return;
            MarkHostile(attacker, "Hostile.player", victim.displayName);
        }

        private void MarkHostile(BasePlayer player, string reasonKey, string reasonArg = null)
        {
            double until = Now + Mathf.Max(1f, config.ProtectionHostileSeconds);
            MarkHostileOne((ulong)player.userID, player, until, reasonKey, reasonArg, null);

            // Consequences for everyone: the whole team shares the flag.
            if (!config.ProtectionShareWithTeam || player.Team == null || player.Team.members == null) return;
            foreach (var memberId in player.Team.members.ToList())
            {
                if (memberId == (ulong)player.userID) continue;
                MarkHostileOne(memberId, BasePlayer.FindByID(memberId), until, reasonKey, reasonArg, player.displayName);
            }
        }

        private void MarkHostileOne(ulong userId, BasePlayer online, double until, string reasonKey, string reasonArg, string teammate)
        {
            bool wasHostile = IsHostile(userId);
            hostileUntil[userId] = until;
            if (online == null || !online.IsConnected) return;

            // Tell them once per flag (refreshes are silent), plus a reminder if they've
            // stayed hostile for a whole window.
            double lastNotice;
            bool remind = hostileNoticeAt.TryGetValue(userId, out lastNotice) &&
                          Now - lastNotice >= Mathf.Max(1f, config.ProtectionHostileSeconds);
            if (wasHostile && !remind) return;
            hostileNoticeAt[userId] = Now;

            string uid = online.UserIDString;
            string reason = Msg(reasonKey, uid, reasonArg);
            if (teammate != null) reason = Msg("HostileTeammate", uid, teammate, reason);
            Reply(online, "HostileFlagged", FormatSeconds(until - Now), reason);
        }

        private string FormatSeconds(double seconds) =>
            seconds >= 60 ? FormatSpan(seconds) : $"{Math.Max(1, (int)Math.Ceiling(seconds))}s";

        // Death settles the account: without this the heli that just killed you is
        // waiting over the respawn point. (Teammates keep their own flags.)
        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            if (player == null || !config.ProtectionClearOnDeath || hostileUntil.Count == 0) return;
            ulong id = (ulong)player.userID;
            hostileUntil.Remove(id);
            hostileNoticeAt.Remove(id);
        }

        // Settle a billing hold at the office: scrap fee, no payout, no bonus hours.
        private void TryPayBill(BasePlayer player, FaultData fault)
        {
            int fee = BillingFee(fault);
            var def = ItemManager.FindItemDefinition(ScrapShortname);
            if (def == null) return;
            if (fee > 0)
            {
                if (player.inventory.GetAmount(def.itemid) < fee)
                {
                    Reply(player, "BillNeedScrap", fee);
                    return;
                }
                player.inventory.Take(null, def.itemid, fee);
            }
            ResolveFault(fault, player);
        }

        // --- Patrol helicopter ---

        // Gates both new-target acquisition and the per-second "still visible" refresh
        // of existing targets. Hostile players fall through to vanilla (LOS + night
        // rule) — returning true here would bypass line of sight.
        private object CanHelicopterTarget(PatrolHelicopterAI ai, BasePlayer player)
        {
            if (!IsRealPlayer(player) || !HeliProtected()) return null;
            return IsHostile((ulong)player.userID) ? null : (object)false;
        }

        // Belt and braces: refuse the gun lock on a non-hostile player.
        private object OnHelicopterTarget(HelicopterTurret turret, BaseCombatEntity target)
        {
            var player = target as BasePlayer;
            if (!IsRealPlayer(player) || !HeliProtected()) return null;
            return IsHostile((ulong)player.userID) ? null : (object)true;
        }

        // Cancel strafe / napalm runs on non-hostile players. (Not CanHelicopterStrafeTarget:
        // a false there only makes the heli switch from rockets to napalm.)
        private object OnHelicopterStrafeEnter(PatrolHelicopterAI ai, Vector3 pos, BasePlayer target)
        {
            if (!IsRealPlayer(target) || !HeliProtected()) return null;
            return IsHostile((ulong)target.userID) ? null : (object)true;
        }

        // --- Bradley APC ---

        // Runs at the end of VisibilityTest on every target scan, so non-hostile
        // players drop off the list within memoryDuration. Hostile -> vanilla LOS.
        private object CanBradleyApcTarget(BradleyAPC apc, BaseEntity ent)
        {
            var player = ent as BasePlayer;
            if (!IsRealPlayer(player) || !BradleyProtected()) return null;
            return IsHostile((ulong)player.userID) ? null : (object)false;
        }

        // No ground crew while protected (a heli-lapse minor fault doesn't re-enable it).
        private object CanDeployScientists(BradleyAPC apc, BaseEntity attacker, List<GameObjectRef> prefabs, List<Vector3> positions)
        {
            return BradleyProtected() ? (object)false : null;
        }

        #endregion

        #region Purchases

        private int GetScrap(BasePlayer player)
        {
            var def = ItemManager.FindItemDefinition(ScrapShortname);
            return def == null ? 0 : player.inventory.GetAmount(def.itemid);
        }

        private bool TryPurchase(BasePlayer player, string serviceKey)
        {
            if (!IsService(serviceKey)) return false;
            if (IsDisabled(serviceKey)) { Reply(player, "ServiceNotEssential"); return false; }

            double banked = GetBanked(serviceKey);
            double deposit = config.HoursPerPayment * 3600.0;
            double cap = config.MaxPrepaidDays * 24.0 * 3600.0;
            if (banked + deposit > cap + 1)
            {
                Reply(player, "PaidMax", LabelFor(serviceKey, player.UserIDString), config.MaxPrepaidDays);
                return false;
            }

            var def = ItemManager.FindItemDefinition(ScrapShortname);
            if (def == null) return false;
            if (player.inventory.GetAmount(def.itemid) < config.PricePerDay)
            {
                Reply(player, "NeedScrap", config.PricePerDay, LabelFor(serviceKey, player.UserIDString));
                return false;
            }

            player.inventory.Take(null, def.itemid, config.PricePerDay);
            data.Banked[serviceKey] = banked + deposit;
            warnLevel[serviceKey] = 0;
            SaveData();

            bool firstActivation = !lastActive.Contains(serviceKey);
            if (firstActivation)
            {
                lastActive.Add(serviceKey);
                ApplyService(serviceKey);
                AnnounceAll("ServiceActivePaid", LabelFor(serviceKey, null), DescFor(serviceKey, null), player.displayName);
            }
            else
            {
                Reply(player, "ToppedUp", LabelFor(serviceKey, player.UserIDString), FormatSpan(GetBanked(serviceKey)));
            }
            return true;
        }

        private string FormatSpan(double seconds)
        {
            if (seconds <= 0) return "empty";
            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
            return $"{Math.Max(1, span.Minutes)}m";
        }

        #endregion

        #region Commands

        private bool IsAdmin(BasePlayer player) =>
            player == null || player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin);

        private bool AtOffice(BasePlayer player) =>
            clerk != null && Vector3.Distance(player.transform.position, clerk.transform.position) <= config.InteractRange * 2f;

        [ChatCommand("pw")]
        private void CmdChat(BasePlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                if (AtOffice(player) || IsAdmin(player))
                {
                    ShowPanel(player);
                    return;
                }

                if (clerk != null)
                    Reply(player, "OfficeAt", MapHelper.PositionToString(clerk.transform.position));
                else
                    Reply(player, "OfficeMissing");
                return;
            }

            if (!IsAdmin(player))
            {
                ReplyRaw(player, "NoPermission");
                return;
            }

            switch (args[0].ToLower())
            {
                case "setoffice":
                {
                    CaptureOfficeSpot(player);
                    PasteBuilding(player);
                    SpawnOffice();
                    if (string.IsNullOrEmpty(config.OfficeMonument))
                        ReplyRaw(player, "SetOffice");
                    else
                        ReplyRaw(player, "SetOfficeAnchored", GetMonumentName(player.transform.position) ?? "the monument");
                    break;
                }

                case "setclerk":
                    CaptureOfficeSpot(player);
                    SpawnOffice();
                    ReplyRaw(player, "ClerkMoved");
                    break;

                case "setboombox":
                {
                    string monumentName, position;
                    float rotationY;
                    CaptureAnchoredSpot(player, out monumentName, out position, out rotationY);
                    config.BoomboxMonument = monumentName;
                    config.BoomboxPosition = position;
                    config.BoomboxRotationY = rotationY;
                    if (string.IsNullOrEmpty(config.BoomboxStation))
                        config.BoomboxStation = "rustradio.facepunch.com";
                    SaveConfig();
                    SpawnBoombox();
                    ReplyRaw(player, "BoomboxPlaced", config.BoomboxStation);
                    break;
                }

                case "setpump":
                {
                    var monument = FindMonumentAt(player.transform.position);
                    string stationName = monument != null
                        ? (monument.displayPhrase != null ? monument.displayPhrase.english : monument.name)
                        : null;
                    if (monument == null || string.IsNullOrEmpty(stationName) ||
                        (!stationName.ToLower().Contains("oxum") && !stationName.ToLower().Contains("gas station")))
                    {
                        ReplyRaw(player, "PumpNotAtStation");
                        return;
                    }

                    Vector3 local = monument.transform.InverseTransformPoint(player.transform.position);
                    float rotationY = player.eyes.rotation.eulerAngles.y + 180f - monument.transform.eulerAngles.y;
                    config.GaragePumpSpots.Add($"{local.x:F2} {local.y:F2} {local.z:F2} {rotationY:F1}");
                    config.GaragePumpsEnabled = true;
                    SaveConfig();
                    SpawnGaragePumps();
                    Tick();
                    ReplyRaw(player, "PumpAdded", config.GaragePumpSpots.Count, garagePumps.Count);
                    break;
                }

                case "clearpumps":
                    config.GaragePumpSpots.Clear();
                    SaveConfig();
                    KillGaragePumps();
                    ReplyRaw(player, "PumpsCleared");
                    break;

                // Grocery machines live at supermarkets AND gas stations — the command
                // stores the spot for whichever monument type the admin stands in.
                case "setgrocer":
                {
                    var grocerMonument = FindMonumentAt(player.transform.position);
                    bool atSupermarket = MonumentMatches(grocerMonument, SupermarketKeywords);
                    bool atStation = MonumentMatches(grocerMonument, GasStationKeywords);
                    if (!atSupermarket && !atStation)
                    {
                        ReplyRaw(player, "GrocerNotHere");
                        return;
                    }

                    Vector3 grocerLocal = grocerMonument.transform.InverseTransformPoint(player.transform.position);
                    float grocerRotY = player.eyes.rotation.eulerAngles.y + 180f - grocerMonument.transform.eulerAngles.y;
                    string grocerSpot = $"{grocerLocal.x:F2} {grocerLocal.y:F2} {grocerLocal.z:F2} {grocerRotY:F1}";
                    if (atSupermarket) config.GrocerSpot = grocerSpot;
                    else config.GarageGrocerSpot = grocerSpot;
                    SaveConfig();
                    KillMarketsExtras();
                    Tick(); // respawns immediately if the Markets service is up
                    ReplyRaw(player, atSupermarket ? "GrocerPlaced" : "GrocerPlacedStation");
                    break;
                }

                case "setmarket":
                {
                    var superMonument = FindMonumentAt(player.transform.position);
                    if (!MonumentMatches(superMonument, SupermarketKeywords))
                    {
                        ReplyRaw(player, "NotAtSupermarket");
                        return;
                    }

                    Vector3 superLocal = superMonument.transform.InverseTransformPoint(player.transform.position);
                    float superRotY = player.eyes.rotation.eulerAngles.y + 180f - superMonument.transform.eulerAngles.y;
                    config.MarketplaceSpot = $"{superLocal.x:F2} {superLocal.y:F2} {superLocal.z:F2} {superRotY:F1}";
                    SaveConfig();
                    KillMarketsExtras();
                    Tick();
                    ReplyRaw(player, "MarketPlaced");
                    break;
                }

                // Clears the grocer for the monument type you stand in; elsewhere, both.
                case "cleargrocer":
                {
                    var clearMonument = FindMonumentAt(player.transform.position);
                    bool clearSuper = MonumentMatches(clearMonument, SupermarketKeywords);
                    bool clearStation = MonumentMatches(clearMonument, GasStationKeywords);
                    if (clearSuper || !clearStation) config.GrocerSpot = "";
                    if (clearStation || !clearSuper) config.GarageGrocerSpot = "";
                    SaveConfig();
                    KillMarketsExtras();
                    Tick();
                    ReplyRaw(player, "GrocerCleared");
                    break;
                }

                case "clearmarket":
                    config.MarketplaceSpot = "";
                    SaveConfig();
                    KillMarketsExtras();
                    Tick(); // respawns whichever extra is still configured
                    ReplyRaw(player, "MarketCleared");
                    break;

                // Removes the configured pump spot nearest to the admin (same station).
                case "removepump":
                {
                    var pumpMonument = FindMonumentAt(player.transform.position);
                    if (!MonumentMatches(pumpMonument, GasStationKeywords))
                    {
                        ReplyRaw(player, "PumpNotAtStation");
                        return;
                    }

                    int bestIndex = -1;
                    float bestDist = 6f; // must be standing close to the offending pump
                    for (int i = 0; i < config.GaragePumpSpots.Count; i++)
                    {
                        Vector3 spotLocal;
                        float spotRot;
                        if (!TryParsePumpSpot(config.GaragePumpSpots[i], out spotLocal, out spotRot)) continue;
                        float dist = Vector3.Distance(
                            pumpMonument.transform.TransformPoint(spotLocal), player.transform.position);
                        if (dist < bestDist) { bestDist = dist; bestIndex = i; }
                    }
                    if (bestIndex < 0)
                    {
                        ReplyRaw(player, "PumpNoneNear", 6);
                        return;
                    }
                    config.GaragePumpSpots.RemoveAt(bestIndex);
                    SaveConfig();
                    SpawnGaragePumps();
                    Tick();
                    ReplyRaw(player, "PumpRemoved", bestIndex + 1, config.GaragePumpSpots.Count);
                    break;
                }

                // Admin diagnostic: identify the entity (or static scenery) being looked at.
                case "whatsthis":
                {
                    RaycastHit hit;
                    if (!Physics.Raycast(player.eyes.HeadRay(), out hit, 10f))
                    {
                        SendReply(player, "Nothing hit within 10m.");
                        return;
                    }
                    var target = hit.GetEntity();
                    if (target == null)
                    {
                        SendReply(player, $"'{hit.collider.name}' is not an entity — static/decorative scenery baked into the monument.");
                        return;
                    }
                    SendReply(player,
                        $"{target.ShortPrefabName} ({target.GetType().Name})\n" +
                        $"prefab: {target.PrefabName}\n" +
                        $"powergrid entity: {target is IPowergridEntity}, monument: {GetMonumentName(target.transform.position) ?? "none"}");
                    break;
                }

                // Admin diagnostic: live production state of the nearest garage pump.
                case "pumpinfo":
                {
                    WaterCatcher nearestPump = null;
                    float bestSqr = float.MaxValue;
                    foreach (var p in garagePumps)
                    {
                        if (p == null || p.IsDestroyed) continue;
                        float sqr = (p.transform.position - player.transform.position).sqrMagnitude;
                        if (sqr < bestSqr) { bestSqr = sqr; nearestPump = p; }
                    }
                    if (nearestPump == null)
                    {
                        SendReply(player, $"No live garage pumps ({config.GaragePumpSpots.Count} spot(s) configured). Place one with /pw setpump.");
                        return;
                    }
                    try
                    {
                        float baseRate = nearestPump.collectionRates != null ? nearestPump.collectionRates.baseRate : -1f;
                        int perCollect = baseRate >= 0f ? Mathf.CeilToInt(nearestPump.maxItemToCreate * baseRate) : -1;
                        var slot = nearestPump.inventory != null ? nearestPump.inventory.GetSlot(0) : null;
                        SendReply(player,
                            $"Pump {Mathf.Sqrt(bestSqr):F0}m away: producing={nearestPump.HasFlag(WaterCatcher.Flag_CanProduceItem)} " +
                            $"interval={nearestPump.overrideCollectInterval:F0}s " +
                            $"perCollect={perCollect} (maxItemToCreate={nearestPump.maxItemToCreate:F2} baseRate={baseRate:F2}) " +
                            $"held={(slot != null ? slot.amount : 0)}/{(nearestPump.inventory != null ? nearestPump.inventory.maxStackSize : 0)} " +
                            $"full={nearestPump.IsFull()}");
                    }
                    catch (Exception e) { SendReply(player, $"pumpinfo failed: {e.Message}"); }
                    break;
                }

                case "pastefile":
                    config.PasteFile = args.Length >= 2 ? args[1] : "";
                    SaveConfig();
                    if (string.IsNullOrEmpty(config.PasteFile))
                        ReplyRaw(player, "PastefileCleared");
                    else
                        ReplyRaw(player, "PastefileSet", config.PasteFile);
                    break;

                case "removeoffice":
                    KillOffice();
                    KillBoombox();
                    RemoveBuilding();
                    config.OfficePosition = "";
                    config.OfficeMonument = "";
                    config.BoomboxPosition = "";
                    config.BoomboxMonument = "";
                    SaveConfig();
                    ReplyRaw(player, "OfficeRemoved");
                    break;

                case "grant":
                {
                    if (args.Length < 2) { ReplyRaw(player, "UsageGrant"); return; }
                    float days;
                    if (args.Length < 3 || !float.TryParse(args[2], out days)) days = 1f;
                    foreach (var key in ServiceKeys)
                    {
                        if (args[1] != "all" && !key.StartsWith(args[1].ToLower())) continue;
                        data.Banked[key] = GetBanked(key) + days * config.HoursPerPayment * 3600.0;
                        warnLevel[key] = 0;
                    }
                    SaveData();
                    Tick();
                    ReplyRaw(player, "Granted", days, args[1]);
                    break;
                }

                case "revoke":
                {
                    if (args.Length < 2) { ReplyRaw(player, "UsageRevoke"); return; }
                    foreach (var key in ServiceKeys)
                    {
                        if (args[1] != "all" && !key.StartsWith(args[1].ToLower())) continue;
                        data.Banked[key] = 0;
                    }
                    SaveData();
                    Tick();
                    ReplyRaw(player, "Revoked", args[1]);
                    break;
                }

                case "fault":
                {
                    if (args.Length < 2) { ReplyRaw(player, "UsageFault"); return; }
                    string serviceKey = null;
                    foreach (var key in ServiceKeys)
                        if (key.StartsWith(args[1].ToLower())) { serviceKey = key; break; }
                    if (serviceKey == null) { ReplyRaw(player, "UnknownService", args[1]); return; }
                    string label = LabelFor(serviceKey, player.UserIDString);
                    if (IsDisabled(serviceKey)) { ReplyRaw(player, "ServiceNotEssential"); return; }
                    if (!IsActive(serviceKey)) { ReplyRaw(player, "FaultNotActive", label); return; }
                    if (GetFault(serviceKey) != null) { ReplyRaw(player, "FaultExists", label); return; }
                    string severity = args.Length >= 3 && args[2].ToLower().StartsWith("maj") ? "major" : "minor";
                    if (StartFault(serviceKey, severity))
                        ReplyRaw(player, "FaultStarted", severity, label);
                    else
                        ReplyRaw(player, "FaultNoSite", label);
                    break;
                }

                case "perf":
                {
                    double avg = tickCount > 0 ? tickTotalMs / tickCount : 0;
                    SendReply(player,
                        $"PublicWorks tick: last {tickLastMs:F1}ms, avg {avg:F1}ms, max {tickMaxMs:F1}ms over {tickCount} ticks " +
                        $"(every {Mathf.Max(10f, config.TickSeconds):F0}s). Last breakdown: {TickBreakdown()}\n" +
                        $"Tracked: {serviceEntities.Sum(s => s.Value.Count)} powergrid entities, {trains.Count} trains, " +
                        $"{garagePumps.Count} garage pumps, {domePumps.Count} dome pumps, {patrolHelis.Count} heli, {bradleys.Count} bradley, {hostileUntil.Count} hostile.");
                    return;
                }

                case "hostile":
                {
                    if (args.Length >= 2 && args[1].ToLower() == "clear")
                    {
                        int count = hostileUntil.Count;
                        ClearHostility();
                        ReplyRaw(player, "HostileCleared", count);
                        return;
                    }
                    if (args.Length >= 2) { ReplyRaw(player, "UsageHostile"); return; }
                    PruneHostility();
                    if (hostileUntil.Count == 0) { ReplyRaw(player, "HostileNone"); return; }
                    var lines = new List<string> { Msg("HostileHeader", player.UserIDString, hostileUntil.Count) };
                    foreach (var pair in hostileUntil.OrderByDescending(p => p.Value))
                    {
                        var hostile = BasePlayer.FindByID(pair.Key) ?? BasePlayer.FindSleeping(pair.Key);
                        string name = hostile != null ? hostile.displayName : covalence.Players.FindPlayerById(pair.Key.ToString())?.Name;
                        name = string.IsNullOrEmpty(name) ? pair.Key.ToString() : $"{name} ({pair.Key})";
                        lines.Add(Msg("HostileEntry", player.UserIDString, name, (int)Math.Ceiling(pair.Value - Now)));
                    }
                    SendReply(player, string.Join("\n", lines));
                    return;
                }

                case "clearfault":
                {
                    if (args.Length < 2) { ReplyRaw(player, "UsageClearfault"); return; }
                    int cleared = 0;
                    for (int i = data.Faults.Count - 1; i >= 0; i--)
                    {
                        var fault = data.Faults[i];
                        if (args[1] != "all" && !fault.Service.StartsWith(args[1].ToLower())) continue;
                        ResolveFault(fault, null);
                        cleared++;
                    }
                    if (cleared > 0)
                        ReplyRaw(player, "FaultsCleared", cleared);
                    else
                        ReplyRaw(player, "NoFaultsMatch");
                    break;
                }

                default:
                    ReplyRaw(player, "Usage");
                    break;
            }
        }

        // Read-only status view, usable anywhere: shows every service and any active
        // fault, but paying and taking contracts always require standing at the office
        // (admins included — /pw is the interactive-anywhere panel).
        [ChatCommand("pwinfo")]
        private void CmdInfo(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            ShowPanel(player, AtOffice(player));
        }

        [ConsoleCommand("pw.cmd")]
        private void CmdConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (!arg.HasArgs()) return;

            switch (arg.GetString(0).ToLower())
            {
                case "close":
                    if (player != null) CuiHelper.DestroyUi(player, UiPanelName);
                    return;

                case "buy":
                {
                    if (player == null) return;
                    // Must be at the office (admins exempt) — the CUI only opens there, but re-check server-side.
                    if (!IsAdmin(player) && !AtOffice(player)) return;
                    TryPurchase(player, arg.GetString(1));
                    ShowPanel(player);
                    return;
                }

                case "job":
                {
                    if (player == null) return;
                    if (!IsAdmin(player) && !AtOffice(player)) return;
                    var fault = GetFault(arg.GetString(1));
                    if (fault == null) { ShowPanel(player); return; } // resolved between render and click
                    if (fault.Service == ProtectionKey) { TryPayBill(player, fault); ShowPanel(player); return; }
                    if (!fault.Accepted.Contains((ulong)player.userID))
                    {
                        fault.Accepted.Add((ulong)player.userID);
                        SaveData();
                    }
                    Reply(player, "ContractAccepted",
                        Msg("Flavor." + fault.Service, player.UserIDString),
                        fault.Monument,
                        MapHelper.PositionToString(fault.Site),
                        MaterialsText(RepairMaterialsFor(fault), player.UserIDString),
                        RepairReward(fault),
                        FormatSpan(fault.Deadline - Now));
                    ShowPanel(player);
                    return;
                }

                case "bill":
                {
                    if (player == null) return;
                    if (!IsAdmin(player) && !AtOffice(player)) return;
                    var fault = GetFault(ProtectionKey);
                    if (fault == null) Reply(player, "BillNoFault");
                    else TryPayBill(player, fault);
                    ShowPanel(player);
                    return;
                }
            }
        }

        #endregion

        #region CUI

        private const string ColBg = "0.09 0.09 0.10 0.97";
        private const string ColRow = "0.15 0.15 0.16 1";
        private const string ColBtn = "0.25 0.25 0.25 1";
        private const string ColActive = "0.30 0.55 0.20 1";
        private const string ColInactive = "0.45 0.18 0.18 1";
        private const string ColFault = "0.85 0.45 0.10 1";
        private const string ColText = "0.88 0.88 0.88 1";
        private const string ColDim = "0.6 0.6 0.6 1";
        private const string ColTitle = "0.98 0.65 0.15 1";

        private string OfficeHint(string userId)
        {
            if (clerk == null) return Msg("OfficeHintNone", userId);
            string monument = GetMonumentName(clerk.transform.position);
            string grid = MapHelper.PositionToString(clerk.transform.position);
            return monument != null
                ? Msg("OfficeHintMonument", userId, monument, grid)
                : Msg("OfficeHintGrid", userId, grid);
        }

        private void ShowPanel(BasePlayer player, bool interactive = true)
        {
            CuiHelper.DestroyUi(player, UiPanelName);
            var ui = new CuiElementContainer();

            var panel = ui.Add(new CuiPanel
            {
                Image = { Color = ColBg },
                RectTransform = { AnchorMin = "0.30 0.15", AnchorMax = "0.70 0.88" },
                CursorEnabled = true
            }, "Overlay", UiPanelName);

            string uid = player.UserIDString;
            ui.Add(new CuiLabel
            {
                Text = { Text = Msg("UiTitle", uid), FontSize = 18, Align = TextAnchor.MiddleCenter, Color = ColTitle },
                RectTransform = { AnchorMin = "0 0.93", AnchorMax = "1 0.995" }
            }, panel);
            ui.Add(new CuiLabel
            {
                Text = { Text = Msg("UiSubtitle", uid, config.PricePerDay), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColDim },
                RectTransform = { AnchorMin = "0 0.895", AnchorMax = "1 0.935" }
            }, panel);
            ui.Add(new CuiButton
            {
                Button = { Color = ColInactive, Command = "pw.cmd close" },
                Text = { Text = "✕", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = ColText },
                RectTransform = { AnchorMin = "0.93 0.945", AnchorMax = "0.985 0.99" }
            }, panel);

            // Nine rows between the subtitle (0.885) and the footer (0.105): 9 x 0.085
            // bottoms out at 0.12, the same clearance eight rows had at 0.095.
            float top = 0.885f;
            float rowH = 0.085f;
            float burnNow = BurnRate();
            for (int i = 0; i < ServiceKeys.Length; i++)
            {
                var key = ServiceKeys[i];
                bool disabled = IsDisabled(key);
                bool active = IsActive(key);
                var fault = disabled ? null : GetFault(key);
                float y1 = top - (i + 1) * rowH + 0.008f;
                float y2 = top - i * rowH;

                ui.Add(new CuiPanel
                {
                    Image = { Color = ColRow },
                    RectTransform = { AnchorMin = $"0.03 {y1}", AnchorMax = $"0.97 {y2}" }
                }, panel);

                // status dot (orange while faulted, gray while shelved by Cobalt)
                ui.Add(new CuiPanel
                {
                    Image = { Color = disabled ? ColBtn : (fault != null ? ColFault : (active ? ColActive : ColInactive)) },
                    RectTransform = { AnchorMin = $"0.045 {y1 + 0.028f}", AnchorMax = $"0.062 {y2 - 0.028f}" }
                }, panel);

                ui.Add(new CuiLabel
                {
                    Text = { Text = LabelFor(key, uid), FontSize = 13, Align = TextAnchor.MiddleLeft, Color = ColText },
                    RectTransform = { AnchorMin = $"0.08 {y1 + (y2 - y1) * 0.45f}", AnchorMax = $"0.62 {y2}" }
                }, panel);
                ui.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = disabled ? Msg("ServiceNotEssential", uid) : DescFor(key, uid),
                        FontSize = 9, Align = TextAnchor.UpperLeft,
                        Color = disabled ? "0.72 0.55 0.32 1" : ColDim
                    },
                    RectTransform = { AnchorMin = $"0.08 {y1}", AnchorMax = $"0.62 {y1 + (y2 - y1) * 0.48f}" }
                }, panel);

                // Bucket gauge: fill = banked vs the prepay cap, with time-left-at-current-usage under it.
                double banked = GetBanked(key);
                double cap = config.MaxPrepaidDays * 24.0 * 3600.0;
                float frac = (float)Math.Min(1.0, banked / cap);
                double eta = banked / Math.Max(0.01f, burnNow);

                if (!disabled)
                    ui.Add(new CuiPanel
                    {
                        Image = { Color = "0.09 0.09 0.10 1" },
                        RectTransform = { AnchorMin = $"0.625 {y2 - 0.032f}", AnchorMax = $"0.785 {y2 - 0.014f}" }
                    }, panel);
                if (active && frac > 0.005f)
                {
                    string barColor = eta < 3600 ? "0.95 0.55 0.20 1" : "0.45 0.70 0.28 1";
                    ui.Add(new CuiPanel
                    {
                        Image = { Color = barColor },
                        RectTransform = { AnchorMin = $"0.625 {y2 - 0.032f}", AnchorMax = $"{0.625f + frac * 0.16f} {y2 - 0.014f}" }
                    }, panel);
                }
                ui.Add(new CuiLabel
                {
                    Text =
                    {
                        Text = disabled
                            ? Msg("UiNotEssential", uid)
                            : fault != null
                                ? Msg(key == ProtectionKey ? "UiFaultHold" : "UiFaultCrew", uid, FormatSpan(fault.Deadline - Now))
                                : (active ? Msg("UiTimeLeft", uid, FormatSpan(eta)) : Msg("UiInactive", uid)),
                        FontSize = 9, Align = disabled ? TextAnchor.MiddleCenter : TextAnchor.UpperCenter,
                        Color = disabled ? ColDim : fault != null ? "0.95 0.55 0.20 1" : (active ? "0.55 0.83 0.35 1" : "0.85 0.45 0.45 1")
                    },
                    RectTransform = { AnchorMin = $"0.625 {y1}", AnchorMax = $"0.785 {(disabled ? y2 : y2 - 0.036f)}" }
                }, panel);

                if (disabled)
                {
                    // Cobalt isn't selling this one: no gauge, no price, no button.
                }
                else if (!interactive)
                {
                    // Status view from afar: the button slot shows where the fault is instead.
                    ui.Add(new CuiLabel
                    {
                        Text =
                        {
                            Text = fault != null
                                ? Msg("UiFaultAt", uid, MapHelper.PositionToString(fault.Site))
                                : Msg("UiPerDay", uid, config.PricePerDay),
                            FontSize = 10, Align = TextAnchor.MiddleCenter,
                            Color = fault != null ? "0.95 0.55 0.20 1" : ColDim
                        },
                        RectTransform = { AnchorMin = $"0.80 {y1 + 0.012f}", AnchorMax = $"0.955 {y2 - 0.012f}" }
                    }, panel);
                }
                else if (fault != null && key == ProtectionKey)
                {
                    // Billing hold: settle it right here, no contract / site trip.
                    ui.Add(new CuiButton
                    {
                        Button = { Color = ColFault, Command = "pw.cmd bill" },
                        Text = { Text = Msg("UiPayBill", uid, BillingFee(fault)), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColText },
                        RectTransform = { AnchorMin = $"0.80 {y1 + 0.012f}", AnchorMax = $"0.955 {y2 - 0.012f}" }
                    }, panel);
                }
                else if (fault != null)
                {
                    bool onJob = fault.Accepted.Contains((ulong)player.userID);
                    ui.Add(new CuiButton
                    {
                        Button = { Color = onJob ? ColBtn : ColFault, Command = $"pw.cmd job {key}" },
                        Text = { Text = Msg(onJob ? "UiOnJob" : "UiTakeJob", uid), FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColText },
                        RectTransform = { AnchorMin = $"0.80 {y1 + 0.012f}", AnchorMax = $"0.955 {y2 - 0.012f}" }
                    }, panel);
                }
                else
                {
                    ui.Add(new CuiButton
                    {
                        Button = { Color = active ? ColBtn : ColActive, Command = $"pw.cmd buy {key}" },
                        Text = { Text = Msg(active ? "UiTopUp" : "UiPay", uid), FontSize = 11, Align = TextAnchor.MiddleCenter, Color = ColText },
                        RectTransform = { AnchorMin = $"0.80 {y1 + 0.012f}", AnchorMax = $"0.955 {y2 - 0.012f}" }
                    }, panel);
                }
            }

            ui.Add(new CuiLabel
            {
                Text =
                {
                    Text = Msg("UiFooter", uid, burnNow.ToString("F2"), HumanCount(), config.MaxPrepaidDays),
                    FontSize = 10, Align = TextAnchor.MiddleCenter, Color = ColDim
                },
                RectTransform = { AnchorMin = "0 0.065", AnchorMax = "1 0.105" }
            }, panel);
            ui.Add(new CuiLabel
            {
                Text =
                {
                    Text = interactive ? Msg("UiScrap", uid, GetScrap(player)) : OfficeHint(uid),
                    FontSize = 12, Align = TextAnchor.MiddleCenter, Color = ColTitle
                },
                RectTransform = { AnchorMin = "0 0.015", AnchorMax = "1 0.06" }
            }, panel);

            CuiHelper.AddUi(player, ui);
        }

        #endregion
    }
}
