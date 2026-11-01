using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace SpireStrategist.Mod;

internal static class ThisOrThatForecast
{
    internal static RelicModel? Read(Player player)
    {
        // The verified native branch rolls Rewards, then removes from the player's bag.
        // Never call RelicFactory.PullNextRelicFromFront on the real player for a preview.
        // Serializable bags omit multiplayer fallback and refresh sources.
        if(player.RunState.Players.Count!=1 || GameActions.Field(player.RelicGrabBag,"_refreshAllowed") is true)
            return null;
        var rng=new Rng(player.PlayerRng.Rewards.ToSerializable());
        var bag=RelicGrabBag.FromSerializable(player.RelicGrabBag.ToSerializable());
        return bag.PullFromFront(RelicFactory.RollRarity(rng),player.RunState)??RelicFactory.FallbackRelic;
    }
}
