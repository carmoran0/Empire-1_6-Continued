using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Base-game (Royalty) psycast provider for the unit designer. Psycasts are <see cref="AbilityDef"/>s
    /// with <c>IsPsycast == true</c> and a psylink <c>level</c> gate; they are granted via
    /// <see cref="Pawn_AbilityTracker.GainAbility"/> against a pawn carrying a <see cref="Hediff_Psylink"/>
    /// (<c>PsychicAmplifier</c>). Uses the generic picker (no custom editor).
    /// </summary>
    public class VanillaAbilityProvider : IAbilitySystemProvider
    {
        public const string ProviderKey = "Vanilla";

        // Base silver per psycast before the FCSettings multiplier; scales mildly with level.
        private const double BaseAbilityCost = 300.0;
        private const double PerLevelAbilityCost = 300.0;

        public string Key => ProviderKey;
        public string Label => "Royalty";
        public bool IsActive => ModLister.RoyaltyInstalled;
        public int Priority => 0;
        public int MaxPsylinkLevel => 6;
        public bool UsesCustomEditor => false;

        public IEnumerable<AbilityPickEntry> ListPickable(int psylinkLevel)
        {
            foreach (AbilityDef def in DefDatabase<AbilityDef>.AllDefs)
            {
                if (!def.IsPsycast) continue;
                if (def.level < 1 || def.level > psylinkLevel) continue;
                yield return ToEntry(def);
            }
        }

        public bool TryGetDisplay(string defName, out AbilityPickEntry entry)
        {
            AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
            if (def is null) { entry = null; return false; }
            entry = ToEntry(def);
            return true;
        }

        private static AbilityPickEntry ToEntry(AbilityDef def)
        {
            return new AbilityPickEntry
            {
                defName = def.defName,
                label = def.LabelCap,
                description = def.description,
                icon = def.uiIcon,
                level = def.level,
                cost = (BaseAbilityCost + PerLevelAbilityCost * def.level) * FCSettings.militaryPsycastCostMultiplier
            };
        }

        // Not used (UsesCustomEditor == false), but the interface requires it.
        public void OpenEditor(MilUnitFC unit, Action onClosed) { }

        /// <summary>
        /// Grants psylink at <paramref name="level"/> the "neuroformer way" (a <c>PsychicAmplifier</c>
        /// hediff), then strips the random psycasts the base game auto-grants on level gain — the
        /// designer assigns specific psycasts explicitly via <see cref="GrantAbility"/>.
        /// </summary>
        public void ApplyPsylink(Pawn pawn, int level)
        {
            if (pawn?.health is null || level < 1) return;

            // Snapshot psycasts already present (from pawn generation), so we only remove the ones
            // our psylink grant adds.
            HashSet<AbilityDef> preExisting = pawn.abilities != null
                ? new HashSet<AbilityDef>(pawn.abilities.abilities.Where(a => a.def.IsPsycast).Select(a => a.def))
                : new HashSet<AbilityDef>();

            Hediff_Psylink psylink = pawn.GetMainPsylinkSource();
            if (psylink is null)
            {
                psylink = (Hediff_Psylink)HediffMaker.MakeHediff(HediffDefOf.PsychicAmplifier, pawn);
                psylink.suppressPostAddLetter = true;
                BodyPartRecord brain = pawn.health.hediffSet.GetBrain();
                pawn.health.AddHediff(psylink, brain);
                psylink.suppressPostAddLetter = false;
            }

            // Jump straight to the desired level without firing ChangeLevel's per-level random grant.
            int capped = Math.Min(level, MaxPsylinkLevel);
            psylink.level = capped;
            psylink.Severity = capped;
            pawn.psychicEntropy?.Notify_GainedPsylink();

            if (pawn.abilities != null)
            {
                foreach (Ability a in pawn.abilities.abilities.Where(a => a.def.IsPsycast).ToList())
                {
                    if (!preExisting.Contains(a.def))
                        pawn.abilities.RemoveAbility(a.def);
                }
            }
        }

        public void GrantAbility(Pawn pawn, string defName)
        {
            if (pawn?.abilities is null) return;
            AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
            if (def is null) return;
            pawn.abilities.GainAbility(def);
        }
    }
}
