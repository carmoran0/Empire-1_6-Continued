using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FactionColonies.util;
using RimWorld;
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
        public static double prosperityBaseRecovery = 1;
        public static int productionResearchBase = 100;
        public static double militaryAnimalCostMultiplier = 1.5;
        public static double militaryRaceCostMultiplier = 0.15;

        public static float maxThreatMultiplier = DEFAULT_MAX_THREAT_MULTIPLIER;
        public static float defenderAdvantage = DEFAULT_DEFENDER_ADVANTAGE;

        public static int maxPolicyCount = 2;

        /* Flag for debug/verbose logging. */
        private static bool printDebug = false;
        public static bool PrintDebug => printDebug;

        /* Flag for performance watchdog logging. */
        public static bool performanceLogging = false;

        // Window size settings - add these fields
        public static float buildingWindowWidth = 800f;
        public static float buildingWindowHeight = 600f;

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
            i ++;
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

        private Vector2 scrollVector = new Vector2();
        private float viewRectHeight = -1f;

        private bool firstRun = true;
        private bool fixDone = false;

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
            silverPerResource_buffer = silverPerResource.ToString();
            timeBetweenTaxes_buffer = timeBetweenTaxes_days.ToString();
            productionTitheMod_buffer = productionTitheMod.ToString();
            workerCost_buffer = workerCost.ToString();
            settlementMaxLevel_buffer = settlementMaxLevel.ToString();

            minMaxDaysTillMilitaryAction = new IntRange(minDaysTillMilitaryAction, maxDaysTillMilitaryAction);
            minMaxDaysTillRandomEvent = new IntRange(minDaysTillRandomEvent, maxDaysTillRandomEvent);

            viewRectHeight = viewRectHeight == -1f ? float.MaxValue : viewRectHeight;
            Rect viewRect = new Rect(inRect.x, inRect.y, inRect.width - 17f, viewRectHeight);
            Rect listRect = new Rect(inRect.x, inRect.y, inRect.width - 17f, float.MaxValue);

            Widgets.BeginScrollView(inRect, ref scrollVector, viewRect);
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
            ls.CheckboxLabeled("FCSettingDisableHostileMilActions".Translate(), ref disableHostileMilitaryActions);
            ls.CheckboxLabeled("FCSettingDisableRandomEvents".Translate(), ref disableRandomEvents);
            ls.CheckboxLabeled("FCSettingDeadPawnsIncreaseMilCooldown".Translate(), ref deadPawnsIncreaseMilitaryCooldown);
            ls.CheckboxLabeled("FCSettingForcedPausing".Translate(), ref disableForcedPausingDuringEvents);
            if (ls.ButtonText("FCSettingBattleMode".Translate() + battleMode)) Find.WindowStack.Add(new FloatMenu(BattleModeOptions));
            if (ls.ButtonText("selectTaxDeliveryModeButton".Translate() + forcedTaxDeliveryMode)) Find.WindowStack.Add(new FloatMenu(ForcedTaxDeliveryOptions));
            if (ls.ButtonText("FCTaxNotificationModeButton".Translate() + taxNotificationMode)) Find.WindowStack.Add(new FloatMenu(TaxNotificationOptions));

            ls.Label("FCSettingMinMaxMilitaryAction".Translate());
            ls.IntRange(ref minMaxDaysTillMilitaryAction, 1, 30);
            minDaysTillMilitaryAction = minMaxDaysTillMilitaryAction.min;
            maxDaysTillMilitaryAction = Math.Max(1, minMaxDaysTillMilitaryAction.max);

            ls.Label("FCSettingMaxThreatScaling".Translate() + ": " + maxThreatMultiplier.ToString("0.0") + "x");
            maxThreatMultiplier = ls.Slider(maxThreatMultiplier, 1.0f, 5.0f);

            ls.Label("FCSettingDefenderAdvantage".Translate() + ": " + defenderAdvantage.ToString("0.00") + "x");
            defenderAdvantage = ls.Slider(defenderAdvantage, 1.0f, 1.5f);

            ls.Label("FCSettingMinMaxRandomEvent".Translate());
            ls.IntRange(ref minMaxDaysTillRandomEvent, 0, 30);
            minDaysTillRandomEvent = minMaxDaysTillRandomEvent.min;
            maxDaysTillRandomEvent = Math.Max(1, minMaxDaysTillRandomEvent.max);

            ls.CheckboxLabeled("FCSettingEnableDebugLogging".Translate(), ref printDebug);
            ls.CheckboxLabeled("FCSettingEnablePerfLogging".Translate(), ref performanceLogging);

            if (ls.ButtonText("FCOpenPatchNotes".Translate())) DebugActionsMisc.PatchNotesDisplayWindow();

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
                disableForcedPausingDuringEvents = DEFAULT_DISABLE_FORCED_PAUSING_DURING_EVENTS;
                forcedTaxDeliveryMode = DEFAULT_TAX_DELIVERY_MODE;
                taxNotificationMode = DEFAULT_TAX_NOTIFICATION_MODE;
                difficultyLevel = DEFAULT_DIFFICULTY_LEVEL;
                ApplyDifficultyPreset(difficultyLevel);
            }

            viewRectHeight = ls.CurHeight + 5f;
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

        public override void WriteSettings()
        {
            base.WriteSettings();
        }

        public override void DoSettingsWindowContents(Rect inRect) => settings.DoWindowContents(inRect);
    }
}
