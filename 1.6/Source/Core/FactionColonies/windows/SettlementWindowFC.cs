using FactionColonies.util;
using RimWorld;
using RimWorld.Planet;
using RimWorld.QuestGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace FactionColonies
{
    public sealed class SettlementWindowFc : Window
    {
        public override Vector2 InitialSize
        {
            get { return new Vector2(1250f, 645f); }
        }


        //UI STUFF
        public const int ScrollSpacing = 45;
        public const int ScrollHeight = 315;

        private const int margin = 5;
        private const int smallMargin = 3;

        //time variables
        private int uiUpdateTimer;
        private int maxScroll;
        private FactionFC factionfc;

        // Building UI values
        private const int buildingSpacing = margin;//15;
        private const int buildingBoxSide = 72;

        private const int constructionListItemLabelHeight = 15;
        private const int constructionListProgressBarHeight = 10;
        private const int constructionListItemHeight = (smallMargin * 4) + (constructionListItemLabelHeight * 2) + constructionListProgressBarHeight; // 4 * smallMargin + 2 * label height + progress bar height
        private const int constructionListIconHeight = constructionListItemLabelHeight * 2 + smallMargin;

        private const int buildingSpacingFromSide = margin; //15; // (494 - (spacing + boxSide) * elementsPerRow) / 2;

        private const int scrollSpacing = 16;

        // UI State
        private int overviewTab = 0;
        private int titheTab = 0;

        // Tithe buffers
        private List<string> titheBuffers = new List<string>();
        private int currentDictSize = 0;

        // Comps with overview tabs
        private List<ISettlementWindowOverview> overviews = new List<ISettlementWindowOverview>();

        public override void PreOpen()
        {
            base.PreOpen();
            maxScroll = (settlement.Resources.Count * ScrollSpacing) - ScrollHeight;
            factionfc = FactionCache.FactionComp;

            foreach (WorldObjectComp comp in settlement.AllComps)
            {
                ISettlementWindowOverview overview = comp as ISettlementWindowOverview;
                if (!(overview is null))
                {
                    overview.PreOpenWindow(settlement);
                    overviews.Add(overview);
                    overviewTabs.Add(overview.OverviewTabName());
                }
            }
        }
        public override void PostClose()
        {
            base.PostClose();

            foreach(ISettlementWindowOverview overview in overviews)
            {
                overview.PostCloseWindow();
            }
        }

        public void UiUpdate()
        {
            if (uiUpdateTimer == 0)
            {
                uiUpdateTimer = FCSettings.updateUiTimer;
            }
            else
            {
                uiUpdateTimer -= 1;
            }
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            UiUpdate();
        }

        private List<string> overviewTabs = new List<string>
        {
            "Overview".Translate(),
            "Tithing".Translate()
        };

        private readonly List<string> stats = new List<string>(5) 
        {
            "FCMilitaryLevel".Translate(),
            "FCHappiness".Translate(),
            "FCLoyality".Translate(),
            "FCUnrest".Translate(),
            "FCProsperity".Translate()
        };

        private readonly List<string> buttons = new List<string>(5)
        {
            "UpgradeTown".Translate(),
            "FCSpecialActions".Translate(),
            "PrisonersMenu".Translate(),
            "Military".Translate(),
            "DeleteSettlement".Translate()
        };

        private WorldSettlementFC settlement; //Don't expose

        public SettlementWindowFc(WorldSettlementFC settlement)
        {
            if (settlement == null)
            {
                Close();
            }

            this.settlement = settlement;
            forcePause = false;
            draggable = true;
            doCloseX = true;
            preventCameraMotion = false;
        }


        public override void DoWindowContents(Rect inRect)
        {
            PerfWatchdog.Enter("SettlementWindow.DoWindowContents");
            GameFont fontBefore = Text.Font;
            TextAnchor anchorBefore = Text.Anchor;

            float validWidth = InitialSize.x - (Margin * 2);
            float validHeight = InitialSize.y - (Margin * 2);

            float leftWidth = 150f;
            float rightWidth = 415f;
            float centerWidth = validWidth - leftWidth - rightWidth - (margin * 2);

            Rect headerBox = new Rect(inRect.x, inRect.y, leftWidth + centerWidth + margin, 30 + (margin * 2) + 60);
            Rect leftBox = new Rect(inRect.x, headerBox.yMax + (margin * 2), leftWidth, validHeight - headerBox.height - (margin * 2));
            Rect centerBox = new Rect(leftBox.xMax + margin, headerBox.yMax + (margin * 2), centerWidth, validHeight - headerBox.height - (margin * 2));
            Rect rightBox = new Rect(centerBox.xMax + margin, inRect.y, rightWidth, validHeight);

            DrawCenterHeader(headerBox);
            DrawLeftInfo(leftBox);
            DrawCenterInfo(centerBox);
            DrawRightInfo(rightBox);

            Text.Font = fontBefore;
            Text.Anchor = anchorBefore;
            PerfWatchdog.Exit();
        }

        /* Left side overview */
        private void DrawLeftInfo(Rect boundingBox)
        {
            Rect topBox = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, boundingBox.height / 2f);
            Rect topBoxInner = new Rect(topBox.x + margin, topBox.y + margin, topBox.width - (margin * 2), topBox.height - (margin * 2));
            Rect botBox = new Rect(topBox.x, topBox.yMax, topBox.width, topBox.height);
            Rect botBoxInner = new Rect(botBox.x + margin, botBox.y + margin, botBox.width - (margin * 2), botBox.height - (margin * 2));

            Color origColor = GUI.color;
            GUI.color = Color.gray;
            Widgets.DrawBox(topBox);
            Widgets.DrawBox(botBox);
            GUI.color = origColor;
            DrawSettlementStats(topBoxInner);
            DrawMainButtons(botBoxInner);
        }

        private void DrawCenterInfo(Rect boundingBox)
        {
            Color origColor = GUI.color;
            float nTabs = overviewTabs.Count;
            float tabY = boundingBox.y;
            float tabHeight = 20f;
            float tabWidth = boundingBox.width / nTabs;
            Rect chosenRect = new Rect();
            for (int i = 0; i < nTabs; i++)
            {
                Rect tabRect = new Rect(boundingBox.x + (tabWidth * i), tabY, tabWidth, tabHeight);
                TaggedString label = overviewTabs[i];
                string nulabel = Text.ClampTextWithEllipsis(tabRect, label);
                if (nulabel != label)
                {
                    UIUtil.TipRegionByText(tabRect, label);
                }
                if (Widgets.ButtonText(tabRect, label))
                {
                    overviewTab = i;
                    if (overviews.Count > 0 && overviewTab >= 2 && (overviewTab - 2) < overviews.Count)
                    {
                        ISettlementWindowOverview overview = overviews[overviewTab - 2];
                        overview.OnTabSwitch();
                    }
                }
                if (overviewTab == i)
                {
                    chosenRect = tabRect;
                }
            }
            //tabs for different sections: Overview, Tithing, sub-mod added windows
            Rect overviewBounds = new Rect(boundingBox.x, tabY + tabHeight, boundingBox.width, boundingBox.height - tabHeight);
            Rect infobox = new Rect(overviewBounds.x + margin, overviewBounds.y + margin, overviewBounds.width - (margin*2), overviewBounds.height - (margin*2)); //originally: 520 width, 340 height
            GUI.color = Color.gray;
            //fancy custom tab stuff
            Widgets.DrawLineHorizontal(boundingBox.x, chosenRect.yMax, chosenRect.x - boundingBox.x);
            Widgets.DrawLineVertical(chosenRect.x, chosenRect.y, chosenRect.height);
            Widgets.DrawLineHorizontal(chosenRect.x, chosenRect.y, chosenRect.width);
            Widgets.DrawLineVertical(chosenRect.xMax, chosenRect.y, chosenRect.height);
            Widgets.DrawLineHorizontal(chosenRect.xMax, chosenRect.yMax, boundingBox.xMax - chosenRect.xMax);
            Widgets.DrawLineVertical(boundingBox.x, chosenRect.yMax, boundingBox.height - chosenRect.height);
            Widgets.DrawLineVertical(boundingBox.xMax, chosenRect.yMax, boundingBox.height - chosenRect.height);
            Widgets.DrawLineHorizontal(boundingBox.x, boundingBox.yMax-1, boundingBox.width);
            GUI.color = origColor;
            DrawOverview(infobox);
        }
        private void DrawOverview(Rect boundingBox)
        {
            if (overviewTab == 0)
            {
                DrawBasicOverview(boundingBox);
            }
            else if (overviewTab == 1)
            {
                DrawTitheOverview(boundingBox);
            }
            else if (overviews.Count > 0 && overviewTab >= 2 && (overviewTab - 2) < overviews.Count)
            {
                ISettlementWindowOverview overview = overviews[overviewTab - 2];
                overview.DrawOverviewTab(boundingBox);
            }
        }
        private void DrawCenterHeader(Rect boundingBox)
        {
            /* Settlement name on top */
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect nameRect = new Rect(boundingBox.x + margin, boundingBox.y + margin, boundingBox.width - (margin * 2 + 20), 30);
            Widgets.Label(nameRect, settlement.Name);
            //Draw name settings button
            Rect configRect = new Rect(nameRect.xMax + margin, boundingBox.y + margin, 20, 20);
            if (Widgets.ButtonImage(configRect, TexLoad.iconCustomize))
            {
                //if click faction customize button
                Find.WindowStack.Add(new SettlementCustomizeWindowFc(settlement));
            }
            // Just used for alignment. Can maybe use this box to draw some background art based on the settlement's biome. Kinda like stellaris, maybe
            Rect infoBox = new Rect(boundingBox.x, nameRect.yMax, boundingBox.width, boundingBox.height - (nameRect.height + margin * 2));

            /* Town level */
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Rect levelBoundingBox = new Rect(infoBox.x, infoBox.y + margin, 60, 60);
            //gotta love aligning text
            Rect levelBox = new Rect(levelBoundingBox.x + 14, levelBoundingBox.y + 14, 30, 30);
            Widgets.DrawShadowAround(levelBox);
            Widgets.DrawHighlight(levelBoundingBox);
            Widgets.DrawBox(levelBoundingBox);
            Widgets.Label(levelBox, settlement.settlementLevel.ToString());

            // Draw settlement type, basic description (from def), and location text
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rightSideWidth = boundingBox.width - (margin + levelBoundingBox.width);
            Rect typeBox = new Rect(levelBoundingBox.xMax + margin, levelBoundingBox.y, rightSideWidth, levelBoundingBox.height / 2);
            Rect typeTextBox = new Rect(typeBox.x + margin, typeBox.y, (typeBox.width - (margin * 2))/2f, typeBox.height);
            Rect foundTextBox = new Rect(typeTextBox.xMax, typeTextBox.y, typeTextBox.width, typeTextBox.height);
            Rect basicDescBox = new Rect(typeBox.x, typeBox.yMax, rightSideWidth * 0.4f, levelBoundingBox.height / 2);
            Rect basicDescTextBox = new Rect(basicDescBox.x + margin, basicDescBox.y, basicDescBox.width - (margin * 2), basicDescBox.height);
            Rect locBox = new Rect(basicDescBox.xMax + margin, typeBox.yMax, (rightSideWidth * 0.6f) - margin, levelBoundingBox.height / 2);
            Rect locTextBox = new Rect(locBox.x + margin, locBox.y, locBox.width - (margin * 2), locBox.height);
            Widgets.DrawHighlight(typeBox);
            Widgets.Label(typeTextBox, settlement.settlementDef.LabelCap);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(foundTextBox, "FCFoundedOn".Translate(settlement.GetFoundingDate()));
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Tiny;
            Widgets.Label(basicDescTextBox, TextUtil.GetTownTitle(settlement));
            Widgets.DrawLineVertical(basicDescBox.xMax, basicDescBox.y + margin, basicDescBox.height - (margin * 2));
            //TODO: localize this. LabelCap and description can be localized through def injection, but locationText is derived differently
            Widgets.Label(locTextBox, settlement.locationText);
        }
        private void DrawBasicOverview(Rect boundingBox)
        {
            float bottomHeight = (buildingBoxSide * 3) + (buildingSpacing * 4) + 30f;
            Rect topRect = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, (boundingBox.height - margin - bottomHeight));
            Rect botRect = new Rect(boundingBox.x, topRect.yMax + margin, boundingBox.width, bottomHeight);

            DrawBasicOverviewTop(topRect);
            DrawBasicOverviewBottom(botRect);
        }

        private void DrawBasicOverviewTop(Rect boundingBox)
        {
            // Maybe we'll put more than just the description here. Who knows?
            // A biome-fitting image might be cool, kind of like Stellaris
            DrawDescription(boundingBox);
        }

        private void DrawBasicOverviewBottom(Rect boundingBox)
        {
            float scrollMargin = ((settlement.BuildingsComp?.Buildings.Count ?? 0) > 8) ? scrollSpacing : 0;
            float buildingBoxWidth = Math.Max(boundingBox.x * 0.7f, (buildingSpacingFromSide * 2) + (buildingBoxSide * 4) + (buildingSpacing * 3) + scrollMargin);
            float constructionBoxWidth = boundingBox.width - buildingBoxWidth;

            Rect leftBox = new Rect(boundingBox.x, boundingBox.y, constructionBoxWidth, boundingBox.height);
            Rect rightBox = new Rect(leftBox.xMax, leftBox.y, buildingBoxWidth, boundingBox.height);

            if (settlement.BuildingsComp == null)
            {
                DrawFacilities(rightBox);
                return;
            }

            int numUnderConstruction = settlement.BuildingsComp.GetUnderConstructionBuildings().Count + (settlement.isUpgrading ? 1 : 0);
            DrawConstructionBox(leftBox, numUnderConstruction, settlement.BuildingsComp.GetUnderConstructionBuildings());
            DrawFacilities(rightBox);
        }
        private void DrawTitheOverview(Rect boundingBox)
        {
            Color origColor = GUI.color;
            List<ResourceFC> resources = settlement.GetTitheableResources();
            int numResources = resources.Count;
            /* Draw resource tabs on the left */
            float tabWidth = 25f;
            float tabHeight = boundingBox.height / numResources;
            Rect chosenRect = new Rect();
            for (int i = 0; i < numResources; i++)
            {
                Rect tabBox = new Rect(boundingBox.x, boundingBox.y + (tabHeight * i), tabWidth, tabHeight);
                float imgSize = Math.Min(tabWidth, tabHeight);
                Rect iconBox = new Rect(tabBox.x + 2f + (tabWidth - imgSize) / 2f, tabBox.y + (tabHeight - imgSize) / 2f, imgSize, imgSize);
                if (UIUtil.ButtonFlat(tabBox, "", highlighted: titheTab == i))
                {
                    titheTab = i;
                    UpdateTitheDictBuffers(resources[i]);
                }
                Text.Font = GameFont.Small;
                Widgets.Label(iconBox, new GUIContent(resources[i].def.Icon));
                // Resource color accent
                Widgets.DrawBoxSolid(new Rect(tabBox.x, tabBox.y, 3f, tabBox.height), resources[i].def.color);
                UIUtil.TipRegionByText(tabBox, resources[i].def.LabelCap);
                if (titheTab == i)
                {
                    chosenRect = tabBox;
                }
            }
            GUI.color = resources[titheTab].def.color;
            //fancy custom tab stuff
            Widgets.DrawLineVertical(chosenRect.xMax, boundingBox.y, chosenRect.y - boundingBox.y);
            Widgets.DrawLineHorizontal(chosenRect.x, chosenRect.y, chosenRect.width);
            Widgets.DrawLineVertical(chosenRect.x, chosenRect.y, chosenRect.height);
            Widgets.DrawLineHorizontal(chosenRect.x, chosenRect.yMax, chosenRect.width);
            Widgets.DrawLineVertical(chosenRect.xMax, chosenRect.yMax, boundingBox.yMax - chosenRect.yMax);
            GUI.color = origColor;

            ResourceFC titheRes = resources[titheTab];

            /* Calculate heights */
            float headerHeight = 30 + margin + (23f * 3);//60f;
            float footerHeight = 23f;
            float bodyHeight = boundingBox.height - headerHeight - footerHeight - (margin * 2);
            float randomboxHeight = 0f;
            if (titheRes.hasRandomTithe)
            {
                randomboxHeight = bodyHeight / 2f;
            }
            else
            {
                randomboxHeight = 23f * 2;
            }
            float scrollboxHeight = bodyHeight - randomboxHeight;

            float bodyX = boundingBox.x + tabWidth + margin;
            float bodyWidth = boundingBox.width - tabWidth - margin;

            /* Header box */
            Rect headerBox = new Rect(bodyX, boundingBox.y, bodyWidth, headerHeight);
            DrawTitheHeaderBox(headerBox, titheRes);

            /* Scrollbox */
            Rect scrollBox = new Rect(bodyX, headerBox.yMax + margin, bodyWidth, scrollboxHeight);
            DrawTitheScrollBox(scrollBox, titheRes);

            /* Random tithe box */
            Rect titheBox = new Rect(bodyX, scrollBox.yMax, bodyWidth, randomboxHeight);
            DrawTitheRandomBox(titheBox, titheRes);

            /* Footer box */
            Rect footerBox = new Rect(bodyX, titheBox.yMax + margin, bodyWidth, footerHeight);
            DrawTitheFooterBox(footerBox, titheRes);
        }
        private void DrawTitheHeaderBox(Rect boundingBox, ResourceFC res)
        {
            Rect iconBox = new Rect(boundingBox.x, boundingBox.y, 30f, 30f);
            Rect labelHighlight = new Rect(iconBox.xMax + margin, boundingBox.y, boundingBox.width - margin - iconBox.width, 30f);
            Rect labelText = new Rect(labelHighlight.x + smallMargin, labelHighlight.y + smallMargin, labelHighlight.width - (smallMargin*2), labelHighlight.height - (smallMargin*2));

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.DrawHighlight(iconBox);
            Widgets.Label(iconBox, new GUIContent(res.def.Icon));
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.DrawHighlight(labelHighlight);
            Widgets.Label(labelText, res.def.LabelCap);
            Rect iconAccent = new Rect(iconBox.x, iconBox.y, iconBox.width, 3f);
            Rect labelAccent = new Rect(labelHighlight.x, labelHighlight.y, labelHighlight.width, 3f);
            Widgets.DrawBoxSolid(iconAccent, res.def.color);
            Widgets.DrawBoxSolid(labelAccent, res.def.color);

            /* Info boxes */
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rowHeight = 23f;
            float labelHeight = rowHeight - (smallMargin * 2);

            Rect titheModBox = new Rect(boundingBox.x, iconBox.yMax + margin, (boundingBox.width - margin)/2f, rowHeight * 3f);
            Rect prodBox = new Rect(titheModBox.xMax + margin, iconBox.yMax + margin, (boundingBox.width - margin)/2f, rowHeight);
            Rect budgetBox = new Rect(titheModBox.xMax + margin, prodBox.yMax, (boundingBox.width - margin) / 2f, rowHeight);

            /* Tithe modifier info */
            Rect titheRow1 = new Rect(titheModBox.x, titheModBox.y, titheModBox.width, rowHeight);
            Rect trow1label = new Rect(titheRow1.x + smallMargin, titheRow1.y + smallMargin, (titheRow1.width - (smallMargin * 2)), labelHeight);
            Rect titheRow2 = new Rect(titheModBox.x + (margin * 2), titheRow1.yMax, titheModBox.width - (margin * 2), rowHeight);
            Rect trow2label = new Rect(titheRow2.x + smallMargin, titheRow2.y + smallMargin, (titheRow2.width - (smallMargin * 2))*0.75f, labelHeight);
            Rect trow2num = new Rect(trow2label.xMax, trow2label.y, (titheRow2.width - (smallMargin * 2)) * 0.25f, labelHeight);
            Rect titheRow3 = new Rect(titheRow2.x, titheRow2.yMax, titheRow2.width, rowHeight);
            Rect trow3label = new Rect(titheRow3.x + smallMargin, titheRow3.y + smallMargin, (titheRow3.width - (smallMargin * 2))*0.75f, labelHeight);
            Rect trow3num = new Rect(trow3label.xMax, trow3label.y, (titheRow3.width - (smallMargin * 2)) * 0.25f, labelHeight);
            Widgets.DrawHighlight(titheModBox);
            Widgets.DrawHighlight(titheRow1);
            Widgets.Label(trow1label, "TitheModifier".Translate());
            Widgets.Label(trow2label, "PerWorker".Translate());
            Widgets.DrawHighlight(titheRow3);
            Widgets.Label(trow3label, "Total".Translate());
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(trow2num, res.GetTitheModifierPerWorker().ToString());
            Widgets.Label(trow3num, res.GetTotalTitheModifierForWorkers().ToString());

            /* Production */
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect prodLabel = new Rect(prodBox.x + smallMargin, prodBox.y + smallMargin, (prodBox.width - (margin * 2)) * 0.75f, labelHeight);
            Rect prodnum = new Rect(prodLabel.xMax, prodLabel.y, (prodBox.width - (margin * 2)) * 0.25f, labelHeight);
            Widgets.DrawHighlight(prodBox);
            Widgets.Label(prodLabel, "TotalProd".Translate());
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(prodnum, res.taxableProductionMarketValue.ToString());

            /* Tithe Budget */
            Text.Anchor = TextAnchor.MiddleLeft;
            Rect budgetLabel = new Rect(budgetBox.x + smallMargin, budgetBox.y + smallMargin, (budgetBox.width - (margin * 2)) * 0.75f, labelHeight);
            Rect budgetnum = new Rect(budgetLabel.xMax, budgetLabel.y, (budgetBox.width - (margin * 2)) * 0.25f, labelHeight);
            Widgets.DrawMenuSection(budgetBox);
            Widgets.Label(budgetLabel, "TotalTitheBudget".Translate());
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(budgetnum, res.GetTitheIncome().ToString());
        }
        private Vector2 titheScrollBar = new Vector2();
        private void UpdateTitheDictBuffers(ResourceFC res)
        {
            titheBuffers.Clear();
            if (res != null)
            {
                List<ThingQualityTuple> items = res.GetTitheListKeys();
                for (int i = 0; i < items.Count; i ++)
                {
                    titheBuffers.Add(res.GetTitheListValue(items[i]).ToString());
                }
                res.storedRandomTitheBudgetBuffer = res.storedRandomTitheBudget.ToString();
            }
            currentDictSize = titheBuffers.Count;
        }
        private void KeepTitheDictBuffersUpdated(ResourceFC res)
        {
            if (res != null && currentDictSize != res.GetTitheListCount())
            {
                UpdateTitheDictBuffers(res);
            }
        }
        private void DrawTitheScrollBox(Rect boundingBox, ResourceFC res)
        {
            Color origColor = GUI.color;
            GUI.color = Color.gray;
            Widgets.DrawBox(boundingBox);
            GUI.color = origColor;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rowHeight = 23f;
            Rect header = new Rect(boundingBox.x, boundingBox.y, boundingBox.width * 0.8f, rowHeight);
            Rect headerText = new Rect(header.x + margin, header.y, header.width - (margin * 2), header.height);
            Widgets.DrawHighlight(header);
            Widgets.Label(headerText, "TitheSelection".Translate());

            Rect addItemButton = new Rect(header.xMax, header.y, (boundingBox.width * 0.2f)-smallMargin, header.height);
            if (Widgets.ButtonText(addItemButton, "AddItem".Translate()))
            {
                Find.WindowStack.Add(new SettlementWindowFC_AddTithe(settlement, res));
            }

            KeepTitheDictBuffersUpdated(res);

            List<ThingQualityTuple> titheItems = res.GetTitheListKeys();

            /* Doing weird box-in-a-box to try and fix some UI drawing issues */
            Rect drawBox = new Rect(boundingBox.x + margin, header.yMax, boundingBox.width - (margin * 2), boundingBox.yMax - header.yMax - margin);
            Rect selectedListBox = new Rect(drawBox.x + 2, drawBox.y + 2, drawBox.width - 4, drawBox.height - 4);
            float listHeight = titheItems.Count * rowHeight;
            float width;
            if (listHeight > selectedListBox.height)
            {
                width = selectedListBox.width - scrollSpacing;
            }
            else
            {
                width = selectedListBox.width;
            }
            Rect innerScrollBox = new Rect(selectedListBox.x, selectedListBox.y, width, listHeight);
            Widgets.DrawMenuSection(selectedListBox);
            Widgets.BeginScrollView(selectedListBox, ref titheScrollBar, innerScrollBox);
            for (int i = 0; i <  titheItems.Count; i++)
            {
                ThingQualityTuple thingTuple = titheItems[i];
                ThingDef iThing = thingTuple.thingDef;
                QualityCategory iQuality = thingTuple.quality;
                ThingDef iStuff = thingTuple.stuffDef;
                bool labelExtended = false;

                Rect row = new Rect(innerScrollBox.x, innerScrollBox.y + (i * rowHeight), innerScrollBox.width, rowHeight);
                Rect icon = new Rect(row.x + margin, row.y, rowHeight, rowHeight);
                Rect info = new Rect(icon.xMax, row.y + 2, rowHeight - 4, rowHeight - 4);
                Rect xBox = new Rect(row.xMax - margin - 20f, row.y + 2, rowHeight-4, rowHeight-4);
                Rect fieldBox = new Rect(xBox.x - margin - 180f, row.y+2, 180f, rowHeight-4);
                Rect valueLabel = new Rect(fieldBox.x - margin - 60f, row.y, 60f, rowHeight);
                Rect stuffBox = new Rect(valueLabel.x - margin - 80f, row.y+2, 80f, rowHeight-4);
                Rect qualityBox = new Rect(stuffBox.x - 80f, row.y+2, 80f, rowHeight-4);
                Rect label = new Rect(info.xMax + margin, row.y, qualityBox.x - info.xMax - margin, rowHeight);
                if (i % 2 == 0)
                {
                    Widgets.DrawHighlight(row);
                }


                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(icon, new GUIContent(iThing.uiIcon));
                UIUtil.InfoCardButton(info, iThing);
                if (Widgets.ButtonText(xBox, "X"))
                {
                    res.RemoveFromTitheList(thingTuple);
                    break;
                }
                UIUtil.TipRegionByText(xBox, "TitheXDesc".Translate());
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(valueLabel, $"${Math.Round(res.TitheThingValue(thingTuple),2)}");

                QualityCategory maxQuality = QualityCategory.Legendary;
                if (CraftUtil.ThingHasQuality(iThing) && res.CanSetTitheQuality(out maxQuality))
                {
                    List<QualityCategory> categoryList = res.GetValidTitheQualities(maxQuality);
                    if (Widgets.ButtonText(qualityBox, TextUtil.GetQualityLabelCap(iQuality)))
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (QualityCategory cat in categoryList)
                        {
                            options.Add(new FloatMenuOption(TextUtil.GetQualityLabelCap(cat), delegate
                            {
                                int qty = res.GetTitheListValue(thingTuple);
                                ThingQualityTuple newTuple = new ThingQualityTuple
                                {
                                    thingDef = iThing,
                                    quality = cat,
                                    stuffDef = iStuff
                                };
                                if (res.HasTitheListKey(newTuple))
                                {
                                    Messages.Message(newTuple.ListRejectionMessage(), MessageTypeDefOf.RejectInput);
                                }
                                else
                                {
                                    res.RemoveFromTitheList(thingTuple);
                                    res.AddToTitheList(newTuple, qty);
                                    UpdateTitheDictBuffers(res);
                                }
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                }
                else
                {
                    label.width += qualityBox.width;
                    labelExtended = true;
                }

                if (CraftUtil.ThingIsStuffable(iThing))
                {
                    List<ThingDef> stuffList = res.GetStuffListForThingDef(iThing);
                    if (Widgets.ButtonText(stuffBox, iStuff?.LabelCap ?? "None"))
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (ThingDef stuff in stuffList)
                        {
                            options.Add(new FloatMenuOption(stuff.LabelCap, delegate
                            {
                                int qty = res.GetTitheListValue(thingTuple);
                                ThingQualityTuple newTuple = new ThingQualityTuple
                                {
                                    thingDef = iThing,
                                    quality = iQuality,
                                    stuffDef = stuff
                                };
                                if (res.HasTitheListKey(newTuple))
                                {
                                    Messages.Message(newTuple.ListRejectionMessage(), MessageTypeDefOf.RejectInput);
                                }
                                else
                                {
                                    res.RemoveFromTitheList(thingTuple);
                                    res.AddToTitheList(newTuple, qty);
                                    UpdateTitheDictBuffers(res);
                                }
                            }));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                }
                else if (labelExtended)
                {
                    label.width += stuffBox.width;
                }
                string nulabel = Text.ClampTextWithEllipsis(label, iThing.LabelCap);
                Widgets.Label(label, nulabel);
                if (nulabel != iThing.LabelCap)
                {
                    UIUtil.TipRegionByText(label, iThing.LabelCap);
                }
                // This seems like a *really* hacky way to handle these buffers. Seems like it'd be prone to UI jitteryness, or just general bad feel
                //   keep this in mind when testing...
                int quantity = res.GetTitheListValue(thingTuple);
                int oldQuantity = quantity;
                int max = quantity + res.MaxThingCanAfford(thingTuple);
                string buf = titheBuffers[i];
                Widgets.IntEntry(fieldBox, ref quantity, ref buf);
                int unclamped = quantity;
                quantity = Math.Clamp(quantity, 0, max);
                buf = quantity.ToString();
                if (unclamped > max)
                {
                    if (res.GetTitheIncome() <= 0)
                        Messages.Message("TitheBudgetNoWorkers".Translate(), MessageTypeDefOf.RejectInput);
                    else
                        Messages.Message("TitheBudgetInsufficient".Translate(), MessageTypeDefOf.RejectInput);
                }
                if (oldQuantity != quantity)
                {
                    res.AddToTitheList(thingTuple, quantity, true);
                }
                titheBuffers[i] = buf;
            }

            Widgets.EndScrollView();
        }
        private Vector2 randomTitheScrollBar = new Vector2();
        private void DrawTitheRandomBox(Rect boundingBox, ResourceFC res)
        {
            Color origColor = GUI.color;
            GUI.color = Color.gray;
            Widgets.DrawBox(boundingBox);
            GUI.color = origColor;

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rowHeight = 23f;
            Rect header = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, rowHeight);
            Rect headerText = new Rect(header.x + margin, header.y, header.width - (margin * 2), header.height);
            Widgets.DrawHighlight(header);
            Widgets.CheckboxLabeled(headerText, "RandomTithesEnabled".Translate(), ref res.hasRandomTithe);
            UIUtil.TipRegionByText(header, "RandomTithesDesc".Translate());

            Rect accruedBox = new Rect(boundingBox.x, header.yMax, boundingBox.width * 0.6f, rowHeight);
            Rect accruedTextBox = new Rect(accruedBox.x + smallMargin, accruedBox.y, accruedBox.width - (smallMargin * 2), accruedBox.height);
            Rect disburseBox = new Rect(accruedBox.xMax, header.yMax, boundingBox.width - accruedBox.width, rowHeight);
            Rect disbursedTextBox = new Rect(disburseBox.x + smallMargin, disburseBox.y, disburseBox.width - (smallMargin * 2), disburseBox.height);
            Widgets.Label(accruedTextBox, "RandomTitheAccrued".Translate(res.randomTitheStock));
            Widgets.CheckboxLabeled(disbursedTextBox, "DisburseAccruedRandomTithe".Translate(), ref res.disburseTitheStock);
            UIUtil.TipRegionByText(accruedBox, "RandomTitheAccruedDesc".Translate());
            UIUtil.TipRegionByText(disburseBox, "DisburseAccruedRandomTitheDesc".Translate());

            if (res.hasRandomTithe)
            {
                Rect budgetBox = new Rect(boundingBox.x, disburseBox.yMax, boundingBox.width * 0.6f, 23f);
                Rect budgetTextBox = new Rect(budgetBox.x + margin, budgetBox.y, budgetBox.width - (margin * 2), budgetBox.height);
                Widgets.TextFieldNumericLabeled(budgetTextBox, "RandomTitheBudget".Translate() + ": ", ref res.storedRandomTitheBudget, ref res.storedRandomTitheBudgetBuffer, 0, (float)(res.GetTitheIncome() - res.titheTotalValueNoRandom));
                res.RefreshOnRandomTitheBudgetChange();
                Rect selectBox = new Rect(budgetBox.xMax, budgetBox.y, boundingBox.width * 0.4f - margin, budgetBox.height);
                if (Widgets.ButtonText(selectBox, "ItemSelection".Translate()))
                {
                    Find.WindowStack.Add(new SettlementWindowFC_RandomTithe(settlement, res));
                }

                List<ThingDef> selectedThings = res.GetRandomTitheFilterThings();

                /* Doing weird box-in-a-box to try and fix some UI drawing issues */
                Rect drawBox = new Rect(boundingBox.x + margin, budgetBox.yMax, boundingBox.width - (margin * 2), boundingBox.yMax - budgetBox.yMax - margin);
                Rect selectedListBox = new Rect(drawBox.x + 2, drawBox.y + 2, drawBox.width - 4, drawBox.height - 4);
                float listHeight = selectedThings.Count * rowHeight;
                float width;
                if (listHeight > selectedListBox.height)
                {
                    width = selectedListBox.width - scrollSpacing;
                }
                else
                {
                    width = selectedListBox.width;
                }
                Rect innerScrollBox = new Rect(selectedListBox.x, selectedListBox.y, width, listHeight);

                Widgets.DrawMenuSection(selectedListBox);
                Widgets.BeginScrollView(selectedListBox, ref randomTitheScrollBar, innerScrollBox);

                for (int i = 0; i < selectedThings.Count; i++)
                {
                    ThingDef iThing = selectedThings[i];
                    Rect row = new Rect(innerScrollBox.x, innerScrollBox.y + (i * rowHeight), innerScrollBox.width, rowHeight);
                    Rect icon = new Rect(row.x + margin, row.y, rowHeight, rowHeight);
                    Rect info = new Rect(icon.xMax, row.y + 2, rowHeight - 4, rowHeight - 4);
                    Rect xBox = new Rect(row.xMax - margin - 20f, row.y + 2, 19f, 19f);
                    Rect valueLabel = new Rect(xBox.x - margin - 60f, xBox.y, 60f, rowHeight);
                    Rect label = new Rect(info.xMax + margin, row.y, valueLabel.x - info.xMax - margin, rowHeight);

                    if (i % 2 == 0)
                    {
                        Widgets.DrawHighlight(row);
                    }
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(icon, new GUIContent(iThing.uiIcon));
                    if (Widgets.ButtonText(xBox, "X"))
                    {
                        res.SetRandomTitheFilterAllow(iThing, false);
                    }
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(label, iThing.LabelCap);
                    Widgets.Label(valueLabel, $"${Math.Round(iThing.BaseMarketValue,2)}");
                    UIUtil.InfoCardButton(info, iThing);
                }

                Widgets.EndScrollView();
            }
        }
        private void DrawTitheFooterBox(Rect boundingBox, ResourceFC res)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;

            /* Available Budget */
            float budgetWidth = (boundingBox.width * 0.5f);
            Rect availBudgetBox = new Rect(boundingBox.xMax - budgetWidth, boundingBox.y, budgetWidth, boundingBox.height);
            Rect availLabel = new Rect(availBudgetBox.x + smallMargin, availBudgetBox.y + smallMargin, (availBudgetBox.width - smallMargin * 2) / 2f, availBudgetBox.height - smallMargin * 2);
            Rect availNum = new Rect(availLabel.xMax, availLabel.y, availLabel.width, availLabel.height);
            double totalTithe = Math.Round(res.GetTitheIncome(), 2);
            double usedTithe = Math.Round(res.titheTotalValue, 2);
            TaggedString usedTitheStr = usedTithe.ToString();
            if (usedTithe > totalTithe)
            {
                usedTitheStr = usedTitheStr.Colorize(Color.red);
                UIUtil.TipRegionByText(availNum, "TitheOverBudget".Translate());
            }
            else if (res.actualIncome < 0)
            {
                usedTitheStr = usedTitheStr.Colorize(Color.yellow);
            }
            else
            {
                usedTitheStr = usedTitheStr.Colorize(Color.green);
            }
            Widgets.DrawHighlight(availBudgetBox);
            Widgets.Label(availLabel, "UsedTitheBudget".Translate() + ":");
            Widgets.Label(availNum, "NumRatio".Translate(usedTitheStr, totalTithe));
        }

        //Original: 125 wide, 215ish tall
        private void DrawSettlementStats(Rect boundingBox)
        {
            float statBoxHeight = (boundingBox.height - (4 * margin)) / 5;
            float statGainBoxHeight = 30;
            float statGainBoxWidth = 35;
            float statSize = Math.Min(30f, statBoxHeight);
            for (int i = 0; i < stats.Count; i++)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Text.Font = GameFont.Medium;
                Rect statBox = new Rect(boundingBox.x, boundingBox.y + (statBoxHeight + margin) * i, boundingBox.width, statBoxHeight);
                Widgets.DrawMenuSection(statBox);
                Rect buttonBox = new Rect(statBox.x + margin, statBox.y + margin, statSize + 4, statSize + 4);
                Rect labelBox = new Rect(buttonBox.xMax, buttonBox.y, statBox.width - (buttonBox.width + margin * 2), buttonBox.height);
                Rect statGainBox = new Rect(statBox.xMax - statGainBoxWidth - margin, statBox.y + (statBox.height - statGainBoxHeight)/2, statGainBoxWidth, statGainBoxHeight);
                //Rect statGainLabel = new Rect(statGainBox.x + smallMargin, statGainBox.y + smallMargin, statGainBoxWidth - (smallMargin * 2), statGainBoxHeight - (smallMargin * 2));
                Rect mainToolTipBox = new Rect(statBox.x, statBox.y, statGainBox.x - statBox.x, statBox.height);
                string tooltip = "";
                if (stats[i] == "militaryLevel")
                {
                    Widgets.Label(buttonBox, new GUIContent(TexLoad.iconMilitary));
                    Widgets.Label(labelBox, settlement.settlementMilitaryLevel.ToString());
                    FactionFC fc = FactionCache.FactionComp;
                    double baseLvl = settlement.settlementMilitaryLevel;
                    double eff = settlement.GetStatValue(FCStatDefOf.militaryCombatEfficiency);
                    double atkLvlBonus = fc.GetStatValue(FCStatDefOf.militaryLevelBonusAttacking);
                    double atkEffBonus = fc.GetStatValue(FCStatDefOf.militaryEfficiencyBonusAttacking);
                    double defLvlBonus = fc.GetStatValue(FCStatDefOf.militaryLevelBonusDefending);
                    double defEffBonus = fc.GetStatValue(FCStatDefOf.militaryEfficiencyBonusDefending);
                    double defAdv = FCSettings.defenderAdvantage;
                    double offPower = Math.Round((baseLvl + atkLvlBonus) * eff * atkEffBonus);
                    double defPower = Math.Round((baseLvl + defLvlBonus) * eff * defEffBonus * defAdv);

                    tooltip = "SettlementMilitaryLevel".Translate() + "\n-----\n"
                        + "SettlementMilitaryLevelDesc".Translate() + "\n\n"
                        + "Base level: " + baseLvl;
                    if (Math.Abs(eff - 1.0) > 0.001)
                        tooltip += "\nCombat efficiency: " + eff.ToString("0.0#") + "x";
                    tooltip += "\n\nOffensive Power: " + offPower;
                    if (Math.Abs(atkLvlBonus) > 0.001)
                        tooltip += "\n  Level bonus: +" + atkLvlBonus.ToString("0.#");
                    if (Math.Abs(atkEffBonus - 1.0) > 0.001)
                        tooltip += "\n  Efficiency bonus: " + atkEffBonus.ToString("0.0#") + "x";
                    tooltip += "\n\nDefensive Power: " + defPower;
                    if (Math.Abs(defLvlBonus) > 0.001)
                        tooltip += "\n  Level bonus: +" + defLvlBonus.ToString("0.#");
                    if (Math.Abs(defEffBonus - 1.0) > 0.001)
                        tooltip += "\n  Efficiency bonus: " + defEffBonus.ToString("0.0#") + "x";
                    if (Math.Abs(defAdv - 1.0) > 0.001)
                        tooltip += "\n  Defender advantage: " + defAdv.ToString("0.0#") + "x";
                }

                if (stats[i] == "happiness")
                {
                    Widgets.Label(buttonBox, new GUIContent(TexLoad.iconHappiness));
                    Widgets.Label(labelBox, settlement.happiness + "%");
                    tooltip = "SettlementHappiness".Translate() + "\n-----\n" + "SettlementHappinessDesc".Translate();

                    Widgets.DrawHighlight(statGainBox);
                    double happinessGain = Math.Round(settlement.GetTotalHappinessGain(),1);
                    TaggedString statGain = TextUtil.ColorizeAdditiveBonus(happinessGain);

                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Small;
                    Widgets.Label(statGainBox, statGain);
                    UIUtil.TipRegionByText(statGainBox, settlement.GetHappinessDesc());
                }

                if (stats[i] == "loyalty")
                {
                    Widgets.Label(buttonBox, new GUIContent(TexLoad.iconLoyalty));
                    Widgets.Label(labelBox, settlement.loyalty + "%");
                    tooltip = "SettlementLoyalty".Translate() + "\n-----\n" + "SettlementLoyaltyDesc".Translate();

                    Widgets.DrawHighlight(statGainBox);
                    double loyaltyGain = Math.Round(settlement.GetTotalLoyaltyGain(),1);
                    TaggedString statGain = TextUtil.ColorizeAdditiveBonus(loyaltyGain);

                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Small;
                    Widgets.Label(statGainBox, statGain);
                    UIUtil.TipRegionByText(statGainBox, settlement.GetLoyaltyDesc());
                }

                if (stats[i] == "unrest")
                {
                    Widgets.Label(buttonBox, new GUIContent(TexLoad.iconUnrest));
                    Widgets.Label(labelBox, settlement.unrest + "%");
                    tooltip = "SettlementUnrest".Translate() + "\n-----\n" + "SettlementUnrestDesc".Translate();

                    Widgets.DrawHighlight(statGainBox);
                    double unrestGain = Math.Round(settlement.GetTotalUnrestGain(),1);
                    TaggedString statGain = TextUtil.ColorizeAdditiveBonus(unrestGain, true);

                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Small;
                    Widgets.Label(statGainBox, statGain);
                    UIUtil.TipRegionByText(statGainBox, settlement.GetUnrestDesc());
                }

                if (stats[i] == "prosperity")
                {
                    Widgets.Label(buttonBox, new GUIContent(TexLoad.iconProsperity));
                    Widgets.Label(labelBox, settlement.prosperity + "%");
                    tooltip = "SettlementProsperity".Translate() + "\n-----\n" + "SettlementProsperityDesc".Translate();

                    Widgets.DrawHighlight(statGainBox);
                    double prosperityGain = Math.Round(settlement.GetProsperityGain(),1);
                    TaggedString statGain = TextUtil.ColorizeAdditiveBonus(prosperityGain);

                    Text.Anchor = TextAnchor.MiddleCenter;
                    Text.Font = GameFont.Small;
                    Widgets.Label(statGainBox, statGain);
                    UIUtil.TipRegionByText(statGainBox, settlement.GetProsperityDesc());
                }

                UIUtil.TipRegionByText(mainToolTipBox, tooltip);
            }
            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Medium;
        }

        private void DrawDescription(Rect boundingBox)
        {
            Widgets.DrawMenuSection(boundingBox);

            Rect textBox = new Rect(boundingBox.x + margin, boundingBox.y + margin, boundingBox.width - (margin * 2), boundingBox.height - (margin * 2));

            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Widgets.Label(textBox, settlement.description);
        }
        private void RemoveSettlement()
        {
            LogUtil.Message($"Removing settlement {settlement.Name}...");
            Find.WindowStack.TryRemove(this);
            ColonyUtil.RemovePlayerSettlement(settlement);
        }
        private void DrawMainButtons(Rect boundingBox)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            float size = (boundingBox.height - ((buttons.Count - 1) * margin)) / (buttons.Count);
            for (int i = 0; i < buttons.Count; i++)
            {
                Rect buttonRect = new Rect(boundingBox.x, boundingBox.y + ((size + margin) * i), boundingBox.width, size);
                string label = buttons[i];
                bool enabled = true;
                if (label == "UpgradeTown".Translate() && settlement.isUpgrading)
                {
                    label = "SettlementUpgradeInProgress".Translate();
                    GUI.color = Color.gray;
                    enabled = false;
                }
                if (Widgets.ButtonText(buttonRect, label, active: enabled))
                {
                    //If click a button button
                    if (label == "UpgradeTown".Translate())
                    {
                        //if click upgrade town button
                        Find.WindowStack.Add(new SettlementUpgradeWindowFc(settlement));
                    }

                    if (label == "DeleteSettlement".Translate())
                    {
                        Find.WindowStack.Add(new Dialog_Confirm("DeleteSettlementConfirm".Translate(settlement.Name), RemoveSettlement));
                    }

                    if (label == "FCSpecialActions".Translate())
                    {
                        List<FloatMenuOption> list = new List<FloatMenuOption>
                        {
                            //Add to all
                            new FloatMenuOption("GoToLocation".Translate(), delegate
                            {
                                Find.WindowStack.TryRemove(this);
                                settlement.GoTo();
                            })
                        };


                        factionfc.ForEachBehavior(b =>
                        {
                            var actions = b.GetSettlementActions(factionfc, settlement);
                            if (actions != null)
                                list.AddRange(actions);
                        });

                        if (list.Count == 0)
                            list.Add(new FloatMenuOption("FCNoSpecialActions".Translate(), delegate { }));
                        Find.WindowStack.Add(new FloatMenu(list));
                    }

                    if (label == "PrisonersMenu".Translate())
                    {
                        Find.WindowStack.Add(new FCPrisonerMenu(settlement));
                    }

                    if (label == "Military".Translate() && settlement.MilitaryComp != null)
                    {
                        bool canAutoDefend = settlement.MilitaryComp.IsMilitaryValid();
                        List<FloatMenuOption> list = new List<FloatMenuOption>
                        {
                            new FloatMenuOption(
                            canAutoDefend
                                ? "ToggleAutoDefend".Translate(settlement.MilitaryComp.autoDefend.ToString())
                                : "ToggleAutoDefend".Translate(false.ToString()) + " (" + "FCMilAutoDefendDisabled".Translate() + ")",
                            canAutoDefend
                                ? (Action)delegate
                                {
                                    settlement.MilitaryComp.autoDefend = !settlement.MilitaryComp.autoDefend;
                                }
                                : null)
                        };

                        if (settlement.MilitaryComp.isUnderAttack)
                        {
                            FCEvent evt = MilitaryUtilFC.ReturnMilitaryEventByLocation(settlement.Tile);

                            list.Add(new FloatMenuOption(
                                "SettlementDefendingInformation".Translate(
                                    evt.militaryForceDefending.homeSettlement.Name,
                                    evt.militaryForceDefending.DefensivePower), null, MenuOptionPriority.High));
                            list.Add(new FloatMenuOption("ChangeDefendingForce".Translate(), delegate
                            {
                                List<FloatMenuOption> settlementList = new List<FloatMenuOption>();
                                WorldSettlementFC homeSettlement = settlement;

                                double homePower = Math.Round(homeSettlement.settlementMilitaryLevel
                                    * homeSettlement.GetStatValue(FCStatDefOf.militaryCombatEfficiency)
                                    * FCSettings.defenderAdvantage);
                                settlementList.Add(new FloatMenuOption(
                                    "ResetToHomeSettlement".Translate(homePower),
                                    delegate { MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, homeSettlement); },
                                    MenuOptionPriority.High));

                                foreach (WorldSettlementFC settlement in FactionCache.FactionComp.settlements)
                                {
                                    if (settlement.MilitaryComp.IsMilitaryValid() && settlement != homeSettlement)
                                    {
                                        double power = Math.Round(settlement.settlementMilitaryLevel
                                            * settlement.GetStatValue(FCStatDefOf.militaryCombatEfficiency)
                                            * FCSettings.defenderAdvantage);
                                        settlementList.Add(new FloatMenuOption(
                                            settlement.Name + " " + "FCPower".Translate() + " " +
                                            power + " - " + "FCAvailable".Translate() +
                                            ": " + (!settlement.MilitaryComp.IsMilitaryBusySilent()).ToString(), delegate
                                            {
                                                if (settlement.MilitaryComp.IsMilitaryBusy())
                                                {
                                                    //military is busy
                                                }
                                                else
                                                {
                                                    MilitaryUtilFC.ChangeDefendingMilitaryForce(evt, settlement);
                                                }
                                            }
                                        ));
                                    }
                                }

                                if (settlementList.Count == 0)
                                {
                                    settlementList.Add(new FloatMenuOption("NoValidMilitaries".Translate(), null));
                                }

                                Find.WindowStack.Add(new Searchable_FloatMenu(settlementList) { vanishIfMouseDistant = true });
                            }));

                            Find.WindowStack.Add(new FloatMenu(list));
                        }
                        else
                        {
                            list.Add(new FloatMenuOption("SettlementNotBeingAttacked".Translate(), null));
                            Find.WindowStack.Add(new FloatMenu(list));
                        }
                    }
                }
                if (label == "SettlementUpgradeInProgress".Translate())
                {
                    GUI.color = Color.white;
                }
            }
        }
        private Vector2 scrollVectorBuildings = new Vector2();
        public void DrawFacilities(Rect boundingBox)
        {
            Widgets.DrawMenuSection(boundingBox);

            if (settlement.BuildingsComp == null)
            {
                // can't draw what doesn't exist
                return;
            }

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            float scrollMargin = ((settlement.BuildingsComp?.Buildings.Count ?? 0) > 8) ? scrollSpacing : 0;

            Rect labelHighlight = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 30);
            Rect labelTextBox = new Rect(labelHighlight.x + smallMargin, labelHighlight.y + smallMargin, labelHighlight.width - (smallMargin * 2), labelHighlight.height - (smallMargin * 2));
            Widgets.DrawHighlight(labelHighlight);
            Widgets.Label(labelTextBox, "Facilities".Translate());

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;

            int elementsPerRow = (int)((boundingBox.width - (buildingSpacingFromSide * 2)) / (buildingBoxSide + buildingSpacing));
            float buildingBoxHeight = boundingBox.height - (labelHighlight.height + margin);
            float totalHeight = Mathf.Ceil(((float)settlement.BuildingsComp.Buildings.Count / (float)elementsPerRow)) * (buildingBoxSide + buildingSpacing);

            int row;
            int column;

            Rect box = new Rect(0 + buildingSpacingFromSide, 0, buildingBoxSide, buildingBoxSide);
            Rect buildingIcon = new Rect(4 + box.x, 4 + box.y, buildingBoxSide - 8, buildingBoxSide - 8);

            Rect nBox;
            Rect nBuilding;

            Rect buildingBox = new Rect(boundingBox.x, labelHighlight.yMax + margin, boundingBox.width, buildingBoxHeight);

            Rect viewRect = new Rect(buildingBox.x, labelHighlight.yMax + margin, boundingBox.width - scrollMargin, totalHeight);

            Widgets.BeginScrollView(buildingBox, ref scrollVectorBuildings, viewRect);


            int i = 0;

            foreach (BuildingFC buildingfc in settlement.BuildingsComp.Buildings)
            {
                BuildingFCDef building = buildingfc.def;
                //Update Variables for List
                row = (int)Math.Floor(i / (double)elementsPerRow);
                column = i % elementsPerRow;

                nBox = new Rect(
                    new Vector2(box.x + buildingBox.x + ((box.width + buildingSpacing) * column),
                                box.y + viewRect.y + ((box.height + buildingSpacing) * row)),
                    box.size);
                nBuilding = new Rect(
                    new Vector2(buildingIcon.x + buildingBox.x + ((box.width + buildingSpacing) * column),
                                buildingIcon.y + viewRect.y + ((box.height + buildingSpacing) * row)),
                    buildingIcon.size);

                //Actual UI Code
                Widgets.DrawMenuSection(nBox);
                if (i < settlement.BuildingsComp.NumBuildingSlots)
                {
                    UIUtil.TipRegionByText(nBuilding, settlement.BuildingsComp.GetBuildingDescFull(building));
                    if (Widgets.ButtonImage(nBuilding, building.Icon))
                    {
                        Find.WindowStack.Add(new FCBuildingWindow(settlement, i));
                    }
                }
                else
                {
                    bool isCapLocked = settlement.BuildingsComp.NumBuildingSlots >= settlement.settlementDef.maxBuildingCount;
                    string lockTooltip = isCapLocked
                        ? "FCBuildingLockedMax".Translate()
                        : "FCBuildingLockedLevel".Translate(2 * (i - 2));
                    UIUtil.TipRegionByText(nBox, lockTooltip);
                    if (Widgets.ButtonImage(nBuilding, TexLoad.buildingLocked))
                    {
                        Messages.Message("FCBuildingLocked".Translate(), MessageTypeDefOf.RejectInput);
                    }
                }

                i++;
            }
            Widgets.EndScrollView();
        }
        private Vector2 scrollVectorConstruction = new Vector2();
        private void DrawConstructionBox(Rect boundingBox, int numConstruction, List<BuildingFC> construction)
        {
            Widgets.DrawMenuSection(boundingBox);

            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            /* Draw the construction header */
            Rect conHeader = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 30);
            Rect conHeaderText = new Rect(conHeader.x, conHeader.y + smallMargin, conHeader.width, conHeader.height - (smallMargin*2));
            Widgets.DrawHighlight(conHeader);
            Widgets.Label(conHeaderText, "ActiveConstruction".Translate());

            Text.Font = GameFont.Small;
            if (numConstruction > 0)
            {
                /* Scroll view time, baby */
                float listHeight = boundingBox.height - (conHeader.height + margin);
                float totalHeight = (constructionListItemHeight * numConstruction) + (margin * (numConstruction - 1));
                float scrollBarMargin = (totalHeight < listHeight) ? 0 : scrollSpacing;
                Rect listBox = new Rect(boundingBox.x, conHeader.yMax + margin, boundingBox.width, listHeight);
                Rect viewRect = new Rect(listBox.x, listBox.y, listBox.width - scrollBarMargin, totalHeight);

                Widgets.BeginScrollView(listBox, ref scrollVectorConstruction, viewRect);

                float initialY = viewRect.y;
                Text.Anchor = TextAnchor.MiddleLeft;

                if (settlement.isUpgrading)
                {
                    float progress = (float)(Find.TickManager.TicksGame - settlement.startUpgradeTick) / (float)(settlement.finishUpgradeTick - settlement.startUpgradeTick);
                    Rect upgradeRect = new Rect(viewRect.x + margin,
                                                viewRect.y,
                                                viewRect.width - (margin * 2),
                                                constructionListItemHeight);
                    DrawConstructionInfoBox(upgradeRect, null, "settlementupgrading".Translate(),
                                            "completiontimer".Translate((settlement.finishUpgradeTick - Find.TickManager.TicksGame).ToTimeString()),
                                            progress);

                    initialY = upgradeRect.yMax + margin;
                }

                for (int i = 0; i < construction.Count; i++)
                {
                    float progress = (float)(Find.TickManager.TicksGame - construction[i].startedTick) / (float)(construction[i].completionTick - construction[i].startedTick);
                    Rect upgradeRect = new Rect(viewRect.x + margin,
                                                initialY + (i * (constructionListItemHeight + margin)),
                                                viewRect.width - (margin * 2),
                                                constructionListItemHeight);
                    DrawConstructionInfoBox(upgradeRect, construction[i].underConstructionDef.Icon, construction[i].underConstructionDef.LabelCap,
                                            "completiontimer".Translate((construction[i].completionTick - Find.TickManager.TicksGame).ToTimeString()),
                                            progress);

                    UIUtil.TipRegionByText(upgradeRect, settlement.BuildingsComp?.GetBuildingDescFull(construction[i].underConstructionDef) ?? TaggedString.Empty);
                }

                Widgets.EndScrollView();
            }
        }
        private void DrawConstructionInfoBox(Rect boundingBox, Texture2D icon, string label, string time, float progress)
        {
            Text.Font = GameFont.Tiny;
            float elementHeight = (boundingBox.height - (smallMargin * 4f)) / 3f;
            float iconHeight = (boundingBox.height - (smallMargin * 3f)) * (2f/3f);
            float labelX = icon == null ? boundingBox.x : boundingBox.x + constructionListIconHeight + smallMargin;
            Rect iconBox = new Rect(boundingBox.x + smallMargin,
                                    boundingBox.y + smallMargin,
                                    constructionListIconHeight,
                                    constructionListIconHeight);
            Rect labelBox = new Rect(labelX + smallMargin*2,
                                     boundingBox.y + smallMargin,
                                     boundingBox.xMax - (labelX + smallMargin*3),
                                     constructionListItemLabelHeight);
            Rect labelHighlight = new Rect(labelX + smallMargin,
                                           boundingBox.y + smallMargin,
                                           boundingBox.xMax - (labelX + smallMargin * 2),
                                           constructionListItemLabelHeight);
            Rect timeBox = new Rect(labelBox.x,
                                    labelBox.yMax + smallMargin,
                                    labelBox.width,
                                    constructionListItemLabelHeight);
            Rect progressRect = new Rect(boundingBox.x + smallMargin,
                                         timeBox.yMax + smallMargin,
                                         boundingBox.width - (smallMargin * 2),
                                         constructionListProgressBarHeight);

            string nulabel = Text.ClampTextWithEllipsis(labelBox, label);

            Widgets.DrawMenuSection(boundingBox);
            if (icon != null)
            {
                Widgets.ButtonImage(iconBox, icon);
            }
            Widgets.DrawHighlight(labelHighlight);
            Widgets.Label(labelBox, nulabel);
            Widgets.Label(timeBox, time);
            UIUtil.DrawProgressBar(progressRect, progress);
        }

        private void DrawRightInfo(Rect boundingBox)
        {
            Color origColor = GUI.color;
            GUI.color = Color.gray;
            Widgets.DrawBox(boundingBox);
            GUI.color = origColor;
            Rect prodBox = new Rect(boundingBox.x + margin, boundingBox.y + margin, boundingBox.width - (margin*2), boundingBox.height - (margin*2));
            DrawProduction(prodBox);
        }
        public void DrawProduction(Rect boundingBox)
        {
            Rect header = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 30f);
            Rect costs = new Rect(boundingBox.x, header.yMax, boundingBox.width, 73f);
            Rect workers = new Rect(boundingBox.x, costs.yMax + margin, boundingBox.width, 69f);

            DrawProductionHeader(header);
            DrawCostBreakdown(costs);
            DrawWorkerBreakdown(workers);

            Rect prodOverview = new Rect(boundingBox.x, workers.yMax + margin, boundingBox.width, boundingBox.yMax - (workers.yMax + margin));
            DrawProductionOverview(prodOverview);
        }
        private void DrawProductionHeader(Rect boundingBox)
        {
            Text.Font = GameFont.Medium;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 30), "Production".Translate());
        }

        private void DrawCostBreakdown(Rect boundingBox)
        {
            Text.Font = GameFont.Small;
            float rowHeight = 20f;
            float labelHeight = rowHeight - (smallMargin * 2);
            float labelWidth = (boundingBox.width - margin) / 2f;

            Rect profitBox = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, 28f);
            Rect profitLabel = new Rect(profitBox.x, profitBox.y, labelWidth, profitBox.height);
            Rect profitNum = new Rect(profitLabel.xMax + margin, profitLabel.y, labelWidth, profitBox.height);
            Widgets.DrawHighlight(profitBox);
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(profitLabel, "Total".Translate() + " " + "Profit".Translate() + ":");
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(profitNum, new GUIContent(Math.Round(settlement.totalProfit).ToString(), ThingDefOf.Silver.uiIcon));

            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.LowerCenter;
            labelWidth = (boundingBox.width - (margin * 12f)) / 3f;
            Rect incomeBox = new Rect(boundingBox.x + (margin*3), profitBox.yMax + margin, labelWidth, rowHeight*2);
            Rect costsBox = new Rect(incomeBox.xMax + (margin*3), incomeBox.y, labelWidth, rowHeight*2);
            Rect taxBonusBox = new Rect(costsBox.xMax + (margin*3), incomeBox.y, labelWidth, rowHeight*2);

            Rect incomeLabel = new Rect(incomeBox.x, incomeBox.y, incomeBox.width, incomeBox.height/2f);
            Rect costLabel = new Rect(costsBox.x, costsBox.y, costsBox.width, costsBox.height/2f);
            Rect taxBonusLabel = new Rect(taxBonusBox.x, taxBonusBox.y, taxBonusBox.width, taxBonusBox.height/2f);

            Rect incomeNum = new Rect(incomeLabel.x, incomeLabel.yMax + smallMargin, incomeBox.width, incomeBox.height / 2f);
            Rect costsNum = new Rect(costLabel.x, costLabel.yMax + smallMargin, costsBox.width, costsBox.height / 2f);
            Rect taxBonusNum = new Rect(taxBonusLabel.x, taxBonusLabel.yMax + smallMargin, taxBonusBox.width, taxBonusBox.height / 2f);

            Widgets.DrawHighlight(incomeBox);
            Widgets.DrawHighlight(costsBox);
            Widgets.DrawHighlight(taxBonusBox);

            Widgets.Label(incomeLabel, "Total".Translate() + " " + "Income".Translate());
            Widgets.Label(costLabel, "FCUpkeep".Translate());
            Widgets.Label(taxBonusLabel, "TaxBase".Translate());

            Text.Anchor = TextAnchor.UpperCenter;
            Widgets.Label(incomeNum, Math.Round(settlement.totalIncome,2).ToString());
            Widgets.Label(costsNum, Math.Round(settlement.totalUpkeep,2).ToString());
            Widgets.Label(taxBonusNum, (settlement.GetSettlementTaxBonus() * 100d).ToString() + "%");

            UIUtil.TipRegionByText(incomeBox, settlement.incomeExp);
            UIUtil.TipRegionByText(costsBox, settlement.upkeepExp);
        }

        private void DrawWorkerBreakdown(Rect boundingBox)
        {
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleLeft;
            float rowHeight = 23f;
            float labelHeight = rowHeight - (smallMargin * 2);
            float labelWidth = (boundingBox.width - (margin * 2));

            Rect workerBox = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, rowHeight);
            Rect overMaxBox = new Rect(boundingBox.x, workerBox.yMax, boundingBox.width, rowHeight);
            Rect upkeepBox = new Rect(boundingBox.x, overMaxBox.yMax, boundingBox.width, rowHeight);

            Rect workerLabel = new Rect(workerBox.x + margin, workerBox.y + smallMargin, labelWidth * 0.75f, labelHeight);
            Rect overMaxLabel = new Rect(overMaxBox.x + margin, overMaxBox.y + smallMargin, labelWidth * 0.75f, labelHeight);
            Rect upkeepLabel = new Rect(upkeepBox.x + margin, upkeepBox.y + smallMargin, labelWidth * 0.75f, labelHeight);

            Rect workerNum = new Rect(workerLabel.xMax, workerLabel.y, labelWidth * 0.25f, labelHeight);
            Rect overMaxNum = new Rect(overMaxLabel.xMax, overMaxLabel.y, labelWidth * 0.25f, labelHeight);
            Rect upkeepNum = new Rect(upkeepLabel.xMax, upkeepLabel.y, labelWidth * 0.25f, labelHeight);

            Widgets.DrawHighlight(workerBox);
            UIUtil.TipRegionByText(overMaxBox, "AssignedOvermaxWorkersTooltip".Translate());
            Widgets.DrawHighlight(upkeepBox);

            Widgets.Label(workerLabel, "AssignedWorkers".Translate());
            Widgets.Label(overMaxLabel, "AssignedOvermaxWorkers".Translate());
            Widgets.Label(upkeepLabel, "CostPerWorker".Translate());

            Text.Anchor = TextAnchor.MiddleRight;
            int numWorkers = (int)Math.Min(settlement.workers, settlement.workersMax);
            int numOvermaxWorkers = (int)Math.Max(0, settlement.workers - settlement.workersMax);
            Widgets.Label(workerNum, "AssignedWorkersValue".Translate(numWorkers, settlement.workersMax));
            Widgets.Label(overMaxNum, "AssignedOvermaxWorkersValue".Translate(numOvermaxWorkers, settlement.workersUltraMax-settlement.workersMax));
            Widgets.Label(upkeepNum, settlement.workerCost.ToString());
        }

        private void DrawProductionOverview(Rect boundingBox)
        {
            Text.Anchor = TextAnchor.MiddleCenter;
            Text.Font = GameFont.Tiny;
            /* Header */
            float colWidth = (boundingBox.width - (margin*7)) / 8f;
            float headerHeight = 44;

            Rect workersBox = new Rect(boundingBox.x + colWidth + margin, boundingBox.y, colWidth, headerHeight);
            Rect prodHeaderBox = new Rect(workersBox.xMax + margin, boundingBox.y, colWidth * 3 + margin * 2, headerHeight / 2f);
            Rect prodBaseBox = new Rect(workersBox.xMax + margin, prodHeaderBox.yMax, colWidth, headerHeight / 2f);
            Rect prodMultBox = new Rect(prodBaseBox.xMax + margin, prodBaseBox.y, colWidth, prodBaseBox.height);
            Rect prodFinalBox = new Rect(prodMultBox.xMax + margin, prodMultBox.y, colWidth, prodMultBox.height);
            Rect prodTotalBox = new Rect(prodHeaderBox.xMax + margin, boundingBox.y, colWidth, headerHeight);

            Rect incomeBox = new Rect(prodTotalBox.xMax + margin, boundingBox.y, colWidth*2 + margin, headerHeight/2f);
            Rect incomeRawBox = new Rect(incomeBox.x, incomeBox.yMax, colWidth, headerHeight / 2f);
            Rect incomeNetBox = new Rect(incomeRawBox.xMax + margin, incomeRawBox.y, colWidth, headerHeight/2f);
            Widgets.DrawHighlight(workersBox);
            Widgets.Label(workersBox, "Workers".Translate());

            Widgets.DrawHighlight(prodHeaderBox);
            Widgets.Label(prodHeaderBox, "PerWorkerProduction".Translate());
            Widgets.DrawLineHorizontal(prodHeaderBox.x, prodHeaderBox.yMax, prodHeaderBox.width);
            Widgets.DrawHighlight(prodBaseBox);
            Widgets.Label(prodBaseBox, "Base".Translate());
            Widgets.DrawHighlight(prodMultBox);
            Widgets.Label(prodMultBox, "Mult".Translate());
            Widgets.DrawHighlight(prodFinalBox);
            Widgets.Label(prodFinalBox, "Final".Translate());

            Widgets.DrawHighlight(prodTotalBox);
            Widgets.Label(prodTotalBox, "Total".Translate());

            Widgets.DrawHighlight(incomeBox);
            Widgets.Label(incomeBox, "Income".Translate());
            Widgets.DrawLineHorizontal(incomeBox.x, incomeBox.yMax, incomeBox.width);
            Widgets.DrawHighlight(incomeRawBox);
            Widgets.Label(incomeRawBox, "Raw".Translate());
            UIUtil.TipRegionByText(incomeRawBox, "RawIncomeDesc".Translate());
            Widgets.DrawHighlight(incomeNetBox);
            Widgets.Label(incomeNetBox, "Net".Translate());
            UIUtil.TipRegionByText(incomeNetBox, "NetIncomeDesc".Translate());

            Rect resourceArea = new Rect(boundingBox.x, workersBox.yMax + margin, boundingBox.width, boundingBox.yMax - (workersBox.yMax + margin));
            DrawResources(resourceArea, colWidth);
        }
        private Vector2 scrollVectorResources = new Vector2();
        private void DrawResources(Rect boundingBox, float colWidth)
        {
            float rowHeight = 25f;
            List<ResourceFC> availableResources = settlement.Resources;
            float totalHeight = (availableResources.Count * rowHeight) + (availableResources.Count * margin);
            Rect viewRect = new Rect(boundingBox.x, boundingBox.y, boundingBox.width, totalHeight);
            Widgets.BeginScrollView(boundingBox, ref scrollVectorResources, viewRect, false);
            // Get the appropriate resource types based on settlement type

            Rect totalProdCol = new Rect(viewRect.x + (5f * (colWidth + margin)), viewRect.y, colWidth, viewRect.height - (margin / 2f));
            Rect incomeRawCol = new Rect(viewRect.x + (6f * (colWidth + margin)), viewRect.y, colWidth, viewRect.height - (margin / 2f));
            Rect incomeNetCol = new Rect(viewRect.x + (7f * (colWidth + margin)), viewRect.y, colWidth, viewRect.height - (margin / 2f));
            Widgets.DrawHighlight(totalProdCol);
            Widgets.DrawHighlight(incomeRawCol);
            Widgets.DrawMenuSection(incomeNetCol);
            UIUtil.TipRegionByText(incomeRawCol, "RawIncomeDesc".Translate());

            for (int i = 0; i < availableResources.Count; i++)
            {
                ResourceFC resource = availableResources[i];
                if (resource == null) continue;

                float rectY = viewRect.y + (i * (rowHeight + margin));
                /* Alternating highlights, to make rows easier to read/track */
                if (i % 2 == 0)
                {
                    Rect rowHighlight = new Rect(viewRect.x, rectY - (margin / 2f), viewRect.width, rowHeight + margin);
                    Widgets.DrawHighlight(rowHighlight);
                }

                // Resource color accent
                Widgets.DrawBoxSolid(new Rect(viewRect.x, rectY, 3f, rowHeight), resource.def.color);

                float resourceImgSize = Math.Min(colWidth, rowHeight);
                float resourceImxgX = viewRect.x + ((colWidth - resourceImgSize) / 2f);
                Rect resourceImgRect = new Rect(resourceImxgX, rectY, resourceImgSize, resourceImgSize);
                Widgets.ButtonImage(resourceImgRect, resource.def.Icon);
                UIUtil.TipRegionByText(resourceImgRect, resource.def.LabelCap);

                //Production Efficiency
                float arrowButtonHeight = Math.Min(rowHeight, 20f);
                float arrowButtonY = rectY + ((rowHeight - arrowButtonHeight) / 2f);
                Rect workersDecArrow = new Rect(viewRect.x + colWidth + margin, arrowButtonY, colWidth / 3f, arrowButtonHeight);
                Rect workersNum = new Rect(workersDecArrow.xMax, rectY, workersDecArrow.width, rowHeight);
                Rect workersIncArrow = new Rect(workersNum.xMax, arrowButtonY, workersDecArrow.width, arrowButtonHeight);
                Widgets.Label(workersNum, resource.assignedWorkers.ToString());
                if (Widgets.ButtonText(workersDecArrow, "<")) IncreaseWorkers(resource, true);
                if (Widgets.ButtonText(workersIncArrow, ">")) IncreaseWorkers(resource);

                //Base Production
                Rect baseProd = new Rect(workersIncArrow.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(baseProd, TextUtil.FloorStat(resource.productionBase));
                UIUtil.TipRegionByText(baseProd, resource.GetProductionAdditivesDesc());

                //Modifier
                Rect multProd = new Rect(baseProd.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(multProd, TextUtil.FloorStat(resource.productionMult));
                UIUtil.TipRegionByText(multProd, resource.GetProductionMultipliersDesc());

                //Final Base
                Rect finalProd = new Rect(multProd.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(finalProd, (TextUtil.FloorStat(resource.production)));

                //Total Production
                Rect totalProd = new Rect(finalProd.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(totalProd, (TextUtil.FloorStat(resource.rawTotalProduction)));
                if (resource.AccumulationDays > 0)
                {
                    int totalPeriodDays = FCSettings.timeBetweenTaxes / GenDate.TicksPerDay;
                    string tooltip = "FCTotalProdTooltip".Translate(
                        TextUtil.FloorStat(resource.InstantaneousProduction),
                        TextUtil.FloorStat(resource.AccumulatedAverageProduction),
                        resource.AccumulationDays.ToString(),
                        totalPeriodDays.ToString());
                    TooltipHandler.TipRegion(totalProd, tooltip);
                }

                //Raw Income (total production as silver, before stockpile diversions and tithes)
                Rect incomeRawBox = new Rect(totalProd.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(incomeRawBox, (TextUtil.FloorStat(resource.grossMarketValue)));

                //Net Income, after stockpile diversions and tithes
                Rect incomeNetBox = new Rect(incomeRawBox.xMax + margin, rectY, colWidth, rowHeight);
                Widgets.Label(incomeNetBox, (TextUtil.FloorStat(resource.actualIncome)));

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("NetIncomeBreakdownGross".Translate(TextUtil.FloorStat(resource.grossMarketValue)));
                if (resource.stockpileMarketValue > 0)
                    sb.AppendLine("NetIncomeBreakdownStockpile".Translate(TextUtil.FloorStat(resource.stockpileMarketValue)));
                if (resource.titheTotalValue > 0)
                    sb.AppendLine("NetIncomeBreakdownTithes".Translate(TextUtil.FloorStat(resource.titheTotalValue)));
                sb.Append("NetIncomeBreakdownNet".Translate(TextUtil.FloorStat(resource.actualIncome)));
                UIUtil.TipRegionByText(incomeNetBox, sb.ToString());
            }

            Widgets.EndScrollView();
        }

        /// <summary>
        /// Increases the amount of workers in a settlement. Decreases if <paramref name="negative"/> is true. Modifies the amount based on if shift/ctrl are held
        /// </summary>
        /// <param name="resourceType"></param>
        /// <param name="negative"></param>
        private void IncreaseWorkers(ResourceFC resource, bool negative = false)
        {
            if (settlement.MilitaryComp?.isUnderAttack == true)
            {
                Messages.Message("SettlementUnderAttack".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }
            //if clicked to lower amount of workers
            settlement.IncreaseWorkers(resource, (negative ? -1 : 1) * Modifiers.GetModifier);
        }
    }
}