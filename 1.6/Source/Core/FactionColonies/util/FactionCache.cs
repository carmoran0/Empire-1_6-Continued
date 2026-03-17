using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Static cache to hold on to frequently-accessed fields that change infrequently, or never.
    /// 
    /// <para>This cache needs to be invalidated any time the game loads or changes. Presently, this is done through a Harmony Postfix on Game.Dispose().</para>
    /// <para>NOTE: DefDatabase[PawnKindDef].AllDefsListForReading is cached here. That means that def hotloading is a no-no.</para>
    /// </summary>
    public static class FactionCache
    {
        private static Faction _cachedColonyFaction = null;
        private static Faction _cachedPlayerFaction = null;
        private static FactionFC _cachedFactionWorldComp = null;
        private static FactionDef _cachedFactionDef = null;
        private static List<PawnKindDef> _cachedPawnKindDefs = null;
        private static Dictionary<(Type, string), FieldInfo> _cachedFields = new Dictionary<(Type, string), FieldInfo>();
        private static List<XenotypeDef> _cachedXenotypeList = null;
        private static List<XenotypeDef> _cachedViolentXenotypeList = null;
        private static List<CustomXenotype> _cachedCustomXenotypeList = null;
        private static List<CustomXenotype> _cachedViolentCustomXenotypeList = null;
        private static Dictionary<string, CustomXenotype> _cachedCustomXenotypeDecoder = null;
        private static List<ThingDef> _cachedRaceList = null;
        private static List<PawnKindDef> _cachedAnimalKinds = null;
        private static List<PawnKindDef> _cachedCombatAnimalKinds = null;
        private static List<PawnKindDef> _cachedPackAnimalKinds = null;
        private static bool _checkedForNonViolentXenos = false;
        private static bool _cachedNonViolentXenosExist = false;
        private static Dictionary<XenotypeDef, bool> _cachedXenotypeViolenceDict = null;
        private static Dictionary<string, bool> _cachedCustomXenotypeViolenceDict = null;
        private static List<FCPolicyDef> _cachedFCPolicyDefs = null;
        private static Dictionary<FCPolicyDef, string> _cachedFCPolicyDescs = null;
        private static Dictionary<BuildingFCDef, List<BuildingUpgradeEntry>> _cachedUpgradeTrees = null;
        private static Dictionary<BuildingFCDef, List<BuildingFCDef>> _cachedRequiredByMap = null;
        private static List<FCEventCategoryDef> _cachedEventCategoryDefs = null;
        private static List<MilitaryJobDef> _cachedHostileMilitaryJobs = null;
        // Empire refers to some ResearchProjectDefs before DefOfs are resolved. So instead of using DefOfs, we'll cache them here.
        private static ResearchProjectDef _cachedTechLevelBarrierUltra = null;
        private static ResearchProjectDef _cachedTechLevelBarrierSpacer = null;
        private static ResearchProjectDef _cachedTechLevelBarrierIndustrial = null;
        private static ResearchProjectDef _cachedTechLevelBarrierMedieval = null;
        private static ResearchProjectDef _cachedTransportPods = null;

        public static FactionFC FactionComp
        {
            get
            {
                if (_cachedFactionWorldComp == null)
                {
                    _cachedFactionWorldComp = Find.World.GetComponent<FactionFC>();
                }
                return _cachedFactionWorldComp;
            }
        }
        /// <summary>
        /// The NPC Empire faction that the player created and controls.
        /// </summary>
        public static Faction PlayerColonyFaction
        {
            get
            {
                if (_cachedColonyFaction == null)
                {
                    _cachedColonyFaction = Find.FactionManager.FirstFactionOfDef(DefDatabase<FactionDef>.GetNamed("PColony"));
                }
                return _cachedColonyFaction;
            }
        }
        public static bool IsPlayerColonyFaction(Faction f) => !(PlayerColonyFaction is null) && f == PlayerColonyFaction;
        /// <summary>
        /// The player faction itself.
        /// </summary>
        public static Faction PlayerFaction
        {
            get
            {
                if (_cachedPlayerFaction == null)
                {
                    _cachedPlayerFaction = Find.FactionManager.AllFactions.FirstOrDefault(faction => faction.IsPlayer);
                }
                return _cachedPlayerFaction;
            }
        }
        public static List<PawnKindDef> AllPawnKindDefs
        {
            get
            {
                if (_cachedPawnKindDefs == null || _cachedPawnKindDefs.Count == 0)
                {
                    _cachedPawnKindDefs = DefDatabase<PawnKindDef>.AllDefsListForReading;
                }
                return _cachedPawnKindDefs;
            }
        }
        public static Dictionary<(Type, string), FieldInfo> FieldCache => _cachedFields;
        public static FieldInfo GetFieldCacheValue(Type typ, string field)
        {
            FieldInfo fieldInfo;
            if (FieldCache.TryGetValue((typ, field), out fieldInfo))
            {
                return fieldInfo;
            }
            fieldInfo = typ.GetField(field);
            FieldCache.Add((typ, field), fieldInfo);
            return fieldInfo;
        }
        public static FactionDef EmpireFactionDef
        {
            get
            {
                if (_cachedFactionDef == null)
                {
                    _cachedFactionDef = DefDatabase<FactionDef>.GetNamed("PColony");
                }
                return _cachedFactionDef;
            }
        }
        public static List<XenotypeDef> XenotypeDefs
        {
            get
            {
                if (_cachedXenotypeList == null)
                {
                    _cachedXenotypeList = DefDatabase<XenotypeDef>.AllDefsListForReading;
                }
                return _cachedXenotypeList;
            }
        }
        public static List<CustomXenotype> CustomXenotypes
        {
            get
            {
                if (_cachedCustomXenotypeList == null)
                {
                    _cachedCustomXenotypeList = Current.Game?.customXenotypeDatabase?.customXenotypes;
                }
                return _cachedCustomXenotypeList;
            }
        }
        public static Dictionary<string, CustomXenotype> CustomXenotypesDecoder
        {
            get
            {
                if (_cachedCustomXenotypeDecoder == null)
                {
                    if (!(CustomXenotypes is null || CustomXenotypes.Count == 0))
                    {
                        _cachedCustomXenotypeDecoder = new Dictionary<string, CustomXenotype>();
                        foreach (CustomXenotype xenotype in CustomXenotypes)
                        {
                            _cachedCustomXenotypeDecoder.Add(xenotype.name, xenotype);
                        }
                    }
                }
                return _cachedCustomXenotypeDecoder;
            }
        }
        public static List<ThingDef> HumanlikeRaces
        {
            get
            {
                if (_cachedRaceList == null)
                {
                    _cachedRaceList = new List<ThingDef>();
                    foreach (PawnKindDef pawnKind in AllPawnKindDefs)
                    {
                        if (pawnKind.race != null && !_cachedRaceList.Any(r => r.defName == pawnKind.race.defName) && (pawnKind.race == ThingDefOf.Human || pawnKind.IsHumanLikeRace()))
                        {
                            _cachedRaceList.Add(pawnKind.race);
                        }
                    }
                }
                return _cachedRaceList;
            }
        }
        // Technically there should *always* be at least one race: ThingDefOf.Human. But it probably can't hurt to null-check, just in case of edge cases...
        public static int HumanlikeRacesCount
        {
            get
            {
                if (HumanlikeRaces == null)
                {
                    return 0;
                }
                else
                {
                    return HumanlikeRaces.Count;
                }
            }
        }
        public static List<PawnKindDef> AllAnimalKindDefs
        {
            get
            {
                if (_cachedAnimalKinds == null)
                {
                    _cachedAnimalKinds = AllPawnKindDefs.Where(kind => kind.IsAnimalAndAllowed()).ToList();
                }
                return _cachedAnimalKinds;
            }
        }
        public static List<PawnKindDef> AllCombatAnimalKindDefs
        {
            get
            {
                if(_cachedCombatAnimalKinds == null)
                {
                    _cachedCombatAnimalKinds = AllPawnKindDefs.Where(kind => kind.IsCombatAnimal()).ToList();
                }
                return _cachedCombatAnimalKinds;
            }
        }
        public static List<PawnKindDef> AllPackAnimalKinds
        {
            get
            {
                if (_cachedPackAnimalKinds == null)
                {
                    _cachedPackAnimalKinds = AllPawnKindDefs.Where(kind => kind.RaceProps.packAnimal).ToList();
                }
                return _cachedPackAnimalKinds;
            }
        }
        public static bool NonViolentXenotypesExist
        {
            get
            {
                if(!_checkedForNonViolentXenos)
                {
                    if (XenotypeDefs?.Count > 0)
                    {
                        foreach (XenotypeDef xenotype in XenotypeDefs)
                        {
                            if (XenotypeFilter.XenotypeNeedsSecurityGuards(xenotype))
                            {
                                _cachedNonViolentXenosExist = true;
                                break;
                            }
                        }
                    }
                    if (!_cachedNonViolentXenosExist && CustomXenotypes?.Count > 0)
                    {
                        foreach (CustomXenotype xenotype in CustomXenotypes)
                        {
                            if (XenotypeFilter.CustomXenotypeNeedsSecurityGuards(xenotype.name))
                            {
                                _cachedNonViolentXenosExist = true;
                                break;
                            }
                        }
                    }
                    _checkedForNonViolentXenos = true;
                }
                return _cachedNonViolentXenosExist;
            }
        }
        public static Dictionary<XenotypeDef, bool> XenotypeViolence
        {
            get
            {
                if (_cachedXenotypeViolenceDict == null && XenotypeDefs?.Count > 0)
                {
                    _cachedXenotypeViolenceDict = new Dictionary<XenotypeDef, bool>();
                    foreach (XenotypeDef xenotype in XenotypeDefs)
                    {
                        _cachedXenotypeViolenceDict.Add(xenotype, !XenotypeFilter.XenotypeNeedsSecurityGuards(xenotype));
                    }
                }
                return _cachedXenotypeViolenceDict;
            }
        }
        public static Dictionary<string, bool> CustomXenotypeViolence
        {
            get
            {
                if (_cachedCustomXenotypeViolenceDict == null && CustomXenotypes?.Count > 0)
                {
                    _cachedCustomXenotypeViolenceDict = new Dictionary<string, bool>();
                    foreach (CustomXenotype xenotype in CustomXenotypes)
                    {
                        _cachedCustomXenotypeViolenceDict.Add(xenotype.name, !XenotypeFilter.CustomXenotypeNeedsSecurityGuards(xenotype.name));
                    }
                }
                return _cachedCustomXenotypeViolenceDict;
            }
        }
        public static bool XenotypeIsNonViolent(XenotypeDef xenotype)
        {
            if (xenotype == null) return false;
            if (XenotypeViolence?.TryGetValue(xenotype, out bool violent) == true)
            {
                return !violent;
            }
            return false;
        }
        public static bool CustomXenotypeIsNonViolent(string xenotypeName)
        {
            if (CustomXenotypeViolence?.TryGetValue(xenotypeName, out bool violent) == true)
            {
                return !violent;
            }
            return false;
        }
        public static bool CustomXenotypeIsNonViolent(CustomXenotype xenotype)
        {
            return CustomXenotypeIsNonViolent(xenotype.name);
        }
        public static List<FCPolicyDef> AllFCPolicies
        {
            get
            {
                if (_cachedFCPolicyDefs == null)
                {
                    _cachedFCPolicyDefs = DefDatabase<FCPolicyDef>.AllDefsListForReading;
                }
                return _cachedFCPolicyDefs;
            }
        }
        public static Dictionary<FCPolicyDef, string> FCPolicyDescs
        {
            get
            {
                if (_cachedFCPolicyDescs == null)
                {
                    _cachedFCPolicyDescs = new Dictionary<FCPolicyDef, string>();
                    foreach (FCPolicyDef policy in AllFCPolicies)
                    {
                        _cachedFCPolicyDescs.Add(policy, policy.PolicyDesc());
                    }
                }
                return _cachedFCPolicyDescs;
            }
        }
        public static List<XenotypeDef> ViolentXenotypeDefs
        {
            get
            {
                if (_cachedViolentXenotypeList == null)
                {
                    _cachedViolentXenotypeList = XenotypeDefs.Where(x => !XenotypeIsNonViolent(x)).ToList();
                }
                return _cachedViolentXenotypeList;
            }
        }
        public static List<CustomXenotype> ViolentCustomXenotypes
        {
            get
            {
                if (_cachedViolentCustomXenotypeList == null)
                {
                    _cachedViolentCustomXenotypeList = CustomXenotypes.Where(x => !CustomXenotypeIsNonViolent(x)).ToList();
                }
                return _cachedViolentCustomXenotypeList;
            }
        }

        /* Tech caching */
        public static ResearchProjectDef TechLevelBarrierUltra
        {
            get
            {
                if (_cachedTechLevelBarrierUltra == null)
                {
                    _cachedTechLevelBarrierUltra = DefDatabase<ResearchProjectDef>.GetNamed("ShipBasics", false);
                }
                return _cachedTechLevelBarrierUltra;
            }
        }
        public static ResearchProjectDef TechLevelBarrierSpacer
        {
            get
            {
                if (_cachedTechLevelBarrierSpacer == null)
                {
                    _cachedTechLevelBarrierSpacer = DefDatabase<ResearchProjectDef>.GetNamed("Fabrication", false);
                }
                return _cachedTechLevelBarrierSpacer;
            }
        }
        public static ResearchProjectDef TechLevelBarrierIndustrial
        {
            get
            {
                if (_cachedTechLevelBarrierIndustrial == null)
                {
                    _cachedTechLevelBarrierIndustrial = DefDatabase<ResearchProjectDef>.GetNamed("Electricity", false);
                }
                return _cachedTechLevelBarrierIndustrial;
            }
        }
        public static ResearchProjectDef TechLevelBarrierMedieval
        {
            get
            {
                if (_cachedTechLevelBarrierMedieval == null)
                {
                    _cachedTechLevelBarrierMedieval = DefDatabase<ResearchProjectDef>.GetNamed("Smithing", false);
                }
                return _cachedTechLevelBarrierMedieval;
            }
        }
        public static ResearchProjectDef TechTransportPods
        {
            get
            {
                if (_cachedTransportPods == null)
                {
                    _cachedTransportPods = DefDatabase<ResearchProjectDef>.GetNamed("TransportPod", false);
                }
                return _cachedTransportPods;
            }
        }

        /// <summary>
        /// For each building, a flattened list of all upgrades reachable through the upgrade tree.
        /// </summary>
        public static Dictionary<BuildingFCDef, List<BuildingUpgradeEntry>> UpgradeTrees
        {
            get
            {
                if (_cachedUpgradeTrees == null)
                {
                    _cachedUpgradeTrees = new Dictionary<BuildingFCDef, List<BuildingUpgradeEntry>>();
                    foreach (BuildingFCDef building in DefDatabase<BuildingFCDef>.AllDefsListForReading)
                    {
                        if (building.upgrades == null || building.upgrades.Count == 0) continue;
                        List<BuildingUpgradeEntry> tree = new List<BuildingUpgradeEntry>();
                        CollectUpgradeTree(building, 0, null, tree);
                        _cachedUpgradeTrees[building] = tree;
                    }
                }
                return _cachedUpgradeTrees;
            }
        }

        private static void CollectUpgradeTree(BuildingFCDef building, int depth, BuildingFCDef parent, List<BuildingUpgradeEntry> result)
        {
            if (building.upgrades == null) return;
            foreach (BuildingFCDef upgrade in building.upgrades)
            {
                result.Add(new BuildingUpgradeEntry { def = upgrade, depth = depth, parent = parent ?? building });
                CollectUpgradeTree(upgrade, depth + 1, upgrade, result);
            }
        }

        /// <summary>
        /// Reverse lookup: for each building, all buildings that list it in their requiredBuildings.
        /// </summary>
        public static Dictionary<BuildingFCDef, List<BuildingFCDef>> RequiredByMap
        {
            get
            {
                if (_cachedRequiredByMap == null)
                {
                    _cachedRequiredByMap = new Dictionary<BuildingFCDef, List<BuildingFCDef>>();
                    foreach (BuildingFCDef building in DefDatabase<BuildingFCDef>.AllDefsListForReading)
                    {
                        if (building.requiredBuildings == null) continue;
                        foreach (BuildingFCDef req in building.requiredBuildings)
                        {
                            if (!_cachedRequiredByMap.TryGetValue(req, out List<BuildingFCDef> list))
                            {
                                list = new List<BuildingFCDef>();
                                _cachedRequiredByMap[req] = list;
                            }
                            list.Add(building);
                        }
                    }
                }
                return _cachedRequiredByMap;
            }
        }
        public static List<FCEventCategoryDef> FCEventCategoryDefs
        {
            get
            {
                if (_cachedEventCategoryDefs == null)
                {
                    _cachedEventCategoryDefs = DefDatabase<FCEventCategoryDef>.AllDefsListForReading;
                }
                return _cachedEventCategoryDefs;
            }
        }
        /// <summary>
        /// MilitaryJobDefs that have a floatMenuLabelKey, i.e. hostile operations shown in the world gizmo menu.
        /// </summary>
        public static List<MilitaryJobDef> HostileMilitaryJobs
        {
            get
            {
                if (_cachedHostileMilitaryJobs == null)
                {
                    _cachedHostileMilitaryJobs = new List<MilitaryJobDef>();
                    foreach (MilitaryJobDef job in DefDatabase<MilitaryJobDef>.AllDefsListForReading)
                    {
                        if (job.floatMenuLabelKey != null)
                            _cachedHostileMilitaryJobs.Add(job);
                    }
                }
                return _cachedHostileMilitaryJobs;
            }
        }

        public static void InvalidateCache()
        {
            LogUtil.Message("Invalidating FactionCache...");
            _cachedColonyFaction = null;
            _cachedPlayerFaction = null;
            _cachedPawnKindDefs = null;
            _cachedFactionWorldComp = null;
            _cachedFactionDef = null;
            _cachedFields.Clear();
            _cachedRaceList = null;
            _cachedXenotypeList = null;
            _cachedViolentXenotypeList = null;
            _cachedAnimalKinds = null;
            _cachedCombatAnimalKinds = null;
            _cachedPackAnimalKinds = null;
            _cachedXenotypeViolenceDict = null;
            _cachedFCPolicyDefs = null;
            _cachedFCPolicyDescs = null;
            _cachedUpgradeTrees = null;
            _cachedRequiredByMap = null;
            _cachedEventCategoryDefs = null;
            _cachedHostileMilitaryJobs = null;

            _cachedTechLevelBarrierUltra = null;
            _cachedTechLevelBarrierSpacer = null;
            _cachedTechLevelBarrierIndustrial = null;
            _cachedTechLevelBarrierMedieval = null;
            _cachedTransportPods = null;

            InvalidateCustomXenotypeCache();
        }
        /* Custom xenotypes are actually expected to change while the game is loaded, and thus we may have to refresh that specific cache more frequently than the rest.
         * Hence, it gets its own function. */
        public static void InvalidateCustomXenotypeCache()
        {
            _cachedCustomXenotypeList = null;
            _cachedViolentCustomXenotypeList = null;
            _cachedCustomXenotypeDecoder = null;
            _cachedCustomXenotypeViolenceDict = null;

            _checkedForNonViolentXenos = false;
            _cachedNonViolentXenosExist = false;
        }
    }
}
