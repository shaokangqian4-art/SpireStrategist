using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Entities.RestSite;

namespace SpireStrategist.Mod;

internal static class GameActions
{
    internal static object? Field(object target, string field) => AccessTools.Field(target.GetType(), field)?.GetValue(target);
    internal static object? Invoke(object target, string method, params object?[] args) =>
        (AccessTools.Method(target.GetType(), method) ?? throw new MissingMethodException(target.GetType().Name, method)).Invoke(target, args);
    internal static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T found) yield return found;
            foreach(var nested in Descendants<T>(child)) yield return nested;
        }
    }
    internal static bool RewardReady(NCardRewardSelectionScreen screen) =>
        Field(screen, "_completionSource") is TaskCompletionSource<int?> completion && !completion.Task.IsCompleted;
    internal static void SelectCard(NCardRewardSelectionScreen screen, CardModel card) => Invoke(screen, "SelectCard", screen.GetCardHolder(card));
    internal static void SelectSkip(NCardRewardSelectionScreen screen, int index) => Invoke(screen, "OnAlternateRewardSelected", index);
    internal static Task SelectRest(NRestSiteButton button, RestSiteOption option) => (Task)Invoke(button, "SelectOption", option)!;
    internal static Task ClaimReward(NRewardButton button) => (Task)Invoke(button, "GetReward")!;
    internal static void PreviewSelection(Control screen, MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl button) =>
        (AccessTools.Method(screen.GetType(), "PreviewSelection", [typeof(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NButton)])
            ?? throw new MissingMethodException(screen.GetType().Name,"PreviewSelection(NButton)"))
        .Invoke(screen,[button]);
    internal static void SelectUpgrade(NDeckUpgradeSelectScreen screen, CardModel card) => Invoke(screen, "OnCardClicked", card);
    internal static void ConfirmUpgrade(NDeckUpgradeSelectScreen screen) => Invoke(screen, "ConfirmSelection", Field(screen, "_singlePreviewConfirmButton"));
}
