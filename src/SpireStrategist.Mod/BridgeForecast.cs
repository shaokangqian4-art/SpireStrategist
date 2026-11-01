using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal static class BridgeForecast
{
    internal static BridgeStop[] Read(SlipperyBridge e,RunSnapshot s)
    {
        if(e.Owner is not {} owner||GameActions.Field(e,"_randomCardToLose") is not CardModel current)return [];
        var rng=new Rng(e.Rng.ToSerializable());
        var skipped=new HashSet<CardModel>(GameActions.Field(e,"_skippedRemovals") as IEnumerable<CardModel>??[]);
        var nextLoss=(int)e.DynamicVars["HpLoss"].BaseValue;
        var total=0;var stops=new List<BridgeStop>();
        for(var step=1;step<=16;step++)
        {
            total+=nextLoss++;
            if(total>=s.Hp)break;
            skipped.Add(current);
            var pool=owner.Deck.Cards.Where(c=>c.GetType()!=current.GetType()&&c.IsRemovable&&!skipped.Contains(c)).ToList();
            if(pool.Count==0)pool=owner.Deck.Cards.Where(c=>c.IsRemovable).ToList();
            if(pool.Count==0)break;
            current=rng.NextItem(pool)!;
            stops.Add(new(SnapshotReader.ReadCard(current),step,total));
        }
        return stops.ToArray();
    }
}
