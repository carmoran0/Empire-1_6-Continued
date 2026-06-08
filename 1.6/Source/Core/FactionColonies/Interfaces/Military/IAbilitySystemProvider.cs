using System;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// A single pickable ability surfaced by an <see cref="IAbilitySystemProvider"/>. Pure display
    /// data for the picker UI and the chosen-ability list row; the actual grant is done by the
    /// provider via <see cref="IAbilitySystemProvider.GrantAbility"/>.
    /// </summary>
    public class AbilityPickEntry
    {
        public string defName;
        public string label;
        public string description;
        public Texture2D icon;
        public int level;     // required psylink level (informational / sort)
        public double cost;    // silver cost contribution toward equipmentTotalCost
    }

    /// <summary>
    /// Pluggable ability system for the unit designer's Abilities tab. The base game and Vanilla
    /// Psycasts Expanded are distinct, non-overlapping systems (different AbilityDef types, different
    /// learning models), so each is wrapped in a provider. Exactly one provider is "active" at a
    /// time (see <see cref="AbilitySystemRegistry.Active"/>); saved abilities carry their provider's
    /// <see cref="Key"/> so they apply through the right system even on load.
    ///
    /// Register implementations via <see cref="AbilitySystemRegistry.Register"/>. Providers are
    /// app-lifetime singletons (not per-game), so registration happens from a
    /// <c>[StaticConstructorOnStartup]</c>, not from world-component load.
    /// </summary>
    public interface IAbilitySystemProvider
    {
        /// <summary>Stable identifier persisted on every <c>SavedAbility</c> this provider produces.</summary>
        string Key { get; }

        /// <summary>Human-readable name (for empty-state text / tooltips).</summary>
        string Label { get; }

        /// <summary>True when this system's mod(s) are loaded and the system is usable.</summary>
        bool IsActive { get; }

        /// <summary>Higher wins when multiple providers are active. Vanilla is lowest.</summary>
        int Priority { get; }

        /// <summary>Maximum psylink level this system supports (caps the tab's stepper).</summary>
        int MaxPsylinkLevel { get; }

        /// <summary>
        /// When true (VPE), the player explicitly chooses psycasts: the tab shows an "Edit Psycasts"
        /// button (<see cref="OpenEditor"/>) and a read-only list of the chosen abilities. When
        /// false (base game), psycasts are granted randomly at spawn from the psylink level — no
        /// picker, no stored abilities — exactly as base RimWorld does.
        /// </summary>
        bool SupportsExplicitSelection { get; }

        /// <summary>
        /// Opens the provider-owned editor (only when <see cref="SupportsExplicitSelection"/>). The
        /// editor writes the chosen abilities back into <paramref name="unit"/>.abilities; it should
        /// invoke <paramref name="onClosed"/> when done so the tab can refresh cost/preview.
        /// </summary>
        void OpenEditor(MilUnitFC unit, Action onClosed);

        /// <summary>Resolves a saved ability's display data (label/icon/cost) for list rendering.</summary>
        bool TryGetDisplay(string defName, out AbilityPickEntry entry);

        /// <summary>Grants psylink of the given level to a freshly generated pawn (the neuroformer way).</summary>
        void ApplyPsylink(Pawn pawn, int level);

        /// <summary>Forces the end-state of one chosen ability onto a freshly generated pawn.</summary>
        void GrantAbility(Pawn pawn, string defName);
    }
}
