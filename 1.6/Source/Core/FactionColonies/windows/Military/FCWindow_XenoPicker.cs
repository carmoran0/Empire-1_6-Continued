using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace FactionColonies
{
    public class FCWindow_XenoPicker : Window
    {
        private readonly MilUnitFC unit;
        private XenotypeDef selectedDef;
        private string searchTerm = "";
        private Vector2 scrollPos;

        private const float RowHeight = 30f;
        private const float IconSize = 24f;
        private const float SearchBarHeight = 28f;
        private const float ButtonHeight = 35f;
        private const float margin = 5f;

        public override Vector2 InitialSize => new Vector2(450f, 550f);

        public FCWindow_XenoPicker(MilUnitFC unit)
        {
            this.unit = unit;
            selectedDef = unit.xenotype;
            draggable = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            // Title
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(new Rect(0, 0, inRect.width, 35f), "changeUnitXenoButton".Translate());

            // Search bar
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect searchRect = new Rect(0, 40f, inRect.width, SearchBarHeight);
            searchTerm = Widgets.TextField(searchRect, searchTerm);

            // Build xeno list — includes non-violent xenotypes (they get a warning label)
            List<string> seenXenos = new List<string>();
            List<(XenotypeDef def, string label)> xenoOptions = new List<(XenotypeDef, string)>();

            foreach (XenotypeDef def in FactionCache.XenotypeDefs)
            {
                xenoOptions.Add((def, def.label.CapitalizeFirst()));
            }

            xenoOptions.Sort((a, b) => string.Compare(a.label, b.label, StringComparison.OrdinalIgnoreCase));

            // Filter by search
            List<(XenotypeDef def, string label)> filtered = string.IsNullOrEmpty(searchTerm)
                ? xenoOptions
                : xenoOptions.Where(x => x.label.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

            // Scroll view
            float listTop = searchRect.yMax + margin;
            float listHeight = inRect.height - listTop - ButtonHeight - 15f;
            Rect scrollOutRect = new Rect(0, listTop, inRect.width, listHeight);
            Widgets.DrawMenuSection(scrollOutRect);

            float viewHeight = filtered.Count * RowHeight;
            Rect scrollViewRect = new Rect(0, 0, scrollOutRect.width - (viewHeight > listHeight ? 16f : 0f),
                Mathf.Max(viewHeight, listHeight));

            Widgets.BeginScrollView(scrollOutRect, ref scrollPos, scrollViewRect);

            for (int i = 0; i < filtered.Count; i++)
            {
                var (def, label) = filtered[i];
                bool isNonViolent = FactionCache.XenotypeIsNonViolent(def);
                Rect row = new Rect(0, i * RowHeight, scrollViewRect.width, RowHeight);

                if (def == selectedDef)
                    Widgets.DrawHighlightSelected(row);
                else if (i % 2 == 0)
                    Widgets.DrawHighlight(row);

                Rect iconRect = new Rect(row.x + 2f, row.y + 3f, IconSize, IconSize);
                GUI.DrawTexture(iconRect, def.Icon);

                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                Rect labelRect = new Rect(iconRect.xMax + 5f, row.y, row.width - IconSize - 10f, RowHeight);
                float xenoFactor = def.genes.Aggregate(1f, (acc, g) => acc * g.marketValueFactor);
                if (isNonViolent) xenoFactor *= 0.25f;
                string costText = xenoFactor != 1f ? " (x" + xenoFactor.ToString("F2") + $" {"Cost".Translate()})" : "";

                Color prevColor = GUI.color;
                if (isNonViolent)
                {
                    GUI.color = new Color(1f, 0.85f, 0.4f); // amber warning
                    Widgets.Label(labelRect, label + costText + " [" + "FCNonViolent".Translate() + "]");
                }
                else
                {
                    Widgets.Label(labelRect, label + costText);
                }
                GUI.color = prevColor;

                if (isNonViolent)
                {
                    TooltipHandler.TipRegion(row, "FCNonViolentXenoTooltip".Translate());
                }

                if (Widgets.ButtonInvisible(row))
                {
                    selectedDef = def;
                }
            }

            if (filtered.Count == 0)
            {
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(scrollOutRect, "changeUnitXenoNoXenos".Translate());
            }

            Widgets.EndScrollView();

            // Bottom buttons
            float buttonWidth = 120f;
            Rect buttonBar = new Rect(0, inRect.height - ButtonHeight - 5f, inRect.width, ButtonHeight);

            Rect cancelRect = new Rect(buttonBar.xMax - buttonWidth, buttonBar.y, buttonWidth, buttonBar.height);
            if (Widgets.ButtonText(cancelRect, "CancelButton".Translate()))
            {
                Close();
            }

            bool canConfirm = selectedDef != null;
            Rect confirmRect = new Rect(cancelRect.x - buttonWidth - 10f, buttonBar.y, buttonWidth, buttonBar.height);
            if (Widgets.ButtonText(confirmRect, "FCConfirm".Translate(), active: canConfirm))
            {
                if (canConfirm)
                {
                    unit.xenotype = selectedDef;
                    if (FactionCache.XenotypeIsNonViolent(selectedDef))
                    {
                        unit.ClearWeapon();
                    }
                    unit.RerollPreviewPawn();
                    Close();
                }
            }

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
        }
    }
}
