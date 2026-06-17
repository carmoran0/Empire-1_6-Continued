using System.Collections.Generic;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace FactionColonies
{
    /* Base for a settlement WorldObjectComp that can absorb pawns delivered straight to the
       settlement by transport pod (see TransportersArrivalAction_AddToSettlementFC), skipping the
       tedious land -> form caravan -> transfer flow.

       One arrival type per subclass (the arrival action identifies the chosen type by its class
       name, so don't register two of the same type on one settlement). WorldSettlementFC enumerates
       AllComps.OfType<WorldObjectComp_SettlementPawnArrival>() and offers one float-menu option per
       comp that accepts at least one pawn in the launched pods. */
    public abstract class WorldObjectComp_SettlementPawnArrival : WorldObjectComp
    {
        public WorldSettlementFC Settlement => parent as WorldSettlementFC;

        public abstract bool AcceptsPawn(Pawn pawn);

        /* Settlement-level gate (permission / capacity / state), enforced at both menu time and
           arrival. The central battle/raid block lives in the arrival action, not here. */
        public virtual FloatMenuAcceptanceReport CanReceive => true;

        /* Float-menu label, e.g. "Add prisoners to <settlement>". */
        public abstract string ArrivalMenuLabel { get; }

        /* Absorb the already-filtered pawns. By the time this runs they have already been removed
           from the pod containers, so this comp becomes their sole owner. */
        public abstract void ReceivePawns(List<Pawn> pawns);
    }
}
