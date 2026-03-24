using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class FactionFC : WorldComponent, ILifecycleParticipant
    {
        #region Fields & Properties

        // ── Core Identity ──
        public string name = "PlayerFaction".Translate();
        public string title = "Bastion".Translate();
        public Texture2D factionIcon = TexLoad.factionIcons[0];
        public string factionIconPath = TexLoad.factionIcons[0].name;
        public bool factionCreated;
        private int foundingTick = 0;
        public int FoundingTick => foundingTick;
        private Vector2 startingLongLat = new Vector2();
        public Vector2 StartingLongLat => startingLongLat;

        // ── Capital & Maps ──
        public PlanetTile capitalLocation = PlanetTile.Invalid;
        public string capitalPlanet;
        public Map taxMap;

        public Map TaxMap
        {
            get
            {
                Map map;
                if (taxMap == null)
                {
                    map = Find.WorldObjects.SettlementAt(FactionCache.FactionComp.capitalLocation)?.Map;
                    if (map is null)
                    {
                        //if no tax map or no capital map is valid
                        map = Find.CurrentMap.IsPlayerHome ? Find.CurrentMap : Find.AnyPlayerHomeMap;

                        LogUtil.MessageForce(
                            "Unable to find a player-set tax map or a valid location for the capital. Please open the faction main menu tab and set the capital and tax map. Taxes were sent to the following random PlayerHomeMap " +
                            map.Parent.LabelCap);
                    }
                }
                else
                {
                    map = taxMap;
                }

                return map;
            }
        }

        // ── Settlements ──
        /// <summary>
        /// Used by other mods to find our settlements. Move, rename, or otherwise modify at your own peril
        /// </summary>
        public List<WorldSettlementFC> settlements = new List<WorldSettlementFC>();

        // ── Timing & Scheduling ──
        public int taxTimeDue = Find.TickManager.TicksGame;
        public int timeStart = Find.TickManager.TicksGame;
        public int uiTimeUpdate;
        public int militaryTimeDue;
        public const int MercenaryHealTickInterval = GenDate.TicksPerHour;
        private bool firstTick = true;

        // ── Lazy-Cached Averages ──
        /* Faction averages — lazy-cached via dirtyAveragesCache */
        private double _averageHappiness = 100;
        private double _averageLoyalty = 100;
        private double _averageUnrest = 0;
        private double _averageProsperity = 100;
        private bool dirtyAveragesCache = true;
        public double averageHappiness { get { if (dirtyAveragesCache) RecomputeAverages(); return _averageHappiness; } }
        public double averageLoyalty { get { if (dirtyAveragesCache) RecomputeAverages(); return _averageLoyalty; } }
        public double averageUnrest { get { if (dirtyAveragesCache) RecomputeAverages(); return _averageUnrest; } }
        public double averageProsperity { get { if (dirtyAveragesCache) RecomputeAverages(); return _averageProsperity; } }

        // ── Lazy-Cached Profit ──
        /* Faction profit — lazy-cached via dirtyFactionProfitCache */
        private double _income;
        private double _upkeep;
        private double _profit;
        private bool dirtyFactionProfitCache = true;
        public double income { get { if (dirtyFactionProfitCache) RecomputeTotalProfit(); return _income; } }
        public double upkeep { get { if (dirtyFactionProfitCache) RecomputeTotalProfit(); return _upkeep; } }
        public double profit { get { if (dirtyFactionProfitCache) RecomputeTotalProfit(); return _profit; } }

        // ── Lazy-Cached Tech Level ──
        /* Tech level — lazy-cached via dirtyTechLevelCache */
        private TechLevel _techLevel = TechLevel.Undefined;
        private bool dirtyTechLevelCache = true;
        public TechLevel techLevel
        {
            get
            {
                if (dirtyTechLevelCache) RecomputeTechLevel();
                return _techLevel;
            }
        }

        // ── Lazy-Cached Grand Thing List ──
        private bool DirtyGrandThingListFlag = true;
        private List<ThingDef> grandThingList = null;

        // ── Stat & Behavior Caches ──
        private Dictionary<FCStatDef, double> cachedFactionStatValues = new Dictionary<FCStatDef, double>();
        private List<FCPolicyBehavior> _cachedBehaviors = null;
        public List<FCPolicyBehavior> cachedBehaviors
        {
            get
            {
                if (_cachedBehaviors == null)
                    RebuildBehaviorCache();
                return _cachedBehaviors;
            }
        }
        private HashSet<FCActionType> _cachedBlockedActions;
        private HashSet<FCActionType> _cachedEnabledActions;
        private HashSet<MilitaryJobDef> _cachedBlockedJobs;
        private HashSet<MilitaryJobDef> _cachedEnabledJobs;

        // ── Policies & Traits ──
        public List<FCPolicy> policies = new List<FCPolicy>();
        public List<FCPolicy> factionTraits = new List<FCPolicy>
        {
            new FCPolicy(FCPolicyDefOf.empty),
            new FCPolicy(FCPolicyDefOf.empty),
            new FCPolicy(FCPolicyDefOf.empty),
            new FCPolicy(FCPolicyDefOf.empty),
            new FCPolicy(FCPolicyDefOf.empty)
        };

        // ── Edicts (toggleable faction-level policies) ──
        public Dictionary<FCPolicyCategory, FCPolicy> edicts = new Dictionary<FCPolicyCategory, FCPolicy>();
        private HashSet<FCPolicyCategory> pendingEdictActivations = new HashSet<FCPolicyCategory>();

        // Minimum faction level required to unlock each edict category
        public static readonly Dictionary<FCPolicyCategory, int> EdictCategoryUnlockLevels = new Dictionary<FCPolicyCategory, int>
        {
            { FCPolicyCategory.Social, 2 },
            { FCPolicyCategory.Tax, 3 },
            { FCPolicyCategory.Doctrine, 3 },
            { FCPolicyCategory.Military, 4 }
        };

        // ── Events & Bills ──
        public List<FCEvent> events = new List<FCEvent>();
        public float randomEventLastAdded = 0f;
        public List<BillFC> Bills = new List<BillFC>();
        public List<BillFC> OldBills = new List<BillFC>();
        public bool autoResolveBills;
        public bool autoResolveBillsChanged = false;

        // ── Resources ──
        public float researchPointPool = 0;
        public List<ResourcePool> resourcePools = new List<ResourcePool>();
        public ThingWithComps powerOutput;
        public List<ResourceDisplay> factionResources = new List<ResourceDisplay>();
        public List<ResourceDisplay> FactionResources => factionResources;

        // ── Military & Roads ──
        public MilitaryCustomizationUtil militaryCustomizationUtil = new MilitaryCustomizationUtil();
        public EmpireThreatAdaptation threatAdaptation = new EmpireThreatAdaptation();
        public FCRoadBuilder roadBuilder = new FCRoadBuilder();
        public List<int> militaryTargets = new List<int>();

        // ── Caravans ──
        public List<PlanetTile> settlementCaravansList = new List<PlanetTile>(); //list of locations caravans already sent to

        // ── Leveling ──
        public int factionLevel = 1;
        public float factionXPCurrent = 0;
        public float factionXPGoal = 100;

        // ── ID Counters ──
        private int nextUnitId;
        private int nextSquadId;
        public int NextUnitID => ++nextUnitId;
        public int NextSquadID => ++nextSquadId;
        public int nextSettlementFCID = 1;
        public int nextMercenarySquadID = 1;
        public int nextMercenaryID = 1;
        public int nextTaxID = 1;
        public int nextBillID = 1;
        public int nextEventID = 1;
        public int nextPrisonerID = 1;
        public int nextMilitaryFireSupportID = 1;

        // ── Filters & Misc ──
        public XenotypeFilter xenotypeFilter;
        public List<PlanetLayerDef> layersForTilePicker = null;
        public float tradedAmount = 0;

        #endregion

        #region Constructor & Lifecycle

        private static bool harmonyPatched = false;

        public FactionFC(World world) : base(world)
        {
            if (!harmonyPatched)
            {
                var harmony = new Harmony("com.Matathias.Empire");

                if (SystemInfo.operatingSystemFamily == OperatingSystemFamily.Linux)
                {
                    FixLinuxHarmonyCrash(harmony);
                }

                harmony.PatchAll();
                harmonyPatched = true;
            }
        }

        // Fix a crash related to a harmony bug on Linux
        // This gets all patches Empire makes, gets the ones that would crash on Linux, and fixes them
        static void FixLinuxHarmonyCrash(Harmony harmony)
        {
            bool WouldCrash(MethodInfo method)
            {
                if (method == null || !method.IsVirtual || method.IsAbstract || method.IsFinal)
                {
                    return false;
                }

                byte[] bytes = method.GetMethodBody()?.GetILAsByteArray();
                if (bytes == null || bytes.Length == 0 || (bytes.Length == 1 && bytes.First() == 0x2A))
                {
                    return true;
                }
                return false;
            }

            var methods = typeof(FactionFC).Assembly.GetTypes().Where(t0 => t0 != null && t0.IsClass && !typeof(Delegate).IsAssignableFrom(t0) && t0.GetCustomAttributes(typeof(HarmonyPatch)).Any()).SelectMany(t1 =>
            {
                HarmonyPatch patch = (HarmonyPatch)Attribute.GetCustomAttribute(t1, typeof(HarmonyPatch));
                MethodInfo[] m = patch?.info?.declaringType?.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (m == null) return new List<MethodInfo>();

                return m.Where(met => met.Name == patch.info.methodName);
            }).Where(WouldCrash);

            foreach (MethodInfo i in methods)
            {
                // Patching methods without any Prefixes/Postfixes before actually patching them fixes it. Idk why
                harmony.Patch(i);
            }
        }

        /// <summary>
        /// Called when the Empire faction is created.
        /// </summary>
        public void OnCreation()
        {
            foundingTick = Find.TickManager.TicksGame;
        }

        public string GetFoundingDate(bool full = true)
        {
            if (full)
            {
                return GenDate.DateFullStringAt(foundingTick, startingLongLat);
            }
            else
            {
                return GenDate.DateShortStringAt(foundingTick, startingLongLat);
            }
        }

        #endregion

        #region Serialization & Initialization

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref foundingTick, "foundingTick", defaultValue: 0);
            Scribe_Values.Look(ref startingLongLat, "foundingLongLat");
            Scribe_Values.Look(ref capitalLocation, "capitalLocation");
            Scribe_Values.Look(ref capitalPlanet, "capitalPlanet");
            Scribe_References.Look(ref taxMap, "taxMap");
            Scribe_Values.Look(ref factionCreated, "factionCreated");

            Scribe_Values.Look(ref _averageHappiness, "averageHappiness");
            Scribe_Values.Look(ref _averageLoyalty, "averageLoyalty");
            Scribe_Values.Look(ref _averageUnrest, "averageUnrest");
            Scribe_Values.Look(ref _averageProsperity, "averageProsperity");

            Scribe_Values.Look(ref _income, "income");
            Scribe_Values.Look(ref _upkeep, "upkeep");
            Scribe_Values.Look(ref _profit, "profit");

            Scribe_Values.Look(ref taxTimeDue, "taxTimeDue");
            Scribe_Values.Look(ref timeStart, "timeStart", -1);
            Scribe_Values.Look(ref uiTimeUpdate, "uiTimeUpdate");
            Scribe_Values.Look(ref militaryTimeDue, "militaryTimeDue", -1);
            Scribe_Values.Look(ref _techLevel, "techLevel");
            Scribe_Values.Look(ref factionIconPath, "factionIconPath", "Base");

            Scribe_Collections.Look(ref settlements, "settlements", LookMode.Reference);
            Scribe_Collections.Look(ref policies, "factionPolicies", LookMode.Deep);
            Scribe_Collections.Look(ref events, "events", LookMode.Deep);
            Scribe_Collections.Look(ref settlementCaravansList, "settlementCaravansList", LookMode.Value);
            Scribe_Collections.Look(ref militaryTargets, "militaryTargets", LookMode.Value);

            //New Producitons types
            Scribe_Collections.Look(ref resourcePools, "resourcePools", LookMode.Deep);
            Scribe_References.Look(ref powerOutput, "powerOutput");

            //save resources
            Scribe_Collections.Look(ref factionResources, "factionResources", LookMode.Deep);

            Scribe_Deep.Look(ref xenotypeFilter, "xenotypeFilter");

            //Update
            Scribe_Values.Look(ref nextSettlementFCID, "nextSettlementFCID");

            //Military Customization Util
            Scribe_Deep.Look(ref militaryCustomizationUtil, "militaryCustomizationUtil");
            Scribe_Values.Look(ref nextMilitaryFireSupportID, "nextMilitaryFireSupportID", 1);
            Scribe_Values.Look(ref nextUnitId, "nextUnitID", 1);
            Scribe_Values.Look(ref nextSquadId, "nextSquadID", 1);
            Scribe_Values.Look(ref nextMercenaryID, "nextMercenaryID", 1);
            Scribe_Values.Look(ref nextMercenarySquadID, "nextMercenarySquadID", 1);
            Scribe_Values.Look(ref nextPrisonerID, "nextPrisonerID", 1);

            //New Tax Stuff
            Scribe_Values.Look(ref nextTaxID, "nextTaxID", 1);
            Scribe_Values.Look(ref nextBillID, "nextBillID", 1);
            Scribe_Values.Look(ref nextEventID, "nextEventID", 1);


            Scribe_Collections.Look(ref Bills, "Bills", LookMode.Deep);
            Scribe_Collections.Look(ref OldBills, "OldBills", LookMode.Deep);
            Scribe_Values.Look(ref autoResolveBills, "autoResolveBills");

            //Road builder
            Scribe_Deep.Look(ref roadBuilder, "roadBuilder");

            //Threat adaptation
            Scribe_Deep.Look(ref threatAdaptation, "threatAdaptation");
            if (threatAdaptation == null) threatAdaptation = new EmpireThreatAdaptation();

            // Legacy trait Scribe_Values removed — state is now in FCPolicyBehavior subclasses,
            // serialized via FCPolicy.ExposeData -> FCPolicyBehavior.ExposeData.

            //Settlement Leveling
            Scribe_Values.Look(ref factionLevel, "factionLevel");
            Scribe_Values.Look(ref factionXPCurrent, "factionXPCurrent");
            Scribe_Values.Look(ref factionXPGoal, "factionXPGoal");
            Scribe_Collections.Look(ref factionTraits, "factionTraits", LookMode.Deep);

            Scribe_Collections.Look(ref edicts, "edicts", LookMode.Value, LookMode.Deep);
            if (edicts == null) edicts = new Dictionary<FCPolicyCategory, FCPolicy>();
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                pendingEdictActivations.Clear();
                foreach (var kvp in edicts)
                {
                    if (kvp.Value != null && !kvp.Value.IsFullyActive)
                        pendingEdictActivations.Add(kvp.Key);
                }
            }

            //Research Trading
            Scribe_Values.Look(ref tradedAmount, "tradedAmount");

            //Random Event
            Scribe_Values.Look(ref randomEventLastAdded, "randomEventLastAddedTick");
        }

        public override void FinalizeInit(bool fromLoad)
        {
            LogUtil.MessageForce("Empire v" + FCSettings.GetModVersion());
            base.FinalizeInit(fromLoad);

            // Apply saved tech level to FactionDef early — must happen before anything
            // reads faction.def.techLevel directly. Calls UpdateFactionDef directly instead
            // of going through RecomputeTechLevel, which has side effects (xenotypeFilter
            // FinalizeInit) that depend on deferred initialization.
            if (fromLoad && _techLevel > TechLevel.Undefined)
            {
                Faction playerColonyfaction = FactionCache.PlayerColonyFaction;
                if (playerColonyfaction != null && playerColonyfaction.def.techLevel < _techLevel)
                {
                    UpdateFactionDef(_techLevel, ref playerColonyfaction);
                }
            }

            // Initialize xenotype filter
            // The xenotype filter isn't properly loaded until after this function is called, so we don't *actually* want to finalize it yet.
            //   Only finalize it if it doesn't even exist
            if (xenotypeFilter == null)
            {
                LogUtil.Warning("Null xenotypeFilter detected - Creating new one");
                xenotypeFilter = new XenotypeFilter(this);
                xenotypeFilter.FinalizeInit(this);
            }

            // Rebuilt on each load from DefDatabase — intentional, ensures defs stay in sync
            factionResources.Clear();
            foreach (ResourceTypeDef resourceTypeDef in DefDatabase<ResourceTypeDef>.AllDefs)
            {
                factionResources.Add(new ResourceDisplay(resourceTypeDef));
                LogUtil.Message($"Added ResourceDisplay for resourceTypeDef {resourceTypeDef} to FactionFC.factionResources");
            }
            factionResources.Sort(ResourceDisplay.SortForUI);

            LifecycleRegistry.Register(this);

            if (fromLoad)
            {
                // Reapply active event stat modifiers to settlements.
                // Events live on FactionFC, so they aren't available during individual settlement PostLoadInit.
                foreach (FCEvent evt in events)
                {
                    if (evt?.def == null || evt.def.statModifiers == null) continue;
                    string sourceId = "event_" + evt.def.defName;
                    if (evt.settlementTraitLocations.Any())
                    {
                        foreach (WorldSettlementFC location in evt.settlementTraitLocations)
                        {
                            if (location != null)
                                location.AddStatModifiers(evt.def.statModifiers, sourceId, evt.def.label);
                        }
                    }
                    else
                    {
                        foreach (WorldSettlementFC settlement in settlements)
                        {
                            settlement.AddStatModifiers(evt.def.statModifiers, sourceId, evt.def.label);
                        }
                    }
                }

                // addStatModifiers already calls InvalidateStatCache -> DirtyStatsCache,
                // so values will recompute lazily on next access
            }
        }

        #endregion

        #region Tick Loop

        public override void WorldComponentTick()
        {
            base.WorldComponentTick();
            PerfWatchdog.Pulse();
            Faction faction = FactionCache.PlayerColonyFaction;
            if (firstTick)
            {
                roadBuilder.FirstTick();

                if (!(faction is null))
                {
                    _ = techLevel;
                    factionIcon = TexLoad.factionIcons.FirstOrFallback(obj => obj.name == factionIconPath,
                        TexLoad.factionIcons.First());
                    UpdateFactionIcon(ref faction, "FactionIcons/" + factionIcon.name);
                    factionIconPath = factionIcon.name;

                    if (!name.NullOrEmpty() && faction.Name != name)
                    {
                        faction.Name = name;
                    }
                }

                militaryCustomizationUtil.CheckMilitaryUtilForErrors();

                // Auto-open patch notes if a new version exceeds the player's threshold
                if (FCSettings.patchNoteAutoOpenThreshold != PatchNoteType.Undefined)
                {
                    PatchNoteDef latest = PatchNoteDef.GetLatestForMod("matathias.empire");
                    if (latest != null
                        && latest.IsNewerThan(FCSettings.lastSeenVersionMajor, FCSettings.lastSeenVersionMinor, FCSettings.lastSeenVersionPatch)
                        && latest.GetPatchNoteType >= FCSettings.patchNoteAutoOpenThreshold)
                    {
                        Find.WindowStack.Add(new PatchNotesDisplayWindow());
                    }
                }

                /* Get the longlat of the player's starting location. This will be used when calculating founding dates. */
                Map playerHome = Find.AnyPlayerHomeMap;
                if (playerHome is null)
                {
                    LogUtil.Warning("Found NULL for player map on first tick. This probably shouldn't happen...");
                    startingLongLat = default(Vector2);
                }
                else
                {
                    startingLongLat = Find.WorldGrid.LongLatOf(playerHome.Tile);
                }

                if (FCSettings.PerformanceLogging)
                    PerfWatchdog.SetEnabled(true);

                firstTick = false;
            }

            long _t;

            _t = PerfWatchdog.EnterTimed("FCEventMaker.ProcessEvents");
            FCEventMaker.ProcessEvents(in events);
            PerfWatchdog.ExitTimed("FCEventMaker.ProcessEvents", _t);

            _t = PerfWatchdog.EnterTimed("BillUtility.ProcessBills");
            BillUtility.ProcessBills();
            PerfWatchdog.ExitTimed("BillUtility.ProcessBills", _t);

            _t = PerfWatchdog.EnterTimed("FireSupportTick");
            FireSupportTick();
            PerfWatchdog.ExitTimed("FireSupportTick", _t);

            /* Check on the leader */
            //This check used to exist in updateTechLevel(), but it doesn't really seem appropriate there. So, moved it here.
            if (Find.TickManager.TicksGame % GenDate.TicksPerDay == 0)
            {
                if (faction != null && (faction.leader == null || faction.leader.Dead))
                {
                    ColonyUtil.CreatePlayerFactionLeader(faction);
                }
            }

            _t = PerfWatchdog.EnterTimed("TaxTick");
            TaxTick(faction);
            PerfWatchdog.ExitTimed("TaxTick", _t);

            _t = PerfWatchdog.EnterTimed("UITick");
            UITick(faction);
            PerfWatchdog.ExitTimed("UITick", _t);

            _t = PerfWatchdog.EnterTimed("StatTick");
            StatTick(faction);
            PerfWatchdog.ExitTimed("StatTick", _t);

            _t = PerfWatchdog.EnterTimed("MilitaryTick");
            MilitaryTick(faction);
            PerfWatchdog.ExitTimed("MilitaryTick", _t);

            if (Find.TickManager.TicksGame % MercenaryHealTickInterval == 0)
            {
                _t = PerfWatchdog.EnterTimed("TickMercenaryHealing");
                militaryCustomizationUtil?.TickMercenaryHealing(MercenaryHealTickInterval);
                PerfWatchdog.ExitTimed("TickMercenaryHealing", _t);
            }

            _t = PerfWatchdog.EnterTimed("ThreatAdaptation.Tick");
            threatAdaptation.Tick();
            PerfWatchdog.ExitTimed("ThreatAdaptation.Tick", _t);

            if (pendingEdictActivations.Count > 0 && Find.TickManager.TicksGame % 250 == 0)
            {
                _t = PerfWatchdog.EnterTimed("CheckEdictActivations");
                CheckEdictActivations();
                PerfWatchdog.ExitTimed("CheckEdictActivations", _t);
            }
            if (!(faction is null))
            {
                _t = PerfWatchdog.EnterTimed("RoadTick");
                roadBuilder.RoadTick();
                PerfWatchdog.ExitTimed("RoadTick", _t);

                _t = PerfWatchdog.EnterTimed("TickActions");
                TickActions();
                PerfWatchdog.ExitTimed("TickActions", _t);
            }
        }

        public void TaxTick(Faction faction)
        {
            if (faction == null || Find.TickManager.TicksGame < taxTimeDue)
                return;

            AddTax();
            taxTimeDue = Find.TickManager.TicksGame + FCSettings.timeBetweenTaxes;

            if (autoResolveBills)
                PaymentUtil.AutoresolveBills(Bills);
        }

        public void StatTick(Faction faction)
        {
            if (faction == null || Find.TickManager.TicksGame % GenDate.TicksPerDay != 0)
                return;

            UpdateSettlementStats();
            AccumulateDailyProduction();
            DirtyAveragesCache();
            SyncGoodwillWithAverages();
            RelationsUtilFC.ResetPlayerColonyRelations();
            UpdateDailyResourcePools();
            MakeRandomEvent();
        }

        public void MilitaryTick(Faction faction)
        {
            if (Find.TickManager.TicksGame >= militaryTimeDue)
            {
                if (faction != null &&
                    FCSettings.disableHostileMilitaryActions == false &&
                    Find.TickManager.TicksGame > (timeStart + GenDate.TicksPerSeason))
                {
                    //if military actions not disabled or game has not passed through the first season

                    if (settlements.Any() || RaidTargetRegistry.Targets.Count > 0)
                    {
                        // Build weighted target list for settlements
                        List<WorldSettlementFC> settlementTargets = new List<WorldSettlementFC>();
                        foreach (WorldSettlementFC settlement in settlements)
                        {
                            if (settlement.MilitaryComp?.isUnderAttack != true)
                            {
                                int weightValue = GetMilitaryTargetWeight(settlement.settlementMilitaryLevel);
                                for (int k = 0; k < weightValue; k++)
                                {
                                    settlementTargets.Add(settlement);
                                }
                            }
                        }

                        // Build weighted target list for external raid targets
                        List<IRaidTarget> externalTargets = new List<IRaidTarget>();
                        foreach (IRaidTarget raidTarget in RaidTargetRegistry.Targets)
                        {
                            if (!raidTarget.IsUnderAttack)
                            {
                                int weightValue = GetMilitaryTargetWeight(raidTarget.MilitaryLevel);
                                for (int k = 0; k < weightValue; k++)
                                {
                                    externalTargets.Add(raidTarget);
                                }
                            }
                        }

                        int totalWeight = settlementTargets.Count + externalTargets.Count;
                        if (totalWeight > 0)
                        {
                            double etl = ThreatScalingUtil.ComputeEmpireThreatLevel(this);
                            Faction enemy = ThreatScalingUtil.PickWeightedEnemyFaction(etl);
                            if (enemy != null)
                            {
                                int roll = Rand.Range(0, totalWeight);
                                if (roll < settlementTargets.Count)
                                {
                                    WorldSettlementFC settlement = settlementTargets[roll];
                                    MilitaryUtilFC.AttackPlayerSettlement(militaryForce.CreateMilitaryForceFromFaction(enemy, true), settlement, enemy);
                                }
                                else
                                {
                                    IRaidTarget raidTarget = externalTargets[roll - settlementTargets.Count];
                                    MilitaryUtilFC.AttackRaidTarget(militaryForce.CreateMilitaryForceFromFaction(enemy, true), raidTarget, enemy);
                                }
                            }
                        }
                    }
                }

                militaryTimeDue = Find.TickManager.TicksGame + (GenDate.TicksPerDay * ThreatScalingUtil.ComputeScaledAttackInterval(this));
            }
        }

        private static int GetMilitaryTargetWeight(int militaryLevel)
        {
            switch (militaryLevel)
            {
                case 0:
                case 1:
                    return 10;
                case 2:
                case 3:
                    return 7;
                case 4:
                case 5:
                    return 3;
                default:
                    return 1;
            }
        }

        public void UITick(Faction faction)
        {
            if (uiTimeUpdate <= 0) //update per time?
            {
                uiTimeUpdate = FCSettings.updateUiTimer;

                if (faction != null)
                {
                    //already built in ui update -.-
                    Find.WindowStack.WindowsUpdate();

                    // Profit and averages are lazy-cached — no eager update needed
                }
            }
            else
            {
                uiTimeUpdate -= 1;
            }
        }

        public void FireSupportTick()
        {
            if (militaryCustomizationUtil.fireSupport == null)
            {
                militaryCustomizationUtil.fireSupport = new List<MilitaryFireSupport>();
            }

            //Other functions
            militaryCustomizationUtil.fireSupport.RemoveAll(support => support.ShouldBeOver);
            militaryCustomizationUtil.fireSupport.ForEach(support => support.Process());
        }

        public void TickActions()
        {
            // Dispatch Tick to all active behavior instances
            ForEachBehavior(b => b.Tick(this));
        }

        #endregion

        #region Lazy Caches

        public void DirtyFactionProfitCache()
        {
            dirtyFactionProfitCache = true;
        }

        private void RecomputeTotalProfit()
        {
            _income = settlements.Sum(s => s.totalIncome);
            _upkeep = settlements.Sum(s => s.totalUpkeep) + GetEdictUpkeep();
            _profit = _income - _upkeep;
            dirtyFactionProfitCache = false;
        }

        public void DirtyAveragesCache()
        {
            dirtyAveragesCache = true;
        }

        private void RecomputeAverages()
        {
            double avgHappiness = 0;
            double avgLoyalty = 0;
            double avgUnrest = 0;
            double avgProsperity = 0;

            if (settlements.Count > 0)
            {
                foreach (WorldSettlementFC settlement in settlements)
                {
                    avgHappiness += settlement.happiness;
                    avgLoyalty += settlement.loyalty;
                    avgUnrest += settlement.unrest;
                    avgProsperity += settlement.prosperity;
                }

                avgHappiness /= settlements.Count;
                avgLoyalty /= settlements.Count;
                avgUnrest /= settlements.Count;
                avgProsperity /= settlements.Count;
            }

            _averageHappiness = avgHappiness;
            _averageLoyalty = avgLoyalty;
            _averageUnrest = avgUnrest;
            _averageProsperity = avgProsperity;
            dirtyAveragesCache = false;
        }

        public void DirtyTechLevelCache()
        {
            dirtyTechLevelCache = true;
        }

        private void RecomputeTechLevel()
        {
            ResearchManager researchManager = Find.ResearchManager;
            bool medievalOnly = FCSettings.medievalTechOnly;
            TechLevel curTechLevel = _techLevel;

            if (!medievalOnly && FactionCache.TechLevelBarrierUltra != null &&
                researchManager.GetProgress(FactionCache.TechLevelBarrierUltra) == FactionCache.TechLevelBarrierUltra.baseCost && _techLevel < TechLevel.Ultra)
            {
                _techLevel = TechLevel.Ultra;
                LogUtil.Message("updateTechLevel: Ultra");
            }
            else if (!medievalOnly && FactionCache.TechLevelBarrierSpacer != null &&
                     researchManager.GetProgress(FactionCache.TechLevelBarrierSpacer) == FactionCache.TechLevelBarrierSpacer.baseCost &&
                     _techLevel < TechLevel.Spacer)
            {
                _techLevel = TechLevel.Spacer;
                LogUtil.Message("updateTechLevel: Spacer");
            }
            else if (!medievalOnly && FactionCache.TechLevelBarrierIndustrial != null &&
                     researchManager.GetProgress(FactionCache.TechLevelBarrierIndustrial) == FactionCache.TechLevelBarrierIndustrial.baseCost &&
                     _techLevel < TechLevel.Industrial)
            {
                _techLevel = TechLevel.Industrial;
                LogUtil.Message("updateTechLevel: Industrial");
            }
            else if (FactionCache.TechLevelBarrierMedieval != null &&
                     researchManager.GetProgress(FactionCache.TechLevelBarrierMedieval) == FactionCache.TechLevelBarrierMedieval.baseCost &&
                     _techLevel < TechLevel.Medieval)
            {
                _techLevel = TechLevel.Medieval;
                LogUtil.Message("updateTechLevel: Medieval");
            }
            else
            {
                if (_techLevel < TechLevel.Neolithic)
                {
                    LogUtil.Message("updateTechLevel: Neolithic");
                    _techLevel = TechLevel.Neolithic;
                }
            }

            if (_techLevel != curTechLevel)
            {
                xenotypeFilter.FinalizeInit(this);
                DirtyAllTitheCaches();
            }

            Faction playerColonyfaction = FactionCache.PlayerColonyFaction;
            if (playerColonyfaction != null && playerColonyfaction.def.techLevel < _techLevel)
            {
                LogUtil.Message("Updating Tech Level");
                UpdateFactionDef(_techLevel, ref playerColonyfaction);
            }

            dirtyTechLevelCache = false;
        }

        public void DirtyAllTitheCaches()
        {
            foreach (WorldSettlementFC settlement in settlements)
            {
                foreach (ResourceFC resource in settlement.Resources)
                {
                    resource.SetDirtyRandomTitheCache();
                }
            }
        }

        /// <summary>
        /// Returns a list of *all* things that this faction can produce.
        /// </summary>
        public List<ThingDef> GetGrandThingList()
        {
            if (DirtyGrandThingListFlag)
            {
                grandThingList = new List<ThingDef>();
                foreach (WorldSettlementFC settlement in settlements)
                {
                    grandThingList.AddRange(settlement.GetGrandThingList());
                }
                grandThingList = grandThingList.Distinct().ToList();
                DirtyGrandThingListFlag = false;
            }
            return grandThingList;
        }

        public void DirtyGrandThingList()
        {
            DirtyGrandThingListFlag = true;
        }

        public List<ThingDef> GetStuffListForThingDef(ThingDef thing)
        {
            return CraftUtil.GetThingStuffs(thing, GetGrandThingList());
        }

        #endregion

        #region Stat System

        /// <summary>
        /// Entry point for stat queries. Combines settlement-level and faction-level cached partials,
        /// then applies uncached behavior ModifyStat adjustments.
        /// </summary>
        public double GetStatValue(FCStatDef stat, WorldSettlementFC settlement = null)
        {
            double value;
            double factionPart = GetFactionStatValue(stat);

            if (settlement != null && stat.appliesToSettlements)
            {
                double settlementPart = settlement.GetSettlementStatValue(stat);
                if (stat.aggregation == FCStatAggregation.Additive)
                    value = settlementPart + factionPart;
                else
                    value = settlementPart * factionPart;
            }
            else
            {
                value = factionPart;
            }

            // Apply runtime-dependent behavior modifiers (uncached — may depend on settlement state)
            foreach (FCPolicyBehavior b in cachedBehaviors)
            {
                try
                {
                    value = b.ModifyStat(stat, value, settlement);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"Behavior ModifyStat error for stat '{stat.defName}': {e}");
                }
            }

            return value;
        }

        /// <summary>
        /// Computes and caches the faction-level stat partial (policies + traits only).
        /// Starts from stat.IdentityValue, applies only faction-level static modifiers.
        /// </summary>
        public double GetFactionStatValue(FCStatDef stat)
        {
            if (cachedFactionStatValues.TryGetValue(stat, out double cached))
                return cached;

            double value = stat.IdentityValue;

            foreach (FCPolicy p in policies)
            {
                if (p?.def == null) continue;
                foreach (FCStatModifier mod in p.def.statModifiers)
                {
                    if (mod.stat == stat)
                    {
                        if (stat.aggregation == FCStatAggregation.Additive)
                            value += mod.value;
                        else
                            value *= mod.value;
                    }
                }
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def == null || p.def == FCPolicyDefOf.empty) continue;
                foreach (FCStatModifier mod in p.def.statModifiers)
                {
                    if (mod.stat == stat)
                    {
                        if (stat.aggregation == FCStatAggregation.Additive)
                            value += mod.value;
                        else
                            value *= mod.value;
                    }
                }
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def == null || !edict.IsFullyActive) continue;
                foreach (FCStatModifier mod in edict.def.statModifiers)
                {
                    if (mod.stat == stat)
                    {
                        if (stat.aggregation == FCStatAggregation.Additive)
                            value += mod.value;
                        else
                            value *= mod.value;
                    }
                }
            }

            cachedFactionStatValues[stat] = value;
            return value;
        }

        /// <summary>
        /// Builds a description string for faction-level stat contributions (policies + traits).
        /// Not cached — only used for UI tooltips.
        /// </summary>
        public string GetFactionStatDesc(FCStatDef stat, bool hardinvert = false)
        {
            string desc = "";
            bool isAdditive = stat.aggregation == FCStatAggregation.Additive;
            bool invert = stat.invertedForDisplay;

            foreach (FCPolicy p in policies)
            {
                if (p?.def == null) continue;
                foreach (FCStatModifier mod in p.def.statModifiers)
                {
                    if (mod.stat != stat) continue;
                    if (isAdditive)
                        desc += TextUtil.ColorizeAdditiveBonus(mod.value, invert: invert, hardinvert: hardinvert) + " - " + p.def.LabelCap + "\n";
                    else
                        desc += TextUtil.ColorizeMultiplierBonus(mod.value, invert: invert) + " - " + p.def.LabelCap + "\n";
                }
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def == null || p.def == FCPolicyDefOf.empty) continue;
                foreach (FCStatModifier mod in p.def.statModifiers)
                {
                    if (mod.stat != stat) continue;
                    if (isAdditive)
                        desc += TextUtil.ColorizeAdditiveBonus(mod.value, invert: invert, hardinvert: hardinvert) + " - " + p.def.LabelCap + "\n";
                    else
                        desc += TextUtil.ColorizeMultiplierBonus(mod.value, invert: invert) + " - " + p.def.LabelCap + "\n";
                }
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def == null || !edict.IsFullyActive) continue;
                foreach (FCStatModifier mod in edict.def.statModifiers)
                {
                    if (mod.stat != stat) continue;
                    if (isAdditive)
                        desc += TextUtil.ColorizeAdditiveBonus(mod.value, invert: invert, hardinvert: hardinvert) + " - " + edict.def.LabelCap + " (" + "FCEdict".Translate() + ")\n";
                    else
                        desc += TextUtil.ColorizeMultiplierBonus(mod.value, invert: invert) + " - " + edict.def.LabelCap + " (" + "FCEdict".Translate() + ")\n";
                }
            }

            return desc;
        }

        /// <summary>
        /// Clears the faction-level stat cache and dirties resource/desc caches on all settlements
        /// (since final combined stat values have changed).
        /// Does NOT clear settlement stat value caches — settlement-level modifiers are unaffected.
        /// </summary>
        public void InvalidateFactionStatCache()
        {
            cachedFactionStatValues.Clear();
            foreach (WorldSettlementFC s in settlements)
            {
                s.InvalidateDescCache();
                s.InvalidateResourceCaches();
            }
        }

        /// <summary>
        /// Invalidates stat and resource caches on all settlements.
        /// Called after faction-wide events (e.g., research completion) that may affect
        /// settlement-level stat or resource production providers.
        /// </summary>
        public void InvalidateAllSettlementStatCaches()
        {
            foreach (WorldSettlementFC s in settlements)
                s.InvalidateStatCache();
        }

        #endregion

        #region Behavior System

        /// <summary>
        /// Rebuilds the cached behavior list from active policies and traits.
        /// Order: policies first (in list order), then traits (in slot order).
        /// This order determines ModifyStat chaining — currently no two behaviors modify the same stat.
        /// </summary>
        public void RebuildBehaviorCache()
        {
            LogUtil.Message($"Rebuilding faction behavior cache");
            _cachedBehaviors = new List<FCPolicyBehavior>();
            foreach (FCPolicy p in policies)
            {
                if (p?.behavior != null)
                    _cachedBehaviors.Add(p.behavior);
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def == null || p.def == FCPolicyDefOf.empty) continue;
                if (p.behavior != null)
                    _cachedBehaviors.Add(p.behavior);
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.behavior != null)
                    _cachedBehaviors.Add(edict.behavior);
            }

            RebuildActionCache();

            // Policy/trait changes affect faction-level stat values and behavior ModifyStat results
            InvalidateFactionStatCache();
        }

        private void RebuildActionCache()
        {
            _cachedBlockedActions = new HashSet<FCActionType>();
            _cachedEnabledActions = new HashSet<FCActionType>();
            _cachedBlockedJobs = new HashSet<MilitaryJobDef>();
            _cachedEnabledJobs = new HashSet<MilitaryJobDef>();
            foreach (FCPolicy p in policies)
            {
                if (p?.def == null) continue;
                if (p.def.blockedActions != null) foreach (var a in p.def.blockedActions) _cachedBlockedActions.Add(a);
                if (p.def.enabledActions != null) foreach (var a in p.def.enabledActions) _cachedEnabledActions.Add(a);
                if (p.def.blockedMilitaryJobs != null) foreach (var j in p.def.blockedMilitaryJobs) _cachedBlockedJobs.Add(j);
                if (p.def.enabledMilitaryJobs != null) foreach (var j in p.def.enabledMilitaryJobs) _cachedEnabledJobs.Add(j);
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def == null || p.def == FCPolicyDefOf.empty) continue;
                if (p.def.blockedActions != null) foreach (var a in p.def.blockedActions) _cachedBlockedActions.Add(a);
                if (p.def.enabledActions != null) foreach (var a in p.def.enabledActions) _cachedEnabledActions.Add(a);
                if (p.def.blockedMilitaryJobs != null) foreach (var j in p.def.blockedMilitaryJobs) _cachedBlockedJobs.Add(j);
                if (p.def.enabledMilitaryJobs != null) foreach (var j in p.def.enabledMilitaryJobs) _cachedEnabledJobs.Add(j);
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def == null || !edict.IsFullyActive) continue;
                if (edict.def.blockedActions != null) foreach (var a in edict.def.blockedActions) _cachedBlockedActions.Add(a);
                if (edict.def.enabledActions != null) foreach (var a in edict.def.enabledActions) _cachedEnabledActions.Add(a);
                if (edict.def.blockedMilitaryJobs != null) foreach (var j in edict.def.blockedMilitaryJobs) _cachedBlockedJobs.Add(j);
                if (edict.def.enabledMilitaryJobs != null) foreach (var j in edict.def.enabledMilitaryJobs) _cachedEnabledJobs.Add(j);
            }
            _cachedEnabledActions.ExceptWith(_cachedBlockedActions);
            _cachedEnabledJobs.ExceptWith(_cachedBlockedJobs);
        }

        public void ForEachBehavior(Action<FCPolicyBehavior> action)
        {
            foreach (FCPolicyBehavior b in cachedBehaviors)
            {
                try
                {
                    action(b);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"Policy behavior error: {e}");
                }
            }
        }

        public T FoldBehaviors<T>(T seed, Func<FCPolicyBehavior, T, T> folder)
        {
            foreach (FCPolicyBehavior b in cachedBehaviors)
            {
                try
                {
                    seed = folder(b, seed);
                }
                catch (Exception e)
                {
                    LogUtil.Error($"Policy behavior fold error: {e}");
                }
            }
            return seed;
        }

        /// <summary>
        /// Calls OnRemoved on all behaviors in the given policy list, then clears it.
        /// Use this instead of directly clearing/replacing policy lists.
        /// </summary>
        public void RemoveAllPolicies(List<FCPolicy> policyList)
        {
            foreach (FCPolicy p in policyList)
            {
                if (p?.behavior != null)
                {
                    try { p.behavior.OnRemoved(this); }
                    catch (Exception e) { LogUtil.Error($"FCPolicyBehavior.OnRemoved error for '{p.def?.defName}': {e}"); }
                }
            }
            policyList.Clear();
        }

        #endregion

        #region Policy & Action Checks

        public bool HasPolicy(FCPolicyDef def)
        {
            //Don't game the system
            if (policies.Count < 2)
            {
                return false;
            }

            foreach (FCPolicy policy in policies)
            {
                if (policy.def == def)
                    return true;
            }

            return false;
        }

        public bool HasTrait(FCPolicyDef def)
        {
            foreach (FCPolicy trait in factionTraits)
            {
                if (trait.def == def)
                    return true;
            }

            return false;
        }

        #region Edicts

        public bool IsEdictCategoryUnlocked(FCPolicyCategory category)
        {
            int required;
            if (!EdictCategoryUnlockLevels.TryGetValue(category, out required))
                return false;
            return factionLevel >= required;
        }

        public FCPolicy GetActiveEdict(FCPolicyCategory category)
        {
            FCPolicy edict;
            edicts.TryGetValue(category, out edict);
            return edict;
        }

        public bool HasEdict(FCPolicyDef def)
        {
            FCPolicy edict;
            if (!edicts.TryGetValue(def.category, out edict)) return false;
            return edict.def == def;
        }

        public void EnactEdict(FCPolicyDef def)
        {
            if (!def.IsEdict)
            {
                LogUtil.Error($"EnactEdict called on non-edict def '{def.defName}'");
                return;
            }
            if (!IsEdictCategoryUnlocked(def.category))
            {
                Messages.Message("FCEdictCategoryLocked".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            if (def.factionLevelRequirement > 0 && factionLevel < def.factionLevelRequirement)
            {
                Messages.Message("FCEdictLevelRequired".Translate(def.factionLevelRequirement), MessageTypeDefOf.RejectInput);
                return;
            }

            // Check incompatibility with active core policies and traits
            if (!def.incompatiblePolicies.NullOrEmpty())
            {
                foreach (FCPolicyDef blocked in def.incompatiblePolicies)
                {
                    if (HasPolicy(blocked) || HasTrait(blocked))
                    {
                        Messages.Message("FCEdictIncompatible".Translate(def.LabelCap, blocked.LabelCap), MessageTypeDefOf.RejectInput);
                        return;
                    }
                }
            }

            // Check policy prerequisites
            string failReason;
            if (!def.MeetsPolicyRequirements(this, out failReason))
            {
                Messages.Message(failReason, MessageTypeDefOf.RejectInput);
                return;
            }

            // Revoke existing edict in this category (if any)
            RevokeEdict(def.category, silent: true);

            FCPolicy edict = new FCPolicy(def);
            edicts[def.category] = edict;
            if (def.enactDuration > 0)
                pendingEdictActivations.Add(def.category);
            RebuildBehaviorCache();
            DirtyFactionProfitCache();
            Messages.Message("FCEdictEnacted".Translate(def.LabelCap), MessageTypeDefOf.PositiveEvent);
        }

        public void RevokeEdict(FCPolicyCategory category, bool silent = false)
        {
            FCPolicy edict;
            if (!edicts.TryGetValue(category, out edict)) return;

            if (edict.behavior != null)
            {
                try { edict.behavior.OnRemoved(this); }
                catch (Exception e) { LogUtil.Error($"Edict behavior OnRemoved error for '{edict.def?.defName}': {e}"); }
            }

            string label = edict.def?.LabelCap ?? "";
            FCPolicyDef revokedDef = edict.def;
            edicts.Remove(category);
            pendingEdictActivations.Remove(category);
            RebuildBehaviorCache();
            DirtyFactionProfitCache();
            if (!silent)
                Messages.Message("FCEdictRevoked".Translate(label), MessageTypeDefOf.NeutralEvent);

            // Cascade: revoke any active edicts that depended on the one just removed
            if (revokedDef != null)
            {
                List<FCPolicyCategory> toRevoke = new List<FCPolicyCategory>();
                foreach (KeyValuePair<FCPolicyCategory, FCPolicy> kvp in edicts)
                {
                    if (kvp.Value.def.requiredPolicies.NullOrEmpty()) continue;
                    if (!kvp.Value.def.MeetsPolicyRequirements(this, out _))
                        toRevoke.Add(kvp.Key);
                }
                foreach (FCPolicyCategory cat in toRevoke)
                {
                    string depLabel = edicts[cat].def?.LabelCap ?? "";
                    Messages.Message("FCEdictRevokedDependency".Translate(depLabel, label), MessageTypeDefOf.NeutralEvent);
                    RevokeEdict(cat, silent: true);
                }
            }
        }

        public void RevokeAllEdicts()
        {
            // Copy keys to avoid modifying collection during iteration
            List<FCPolicyCategory> categories = new List<FCPolicyCategory>(edicts.Keys);
            foreach (FCPolicyCategory cat in categories)
            {
                RevokeEdict(cat, silent: true);
            }
        }

        public int GetEdictUpkeep()
        {
            int total = 0;
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict.IsFullyActive)
                    total += edict.def.upkeepSilver;
            }
            return total;
        }

        private void CheckEdictActivations()
        {
            bool anyActivated = false;
            List<FCPolicyCategory> toRemove = new List<FCPolicyCategory>();
            foreach (FCPolicyCategory cat in pendingEdictActivations)
            {
                FCPolicy edict;
                if (!edicts.TryGetValue(cat, out edict) || edict.IsFullyActive)
                {
                    toRemove.Add(cat);
                    if (edicts.ContainsKey(cat))
                        anyActivated = true;
                }
            }
            foreach (FCPolicyCategory cat in toRemove)
                pendingEdictActivations.Remove(cat);

            if (anyActivated)
            {
                InvalidateFactionStatCache();
                DirtyFactionProfitCache();
            }
        }

        #endregion

        public bool AnyPolicyBlocks(FCActionType action) => _cachedBlockedActions?.Contains(action) ?? false;
        public bool AnyPolicyEnables(FCActionType action) => _cachedEnabledActions?.Contains(action) ?? false;

        /// <summary>
        /// Unified check: for opt-out actions, returns true unless blocked. For opt-in actions, returns true only if enabled.
        /// </summary>
        public bool IsActionAllowed(FCActionType action)
        {
            if (FCActionTypeUtil.RequiresEnable(action))
                return AnyPolicyEnables(action);
            return !AnyPolicyBlocks(action);
        }

        /// <summary>
        /// Checks if a specific military job is allowed. Respects defaultEnabled on the job def,
        /// plus policy/trait overrides. Does NOT check the DeployMilitary action gate — caller must check that separately.
        /// </summary>
        public bool IsMilitaryJobAllowed(MilitaryJobDef job)
        {
            if (_cachedBlockedJobs != null && _cachedBlockedJobs.Contains(job)) return false;
            if (!job.defaultEnabled)
                return _cachedEnabledJobs != null && _cachedEnabledJobs.Contains(job);
            return true;
        }

        /// <summary>
        /// Returns true if any active policy prevents building destruction on battle loss.
        /// Uses the new data-driven preventBuildingDestruction flag on FCPolicyDef.
        /// </summary>
        public bool AnyPolicyPreventsBuildingDestruction()
        {
            foreach (FCPolicy p in policies)
            {
                if (p?.def != null && p.def.preventBuildingDestruction)
                    return true;
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def == null || p.def == FCPolicyDefOf.empty) continue;
                if (p.def.preventBuildingDestruction)
                    return true;
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def != null && edict.IsFullyActive && edict.def.preventBuildingDestruction)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Returns true if any active policy suppresses member death penalties.
        /// Uses the new data-driven suppressMemberDeathPenalty flag on FCPolicyDef.
        /// </summary>
        public bool AnyPolicySuppressesMemberDeathPenalty()
        {
            foreach (FCPolicy p in policies)
            {
                if (p?.def != null && p.def.suppressMemberDeathPenalty)
                    return true;
            }
            foreach (FCPolicy p in factionTraits)
            {
                if (p?.def == null || p.def == FCPolicyDefOf.empty) continue;
                if (p.def.suppressMemberDeathPenalty)
                    return true;
            }
            foreach (FCPolicy edict in edicts.Values)
            {
                if (edict?.def != null && edict.IsFullyActive && edict.def.suppressMemberDeathPenalty)
                    return true;
            }
            return false;
        }

        #endregion

        #region Lifecycle Dispatch

        // Bridges registry dispatch to policy behaviors so ColonyUtil only needs one call path.

        void ILifecycleParticipant.OnSettlementCreated(WorldSettlementFC settlement)
        {
            ForEachBehavior(b => b.OnSettlementCreated(this, settlement));
        }

        void ILifecycleParticipant.OnSettlementRemoved(WorldSettlementFC settlement)
        {
            ForEachBehavior(b => b.OnSettlementRemoved(this, settlement));
        }

        void ILifecycleParticipant.OnSettlementUpgraded(WorldSettlementFC settlement, int oldLevel, int newLevel)
        {
            ForEachBehavior(b => b.OnSettlementUpgraded(this, settlement, newLevel));
        }

        void ILifecycleParticipant.OnSettlementTypeChanged(WorldSettlementFC settlement, WorldSettlementDef oldDef, WorldSettlementDef newDef)
        {
            ForEachBehavior(b => b.OnSettlementTypeChanged(this, settlement, oldDef, newDef));
        }

        void ILifecycleParticipant.OnBuildingConstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot)
        {
            ForEachBehavior(b => b.OnBuildingConstructed(this, settlement, building, slot));
        }

        void ILifecycleParticipant.OnBuildingDeconstructed(WorldSettlementFC settlement, BuildingFCDef building, int slot)
        {
            ForEachBehavior(b => b.OnBuildingDeconstructed(this, settlement, building, slot));
        }

        void ILifecycleParticipant.OnSquadDeployed(WorldSettlementFC settlement, MilitaryJobDef job, bool isExtraSquad)
        {
            ForEachBehavior(b => b.OnSquadDeployed(this, settlement, isExtraSquad));
        }

        void ILifecycleParticipant.OnSquadRecalled(WorldSettlementFC settlement)
        {
            ForEachBehavior(b => b.OnSquadRecalled(this, settlement));
        }

        void ILifecycleParticipant.OnBattleResolved(WorldSettlementFC settlement, MilitaryJobDef job, bool victory, BattleResult result)
        {
            ForEachBehavior(b => b.OnBattleResolved(this, settlement, job, victory, result));
        }

        void ILifecycleParticipant.OnResearchCompleted(ResearchProjectDef project)
        {
            ForEachBehavior(b => b.OnResearchCompleted(this, project));
        }

        #endregion

        #region Settlement Management

        public void AddSettlement(WorldSettlementFC settlement)
        {
            settlements.Add(settlement);
            DirtyFactionProfitCache();
            DirtyAveragesCache();
        }

        public WorldSettlementFC ReturnSettlementByLocation(PlanetTile location)
        {
            for (int i = 0; i < settlements.Count; i++)
            {
                if (settlements[i].Tile == location)
                {
                    return settlements[i];
                }
            }

            return null;
        }

        public string GetSettlementName(PlanetTile location)
        {
            return ReturnSettlementByLocation(location)?.Name ?? "Null";
        }

        public void UpdateSettlementStats()
        {
            foreach (WorldSettlementFC settlement in settlements)
            {
                settlement.UpdateHappiness();
                settlement.UpdateLoyalty();
                settlement.UpdateUnrest();
                settlement.UpdateProsperity();
            }
        }

        public int ReturnHighestMilitaryLevel()
        {
            int max = 1;
            foreach (WorldSettlementFC settlement in settlements)
            {
                max = Math.Max(max, settlement.settlementMilitaryLevel);
            }

            return max;
        }

        #endregion

        #region Tax & Billing

        public void SetStartTime()
        {
            taxTimeDue = Find.TickManager.TicksGame + FCSettings.timeBetweenTaxes;
        }

        public void AddTax()
        {
            TaxTickRegistry.InvokePreTaxResolution(this);
            foreach (ResourcePool pool in resourcePools)
            {
                if (pool.resource.PoolResourceResetsAtTaxTime())
                {
                    pool.pool = 0;
                }
            }

            if (settlements.Count != 0)
            {
                foreach (WorldSettlementFC settlement in settlements)
                {
                    AddExperienceToFactionLevel(2f);

                    List<Thing> list = new List<Thing>();
                    int silverAmount = 0;
                    list = settlement.CreateTax(out silverAmount);
                    List<ResourcePool> resourcePools = settlement.CreateResourcePools();

                    BillFC bill = new BillFC(settlement);
                    bill.taxes.resourcePools = resourcePools;
                    bill.taxes.itemTithes.AddRange(list);
                    bill.taxes.silverAmount = silverAmount;

                    Bills.Add(bill);

                    TextUtil.GetTownTitle(settlement);
                    TaxTickPrisoner(settlement);
                    ForEachBehavior(b => b.OnTaxCollected(this, settlement));
                }

                Find.LetterStack.ReceiveLetter("TaxesBilledShort".Translate(), "TaxesBilledDesc".Translate(),
                    LetterDefOf.PositiveEvent);
                DirtyFactionProfitCache();
            }
            else
            {
                Messages.Message("NoSettlementsToTax".Translate(), MessageTypeDefOf.NeutralEvent);
            }

            // Deduct edict upkeep
            int edictUpkeep = GetEdictUpkeep();
            if (edictUpkeep > 0)
            {
                if (PaymentUtil.GetSilver() >= edictUpkeep)
                {
                    PaymentUtil.PaySilver(edictUpkeep, "EdictUpkeep");
                }
                else
                {
                    RevokeAllEdicts();
                    Messages.Message("FCEdictUpkeepUnpaid".Translate(), MessageTypeDefOf.NegativeEvent);
                }
            }

            TaxTickRegistry.InvokePostTaxResolution(this);
        }

        public void TaxTickPrisoner(WorldSettlementFC settlement)
        {
            int i = 0;
            while (i < settlement.prisonerList.Count)
            {
                FCPrisoner prisoner = settlement.prisonerList[i];
                bool dead = false;

                switch (prisoner.workload)
                {
                    case FCWorkLoad.Heavy:
                        if (prisoner.AdjustHealth(-20))
                            dead = true;
                        break;
                    case FCWorkLoad.Medium:
                        if (prisoner.AdjustHealth(-10))
                            dead = true;
                        break;
                    case FCWorkLoad.Light:
                        if (prisoner.AdjustHealth(4))
                            dead = true;
                        break;
                }

                /* Only increment if the prisoner hasn't died.
                 * If they *did* die, then AdjustHealth() will have removed them from the list already. So if we increment, then we'll actually skip the next prisoner. */
                if (!dead) i++;
            }
        }

        // resetTraitMercantileCaravanTime removed — mercantile caravan scheduling
        // is now handled by FCPolicyBehavior_Mercantile.Tick/OnEnacted.

        public double GetTotalIncome() => income;
        public double GetTotalUpkeep() => upkeep;
        public double GetTotalProfit() => profit;

        #endregion

        #region Events

        public void AddEvent(FCEvent fcevent)
        {
            if (fcevent == null) return;

            if (fcevent.goods != null && fcevent.goods.Count > 0)
            {
                fcevent.goods = FCEvent.ConsolidateGoods(fcevent.goods);
            }

            //Add event to events
            events.Add(fcevent);

            LogUtil.Message($"AddEvent: adding new fcevent {fcevent.def.defName}");

            string sourceId = "event_" + fcevent.def.defName;

            //check if event has a location, if does, add stat modifiers to that specific location;
            if (fcevent.settlementTraitLocations.Count() > 0) //if has specific locations
            {
                foreach (WorldSettlementFC location in fcevent.settlementTraitLocations)
                {
                    location.AddStatModifiers(fcevent.def.statModifiers, sourceId, fcevent.def.label);
                }
            }
            else
            {
                //if no specific location then faction wide — apply to all settlements
                foreach (WorldSettlementFC settlement in settlements)
                {
                    settlement.AddStatModifiers(fcevent.def.statModifiers, sourceId, fcevent.def.label);
                }
            }
        }

        private void MakeRandomEvent()
        {
            if (RandomEventsDisabledOrNoSettlements()) return;

            if (CanMakeRandomEventNow())
            {
                FCEvent tmpEvt = FCEventMaker.MakeRandomEvent(FCEventMaker.ReturnRandomEvent(), null);
                if (tmpEvt != null)
                {
                    FactionCache.FactionComp.AddEvent(tmpEvt);
                    randomEventLastAdded = 0f;

                    //letter code
                    string settlementString = tmpEvt.settlementTraitLocations.Join((settlement) => $" {settlement.Name}", "\n");

                    if (!settlementString.NullOrEmpty())
                    {
                        Find.LetterStack.ReceiveLetter("Random Event", $"{tmpEvt.def.desc}\n{"EventAffectingSettlements".Translate()}\n{settlementString}", LetterDefOf.NeutralEvent);
                    }
                    else
                    {
                        Find.LetterStack.ReceiveLetter("Random Event", tmpEvt.def.desc,
                            LetterDefOf.NeutralEvent);
                    }
                }
                else
                {
                    randomEventLastAdded += 1f;
                }
            }
            else
            {
                randomEventLastAdded += 1f;
            }

        }

        private bool CanMakeRandomEventNow()
        {
            if ((FCSettings.maxDaysTillRandomEvent - FCSettings.minDaysTillRandomEvent) == 0)
            {
                return randomEventLastAdded >= FCSettings.minDaysTillRandomEvent;
            }
            else
            {
                return Rand.Chance((randomEventLastAdded - FCSettings.minDaysTillRandomEvent) / (FCSettings.maxDaysTillRandomEvent - FCSettings.minDaysTillRandomEvent));
            }
        }

        private bool RandomEventsDisabledOrNoSettlements() => FactionCache.FactionComp.settlements.Count == 0 || FCSettings.disableRandomEvents;

        #endregion

        #region Resources

        public void AddResourcePool(ResourcePool pool)
        {
            if (pool == null)
            {
                return;
            }
            else if (pool.pool == 0)
            {
                return;
            }
            /* If the pool wants to do any pre-adding-to-global-pool shenanigans, let it do so now. */
            pool.pool = pool.resource.PreAddToGlobalPool(pool.pool);

            ResourcePool rpool = resourcePools.Find((ResourcePool p) => p.resource == pool.resource);
            if (rpool != null)
            {
                rpool.pool += pool.pool;
            }
            else
            {
                resourcePools.Add(pool);
            }
            pool.resource.AddedToGlobalPool(pool.pool);
        }
        public void AddResourcePools(List<ResourcePool> pools)
        {
            foreach (ResourcePool pool in pools)
            {
                AddResourcePool(pool);
            }
        }
        public double GetResourcePoolValue(ResourceTypeDef res)
        {
            ResourcePool rpool = resourcePools.Find((ResourcePool p) => p.resource == res);
            if (rpool == null)
            {
                LogUtil.Warning($"Tried to get resource pool value for ResourceTypeDef {res}, but there was no faction resource pool");
                return 0;
            }
            return rpool.pool;
        }

        public IEnumerable<FloatMenuOption> GetFactionMenuResourcePoolFloatMenuOptions()
        {
            foreach (ResourcePool pool in resourcePools)
            {
                IEnumerable<FloatMenuOption> options = pool.resource.GetFactionMenuFloatMenuOptions(pool);
                if (options != null)
                {
                    foreach (FloatMenuOption option in options)
                    {
                        yield return option;
                    }
                }
            }
        }
        private void AccumulateDailyProduction()
        {
            foreach (WorldSettlementFC settlement in settlements)
            {
                settlement.AccumulateDailyProduction();
            }
        }

        public void UpdateDailyResourcePools()
        {
            foreach (ResourcePool pool in resourcePools)
            {
                LogUtil.Message($"Daily ResourcePool update for resourceTypeDef {pool.resource.defName}. Pool size: {pool.pool}");
                pool.resource.DailyUpdate(pool);
                LogUtil.Message($"Post-Daily ResourcePool update for resourceTypeDef {pool.resource.defName}. New Pool size: {pool.pool}");
            }
        }

        public ResourceDisplay ReturnResource(ResourceTypeDef resourceTypeDef)
        {
            ResourceDisplay res = factionResources.Find((ResourceDisplay rfc) => rfc.resourceDef == resourceTypeDef);
            if (res == null)
            {
                /* This should never happen! */
                LogUtil.Error($"Requested resource {resourceTypeDef.defName} is not in the list of faction resources!");
            }
            return res;
        }

        public void SetDirtyResourceDisplayCache(ResourceTypeDef rdef)
        {
            ResourceDisplay rdisplay = factionResources.Find((ResourceDisplay rd) => rd.resourceDef == rdef);
            if (rdisplay != null)
            {
                rdisplay.SetDirtyCache();
            }
        }

        #endregion

        #region ID Generation

        public int GetNextSettlementFCID()
        {
            nextSettlementFCID++;
            return nextSettlementFCID;
        }

        public int GetNextMercenaryID()
        {
            nextMercenaryID++;
            return nextMercenaryID;
        }

        public int GetNextMilitaryFireSupportID()
        {
            nextMilitaryFireSupportID++;
            return nextMilitaryFireSupportID;
        }

        public int GetNextMercenarySquadID()
        {
            nextMercenarySquadID++;
            return nextMercenarySquadID;
        }

        public int GetNextTaxID()
        {
            nextTaxID++;
            return nextTaxID;
        }

        public int GetNextEventID()
        {
            nextEventID++;
            return nextEventID;
        }

        public int GetNextBillID()
        {
            nextBillID++;
            return nextBillID;
        }

        public int GetNextPrisonerID()
        {
            nextPrisonerID++;
            return nextPrisonerID;
        }

        #endregion

        #region Leveling

        public float UpdateFactionLevelGoalXP(int currentLevel)
        {
            return SettlementFormulas.CalculateFactionLevelGoalXP(currentLevel);
        }

        public bool AddExperienceToFactionLevel(float xp)
        {
            bool leveled = false;
            factionXPCurrent += xp;

            while (factionXPCurrent >= factionXPGoal)
            {
                factionXPCurrent -= factionXPGoal;
                factionLevel += 1;
                Find.LetterStack.ReceiveLetter("FCFactionLevelUp".Translate(),
                    "FCFactionLevelUpDesc".Translate(name, factionLevel), LetterDefOf.PositiveEvent);
                leveled = true;
                factionXPGoal = UpdateFactionLevelGoalXP(factionLevel);
            }

            return leveled;
        }

        #endregion

        #region Capital Management

        public void SetCapital()
        {
            // Check if there's an active capital spot first
            Building_CapitalSpot activeCapitalSpot = GetActiveCapitalSpot();
            if (activeCapitalSpot != null)
            {
                Messages.Message(
                    "FCCapitalAlreadyEstablished".Translate(activeCapitalSpot.Map.Parent.LabelCap),
                    MessageTypeDefOf.RejectInput
                );
                return;
            }

            if (Find.CurrentMap != null && Find.CurrentMap.IsPlayerHome)
            {
                capitalLocation = Find.CurrentMap.Parent.Tile;

                Messages.Message("SetAsFactionCapital".Translate(Find.CurrentMap.Parent.LabelCap),
                    MessageTypeDefOf.NeutralEvent);
            }
            else
            {
                Messages.Message(
                    "FCUnableToSetCapitalHere".Translate(),
                    MessageTypeDefOf.NegativeEvent);
            }
        }

        public bool HasActiveCapitalSpot()
        {
            return !(GetActiveCapitalSpot() is null);
        }

        public Building_CapitalSpot GetActiveCapitalSpot()
        {
            foreach (Map map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;

                foreach (Building building in map.listerBuildings.allBuildingsColonist)
                {
                    if (building is Building_CapitalSpot capitalSpot && capitalSpot.IsActiveCapitalSpot)
                    {
                        return capitalSpot;
                    }
                }
            }
            return null;
        }

        public Map ReturnCapitalMap()
        {
            for (int i = 0; i < Find.Maps.Count; i++)
            {
                if (Find.Maps[i].Tile == capitalLocation)
                {
                    return Find.Maps[i];
                }
            }

            LogUtil.Message("CouldNotFindMapOfCapital".Translate());
            return null;
        }

        #endregion

        #region Faction Definition

        //TODO: this whole function is playing with defs. Doesn't seem great. Not sure if there's another way to set icons, though. Need to investigate
        public void UpdateFactionIcon(ref Faction faction, string iconPath)
        {
            LogUtil.Message("Updated Icon - " + iconPath);
            if (faction?.def != null)
            {
                faction.def.factionIconPath = iconPath;
            }
            if (settlements.Any() && settlements[0]?.def != null && UnityData.IsInMainThread)
            {
                //TODO: not sure if this will interact wierdly with the new SettlementDef. Keep an eye on this
                WorldSettlementFC.traitCachedIcon.SetValue(settlements[0].def, ContentFinder<Texture2D>.Get(iconPath));
            }

            foreach (WorldSettlementFC settlement in settlements)
            {
                if (settlement?.def != null)
                {
                    settlement.def.expandingIconTexture = iconPath;
                }
                if (settlement?.Faction?.def != null)
                {
                    settlement.Faction.def.factionIconPath = iconPath;
                }
            }
        }

        public void UpdateFactionDef(TechLevel tech, ref Faction faction)
        {
            FactionDef replacingDef;
            ThingFilter apparelStuffFilter = new ThingFilter();
            FactionDef def = faction.def;

            switch (tech)
            {
                case TechLevel.Archotech:
                case TechLevel.Ultra:
                case TechLevel.Spacer:
                    replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("OutlanderCivil");

                    break;
                case TechLevel.Industrial:
                    replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("OutlanderCivil");
                    break;
                case TechLevel.Medieval:
                    if (FCSettings.IsModLoaded("OskarPotocki.VanillaFactionsExpanded.MedievalModule"))
                    {
                        replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("VFEM_KingdomCivil");
                    }
                    else
                    {
                        replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("TribeCivil");
                    }

                    break;
                default:
                    replacingDef = DefDatabase<FactionDef>.GetNamedSilentFail("TribeCivil");
                    break;
            }
            def.caravanTraderKinds = replacingDef.caravanTraderKinds;
            if (replacingDef.backstoryFilters != null && replacingDef.backstoryFilters.Count != 0)
                def.backstoryFilters = replacingDef.backstoryFilters;
            def.techLevel = tech;
            def.basicMemberKind = replacingDef.basicMemberKind;
            def.visitorTraderKinds = replacingDef.visitorTraderKinds;
            def.baseTraderKinds = replacingDef.baseTraderKinds;
            if (replacingDef.apparelStuffFilter != null)
                def.apparelStuffFilter = replacingDef.apparelStuffFilter;


            if (tech >= TechLevel.Spacer && def.apparelStuffFilter != null)
            {
                def.apparelStuffFilter.SetAllow(DefDatabase<StuffCategoryDef>.GetNamedSilentFail("Synthread"), true);
                def.apparelStuffFilter.SetAllow(DefDatabase<StuffCategoryDef>.GetNamedSilentFail("Hyperweave"), true);
                def.apparelStuffFilter.SetAllow(DefDatabase<StuffCategoryDef>.GetNamedSilentFail("Plasteel"), true);
            }
            UpdateFactionIcon(ref faction, "FactionIcons/" + factionIconPath);

            LogUtil.Message("FactionFC.UpdateFactionDef - Completed tech update");
        }

        public string ReturnNextTechToLevel()
        {
            switch (techLevel)
            {
                case TechLevel.Ultra:
                    return "ReachedMaxLevel".Translate();
                case TechLevel.Spacer:
                    return "FCShipBasics".Translate();
                case TechLevel.Industrial:
                    return "FCFabrication".Translate();
                case TechLevel.Medieval:
                    return "FCElectricity".Translate();
                case TechLevel.Neolithic:
                    return "FCSmithing".Translate();
                default:
                    return "N/A";
            }
        }

        #endregion

        #region Misc

        public void SetName(string name)
        {
            this.name = name;
        }

        public void GainHappiness(double amount)
        {
            foreach (WorldSettlementFC settlement in settlements)
            {
                settlement.GainHappiness(amount);
            }
        }

        public void GainUnrestForReason(Message msg, double amount)
        {
            Messages.Message(msg);
            foreach (WorldSettlementFC settlement in settlements)
            {
                settlement.GainUnrest(amount);
            }
        }

        public bool SendDiplomaticEnvoy(Faction faction)
        {
            if (faction.def.permanentEnemy)
            {
                Messages.Message("FCCannotImproveRelationsWithType".Translate(), MessageTypeDefOf.RejectInput);
                return false;
            }

            // Try new behavior system first
            bool handled = false;
            ForEachBehavior(b =>
            {
                if (!handled)
                    handled = b.HandleDiplomaticEnvoy(this, faction);
            });
            return handled;
        }

        /// <summary>
        /// Syncs faction goodwill with average happiness. Should only be called from StatTick (daily).
        /// </summary>
        private void SyncGoodwillWithAverages()
        {
            if (settlements.Any() && FactionCache.PlayerColonyFaction != null)
            {
                FactionCache.PlayerColonyFaction.TryAffectGoodwillWith(Find.FactionManager.OfPlayer,
                    (Convert.ToInt32(averageHappiness) - FactionCache.PlayerColonyFaction.PlayerGoodwill));
            }
        }

        public bool CheckSettlementCaravansList(PlanetTile location) //list of destinations caravans gone to
        {
            for (int i = 0; i < settlementCaravansList.Count; i++)
            {
                if (location == settlementCaravansList[i] || Find.WorldGrid.IsNeighbor(location, settlementCaravansList[i]))
                {
                    return true; // is on list
                }
            }

            return false; //is not on list
        }

        #endregion
    }
}
