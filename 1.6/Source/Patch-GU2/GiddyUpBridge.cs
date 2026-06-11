using FactionColonies.util;
using GiddyUp;
using GiddyUpCore.Core;
using Verse;

namespace FactionColonies.GiddyUpCompat
{
    /// <summary>
    /// Giddy Up 2 implementation of <see cref="IGiddyUpBridge"/>. Only exists in Empire.GiddyUp.dll,
    /// which is loaded only when Giddy Up 2 is active (LoadFolders.xml conditional loading).
    ///
    /// The Empire.GiddyUp project publicizes GiddyUpCore (Krafs.Publicizer + IgnoresAccessChecksTo), so
    /// this calls GU2's internal MountUtility/StorageUtility directly with typed syntax — no reflection.
    /// <see cref="Mount"/> uses GU2's instant mount (GiveJobMethod.Instant), the same path GU2 uses to
    /// spawn pre-mounted raiders, so the merc ends up mounted immediately with no walk-up.
    /// </summary>
    public class GiddyUpBridge : IGiddyUpBridge
    {
        public bool IsMountable(PawnKindDef kind)
        {
            if (kind?.race is null) return false;
            // GU2's MountableCache (built from its settings + Mountable/NotMountable mod extensions) is the
            // same gate GU2 uses to decide whether a kind can ever be ridden. AllAnimalKindDefs are already
            // animals, so this cache check is sufficient for filtering the mount picker.
            return ModSettings_GiddyUp.MountableCache.Contains(kind.race.shortHash);
        }

        public void Mount(Pawn rider, Pawn mount)
        {
            if (rider is null || mount is null) return;
            if (rider.IsMounted()) return;   // idempotent — don't re-mount an already-mounted rider
            // Instant: set the mounted state directly (no walk-up job), as GU2 does for pre-mounted raiders.
            rider.GoMount(mount, MountUtility.GiveJobMethod.Instant);
            // GU2 draws the rider as a render node on the MOUNT's render tree and suppresses the rider's
            // own draw. The Mount job rebuilds that tree at the end; GoMount(Instant) does NOT. So when we
            // mount a rider whose mount was already drawn (mid-deploy, after pods open), the mount's tree
            // lacks the rider node and the rider goes invisible. Rebuild it (same call the Mount job uses).
            MountedRiderRenderNodeUtility.RefreshMountedAnimalGraphics(mount);
        }
    }
}
