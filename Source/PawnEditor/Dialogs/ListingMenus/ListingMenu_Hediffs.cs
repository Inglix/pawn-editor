﻿using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace PawnEditor;

[StaticConstructorOnStartup]
// ToDo: Separate list of mechs/ animals
public class ListingMenu_Hediffs : ListingMenu<HediffDef>
{
    private static readonly List<HediffDef> items;
    private static readonly Func<HediffDef, string> labelGetter = d => d.LabelCap;
    private static readonly Func<HediffDef, string> descGetter = d => d.Description;
    private static readonly Action<HediffDef, Pawn> action = TryAdd;
    private static readonly List<TFilter<HediffDef>> filters;

    private static readonly HashSet<TechLevel> possibleTechLevels;
    private static readonly Dictionary<HediffDef, (List<BodyPartDef>, List<BodyPartGroupDef>)> defaultBodyParts;

    static ListingMenu_Hediffs()
    {
        defaultBodyParts = DefDatabase<RecipeDef>.AllDefs.Where(recipe => recipe.addsHediff != null)
           .ToDictionary(recipe => recipe.addsHediff, recipe =>
                (recipe.appliedOnFixedBodyParts, recipe.appliedOnFixedBodyPartGroups));

        possibleTechLevels = new();
        foreach (var hediff in DefDatabase<HediffDef>.AllDefsListForReading)
            if (hediff.spawnThingOnRemoved is { techLevel: var level })
                possibleTechLevels.Add(level);

        items = DefDatabase<HediffDef>.AllDefsListForReading;
        filters = GetFilters();
    }

    public ListingMenu_Hediffs(Pawn pawn) : base(items, labelGetter, b => action(b, pawn),
        "ChooseStuffForRelic".Translate() + " " + "PawnEditor.Hediff".Translate().ToLower(),
        b => descGetter(b), null, filters, pawn) { }

    private static void TryAdd(HediffDef hediffDef, Pawn pawn)
    {
        void AddCheck(BodyPartRecord part)
        {
            void ReallyAdd()
            {
                var price = hediffDef.priceOffset;
                if (price == 0 && hediffDef.priceImpact && hediffDef.spawnThingOnRemoved != null) price = hediffDef.spawnThingOnRemoved.BaseMarketValue;
                if (price is >= 1 or <= 1 && hediffDef.priceImpact && !PawnEditor.CanUsePoints(price)) return;
                pawn.health.AddHediff(hediffDef, part);
                PawnEditor.Notify_PointsUsed();
            }

            if (typeof(Hediff_AddedPart).IsAssignableFrom(hediffDef.hediffClass)
             && pawn.health.hediffSet.GetFirstHediffMatchingPart<Hediff_AddedPart>(part) is { } hediff)
            {
                Find.WindowStack.Add(
                    Dialog_MessageBox.CreateConfirmation("PawnEditor.HediffConflict".Translate(hediffDef.LabelCap, hediff.LabelCap), ReallyAdd));
                return;
            }

            if (!typeof(Hediff_Injury).IsAssignableFrom(hediffDef.hediffClass))
            {
                var existing = new List<Hediff>();
                pawn.health.hediffSet.GetHediffs(ref existing, h => h.def == hediffDef && h.Part == part);
                if (existing.Count > 0)
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("PawnEditor.HediffDuplicate".Translate(hediffDef.LabelCap), ReallyAdd));
                    return;
                }
            }

            ReallyAdd();
        }

        if (defaultBodyParts.TryGetValue(hediffDef, out var result))
        {
            if (result.Item1?.Select(part => pawn.RaceProps.body.GetPartsWithDef(part)?.FirstOrDefault()).FirstOrDefault() is { } part1)
                AddCheck(part1);
            else if (result.Item2?.Select(group => pawn.RaceProps.body.AllParts.FirstOrDefault(part => part.IsInGroup(group))).FirstOrDefault() is { } part2)
                AddCheck(part2);
            else
                AddCheck(null);
        }
        else
            AddCheck(null);
    }

    private static List<TFilter<HediffDef>> GetFilters()
    {
        var list = new List<TFilter<HediffDef>>();

        list.Add(new("PawnEditor.Prosthetic".Translate(), false, def => typeof(Hediff_AddedPart).IsAssignableFrom(def.hediffClass)));
        list.Add(new("PawnEditor.IsImplant".Translate(), false, def => typeof(Hediff_Implant).IsAssignableFrom(def.hediffClass) && !typeof(Hediff_AddedPart)
           .IsAssignableFrom(def.hediffClass)));
        list.Add(new("PawnEditor.IsInjury".Translate(), false, def => typeof(Hediff_Injury).IsAssignableFrom(def.hediffClass)));
        list.Add(new("PawnEditor.IsDisease".Translate(), false, def => def.makesSickThought));
        var techLevel = possibleTechLevels.ToDictionary<TechLevel, FloatMenuOption, Func<HediffDef, bool>>(
            level => new(level.ToStringHuman().CapitalizeFirst(), () => { }),
            level => hediff => hediff.spawnThingOnRemoved?.techLevel == level);
        list.Add(new("PawnEditor.TechLevel".Translate(), false, techLevel));
        return list;
    }
}
