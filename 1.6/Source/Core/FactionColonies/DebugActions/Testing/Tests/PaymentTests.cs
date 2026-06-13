using System.Collections.Generic;
using System.Linq;
using Verse;

namespace FactionColonies
{
    public static class PaymentTests
    {
        // ============================
        // SilverPaymentContext (pure)
        // ============================

        [EmpireTest("Payment")]
        public static void SilverPaymentContext_Constructor_SetsFields()
        {
            var ctx = new SilverPaymentContext(500, PaymentUtil.Reason_SquadDeployment);
            TestAssert.AreEqual(500, ctx.Amount, message: "Amount");
            TestAssert.AreEqual(PaymentUtil.Reason_SquadDeployment, ctx.Reason, message: "Reason");
            TestAssert.IsNull(ctx.Settlement, "Settlement should default to null");
        }

        [EmpireTest("Payment")]
        public static void SilverPaymentContext_ModifierReducesAmount()
        {
            var modifier = new TestPaymentZeroer();
            SilverPaymentRegistry.Register(modifier);
            try
            {
                var ctx = new SilverPaymentContext(200, "test");
                SilverPaymentRegistry.InvokeModifiers(ctx);
                TestAssert.AreEqual(0, ctx.Amount, "Modifier should zero the amount");
            }
            finally
            {
                SilverPaymentRegistry.Unregister(modifier);
            }
        }

        // ============================
        // PaySilver (game state)
        // ============================

        [EmpireTest("Payment")]
        public static void PaySilver_ZeroAmount_ReturnsTrue()
        {
            // Zero amount should succeed without consuming anything
            TestAssert.IsTrue(PaymentUtil.PaySilver(0, "test"),
                "PaySilver(0) should return true");
        }

        [EmpireTest("Payment")]
        public static void PaySilver_NegativeAmount_ReturnsTrue()
        {
            // Negative amount early-returns true
            TestAssert.IsTrue(PaymentUtil.PaySilver(-10, "test"),
                "PaySilver(-10) should return true");
        }

        [EmpireTest("Payment")]
        public static void PaySilver_RegistryReducesToZero_Succeeds()
        {
            var modifier = new TestPaymentZeroer();
            SilverPaymentRegistry.Register(modifier);
            try
            {
                // Even though we "pay" 9999, the modifier zeros it, so no silver consumed
                TestAssert.IsTrue(PaymentUtil.PaySilver(9999, "test"),
                    "PaySilver should succeed when modifier zeros the amount");
            }
            finally
            {
                SilverPaymentRegistry.Unregister(modifier);
            }
        }

        // ============================
        // TryPaySilver (atomic, game state)
        // ============================

        [EmpireTest("Payment")]
        public static void TryPaySilver_ZeroAmount_SucceedsNoDeduction()
        {
            int before = PaymentUtil.GetSilver();
            TestAssert.IsTrue(PaymentUtil.TryPaySilver(0, "test"), "TryPaySilver(0) should return true");
            TestAssert.AreEqual(before, PaymentUtil.GetSilver(), message: "TryPaySilver(0) must not deduct");
        }

        [EmpireTest("Payment")]
        public static void TryPaySilver_NegativeAmount_SucceedsNoDeduction()
        {
            int before = PaymentUtil.GetSilver();
            TestAssert.IsTrue(PaymentUtil.TryPaySilver(-50, "test"), "TryPaySilver(-50) should return true");
            TestAssert.AreEqual(before, PaymentUtil.GetSilver(), message: "TryPaySilver(negative) must not deduct");
        }

        [EmpireTest("Payment")]
        public static void TryPaySilver_Unaffordable_ReturnsFalseNoDeduction()
        {
            int before = PaymentUtil.GetSilver();
            // Ask for far more than any plausible balance: must refuse and deduct nothing (closes the
            // L1 footgun where the legacy PaySilver always returned true).
            TestAssert.IsFalse(PaymentUtil.TryPaySilver(before + 1_000_000, "test"),
                "TryPaySilver beyond the balance should return false");
            TestAssert.AreEqual(before, PaymentUtil.GetSilver(), message: "Failed TryPaySilver must deduct nothing");
        }

        [EmpireTest("Payment")]
        public static void TryPaySilver_ModifierZeros_SucceedsNoDeduction()
        {
            var modifier = new TestPaymentZeroer();
            SilverPaymentRegistry.Register(modifier);
            try
            {
                int before = PaymentUtil.GetSilver();
                TestAssert.IsTrue(PaymentUtil.TryPaySilver(9999, "test"),
                    "TryPaySilver should succeed when a modifier zeros the amount");
                TestAssert.AreEqual(before, PaymentUtil.GetSilver(), message: "Zeroed payment must not deduct");
            }
            finally
            {
                SilverPaymentRegistry.Unregister(modifier);
            }
        }

        [EmpireTest("Payment")]
        public static void TryPaySilver_ModifierInflates_ChecksEffectiveAmount()
        {
            var modifier = new TestPaymentInflator(1_000_000);
            SilverPaymentRegistry.Register(modifier);
            try
            {
                int before = PaymentUtil.GetSilver();
                // Nominal amount is 1, but the modifier inflates it past any balance. TryPaySilver must
                // check the modifier-adjusted (effective) amount, not the nominal one, so it refuses.
                TestAssert.IsFalse(PaymentUtil.TryPaySilver(1, "test"),
                    "TryPaySilver must check the modifier-adjusted (effective) amount");
                TestAssert.AreEqual(before, PaymentUtil.GetSilver(), message: "Refused inflated payment must deduct nothing");
            }
            finally
            {
                SilverPaymentRegistry.Unregister(modifier);
            }
        }

        // ============================
        // GetSilver (game state)
        // ============================

        [EmpireTest("Payment")]
        public static void GetSilver_IsNonNegative()
        {
            int silver = PaymentUtil.GetSilver();
            TestAssert.IsTrue(silver >= 0, $"GetSilver should be >= 0, got {silver}");
        }

        // ============================
        // ReturnValueOfTithe (pure)
        // ============================

        [EmpireTest("Payment")]
        public static void ReturnValueOfTithe_EmptyList_ReturnsZero()
        {
            double value = PaymentUtil.ReturnValueOfTithe(new List<Thing>());
            TestAssert.AreEqual(0.0, value, message: "Empty list should return 0");
        }

        // ============================
        // GenerateRewardThings (game state)
        // ============================

        [EmpireTest("Payment")]
        public static void GenerateRewardThings_NullRewardDef_ReturnsEmpty()
        {
            List<Thing> result = PaymentUtil.GenerateRewardThings(100, null);
            TestAssert.IsNotNull(result, "Should return non-null list");
            TestAssert.IsEmpty(result, "Null rewardDef should produce empty list");
        }

        [EmpireTest("Payment")]
        public static void GenerateRewardThings_ValidDef_ProducesThings()
        {
            ResourceEventRewardDef rewardDef = DefDatabase<ResourceEventRewardDef>.AllDefsListForReading
                .FirstOrDefault();
            if (rewardDef == null) TestAssert.Skip("No ResourceEventRewardDef found");

            List<Thing> result = PaymentUtil.GenerateRewardThings(500, rewardDef);

            TestAssert.IsNotNull(result, "Should return non-null list");
            TestAssert.IsNotEmpty(result, $"GenerateRewardThings should produce items for {rewardDef.defName}");
            foreach (Thing t in result)
            {
                TestAssert.IsNotNull(t, "Generated thing should not be null");
            }
        }

        // ============================
        // Test Double
        // ============================

        private class TestPaymentZeroer : ISilverPaymentModifier
        {
            public void ModifyPayment(SilverPaymentContext context)
            {
                context.Amount = 0;
            }
        }

        private class TestPaymentInflator : ISilverPaymentModifier
        {
            private readonly int add;
            public TestPaymentInflator(int add) { this.add = add; }
            public void ModifyPayment(SilverPaymentContext context)
            {
                context.Amount += add;
            }
        }
    }
}
