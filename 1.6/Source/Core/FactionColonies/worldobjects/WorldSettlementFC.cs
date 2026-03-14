using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    ///     WorldObject that in many ways re-implements Settlement.cs from Rimworld.Planet. May cause compatibility issues with
    ///     other mods that rely on finding Settlement objects on the world map. Recommend testing this extensively with mods
    ///     like SoS2, RimWar, or any mods that modify, collect, or deep save world objects before publishing changes
    /// </summary>
    public class WorldSettlementFC : Settlement
    {
        private string name;
        private string nameShort;
        private string nameOriginal;
        public string title = "Hamlet".Translate();
        private string _description = "FCGenericError".Translate();
        private bool dirtyDescriptionCache = true;
        public string description
        {
            get
            {
                if (dirtyDescriptionCache) RecomputeDescription();
                return _description;
            }
        }
        private int foundingTick;
        public int FoundingTick => foundingTick;

        /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*
         * ~        Settlement Base Info         ~ *
         *-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/
        public int settlementLevel = 1;

        public int GetBuildingSlots()
        {
            return settlementDef.GetSettlementTypeExtension().GetBuildingSlots(settlementLevel, settlementDef.maxBuildingCount);
        }

        public int GetUpgradeCost(int baseCost)
        {
            return settlementDef.GetSettlementTypeExtension().GetUpgradeCost(settlementLevel, baseCost);
        }

        public int GetUpgradeTime(double buildTimeMult)
        {
            return settlementDef.GetSettlementTypeExtension().GetUpgradeTime(settlementLevel, buildTimeMult);
        }

        /* Workers — lazy-cached, use DirtyStatsCache()/DirtyProfitCache() to invalidate */
        private double _workers;
        private double _workersMax;
        private double _workersUltraMax;
        private double _workerCost;
        private double _workerTotalUpkeep;
        private bool dirtyStatsCache = true;
        private bool dirtyProfitCache = true;

        public double workers { get { if (dirtyProfitCache) RecomputeProfit(); return _workers; } }
        public double workersMax { get { if (dirtyStatsCache) RecomputeStats(); return _workersMax; } }
        public double workersUltraMax { get { if (dirtyStatsCache) RecomputeStats(); return _workersUltraMax; } }
        public double workerCost { get { if (dirtyProfitCache) RecomputeProfit(); return _workerCost; } }
        public double workerTotalUpkeep { get { if (dirtyProfitCache) RecomputeProfit(); return _workerTotalUpkeep; } }
        /* Social Stats */
        public double unrest;
        public double loyalty = 100;
        public double happiness = 100;
        private double _prosperity = 100;
        public double prosperity
        {
            get { return _prosperity; }
            set
            {
                if (_prosperity == value) return;
                _prosperity = value;
                InvalidateResourceCaches();
                DirtyProfitCache();
            }
        }

        /// <summary>
        /// Stat modifiers from buildings, settlement type, and events that apply to this settlement.
        /// Use AddStatModifiers/RemoveStatModifiers to modify.
        /// Each entry tracks the sourceId that added it for removal by source.
        /// </summary>
        private struct TaggedStatModifier
        {
            public string sourceId;
            public string sourceLabel;
            public FCStatModifier mod;
        }
        private List<TaggedStatModifier> statModifiers = new List<TaggedStatModifier>();
        private Dictionary<FCStatDef, double> cachedStatValues = new Dictionary<FCStatDef, double>();
        private Dictionary<FCStatDef, string> cachedStatDescs = new Dictionary<FCStatDef, string>();

        public List<FCPrisoner> prisonerList = new List<FCPrisoner>();

        public float oneTimeSilverIncome;
        public List<Thing> tithe = new List<Thing>();
        public int titheEstimatedIncome;

        public string biome;
        public BiomeResourceDef biomeDef;

        public bool isUpgrading = false;
        public int startUpgradeTick = -1;
        public int finishUpgradeTick = -1;

        //ui only — lazy-cached via dirtyProfitCache
        private double _totalUpkeep;
        private string _upkeepExp = "";
        private double _totalIncome;
        private string _incomeExp = "";
        private double _totalProfit;

        public double totalUpkeep { get { if (dirtyProfitCache) RecomputeProfit(); return _totalUpkeep; } }
        public string upkeepExp { get { if (dirtyProfitCache) RecomputeProfit(); return _upkeepExp; } }
        public double totalIncome { get { if (dirtyProfitCache) RecomputeProfit(); return _totalIncome; } }
        public string incomeExp { get { if (dirtyProfitCache) RecomputeProfit(); return _incomeExp; } }
        public double totalProfit { get { if (dirtyProfitCache) RecomputeProfit(); return _totalProfit; } }

        // Jealously guard our resources. Only we can modify them!
        private List<ResourceFC> resources = new List<ResourceFC>();
        public List<ResourceFC> Resources => resources;
        private List<ThingDef> grandThingList = new List<ThingDef>();
        private bool dirtyGrandThingListFlag = true;

        // Comp caching for the most-frequently accessed comps
        private WorldObjectComp_SettlementMilitary cachedMilitaryComp = null;
        private bool checkedMilitaryComp = false;
        private WorldObjectComp_SettlementBuildings cachedBuildingsComp = null;
        private bool checkedBuildingsComp = false;

        // A private state variable
        private bool calculatingTax = false;
        public bool IsCalculatingTax => calculatingTax;
        public WorldObjectComp_SettlementMilitary MilitaryComp
        {
            get
            {
                if (!checkedMilitaryComp)
                {
                    cachedMilitaryComp = GetComponent<WorldObjectComp_SettlementMilitary>();
                    checkedMilitaryComp = true;
                    if (cachedMilitaryComp == null)
                    {
                        LogUtil.Warning($"Attempted to access settlement {Name}'s MilitaryComp, but it doesn't have one");
                    }
                }
                return cachedMilitaryComp;
            }
        }
        public WorldObjectComp_SettlementBuildings BuildingsComp
        {
            get
            {
                if (!checkedBuildingsComp)
                {
                    cachedBuildingsComp = GetComponent<WorldObjectComp_SettlementBuildings>();
                    checkedBuildingsComp = true;
                    if (cachedBuildingsComp == null)
                    {
                        LogUtil.Warning($"Attempted to access settlement {Name}'s BuildingsComp, but it doesn't have one");
                    }
                }
                return cachedBuildingsComp;
            }
        }
        public int settlementMilitaryLevel
        {
            get
            {
                if (!(MilitaryComp is null))
                {
                    return MilitaryComp.settlementMilitaryLevel;
                }
                return 0;
            }
            set
            {
                if (!(MilitaryComp is null))
                {
                    MilitaryComp.settlementMilitaryLevel = value;
                }
                else
                {
                    LogUtil.Warning($"Settlement {Name} does not have a MilitaryComp, but tried to set its settlementMilitaryLevel to {value}");
                }
            }
        }

        public string ShortName
        {
            get
            {
                if (!nameShort.NullOrEmpty()) return nameShort;

                nameShort = TextGen.ToShortName(name);

                return nameShort;
            }
            set => nameShort = value.NullOrEmpty() ? name : value;
        }

        public string OriginalName
        {
            get => nameOriginal;
            private set => nameOriginal = value;
        }

        private string cachedlocationText = string.Empty;
        public string locationText
        {
            get
            {
                if (cachedlocationText.NullOrEmpty())
                {
                    cachedlocationText = settlementDef.GetModExtension<SettlementTypeExtension>().GetLocationText(this);
                }
                return cachedlocationText;
            }
            set
            {
                cachedlocationText = value;
            }
        }

        public static readonly FieldInfo traitCachedIcon = typeof(WorldObjectDef).GetField("expandingIconTextureInt",
            BindingFlags.NonPublic | BindingFlags.Instance);

        public static readonly FieldInfo traitCachedMaterial = typeof(WorldObjectDef).GetField("material",
            BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        ///     A flag meant to indicate whether or not this settlement is meant for actual destruction; used to override
        ///     WorldObject.Destroy() for compatibility purposes
        /// </summary>
        private bool destroyFlag;

        public new WorldSettlementTraderTracker trader;

        public new string Name
        {
            get
            {
                return name ?? (name = "");
            }
            set => name = value;
        }

        public override string Label => Name;


        public new TraderKindDef TraderKind
        {
            get
            {
                if (trader.settlement == null) trader.settlement = this;
                return trader?.TraderKind;
            }
        }

        public new IEnumerable<Thing> Goods => trader?.StockListForReading;

        public new int RandomPriceFactorSeed => trader?.RandomPriceFactorSeed ?? 0;

        public new string TraderName => trader?.TraderName;

        public new bool CanTradeNow => trader != null && trader.CanTradeNow;

        public new float TradePriceImprovementOffsetForPlayer => trader?.TradePriceImprovementOffsetForPlayer ?? 0.0f;

        public new TradeCurrency TradeCurrency => TraderKind.tradeCurrency;

        public new bool EverVisited => trader.EverVisited;

        public new bool RestockedSinceLastVisit => trader.RestockedSinceLastVisit;

        public new int NextRestockTick => trader.NextRestockTick;
        public WorldSettlementDef settlementDef => def as WorldSettlementDef;

        /// <summary>
        ///     Indicate that this should be destroyed when WorldObject.Destroy() is called
        /// </summary>
        public void PrepareDestroy()
        {
            destroyFlag = true;
        }

        /// <summary>
        ///     Compatibility focused: this object should only be destroyed very deliberately, else another object is likely trying
        ///     to handle negative combat resolution against this settlement.
        /// </summary>
        public override void Destroy()
        {
            if (MilitaryComp != null)
            {
                MilitaryComp.EndBattle(false, 0);
            }

            if (destroyFlag)
            {
                base.Destroy();
            }
        }

        public void InvalidateCache()
        {
            InvalidateStatCache();
            DirtyDescriptionCache();
            cachedlocationText = null;
            cachedBuildingsComp = null;
            checkedBuildingsComp = false;
            cachedMilitaryComp = null;
            checkedMilitaryComp = false;
        }
        public void InvalidateStatCache()
        {
            cachedStatDescs.Clear();
            cachedStatValues.Clear();
            InvalidateResourceCaches();
            DirtyStatsCache();
        }

        /// <summary>
        /// Clears cached stat descriptions without clearing stat value caches.
        /// Called when faction-level modifiers change (desc includes faction contributions).
        /// </summary>
        public void InvalidateDescCache()
        {
            cachedStatDescs.Clear();
        }

        /// <summary>
        /// Dirties resource production caches without clearing stat caches.
        /// Called by FactionFC.InvalidateFactionStatCache when faction-level modifiers change
        /// (settlement stat caches are unaffected, but final combined values change).
        /// </summary>
        public void InvalidateResourceCaches()
        {
            foreach (ResourceFC resource in resources)
            {
                resource.SetDirtyCacheProdBase();
                resource.SetDirtyCacheProdMult();
            }
        }

        /// <summary>
        /// Handles the setting up of a settlement's resources. Allows for adding or removing resources after settlement creation (such as if the resource itself has
        /// a techlevel or research restriction)
        /// </summary>
        /// <param name="techlevel"></param>
        public void PrepareResources(TechLevel techlevel)
        {
            foreach (ResourceAvailability rtd in settlementDef.resources)
            {
                bool resourceAllowed = biomeDef.GetBiomeResource(rtd.resourceDef) != null && rtd.resourceDef.ResourceTypeAllowedByTech(techlevel);
                ResourceFC res = resources.Find((ResourceFC rfc) => rfc.def == rtd.resourceDef);
                if (res is null && resourceAllowed)
                {
                    LogUtil.Message($"Adding resource {rtd.resourceDef.label} to settlement {Name}");
                    /* ResourceFC initialization takes care of biome bonuses, so no need to handle that up here */
                    resources.Add(new ResourceFC(rtd.resourceDef, this));
                }
                else if (!(res is null) && !resourceAllowed)
                {
                    LogUtil.Message($"Removing resource {rtd.resourceDef.label} from settlement {Name}");
                    resources.Remove(res);
                }
                else if (!(res is null))
                {
                    res.SetDirtyCache();
                }
            }
            resources.Sort(ResourceFC.SortForUI);
            BuildingsComp?.InvalidateFilters();
        }

        public override void PostMake()
        {
            trader = new WorldSettlementTraderTracker(this);

            if (!(def is WorldSettlementDef))
            { 
                LogUtil.Error($"Created settlement {name} with an invalid def: {def}! Panic! Defaulting to base def!");
                def = WorldSettlementDefOf.WorldSettlementDef_Surface;
            }
            FactionFC faction = FactionCache.FactionComp;
            Name = settlementDef.GetSettlementTypeExtension().GetSettlementName();

            UpdateTechIcon();
            def.expandingIconTexture = "FactionIcons/" + faction.factionIconPath;
            traitCachedIcon.SetValue(def, ContentFinder<Texture2D>.Get(def.expandingIconTexture));
            base.PostMake();

            LogUtil.Message($"Created world settlement {Name} with def {def}");
        }
        /// <summary>
        /// Handles necessary post-PostMake processing that requires the Tile field to be set.
        /// </summary>
        /// <param name="tile"></param>
        public void PostPostMake(PlanetTile tile)
        {
            FactionFC faction = FactionCache.FactionComp;
            this.Tile = tile;

            settlementLevel = 1;

            _workers = 0;

            biome = Tile.Tile.PrimaryBiome.defName;
            bool useTileBiome = true;

            if (settlementDef.biomeResourceOverride != null)
            {
                LogUtil.Message($"Using biome {settlementDef.biomeResourceOverride.defName} as override for settlement {Name} of type {settlementDef}");
                useTileBiome = false;
                biomeDef = settlementDef.biomeResourceOverride;
                if (!DefDatabase<BiomeResourceDef>.AllDefs.Contains(biomeDef))
                {
                    LogUtil.Error($"Settlement {Name} of type {settlementDef.LabelCap} has invalid override biome. Falling back onto tile biome");
                    biomeDef = BiomeResourceDefOf.defaultBiome;
                    useTileBiome = true;
                }
            }
            if (useTileBiome)
            {
                //modded biomes handling
                biomeDef = DefDatabase<BiomeResourceDef>.GetNamed(biome, false) ?? BiomeResourceDefOf.defaultBiome;
                LogUtil.Message($"Founding settlement {Name} on biome {biomeDef.LabelCap}");
            }

            BuildingsComp?.InitBuildings();

            PrepareResources(faction.techLevel);

            /* If the settlement type has inherent stat modifiers, add them here. */
            // AddStatModifiers calls InvalidateStatCache -> DirtyStatsCache, so values recompute on first access
            AddStatModifiers(settlementDef.statModifiers, "settlementType", settlementDef.label);

            foundingTick = Find.TickManager.TicksGame;
        }
        public string GetFoundingDate(bool full = true)
        {
            if (full)
            {
                return GenDate.DateFullStringAt(foundingTick, FactionCache.FactionComp?.StartingLongLat ?? default(Vector2));
            }
            else
            {
                return GenDate.DateShortStringAt(foundingTick, FactionCache.FactionComp?.StartingLongLat ?? default(Vector2));
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Deep.Look(ref trader, "trader", this);
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref foundingTick, "foundingTick", defaultValue: 0);
            Scribe_Values.Look(ref nameShort, "nameShort", ShortName);
            Scribe_Values.Look(ref nameOriginal, "nameOriginal", OriginalName);
            Scribe_Values.Look(ref title, "title");
            Scribe_Values.Look(ref _description, "description");
            Scribe_Values.Look(ref _workers, "workers");
            Scribe_Values.Look(ref _workersMax, "workersMax");
            Scribe_Values.Look(ref _workersUltraMax, "workersUltraMax");
            Scribe_Values.Look(ref settlementLevel, "settlementLevel");
            Scribe_Values.Look(ref unrest, "unrest");
            Scribe_Values.Look(ref loyalty, "loyalty");
            Scribe_Values.Look(ref happiness, "happiness");
            Scribe_Values.Look(ref _prosperity, "prosperity");
            Scribe_Values.Look(ref _workerCost, "workerCost");
            Scribe_Values.Look(ref _workerTotalUpkeep, "workerTotalUpkeep");

            Scribe_Collections.Look(ref resources, "resources", LookMode.Deep);

            //Taxes
            Scribe_Collections.Look(ref tithe, "tithe", LookMode.Deep);
            Scribe_Values.Look(ref titheEstimatedIncome, "titheEstimatedIncome");
            Scribe_Values.Look(ref oneTimeSilverIncome, "silverIncome");


            //Stat modifiers — not serialized directly; rebuilt from buildings/settlement type on load

            //Biome_info
            Scribe_Values.Look(ref biome, "biome");
            Scribe_Defs.Look(ref biomeDef, "biomedef");

            Scribe_Values.Look(ref isUpgrading, "isupgrading", defaultValue: false);
            Scribe_Values.Look(ref startUpgradeTick, "startupgradetick", -1);
            Scribe_Values.Look(ref finishUpgradeTick, "finishupgradetick", -1);

            //Prisoners
            Scribe_Collections.Look(ref prisonerList, "prisonerList", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (trader != null && trader.settlement == null) trader.settlement = this;

                // Rebuild stat modifiers from buildings and settlement type before calculating stats.
                // statModifiers is intentionally not serialized — it's rebuilt from sources on load.
                // base.ExposeData() already called comp PostExposeData, so buildings are loaded.
                ClearStatModifiers();
                BuildingsComp?.ReapplyBuildingStatModifiers();
                // AddStatModifiers calls InvalidateStatCache -> DirtyStatsCache, so values recompute on first access
                AddStatModifiers(settlementDef.statModifiers, "settlementType", settlementDef.label);
                DirtyDescriptionCache();
            }
        }

        public void UpdateTechIcon()
        {
            var techLevel = FactionCache.FactionComp.techLevel;
            LogUtil.Message("Got tech level " + techLevel);
            if (techLevel == TechLevel.Animal || techLevel == TechLevel.Neolithic)
                def.texture = "World/WorldObjects/TribalSettlement";
            else
                def.texture = "World/WorldObjects/DefaultSettlement";

            traitCachedMaterial.SetValue(def, MaterialPool.MatFrom(def.texture,
                ShaderDatabase.WorldOverlayTransparentLit, WorldMaterials.WorldObjectRenderQueue));
        }

        public override IEnumerable<Gizmo> GetCaravanGizmos(Caravan caravan)
        {
            foreach (Gizmo gizmo in base.GetCaravanGizmos(caravan))
            {
                yield return gizmo;
            }
            if (MilitaryComp?.isUnderAttack != true && FactionCache.FactionComp.IsActionAllowed(FCActionType.TradeWithSettlement))
            {
                trader.settlement = trader.settlement ?? this;
                var kindDef = trader.TraderKind;
                var action = (Command_Action)CaravanVisitUtility.TradeCommand(caravan, Faction, kindDef);

                var bestNegotiator = BestCaravanPawnUtility.FindBestNegotiator(caravan, Faction, kindDef);
                action.action = () =>
                {
                    if (!CanTradeNow)
                        return;
                    Find.WindowStack.Add(new Dialog_Trade(bestNegotiator, this));
                    PawnRelationUtility.Notify_PawnsSeenByPlayer_Letter_Send(Goods.OfType<Pawn>(),
                        "LetterRelatedPawnsTradingWithSettlement"
                            .Translate((NamedArgument)Faction.OfPlayer.def.pawnsPlural), LetterDefOf.NeutralEvent);
                };

                yield return action;
            }
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (FloatMenuOption option in base.GetFloatMenuOptions(caravan))
            {
                yield return option;
            }
            if ((MilitaryComp == null || !MilitaryComp.isUnderAttack) && FactionCache.FactionComp.IsActionAllowed(FCActionType.TradeWithSettlement))
                foreach (var option in WorldSettlementTradeAction.GetFloatMenuOptions(caravan, this))
                    yield return option;
        }

        protected override void Tick()
        {
            base.Tick();
            trader?.TraderTrackerTick();
        }

        public void PublicTick()
        {
            Tick();
        }

        public override bool ShouldRemoveMapNow(out bool removeWorldObject)
        {
            removeWorldObject = false;
            if (MilitaryComp?.isUnderAttack == true) return false;
            return MilitaryComp is null || !(MilitaryComp.defenders.Any() || MilitaryComp.attackers.Any());
        }

        public void AddPrisoner(Pawn prisoner)
        {
            prisonerList.Add(new FCPrisoner(prisoner, this));
            DirtyStatsCache();
        }

        public void UpgradeSettlement(int times = 1)
        {
            int oldLevel = settlementLevel;
            settlementLevel += times;
            if (settlementLevel > FCSettings.settlementMaxLevel ||
                settlementLevel > settlementDef.maxSettlementLevel)
            {
                settlementLevel = FCSettings.settlementMaxLevel;
            }
            if (settlementLevel < 0) settlementLevel = 0;
            DirtyStatsCache();
            DirtyDescriptionCache();
            settlementDef.GetSettlementTypeExtension()?.OnUpgrade(this, oldLevel, settlementLevel);
            LifecycleRegistry.InvokeOnSettlementUpgraded(this, oldLevel, settlementLevel);
        }

        public void DelevelSettlement(int times = -1)
        {
            UpgradeSettlement(times);
        }

        /// <summary>
        /// Transitions this settlement to a new WorldSettlementDef, reconciling all dependent state
        /// (comps, stats, resources, buildings, caches). Returns false if the transition is blocked
        /// (e.g., incompatible planet layer or biome).
        /// </summary>
        public bool TransitionType(WorldSettlementDef newDef)
        {
            if (newDef == null || newDef == settlementDef) return false;

            WorldSettlementDef oldDef = settlementDef;

            // --- Validation: tile must be valid for new type ---
            SettlementTypeExtension newExt = newDef.GetSettlementTypeExtension();
            if (newExt == null)
            {
                LogUtil.Error($"Cannot transition {Name}: {newDef.defName} has no SettlementTypeExtension");
                return false;
            }
            StringBuilder reason = new StringBuilder();
            if (!newExt.TileIsValidForTypeTransition(new PlanetTile(Tile), reason))
            {
                LogUtil.Warning($"Cannot transition {Name} from {oldDef.defName} to {newDef.defName}: {reason}");
                return false;
            }

            // --- Pre-transition hooks ---
            oldDef.GetSettlementTypeExtension()?.PreTypeTransition(this, newDef);

            // --- Stat cleanup ---
            RemoveStatModifiersBySource("settlementType");

            // --- Deconstruct invalid buildings (before def swap, using new def for validation) ---
            if (BuildingsComp != null)
            {
                for (int i = BuildingsComp.Buildings.Count - 1; i >= 0; i--)
                {
                    BuildingFCDef bDef = BuildingsComp.Buildings[i].def;
                    if (bDef != BuildingFCDefOf.Empty && !bDef.CanBeBuiltForSettlementType(newDef))
                    {
                        Messages.Message("BuildingRemovedByTypeTransition".Translate(bDef.LabelCap, Name), MessageTypeDefOf.NeutralEvent);
                        BuildingsComp.DeconstructBuilding(i);
                    }
                }
            }

            // --- Core swap ---
            def = newDef;

            // --- Reconcile comps ---
            ReconcileComps(oldDef, newDef);

            // --- Clamp level ---
            if (settlementLevel > settlementDef.maxSettlementLevel)
                settlementLevel = settlementDef.maxSettlementLevel;

            // --- Reconcile resources: remove orphans, then add/dirty via PrepareResources ---
            HashSet<ResourceTypeDef> newResourceDefs = new HashSet<ResourceTypeDef>();
            foreach (ResourceAvailability ra in settlementDef.resources)
                newResourceDefs.Add(ra.resourceDef);
            for (int i = resources.Count - 1; i >= 0; i--)
            {
                if (!newResourceDefs.Contains(resources[i].def))
                    resources.RemoveAt(i);
            }
            PrepareResources(FactionCache.FactionComp.techLevel);

            // --- Apply new stat modifiers ---
            AddStatModifiers(settlementDef.statModifiers, "settlementType", settlementDef.label);

            // --- Reconcile building slots ---
            BuildingsComp?.ReinitBuildings();

            // --- Update icon/texture ---
            UpdateTechIcon();
            def.expandingIconTexture = "FactionIcons/" + FactionCache.FactionComp.factionIconPath;
            traitCachedIcon.SetValue(def, ContentFinder<Texture2D>.Get(def.expandingIconTexture));

            // --- Invalidate all caches ---
            InvalidateCache();
            FactionCache.FactionComp?.DirtyFactionProfitCache();
            FactionCache.FactionComp?.DirtyAveragesCache();

            // --- Post-transition hooks ---
            newDef.GetSettlementTypeExtension()?.PostTypeTransition(this, oldDef);
            LifecycleRegistry.InvokeOnSettlementTypeChanged(this, oldDef, newDef);

            LogUtil.Message($"Settlement {Name} transitioned from {oldDef.defName} to {newDef.defName}");
            return true;
        }

        /// <summary>
        /// Reconciles the WorldObjectComp list after a def swap.
        /// Removes comps whose compClass only existed on the old def (calling PostDestroy).
        /// Adds comps whose compClass only exists on the new def.
        /// Comps present on both defs are left untouched, preserving their state.
        /// </summary>
        private void ReconcileComps(WorldSettlementDef oldDef, WorldSettlementDef newDef)
        {
            HashSet<Type> oldCompClasses = new HashSet<Type>();
            foreach (WorldObjectCompProperties props in oldDef.comps)
                oldCompClasses.Add(props.compClass);

            HashSet<Type> newCompClasses = new HashSet<Type>();
            foreach (WorldObjectCompProperties props in newDef.comps)
                newCompClasses.Add(props.compClass);

            // Remove comps that are on the old def but NOT on the new def
            List<WorldObjectComp> compsList = AllComps;
            for (int i = compsList.Count - 1; i >= 0; i--)
            {
                Type compType = compsList[i].GetType();
                if (oldCompClasses.Contains(compType) && !newCompClasses.Contains(compType))
                {
                    compsList[i].PostDestroy();
                    compsList.RemoveAt(i);
                }
            }

            // Add comps that are on the new def but NOT on the old def
            HashSet<Type> currentCompClasses = new HashSet<Type>();
            foreach (WorldObjectComp comp in compsList)
                currentCompClasses.Add(comp.GetType());

            foreach (WorldObjectCompProperties props in newDef.comps)
            {
                if (!currentCompClasses.Contains(props.compClass))
                {
                    try
                    {
                        WorldObjectComp comp = (WorldObjectComp)Activator.CreateInstance(props.compClass);
                        comp.parent = this;
                        compsList.Add(comp);
                        comp.Initialize(props);
                    }
                    catch (Exception e)
                    {
                        LogUtil.Error($"Failed to create comp {props.compClass} during type transition: {e}");
                    }
                }
            }
        }

        public void GainUnrestWithReason(Message message, double amount)
        {
            Messages.Message(message);
            unrest += amount * GetStatValue(FCStatDefOf.unrestGainedMultiplier);
            FactionCache.FactionComp?.DirtyAveragesCache();
        }
        public void GainUnrest(double amount)
        {
            unrest += amount * GetStatValue(FCStatDefOf.unrestGainedMultiplier);
            FactionCache.FactionComp?.DirtyAveragesCache();
        }

        public void GainHappiness(double amount)
        {
            happiness += amount * GetStatValue(FCStatDefOf.happinessGainedMultiplier);
            FactionCache.FactionComp?.DirtyAveragesCache();
        }

        /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*
         * ~     Lazy Cache Invalidation        ~ *
         *-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

        /// <summary>
        /// Marks the stats cache (workersMax, workersUltraMax, militaryLevel) as dirty.
        /// Also cascades to dirty the profit cache since profit depends on stats.
        /// </summary>
        public void DirtyStatsCache()
        {
            dirtyStatsCache = true;
            dirtyProfitCache = true;
        }

        /// <summary>
        /// Marks the profit cache (income, upkeep, profit, workerCost) as dirty.
        /// </summary>
        public void DirtyProfitCache()
        {
            dirtyProfitCache = true;
            FactionCache.FactionComp?.DirtyFactionProfitCache();
        }

        /// <summary>
        /// Marks the description cache as dirty.
        /// </summary>
        public void DirtyDescriptionCache()
        {
            dirtyDescriptionCache = true;
        }

        /*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*
         * ~     Lazy Cache Recomputation       ~ *
         *-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*/

        private void RecomputeStats()
        {
            PerfWatchdog.Enter("Settlement.RecomputeStats");
            FactionFC factionFc = FactionCache.FactionComp;

            int extraWorkersSoftcap = (int)factionFc.GetStatValue(FCStatDefOf.extraWorkersSoftcap, this);
            int overMaxAdjustment = (int)factionFc.GetStatValue(FCStatDefOf.overMaxWorkersAdjustment, this);

            //Military Settlement Level
            settlementMilitaryLevel = settlementLevel - 1 + Convert.ToInt32(GetStatValue(FCStatDefOf.militaryBaseLevel));

            //Worker Stats
            _workersMax = settlementDef.workersMaxBase + (settlementLevel * (settlementDef.workersMaxMult + extraWorkersSoftcap)) +
                         GetStatValue(FCStatDefOf.workerBaseMax) + ReturnMaxWorkersFromPrisoners();
            _workersUltraMax = _workersMax + settlementDef.workersUltraMaxBase + overMaxAdjustment + (settlementLevel * settlementDef.workersUltraMaxMult) +
                              GetStatValue(FCStatDefOf.workerBaseOverMax) + ReturnOverMaxWorkersFromPrisoners();

            dirtyStatsCache = false;
            dirtyProfitCache = true;
            PerfWatchdog.Exit();
        }

        private void RecomputeProfit()
        {
            PerfWatchdog.Enter("Settlement.RecomputeProfit");
            if (dirtyStatsCache) RecomputeStats();

            _upkeepExp = "";
            _incomeExp = "";
            _workers = GetTotalWorkers_Internal();
            double upkeep = 0;
            double income = 0;

            _workerTotalUpkeep = SettlementFormulas.CalculateWorkerUpkeep(_workers, _workersMax, GetBaseWorkerCost());
            if (_workerTotalUpkeep > 0)
            {
                _upkeepExp += "+" + Math.Round(_workerTotalUpkeep, 2).ToString() + " - " + "Workers".Translate() + "\n";
            }

            upkeep += _workerTotalUpkeep;

            double buildingsUpkeep = BuildingsComp?.TotalUpkeep() ?? 0;
            if (buildingsUpkeep > 0)
            {
                upkeep += buildingsUpkeep;
                _upkeepExp += "+" + Math.Round(buildingsUpkeep, 2).ToString() + " - " + "Buildings".Translate() + "\n";
            }
            else if (buildingsUpkeep < 0)
            {
                income += Math.Abs(buildingsUpkeep);
                _incomeExp += "+" + Math.Round(Math.Abs(buildingsUpkeep), 2).ToString() + " - " + "Buildings".Translate() + "\n";
            }

            foreach (ResourceFC resource in resources)
            {
                if (resource.actualIncome > 0)
                {
                    income += resource.actualIncome;
                    _incomeExp += "+" + Math.Round(resource.actualIncome, 2).ToString() + " - " + resource.label + " " + "Income".Translate() + "\n";
                }
                else if (resource.actualIncome < 0)
                {
                    upkeep += (-1) * resource.actualIncome;
                    _upkeepExp += "+" + Math.Round(-1 * resource.actualIncome, 2).ToString() + " - " + resource.label + " " + "Tithing".Translate() + "\n";
                }
            }

            _upkeepExp = _upkeepExp.Trim();
            _incomeExp = _incomeExp.Trim();

            _totalUpkeep = upkeep;
            _totalIncome = income;
            _workerCost = _workers == 0 ? GetBaseWorkerCost() : (_workerTotalUpkeep / _workers);
            _totalProfit = _totalIncome - _totalUpkeep;

            dirtyProfitCache = false;
            PerfWatchdog.Exit();
        }

        private void RecomputeDescription()
        {
            _description = GetDescriptionBiome() + "\n\n" + GetSettlementLevelDesc();
            dirtyDescriptionCache = false;
        }

        public double GetHappinessGain()
        {
            double happinessGainMultiplier = GetStatValue(FCStatDefOf.happinessGainedMultiplier);
            return happinessGainMultiplier * (FCSettings.happinessBaseGain + GetStatValue(FCStatDefOf.happinessGainedBase));
        }
        public double GetHappinessLoss()
        {
            double happinessLostMultiplier = GetStatValue(FCStatDefOf.happinessLostMultiplier);
            return happinessLostMultiplier * (FCSettings.happinessBaseLost + GetStatValue(FCStatDefOf.happinessLostBase));
        }
        public double GetTotalHappinessGain()
        {
            return GetHappinessGain() - GetHappinessLoss();
        }
        public void UpdateHappiness()
        {
            happiness = SettlementFormulas.ClampStat(happiness, GetTotalHappinessGain());
        }
        public string GetHappinessDesc()
        {
            double happinessGain = GetTotalHappinessGain();
            string desc = "";

            if (happinessGain >= 0)
                desc = "SettlementStatGain".Translate(Math.Abs(happinessGain), "Happiness".Translate());
            else
                desc = "SettlementStatLoss".Translate(Math.Abs(happinessGain), "Happiness".Translate());

            desc += "\n\n";
            string gain = "";
            if (FCSettings.happinessBaseGain != 0)
                gain += TextUtil.ColorizeAdditiveBonus(FCSettings.happinessBaseGain) + " - " + "BaseGain".Translate() + "\n";

            gain += GetStatDesc(FCStatDefOf.happinessGainedBase);
            gain += GetStatDesc(FCStatDefOf.happinessGainedMultiplier);
            if (!gain.NullOrEmpty())
                desc += gain + "\n";

            if (FCSettings.happinessBaseLost != 0)
                desc += TextUtil.ColorizeAdditiveBonus(FCSettings.happinessBaseLost, hardinvert: true) + " - " + "BaseLoss".Translate() + "\n";

            desc += GetStatDesc(FCStatDefOf.happinessLostBase, hardinvert: true);
            desc += GetStatDesc(FCStatDefOf.happinessLostMultiplier);

            return desc.Trim();
        }

        public double GetLoyaltyGain()
        {
            double loyaltyGainMultiplier = GetStatValue(FCStatDefOf.loyaltyGainedMultiplier);
            return loyaltyGainMultiplier * (FCSettings.loyaltyBaseGain + GetStatValue(FCStatDefOf.loyaltyGainedBase));
        }
        public double GetLoyaltyLoss()
        {
            double loyaltyLostMultiplier = GetStatValue(FCStatDefOf.loyaltyLostMultiplier);
            return loyaltyLostMultiplier * (FCSettings.loyaltyBaseLost + GetStatValue(FCStatDefOf.loyaltyLostBase));
        }
        public double GetTotalLoyaltyGain()
        {
            return GetLoyaltyGain() - GetLoyaltyLoss();
        }
        public void UpdateLoyalty()
        {
            loyalty = SettlementFormulas.ClampStat(loyalty, GetTotalLoyaltyGain());
        }
        public string GetLoyaltyDesc()
        {
            double loyaltyGain = GetTotalLoyaltyGain();
            string desc = "";
            if (loyaltyGain >= 0)
                desc = "SettlementStatGain".Translate(Math.Abs(loyaltyGain), "Loyalty".Translate());
            else
                desc = "SettlementStatLoss".Translate(Math.Abs(loyaltyGain), "Loyalty".Translate());

            desc += "\n\n";
            string gain = "";
            if (FCSettings.loyaltyBaseGain != 0)
                gain += TextUtil.ColorizeAdditiveBonus(FCSettings.loyaltyBaseGain) + " - " + "BaseGain".Translate() + "\n";

            gain += GetStatDesc(FCStatDefOf.loyaltyGainedBase);
            gain += GetStatDesc(FCStatDefOf.loyaltyGainedMultiplier);
            if (!gain.NullOrEmpty())
                desc += gain + "\n";

            if (FCSettings.loyaltyBaseLost != 0)
                desc += "\n" + TextUtil.ColorizeAdditiveBonus(FCSettings.loyaltyBaseLost, hardinvert: true) + " - " + "BaseLoss".Translate() + "\n";

            desc += GetStatDesc(FCStatDefOf.loyaltyLostBase, hardinvert: true);
            desc += GetStatDesc(FCStatDefOf.loyaltyLostMultiplier);

            return desc.Trim();
        }

        public double GetProsperityGain()
        {
            return FCSettings.prosperityBaseRecovery + GetStatValue(FCStatDefOf.prosperityBaseRecovery);
        }
        public void UpdateProsperity()
        {
            prosperity = SettlementFormulas.ClampStat(prosperity, GetProsperityGain());
        }
        public string GetProsperityDesc()
        {
            double prosperityGain = GetProsperityGain();
            string desc = "";
            if (prosperityGain >= 0)
                desc = "SettlementStatGain".Translate(Math.Abs(prosperityGain), "Prosperity".Translate());
            else
                desc = "SettlementStatLoss".Translate(Math.Abs(prosperityGain), "Prosperity".Translate());

            desc += "\n\n";
            if (FCSettings.prosperityBaseRecovery != 0)
                desc += TextUtil.ColorizeAdditiveBonus(FCSettings.prosperityBaseRecovery) + " - " + "BaseRecovery".Translate() + "\n";

            desc += GetStatDesc(FCStatDefOf.prosperityBaseRecovery);

            return desc.Trim();
        }
        public double GetUnrestGain()
        {
            double unrestGainMultiplier = GetStatValue(FCStatDefOf.unrestGainedMultiplier);
            return unrestGainMultiplier * (FCSettings.unrestBaseGain + GetStatValue(FCStatDefOf.unrestGainedBase));
        }
        public double GetUnrestLoss()
        {
            double unrestLostMultiplier = GetStatValue(FCStatDefOf.unrestLostMultiplier);
            return unrestLostMultiplier * (FCSettings.unrestBaseLost + GetStatValue(FCStatDefOf.unrestLostBase));
        }
        public double GetTotalUnrestGain()
        {
            return GetUnrestGain() - GetUnrestLoss();
        }
        public void UpdateUnrest()
        {
            unrest = SettlementFormulas.ClampStat(unrest, GetTotalUnrestGain());
        }
        public string GetUnrestDesc()
        {
            double unrestGain = GetTotalUnrestGain();
            string desc = "";
            if (unrestGain >= 0)
                desc = "SettlementStatGain".Translate(Math.Abs(unrestGain), "Unrest".Translate());
            else
                desc = "SettlementStatLoss".Translate(Math.Abs(unrestGain), "Unrest".Translate());

            desc += "\n\n";
            string gain = "";
            if (FCSettings.unrestBaseGain != 0)
                gain += TextUtil.ColorizeAdditiveBonus(FCSettings.unrestBaseGain, invert: true) + " - " + "BaseGain".Translate() + "\n";

            gain += GetStatDesc(FCStatDefOf.unrestGainedBase);
            gain += GetStatDesc(FCStatDefOf.unrestGainedMultiplier);
            if (!gain.NullOrEmpty())
                desc += gain + "\n";

            if (FCSettings.unrestBaseLost != 0)
                desc += TextUtil.ColorizeAdditiveBonus(FCSettings.unrestBaseLost, invert: true, hardinvert: true) + " - " + "BaseLoss".Translate() + "\n";

            desc += GetStatDesc(FCStatDefOf.unrestLostBase, hardinvert: true);
            desc += GetStatDesc(FCStatDefOf.unrestLostMultiplier);

            return desc.Trim();
        }
        public double GetSettlementTaxBonus()
        {
            FactionFC faction = FactionCache.FactionComp;
            double bonus = faction.GetStatValue(FCStatDefOf.taxBonusFlat, this);
            bonus += GetStatValue(FCStatDefOf.taxBasePercentage);
            bonus = ((100d + bonus) / 100d);
            return bonus;
        }

        public double GetTotalIncome() => totalIncome;

        public int GetTotalWorkers()
        {
            int totalWorkers = 0;
            foreach (ResourceFC resource in resources)
            {
                totalWorkers += resource.assignedWorkers;
            }

            while (totalWorkers > workersUltraMax)
            {
                if (IncreaseWorkers(null, -1))
                {
                    totalWorkers -= 1;
                }
                else
                {
                    LogUtil.Error($"GetTotalWorkers: IncreaseWorkers failed to shed a worker for {Name}. Breaking to prevent freeze.");
                    break;
                }
            }

            return totalWorkers;
        }

        /// <summary>
        /// Internal worker count for use inside RecomputeProfit. Reads backing fields directly
        /// and sheds workers without triggering profit recalculation.
        /// </summary>
        private int GetTotalWorkers_Internal()
        {
            int totalWorkers = 0;
            foreach (ResourceFC resource in resources)
            {
                totalWorkers += resource.assignedWorkers;
            }

            int maxAttempts = resources.Count * ((int)(totalWorkers - _workersUltraMax) + 1) * 3;
            int attempts = 0;
            while (totalWorkers > _workersUltraMax)
            {
                if (++attempts > maxAttempts)
                {
                    LogUtil.Error($"GetTotalWorkers_Internal: exceeded {maxAttempts} attempts shedding workers for {Name}. Bailing out to prevent freeze.");
                    break;
                }
                int idx = Rand.RangeInclusive(0, resources.Count - 1);
                if (resources[idx].assignedWorkers > 0)
                {
                    resources[idx].assignedWorkers -= 1;
                    totalWorkers -= 1;
                }
            }

            return totalWorkers;
        }

        private bool CanStillModify(ResourceFC resource, int singleMod) => _workers + singleMod <= workersUltraMax && _workers + singleMod >= 0 && resource.assignedWorkers + singleMod <= workersUltraMax && resource.assignedWorkers + singleMod >= 0;

        public bool IncreaseWorkers(ResourceFC resource, int numWorkers)
        {
            int singleMod = (numWorkers > 0) ? 1 : -1;
            if (resource == null)
            {
                if (numWorkers >= 0 && _workers <= workersUltraMax)
                {
                    return false;
                }

                int maxAttempts = resources.Count * 3;
                while (_workers > workersUltraMax)
                {
                    if (--maxAttempts < 0)
                    {
                        LogUtil.Error($"IncreaseWorkers: exceeded max attempts finding a worker to shed for {Name}. Bailing out to prevent freeze.");
                        break;
                    }
                    int num = Rand.RangeInclusive(0, resources.Count - 1);
                    if (resources[num].assignedWorkers > 0)
                    {
                        resources[num].assignedWorkers -= 1;
                        return true;
                    }
                }
            }
            else
            {
                while (CanStillModify(resource, singleMod))
                {
                    _workers += singleMod;
                    resource.assignedWorkers += singleMod;
                    numWorkers -= singleMod;
                    if (numWorkers == 0)
                    {
                        DirtyProfitCache();
                        FactionCache.FactionComp.DirtyFactionProfitCache();
                        return true;
                    }
                }
                DirtyProfitCache();
                FactionCache.FactionComp.DirtyFactionProfitCache();
            }

            return false;
        }

        public double GetBaseWorkerCost()
        {
            return FCSettings.workerCost + GetStatValue(FCStatDefOf.workerBaseCost);
        }

        public double GetTotalUpkeep() => totalUpkeep;

        public double GetTotalProfit() => totalProfit;

        public float Happiness
        {
            get { return (float)Math.Round(happiness, 1); }
        }

        public float Unrest
        {
            get { return (float)Math.Round(unrest, 1); }
        }

        public float Loyalty
        {
            get { return (float)Math.Round(loyalty, 1); }
        }

        public float Prosperity
        {
            get { return (float)Math.Round(prosperity, 1); }
        }

        public ResourceFC ReturnHighestResource()
        {
            double highest = -1;
            ResourceFC highestResource = null;

            foreach (ResourceFC resource in resources)
            {
                if (resource.actualIncome > highest)
                {
                    highest = resource.actualIncome;
                    highestResource = resource;
                }
            }

            return highestResource;
        }

        public double GetDefenseBonus()
        {
            double defenseBonus = 0;
            foreach (ResourceFC resource in resources)
            {
                if (resource.def.defenseWeight > 0f && resource.effectiveRawTotalProduction > 0)
                {
                    defenseBonus += resource.effectiveRawTotalProduction * resource.def.defenseWeight;
                }
            }
            return defenseBonus;
        }

        private string GetDescriptionBiome()
        {
            if (!biomeDef.descriptionKey.NullOrEmpty())
                return biomeDef.descriptionKey.Translate();
            return "FCDescUnknown".Translate();
        }
        private string GetSettlementLevelDesc()
        {
            return settlementDef.GetSettlementTypeExtension()?.GetSettlementLevelDesc(settlementLevel)
                ?? "FCTownLevel5".Translate();
        }

        /// <summary>
        /// Adds stat modifiers from a source (building, settlement type, etc).
        /// Resource production bonuses are now handled via FCStatDef's linkedResource on ResourceFC.
        /// </summary>
        public void AddStatModifiers(List<FCStatModifier> mods, string sourceId, string sourceLabel = null)
        {
            if (mods != null)
            {
                foreach (FCStatModifier mod in mods)
                    statModifiers.Add(new TaggedStatModifier { sourceId = sourceId, sourceLabel = sourceLabel ?? sourceId, mod = mod });
            }
            InvalidateStatCache();
        }

        /// <summary>
        /// Removes stat modifiers previously added by the given source.
        /// mods must be the exact same FCStatModifier object references that were passed to
        /// AddStatModifiers, since removal uses reference equality (the def's objects stored via AddRange).
        /// </summary>
        public void RemoveStatModifiers(List<FCStatModifier> mods, string sourceId)
        {
            if (mods != null)
            {
                foreach (FCStatModifier mod in mods)
                {
                    for (int i = statModifiers.Count - 1; i >= 0; i--)
                    {
                        if (statModifiers[i].mod == mod)
                        {
                            statModifiers.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
            InvalidateStatCache();
        }

        /// <summary>
        /// Removes all stat modifiers that were added with the given sourceId.
        /// </summary>
        public void RemoveStatModifiersBySource(string sourceId)
        {
            for (int i = statModifiers.Count - 1; i >= 0; i--)
            {
                if (statModifiers[i].sourceId == sourceId)
                    statModifiers.RemoveAt(i);
            }
            InvalidateStatCache();
        }

        /// <summary>
        /// Clears all settlement-level stat modifiers (from buildings, settlement type).
        /// </summary>
        public void ClearStatModifiers()
        {
            statModifiers.Clear();
            InvalidateStatCache();
        }

        /// <summary>
        /// The settlement-level stat modifier list (unwrapped from tagged entries).
        /// </summary>
        public List<FCStatModifier> StatModifiers
        {
            get
            {
                var result = new List<FCStatModifier>(statModifiers.Count);
                foreach (TaggedStatModifier tagged in statModifiers)
                    result.Add(tagged.mod);
                return result;
            }
        }

        /// <summary>
        /// Computes and caches the settlement-level stat partial (buildings, settlement type, events, IStatModifierProvider comps).
        /// Does NOT include faction-level modifiers or behavior adjustments.
        /// Called by FactionFC.GetStatValue to get the settlement contribution for aggregation.
        /// </summary>
        public double GetSettlementStatValue(FCStatDef stat)
        {
            if (cachedStatValues.TryGetValue(stat, out double cached))
                return cached;

            double value = stat.IdentityValue;

            foreach (TaggedStatModifier tagged in statModifiers)
            {
                if (tagged.mod.stat == stat)
                {
                    if (stat.aggregation == FCStatAggregation.Additive)
                        value += tagged.mod.value;
                    else
                        value *= tagged.mod.value;
                }
            }

            foreach (WorldObjectComp comp in AllComps)
            {
                if (comp is IStatModifierProvider provider)
                {
                    double compValue = provider.GetStatModifier(stat);
                    if (stat.aggregation == FCStatAggregation.Additive)
                        value += compValue;
                    else
                        value *= compValue;
                }
            }

            cachedStatValues[stat] = value;
            return value;
        }

        /// <summary>
        /// Returns the final combined stat value at this settlement.
        /// Delegates to FactionFC.GetStatValue which combines settlement + faction partials + behaviors.
        /// </summary>
        public double GetStatValue(FCStatDef stat)
        {
            if (!stat.appliesToSettlements)
                return FactionCache.FactionComp.GetStatValue(stat);
            return FactionCache.FactionComp.GetStatValue(stat, this);
        }

        /// <summary>
        /// Builds a per-source breakdown description for a stat at this settlement.
        /// Combines settlement-level, faction-level, and behavior contributions.
        /// </summary>
        public string GetStatDesc(FCStatDef stat, bool hardinvert = false)
        {
            if (!stat.appliesToSettlements) return "";
            if (!cachedStatDescs.TryGetValue(stat, out string desc))
            {
                desc = "";
                bool isAdditive = stat.aggregation == FCStatAggregation.Additive;
                bool invert = stat.invertedForDisplay;

                // Settlement-level modifiers (buildings, settlement type, events)
                foreach (TaggedStatModifier tagged in statModifiers)
                {
                    if (tagged.mod.stat != stat) continue;
                    if (isAdditive)
                        desc += TextUtil.ColorizeAdditiveBonus(tagged.mod.value, invert: invert, hardinvert: hardinvert) + " - " + tagged.sourceLabel + "\n";
                    else
                        desc += TextUtil.ColorizeMultiplierBonus(tagged.mod.value, invert: invert) + " - " + tagged.sourceLabel + "\n";
                }

                // IStatModifierProvider comps
                foreach (WorldObjectComp comp in AllComps)
                {
                    if (comp is IStatModifierProvider provider)
                        desc += provider.GetStatModifierDesc(stat);
                }

                // Faction-level policy/trait modifiers (delegated to FactionFC)
                FactionFC faction = FactionCache.FactionComp;
                desc += faction.GetFactionStatDesc(stat, hardinvert);

                // Behavior runtime contributions (e.g., Egalitarian happiness bonus, Expansionist discount)
                faction.ForEachBehavior(b =>
                {
                    string behaviorDesc = b.GetStatDescription(stat, this);
                    if (!behaviorDesc.NullOrEmpty())
                        desc += behaviorDesc;
                });

                cachedStatDescs[stat] = desc;
            }
            return desc;
        }

        public void DeconstructBuilding(int buildingSlot)
        {
            BuildingsComp?.DeconstructBuilding(buildingSlot);
        }

        private int ReturnMaxWorkersFromPrisoners()
        {
            int num = 0;
            foreach (FCPrisoner prisoner in prisonerList)
            {
                switch (prisoner.workload)
                {
                    case FCWorkLoad.Medium:
                        num++;
                        break;
                    case FCWorkLoad.Heavy:
                        num += 2;
                        break;
                }
            }

            return num;
        }

        private int ReturnOverMaxWorkersFromPrisoners()
        {
            return prisonerList.Count(prisoner => prisoner.workload == FCWorkLoad.Light);
        }


        public bool ValidConstructBuilding(BuildingFCDef building, int buildingSlot)
        {
            if (BuildingsComp == null)
            {
                return false;
            }
            return BuildingsComp.ValidConstructBuilding(building, buildingSlot);
        }


        public void ConstructBuilding(BuildingFCDef building, int buildingSlot)
        {
            if (BuildingsComp == null)
            {
                return;
            }
            BuildingsComp.ConstructBuilding(building, buildingSlot);
        }

        public ResourceFC ReturnResource(string defName) //used to return the correct resource based on string name
        {
            ResourceFC res = resources.Find((ResourceFC rfc) => rfc.def.defName == defName);
            if (res == null)
            {
                LogUtil.Message($"Requested resource {defName} is not in settlement {Name}'s resource list");
            }
            return res;
        }

        public ResourceFC GetResource(ResourceTypeDef type) //used to return the correct resource based on string name
        {
            ResourceFC res = resources.Find((ResourceFC rfc) => rfc.def == type);
            if (res == null)
            {
                LogUtil.Message($"Requested resource {type.defName} is not in settlement {Name}'s resource list");
            }
            return res;
        }

        public ResourceFC GetResourceByIndex(int index)
        {
            if (index >= resources.Count || index < 0)
            {
                return null;
            }
            for (int i = 0; i < resources.Count; i++)
            {
                if (i == index)
                    return resources[i];
            }
            LogUtil.Error($"Reached end of WorldSettmentFC.GetResourceByIndex for settlement {Name} and resource index {index}. This should never happen.");
            return null;
        }
        public List<ResourceFC> GetTitheableResources()
        {
            List<ResourceFC> list = new List<ResourceFC>();
            foreach (ResourceFC res in Resources)
            {
                if (res.canTithe)
                {
                    list.Add(res);
                }
            }
            return list;
        }
        /// <summary>
        /// Returns a list of *all* things that this settlement can produce.
        /// </summary>
        /// <returns></returns>
        public List<ThingDef> GetGrandThingList()
        {
            if (dirtyGrandThingListFlag)
            {
                grandThingList = new List<ThingDef>();
                foreach (ResourceFC res in resources)
                {
                    if (!res.def.isPoolResource)
                    {
                        List<ThingDef> resList = res.GenerateThingDefList();
                        if (resList != null && resList.Count > 0)
                        {
                            grandThingList.AddRange(resList);
                        }
                    }
                }
                dirtyGrandThingListFlag = false;
            }
            return grandThingList;
        }
        public void DirtyGrandThingList()
        {
            dirtyGrandThingListFlag = true;
            FactionCache.FactionComp.DirtyGrandThingList();
        }

        public float GetOneTimeSilverIncome()
        {
            return oneTimeSilverIncome;
        }

        public void ResetOneTimeSilverIncome()
        {
            oneTimeSilverIncome = 0;
        }

        public void AddOneTimeSilverIncome(float amount)
        {
            oneTimeSilverIncome += amount;
        }

        public float ReturnOneTimeSilverIncome(bool reset)
        {
            float income = oneTimeSilverIncome;

            if (reset)
            {
                ResetOneTimeSilverIncome();
            }

            return income;
        }

        public void GoTo()
        {
            Find.World.renderer.wantedMode = WorldRenderMode.Planet;

            //Select Settlement Tile
            Find.WorldSelector.ClearSelection();
            Find.WorldSelector.Select(Find.WorldObjects.MapParentAt(Tile));
            if (Find.MainButtonsRoot.tabs.OpenTab != null)
            {
                Find.MainButtonsRoot.tabs.OpenTab.TabWindow.Close();
            }
        }
        public List<ResourcePool> CreateResourcePools()
        {
            List<ResourcePool> pools = new List<ResourcePool>();

            foreach(ResourceFC resource in resources)
            {
                if (resource.def.isPoolResource)
                {
                    ResourcePool pool = resource.CreatePool();
                    if (pool.pool != 0)
                    {
                        pools.Add(pool);
                    }
                }
            }

            return pools;
        }

        public void PruneResourceTithes()
        {
            foreach (ResourceFC res in resources)
            {
                if (res.canTithe)
                {
                    res.PruneTitheList();
                }
            }
        }
        public void DirtyResourceCache(ResourceTypeDef resDef)
        {
            ResourceFC res = GetResource(resDef);
            if (!(res is null))
            {
                res.SetDirtyCache();
            }
        }
        public void DirtyResourceCache(ResourceFC res)
        {
            if (!(res is null))
            {
                res.SetDirtyCache();
            }
        }
        public void DirtyResourceCaches()
        {
            foreach (ResourceFC res in resources)
            {
                res.SetDirtyCache();
            }
        }
        /// <summary>
        /// Handles any necessary pre-tax preparations to ensure that the tax calculation is up-to-date and accurate.
        /// </summary>
        private void PreTaxPrep()
        {
            DirtyResourceCaches();
            foreach (ResourceFC res in resources)
                res.PruneStockpileAllocations();
            PruneResourceTithes();
            DirtyStatsCache();
            calculatingTax = true;
        }
        public void AccumulateDailyProduction()
        {
            foreach (ResourceFC res in resources)
            {
                res.AccumulateDailyProduction();
            }
        }
        private void PostTaxPrep()
        {
            calculatingTax = false;
            foreach (ResourceFC res in resources)
            {
                res.ResetAccumulator();
            }
        }
        /// <summary>
        /// This function handles the calculations for determing this settlement's taxes at tax time. It handles both tithes and silver taxes.
        /// </summary>
        /// <param name="silverAmount">The amount of silver to tax; positive if the player gains silver, negative otherwise.</param>
        /// <returns>A list of things produced by tithing resources. May be empty if there are no tithes.</returns>
        public List<Thing> CreateTax(out int silverAmount)
        {
            PerfWatchdog.Enter("Settlement.CreateTax");
            PreTaxPrep();
            settlementDef.GetSettlementTypeExtension()?.PreTax(this);
            TaxTickRegistry.InvokePreSettlementCreateTax(this);

            List<Thing> titheThings = new List<Thing>();
            int tmpSilverAmount = (int)((totalIncome - totalUpkeep) + ReturnOneTimeSilverIncome(true));

            foreach (ResourceFC resource in resources)
            {
                if (resource.canTithe)
                {
                    int resExtraSilver = 0;
                    List<Thing> resTitheThings = resource.GenerateTithe(out resExtraSilver);

                    if (resTitheThings.Count > 0)
                    {
                        titheThings.AddRange(resTitheThings);
                    }
                    tmpSilverAmount += resExtraSilver;
                }
            }

            PostTaxPrep();
            silverAmount = tmpSilverAmount;
            settlementDef.GetSettlementTypeExtension()?.PostTax(this, ref silverAmount, titheThings);
            TaxTickRegistry.InvokePostSettlementCreateTax(this, ref silverAmount, titheThings);
            PerfWatchdog.Exit();
            return titheThings;
        }
    }

    // NOTE: PawnGizmos patch moved to GizmosPatches.cs to avoid duplication
    // The optimized version in GizmosPatches.PawnDraftGizmos handles all pawn gizmo modifications
}