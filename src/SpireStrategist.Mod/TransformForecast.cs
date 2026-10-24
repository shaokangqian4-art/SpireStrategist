using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal sealed record TransformForecast(CardModel Original,CardSnapshot Result,double Value)
{
    internal static bool Supports(EventModel? model)=>model is AromaOfChaos or WhisperingHollow or Neow;

    // Only the verified single-card branches use this forecast. Do not create a card through
    // RunState/CardScope: that would register a preview in the real run's card collection.
    internal static TransformForecast Read(EventModel model,CardModel original,RunSnapshot state)
    {
        var copy=new Rng((model is Neow?original.Owner.RunState.Rng.Niche:model.Rng).ToSerializable());
        return ReadCore(original,state,copy);
    }
    internal static TransformForecast ReadNewLeaf(CardModel original,RunSnapshot state)=>
        ReadCore(original,state,new Rng(original.Owner.RunState.Rng.Niche.ToSerializable()));
    private static TransformForecast ReadCore(CardModel original,RunSnapshot state,Rng copy)
    {
        var canonical=copy.NextItem(CardFactory.GetDefaultTransformationOptions(original,false))!;
        var preview=canonical.ToMutable();preview.Owner=original.Owner;
        var result=SnapshotReader.ReadCard(preview);
        var index=original.Owner.Deck.Cards.ToList().IndexOf(original);
        if(index<0)throw new InvalidOperationException("转化目标不在当前牌组中。");
        var afterRemoval=state with{Deck=state.Deck.Where((_,i)=>i!=index).ToArray()};
        var value=new StrategicValue(state).Remove(SnapshotReader.ReadCard(original))+new StrategicValue(afterRemoval).Add(result);
        return new(original,result,value);
    }

    internal static CardSnapshot[] Leafy(EventModel model)
    {
        var p=model.Owner??throw new InvalidOperationException("变化预测缺少玩家。");
        var copy=new Rng(p.PlayerRng.Transformations.ToSerializable());
        var basics=p.Deck.Cards.Where(c=>c.Rarity==MegaCrit.Sts2.Core.Entities.Cards.CardRarity.Basic).ToArray();
        var targets=new[]{basics.FirstOrDefault(c=>c.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Strike)),
            basics.FirstOrDefault(c=>c.Tags.Contains(MegaCrit.Sts2.Core.Entities.Cards.CardTag.Defend))};
        return targets.Where(c=>c!=null).Select(c=>{
            var canonical=copy.NextItem(CardFactory.GetDefaultTransformationOptions(c!,false))!;
            var preview=canonical.ToMutable();preview.Owner=p;
            return SnapshotReader.ReadCard(preview);
        }).ToArray();
    }
}
