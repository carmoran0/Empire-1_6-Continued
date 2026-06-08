using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using VanillaPsycastsExpanded;
using VEF.Abilities;
using Verse;
using AbilityDef = VEF.Abilities.AbilityDef;

namespace FactionColonies.VPE
{
    /// <summary>
    /// Vanilla Psycasts Expanded provider for the unit designer's Abilities tab. VPE uses its own
    /// ability framework (<c>VEF.Abilities.AbilityDef</c>, a separate DefDatabase from base game) and a
    /// path/points learning model, so it brings its own editor (<see cref="FCWindow_VPEPsycastEditor"/>)
    /// rather than the generic picker. Psylink is granted the "neuroformer way" (base <c>PsychicAmplifier</c>
    /// hediff); VPE's own Harmony patches attach its psycast tracker and sync level/points.
    /// </summary>
    public class VPEAbilityProvider : IAbilitySystemProvider
    {
        public const string ProviderKey = "VPE";

        private const double BaseAbilityCost = 300.0;
        private const double PerLevelAbilityCost = 300.0;

        public string Key => ProviderKey;
        public string Label => "Vanilla Psycasts Expanded";
        public bool IsActive => ModsConfig.IsActive("VanillaExpanded.VPsycastsE");
        public int Priority => 100;
        public int MaxPsylinkLevel => PsycastsMod.Settings != null ? PsycastsMod.Settings.maxLevel : 30;
        public bool UsesCustomEditor => true;

        // Generic-picker path is unused for VPE; the custom editor handles selection.
        public IEnumerable<AbilityPickEntry> ListPickable(int psylinkLevel)
        {
            yield break;
        }

        public void OpenEditor(MilUnitFC unit, Action onClosed)
        {
            if (unit is null) return;
            Find.WindowStack.Add(new FCWindow_VPEPsycastEditor(unit, onClosed));
        }

        public bool TryGetDisplay(string defName, out AbilityPickEntry entry)
        {
            AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
            if (def is null) { entry = null; return false; }
            entry = ToEntry(def);
            return true;
        }

        internal static AbilityPickEntry ToEntry(AbilityDef def)
        {
            AbilityExtension_Psycast psycast = def.Psycast();
            int level = psycast != null ? psycast.level : 0;
            return new AbilityPickEntry
            {
                defName = def.defName,
                label = def.LabelCap,
                description = def.description,
                icon = def.icon,
                level = level,
                cost = (BaseAbilityCost + PerLevelAbilityCost * level) * FCSettings.militaryPsycastCostMultiplier
            };
        }

        /// <summary>
        /// Raises the pawn's psylink to <paramref name="level"/> via the base-game psylink hediff.
        /// VPE's Harmony patches (<c>Hediff_Psylink.PostAdd</c> / <c>ChangeLevel</c>) attach the VPE
        /// psycast tracker, sync its level, grant points, and suppress the base random-psycast grant.
        /// </summary>
        public void ApplyPsylink(Pawn pawn, int level)
        {
            if (pawn?.health is null || level < 1) return;

            Hediff_Psylink psylink = pawn.GetMainPsylinkSource();
            if (psylink is null)
            {
                // Creates the psylink hediff at level 1; VPE's PostAdd patch attaches the tracker.
                pawn.ChangePsylinkLevel(1, false);
                psylink = pawn.GetMainPsylinkSource();
            }

            int current = psylink != null ? psylink.level : 0;
            int target = Math.Min(level, MaxPsylinkLevel);
            if (target > current)
                pawn.ChangePsylinkLevel(target - current, false);
        }

        public void GrantAbility(Pawn pawn, string defName)
        {
            if (pawn is null) return;
            AbilityDef def = DefDatabase<AbilityDef>.GetNamedSilentFail(defName);
            if (def is null) return;

            CompAbilities comp = pawn.GetComp<CompAbilities>();
            if (comp is null) return;
            if (comp.HasAbility(def)) return;

            AbilityExtension_Psycast psycast = def.Psycast();
            if (psycast != null)
            {
                // Unlock the owning path (so the merc's psycaster UI reflects it) then grant the
                // ability plus any prerequisites — forcing the designed end-state, no point cost.
                Hediff_PsycastAbilities tracker = pawn.Psycasts();
                if (tracker != null && psycast.path != null && !tracker.unlockedPaths.Contains(psycast.path))
                    tracker.UnlockPath(psycast.path);
                psycast.UnlockWithPrereqs(comp);
            }
            else
            {
                comp.GiveAbility(def);
            }
        }
    }
}
