using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace FactionColonies.util
{
    public class XenotypeFilter : IExposable
    {
        //TODO: once the new xenotype/race filter is working, add support for choosing the type of animals that the faction uses for caravans and security
        //TODO: presently (2026-02-23), if only non-violent xenotypes are selected in the filter, then we will still generate Baseliners if the would-be-generated pawn must be capable of violence.
        //        This isn't *super* desirable. It would be preferable to let people have a faction full of non-violent hippies if they want. But actually coding that up will be much trickier than what we have now.
        private FactionDef faction;
        private FactionFC factionFc;
        private MilitaryCustomizationUtil militaryUtil;
        private List<TraderKindDef> origBaseTraderKinds = new List<TraderKindDef>();


        private Dictionary<XenotypeDef, SecurityGuardList> securityGuardsByXenotype = new Dictionary<XenotypeDef, SecurityGuardList>();
        private Dictionary<string, SecurityGuardList> securityGuardsByCustomXenotype = new Dictionary<string, SecurityGuardList>();
        /* Xenotype Weights */
        /* Set to private to force other classes to go through our functions when interacting with the dictionary, to properly maintain the cache. */
        private Dictionary<XenotypeDef, float> xenotypeWeights = new Dictionary<XenotypeDef, float>();
        public Dictionary<XenotypeDef, float> XenotypeWeights => xenotypeWeights;
        private bool dirtyXenotypeTotalWeight = true;
        private float xenotypeTotalWeight = 0;
        public float XenotypeTotalWeight
        {
            get
            {
                if (dirtyXenotypeTotalWeight)
                {
                    xenotypeTotalWeight = 0;
                    if (xenotypeWeights.Count > 0)
                    {
                        foreach (float weight in xenotypeWeights.Values)
                        {
                            xenotypeTotalWeight += weight;
                        }
                    }
                    dirtyXenotypeTotalWeight = false;
                }
                return xenotypeTotalWeight;
            }
        }
        /* Custom Xenotype Weights */
        /* Set to private to force other classes to go through our functions when interacting with the dictionary, to properly maintain the cache. */
        private Dictionary<string, float> customXenotypeWeights = new Dictionary<string, float>();
        public Dictionary<string, float> CustomXenotypeWeights => customXenotypeWeights;
        private bool dirtyCustomXenotypeTotalWeight = true;
        private float customXenotypeTotalWeight = 0;
        public float CustomXenotypeTotalWeight
        {
            get
            {
                if (dirtyCustomXenotypeTotalWeight)
                {
                    customXenotypeTotalWeight = 0;
                    if (customXenotypeWeights.Count > 0)
                    {
                        foreach (float weight in customXenotypeWeights.Values)
                        {
                            customXenotypeTotalWeight += weight;
                        }
                    }
                    dirtyCustomXenotypeTotalWeight = false;
                }
                return customXenotypeTotalWeight;
            }
        }
        public float XenoCompleteWeight => XenotypeTotalWeight + CustomXenotypeTotalWeight;
        /* Race Weights */
        /* Only really relevant for Humanoid Alien Races. If HAR isn't active and there's only one valid human race, then all of the raceWeight stuff will effectively be skipped. */
        /* Set to private to force other classes to go through our functions when interacting with the dictionary, to properly maintain the cache. */
        private Dictionary<ThingDef, float> raceWeights = new Dictionary<ThingDef, float>();
        public Dictionary<ThingDef, float> RaceWeights => raceWeights;
        private bool dirtyRaceTotalWeight = true;
        private float raceTotalWeight = 0;
        public float RaceTotalWeight
        {
            get
            {
                if (dirtyRaceTotalWeight)
                {
                    raceTotalWeight = 0;
                    if (raceWeights.Count > 0)
                    {
                        foreach (float weight in raceWeights.Values)
                        {
                            raceTotalWeight += weight;
                        }
                    }
                    dirtyRaceTotalWeight = false;
                }
                return raceTotalWeight;
            }
        }
        private List<PawnKindDef> cachedGuardAnimals = null;
        public List<PawnKindDef> GuardAnimals
        {
            get
            {
                if (cachedGuardAnimals == null)
                {
                    cachedGuardAnimals = FactionCache.AllCombatAnimalKindDefs.OrderByDescending(def => def.combatPower).Take(3).Distinct().ToList();
                }
                return cachedGuardAnimals;
            }
        }
        private bool checkedForNonViolent = false;
        private bool cachedHasOnlyNonViolent = false;
        public bool OnlyNonViolentXenos
        {
            get
            {
                if (!checkedForNonViolent)
                {
                    cachedHasOnlyNonViolent = true;
                    if (xenotypeWeights?.Count > 0 && xenotypeWeights.Any(kvp => kvp.Value > 0 && !XenotypeNeedsSecurityGuards(kvp.Key)))
                    {
                        cachedHasOnlyNonViolent = false;
                    }
                    if (cachedHasOnlyNonViolent && customXenotypeWeights?.Count > 0)
                    {
                        foreach (string xenotypeName in customXenotypeWeights.Keys)
                        {
                            if (customXenotypeWeights[xenotypeName] > 0 && FactionCache.CustomXenotypesDecoder?.TryGetValue(xenotypeName, out CustomXenotype xenotype) == true)
                            {
                                if (!CustomXenotypeNeedsSecurityGuards(xenotype.name))
                                {
                                    cachedHasOnlyNonViolent = false;
                                    break;
                                }
                            }
                        }
                    }
                    if (XenoCompleteWeight == 0)
                    {
                        cachedHasOnlyNonViolent = false;
                    }
                    checkedForNonViolent = true;
                }
                return cachedHasOnlyNonViolent;
            }
        }

        private Dictionary<ThingDef, List<XenotypeDef>> raceXenoAssociations = new Dictionary<ThingDef, List<XenotypeDef>>();

        // Borrowed from RaceThingfilter
        private bool HasMissingPawnKindDefTypes => !faction.pawnGroupMakers[1].traders.Any() || !faction.pawnGroupMakers[0].options.Any() || !faction.pawnGroupMakers[3].options.Any() || WorldSettlementTraderTracker.BaseTraderKinds == null || !WorldSettlementTraderTracker.BaseTraderKinds.Any();

        public XenotypeFilter()
        {
        }

        public XenotypeFilter(FactionFC factionFc)
        {
            LogUtil.Message("Creating new XenotypeFilter");
            this.factionFc = factionFc;
            militaryUtil = factionFc.militaryCustomizationUtil;
            faction = FactionCache.EmpireFactionDef;
            origBaseTraderKinds.AddRange(faction.baseTraderKinds);
        }

        public void FinalizeInit(FactionFC factionFc)
        {
            this.factionFc = factionFc;
            militaryUtil = factionFc.militaryCustomizationUtil;
            faction = FactionCache.EmpireFactionDef;
            LogUtil.Message("XenotypeFilter FinalizeInit");

            if (xenotypeWeights == null)
            {
                xenotypeWeights = new Dictionary<XenotypeDef, float>();
            }
            if (customXenotypeWeights == null)
            {
                customXenotypeWeights = new Dictionary<string, float>();
            }
            if (raceWeights == null)
            {
                raceWeights = new Dictionary<ThingDef, float>();
            }
            if (securityGuardsByXenotype == null)
            {
                securityGuardsByXenotype = new Dictionary<XenotypeDef, SecurityGuardList>();
            }
            if (securityGuardsByCustomXenotype == null)
            {
                securityGuardsByCustomXenotype = new Dictionary<string, SecurityGuardList>();
            }

            if (XenoCompleteWeight == 0)
            {
                InitializeXenotypes();
            }
            if (RaceTotalWeight == 0)
            {
                InitializeRaces();
            }

            RefreshPawnGroupMakers();
            WorldSettlementTraderTracker.ReloadTraderKind();
        }
        /* Functions to interact with the xenotypeWeights and raceWeights dictionaries.
         * Due to caching tracking, we want to force other classes to go through our functions when interacting with the dictionary. */
        public void AddXenotypeWithWeight(XenotypeDef xenotype, float weight)
        {
            if (xenotypeWeights.ContainsKey(xenotype))
            {
                xenotypeWeights[xenotype] = weight;
            }
            else
            {
                xenotypeWeights.Add(xenotype, weight);
            }
            dirtyXenotypeTotalWeight = true;
            checkedForNonViolent = false;
        }
        public void AddCustomXenotypeWithWeight(CustomXenotype xenotype, float weight)
        {
            if (customXenotypeWeights.ContainsKey(xenotype.name))
            {
                customXenotypeWeights[xenotype.name] = weight;
            }
            else
            {
                customXenotypeWeights.Add(xenotype.name, weight);
            }
            dirtyCustomXenotypeTotalWeight = true;
            checkedForNonViolent = false;
        }
        public void AddRaceWithWeight(ThingDef race, float weight)
        {
            if (raceWeights.ContainsKey(race))
            {
                raceWeights[race] = weight;
            }
            else
            {
                raceWeights.Add(race, weight);
            }
            dirtyRaceTotalWeight = true;
        }
        public bool RemoveXenotype(XenotypeDef xenotype)
        {
            if (xenotypeWeights.ContainsKey(xenotype))
            {
                xenotypeWeights.Remove(xenotype);
                dirtyXenotypeTotalWeight = true;
                checkedForNonViolent = false;
                return true;
            }
            return false;
        }
        public bool RemoveCustomXenotype(string xenotype)
        {
            if (customXenotypeWeights.ContainsKey(xenotype))
            {
                customXenotypeWeights.Remove(xenotype);
                dirtyCustomXenotypeTotalWeight = true;
                checkedForNonViolent = false;
                return true;
            }
            return false;
        }
        public bool RemoveRace(ThingDef race)
        {
            if (raceWeights.ContainsKey(race))
            {
                raceWeights.Remove(race);
                dirtyRaceTotalWeight = true;
                return true;
            }
            return false;
        }
        public void ClearXenotypeWeights()
        {
            xenotypeWeights.Clear();
            dirtyXenotypeTotalWeight = true;
            checkedForNonViolent = false;
        }
        public void ClearCustomXenotypeWeights()
        {
            customXenotypeWeights.Clear();
            dirtyCustomXenotypeTotalWeight = true;
            checkedForNonViolent = false;
        }
        public void ClearRaceWeights()
        {
            raceWeights.Clear();
            dirtyRaceTotalWeight = true;
        }
        public void CullXenotypeWeights()
        {
            // If the sum of the xenotypes is 0, then we've found ourselves in an invalid configuration. Re-enable all xenotypes.
            if (XenoCompleteWeight == 0)
            {
                LogUtil.Warning($"XenoCompleteWeight == 0 in CullXenotypeWeights. Re-enabling all xenotypes.");
                InitializeXenotypeWeights();
                InitializeCustomXenotypeWeights();
            }
            else
            {
                List<XenotypeDef> toRemove = new List<XenotypeDef>();
                foreach (XenotypeDef xenotype in xenotypeWeights.Keys)
                {
                    if (xenotypeWeights[xenotype] == 0)
                    {
                        toRemove.Add(xenotype);
                    }
                }
                for (int i = 0; i < toRemove.Count; i++)
                {
                    RemoveXenotype(toRemove[i]);
                }
            }
        }
        public void CullCustomXenotypeWeights()
        {
            // If the sum of the xenotypes is 0, then we've found ourselves in an invalid configuration. Re-enable all xenotypes.
            if (XenoCompleteWeight == 0)
            {
                LogUtil.Warning($"XenoCompleteWeight == 0 in CullCustomXenotypeWeights. Re-enabling all xenotypes.");
                InitializeXenotypeWeights();
                InitializeCustomXenotypeWeights();
            }
            else
            {
                List<string> toRemove = new List<string>();
                foreach (string xenotype in customXenotypeWeights.Keys)
                {
                    if (customXenotypeWeights[xenotype] == 0)
                    {
                        toRemove.Add(xenotype);
                    }
                }
                for (int i = 0; i < toRemove.Count; i++)
                {
                    RemoveCustomXenotype(toRemove[i]);
                }
            }
        }
        public void CullRaceWeights()
        {
            // If the sum of the races is 0, then we've found ourselves in an invalid configuration. Re-enable all races.
            if (RaceTotalWeight == 0)
            {
                LogUtil.Warning($"RaceTotalWeight == 0 in CullRaceWeights. Re-enabling all races.");
                InitializeRaceWeights();
            }
            else
            {
                List<ThingDef> toRemove = new List<ThingDef>();
                foreach (ThingDef race in raceWeights.Keys)
                {
                    if (raceWeights[race] == 0)
                    {
                        toRemove.Add(race);
                    }
                }
                for (int i = 0; i < toRemove.Count; i++)
                {
                    RemoveRace(toRemove[i]);
                }
            }
        }
        public void CullWeights()
        {
            CullXenotypeWeights();
            CullCustomXenotypeWeights();
            CullRaceWeights();

            RefreshPawnGroupMakers();
        }
        public void DebugPrintXenotypeWeights()
        {
            foreach (XenotypeDef def in FactionCache.XenotypeDefs)
            {
                LogUtil.Message($"Xenotype: {def.LabelCap}, Weight: {GetXenotypeWeight(def, true)}");
            }
        }
        public void DebugPrintCustomXenotypeWeights()
        {
            foreach (CustomXenotype xeno in FactionCache.CustomXenotypes)
            {
                LogUtil.Message($"Custom Xenotype: {xeno.name}, Weight: {GetCustomXenotypeWeight(xeno.name, true)}");
            }
        }
        public void DebugPrintRaceWeights()
        {
            foreach (ThingDef race in FactionCache.HumanlikeRaces)
            {
                LogUtil.Message($"Race: {race.LabelCap}, Weight: {GetRaceWeight(race, true)}");
            }
        }
        public float GetXenotypeWeight(XenotypeDef xenotype, bool debug = false)
        {
            if (xenotypeWeights.ContainsKey(xenotype))
            {
                return xenotypeWeights[xenotype];
            }
            if (debug)
            {
                LogUtil.Message($"Xenotype {xenotype.LabelCap} not present in the weights dictionary");
            }
            return 0f;
        }
        public float GetCustomXenotypeWeight(string xenotype, bool debug = false)
        {
            if (customXenotypeWeights.ContainsKey(xenotype))
            {
                return customXenotypeWeights[xenotype];
            }
            if (debug)
            {
                LogUtil.Message($"Custom Xenotype {xenotype} not present in the weights dictionary");
            }
            return 0f;
        }
        public float GetRaceWeight(ThingDef race, bool debug = false)
        {
            if (raceWeights.ContainsKey(race))
            {
                return raceWeights[race];
            }
            if (debug)
            {
                LogUtil.Message($"Race {race.LabelCap} not present in the weights dictionary");
            }
            return 0f;
        }
        public float GetXenotypeChance(XenotypeDef xenotype)
        {
            if (XenoCompleteWeight == 0)
                return 0f;

            return GetXenotypeWeight(xenotype) / XenoCompleteWeight;
        }
        public float GetCustomXenotypeChance(string xenotype)
        {
            if (XenoCompleteWeight == 0)
                return 0f;

            return GetCustomXenotypeWeight(xenotype) / XenoCompleteWeight;
        }
        public float GetRaceChance(ThingDef race)
        {
            if (RaceTotalWeight == 0)
                return 0f;

            return GetRaceWeight(race) / RaceTotalWeight;
        }
        public void ValidateCustomXenotypes()
        {
            FactionCache.InvalidateCustomXenotypeCache();
            List<string> customs = customXenotypeWeights.Keys.ToList();
            if (customs.Count > 0)
            {
                foreach (string xeno in customs)
                {
                    if (!FactionCache.CustomXenotypesDecoder.ContainsKey(xeno))
                    {
                        RemoveCustomXenotype(xeno);
                    }
                }
            }
        }
        private void InitializeXenotypeWeights(bool initAllTypes = true)
        {
            ClearXenotypeWeights();
            if (initAllTypes)
            {
                foreach (XenotypeDef xenotype in FactionCache.XenotypeDefs)
                {
                    if (xenotype.IsXenotypeWithLabel() && xenotype != XenotypeDefOf.Baseliner)
                    {
                        AddXenotypeWithWeight(xenotype, 1);
                        SetupSecurityGuards(xenotype); //will inevitably need tweaking
                    }
                }
            }
            // Always include Baseliner xenotype and Human race as defaults
            if (!XenotypeWeights.ContainsKey(XenotypeDefOf.Baseliner))
            {
                AddXenotypeWithWeight(XenotypeDefOf.Baseliner, 1);
                SetupSecurityGuards(XenotypeDefOf.Baseliner);
            }

            if (XenotypeTotalWeight == 0)
            {
                LogUtil.Error("No enabled xenotypes after InitializeXenotypeWeights()!");
            }
        }
        private void InitializeCustomXenotypeWeights(bool initAllTypes = true)
        {
            ClearCustomXenotypeWeights();
            if (initAllTypes)
            {
                if (FactionCache.CustomXenotypes.Count > 0)
                {
                    foreach (CustomXenotype xenotype in FactionCache.CustomXenotypes)
                    {
                        AddCustomXenotypeWithWeight(xenotype, 1);
                        SetupSecurityGuards(xenotype.name);
                    }
                }
            }
        }
        private void InitializeRaceWeights(bool initAllTypes = true)
        {
            ClearRaceWeights();
            if (initAllTypes)
            {
                foreach (ThingDef race in FactionCache.HumanlikeRaces)
                {
                    if (race != ThingDefOf.Human)
                    {
                        AddRaceWithWeight(race, 1);
                    }
                }
            }
            if (!RaceWeights.ContainsKey(ThingDefOf.Human))
            {
                AddRaceWithWeight(ThingDefOf.Human, 1);
            }

            if (RaceTotalWeight == 0)
            {
                LogUtil.Error("No enabled races after InitializeRaceWeights()!");
            }
        }

        private void InitializeXenotypes(bool initAllTypes = true)
        {
            LogUtil.Message("Initializing Xenotype Weights in XenotypeFilter...");
            InitializeXenotypeWeights(initAllTypes);
            InitializeCustomXenotypeWeights(initAllTypes);
        }
        private void InitializeRaces(bool initAllTypes = true)
        {
            InitializeRaceWeights(initAllTypes);
        }
        public bool IsValidXenotypeForRace(ThingDef inputRace, XenotypeDef xenotype)
        {
            /* If the race is the default Human, then only reject the xenotype if it is associated with a non-human race.
             *   Meant to handle mods that add xenotypes for HAR races. */
            if (inputRace == ThingDefOf.Human)
            {
                /* Always allow Baseliner for Humans. */
                if (xenotype == XenotypeDefOf.Baseliner)
                {
                    return true;
                }
                if (raceXenoAssociations.Count > 0)
                {
                    foreach (ThingDef race in raceXenoAssociations.Keys)
                    {
                        if (race == ThingDefOf.Human)
                        {
                            continue;
                        }
                        if (raceXenoAssociations[race].Contains(xenotype))
                        {
                            return false;
                        }
                    }
                }
                return true;
            }
            /* If the race is NOT the default Human, then only accept the xenotype if it is associated with the given race */
            else
            {
                return raceXenoAssociations.ContainsKey(inputRace) && raceXenoAssociations[inputRace].Contains(xenotype);
            }
        }
        public bool IsValidCustomXenotypeForRace(ThingDef race, string xenotype)
        {
            /* As far as I'm aware, you can't associated custom xenotypes with non-human races, even with HAR.
             * But just in case I'm wrong, or there's some other way around this, I've included this function as an
             * easy way to rectify the custom xenotype validity check.
             * For now, though, we simply return TRUE if the race is Human, and false otherwise. */
            return race == ThingDefOf.Human;
        }
        public bool IsValidXenotypeForRequest(PawnGenerationRequest request, XenotypeDef xenotype)
        {
            /* If the xenotype isn't even enabled in the filter, then exit now */
            if (!xenotypeWeights.ContainsKey(xenotype) || xenotypeWeights[xenotype] <= 0)
            {
                return false;
            }
            if (!IsValidXenotypeForRace(request.KindDef.race, xenotype))
            {
                return false;
            }
            bool needsViolence = request.MustBeCapableOfViolence
                || (request.KindDef.weaponTags != null && request.KindDef.weaponTags.Count > 0)
                || (request.KindDef.requiredWorkTags & WorkTags.Violent) != WorkTags.None;
            if (needsViolence && FactionCache.XenotypeIsNonViolent(xenotype))
            {
                return false;
            }
            if (PawnKindXenotypeChance(request.KindDef, xenotype) <= 0)
            {
                return false;
            }
            if (!CanGeneListDoRequiredWork(request.KindDef.requiredWorkTags, xenotype.genes))
            {
                return false;
            }

            return true;
        }
        public bool IsValidCustomXenotypeForRequest(PawnGenerationRequest request, string xenotypeName)
        {
            /* If the xenotype isn't even enabled in the filter, then exit now */
            if (!customXenotypeWeights.ContainsKey(xenotypeName) || customXenotypeWeights[xenotypeName] <= 0)
            {
                return false;
            }
            if (!IsValidCustomXenotypeForRace(request.KindDef.race, xenotypeName))
            {
                return false;
            }
            bool needsViolence = request.MustBeCapableOfViolence
                || (request.KindDef.weaponTags != null && request.KindDef.weaponTags.Count > 0)
                || (request.KindDef.requiredWorkTags & WorkTags.Violent) != WorkTags.None;
            if (needsViolence && FactionCache.CustomXenotypeIsNonViolent(xenotypeName))
            {
                return false;
            }
            if (FactionCache.CustomXenotypesDecoder.TryGetValue(xenotypeName, out CustomXenotype xenotype))
            {
                if (!CanGeneListDoRequiredWork(request.KindDef.requiredWorkTags, xenotype.genes))
                {
                    return false;
                }
            }
            return true;
        }

        private void SetupSecurityGuards(XenotypeDef xenotype)
        {
            if (!securityGuardsByXenotype.ContainsKey(xenotype))
            {
                securityGuardsByXenotype[xenotype] = new SecurityGuardList();
            }
            
            if (FactionCache.XenotypeIsNonViolent(xenotype))
            {
                // Find suitable security guard animals
                securityGuardsByXenotype[xenotype].SetRange(GuardAnimals);
            }
        }
        private void SetupSecurityGuards(string xenotype)
        {
            if (!securityGuardsByCustomXenotype.ContainsKey(xenotype))
            {
                securityGuardsByCustomXenotype[xenotype] = new SecurityGuardList();
            }

            if (FactionCache.CustomXenotypeIsNonViolent(xenotype))
            {
                securityGuardsByCustomXenotype[xenotype].SetRange(GuardAnimals);
            }
        }

        public static bool NameNeedsSecurityGuards(string name)
        {
            string xenotypeName = name.ToLower();

            // Common non-violent or weak xenotypes that would benefit from security guards
            if (xenotypeName.Contains("pacifist") ||
                xenotypeName.Contains("gentle") ||
                xenotypeName.Contains("weak") ||
                xenotypeName.Contains("frail") ||
                xenotypeName.Contains("nearsighted") ||
                xenotypeName.Contains("peaceful"))
            {
                return true;
            }
            else
            {
                return false;
            }
        }
        public static bool GenesNeedSecurityGuards(List<GeneDef> genes)
        {
            if (!CanGeneListDoViolentWork(genes))
            {
                return true;
            }
            foreach (GeneDef gene in genes)
            {
                if (gene.statFactors != null)
                {
                    foreach (var statModifier in gene.statFactors)
                    {
                        // Check for severely reduced combat stats
                        if ((statModifier.stat == StatDefOf.ShootingAccuracyPawn ||
                             statModifier.stat == StatDefOf.MeleeHitChance ||
                             statModifier.stat == StatDefOf.MeleeDodgeChance) &&
                            statModifier.value < 0.5f)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
        public static bool CanGeneListDoRequiredWork(WorkTags requiredTags, List<GeneDef> genes)
        {
            bool canDoWork = true;
            if (requiredTags != WorkTags.None && genes != null && genes.Count > 0)
            {
                WorkTags geneListDisabledTags = WorkTags.None;
                foreach (GeneDef gene in genes)
                {
                    geneListDisabledTags |= gene.disabledWorkTags;
                }
                if ((geneListDisabledTags & requiredTags) != WorkTags.None)
                {
                    canDoWork = false;
                }
            }
            return canDoWork;
        }
        public static bool CanGeneListDoViolentWork(List<GeneDef> genes)
        {
            if (!CanGeneListDoRequiredWork(WorkTags.Violent, genes))
            {
                return false;
            }
            if (!CanGeneListDoRequiredWork(WorkTags.AllWork, genes))
            {
                return false;
            }
            return true;
        }
        public static bool XenotypeNeedsSecurityGuards(XenotypeDef xenotype)
        {
            if (xenotype?.canGenerateAsCombatant == false)
            {
                return true;
            }
            if (xenotype?.genes is null)
            {
                return false;
            }

            // Simple heuristic: Check the xenotype name for known non-violent types
            // Common non-violent or weak xenotypes that would benefit from security guards
            if (NameNeedsSecurityGuards(xenotype.defName))
            {
                return true;
            }

            // Check for genes that explicitly reduce combat effectiveness
            if (GenesNeedSecurityGuards(xenotype.genes))
            {
                return true;
            }

            // For now, assume most xenotypes don't need security guards unless specifically flagged
            return false;
        }
        public static bool CustomXenotypeNeedsSecurityGuards(string xenotypeName)
        {
            CustomXenotype xenotype = null;
            if (!FactionCache.CustomXenotypesDecoder.TryGetValue(xenotypeName, out xenotype))
            {
                LogUtil.Error($"Custom xenotype {xenotypeName} does not appear in the xenotype decoder dictionary");
                return false;
            }
            if (xenotype?.genes is null) return false;

            // Simple heuristic: Check the xenotype name for known non-violent types
            // Common non-violent or weak xenotypes that would benefit from security guards
            if (NameNeedsSecurityGuards(xenotype.name))
            {
                return true;
            }

            // Check for genes that explicitly reduce combat effectiveness
            if (GenesNeedSecurityGuards(xenotype.genes))
            {
                return true;
            }

            // For now, assume most xenotypes don't need security guards unless specifically flagged
            return false;
        }

        public void ResetToAllXenotypes()
        {
            InitializeXenotypes();
            RefreshPawnGroupMakers();
        }
        public void ResetToBaselinerXenotypeOnly()
        {
            InitializeXenotypes(false);
            RefreshPawnGroupMakers();
        }
        public void ResetToAllRaces()
        {
            InitializeRaces();
            RefreshPawnGroupMakers();
        }
        public void ResetToHumanRaceOnly()
        {
            InitializeRaces(false);
            RefreshPawnGroupMakers();
        }

        /// <summary>
        /// Get all available security guard animal kinds from all xenotypes
        /// </summary>
        public List<PawnKindDef> GetAvailableSecurityGuards()
        {
            var allGuards = new List<PawnKindDef>();
            if (securityGuardsByXenotype.Count > 0)
            {
                foreach (var guardList in securityGuardsByXenotype.Values)
                {
                    allGuards.AddRange(guardList.List);
                }
            }
            if (securityGuardsByCustomXenotype.Count > 0)
            {
                foreach (var guardList in securityGuardsByCustomXenotype.Values)
                {
                    allGuards.AddRange(guardList.List);
                }
            }
            return allGuards.Distinct().ToList();
        }
        /// <summary>
        /// Attempts to find a pawnKindDef for the given race that is not a fighter and not a trader.
        /// </summary>
        /// <param name="race"></param>
        private PawnKindDef GetBasicPawnKindDefForRace(ThingDef race)
        {
            if (race is null)
            {
                return null;
            }

            PawnKindDef outputDef = null;
            List<PawnKindDef> possibleDefs = GetPawnKindDefsForRace(race);
            if (possibleDefs.Count == 0)
            {
                return null;
            }
            outputDef = possibleDefs.First((PawnKindDef def) => !def.trader && !def.isFighter && !def.isBoss && def.label != "mercenary");
            return outputDef;
        }
        private bool PawnKindRaceCheck(PawnKindDef def, ThingDef race, bool lockTechLevel)
        {
            if (lockTechLevel)
            {
                if (def.defaultFactionDef is null)
                {
                    return def.race == race;
                }
                else
                {
                    return def.race == race && (def.defaultFactionDef.techLevel <= factionFc.techLevel);
                }
            }
            else
            {
                return def.race == race;
            }
        }
        private float PawnKindXenotypeChance(PawnKindDef def, XenotypeDef xenotype)
        {
            float remainingChance = 1f;
            int xenoCount = def?.xenotypeSet?.Count ?? 0;
            if (xenoCount > 0)
            {
                for (int i = 0; i < def.xenotypeSet.Count; i++)
                {
                    if (def.xenotypeSet[i].xenotype == xenotype)
                    {
                        return def.xenotypeSet[i].chance;
                    }
                    else
                    {
                        remainingChance -= def.xenotypeSet[i].chance;
                    }
                }
            }
            // Pawnkinds that have a xenotype in their xenotypeSet that is disallowed in the xenotype filter should have been culled by this point.
            //   So we're going to assume that every xenotype we see in the set is one that would be valid.
            int numXenosRemaining = xenotypeWeights.Count - xenoCount;
            if (numXenosRemaining <= 0)
            {
                return remainingChance;
            }
            else
            {
                return remainingChance / (float)(numXenosRemaining);
            }
        }
        private List<PawnKindDef> GetPawnKindDefsForRace(ThingDef race)
        {
            List<PawnKindDef> output = FactionCache.AllPawnKindDefs.Where(def => PawnKindRaceCheck(def, race, true)).ToList();
            LogUtil.Message($"GetPawnKindDefsForRace: found {output.Count} PawnKindDefs for race {race.LabelCap}");

            if (output.Count == 0 || !output.Any((PawnKindDef def) => def.trader) || !output.Any((PawnKindDef def) => def.isFighter))
            {
                output = FactionCache.AllPawnKindDefs.Where(def => PawnKindRaceCheck(def, race, false)).ToList();
                LogUtil.Message($"GetPawnKindDefsForRace: regenerated PawnKindDefs list for race {race.LabelCap} without techlevel restriction. Final count: {output.Count}");
            }
            return output;
        }
        private void ReweightPawnGenOptionsForRace(List<PawnGenOption> options, ThingDef race)
        {
            if (raceWeights.ContainsKey(race))
            {
                float raceWeight = raceWeights[race];
                float numOptionsForRace = options.Count((PawnGenOption op) => op.kind.race == race);
                float newWeight = numOptionsForRace == 0 ? 0 : raceWeight / numOptionsForRace;

                foreach (PawnGenOption op in options)
                {
                    if (op.kind.race == race)
                        op.selectionWeight = newWeight;
                }
            }
        }
        private void ReweightPawnGroupMakers()
        {
            foreach (ThingDef race in RaceWeights.Keys)
            {
                ReweightPawnGenOptionsForRace(faction.pawnGroupMakers[0].options, race);
                ReweightPawnGenOptionsForRace(faction.pawnGroupMakers[1].options, race);
                ReweightPawnGenOptionsForRace(faction.pawnGroupMakers[1].guards, race);
                ReweightPawnGenOptionsForRace(faction.pawnGroupMakers[1].traders, race);
                ReweightPawnGenOptionsForRace(faction.pawnGroupMakers[2].options, race);
                ReweightPawnGenOptionsForRace(faction.pawnGroupMakers[3].options, race);
            }
        }
        private void SetFallbackPawnGroupMakers()
        {
            if (!faction.pawnGroupMakers[1].traders.Any()) //traders
            {
                LogUtil.Warning("RefreshPawnGroupMakers: Failed to find any trader PawnKindDefs, attempting fallbacks");
                PawnKindDef trader = null;

                /* We will first try to make an all-new pawnkinddef with an enabled race */
                PawnKindDef baseDef = GetBasicPawnKindDefForRace(GetRandomRace());
                if (baseDef != null)
                {
                    trader = baseDef.ShallowClone();
                    trader.trader = true;
                    LogUtil.Warning($"RefreshPawnGroupMakers: Created new PawnKindDef for traders using original PawnKindDef {baseDef.defName} of race {baseDef.race.defName}");
                }
                // If trader is still null, attempt to find a fallback option for the Human race
                if (trader is null)
                {
                    var humanPawns = FactionCache.AllPawnKindDefs.Where(def => PawnKindRaceCheck(def, ThingDefOf.Human, true));
                    trader = humanPawns.FirstOrDefault((PawnKindDef def) => def.trader);
                    LogUtil.Message("RefreshPawnGroupMakers: Found trader pawnKindDef for human race");
                }
                if (trader is null)
                {
                    LogUtil.Error("RefreshPawnGroupMakers: Attempted to find fallback PawnKindDef for traders, but failed!");
                }
                else
                {
                    var pawnOption = new PawnGenOption
                    {
                        kind = trader,
                        selectionWeight = 1
                    };
                    faction.pawnGroupMakers[1].traders.Add(pawnOption);
                }
            }
            if (!faction.pawnGroupMakers[0].options.Any()) //combat
            {
                LogUtil.Warning("RefreshPawnGroupMakers: Failed to find any combat PawnKindDefs, attempting fallbacks");
                PawnKindDef fighter = null;

                /* We will first try to make an all-new pawnkinddef with an enabled race */
                PawnKindDef baseDef = GetBasicPawnKindDefForRace(GetRandomRace());
                if (baseDef != null)
                {
                    fighter = baseDef.ShallowClone();
                    fighter.isFighter = true;
                    LogUtil.Warning($"RefreshPawnGroupMakers: Created new PawnKindDef for combat using original PawnKindDef {baseDef.defName} of race {baseDef.race.defName}");
                }
                // If fighter is still null, attempt to find a fallback option for the Human race
                if (fighter is null)
                {
                    var humanPawns = FactionCache.AllPawnKindDefs.Where(def => PawnKindRaceCheck(def, ThingDefOf.Human, true));
                    fighter = humanPawns.FirstOrDefault((PawnKindDef def) => def.isFighter);
                    LogUtil.Message("RefreshPawnGroupMakers: Found combat pawnKindDef for human race");
                }
                if (fighter is null)
                {
                    LogUtil.Error("RefreshPawnGroupMakers: Attempted to find fallback PawnKindDef for fighters, but failed!");
                }
                else
                {
                    var pawnOption = new PawnGenOption
                    {
                        kind = fighter,
                        selectionWeight = 1
                    };
                    faction.pawnGroupMakers[0].options.Add(pawnOption);
                }
            }
            if (!faction.pawnGroupMakers[3].options.Any()) //peaceful
            {
                LogUtil.Warning("RefreshPawnGroupMakers: Failed to find any peaceful PawnKindDefs, attempting fallbacks");
                PawnKindDef peaceful = null;

                PawnKindDef baseDef = GetBasicPawnKindDefForRace(GetRandomRace());
                if (baseDef != null)
                {
                    peaceful = baseDef;
                    LogUtil.Message($"RefreshPawnGroupMakers: Found peaceful PawnKindDef {baseDef.defName} of race {baseDef.race.defName}");
                }
                // If peaceful is still null, attempt to find a fallback option for the Human race
                if (peaceful is null)
                {
                    var humanPawns = FactionCache.AllPawnKindDefs.Where(def => PawnKindRaceCheck(def, ThingDefOf.Human, true));
                    peaceful = humanPawns.FirstOrDefault((PawnKindDef def) => def.label != "mercenary");
                    LogUtil.Message("RefreshPawnGroupMakers: Found peaceful pawnKindDef for human race");
                }
                if (peaceful is null)
                {
                    LogUtil.Error("RefreshPawnGroupMakers: Attempted to find fallback PawnKindDef for peaceful, but failed!");
                }
                else
                {
                    var pawnOption = new PawnGenOption
                    {
                        kind = peaceful,
                        selectionWeight = 1
                    };
                    faction.pawnGroupMakers[3].options.Add(pawnOption);
                }
            }
            if (WorldSettlementTraderTracker.BaseTraderKinds != null && !WorldSettlementTraderTracker.BaseTraderKinds.Any())
            {
                LogUtil.Warning("RefreshPawnGroupMakers: WorldSettlementTraderTracker found no valid baseTraderKinds. Attempting human race fallback");
                faction.baseTraderKinds.AddRange(origBaseTraderKinds);
                WorldSettlementTraderTracker.ReloadTraderKind();
            }
        }
        private void SetPawnGroupMakers()
        {
            /* For each allowed race, find all associated PawnKindDefs. Add every such def that does not contain a disallowed Xenotype to the pawnGroupMakers.
             *   Check each associated PawnKindDef for a xenotypeSet. If there is one, add the xenotypes to the raceXenoAssociations dictionary. */
            /* The handling of xenotypes will be done in a prefix patch on PawnGenerator.GeneratePawn(PawnGenerationRequest). */
            raceXenoAssociations.Clear();

            foreach (ThingDef race in RaceWeights.Keys)
            {
                List<PawnKindDef> humanPawns = GetPawnKindDefsForRace(race);
                List<XenotypeDef> associatedXenotypes = new List<XenotypeDef>();
                foreach (PawnKindDef pawnKind in humanPawns)
                {
                    if (pawnKind.xenotypeSet != null)
                    {
                        /* We need to loop over the xenotypeSet twice: once to determine if this is a valid PawnKindDef, and *then* to add to the associated xenotype dictionary */
                        bool isValid = true;
                        for (int i = 0; i < pawnKind.xenotypeSet.Count && isValid; i++)
                        {
                            if (race != ThingDefOf.Human && pawnKind.xenotypeSet[i].xenotype == XenotypeDefOf.Baseliner)
                            {
                                /* HAR compat: For non-human races, we also consider the Baseliner xenotype to be valid, even if it has been disabled in the filter. */
                                continue;
                            }
                            else if (pawnKind.xenotypeSet[i].chance > 0 && GetXenotypeWeight(pawnKind.xenotypeSet[i].xenotype) == 0)
                            {
                                LogUtil.Message($"SetPawnGroupMakers: pawnKind {pawnKind.defName} has disallowed xenotype {pawnKind.xenotypeSet[i].xenotype} in its xenotypeset");
                                isValid = false;
                            }
                        }
                        if (!isValid)
                        {
                            continue;
                        }
                        for (int i = 0; i < pawnKind.xenotypeSet.Count; i++)
                        {
                            if (pawnKind.xenotypeSet[i].chance > 0)
                            {
                                XenotypeDef xenotype = pawnKind.xenotypeSet[i].xenotype;
                                associatedXenotypes.Add(xenotype);
                            }
                        }
                    }

                    var pawnOption = new PawnGenOption
                    {
                        kind = pawnKind,
                        selectionWeight = RaceWeights[race]
                    };

                    bool isViolenceCapable = FCPawnGenerator.IsViolenceCapablePawnKind(pawnKind);

                    // Settlement group maker: PawnGroupKindWorker_Normal hardcodes mustBeCapableOfViolence=true,
                    // so only add PawnKindDefs that can plausibly generate violence-capable pawns
                    if (isViolenceCapable)
                    {
                        faction.pawnGroupMakers[2].options.Add(pawnOption); // Settlement
                        LogUtil.Message("SetPawnGroupMakers: added " + pawnKind.defName + " (combatPower=" + pawnKind.combatPower + ") to Settlement");
                    }

                    if (pawnKind.label != "mercenary")
                    {
                        faction.pawnGroupMakers[1].options.Add(pawnOption); // Trader
                        faction.pawnGroupMakers[3].options.Add(pawnOption); // Peaceful
                        LogUtil.Message("SetPawnGroupMakers: added " + pawnKind.defName + " (combatPower=" + pawnKind.combatPower + ") to Trader, Peaceful");
                    }

                    if (pawnKind.isFighter)
                    {
                        faction.pawnGroupMakers[0].options.Add(pawnOption); // Combat
                        faction.pawnGroupMakers[1].guards.Add(pawnOption); // Trader guards
                        LogUtil.Message("SetPawnGroupMakers: added " + pawnKind.defName + " (combatPower=" + pawnKind.combatPower + ") to Combat, Trader guards");
                    }
                    else if (pawnKind.factionLeader)
                    {
                        faction.pawnGroupMakers[0].options.Add(pawnOption); // Combat (needed for TryGenerateNewLeader)
                        LogUtil.Message("SetPawnGroupMakers: added " + pawnKind.defName + " (combatPower=" + pawnKind.combatPower + ") to Combat (leader)");
                    }

                    if (pawnKind.trader)
                    {
                        faction.pawnGroupMakers[1].traders.Add(pawnOption);
                        LogUtil.Message("SetPawnGroupMakers: added " + pawnKind.defName + " (combatPower=" + pawnKind.combatPower + ") to Traders");
                    }
                }

                if (associatedXenotypes.Count > 0)
                {
                    raceXenoAssociations[race] = associatedXenotypes.Distinct().ToList();
                }
            }
            ReweightPawnGroupMakers();
            foreach (XenotypeDef xenotype in XenotypeWeights.Keys)
            {
                //TODO: see if anything else needs to be done
                // Add security guards for xenotypes that need them
                if (securityGuardsByXenotype.ContainsKey(xenotype) && securityGuardsByXenotype[xenotype].List.Any())
                {
                    foreach (var guardAnimal in securityGuardsByXenotype[xenotype].List)
                    {
                        var guardOption = new PawnGenOption
                        {
                            kind = guardAnimal,
                            selectionWeight = 1
                        };

                        faction.pawnGroupMakers[0].options.Add(guardOption); // Combat
                        faction.pawnGroupMakers[1].guards.Add(guardOption); // Trader guards
                        LogUtil.Message("SetPawnGroupMakers: added " + guardAnimal.defName + " (combatPower=" + guardAnimal.combatPower + ") to Combat, Trader guards (xenotype guard for " + xenotype.defName + ")");
                    }
                }
            }

            if (customXenotypeWeights.Count > 0)
            {
                foreach (string xenotype in CustomXenotypeWeights.Keys)
                {
                    //TODO
                    // Add security guards for xenotypes that need them
                    if (securityGuardsByCustomXenotype.ContainsKey(xenotype) && securityGuardsByCustomXenotype[xenotype].List.Any())
                    {
                        foreach (var guardAnimal in securityGuardsByCustomXenotype[xenotype].List)
                        {
                            var guardOption = new PawnGenOption
                            {
                                kind = guardAnimal,
                                selectionWeight = 1
                            };

                            faction.pawnGroupMakers[0].options.Add(guardOption); // Combat
                            faction.pawnGroupMakers[1].guards.Add(guardOption); // Trader guards
                            LogUtil.Message("SetPawnGroupMakers: added " + guardAnimal.defName + " (combatPower=" + guardAnimal.combatPower + ") to Combat, Trader guards (custom xenotype guard for " + xenotype + ")");
                        }
                    }
                }
            }
        }
        private void RefreshPawnGroupMakers()
        {
            if (faction == null || factionFc == null) return;
            LogUtil.Message("Refreshing pawn group makers");

            // Clear existing pawn group makers
            faction.pawnGroupMakers = new List<PawnGroupMaker>
            {
                new PawnGroupMaker { kindDef = PawnGroupKindDefOf.Combat },
                new PawnGroupMaker { kindDef = PawnGroupKindDefOf.Trader },
                new PawnGroupMaker { kindDef = PawnGroupKindDefOf.Settlement },
                new PawnGroupMaker { kindDef = PawnGroupKindDefOf.Peaceful }
            };
            /* Reset the customXenotype cache, just in case xenotypes have been removed or added since the last time we were here */
            ValidateCustomXenotypes();

            if (RaceTotalWeight == 0)
            {
                InitializeRaces();
            }
            if (XenoCompleteWeight == 0)
            {
                InitializeXenotypes();
            }

            SetPawnGroupMakers();

            /* If there are pawnGroupMakers with missing options, then try to find options using the basic Human race.
             *   If this fails, then the player (or their mods) is doing something fucky. Likely. Possibly.
             */
            SetFallbackPawnGroupMakers();

            // Add pack animals for caravans
            foreach (PawnKindDef animalKindDef in FactionCache.AllPackAnimalKinds)
            {
                faction.pawnGroupMakers[1].carriers.Add(new PawnGenOption { kind = animalKindDef, selectionWeight = 1 });
            }

            if (HasMissingPawnKindDefTypes)
            {
                string missing = "";
                if (!faction.pawnGroupMakers[1].traders.Any())
                {
                    missing += " | traders";
                }
                if (!faction.pawnGroupMakers[0].options.Any())
                {
                    missing += " | fighters";
                }
                if (!faction.pawnGroupMakers[3].options.Any())
                {
                    missing += " | traders";
                }
                if (WorldSettlementTraderTracker.BaseTraderKinds == null)
                {
                    missing += " | WorldSettlementTraderTracker.BaseTraderKinds == null";
                }
                if (WorldSettlementTraderTracker.BaseTraderKinds != null && !WorldSettlementTraderTracker.BaseTraderKinds.Any())
                {
                    missing += $" | WorldSettlementTraderTracker.BaseTraderKinds count: {WorldSettlementTraderTracker.BaseTraderKinds.Count}";
                }
                LogUtil.Error($"RefreshPawnGroupMakers: still has missing pawn kind def types. Reasons:{missing}");
            }

            RefreshMercenaryPawnGenOptions();
        }

        private void RefreshMercenaryPawnGenOptions()
        {
            if (militaryUtil?.mercenarySquads == null || militaryUtil?.mercenarySquads.Count == 0) return;

            foreach (MercenarySquadFC mercenarySquadFc in militaryUtil.mercenarySquads)
            {
                List<Mercenary> newMercs = new List<Mercenary>();
                foreach (Mercenary mercenary in mercenarySquadFc.mercenaries)
                {
                    // For now, keep existing mercenaries but could be updated to use xenotype system
                    newMercs.Add(mercenary);
                }
                mercenarySquadFc.mercenaries = newMercs;
            }
        }

        public List<PawnKindDef> GetSecurityGuardsForXenotype(XenotypeDef xenotype)
        {
            return securityGuardsByXenotype.ContainsKey(xenotype) 
                ? securityGuardsByXenotype[xenotype].List 
                : new List<PawnKindDef>();
        }
        public void GetFirstXenotypesForRequest(PawnGenerationRequest request, out XenotypeDef xenotype, out CustomXenotype customXenotype)
        {
            XenotypeDef chosenXenotype = null;
            CustomXenotype chosenCustomXenotype = null;

            if (xenotypeWeights.Count > 0)
            {
                foreach (XenotypeDef allowedXenotype in XenotypeWeights.Keys)
                {
                    if (IsValidXenotypeForRequest(request, allowedXenotype))
                    {
                        chosenXenotype = allowedXenotype;
                        break;
                    }
                }
            }
            if (customXenotypeWeights.Count > 0)
            {
                foreach (string allowedXenotype in CustomXenotypeWeights.Keys)
                {
                    if (IsValidCustomXenotypeForRequest(request, allowedXenotype))
                    {
                        if (FactionCache.CustomXenotypesDecoder.TryGetValue(allowedXenotype, out chosenCustomXenotype))
                        {
                            break;
                        }
                        else
                        {
                            chosenCustomXenotype = null;
                        }
                    }
                }
            }
            xenotype = chosenXenotype;
            customXenotype = chosenCustomXenotype;
        }
        public List<XenotypeDef> GetValidXenotypesForRequest(PawnGenerationRequest request)
        {
            List<XenotypeDef> output = new List<XenotypeDef>();
            if (XenotypeWeights.Count == 0)
            {
                /* We really shouldn't ever end up in this case, but *just* in case, we've included a bail-out. */
                LogUtil.Error("XenotypeWeights.Count == 0 in GetValidXenotypesForRequest");
                return output;
            }
            foreach (XenotypeDef xenotype in XenotypeWeights.Keys)
            {
                if (IsValidXenotypeForRequest(request, xenotype))
                {
                    output.Add(xenotype);
                }
            }
            return output;
        }
        public float GetTotalWeightForXenotypeList(List<XenotypeDef> xenotypes)
        {
            float output = 0;
            if (xenotypes is null || xenotypes.Count == 0)
            {
                return 0;
            }
            foreach (XenotypeDef xenotype in xenotypes)
            {
                output += GetXenotypeWeight(xenotype);
            }
            return output;
        }
        public List<string> GetValidCustomXenotypesForRequest(PawnGenerationRequest request)
        {
            List<string> output = new List<string>();
            if (CustomXenotypeWeights.Count == 0)
            {
                return output;
            }
            foreach (string xenotype in CustomXenotypeWeights.Keys)
            {
                if (IsValidCustomXenotypeForRequest(request, xenotype))
                {
                    output.Add(xenotype);
                }
            }
            return output;
        }
        public float GetTotalWeightForCustomXenotypeList(List<string> xenotypes)
        {
            float output = 0;
            if (xenotypes is null || xenotypes.Count == 0)
            {
                return 0;
            }
            foreach (string xenotype in xenotypes)
            {
                output += GetCustomXenotypeWeight(xenotype);
            }
            return output;
        }

        public void GetRandomXenotypeForRequest(PawnGenerationRequest request, out XenotypeDef xenotype, out CustomXenotype customXenotype)
        {
            XenotypeDef chosenXenotype = null;
            CustomXenotype chosenCustomXenotype = null;

            List<XenotypeDef> validXenotypes = GetValidXenotypesForRequest(request);
            List<string> validCustomXenotypes = GetValidCustomXenotypesForRequest(request);

            float cumulative = 0;
            float weightTotal = GetTotalWeightForXenotypeList(validXenotypes) + GetTotalWeightForCustomXenotypeList(validCustomXenotypes);
            float xenoRand = Rand.Value;

            if (validXenotypes.Count > 0)
            {
                foreach (XenotypeDef allowedXenotype in validXenotypes)
                {
                    float thisWeight = GetXenotypeWeight(allowedXenotype);
                    float thisChance = (cumulative + thisWeight) / weightTotal;

                    if (xenoRand < thisChance)
                    {
                        chosenXenotype = allowedXenotype;
                        break;
                    }
                    cumulative += thisWeight;
                }
            }
            if (chosenXenotype is null && validCustomXenotypes.Count > 0)
            {
                foreach (string allowedXenotype in validCustomXenotypes)
                {
                    float thisWeight = GetCustomXenotypeWeight(allowedXenotype);
                    float thisChance = (cumulative + thisWeight) / weightTotal;

                    if (xenoRand < thisChance)
                    {
                        if (FactionCache.CustomXenotypesDecoder.ContainsKey(allowedXenotype))
                        {
                            chosenCustomXenotype = FactionCache.CustomXenotypesDecoder[allowedXenotype];
                            break;
                        }
                    }
                    cumulative += thisWeight;
                }
            }

            if (chosenXenotype is null && chosenCustomXenotype is null)
            {
                /* Just in case there was some weird ordering issue, try to get the first xenotype in the allowed lists that would satisfy the request */
                GetFirstXenotypesForRequest(request, out chosenXenotype, out chosenCustomXenotype);
                /* If both are *still* null, then fall back onto baseliner */
                if (chosenXenotype is null && chosenCustomXenotype is null)
                {
                    LogUtil.Warning($"XenotypeFilter.GetRandomXenotypeForRequest failed to chose a random xenotype. Falling back onto baseliner");
                    chosenXenotype = XenotypeDefOf.Baseliner;
                }
            }

            xenotype = chosenXenotype;
            customXenotype = chosenCustomXenotype;
        }
        public ThingDef GetRandomRace()
        {
            ThingDef outputRace = null;
            float cumulative = 0;
            float weightTotal = RaceTotalWeight;
            float raceRand = Rand.Value;

            if (raceWeights.Count > 0)
            {
                foreach (ThingDef race in raceWeights.Keys)
                {
                    float thisWeight = GetRaceWeight(race);
                    if (thisWeight == 0)
                    {
                        continue;
                    }
                    if (raceRand < (cumulative + thisWeight) / weightTotal)
                    {
                        outputRace = race;
                        break;
                    }
                    cumulative += thisWeight;
                }
            }

            return outputRace;
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref xenotypeWeights, "xenotypeWeights", LookMode.Def, LookMode.Value);
            Scribe_Collections.Look(ref customXenotypeWeights, "customXenotypeWeights", LookMode.Value, LookMode.Value);
            Scribe_Collections.Look(ref raceWeights, "raceWeights", LookMode.Def, LookMode.Value);

            Scribe_Collections.Look(ref securityGuardsByXenotype, "securityGuardsByXenotype", LookMode.Def, LookMode.Deep);
            Scribe_Collections.Look(ref securityGuardsByCustomXenotype, "securityGuardsByCustomXenotype", LookMode.Value, LookMode.Deep);

            Scribe_Collections.Look(ref origBaseTraderKinds, "origBaseTraderKinds", LookMode.Def);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (securityGuardsByXenotype == null)
                {
                    securityGuardsByXenotype = new Dictionary<XenotypeDef, SecurityGuardList>();
                }
                if (securityGuardsByCustomXenotype == null)
                {
                    securityGuardsByCustomXenotype = new Dictionary<string, SecurityGuardList>();
                }
                if (xenotypeWeights == null)
                {
                    xenotypeWeights = new Dictionary<XenotypeDef, float>();
                }
                if (customXenotypeWeights == null)
                {
                    customXenotypeWeights = new Dictionary<string, float>();
                }
                if (raceWeights == null)
                {
                    raceWeights = new Dictionary<ThingDef, float>();
                }
                FinalizeInit(FactionCache.FactionComp);
            }
        }
    }
    /* This class only exists because Scribe_Collections doesn't know what to do with dictionaries of lists... */
    public class SecurityGuardList : IExposable
    {
        private List<PawnKindDef> list = new List<PawnKindDef>();

        public int Count => list.Count;
        public bool Contains(PawnKindDef def) => list.Contains(def);
        public void Clear() => list.Clear();
        public List<PawnKindDef> List => list;

        public void InitList()
        {
            list = new List<PawnKindDef>();
        }
        public bool Add(PawnKindDef def)
        {
            if (Contains(def))
            {
                return false;
            }
            else
            {
                list.Add(def);
                return true;
            }
        }
        public bool Remove(PawnKindDef def)
        {
            if (Contains(def))
            {
                list.Remove(def);
                return true;
            }
            else
            {
                return false;
            }
        }
        public void AddRange(List<PawnKindDef> deflist)
        {
            foreach (PawnKindDef def in deflist)
            {
                Add(def);
            }
        }
        public void RemoveRange(List<PawnKindDef> deflist)
        {
            foreach (PawnKindDef def in deflist)
            {
                Remove(def);
            }
        }
        public void SetRange(List<PawnKindDef> deflist)
        {
            Clear();
            AddRange(deflist);
        }
        public void ExposeData()
        {
            Scribe_Collections.Look(ref list, "guardKindList", LookMode.Def);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (list is null)
                {
                    InitList();
                }
            }
        }
    }
}
