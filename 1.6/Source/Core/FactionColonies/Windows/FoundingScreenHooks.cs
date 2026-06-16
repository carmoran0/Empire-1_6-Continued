using System;
using RimWorld.Planet;

namespace FactionColonies
{
    /// <summary>
    /// Replacement for the Found-screen "Settle" button: a label plus the action to run when clicked.
    /// </summary>
    public class FoundingButtonOverride
    {
        public string Label;
        public Action OnClick;
    }

    /// <summary>
    /// Small extensibility seams on the "Found New Settlement" screen (<see cref="CreateColonyWindowFc"/>).
    /// A submod can replace the Settle button (e.g. to require founding via outposts) and react to the
    /// selected tile/type changing so it can drive its own companion window.
    /// </summary>
    public static class FoundingScreenHooks
    {
        /// <summary>
        /// When set and returns non-null for the current (tile, type), the Found screen draws this
        /// button instead of the normal "Settle" button. Return null to keep the default button.
        /// </summary>
        public static Func<PlanetTile, WorldSettlementDef, FoundingButtonOverride> SettleButtonOverride;

        /// <summary>
        /// Fired whenever the Found screen's selected tile or settlement type changes, and once with
        /// (Invalid, null) when the screen closes. Lets a submod open/refresh/close a companion window.
        /// </summary>
        public static event Action<PlanetTile, WorldSettlementDef> SelectionChanged;

        public static FoundingButtonOverride GetSettleButtonOverride(PlanetTile tile, WorldSettlementDef type)
        {
            return SettleButtonOverride is object ? SettleButtonOverride(tile, type) : null;
        }

        public static void NotifySelectionChanged(PlanetTile tile, WorldSettlementDef type)
        {
            SelectionChanged?.Invoke(tile, type);
        }
    }
}
