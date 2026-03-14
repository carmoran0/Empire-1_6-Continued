using FactionColonies.util;
using HarmonyLib;
using Verse;

namespace FactionColonies
{
    [HarmonyPatch(typeof(Pawn), "Kill")]
    class MercenaryDied
    {
        static bool Prefix(Pawn __instance)
        {
            PerfWatchdog.Enter("MercenaryDied.Prefix");
            if (__instance.IsMercenary())
            {
                if (__instance.Faction != FactionCache.PlayerColonyFaction) __instance.SetFaction(FactionCache.PlayerColonyFaction);
                MercenarySquadFC squad = FactionCache.FactionComp.militaryCustomizationUtil.ReturnSquadFromUnit(__instance);
                if (squad != null)
                {
                    Mercenary merc = FactionCache.FactionComp.militaryCustomizationUtil.ReturnMercenaryFromUnit(__instance, squad);
                    if (merc != null)
                    {
                        if (squad.settlement != null)
                        {
                            if (FCSettings.deadPawnsIncreaseMilitaryCooldown)
                            {
                                squad.dead += 1;
                            }

                            squad.settlement.GainHappiness(-1d);
                        }

                        squad.PassPawnToDeadMercenaries(merc);
                    }

                    squad.RemoveDroppedEquipment();
                }
                else
                {
                    LogUtil.Warning("Mercenary Errored out. Did not find squad.");
                }

                __instance.equipment?.DestroyAllEquipment();
                __instance.apparel?.DestroyAll();
                //__instance.Destroy();
                PerfWatchdog.Exit();
                return true;
            }

            PerfWatchdog.Exit();
            return true;
        }
    }

    [HarmonyPatch(typeof(DeathActionWorker_Simple), "PawnDied")]
    class MercenaryAnimalDied
    {
        static bool Prefix(Corpse corpse)
        {
            PerfWatchdog.Enter("MercAnimalDied.Prefix");
            if (FactionCache.FactionComp.militaryCustomizationUtil.IsMercenaryPawn(corpse.InnerPawn))
            {
                //corpse.InnerPawn.SetFaction(FactionColonies.getPlayerColonyFaction());
                corpse.Destroy();
                PerfWatchdog.Exit();
                return false;
            }

            PerfWatchdog.Exit();
            return true;
        }
    }

    // [HarmonyPatch(typeof(JobGiver_AnimalFlee), "TryGiveJob")]
    class TryGiveJobFleeAnimal
    {
        static bool Prefix(Pawn pawn)
        {
            if (FactionCache.FactionComp.militaryCustomizationUtil.IsMercenaryPawn(pawn))
            {
                return false;
            }

            return true;
        }
    }

}
