using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace SpireStrategist.Mod;

[HarmonyPatch(typeof(NMapScreen),nameof(NMapScreen.Open))]
internal static class MapPlayerOpenPatch
{
    private static void Prefix(NMapScreen __instance,bool isOpenedFromTopBar)
    {
        if(isOpenedFromTopBar&&!__instance.IsOpen)
            AdvisorService.Current?.Autopilot.ManualMapOpened();
    }
}
