using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class FCSettings : ModSettings
    {

        /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-* 
         *           ~  DEFAULTS  ~
         * for saving, reseting, and validation
         * Centralized for ease of editing, and to ensure that all references to these values
         *   are synced.
         *-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
        /* Defaults by difficulty setting */
        public const int MINIMUM_TAX_INTERVAL_DAYS = 1;
        public const EmpireDifficultyLevel DEFAULT_DIFFICULTY_LEVEL = EmpireDifficultyLevel.AdventureStory;
        //Peaceful
        public const int DEFAULT_SILVER_PER_RESOURCE_PEACEFUL = 200;
        public const int DEFAULT_TAX_INTERVAL_DAYS_PEACEFUL = 2;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_PEACEFUL = 50;
        public const int DEFAULT_WORKER_COST_PEACEFUL = 75;
        //Community Builder
        public const int DEFAULT_SILVER_PER_RESOURCE_COMMUNITYBUILDER = 150;
        public const int DEFAULT_TAX_INTERVAL_DAYS_COMMUNITYBUILDER = 5;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_COMMUNITYBUILDER = 25;
        public const int DEFAULT_WORKER_COST_COMMUNITYBUILDER = 100;
        //Adventure Story
        public const int DEFAULT_SILVER_PER_RESOURCE_ADVENTURESTORY = 100;
        public const int DEFAULT_TAX_INTERVAL_DAYS_ADVENTURESTORY = 5;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_ADVENTURESTORY = 25;
        public const int DEFAULT_WORKER_COST_ADVENTURESTORY = 100;
        //Strive to Survive
        public const int DEFAULT_SILVER_PER_RESOURCE_STRIVETOSURVIVE = 100;
        public const int DEFAULT_TAX_INTERVAL_DAYS_STRIVETOSURVIVE = 10;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_STRIVETOSURVIVE = 20;
        public const int DEFAULT_WORKER_COST_STRIVETOSURVIVE = 125;
        //Blood and Dust
        public const int DEFAULT_SILVER_PER_RESOURCE_BLOODANDDUST = 80;
        public const int DEFAULT_TAX_INTERVAL_DAYS_BLOODANDDUST = 15;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_BLOODANDDUST = 15;
        public const int DEFAULT_WORKER_COST_BLOODANDDUST = 125;
        //Losing is Fun
        public const int DEFAULT_SILVER_PER_RESOURCE_LOSINGISFUN = 70;
        public const int DEFAULT_TAX_INTERVAL_DAYS_LOSINGISFUN = 30;
        public const int DEFAULT_PRODUCTION_TITHE_MOD_LOSINGISFUN = 10;
        public const int DEFAULT_WORKER_COST_LOSINGISFUN = 150;
        // Global defaults
        // The default difficulty setting is Adventure Story, so set the global defaults accordingly
        public const int DEFAULT_SILVER_PER_RESOURCE = DEFAULT_SILVER_PER_RESOURCE_ADVENTURESTORY;
        public const int DEFAULT_TAX_INTERVAL_DAYS = DEFAULT_TAX_INTERVAL_DAYS_ADVENTURESTORY;
        public const int DEFAULT_PRODUCTION_TITHE_MOD = DEFAULT_PRODUCTION_TITHE_MOD_ADVENTURESTORY;
        public const int DEFAULT_WORKER_COST = DEFAULT_WORKER_COST_ADVENTURESTORY;
        /* Defaults for Research settings */
        public const bool DEFAULT_MEDIEVAL_TECH_ONLY = false;
        /* Defaults for Settlement settings */
        public const TaxDeliveryMode DEFAULT_TAX_DELIVERY_MODE = TaxDeliveryMode.None;
        public const TaxNotificationMode DEFAULT_TAX_NOTIFICATION_MODE = TaxNotificationMode.All;
        public static double DEFAULT_SETTLEMENT_FOUNDING_COST = 1000;
        public static double DEFAULT_SETTLEMENT_BASE_UPGRADE_COST = 1000;
        public static int DEFAULT_SETTLEMENT_MAX_LEVEL = 10;
        /* Defaults for Events & Military settings */
        public const bool DEFAULT_DISABLE_HOSTILE_MILITARY_ACTIONS = false;
        public const bool DEFAULT_DISABLE_RANDOM_EVENTS = false;
        public const bool DEFAULT_DISABLE_FORCED_PAUSING_DURING_EVENTS = true;
        public const bool DEFAULT_DEAD_PAWNS_INCREASE_MILITARY_COOLDOWN = true;
        public const BattleMode DEFAULT_BATTLE_MODE = BattleMode.Auto;
        public const int DEFAULT_MIN_DAYS_TIL_MILITARY_ACTION = 4;
        public const int DEFAULT_MAX_DAYS_TIL_MILITARY_ACTION = 10;
        public const int DEFAULT_MIN_DAYS_TIL_RANDOM_EVENT = 0;
        public const int DEFAULT_MAX_DAYS_TIL_RANDOM_EVENT = 6;
        public const float DEFAULT_MAX_THREAT_MULTIPLIER = 3.0f;
        public const float DEFAULT_DEFENDER_ADVANTAGE = 1.15f;
        public const float DEFAULT_EFFICIENCY_DAMPING = 0.5f;
        /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-* 
         *           ~  DEFAULTS END ~
         *-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

        public const int updateUiTimer = 150; // UI update interval in ticks

        public static int silverPerResource = DEFAULT_SILVER_PER_RESOURCE;
        public static double silverToCreateSettlement = DEFAULT_SETTLEMENT_FOUNDING_COST;

        private static int timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS;
        public static int timeBetweenTaxes => timeBetweenTaxes_days * GenDate.TicksPerDay;


        public static int productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD;
        public static int workerCost = DEFAULT_WORKER_COST;

        public static EmpireDifficultyLevel difficultyLevel = DEFAULT_DIFFICULTY_LEVEL;

        public static double settlementBaseUpgradeCost = DEFAULT_SETTLEMENT_BASE_UPGRADE_COST;
        public static int settlementMaxLevel = DEFAULT_SETTLEMENT_MAX_LEVEL;

        public static bool medievalTechOnly = DEFAULT_MEDIEVAL_TECH_ONLY;
        public static bool disableHostileMilitaryActions = DEFAULT_DISABLE_HOSTILE_MILITARY_ACTIONS;
        public static bool disableRandomEvents = DEFAULT_DISABLE_RANDOM_EVENTS;
        public static bool disableForcedPausingDuringEvents = DEFAULT_DISABLE_FORCED_PAUSING_DURING_EVENTS;
        public static bool deadPawnsIncreaseMilitaryCooldown = DEFAULT_DEAD_PAWNS_INCREASE_MILITARY_COOLDOWN;
        public static BattleMode battleMode = DEFAULT_BATTLE_MODE;
        public static TaxDeliveryMode forcedTaxDeliveryMode = DEFAULT_TAX_DELIVERY_MODE;
        public static TaxNotificationMode taxNotificationMode = DEFAULT_TAX_NOTIFICATION_MODE;

        public static int minDaysTillMilitaryAction = DEFAULT_MIN_DAYS_TIL_MILITARY_ACTION;
        public static int maxDaysTillMilitaryAction = DEFAULT_MAX_DAYS_TIL_MILITARY_ACTION;
        public static IntRange minMaxDaysTillMilitaryAction = new IntRange(minDaysTillMilitaryAction, maxDaysTillMilitaryAction);

        public static int minDaysTillRandomEvent = DEFAULT_MIN_DAYS_TIL_RANDOM_EVENT;
        public static int maxDaysTillRandomEvent = DEFAULT_MAX_DAYS_TIL_RANDOM_EVENT;
        public static IntRange minMaxDaysTillRandomEvent = new IntRange(minDaysTillRandomEvent, maxDaysTillRandomEvent);

        /* TODO: might be interesting to expose these values in the settings. Might be a bit much
         * for the user though. Perhaps can add an "advanced settings" tab that lets the user
         * fine-tune a lot of the smaller values? */
        public static double unrestBaseGain = 0;
        public static double unrestBaseLost = 1;
        public static double loyaltyBaseGain = 1;
        public static double loyaltyBaseLost = 0;
        public static double happinessBaseGain = 1;
        public static double happinessBaseLost = 0;
        public static double prosperityDriftRate = 1;
        public static int productionResearchBase = 100;
        public static double militaryAnimalCostMultiplier = 1.5;
        public static double militaryRaceCostMultiplier = 0.075;
        public static float mercenaryHealRatePerHour = 1f;

        public static float maxThreatMultiplier = DEFAULT_MAX_THREAT_MULTIPLIER;
        public static float defenderAdvantage = DEFAULT_DEFENDER_ADVANTAGE;
        public static float efficiencyDamping = DEFAULT_EFFICIENCY_DAMPING;

        public static int maxPolicyCount = 2;

        /* Flag for debug/verbose logging. */
        private static bool printDebug = false;
        public static bool PrintDebug => printDebug;

        /* Flag for performance/freeze diagnostic logging. */
        private static bool performanceLogging = false;
        public static bool PerformanceLogging => performanceLogging;

        // Window size settings
        public static float buildingWindowWidth = 800f;
        public static float buildingWindowHeight = 600f;

        // Patch notes version tracking — marks the latest version the player has seen
        public static int lastSeenVersionMajor = 0;
        public static int lastSeenVersionMinor = 0;
        public static int lastSeenVersionPatch = 0;

        // Patch notes auto-open threshold
        public const PatchNoteType DEFAULT_PATCH_NOTE_AUTO_OPEN_THRESHOLD = PatchNoteType.Major;
        public static PatchNoteType patchNoteAutoOpenThreshold = DEFAULT_PATCH_NOTE_AUTO_OPEN_THRESHOLD;

        // Per-event disable list (defName strings)
        public static HashSet<string> disabledEventDefs = new HashSet<string>();
        public static bool IsEventDisabled(string defName) => disabledEventDefs.Contains(defName);

        // Settings tab state
        private static int settingsTab = 0;
        private static List<TabRecord> settingsTabs = new List<TabRecord>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref silverPerResource, "silverPerResource", DEFAULT_SILVER_PER_RESOURCE);
            Scribe_Values.Look(ref timeBetweenTaxes_days, "timeBetweenTaxes_days", DEFAULT_TAX_INTERVAL_DAYS);
            Scribe_Values.Look(ref productionTitheMod, "productionTitheMod", DEFAULT_PRODUCTION_TITHE_MOD);
            Scribe_Values.Look(ref workerCost, "workerCost", DEFAULT_WORKER_COST);
            Scribe_Values.Look(ref settlementMaxLevel, "settlementMaxLevel", DEFAULT_SETTLEMENT_MAX_LEVEL);
            Scribe_Values.Look(ref medievalTechOnly, "medievalTechOnly", DEFAULT_MEDIEVAL_TECH_ONLY);
            Scribe_Values.Look(ref disableHostileMilitaryActions, "disableHostileMilitaryActions", DEFAULT_DISABLE_HOSTILE_MILITARY_ACTIONS);
            Scribe_Values.Look(ref disableRandomEvents, "disableRandomEvents", DEFAULT_DISABLE_RANDOM_EVENTS);
            Scribe_Values.Look(ref forcedTaxDeliveryMode, "forcedTaxDeliveryMode", DEFAULT_TAX_DELIVERY_MODE);
            Scribe_Values.Look(ref taxNotificationMode, "taxNotificationMode", DEFAULT_TAX_NOTIFICATION_MODE);
            Scribe_Values.Look(ref deadPawnsIncreaseMilitaryCooldown, "deadPawnsIncreaseMilitaryCooldown", DEFAULT_DEAD_PAWNS_INCREASE_MILITARY_COOLDOWN);
            Scribe_Values.Look(ref battleMode, "battleMode", DEFAULT_BATTLE_MODE);
            Scribe_Values.Look(ref minDaysTillMilitaryAction, "minDaysTillMilitaryAction", DEFAULT_MIN_DAYS_TIL_MILITARY_ACTION);
            Scribe_Values.Look(ref maxDaysTillMilitaryAction, "maxDaysTillMilitaryAction", DEFAULT_MAX_DAYS_TIL_MILITARY_ACTION);
            Scribe_Values.Look(ref minDaysTillRandomEvent, "minDaysTillRandomEvent", DEFAULT_MIN_DAYS_TIL_RANDOM_EVENT);
            Scribe_Values.Look(ref maxDaysTillRandomEvent, "maxDaysTillRandomEvent", DEFAULT_MAX_DAYS_TIL_RANDOM_EVENT);
            Scribe_Values.Look(ref buildingWindowWidth, "buildingWindowWidth", 800f);
            Scribe_Values.Look(ref buildingWindowHeight, "buildingWindowHeight", 600f);
            Scribe_Values.Look(ref difficultyLevel, "difficultyLevel", DEFAULT_DIFFICULTY_LEVEL);
            Scribe_Values.Look(ref printDebug, "printDebug", false);
            Scribe_Values.Look(ref performanceLogging, "performanceLogging", false);
            Scribe_Values.Look(ref maxThreatMultiplier, "maxThreatMultiplier", DEFAULT_MAX_THREAT_MULTIPLIER);
            Scribe_Values.Look(ref defenderAdvantage, "defenderAdvantage", DEFAULT_DEFENDER_ADVANTAGE);
            Scribe_Values.Look(ref efficiencyDamping, "efficiencyDamping", DEFAULT_EFFICIENCY_DAMPING);
            Scribe_Values.Look(ref mercenaryHealRatePerHour, "mercenaryHealRatePerHour", 1f);
            Scribe_Values.Look(ref lastSeenVersionMajor, "lastSeenVersionMajor", 0);
            Scribe_Values.Look(ref lastSeenVersionMinor, "lastSeenVersionMinor", 0);
            Scribe_Values.Look(ref lastSeenVersionPatch, "lastSeenVersionPatch", 0);
            Scribe_Values.Look(ref patchNoteAutoOpenThreshold, "patchNoteAutoOpenThreshold", DEFAULT_PATCH_NOTE_AUTO_OPEN_THRESHOLD);
            Scribe_Collections.Look(ref disabledEventDefs, "disabledEventDefs", LookMode.Value);
            if (disabledEventDefs == null) disabledEventDefs = new HashSet<string>();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                /* Re-construct the intranges */
                minMaxDaysTillMilitaryAction = new IntRange(minDaysTillMilitaryAction, maxDaysTillMilitaryAction);
                minMaxDaysTillRandomEvent = new IntRange(minDaysTillRandomEvent, maxDaysTillRandomEvent);
            }
        }

        public static string GetModVersion()
        {
            try
            {
                var mod = LoadedModManager.GetMod<FactionColoniesMod>();
                string manifestPath = Path.Combine(mod.Content.RootDir, "About", "Manifest.xml");
                if (File.Exists(manifestPath))
                {
                    string content = File.ReadAllText(manifestPath);
                    int versionStart = content.IndexOf("<version>") + 9;
                    int versionEnd = content.IndexOf("</version>");
                    if (versionStart > 8 && versionEnd > versionStart)
                    {
                        return content.Substring(versionStart, versionEnd - versionStart);
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.Warning("Failed to read version from manifest: " + ex.Message);
            }
            return "Unknown";
        }
        public static void ReapplyStatModifiers()
        {
            FactionFC faction = FactionCache.FactionComp;
            /* Clear stat modifiers for all settlements, and then reapply inherent/building modifiers */
            foreach (WorldSettlementFC settlement in faction.settlements)
            {
                settlement.ClearStatModifiers();
                settlement.BuildingsComp?.ReapplyBuildingStatModifiers();
                settlement.AddStatModifiers(settlement.settlementDef.statModifiers, "settlementType", settlement.settlementDef.label);
            }

            // Re-apply active event stat modifiers to settlements
            foreach (FCEvent evt in faction.events)
            {
                string sourceId = "event_" + evt.def.defName;
                if (evt.settlementTraitLocations.Count() > 0)
                {
                    foreach (WorldSettlementFC location in evt.settlementTraitLocations)
                    {
                        if (location != null)
                            location.AddStatModifiers(evt.def.statModifiers, sourceId, evt.def.label);
                    }
                }
                else
                {
                    foreach (WorldSettlementFC settlement in faction.settlements)
                    {
                        settlement.AddStatModifiers(evt.def.statModifiers, sourceId, evt.def.label);
                    }
                }
            }
        }

        public static bool IsModLoaded(string packageID) => LoadedModManager.RunningModsListForReading.Any(mod => mod.PackageIdPlayerFacing == packageID);


        public static void DebugMarker(ref int i)
        {
            LogUtil.Message($"DebugMarker: {i}");
            i++;
        }

        // Difficulty preset values
        public static void ApplyDifficultyPreset(EmpireDifficultyLevel difficulty)
        {
            switch (difficulty)
            {
                case EmpireDifficultyLevel.Peaceful:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_PEACEFUL;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_PEACEFUL;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_PEACEFUL;
                    workerCost = DEFAULT_WORKER_COST_PEACEFUL;
                    break;
                case EmpireDifficultyLevel.CommunityBuilder:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_COMMUNITYBUILDER;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_COMMUNITYBUILDER;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_COMMUNITYBUILDER;
                    workerCost = DEFAULT_WORKER_COST_COMMUNITYBUILDER;
                    break;
                case EmpireDifficultyLevel.AdventureStory:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_ADVENTURESTORY;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_ADVENTURESTORY;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_ADVENTURESTORY;
                    workerCost = DEFAULT_WORKER_COST_ADVENTURESTORY;
                    break;
                case EmpireDifficultyLevel.StriveToSurvive:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_STRIVETOSURVIVE;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_STRIVETOSURVIVE;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_STRIVETOSURVIVE;
                    workerCost = DEFAULT_WORKER_COST_STRIVETOSURVIVE;
                    break;
                case EmpireDifficultyLevel.BloodAndDust:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_BLOODANDDUST;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_BLOODANDDUST;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_BLOODANDDUST;
                    workerCost = DEFAULT_WORKER_COST_BLOODANDDUST;
                    break;
                case EmpireDifficultyLevel.LosingIsFun:
                    silverPerResource = DEFAULT_SILVER_PER_RESOURCE_LOSINGISFUN;
                    timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS_LOSINGISFUN;
                    productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD_LOSINGISFUN;
                    workerCost = DEFAULT_WORKER_COST_LOSINGISFUN;
                    break;
                case EmpireDifficultyLevel.Custom:
                    // Don't change anything for custom
                    break;
            }
        }

        public static int DaysBetweenTaxesByDifficulty(EmpireDifficultyLevel difficulty)
        {
            switch (difficulty)
            {
                case EmpireDifficultyLevel.Peaceful:
                    return DEFAULT_TAX_INTERVAL_DAYS_PEACEFUL;
                case EmpireDifficultyLevel.CommunityBuilder:
                    return DEFAULT_TAX_INTERVAL_DAYS_COMMUNITYBUILDER;
                case EmpireDifficultyLevel.AdventureStory:
                    return DEFAULT_TAX_INTERVAL_DAYS_ADVENTURESTORY;
                case EmpireDifficultyLevel.StriveToSurvive:
                    return DEFAULT_TAX_INTERVAL_DAYS_STRIVETOSURVIVE;
                case EmpireDifficultyLevel.BloodAndDust:
                    return DEFAULT_TAX_INTERVAL_DAYS_BLOODANDDUST;
                case EmpireDifficultyLevel.LosingIsFun:
                    return DEFAULT_TAX_INTERVAL_DAYS_LOSINGISFUN;
                default:
                    return DEFAULT_TAX_INTERVAL_DAYS;
            }
        }
        public static int TicksBetweenTaxesByDifficulty(EmpireDifficultyLevel difficulty)
        {
            return DaysBetweenTaxesByDifficulty(difficulty) * GenDate.TicksPerDay;
        }

        string silverPerResource_buffer;
        string timeBetweenTaxes_buffer;
        string productionTitheMod_buffer;
        string workerCost_buffer;
        string settlementMaxLevel_buffer;

        private Vector2 scrollVectorGeneral = new Vector2();
        private float viewRectHeightGeneral = -1f;
        private Vector2 scrollVectorEvents = new Vector2();
        private float viewRectHeightEvents = -1f;
        private Vector2 scrollVectorMilitary = new Vector2();
        private float viewRectHeightMilitary = -1f;

        /// <summary>
        /// Creates an option for the list of ForcedTaxDeliveryOptions. Shuttles may not be used if royality is inactive
        /// </summary>
        private FloatMenuOption ShuttleOption
        {
            get
            {
                if (ModsConfig.RoyaltyActive)
                {
                    return new FloatMenuOption("taxDeliveryModeShuttleDesc".Translate(), delegate () { forcedTaxDeliveryMode = TaxDeliveryMode.Shuttle; });
                }
                else
                {
                    return new FloatMenuOption("taxDeliveryModeShuttleUnavailableDesc".Translate(), null);
                }
            }
        }

        /// <summary>
        /// Creates a list of options for forced tax delivery
        /// </summary>
        private List<FloatMenuOption> ForcedTaxDeliveryOptions
        {
            get
            {
                return new List<FloatMenuOption>()
                {
                    new FloatMenuOption("taxDeliveryModeDefaultDesc".Translate(), delegate() {forcedTaxDeliveryMode = default;}),
                    new FloatMenuOption("taxDeliveryModeTaxSpotDesc".Translate(), delegate() {forcedTaxDeliveryMode = TaxDeliveryMode.TaxSpot;}),
                    new FloatMenuOption("taxDeliveryModeCaravanDesc".Translate(), delegate() {forcedTaxDeliveryMode = TaxDeliveryMode.Caravan;}),
                    new FloatMenuOption("taxDeliveryModeDropPodDesc".Translate(), delegate() {forcedTaxDeliveryMode = TaxDeliveryMode.DropPod;}),
                    ShuttleOption
                };
            }
        }

        private List<FloatMenuOption> BattleModeOptions => new List<FloatMenuOption>
        {
            new FloatMenuOption("FCBattleModeAuto".Translate() + " - " + "FCBattleModeAutoDesc".Translate(), () => battleMode = BattleMode.Auto),
            new FloatMenuOption("FCBattleModeManual".Translate() + " - " + "FCBattleModeManualDesc".Translate(), () => battleMode = BattleMode.Manual),
            new FloatMenuOption("FCBattleModeHybrid".Translate() + " - " + "FCBattleModeHybridDesc".Translate(), () => battleMode = BattleMode.Hybrid)
        };

        /// <summary>
        /// Creates a list of options for tax notification mode
        /// </summary>
        private List<FloatMenuOption> TaxNotificationOptions => new List<FloatMenuOption>
        {
            new FloatMenuOption("FCTaxNotifyAll".Translate(), () => taxNotificationMode = TaxNotificationMode.All),
            new FloatMenuOption("FCTaxNotifyLetterOnly".Translate(), () => taxNotificationMode = TaxNotificationMode.LetterOnly),
            new FloatMenuOption("FCTaxNotifyMessageOnly".Translate(), () => taxNotificationMode = TaxNotificationMode.MessageOnly),
            new FloatMenuOption("FCTaxNotifyNone".Translate(), () => taxNotificationMode = TaxNotificationMode.None)
        };

        public void DoWindowContents(Rect inRect)
        {
            // Build tabs
            settingsTabs.Clear();
            settingsTabs.Add(new TabRecord("FCSettingsTabGeneral".Translate(), delegate { settingsTab = 0; }, settingsTab == 0));
            settingsTabs.Add(new TabRecord("FCSettingsTabEvents".Translate(), delegate { settingsTab = 1; }, settingsTab == 1));
            settingsTabs.Add(new TabRecord("FCSettingsTabMilitary".Translate(), delegate { settingsTab = 2; }, settingsTab == 2));

            Rect contentRect = new Rect(inRect.x, inRect.y + 40f, inRect.width, inRect.height - 40f);
            Widgets.DrawMenuSection(contentRect);
            TabDrawer.DrawTabs(contentRect, settingsTabs);

            // Inset the content area slightly for padding
            Rect innerRect = contentRect.ContractedBy(5f);

            switch (settingsTab)
            {
                case 0: DoGeneralTab(innerRect); break;
                case 1: DoEventsTab(innerRect); break;
                case 2: DoMilitaryTab(innerRect); break;
            }
        }

        private void DoGeneralTab(Rect rect)
        {
            silverPerResource_buffer = silverPerResource.ToString();
            timeBetweenTaxes_buffer = timeBetweenTaxes_days.ToString();
            productionTitheMod_buffer = productionTitheMod.ToString();
            workerCost_buffer = workerCost.ToString();
            settlementMaxLevel_buffer = settlementMaxLevel.ToString();

            minMaxDaysTillMilitaryAction = new IntRange(minDaysTillMilitaryAction, maxDaysTillMilitaryAction);
            minMaxDaysTillRandomEvent = new IntRange(minDaysTillRandomEvent, maxDaysTillRandomEvent);

            viewRectHeightGeneral = viewRectHeightGeneral == -1f ? float.MaxValue : viewRectHeightGeneral;
            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 17f, viewRectHeightGeneral);
            Rect listRect = new Rect(rect.x, rect.y, rect.width - 17f, float.MaxValue);

            Widgets.BeginScrollView(rect, ref scrollVectorGeneral, viewRect);
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(listRect);

            // Display mod version
            ls.Label("FCModVersion".Translate(GetModVersion()));
            ls.Gap(10f);

            // Empire Difficulty Selection
            ls.Label("FCSettingEmpireDifficulty".Translate());
            ls.Gap(5f);

            // Create difficulty options with descriptions
            var difficultyOptions = new List<(EmpireDifficultyLevel level, string nameKey, string descKey)>
            {
                (EmpireDifficultyLevel.Peaceful, "FCDifficultyPeaceful", "FCDifficultyPeacefulDesc"),
                (EmpireDifficultyLevel.CommunityBuilder, "FCDifficultyCommunityBuilder", "FCDifficultyCommunityBuilderDesc"),
                (EmpireDifficultyLevel.AdventureStory, "FCDifficultyAdventureStory", "FCDifficultyAdventureStoryDesc"),
                (EmpireDifficultyLevel.StriveToSurvive, "FCDifficultyStriveToSurvive", "FCDifficultyStriveToSurviveDesc"),
                (EmpireDifficultyLevel.BloodAndDust, "FCDifficultyBloodAndDust", "FCDifficultyBloodAndDustDesc"),
                (EmpireDifficultyLevel.LosingIsFun, "FCDifficultyLosingIsFun", "FCDifficultyLosingIsFunDesc"),
                (EmpireDifficultyLevel.Custom, "FCDifficultyCustom", "FCDifficultyCustomDesc")
            };

            foreach (var option in difficultyOptions)
            {
                bool isSelected = difficultyLevel == option.level;

                if (ls.RadioButton(option.nameKey.Translate(), isSelected))
                {
                    if (!isSelected) // Only change if not already selected
                    {
                        difficultyLevel = option.level;
                        if (option.level != EmpireDifficultyLevel.Custom)
                        {
                            ApplyDifficultyPreset(option.level);
                        }
                    }
                }
                // Add description as a separate indented label
                ls.Label("    " + option.descKey.Translate(), -1f);
            }

            ls.Gap(15f);

            // Show economic settings only if Custom is selected
            if (difficultyLevel == EmpireDifficultyLevel.Custom)
            {
                ls.Label("FCSettingSilverPerResource".Translate());
                ls.IntEntry(ref silverPerResource, ref silverPerResource_buffer);
                ls.Label("FCSettingDaysBetweenTax".Translate());
                ls.IntEntry(ref timeBetweenTaxes_days, ref timeBetweenTaxes_buffer);
                ls.Label("FCSettingProductionTitheMod".Translate());
                ls.IntEntry(ref productionTitheMod, ref productionTitheMod_buffer);
                ls.Label("FCSettingWorkerCost".Translate());
                ls.IntEntry(ref workerCost, ref workerCost_buffer);
            }
            else
            {
                // Show current values as read-only labels for non-custom difficulties
                ls.Label($"FCSettingSilverPerResource".Translate() + ": " + silverPerResource);
                ls.Label($"FCSettingDaysBetweenTax".Translate() + ": " + timeBetweenTaxes_days);
                ls.Label($"FCSettingProductionTitheMod".Translate() + ": " + productionTitheMod);
                ls.Label($"FCSettingWorkerCost".Translate() + ": " + workerCost);
            }

            ls.Label("FCSettingMaxSettlementLevel".Translate());
            ls.IntEntry(ref settlementMaxLevel, ref settlementMaxLevel_buffer);
            ls.CheckboxLabeled("MedievalTechOnly".Translate(), ref medievalTechOnly);
            if (ls.ButtonText("selectTaxDeliveryModeButton".Translate() + forcedTaxDeliveryMode)) Find.WindowStack.Add(new FloatMenu(ForcedTaxDeliveryOptions));
            if (ls.ButtonText("FCTaxNotificationModeButton".Translate() + taxNotificationMode)) Find.WindowStack.Add(new FloatMenu(TaxNotificationOptions));

            ls.CheckboxLabeled("FCSettingEnableDebugLogging".Translate(), ref printDebug);

            bool prevPerfLogging = performanceLogging;
            ls.CheckboxLabeled("FCSettingPerfLogging".Translate(), ref performanceLogging);
            if (performanceLogging != prevPerfLogging)
                PerfWatchdog.SetEnabled(performanceLogging);

            if (ls.ButtonText("FCOpenPatchNotes".Translate())) DebugActionsMisc.PatchNotesDisplayWindow();

            string thresholdLabel;
            switch (patchNoteAutoOpenThreshold)
            {
                case PatchNoteType.Major: thresholdLabel = "Major only"; break;
                case PatchNoteType.Minor: thresholdLabel = "Minor and above"; break;
                case PatchNoteType.Hotfix: thresholdLabel = "Hotfix and above"; break;
                case PatchNoteType.Patch: thresholdLabel = "Patch and above"; break;
                default: thresholdLabel = "Never"; break;
            }
            if (ls.ButtonText("FCPatchNoteAutoOpenThreshold".Translate() + thresholdLabel))
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new FloatMenuOption("Major only", () => patchNoteAutoOpenThreshold = PatchNoteType.Major),
                    new FloatMenuOption("Minor and above", () => patchNoteAutoOpenThreshold = PatchNoteType.Minor),
                    new FloatMenuOption("Hotfix and above", () => patchNoteAutoOpenThreshold = PatchNoteType.Hotfix),
                    new FloatMenuOption("Patch and above", () => patchNoteAutoOpenThreshold = PatchNoteType.Patch),
                    new FloatMenuOption("Never", () => patchNoteAutoOpenThreshold = PatchNoteType.Undefined)
                }));
            }

            if (ls.ButtonText("FCSettingResetButton".Translate()))
            {
                silverPerResource = DEFAULT_SILVER_PER_RESOURCE;
                timeBetweenTaxes_days = DEFAULT_TAX_INTERVAL_DAYS;
                productionTitheMod = DEFAULT_PRODUCTION_TITHE_MOD;
                workerCost = DEFAULT_WORKER_COST;
                medievalTechOnly = DEFAULT_MEDIEVAL_TECH_ONLY;
                settlementMaxLevel = DEFAULT_SETTLEMENT_MAX_LEVEL;
                minDaysTillMilitaryAction = DEFAULT_MIN_DAYS_TIL_MILITARY_ACTION;
                maxDaysTillMilitaryAction = DEFAULT_MAX_DAYS_TIL_MILITARY_ACTION;
                minDaysTillRandomEvent = DEFAULT_MIN_DAYS_TIL_RANDOM_EVENT;
                maxDaysTillRandomEvent = DEFAULT_MAX_DAYS_TIL_RANDOM_EVENT;
                disableRandomEvents = DEFAULT_DISABLE_RANDOM_EVENTS;
                deadPawnsIncreaseMilitaryCooldown = DEFAULT_DEAD_PAWNS_INCREASE_MILITARY_COOLDOWN;
                battleMode = DEFAULT_BATTLE_MODE;
                maxThreatMultiplier = DEFAULT_MAX_THREAT_MULTIPLIER;
                defenderAdvantage = DEFAULT_DEFENDER_ADVANTAGE;
                efficiencyDamping = DEFAULT_EFFICIENCY_DAMPING;
                mercenaryHealRatePerHour = 1f;
                disableForcedPausingDuringEvents = DEFAULT_DISABLE_FORCED_PAUSING_DURING_EVENTS;
                forcedTaxDeliveryMode = DEFAULT_TAX_DELIVERY_MODE;
                taxNotificationMode = DEFAULT_TAX_NOTIFICATION_MODE;
                difficultyLevel = DEFAULT_DIFFICULTY_LEVEL;
                patchNoteAutoOpenThreshold = DEFAULT_PATCH_NOTE_AUTO_OPEN_THRESHOLD;
                disabledEventDefs.Clear();
                performanceLogging = false;
                PerfWatchdog.SetEnabled(false);
                ApplyDifficultyPreset(difficultyLevel);
            }

            viewRectHeightGeneral = ls.CurHeight + 5f;
            ls.End();

            Widgets.EndScrollView();
        }

        private void DoEventsTab(Rect rect)
        {
            viewRectHeightEvents = viewRectHeightEvents == -1f ? float.MaxValue : viewRectHeightEvents;
            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 17f, viewRectHeightEvents);
            Rect listRect = new Rect(rect.x, rect.y, rect.width - 17f, float.MaxValue);

            Widgets.BeginScrollView(rect, ref scrollVectorEvents, viewRect);
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(listRect);

            ls.CheckboxLabeled("FCSettingDisableRandomEvents".Translate(), ref disableRandomEvents);
            ls.CheckboxLabeled("FCSettingForcedPausing".Translate(), ref disableForcedPausingDuringEvents);

            ls.Gap(5f);

            minMaxDaysTillRandomEvent = new IntRange(minDaysTillRandomEvent, maxDaysTillRandomEvent);
            ls.Label("FCSettingMinMaxRandomEvent".Translate());
            ls.IntRange(ref minMaxDaysTillRandomEvent, 0, 30);
            minDaysTillRandomEvent = minMaxDaysTillRandomEvent.min;
            maxDaysTillRandomEvent = Math.Max(1, minMaxDaysTillRandomEvent.max);

            ls.GapLine();

            ls.Label("FCSettingConfigureEvents".Translate());
            ls.Gap(5f);

            // Build set of events that are follow-ups of other events (not independent triggers)
            HashSet<string> followUpDefNames = new HashSet<string>();
            foreach (FCEventDef def in DefDatabase<FCEventDef>.AllDefsListForReading)
            {
                if (def.followingEvent != null) followUpDefNames.Add(def.followingEvent.defName);
                if (def.followingEvent2 != null) followUpDefNames.Add(def.followingEvent2.defName);
            }

            List<FCEventDef> rootEvents = new List<FCEventDef>();
            foreach (FCEventDef def in DefDatabase<FCEventDef>.AllDefsListForReading)
            {
                if (followUpDefNames.Contains(def.defName)) continue;
                if (def.activateAtStart || (def.isRandomEvent && def.options.Count == 0))
                {
                    rootEvents.Add(def);
                }
            }

            // Group by category, then sort alphabetically within each group
            var grouped = new Dictionary<string, List<FCEventDef>>();
            foreach (FCEventDef def in rootEvents)
            {
                string catLabel = def.category != null ? def.category.LabelCap.ToString() : "Other";
                if (!grouped.ContainsKey(catLabel))
                    grouped[catLabel] = new List<FCEventDef>();
                grouped[catLabel].Add(def);
            }

            int rowIndex = 0;
            foreach (string catLabel in grouped.Keys.OrderBy(k => k))
            {
                ls.Gap(3f);
                ls.Label(catLabel);
                foreach (FCEventDef def in grouped[catLabel].OrderBy(d => d.label))
                {
                    Rect rowRect = ls.GetRect(Text.LineHeight);

                    if (rowIndex % 2 == 1)
                    {
                        Widgets.DrawLightHighlight(rowRect);
                    }
                    rowIndex++;

                    bool enabled = !disabledEventDefs.Contains(def.defName);
                    bool prev = enabled;
                    Widgets.CheckboxLabeled(rowRect, "  " + def.label, ref enabled);
                    if (enabled != prev)
                    {
                        if (enabled) disabledEventDefs.Remove(def.defName);
                        else disabledEventDefs.Add(def.defName);
                    }
                }
            }

            if (disabledEventDefs.Count > 0)
            {
                ls.Gap(10f);
                if (ls.ButtonText("FCSettingEnableAllEvents".Translate()))
                {
                    disabledEventDefs.Clear();
                }
            }

            viewRectHeightEvents = ls.CurHeight + 5f;
            ls.End();

            Widgets.EndScrollView();
        }

        private void DoMilitaryTab(Rect rect)
        {
            minMaxDaysTillMilitaryAction = new IntRange(minDaysTillMilitaryAction, maxDaysTillMilitaryAction);

            viewRectHeightMilitary = viewRectHeightMilitary == -1f ? float.MaxValue : viewRectHeightMilitary;
            Rect viewRect = new Rect(rect.x, rect.y, rect.width - 17f, viewRectHeightMilitary);
            Rect listRect = new Rect(rect.x, rect.y, rect.width - 17f, float.MaxValue);

            Widgets.BeginScrollView(rect, ref scrollVectorMilitary, viewRect);
            Listing_Standard ls = new Listing_Standard();
            ls.Begin(listRect);

            ls.CheckboxLabeled("FCSettingDisableHostileMilActions".Translate(), ref disableHostileMilitaryActions);
            ls.CheckboxLabeled("FCSettingDeadPawnsIncreaseMilCooldown".Translate(), ref deadPawnsIncreaseMilitaryCooldown);
            if (ls.ButtonText("FCSettingBattleMode".Translate() + battleMode)) Find.WindowStack.Add(new FloatMenu(BattleModeOptions));

            ls.Gap(10f);

            ls.Label("FCSettingMinMaxMilitaryAction".Translate());
            ls.IntRange(ref minMaxDaysTillMilitaryAction, 1, 30);
            minDaysTillMilitaryAction = minMaxDaysTillMilitaryAction.min;
            maxDaysTillMilitaryAction = Math.Max(1, minMaxDaysTillMilitaryAction.max);

            ls.Label("FCSettingMaxThreatScaling".Translate() + ": " + maxThreatMultiplier.ToString("0.0") + "x");
            maxThreatMultiplier = ls.Slider(maxThreatMultiplier, 1.0f, 5.0f);

            ls.Label("FCSettingDefenderAdvantage".Translate() + ": " + defenderAdvantage.ToString("0.00") + "x");
            defenderAdvantage = ls.Slider(defenderAdvantage, 1.0f, 1.5f);

            ls.Label("FCSettingEfficiencyDamping".Translate() + ": " + efficiencyDamping.ToString("0.00"), -1f, "FCSettingEfficiencyDampingTooltip".Translate());
            efficiencyDamping = ls.Slider(efficiencyDamping, 0.0f, 1.0f);

            ls.Label("FCSettingMercHealRate".Translate() + ": " + mercenaryHealRatePerHour.ToString("0.0") + " HP/hr", -1f, "FCSettingMercHealRateTip".Translate());
            mercenaryHealRatePerHour = ls.Slider(mercenaryHealRatePerHour, 0.1f, 100f);

            viewRectHeightMilitary = ls.CurHeight + 5f;
            ls.End();

            Widgets.EndScrollView();
        }
    }


    public class FactionColoniesMod : Mod
    {
        public FCSettings settings = new FCSettings();

        public FactionColoniesMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<FCSettings>();
        }

        public override string SettingsCategory()
        {
            return "Empire";
        }

        public override void DoSettingsWindowContents(Rect inRect) => settings.DoWindowContents(inRect);
    }
}
