using FactionColonies.util;
using GiddyUp;
using Verse;
using Verse.AI;

namespace FactionColonies.GiddyUpCompat
{
    /// <summary>
    /// Giddy Up 2 implementation of <see cref="IGiddyUpBridge"/>. Only exists in Empire.GiddyUp.dll,
    /// which is loaded only when Giddy Up 2 is active (LoadFolders.xml conditional loading).
    ///
    /// Uses only GU2's PUBLIC surface: <see cref="ModSettings_GiddyUp.MountableCache"/> for the
    /// mountability gate and <see cref="ExtendedDataStorage.isMounted"/> for the mounted-state check.
    /// Mounting itself goes through GU2's "Mount" JobDef (GU2's MountUtility.GoMount is internal).
    /// </summary>
    public class GiddyUpBridge : IGiddyUpBridge
    {
        // Resolved when the bridge is constructed (after defs load, via [StaticConstructorOnStartup]).
        private static readonly JobDef MountJob = DefDatabase<JobDef>.GetNamedSilentFail("Mount");

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
            if (MountJob is null || rider?.jobs is null || mount is null) return;
            // Idempotent: GU2 tracks every mounted rider in this public set, so don't re-issue the job.
            if (ExtendedDataStorage.isMounted.Contains(rider.thingIDNumber)) return;
            Job job = new Job(MountJob, new LocalTargetInfo(mount)) { count = 1 };
            rider.jobs.StartJob(job);
        }
    }
}
