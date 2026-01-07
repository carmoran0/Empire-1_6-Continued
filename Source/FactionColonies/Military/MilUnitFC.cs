using System;
using System.Collections.Generic;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading;
using FactionColonies.util;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FactionColonies
{
    public class MilUnitFC : IExposable, ILoadReferenceable
    {
        public int loadID;
        public string name;
        public Pawn defaultPawn;
        public bool isBlank;
        public double equipmentTotalCost;
        public bool isTrader;
        public bool isCivilian;
        public int tickChanged = -1;
        public PawnKindDef animal;
        public PawnKindDef pawnKind;
        public XenotypeDef xenotype;
        public MilUnitFC()
        {
        }

        public MilUnitFC(bool blank)
        {
            loadID = Find.World.GetComponent<FactionFC>().NextUnitID;
            isBlank = blank;
            equipmentTotalCost = 0;

            try
            {
                var playerFaction = FactionColonies.getPlayerColonyFaction();
                if (playerFaction != null && playerFaction.def.pawnGroupMakers.Any() && 
                    playerFaction.def.pawnGroupMakers.Any(pgm => pgm.options?.Any() == true))
                {
                    pawnKind = playerFaction.RandomPawnKind();
                }
                else
                {
                    // Fallback to PColony faction
                    var pColonyDef = DefDatabase<FactionDef>.GetNamed("PColony");
                    if (pColonyDef?.pawnGroupMakers?.Any(pgm => pgm.options?.Any() == true) == true)
                    {
                        pawnKind = pColonyDef.pawnGroupMakers.RandomElement().options.RandomElement().kind;
                    }
                    else
                    {
                        // Ultimate fallback to any colonist pawn kind
                        pawnKind = PawnKindDefOf.Colonist;
                    }
                }
                generateDefaultPawn();
            }
            catch (Exception ex)
            {
                Log.Error($"Empire: Error creating MilUnitFC: {ex.Message}");
                pawnKind = PawnKindDefOf.Colonist;
                generateDefaultPawn();
            }
        }

        public string GetUniqueLoadID()
        {
            return $"MilUnitFC_{loadID}";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID");
            Scribe_Deep.Look(ref defaultPawn, "defaultPawn");
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref isBlank, "blank");
            Scribe_Values.Look(ref equipmentTotalCost, "equipmentTotalCost", -1);
            Scribe_Values.Look(ref isTrader, "isTrader");
            Scribe_Values.Look(ref isCivilian, "isCivilian");
            Scribe_Values.Look(ref tickChanged, "tickChanged");
            Scribe_Defs.Look(ref pawnKind, "PawnKind");
            Scribe_Defs.Look(ref animal, "animal");
            Scribe_Defs.Look(ref xenotype, "xenotype");
        }

        public void generateDefaultPawn()
        {
            List<Apparel> apparel = new List<Apparel>();
            List<ThingWithComps> equipment = new List<ThingWithComps>();
            List<Gene> gene = new List<Gene>();

            if (defaultPawn != null)
            {
                apparel.AddRange(defaultPawn.apparel.WornApparel);
                equipment.AddRange(defaultPawn.equipment.AllEquipmentListForReading);
                gene.AddRange(defaultPawn.genes.GenesListForReading);

            Reset:
                foreach (Apparel cloth in defaultPawn.apparel.WornApparel)
                {
                    defaultPawn.apparel.Remove(cloth);
                    goto Reset;
                }

                foreach (ThingWithComps weapon in defaultPawn.equipment.AllEquipmentListForReading)
                {
                    defaultPawn.equipment.Remove(weapon);
                    goto Reset;
                }

                foreach (Gene xenogene in defaultPawn.genes.GenesListForReading)
                {
                    defaultPawn.genes.RemoveGene(xenogene);
                    goto Reset;
                }

                defaultPawn.Destroy();
            }


            // Try to generate pawn with the requested kind
            try
            {
                defaultPawn = PawnGenerator.GeneratePawn(FCPawnGenerator.WorkerOrMilitaryRequest(pawnKind, xenotype));
                
                // Set faction after generation (since we generate without faction to avoid xenotype forcing)
                if (defaultPawn != null && defaultPawn.Faction == null)
                {
                    var empireFaction = FactionColonies.getPlayerColonyFaction();
                    if (empireFaction != null)
                    {
                        defaultPawn.SetFaction(empireFaction);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning($"Empire: Failed to generate default pawn with kind {pawnKind?.defName}: {ex.Message}");
                defaultPawn = null;
            }
            
            // Fallback 1: Try with Baseliner xenotype and NO faction (avoids faction xenotype forcing) I'll explore this one further as this may break immersion
            if (defaultPawn == null)
            {
                Log.Warning($"Empire: Default pawn generation failed for {pawnKind?.defName}. Trying Baseliner fallback without faction.");
                try
                {
                    pawnKind = PawnKindDefOf.Colonist;
                    var simpleRequest = new PawnGenerationRequest(
                        kind: PawnKindDefOf.Colonist,
                        faction: null, // NO faction - this prevents faction xenotype forcing
                        context: PawnGenerationContext.NonPlayer,
                        tile: -1,
                        forceGenerateNewPawn: false,
                        allowDead: false,
                        allowDowned: false,
                        canGeneratePawnRelations: false, // No relations for factionless pawns
                        mustBeCapableOfViolence: true,
                        colonistRelationChanceFactor: 0,
                        forceAddFreeWarmLayerIfNeeded: false,
                        allowGay: true,
                        allowFood: true,
                        allowAddictions: false,
                        forcedXenotype: XenotypeDefOf.Baseliner // Force Baseliner - guaranteed violence capable
                    );
                    defaultPawn = PawnGenerator.GeneratePawn(simpleRequest);
                    
                    // Set the faction after generation
                    if (defaultPawn != null)
                    {
                        var empireFaction = FactionColonies.getPlayerColonyFaction();
                        if (empireFaction != null)
                        {
                            defaultPawn.SetFaction(empireFaction);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning($"Empire: Baseliner fallback also failed: {ex.Message}");
                }
            }
            
            // Fallback 2: Absolute minimal request - no faction, no xenotype, no violence requirement
            if (defaultPawn == null)
            {
                Log.Warning("Empire: All standard generation failed. Trying minimal fallback.");
                try
                {
                    var fallbackRequest = new PawnGenerationRequest(
                        kind: PawnKindDefOf.Colonist,
                        faction: null, // NO faction
                        context: PawnGenerationContext.NonPlayer,
                        mustBeCapableOfViolence: false // Allow non-violent as absolute last resort
                    );
                    defaultPawn = PawnGenerator.GeneratePawn(fallbackRequest);
                    
                    // Set the faction after generation
                    if (defaultPawn != null)
                    {
                        var empireFaction = FactionColonies.getPlayerColonyFaction();
                        if (empireFaction != null)
                        {
                            defaultPawn.SetFaction(empireFaction);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"Empire: Critical - all pawn generation attempts failed: {ex.Message}");
                }
            }
            
            // Final check - if still null, we cannot proceed!!!
            if (defaultPawn == null)
            {
                Log.Error("Empire: Critical error - could not generate any default pawn for military unit.");
                return;
            }
            
            defaultPawn.mindState.canFleeIndividual = false;
            defaultPawn.apparel.DestroyAll();

            foreach (Apparel clothes in apparel)
            {
                //Log.Message(clothes.Label);
                defaultPawn.apparel.Wear(clothes);
            }

            foreach (ThingWithComps weapon in equipment)
            {
                //Log.Message(weapon.Label);
                equipWeapon(weapon);
            }

            foreach (Gene xenogene in gene)
            {
                //Log.Message(gene.Label);
                GenerateXenotype(xenogene);
            }

        }

        public void changeTick()
        {
            tickChanged = Find.TickManager.TicksGame;
        }

        public void equipWeapon(ThingWithComps weapon)
        {
            changeTick();
            if (isCivilian == false)
            {
                unequipWeapon();
                defaultPawn.equipment.AddEquipment(weapon);
            }
            else
            {
                Messages.Message("You cannot put a weapon on a civilian!", MessageTypeDefOf.RejectInput);
            }

            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void unequipWeapon()
        {
            changeTick();
            defaultPawn.equipment.DestroyAllEquipment();

            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }
        public void GenerateXenotype(Gene xenogene)
        {
            changeTick();
            defaultPawn.genes.SetXenotype(xenotype);
        }
        public void wearEquipment(Apparel Equipment, bool wear)
        {
            changeTick();
        Reset:
            foreach (ApparelLayerDef layer in Equipment.def.apparel.layers)
            {
                foreach (BodyPartGroupDef part in Equipment.def.apparel.bodyPartGroups)
                {
                    foreach (Apparel apparel in defaultPawn.apparel.WornApparel)
                    {
                        if ((apparel.def.apparel.layers.Contains(layer) &&
                             apparel.def.apparel.bodyPartGroups.Contains(part)) ||
                            (Equipment.def.apparel.layers.Contains(ApparelLayerDefOf.Overhead) &&
                             apparel.def.apparel.layers.Contains(ApparelLayerDefOf.Overhead)))
                        {
                            defaultPawn.apparel.Remove(apparel);
                            goto Reset;
                        }
                    }
                }
            }

            if (wear == false)
            {
                //NOTHING
            }
            else
            {
                defaultPawn.apparel.Wear(Equipment);
            }

            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void removeUnit()
        {
            Find.World.GetComponent<FactionFC>().militaryCustomizationUtil.units.Remove(this);
        }

        public void unequipAllEquipment()
        {
            changeTick();
            defaultPawn.apparel.DestroyAll();
            defaultPawn.equipment.DestroyAllEquipment();

            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public double getTotalCost
        {
            get
            {
                updateEquipmentTotalCost();
                return equipmentTotalCost;
            }
        }

        public void updateEquipmentTotalCost()
        {
            if (isBlank)
            {
                equipmentTotalCost = 0;
            }
            else
            {
                double totalCost = 0;
                totalCost += Math.Floor(defaultPawn.def.BaseMarketValue * FactionColonies.militaryRaceCostMultiplier);

                totalCost = defaultPawn.apparel.WornApparel.Aggregate(totalCost,
                    (current, thing) => current + thing.MarketValue);

                totalCost = defaultPawn.equipment.AllEquipmentListForReading.Aggregate(totalCost,
                    (current, thing) => current + thing.MarketValue);

                if (animal != null)
                {
                    totalCost += Math.Floor(animal.race.BaseMarketValue * FactionColonies.militaryAnimalCostMultiplier);
                }

                equipmentTotalCost = Math.Ceiling(totalCost);
            }
        }

        public void setTrader(bool state)
        {
            changeTick();
            isTrader = state;
            if (state)
            {
                setCivilian(true);
            }
        }

        public void setCivilian(bool state)
        {
            changeTick();
            isCivilian = state;
            if (state)
            {
                unequipWeapon();
            }
            else
            {
                setTrader(false);
            }
        }
    }
}