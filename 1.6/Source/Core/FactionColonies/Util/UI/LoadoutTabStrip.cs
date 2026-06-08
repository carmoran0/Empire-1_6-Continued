using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /* The sub-tabs of a unit's loadout editor (shared by DesignUnitsWindow and Dialog_PawnLoadout).
     * Abilities is last so that omitting it (includeAbilities: false) leaves the other tab indices
     * unchanged. */
    public enum LoadoutTab
    {
        Apparel,
        Inventory,
        Implants,
        Abilities
    }

    /* Draws the loadout tab row using the shared ButtonFlat tab drawer (UIUtil.DrawTabRow) and
     * returns the bordered content area below the tabs. */
    public static class LoadoutTabStrip
    {
        public const float TabHeight = 28f;

        public static LoadoutTab Draw(Rect boundingBox, LoadoutTab selected, out Rect contentRect, bool includeAbilities = true)
        {
            List<string> labels = new List<string>
            {
                "fcTabApparel".Translate(),
                "fcTabInventory".Translate(),
                "fcTabImplants".Translate()
            };
            if (includeAbilities)
                labels.Add("fcTabAbilities".Translate());

            // Guard against a stale Abilities selection when the tab is hidden.
            int selectedIdx = (int)selected;
            if (selectedIdx >= labels.Count) selectedIdx = 0;

            int idx = UIUtil.DrawTabRow(boundingBox, labels, selectedIdx, out contentRect,
                tabHeight: TabHeight, minTabWidth: 70f);
            return (LoadoutTab)idx;
        }
    }
}
