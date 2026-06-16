using System.Linq;
using Verse;

namespace FactionColonies
{
    /* DESTRUCTIVE: runs real tax cycles against the live ledger. Creates bills, spends/accrues
       silver, applies penalties, and may revoke edicts on unpaid upkeep. Not reverted. */
    public static class TaxEconomyDestructiveTests
    {
        [EmpireDestructiveTest("Destructive.Tax")]
        public static void AddTax_GeneratesBills()
        {
            FactionFC f = DestructiveTestUtil.RequireFaction();
            if (f.settlements.Count == 0) TestAssert.Skip("No settlements to tax");

            int before = FindFC.TaxLedger.Bills.Count;
            TestAssert.DoesNotThrow(() => FindFC.TaxLedger.AddTax(f), "AddTax threw");
            TestAssert.IsTrue(FindFC.TaxLedger.Bills.Count > before,
                "AddTax should add at least one bill when settlements exist");
            DestructiveTestUtil.AssertEmpireInvariants(f, "AddTax_GeneratesBills");
        }

        [EmpireDestructiveTest("Destructive.Tax")]
        public static void ProcessBills_AndAutoresolve_NoDangling()
        {
            FactionFC f = DestructiveTestUtil.RequireFaction();
            if (f.settlements.Count == 0) TestAssert.Skip("No settlements to tax");

            TestAssert.DoesNotThrow(() => FindFC.TaxLedger.AddTax(f), "AddTax threw");
            TestAssert.DoesNotThrow(() => FindFC.TaxLedger.AutoresolveBills(), "AutoresolveBills threw");
            TestAssert.DoesNotThrow(() => FindFC.TaxLedger.ProcessBills(), "ProcessBills threw");
            // AssertEmpireInvariants verifies no surviving bill references a removed settlement.
            DestructiveTestUtil.AssertEmpireInvariants(f, "ProcessBills_AndAutoresolve");
        }

        [EmpireDestructiveTest("Destructive.Tax")]
        public static void TaxTick_AfterReschedule_Advances()
        {
            FactionFC f = DestructiveTestUtil.RequireFaction();
            if (FindFC.EmpireFaction is null) TestAssert.Skip("No Empire faction");

            FindFC.TaxLedger.Reschedule(0); // due now
            int now = Find.TickManager.TicksGame;
            TestAssert.DoesNotThrow(() => FindFC.TaxLedger.TaxTick(f, FindFC.EmpireFaction), "TaxTick threw");
            TestAssert.GreaterThan(FindFC.TaxLedger.nextTaxDueTick, now,
                "TaxTick should reschedule nextTaxDueTick into the future");
            DestructiveTestUtil.AssertEmpireInvariants(f, "TaxTick_AfterReschedule");
        }

        [EmpireDestructiveTest("Destructive.Tax")]
        public static void AddTax_WithTransientSettlement_ThenRemove_BillCleared()
        {
            FactionFC f = DestructiveTestUtil.RequireFaction();

            WorldSettlementFC s = DestructiveTestUtil.CreateTransientSettlement();
            if (s is null) TestAssert.Skip("No valid tile found for a new settlement");

            TestAssert.DoesNotThrow(() => FindFC.TaxLedger.AddTax(f), "AddTax threw");
            // The new settlement should now have at least one bill.
            TestAssert.IsTrue(FindFC.TaxLedger.Bills.Any(b => b?.settlement == s),
                "New settlement should have a tax bill after AddTax");

            DestructiveTestUtil.SafeRemoveSettlement(s);
            TestAssert.IsFalse(FindFC.TaxLedger.Bills.Any(b => b?.settlement == s),
                "Removing the settlement should sweep its orphaned bills");
            DestructiveTestUtil.AssertEmpireInvariants(f, "AddTax_WithTransientSettlement_ThenRemove");
        }
    }
}
