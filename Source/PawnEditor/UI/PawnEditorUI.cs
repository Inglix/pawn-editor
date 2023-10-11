﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace PawnEditor;

[HotSwappable]
public static partial class PawnEditor
{
    public static bool RenderClothes = true;
    public static bool RenderHeadgear = true;
    private static bool usePointLimit;
    private static float remainingPoints;
    private static Faction selectedFaction;
    private static Pawn selectedPawn;
    private static bool showFactionInfo;
    private static PawnCategory selectedCategory;
    private static float cachedValue;
    private static FloatMenuOption lastRandomization;
    private static TabGroupDef tabGroup;
    private static List<TabRecord> tabs;
    private static TabDef curTab;
    private static List<WidgetDef> widgets;
    private static int startingSilver;

    private static readonly TabDef widgetTab = new()
    {
        defName = "Widgets",
        label = "MiscRecordsCategory".Translate()
    };

    public static PawnLister PawnList = new();
    public static PawnListerBase AllPawns = new();

    private static Rot4 curRot = Rot4.South;

    public static bool Pregame;

    private static TabRecord cachedWidgetTab;

    public static void DoUI(Rect inRect, Action onClose, Action onNext)
    {
        var headerRect = inRect.TakeTopPart(50f);
        headerRect.xMax -= 10f;
        headerRect.yMax -= 20f;
        using (new TextBlock(GameFont.Medium))
            Widgets.Label(headerRect, $"{(Pregame ? "Create" : "PawnEditor.Edit")}Characters".Translate());

        if (ModsConfig.IdeologyActive)
        {
            Text.Font = GameFont.Small;
            string text = "ShowHeadgear".Translate();
            string text2 = "ShowApparel".Translate();
            var width = Mathf.Max(Text.CalcSize(text).x, Text.CalcSize(text2).x) + 4f + 24f;
            var rect2 = headerRect.TakeRightPart(width).TopPartPixels(Text.LineHeight * 2f);
            Widgets.CheckboxLabeled(rect2.TopHalf(), text, ref RenderHeadgear);
            Widgets.CheckboxLabeled(rect2.BottomHalf(), text2, ref RenderClothes);
            headerRect.xMax -= 4f;
        }

        string text3 = "PawnEditor.UsePointLimit".Translate();
        string text4 = "PawnEditor.PointsRemaining".Translate();
        var text5 = remainingPoints.ToStringMoney();
        var num = Text.CalcSize(text4).x;
        var width2 = Mathf.Max(Text.CalcSize(text3).x, num) + 4f + Mathf.Max(Text.CalcSize(text3).x, 24f);
        var rect3 = headerRect.TakeRightPart(width2).TopPartPixels(Text.LineHeight * 2f);
        UIUtility.CheckboxLabeledCentered(rect3.TopHalf(), text3, ref usePointLimit);
        rect3 = rect3.BottomHalf();
        Widgets.Label(rect3.TakeLeftPart(num), text4);
        using (new TextBlock(TextAnchor.MiddleCenter)) Widgets.Label(rect3, text5.Colorize(ColoredText.CurrencyColor));

        DoBottomButtons(inRect.TakeBottomPart(Page.BottomButHeight), onClose, Pregame
            ? onNext
            : () =>
            {
                if (!showFactionInfo && selectedPawn != null)
                    Find.WindowStack.Add(new FloatMenu(Find.Maps.Select(map => PawnList.GetTeleportOption(map, selectedPawn))
                       .Concat(Find.WorldObjects.Caravans.Select(caravan => PawnList.GetTeleportOption(caravan, selectedPawn)))
                       .Append(PawnList.GetTeleportOption(Find.World, selectedPawn))
                       .Append(new("PawnEditor.Teleport.Specific".Translate(), delegate
                        {
                            onClose();
                            DebugTools.curTool = new("PawnEditor.Teleport".Translate(), () =>
                            {
                                var cell = UI.MouseCell();
                                var map = Find.CurrentMap;
                                if (!cell.Standable(map) || cell.Fogged(map)) return;
                                PawnList.TeleportFromTo(selectedPawn, PawnList.GetLocation(selectedPawn), map);
                                selectedPawn.Position = cell;
                                selectedPawn.Notify_Teleported();
                                DebugTools.curTool = null;
                            });
                        }))
                       .ToList()));
            });

        inRect.yMin -= 10f;
        DoLeftPanel(inRect.TakeLeftPart(134), Pregame);
        inRect.xMin += 12f;
        inRect = inRect.ContractedBy(6);
        inRect.TakeTopPart(40);
        Widgets.DrawMenuSection(inRect);
        if (!tabs.NullOrEmpty()) TabDrawer.DrawTabs(inRect, tabs, 1);
        inRect = inRect.ContractedBy(6);
        if (curTab != null)
        {
            if (curTab == widgetTab)
                DoWidgets(inRect);
            else if (showFactionInfo)
                curTab.DrawTabContents(inRect, selectedFaction);
            else
                curTab.DrawTabContents(inRect, selectedPawn);
        }
    }

    public static void DoBottomButtons(Rect inRect, Action onLeftButton, Action onRightButton)
    {
        Text.Font = GameFont.Small;
        if (Widgets.ButtonText(inRect.TakeLeftPart(Page.BottomButSize.x), Pregame ? "Back".Translate() : "Close".Translate())) onLeftButton();

        if (Widgets.ButtonText(inRect.TakeRightPart(Page.BottomButSize.x), Pregame ? "Start".Translate() : "PawnEditor.Teleport".Translate())) onRightButton();

        var randomRect = new Rect(Vector2.zero, Page.BottomButSize).CenteredOnXIn(inRect).CenteredOnYIn(inRect);

        var buttonRect = new Rect(randomRect);

        if (lastRandomization != null && Widgets.ButtonImageWithBG(randomRect.TakeRightPart(20), TexUI.RotRightTex, new Vector2(12, 12)))
            lastRandomization.action();

        randomRect.TakeRightPart(1);

        if (Widgets.ButtonText(randomRect, "Randomize".Translate())) Find.WindowStack.Add(new FloatMenu(GetRandomizationOptions().ToList()));

        buttonRect.x -= 5 + buttonRect.width;

        if (Widgets.ButtonText(buttonRect, "Save".Translate()))
            Find.WindowStack.Add(new FloatMenu(GetSaveLoadItems().Select(item => item.MakeSaveOption()).ToList()));

        buttonRect.x += buttonRect.width * 2 + 10;

        if (Widgets.ButtonText(buttonRect, "Load".Translate()))
            Find.WindowStack.Add(new FloatMenu(GetSaveLoadItems().Select(item => item.MakeLoadOption()).ToList()));
    }

    private static IEnumerable<SaveLoadItem> GetSaveLoadItems()
    {
        if (showFactionInfo)
            yield return new SaveLoadItem<Faction>("PawnEditor.Selected".Translate(), selectedFaction, new()
            {
                LoadLabel = "PawnEditor.LoadFaction".Translate()
            });
        else
            yield return new SaveLoadItem<Pawn>("PawnEditor.Selected".Translate(), selectedPawn, new()
            {
                LoadLabel = "PawnEditor.LoadPawn".Translate(),
                TypePostfix = selectedCategory.ToString()
            });

        if (Pregame)
            yield return new SaveLoadItem<StartingThingsManager.StartingPreset>("PawnEditor.Selection".Translate(), new());
        else
            yield return new SaveLoadItem<Map>("PawnEditor.Colony".Translate(), Find.CurrentMap, new()
            {
                PrepareLoad = map =>
                {
                    MapDeiniter.DoQueuedPowerTasks(map);
                    map.weatherManager.EndAllSustainers();
                    Find.SoundRoot.sustainerManager.EndAllInMap(map);
                    Find.TickManager.RemoveAllFromMap(map);
                },
                OnLoad = map => map.FinalizeLoading()
            });

        if (curTab != null)
            if (showFactionInfo)
                foreach (var item in curTab.GetSaveLoadItems(selectedFaction))
                    yield return item;
            else
                foreach (var item in curTab.GetSaveLoadItems(selectedPawn))
                    yield return item;
    }

    private static IEnumerable<FloatMenuOption> GetRandomizationOptions()
    {
        if (curTab == null) return Enumerable.Empty<FloatMenuOption>();
        return (showFactionInfo ? curTab.GetRandomizationOptions(selectedFaction) : curTab.GetRandomizationOptions(selectedPawn))
           .Select(option => new FloatMenuOption("PawnEditor.Randomize".Translate() + " " + option.Label.ToLower(), () =>
            {
                lastRandomization = option;
                option.action();
                Notify_PointsUsed();
            }));
    }

    public static void RecachePawnList()
    {
        if (selectedFaction == null || !Find.FactionManager.allFactions.Contains(selectedFaction)) selectedFaction = Faction.OfPlayer;
        if (selectedPawn is { Faction: { } pawnFaction } && pawnFaction != selectedFaction) selectedFaction = pawnFaction;
        TabWorker_FactionOverview.RecachePawns(selectedFaction);
        List<Pawn> pawns;
        if (Pregame)
            pawns = selectedCategory == PawnCategory.Humans ? Find.GameInitData.startingAndOptionalPawns : StartingThingsManager.GetPawns(selectedCategory);
        else
        {
            PawnList.UpdateCache(selectedFaction, selectedCategory);
            (pawns, _, _) = PawnList.GetLists();
        }

        if (selectedPawn == null || !pawns.Contains(selectedPawn))
        {
            selectedPawn = pawns.FirstOrDefault();
            CheckChangeTabGroup();
        }

        PortraitsCache.Clear();
    }

    public static void ResetPoints()
    {
        remainingPoints = PawnEditorMod.Settings.PointLimit;
        cachedValue = 0;
        if (!Pregame && PawnEditorMod.Settings.UseSilver)
        {
            startingSilver = ColonyInventory.AllItemsInInventory().Sum(static t => t.def == ThingDefOf.Silver ? t.stackCount : 0);
            remainingPoints = startingSilver;
        }

        Notify_PointsUsed();
    }

    public static void ApplyPoints()
    {
        var amount = remainingPoints - startingSilver;
        if (amount > 0)
        {
            var pos = ColonyInventory.AllItemsInInventory().FirstOrDefault(static t => t.def == ThingDefOf.Silver).Position;
            var silver = ThingMaker.MakeThing(ThingDefOf.Silver);
            silver.stackCount = Mathf.RoundToInt(amount);
            GenPlace.TryPlaceThing(silver, pos, Find.CurrentMap, ThingPlaceMode.Near);
        }
        else if (amount < 0)
        {
            amount = -amount;
            foreach (var thing in ColonyInventory.AllItemsInInventory().Where(static t => t.def == ThingDefOf.Silver))
            {
                var toRemove = Math.Min(thing.stackCount, Mathf.RoundToInt(amount));
                thing.stackCount -= toRemove;
                amount -= toRemove;

                if (thing.stackCount <= 0) thing.Destroy();
                if (amount < 1f) break;
            }
        }
    }

    public static bool CanUsePoints(float amount)
    {
        if (!usePointLimit) return true;
        if (remainingPoints >= amount) return true;
        Messages.Message("PawnEditor.NotEnoughPoints".Translate(amount), MessageTypeDefOf.RejectInput, false);
        return false;
    }

    public static bool CanUsePoints(Thing thing) => CanUsePoints(GetThingValue(thing));
    public static bool CanUsePoints(Pawn pawn) => CanUsePoints(GetPawnValue(pawn));

    public static void Notify_PointsUsed(float? amount = null)
    {
        if (amount.HasValue)
            remainingPoints -= amount.Value;
        else
        {
            var value = 0f;
            if (Pregame)
            {
                value += ValueOfPawns(Find.GameInitData.startingAndOptionalPawns);
                value += ValueOfPawns(StartingThingsManager.GetPawns(PawnCategory.Animals));
                value += ValueOfPawns(StartingThingsManager.GetPawns(PawnCategory.Mechs));
                value += ValueOfThings(StartingThingsManager.GetStartingThingsNear());
                value += ValueOfThings(StartingThingsManager.GetStartingThingsFar());
            }
            else
            {
                AllPawns.UpdateCache(null, PawnCategory.All);
                value += ValueOfPawns(AllPawns.GetList());
                value += ValueOfThings(ColonyInventory.AllItemsInInventory());
            }


            remainingPoints -= value - cachedValue;
            cachedValue = value;
        }
    }

    private static float ValueOfPawns(IEnumerable<Pawn> pawns) => pawns.Sum(GetPawnValue);
    private static float ValueOfThings(IEnumerable<Thing> things) => things.Sum(GetThingValue);
    private static float GetThingValue(Thing thing) => thing.MarketValue * thing.stackCount;

    private static float GetPawnValue(Pawn pawn)
    {
        var num = pawn.MarketValue;
        if (pawn.apparel != null)
            num += pawn.apparel.WornApparel.Sum(t => t.MarketValue);
        if (pawn.equipment != null)
            num += pawn.equipment.AllEquipmentListForReading.Sum(t => t.MarketValue);
        return num;
    }

    private static void SetTabGroup(TabGroupDef def)
    {
        tabGroup = def;
        curTab = def?.tabs?.FirstOrDefault();
        tabs = def?.tabs?.Select(static tab => new TabRecord(tab.LabelCap, () => curTab = tab, () => curTab == tab)).ToList() ?? new List<TabRecord>();
    }

    public static void CheckChangeTabGroup()
    {
        TabGroupDef desiredTabGroup;

        if (showFactionInfo && selectedFaction != null) desiredTabGroup = selectedFaction.IsPlayer ? TabGroupDefOf.PlayerFaction : TabGroupDefOf.NPCFaction;
        else if (selectedPawn != null) desiredTabGroup = selectedCategory == PawnCategory.Humans ? TabGroupDefOf.Humanlike : TabGroupDefOf.AnimalMech;
        else desiredTabGroup = null;

        if (desiredTabGroup != tabGroup) SetTabGroup(desiredTabGroup);
        RecacheWidgets();
    }

    private static void RecacheWidgets()
    {
        if (cachedWidgetTab != null) tabs.Remove(cachedWidgetTab);

        Func<WidgetDef, bool> predicate;
        if (showFactionInfo && selectedFaction != null) predicate = def => def.type == TabDef.TabType.Faction && def.ShowOn(selectedFaction);
        else if (selectedPawn != null) predicate = def => def.type == TabDef.TabType.Pawn && def.ShowOn(selectedPawn);
        else predicate = _ => false;

        widgets = DefDatabase<WidgetDef>.AllDefs.Where(predicate).ToList();

        if (widgets.NullOrEmpty())
            cachedWidgetTab = null;
        else
        {
            cachedWidgetTab = new(widgetTab.LabelCap, static () => curTab = widgetTab, static () => curTab == widgetTab);
            tabs.Add(cachedWidgetTab);
        }
    }

    public static void Select(Pawn pawn)
    {
        selectedPawn = pawn;
        selectedFaction = pawn.Faction;
        showFactionInfo = false;
        if (!selectedCategory.Includes(pawn))
        {
            selectedCategory = pawn.RaceProps.Humanlike ? PawnCategory.Humans : pawn.RaceProps.IsMechanoid ? PawnCategory.Mechs : PawnCategory.Animals;
            RecachePawnList();
        }

        CheckChangeTabGroup();
    }

    public static void Select(Faction faction)
    {
        selectedFaction = faction;
        selectedPawn = null;
        showFactionInfo = true;
        CheckChangeTabGroup();
    }

    public static void GotoTab(TabDef tab)
    {
        curTab = tab;
    }

    public static RenderTexture GetPawnTex(Pawn pawn, Vector2 portraitSize, Rot4 dir, Vector3 cameraOffset = default, float cameraZoom = 1f) =>
        PortraitsCache.Get(pawn, portraitSize, dir, cameraOffset, cameraZoom / pawn.BodySize,
            renderHeadgear: RenderHeadgear, renderClothes: RenderClothes, stylingStation: true);

    public static void SavePawnTex(Pawn pawn, string path)
    {
        var tex = GetPawnTex(pawn, new(128, 128), Rot4.South);
        RenderTexture.active = tex;
        var tex2D = new Texture2D(tex.width, tex.width);
        tex2D.ReadPixels(new(0, 0, tex.width, tex.height), 0, 0);
        RenderTexture.active = null;
        tex2D.Apply(true, false);
        var bytes = tex2D.EncodeToPNG();
        File.WriteAllBytes(path, bytes);
    }

    public static void DrawPawnPortrait(Rect rect)
    {
        var image = GetPawnTex(selectedPawn, rect.size, curRot);
        GUI.color = Command.LowLightBgColor;
        Widgets.DrawBox(rect);
        GUI.color = Color.white;
        GUI.DrawTexture(rect, Command.BGTex);
        GUI.DrawTexture(rect, image);
        if (Widgets.ButtonImage(rect.ContractedBy(8).RightPartPixels(16).TopPartPixels(16), TexUI.RotRightTex))
            curRot.Rotate(RotationDirection.Counterclockwise);

        if (Widgets.InfoCardButtonWorker(rect.ContractedBy(8).LeftPartPixels(16).TopPartPixels(16))) Find.WindowStack.Add(new Dialog_InfoCard(selectedPawn));
    }
}
