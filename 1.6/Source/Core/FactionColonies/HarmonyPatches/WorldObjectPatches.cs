using HarmonyLib;
using RimWorld;
using RimWorld.Planet;

namespace FactionColonies
{
    /// <summary>
    /// Triggers road network recalculation when non-Empire settlements are
    /// added, removed, or change faction on the world map.
    /// </summary>

    [HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Add))]
    class WorldObjectAddPatch
    {
        public static void Postfix(WorldObject o)
        {
            PerfWatchdog.Enter("WorldObjectAdd.Postfix");
            if (o is Settlement)
                FactionCache.FactionComp?.roadBuilder?.FlagUpdateRoadQueues();
            PerfWatchdog.Exit();
        }
    }

    [HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Remove))]
    class WorldObjectRemovePatch
    {
        public static void Postfix(WorldObject o)
        {
            PerfWatchdog.Enter("WorldObjectRemove.Postfix");
            if (o is Settlement)
                FactionCache.FactionComp?.roadBuilder?.FlagUpdateRoadQueues();
            PerfWatchdog.Exit();
        }
    }

    [HarmonyPatch(typeof(WorldObject), nameof(WorldObject.SetFaction))]
    class WorldObjectSetFactionPatch
    {
        public static void Postfix(WorldObject __instance)
        {
            PerfWatchdog.Enter("WorldObjectSetFaction.Postfix");
            if (__instance is Settlement)
                FactionCache.FactionComp?.roadBuilder?.FlagUpdateRoadQueues();
            PerfWatchdog.Exit();
        }
    }

    /// <summary>
    /// Prevents vanilla from treating WorldSettlementFC as a defeated enemy
    /// settlement when its map is removed. Without this, CheckDefeated spawns
    /// DestroyedSettlement objects and crashes in TimedDetectionRaids.CopyFrom.
    /// </summary>
    [HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
    class SettlementDefeatUtilityPatch
    {
        static bool Prefix(Settlement factionBase)
        {
            PerfWatchdog.Enter("CheckDefeated.Prefix");
            bool result = !(factionBase is WorldSettlementFC);
            PerfWatchdog.Exit();
            return result;
        }
    }
}
