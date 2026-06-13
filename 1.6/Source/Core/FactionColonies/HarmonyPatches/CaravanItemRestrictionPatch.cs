using System.Collections.Generic;
using FactionColonies.util;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// When the player reforms a caravan (or loads a drop pod / transporter) to leave a manually
    /// defended Empire settlement, withhold the settlement's own loot from the takeable item list.
    /// The settlement's items are snapshotted at map generation (<see cref="BattlefieldContext.RecordSettlementLoot"/>),
    /// so battle spoils dropped by dead attackers, the player's carried/inventory gear, and the
    /// corpses/gear of fallen reinforcements (all spawned after generation) remain takeable.
    ///
    /// Filtering at <see cref="CaravanFormingUtility.AllReachableColonyItems"/> is the single
    /// chokepoint shared by Dialog_FormCaravan (reform) and TransporterUtility (pods), and is
    /// guarded to Empire battle maps so home colonies and ordinary caravans are unaffected.
    /// </summary>
    [HarmonyPatch(typeof(CaravanFormingUtility), nameof(CaravanFormingUtility.AllReachableColonyItems))]
    class CaravanFormingUtility_AllReachableColonyItems_Patch
    {
        public static void Postfix(Map map, List<Thing> __result)
        {
            if (__result is null || __result.Count == 0) return;
            if (!FCSettings.restrictDefenseMapLoot) return;
            if (!(map?.Parent is WorldSettlementFC)) return;

            BattlefieldContext bf = FindFC.MilitaryManager?.GetBattlefield(map.Tile);
            if (bf is null) return;

            /* __result is vanilla's reused static tmpThings buffer, rebuilt every call and
               iterated synchronously by the caller this frame, so in-place removal is safe. */
            int before = __result.Count;
            __result.RemoveAll(t => bf.IsSettlementLoot(t));
            int removed = before - __result.Count;

            if (removed > 0)
                LogUtil.Message($"CaravanItemRestriction: withheld {removed} Empire item(s) from caravan at tile {map.Tile}");
        }
    }
}
