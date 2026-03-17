using System;
using System.Collections.Generic;
using System.Linq;
using FactionColonies.util;
using RimWorld;
using Verse;

namespace FactionColonies
{
    public class MilUnitFC : IExposable, ILoadReferenceable
    {
        public int loadID;
        public string name;
        public bool isBlank;
        public double equipmentTotalCost;
        public int tickChanged = -1;
        public PawnKindDef animal;
        public PawnKindDef pawnKind;
        public XenotypeDef xenotype;

        // Def-based equipment storage
        public List<SavedThing> weapons = new List<SavedThing>();
        public List<SavedThing> apparel = new List<SavedThing>();
        public bool HasWeapon => weapons.Any(w => w.thing != null);

        // CE ammo preference (null = equip random ammo)
        public ThingDef preferredAmmo;

        // Lazy preview pawn for UI rendering only — not serialized
        private Pawn previewPawn;
        private bool pawnIdentityDirty = true;    // Needs new PawnGenerator call (race/xeno change)
        private bool pawnEquipmentDirty = true;   // Needs equipment refresh on same pawn

        public MilUnitFC()
        {
        }

        public MilUnitFC(bool blank)
        {
            loadID = FactionCache.FactionComp.NextUnitID;
            isBlank = blank;
            equipmentTotalCost = 0;

            try
            {
                Faction playerFaction = FactionCache.PlayerColonyFaction;
                if (playerFaction != null && playerFaction.def.pawnGroupMakers.Any() &&
                    playerFaction.def.pawnGroupMakers.Any(pgm => pgm.options?.Any() == true))
                {
                    pawnKind = playerFaction.RandomPawnKind();
                }
                else
                {
                    var pColonyDef = DefDatabase<FactionDef>.GetNamed("PColony");
                    if (pColonyDef?.pawnGroupMakers?.Any(pgm => pgm.options?.Any() == true) == true)
                    {
                        pawnKind = pColonyDef.pawnGroupMakers.RandomElement().options.RandomElement().kind;
                    }
                    else
                    {
                        pawnKind = PawnKindDefOf.Colonist;
                    }
                }
            }
            catch (Exception ex)
            {
                LogUtil.Error($"Error creating MilUnitFC: {ex.Message}");
                pawnKind = PawnKindDefOf.Colonist;
            }

            if (!isBlank)
            {
                xenotype = XenotypeDefOf.Baseliner;
            }
        }

        public string GetUniqueLoadID()
        {
            return $"MilUnitFC_{loadID}";
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref loadID, "loadID");
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref isBlank, "blank");
            Scribe_Values.Look(ref equipmentTotalCost, "equipmentTotalCost", -1);
            Scribe_Values.Look(ref tickChanged, "tickChanged");
            Scribe_Defs.Look(ref pawnKind, "PawnKind");
            Scribe_Defs.Look(ref animal, "animal");
            Scribe_Defs.Look(ref xenotype, "xenotype");

            // Def-based equipment storage
            Scribe_Collections.Look(ref weapons, "weapons", LookMode.Deep);
            Scribe_Collections.Look(ref apparel, "apparel", LookMode.Deep);
            Scribe_Defs.Look(ref preferredAmmo, "preferredAmmo");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (weapons == null) weapons = new List<SavedThing>();
                if (apparel == null) apparel = new List<SavedThing>();
            }
        }

        // --- Preview Pawn (UI only) ---

        public Pawn PreviewPawn
        {
            get
            {
                if (previewPawn == null || pawnIdentityDirty)
                    RebuildPreviewPawn();
                else if (pawnEquipmentDirty)
                    RefreshPreviewEquipment();
                return previewPawn;
            }
        }

        public void MarkEquipmentDirty()
        {
            pawnEquipmentDirty = true;
        }

        private void RebuildPreviewPawn()
        {
            try
            {
                if (previewPawn != null)
                {
                    previewPawn.apparel?.DestroyAll();
                    previewPawn.equipment?.DestroyAllEquipment();
                    previewPawn.Destroy();
                }

                previewPawn = FCPawnGenerator.GenerateWithForcedXenotype(FCPawnGenerator.WorkerOrMilitaryRequest(pawnKind, xenotype));

                if (previewPawn != null && previewPawn.Faction == null)
                {
                    Faction empireFaction = FactionCache.PlayerColonyFaction;
                    if (empireFaction != null)
                        previewPawn.SetFaction(empireFaction);
                }
            }
            catch (Exception ex)
            {
                LogUtil.Warning($"Failed to generate preview pawn for {name}: {ex.Message}");
                previewPawn = null;
            }

            pawnIdentityDirty = false;

            if (previewPawn == null)
            {
                pawnEquipmentDirty = false;
                return;
            }

            previewPawn.mindState.canFleeIndividual = false;
            RefreshPreviewEquipment();
        }

        private void RefreshPreviewEquipment()
        {
            if (previewPawn == null) return;

            previewPawn.apparel.DestroyAll();
            previewPawn.equipment.DestroyAllEquipment();

            foreach (SavedThing a in apparel)
            {
                Thing t = a.CreateThing();
                if (t is Apparel ap)
                    previewPawn.apparel.Wear(ap);
            }

            foreach (SavedThing w in weapons)
            {
                Thing wt = w.CreateThing();
                if (wt is ThingWithComps twc)
                    previewPawn.equipment.AddEquipment(twc);
            }

            pawnEquipmentDirty = false;
        }

        // --- Equipment Mutation Methods ---

        public void ChangeTick()
        {
            tickChanged = Find.TickManager.TicksGame;
            costDirty = true;
        }

        public void SetWeapon(ThingDef def, ThingDef stuff)
        {
            weapons.Clear();
            weapons.Add(new SavedThing(def, stuff));
            preferredAmmo = null;
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void ClearWeapon()
        {
            weapons.Clear();
            preferredAmmo = null;
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void SetPreferredAmmo(ThingDef ammo)
        {
            preferredAmmo = ammo;
            ChangeTick();
        }

        public void ClearPreferredAmmo()
        {
            preferredAmmo = null;
            ChangeTick();
        }

        public void SetApparel(ThingDef def, ThingDef stuff)
        {
            // Remove conflicting apparel using RimWorld's static check
            BodyDef body = pawnKind?.race?.race?.body ?? BodyDefOf.Human;
            apparel.RemoveAll(existing =>
                !ApparelUtility.CanWearTogether(existing.thing, def, body));
            apparel.Add(new SavedThing(def, stuff));
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void RemoveApparel(ApparelLayerDef layer, BodyPartGroupDef bodyPart)
        {
            apparel.RemoveAll(s => MatchesSlot(s.thing, layer, bodyPart));
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        public void ClearAllEquipment()
        {
            weapons.Clear();
            apparel.Clear();
            preferredAmmo = null;
            pawnEquipmentDirty = true;
            ChangeTick();
            MilSquadFC.UpdateEquipmentTotalCostOfSquadsContaining(this);
        }

        /// <summary>
        /// Check if a ThingDef matches a given apparel slot (layer + optional body part).
        /// </summary>
        public static bool MatchesSlot(ThingDef def, ApparelLayerDef layer, BodyPartGroupDef bodyPart)
        {
            if (def?.apparel == null) return false;
            if (!def.apparel.layers.Contains(layer)) return false;
            if (bodyPart != null && !def.apparel.bodyPartGroups.Contains(bodyPart)) return false;
            return true;
        }

        // --- Cost ---

        private bool costDirty = true;

        public double getTotalCost
        {
            get
            {
                if (costDirty)
                {
                    UpdateEquipmentTotalCost();
                    costDirty = false;
                }
                return equipmentTotalCost;
            }
        }

        public void UpdateEquipmentTotalCost()
        {
            if (isBlank)
            {
                equipmentTotalCost = 0;
                return;
            }

            double totalCost = 0;

            if (pawnKind?.race != null)
            {
                float xenoFactor = 1f;
                if (xenotype?.genes != null)
                {
                    foreach (GeneDef gene in xenotype.genes)
                    {
                        /* Don't include genes that have 0 value (mostly just cosmetic genes) */
                        if (gene.marketValueFactor > 0)
                            xenoFactor *= gene.marketValueFactor;
                    }
                }
                // Non-violent xenotypes cost much less — they can't fight, relying on guard animals
                if (FactionCache.XenotypeIsNonViolent(xenotype))
                {
                    xenoFactor *= 0.25f;
                }
                totalCost += Math.Floor(pawnKind.race.BaseMarketValue * FCSettings.militaryRaceCostMultiplier * xenoFactor);
            }

            foreach (SavedThing a in apparel)
                totalCost += a.MarketValue;

            foreach (SavedThing w in weapons)
                totalCost += w.MarketValue;

            if (animal != null)
                totalCost += Math.Floor(animal.race.BaseMarketValue * FCSettings.militaryAnimalCostMultiplier);

            equipmentTotalCost = Math.Ceiling(totalCost);
        }

        // --- Unit Management ---

        public void RemoveUnit()
        {
            FactionCache.FactionComp.militaryCustomizationUtil.units.Remove(this);
        }

        /// <summary>
        /// Re-roll the preview pawn (new appearance) while keeping equipment.
        /// Used by "Roll New Pawn" and race/xeno change buttons.
        /// </summary>
        public void RerollPreviewPawn()
        {
            pawnIdentityDirty = true;
            pawnEquipmentDirty = true;
            ChangeTick();
        }
    }
}
