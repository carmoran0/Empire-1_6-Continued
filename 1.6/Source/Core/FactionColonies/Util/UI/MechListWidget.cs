using FactionColonies.util;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    /* Shared mechanitor panel for the unit designer's Mechs tab (Biotech only). A "make mechanitor"
     * toggle gates the rest: when on, the unit is given a mechlink at spawn and can be assigned mechs.
     * Bandwidth (read from the preview pawn's MechBandwidth stat — so control-sublink implants and
     * bandwidth-pack apparel count) limits how many mechs fit; the "Add mech" picker hard-blocks an
     * over-budget add. A per-design work-mode chooser sets the mode applied to the unit's bonded mechs.
     * Mirrors ImplantListWidget/AbilityListWidget: reads displayUnit, routes mutations via getEditTarget. */
    public static class MechListWidget
    {
        public struct Options
        {
            public bool canEdit;
            public bool showHeaderButtons;
            public Func<MilUnitFC> getEditTarget;
            public Func<MilUnitFC> getDisplayUnit;
        }

        private const float headerHeight = 25f;
        private const float rowHeight = 28f;
        private const float removeButtonSize = 20f;
        private const float stepperButtonW = 20f;
        private const float IconSize = 24f;

        public static void Draw(Rect rect, MilUnitFC displayUnit, ref Vector2 scrollPos, Options opts)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Biotech-absent guard (the tab is normally hidden, but be defensive).
            if (!ModsConfig.BiotechActive)
            {
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(rect, "fcMechsNoBiotech".Translate());
                Text.Font = fontBefore;
                Text.Anchor = anchorBefore;
                return;
            }

            bool editable = opts.canEdit && opts.showHeaderButtons;

            // --- "Make mechanitor" toggle ---
            Rect toggleRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
            bool isMech = displayUnit?.isMechanitor ?? false;
            bool newIsMech = isMech;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            if (editable)
            {
                Widgets.CheckboxLabeled(toggleRect, "fcMakeMechanitor".Translate(), ref newIsMech);
                if (newIsMech != isMech)
                {
                    MilUnitFC t = opts.getEditTarget?.Invoke();
                    if (t != null) t.SetMechanitor(newIsMech);
                }
            }
            else
            {
                Widgets.Label(toggleRect, "fcMakeMechanitor".Translate() + ": " + (isMech ? "Yes" : "No"));
            }

            // When not a mechanitor, show a hint and stop.
            if (!(displayUnit?.IsMechanitorDesign ?? false) && !newIsMech)
            {
                Rect hintRect = new Rect(rect.x, toggleRect.yMax + 4f, rect.width, rect.height - headerHeight - 6f);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(hintRect.ContractedBy(4f), "fcMechsHint".Translate());
                Text.Font = fontBefore;
                Text.Anchor = anchorBefore;
                return;
            }

            // --- Bandwidth summary + work-mode chooser + add button ---
            float used = displayUnit?.UsedMechBandwidth ?? 0f;
            float total = displayUnit?.TotalMechBandwidth ?? 0f;
            Rect bwRect = new Rect(rect.x, toggleRect.yMax + 2f, rect.width, headerHeight);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            if (used > total + 0.0001f) GUI.color = ColorLibrary.RedReadable;
            Widgets.Label(bwRect, "fcMechBandwidth".Translate(used.ToString("0.#"), total.ToString("0.#")));
            GUI.color = Color.white;

            // Buttons row below the bandwidth label: work-mode chooser (left, wide) + Add Mech (right).
            float buttonsY = bwRect.yMax + 2f;
            if (editable)
            {
                float addW = 110f;
                Rect addBtnRect = new Rect(rect.xMax - addW, buttonsY, addW, headerHeight);
                if (Widgets.ButtonText(addBtnRect, "fcAddMech".Translate()))
                {
                    Func<MilUnitFC> getDisplay = opts.getDisplayUnit ?? (() => displayUnit);
                    Find.WindowStack.Add(new FCWindow_MechPicker(getDisplay, opts.getEditTarget));
                }

                // Work-mode chooser (opens a FloatMenu of MechWorkModeDefs). Fills the row up to the Add button.
                Rect wmRect = new Rect(rect.x, buttonsY, addBtnRect.x - rect.x - 6f, headerHeight);
                MechWorkModeDef curMode = displayUnit?.ResolvedMechWorkMode;
                Text.Anchor = TextAnchor.MiddleCenter;
                if (Widgets.ButtonText(wmRect, "fcMechWorkMode".Translate() + ": " + (curMode?.LabelCap.ToString() ?? "")))
                {
                    List<FloatMenuOption> modeOpts = new List<FloatMenuOption>();
                    foreach (MechWorkModeDef mode in DefDatabase<MechWorkModeDef>.AllDefsListForReading)
                    {
                        MechWorkModeDef captured = mode;
                        modeOpts.Add(new FloatMenuOption(mode.LabelCap, delegate
                        {
                            MilUnitFC t = opts.getEditTarget?.Invoke();
                            if (t != null) t.SetMechWorkMode(captured);
                        }));
                    }
                    if (modeOpts.Count > 0) Find.WindowStack.Add(new FloatMenu(modeOpts));
                }
            }

            // --- Assigned mech list ---
            float listTop = (editable ? buttonsY + headerHeight : bwRect.yMax) + 4f;
            Rect listOutRect = new Rect(rect.x, listTop, rect.width, rect.height - (listTop - rect.y));
            List<SavedMech> items = displayUnit?.mechs ?? new List<SavedMech>();
            float viewHeight = items.Count * rowHeight;
            Rect scrollViewRect = ScrollUtil.BeginScrollView(listOutRect, ref scrollPos, viewHeight);

            for (int i = 0; i < items.Count; i++)
            {
                SavedMech item = items[i];
                if (item.kind?.race is null) continue;
                int index = i;
                Rect row = new Rect(scrollViewRect.x, scrollViewRect.y + i * rowHeight, scrollViewRect.width, rowHeight);
                if (i % 2 == 0) Widgets.DrawHighlight(row);

                Rect iconRect = new Rect(row.x + 2f, row.y + 2f, IconSize, IconSize);
                Widgets.ThingIcon(iconRect, item.kind.race);

                // Remove button (far right)
                Rect removeRect = Rect.zero;
                if (editable)
                {
                    removeRect = new Rect(row.xMax - removeButtonSize - 2f, row.y + (rowHeight - removeButtonSize) / 2f, removeButtonSize, removeButtonSize);
                    Text.Font = GameFont.Small;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    if (Widgets.ButtonText(removeRect, "X"))
                    {
                        MilUnitFC target = opts.getEditTarget?.Invoke();
                        if (target != null) target.RemoveMech(index);
                    }
                }

                // Count steppers ( - N + )
                float stepRight = editable ? removeRect.x - 6f : row.xMax - 4f;
                if (editable)
                {
                    Rect plusRect = new Rect(stepRight - stepperButtonW, row.y + (rowHeight - stepperButtonW) / 2f, stepperButtonW, stepperButtonW);
                    Rect countRect = new Rect(plusRect.x - 26f, row.y, 26f, rowHeight);
                    Rect minusRect = new Rect(countRect.x - stepperButtonW, plusRect.y, stepperButtonW, stepperButtonW);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleCenter;
                    if (Widgets.ButtonText(minusRect, "-"))
                    {
                        MilUnitFC target = opts.getEditTarget?.Invoke();
                        if (target != null) target.DecrementMech(index);
                    }
                    Widgets.Label(countRect, "x" + Mathf.Max(1, item.count));
                    if (Widgets.ButtonText(plusRect, "+"))
                    {
                        MilUnitFC target = opts.getEditTarget?.Invoke();
                        if (target != null) target.AddMech(item.kind);   // hard-blocks on bandwidth
                    }
                    stepRight = minusRect.x - 6f;
                }
                else
                {
                    Rect countRect = new Rect(stepRight - 30f, row.y, 30f, rowHeight);
                    Text.Font = GameFont.Tiny;
                    Text.Anchor = TextAnchor.MiddleRight;
                    Widgets.Label(countRect, "x" + Mathf.Max(1, item.count));
                    stepRight = countRect.x - 6f;
                }

                // Bandwidth cost
                float bandwidth = item.kind.race.GetStatValueAbstract(StatDefOf.BandwidthCost) * Mathf.Max(1, item.count);
                Rect bwCostRect = new Rect(stepRight - 50f, row.y, 50f, rowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(bwCostRect, "BW " + bandwidth.ToString("0.#"));

                // Label
                string label = item.kind.LabelCap;
                Rect labelRect = new Rect(iconRect.xMax + 6f, row.y, bwCostRect.x - iconRect.xMax - 10f, rowHeight);
                Text.Font = GameFont.Tiny;
                Text.Anchor = TextAnchor.MiddleLeft;
                string shownLabel = Text.ClampTextWithEllipsis(labelRect, label);
                Widgets.Label(labelRect, shownLabel);
                if (shownLabel != label) TooltipHandler.TipRegion(labelRect, label);
            }

            ScrollUtil.EndScrollView();

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }
    }
}
