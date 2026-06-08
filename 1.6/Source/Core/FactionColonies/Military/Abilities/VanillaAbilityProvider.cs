using System;
using RimWorld;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Base-game (Royalty) psycast provider. Mirrors vanilla exactly: there is no selection UI — the
    /// player only sets a psylink level, and the base game grants one <em>random</em> psycast per level
    /// when the pawn gains psylink (<see cref="Hediff_Psylink.TryGiveAbilityOfLevel"/>). Stores no
    /// abilities, so <see cref="SupportsExplicitSelection"/> is false and the ability-grant/display
    /// methods are no-ops.
    /// </summary>
    public class VanillaAbilityProvider : IAbilitySystemProvider
    {
        public const string ProviderKey = "Vanilla";

        public string Key => ProviderKey;
        public string Label => "Royalty";
        public bool IsActive => ModLister.RoyaltyInstalled;
        public int Priority => 0;
        public int MaxPsylinkLevel => 6;
        public bool SupportsExplicitSelection => false;

        // No custom editor / no stored abilities.
        public void OpenEditor(MilUnitFC unit, Action onClosed) { }

        public bool TryGetDisplay(string defName, out AbilityPickEntry entry)
        {
            entry = null;
            return false;
        }

        /// <summary>
        /// Raises the pawn's psylink to <paramref name="level"/> the neuroformer way. As in vanilla,
        /// the base game grants one random psycast per level gained (letters suppressed). The first
        /// <see cref="PawnUtility.ChangePsylinkLevel"/> creates the hediff at level 1 and returns, so
        /// a second call advances to the target level.
        /// </summary>
        public void ApplyPsylink(Pawn pawn, int level)
        {
            if (pawn?.health is null || level < 1) return;

            Hediff_Psylink psylink = pawn.GetMainPsylinkSource();
            if (psylink is null)
            {
                pawn.ChangePsylinkLevel(1, false);
                psylink = pawn.GetMainPsylinkSource();
            }

            int current = psylink != null ? psylink.level : 0;
            int target = Math.Min(level, MaxPsylinkLevel);
            if (target > current)
                pawn.ChangePsylinkLevel(target - current, false);
        }

        // Base game stores no chosen abilities; psycasts are the random grants from ApplyPsylink.
        public void GrantAbility(Pawn pawn, string defName) { }
    }
}
