using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;

namespace SpireStrategist.Mod;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    public static void Initialize()
    {
        try
        {
            new Harmony("local.SpireStrategist").PatchAll(typeof(Entry).Assembly);
            GD.Print("[SpireStrategist] 0.6.22 loaded: conditional card mechanisms and opt-in continuous climbs; starts in advice mode.");
            if(NGame.Instance!=null){ClimbController.Initialize(NGame.Instance);AutopilotLab.Postfix(NGame.Instance);}
        }
        catch (Exception ex) { GD.PrintErr("[SpireStrategist] Initialization failed: " + ex); }
    }

    internal static void Safe(Action action)
    {
        try { action(); }
        catch (Exception ex) { GD.PrintErr("[SpireStrategist] " + ex); }
    }
}

[HarmonyPatch(typeof(NRun), nameof(NRun._Ready))]
internal static class RunReadyPatch
{
    static void Postfix(NRun __instance, RunState ____state) =>
        Entry.Safe(() => AdvisorService.Attach(__instance, ____state));
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
internal static class CardOptionsPatch
{
    static void Postfix(NCardRewardSelectionScreen __instance, IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> extraOptions) =>
        AdvisorService.Current?.Guard(() => AdvisorService.Current?.Cards(__instance, options, extraOptions));
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), "SelectCard")]
internal static class CardChoicePatch
{
    static void Prefix(NCardHolder cardHolder, out ChoiceRecord? __state)
    {
        ChoiceRecord? captured = null;
        AdvisorService.Current?.Guard(() => captured = AdvisorService.Current?.PrepareCardChoice(cardHolder.CardModel));
        __state = captured;
    }
    static void Postfix(ChoiceRecord? __state) => AdvisorService.Current?.Guard(() => AdvisorService.Current?.CommitChoice(__state));
}

[HarmonyPatch(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected")]
internal static class AlternativeChoicePatch
{
    static void Prefix(int index, out ChoiceRecord? __state)
    {
        ChoiceRecord? captured = null;
        AdvisorService.Current?.Guard(() => captured = AdvisorService.Current?.PrepareChoice("alternative:" + index));
        __state = captured;
    }
    static void Postfix(ChoiceRecord? __state) => AdvisorService.Current?.Guard(() => AdvisorService.Current?.CommitChoice(__state));
}

[HarmonyPatch(typeof(NRestSiteRoom), "UpdateRestSiteOptions")]
internal static class RestReadyPatch
{
    static void Postfix(NRestSiteRoom __instance) => AdvisorService.Current?.Guard(() => AdvisorService.Current?.Rest(__instance));
}

[HarmonyPatch(typeof(NRestSiteRoom), "OnBeforePlayerSelectedRestSiteOption")]
internal static class RestStartPatch
{
    static void Postfix() => AdvisorService.Current?.Guard(() => AdvisorService.Current?.RestStarted());
}

[HarmonyPatch(typeof(NRestSiteRoom), "OnAfterPlayerSelectedRestSiteOption")]
internal static class RestFinishedPatch
{
    static void Postfix(RestSiteOption option, bool success) =>
        AdvisorService.Current?.Guard(() => AdvisorService.Current?.RestFinished(option, success));
}

[HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom.EnableOptions))]
internal static class RestEnabledPatch
{
    static void Postfix() => AdvisorService.Current?.Guard(() => AdvisorService.Current?.RestEnabled());
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Factories.RelicFactory),nameof(MegaCrit.Sts2.Core.Factories.RelicFactory.PullNextRelicFromFront),
    new[]{typeof(MegaCrit.Sts2.Core.Entities.Players.Player)})]
internal static class DigRelicSelectionPatch
{
    static void Postfix(MegaCrit.Sts2.Core.Entities.Players.Player player,RelicModel __result) =>
        AdvisorService.Current?.Guard(()=>AdvisorService.Current.Autopilot.RestRelicGenerated(player,__result));
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.FinalizeUpgradeInternal))]
internal static class UpgradeRecordPatch
{
    static void Postfix(CardModel __instance) => AdvisorService.Current?.Guard(() => AdvisorService.Current?.CardUpgraded(__instance));
}
