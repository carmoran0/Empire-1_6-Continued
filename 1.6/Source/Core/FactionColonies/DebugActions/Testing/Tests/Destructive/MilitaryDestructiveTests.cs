using System.Linq;
using RimWorld.Planet;

namespace FactionColonies
{
    /* DESTRUCTIVE: hires/dismisses real mercenary squads (spends silver), assigns them, and
       creates/unregisters real deploy operations. Not reverted. */
    public static class MilitaryDestructiveTests
    {
        private static WorldSettlementFC FirstOrTransientSettlement()
        {
            WorldSettlementFC s = FindFC.Settlements?.FirstOrDefault();
            if (s is null) s = DestructiveTestUtil.CreateTransientSettlement();
            return s;
        }

        [EmpireDestructiveTest("Destructive.Military")]
        public static void HireSquad_AddsMercenarySquad()
        {
            FactionFC f = DestructiveTestUtil.RequireFaction();
            MilSquadFC template = DestructiveTestUtil.FirstHireableTemplate();
            if (template is null) TestAssert.Skip("No squad templates designed");

            int before = FindFC.Military.mercenarySquads.Count;
            MercenarySquadFC squad = null;
            TestAssert.DoesNotThrow(() => squad = FindFC.Military.HireSquad(template), "HireSquad threw");
            if (squad is null) TestAssert.Skip("Could not afford to hire a squad");

            TestAssert.AreEqual(before + 1, FindFC.Military.mercenarySquads.Count, "Hiring should add one squad");
            TestAssert.IsTrue(FindFC.Military.mercenarySquads.Contains(squad), "Hired squad should be in the pool");
            DestructiveTestUtil.AssertEmpireInvariants(f, "HireSquad_AddsMercenarySquad");
        }

        [EmpireDestructiveTest("Destructive.Military")]
        public static void DismissSquad_RemovesIt()
        {
            FactionFC f = DestructiveTestUtil.RequireFaction();
            MilSquadFC template = DestructiveTestUtil.FirstHireableTemplate();
            if (template is null) TestAssert.Skip("No squad templates designed");

            MercenarySquadFC squad = null;
            TestAssert.DoesNotThrow(() => squad = FindFC.Military.HireSquad(template), "HireSquad threw");
            if (squad is null) TestAssert.Skip("Could not afford to hire a squad");

            bool dismissed = false;
            TestAssert.DoesNotThrow(() => dismissed = FindFC.Military.DismissSquad(squad), "DismissSquad threw");
            TestAssert.IsTrue(dismissed, "DismissSquad should succeed for a non-busy squad");
            TestAssert.IsFalse(FindFC.Military.mercenarySquads.Contains(squad), "Dismissed squad should be gone from the pool");
            DestructiveTestUtil.AssertEmpireInvariants(f, "DismissSquad_RemovesIt");
        }

        [EmpireDestructiveTest("Destructive.Military")]
        public static void AssignUnassign_RoundTrips()
        {
            FactionFC f = DestructiveTestUtil.RequireFaction();
            WorldSettlementFC settlement = FirstOrTransientSettlement();
            if (settlement is null) TestAssert.Skip("No settlement available");
            if (settlement.MilitaryComp is null) TestAssert.Skip("Settlement has no MilitaryComp");

            MilSquadFC template = DestructiveTestUtil.FirstHireableTemplate();
            if (template is null) TestAssert.Skip("No squad templates designed");
            MercenarySquadFC squad = null;
            TestAssert.DoesNotThrow(() => squad = FindFC.Military.HireSquad(template), "HireSquad threw");
            if (squad is null) TestAssert.Skip("Could not afford to hire a squad");

            bool assigned = false;
            TestAssert.DoesNotThrow(() => assigned = FindFC.Military.AttemptToAssign(squad, settlement), "AttemptToAssign threw");
            if (!assigned) TestAssert.Skip("Assignment rejected by registry validators");

            TestAssert.IsTrue(squad.settlement == settlement, "Squad should be billeted at the settlement");
            bool unassigned = false;
            TestAssert.DoesNotThrow(() => unassigned = FindFC.Military.Unassign(squad), "Unassign threw");
            TestAssert.IsTrue(unassigned, "Unassign should succeed for a non-busy squad");
            TestAssert.IsNull(squad.settlement, "Squad should have no billet after unassign");
            DestructiveTestUtil.AssertEmpireInvariants(f, "AssignUnassign_RoundTrips");
        }

        [EmpireDestructiveTest("Destructive.Military")]
        public static void CreateDeployOp_RegistersThenUnregister()
        {
            FactionFC f = DestructiveTestUtil.RequireFaction();
            WorldSettlementFC settlement = FirstOrTransientSettlement();
            if (settlement is null) TestAssert.Skip("No settlement available");

            MilSquadFC template = DestructiveTestUtil.FirstHireableTemplate();
            if (template is null) TestAssert.Skip("No squad templates designed");
            MercenarySquadFC squad = null;
            TestAssert.DoesNotThrow(() => squad = FindFC.Military.HireSquad(template), "HireSquad threw");
            if (squad is null) TestAssert.Skip("Could not afford to hire a squad");
            FindFC.Military.Assign(squad, settlement, true); // ensure source.settlement is set

            MilitaryOperation op = null;
            PlanetTile tile = settlement.Tile;
            TestAssert.DoesNotThrow(() => op = FindFC.MilitaryManager.CreateDeployOp(squad, tile), "CreateDeployOp threw");
            if (op is null) TestAssert.Skip("Deploy rejected (morale lock?)");

            TestAssert.IsTrue(FindFC.MilitaryManager.Active.Contains(op), "Deploy op should be registered as active");
            TestAssert.DoesNotThrow(() => FindFC.MilitaryManager.Unregister(op), "Unregister threw");
            TestAssert.IsFalse(FindFC.MilitaryManager.Active.Contains(op), "Op should be gone from active after Unregister");
            DestructiveTestUtil.AssertEmpireInvariants(f, "CreateDeployOp_RegistersThenUnregister");
        }

        [EmpireDestructiveTest("Destructive.Military")]
        public static void HireDismiss_Repeated_x5()
        {
            FactionFC f = DestructiveTestUtil.RequireFaction();
            MilSquadFC template = DestructiveTestUtil.FirstHireableTemplate();
            if (template is null) TestAssert.Skip("No squad templates designed");

            for (int i = 0; i < 5; i++)
            {
                MercenarySquadFC squad = null;
                TestAssert.DoesNotThrow(() => squad = FindFC.Military.HireSquad(template), $"HireSquad threw on cycle {i}");
                if (squad is null) TestAssert.Skip($"Could not afford to hire on cycle {i}");
                TestAssert.DoesNotThrow(() => FindFC.Military.DismissSquad(squad), $"DismissSquad threw on cycle {i}");
                DestructiveTestUtil.AssertEmpireInvariants(f, $"HireDismiss cycle {i}");
            }
        }
    }
}
