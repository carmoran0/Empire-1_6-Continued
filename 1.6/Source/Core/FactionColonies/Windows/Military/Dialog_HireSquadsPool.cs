using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /// <summary>
    /// Browse-and-hire menu for squad templates. Lists every <see cref="MilSquadFC"/> in
    /// <see cref="MilitaryFC.squads"/> as a card (accent strip, projected power, unit count,
    /// hire cost) with a per-card Hire button that calls <see cref="MilitaryFC.HireSquad"/>.
    /// An Edit button per card jumps to that template in the squad designer.
    /// <para>Two modes: with no <c>targetSettlement</c> the window hires into the unassigned pool
    /// and stays open so several squads can be hired in one session. With a <c>targetSettlement</c>
    /// (the settlement window's "Hire &amp; assign here" flow) it hires one squad, assigns it to
    /// that settlement, and closes.</para>
    /// Silver and per-card affordability re-read every frame so the readout updates live as money
    /// is spent.
    /// </summary>
    public class Dialog_HireSquadsPool : Window
    {
        public override Vector2 InitialSize => new Vector2(620f, 560f);

        /* Card layout constants — mirror Dialog_SquadPicker / MainTabWindow_Squads so every
           squad-listing surface shares the same rhythm. */
        private const float Pad = 4f;
        private const float RowGap = 2f;
        private const float CardHeaderH = 24f;
        private const float CardDetailH = 22f;
        private const float CardH = CardHeaderH + CardDetailH;
        private const float AccentW = 4f;

        private const float TitleH = 32f;
        private const float ToolbarH = 24f;

        private readonly MilitaryFC mfc;
        private readonly WorldSettlementFC targetSettlement;
        private Vector2 scrollPos;
        private bool affordableOnly = false;

        public Dialog_HireSquadsPool(WorldSettlementFC targetSettlement = null)
        {
            this.targetSettlement = targetSettlement;
            mfc = FindFC.Military;
            doCloseX = true;
            forcePause = false;
            absorbInputAroundWindow = true;
            draggable = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Re-read silver each frame so the readout + per-card gating update live after each
            // hire (HireSquad spends silver synchronously via PaymentUtil.TryPaySilver).
            int silver = PaymentUtil.GetSilver();

            // Title — "Hire & assign to X" in the assign flow, plain "Hire Squads" for the pool.
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            string title = targetSettlement is object
                ? (string)"FCHireSquadsPoolHeaderAssign".Translate(targetSettlement.Name)
                : (string)"FCHireSquadsPoolHeader".Translate();
            Widgets.Label(new Rect(0f, 0f, inRect.width, TitleH), title);

            // Toolbar: affordable-only checkbox (left) + silver readout (right)
            float toolbarY = TitleH + 4f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.CheckboxLabeled(new Rect(0f, toolbarY, 200f, ToolbarH),
                "FCDialogHireSquadAffordableOnly".Translate(), ref affordableOnly);

            Text.Anchor = TextAnchor.MiddleRight;
            UIUtil.DrawColoredLabel(new Rect(inRect.width - 320f, toolbarY, 320f, ToolbarH),
                "FCDialogHireSquadSilver".Translate(silver), Color.gray);

            // Card list (framed)
            float listTop = toolbarY + ToolbarH + 6f;
            Rect listRect = new Rect(0f, listTop, inRect.width, inRect.height - listTop);
            Widgets.DrawMenuSection(listRect);
            DrawCardList(listRect, silver);

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        private void DrawCardList(Rect listRect, int silver)
        {
            List<MilSquadFC> templates = mfc?.squads ?? new List<MilSquadFC>();

            // Build the visible set first so the scroll height matches what we draw (the
            // affordable-only filter can hide rows). Null templates guarded defensively, matching
            // MainTabWindow_Squads.
            List<MilSquadFC> visible = new List<MilSquadFC>();
            for (int i = 0; i < templates.Count; i++)
            {
                MilSquadFC t = templates[i];
                if (t is null) continue;
                if (affordableOnly && silver < HireCost(t)) continue;
                visible.Add(t);
            }

            if (visible.Count == 0)
            {
                // Distinguish "no templates designed" from "templates exist but none affordable".
                string msg = templates.Count == 0
                    ? (string)"FCHireSquadsPoolEmpty".Translate()
                    : (string)"FCHireSquadsPoolNoneAffordable".Translate();
                TextAnchor anchorBefore = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                UIUtil.DrawColoredLabel(new Rect(listRect.x, listRect.y + listRect.height * 0.35f,
                    listRect.width, 40f), msg, Color.gray);
                Text.Anchor = anchorBefore;
                return;
            }

            float innerX = listRect.x + Pad;
            float innerW = listRect.width - Pad * 2f;
            Rect viewRect = new Rect(innerX, listRect.y + Pad, innerW, listRect.height - Pad * 2f);
            float totalH = visible.Count * (CardH + RowGap);
            Rect scrollRect = ScrollUtil.BeginScrollView(viewRect, ref scrollPos, totalH);

            float runningY = 0f;
            bool alternate = false;
            for (int i = 0; i < visible.Count; i++)
            {
                Rect cardRect = new Rect(0f, runningY, scrollRect.width, CardH);
                if (alternate) Widgets.DrawHighlight(cardRect);
                DrawTemplateCard(cardRect, visible[i], silver, alternate);
                runningY += CardH + RowGap;
                alternate = !alternate;
            }

            ScrollUtil.EndScrollView();
        }

        /// <summary>Silver charged by <see cref="MilitaryFC.HireSquad"/> for this template — the
        /// identical expression so the displayed cost and affordability gate match the charge
        /// exactly. NOT the deployment cost.</summary>
        private static int HireCost(MilSquadFC template) =>
            (int)Math.Round(template.GetEquipmentTotalCost() * FCSettings.squadHireCostMultiplier);

        /* Per-template card. Header row: accent strip + template name. Detail row: projected
           power | unit count | hire cost. Right column: Edit (jump to the designer) + Hire
           buttons. Accent + labels go amber when unaffordable, green when affordable (MilReady /
           MilUnderfunded convention shared across the military surfaces). */
        private void DrawTemplateCard(Rect cardRect, MilSquadFC template, int silver, bool isHighlighted)
        {
            int cost = HireCost(template);
            bool affordable = silver >= cost;

            Color accent = affordable ? AccentUtil.MilReady : AccentUtil.MilUnderfunded;
            Color labelTint = affordable ? Color.white : AccentUtil.MilUnderfunded;

            // Accent strip
            Widgets.DrawBoxSolid(new Rect(cardRect.x, cardRect.y, AccentW, cardRect.height), accent);

            float contentX = cardRect.x + AccentW + 6f;

            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            MilSquadFC captured = template;
            float btnY = cardRect.y + 4f;
            float btnH = CardH - 8f;

            // Hire button (rightmost). HireSquad re-reads + spends silver and shows its own
            // Messages; no pre-pay. In assign mode, assign the hire and close; in pool mode stay
            // open (browse-and-hire) — next frame re-reads silver.
            const float hireBtnW = 110f;
            float hireBtnX = cardRect.xMax - hireBtnW - 4f;
            Rect hireRect = new Rect(hireBtnX, btnY, hireBtnW, btnH);
            if (UIUtil.ButtonFlat(hireRect, "FCHireSquadButton".Translate(cost),
                    disabled: !affordable, highlighted: isHighlighted))
            {
                MercenarySquadFC hired = mfc?.HireSquad(captured);
                if (hired is object && targetSettlement is object)
                {
                    mfc.AttemptToAssign(hired, targetSettlement);
                    Close();
                    return;
                }
            }
            TooltipHandler.TipRegion(hireRect, "FCHireSquadButtonTip".Translate(cost));

            // Edit button — opens this template in the squad designer and closes the hire window.
            const float editBtnW = 54f;
            float editBtnX = hireBtnX - 4f - editBtnW;
            Rect editRect = new Rect(editBtnX, btnY, editBtnW, btnH);
            if (UIUtil.ButtonFlat(editRect, "FCHireSquadsPoolEdit".Translate(), highlighted: isHighlighted))
            {
                OpenInDesigner(captured);
                return;
            }
            TooltipHandler.TipRegion(editRect, "FCHireSquadsPoolEditTip".Translate());

            float labelsRight = editBtnX - 6f;

            // Header row: template name.
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            UIUtil.DrawColoredLabel(new Rect(contentX, cardRect.y, labelsRight - contentX, CardHeaderH),
                template.name ?? "(?)", labelTint);

            // Detail row: Power | Units | Cost.
            float detailY = cardRect.y + CardHeaderH;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;

            double power = SquadPowerRegistry.LevelFromCost(template.GetEquipmentTotalCost());
            int unitCount = template.Units != null ? template.Units.Count : 0;

            string powerLbl = (string)"FCSquadColPower".Translate() + ": " + power.ToString("0.0");
            string unitsLbl = "FCHireSquadsPoolUnits".Translate(unitCount);
            string costLbl = (string)"FCSquadColCost".Translate() + ": $" + cost;

            float labelsW = labelsRight - contentX;
            if (labelsW < 0f) labelsW = 0f;
            float colPower = Math.Min(110f, labelsW * 0.34f);
            float colUnits = Math.Min(110f, labelsW * 0.33f);
            float colCost = Math.Max(0f, labelsW - colPower - colUnits);

            float dx = contentX;
            UIUtil.DrawColoredLabel(new Rect(dx, detailY, colPower, CardDetailH), powerLbl, labelTint); dx += colPower;
            UIUtil.DrawColoredLabel(new Rect(dx, detailY, colUnits, CardDetailH), unitsLbl, labelTint); dx += colUnits;
            UIUtil.DrawColoredLabel(new Rect(dx, detailY, colCost, CardDetailH), costLbl, labelTint);

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }

        /* Open (or focus) the squad designer on this template, then close the hire window. Mirrors
           Dialog_ManageExportsFC.OnImport: reuse an open Squads military window if present,
           otherwise spawn one. */
        private void OpenInDesigner(MilSquadFC template)
        {
            FCWindow_Military milWindow = (FCWindow_Military)Find.WindowStack.Windows.FirstOrDefault(
                w => w is FCWindow_Military fcw && fcw.GetMilitaryWindow().Slot == MilitaryWindowSlot.Squads);

            if (milWindow != null)
            {
                milWindow.SetActive(template);
            }
            else
            {
                MilitaryWindow dsw = MilitaryWindowRegistry.CreateSquads(mfc, FindFC.FactionComp);
                FCWindow_Military newWindow = new FCWindow_Military(dsw, "FCMilitaryTableButtonCreateSquad".Translate());
                Find.WindowStack.Add(newWindow);
                newWindow.SetActive(template);
            }

            Close();
        }
    }
}
