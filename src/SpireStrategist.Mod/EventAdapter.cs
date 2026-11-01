using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Random;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal sealed record EventPlan(EventModel Model,RunSnapshot State,NEventOptionButton[] Buttons,EventChoice[] Choices,Evaluation Evaluation);
internal static class EventAdapter
{
    internal static EventPlan? Read(AdvisorService service,NEventRoom room)
    {
        var model=GameActions.Field(room,"_event") as EventModel;
        if(model==null||model.Owner!=service.Player||model.GetType().Assembly!=typeof(EventModel).Assembly)return null;
        var buttons=GameActions.Descendants<NEventOptionButton>(room).Where(b=>b.IsVisibleInTree()&&b.Option!=null).ToArray();
        if(buttons.Length==0)return null;
        var s=service.WithForesight(SnapshotReader.Read(service.State,service.Player));
        var before=JsonSerializer.Serialize(s.Hidden?.Streams);var eventBefore=model.Rng.ToSerializable();
        var vars=model.DynamicVars.ToDictionary(p=>p.Key,p=>(double)p.Value.BaseValue);
        // Native DenseVegetation calls MimicRestSiteHeal: sleep rewards apply,
        // but camp entry passives and Night Terrors' non-mimicked cost do not.
        if(model is DenseVegetation)vars["RestExtra"]=CampMechanics.HealExtra(service,s,mimicked:true);
        var choices=buttons.Select(b=>Describe(model,b.Option,s,vars) with{Key=b.GetInstanceId().ToString()}).ToArray();
        if(!eventBefore.Equals(model.Rng.ToSerializable())||before!=JsonSerializer.Serialize(HiddenReader.Read(service.State,service.Player).Streams))
            throw new InvalidOperationException("事件评估推进了实际随机流，停止接管。");
        return new(model,s,buttons,choices,EventPolicy.Evaluate(s,choices));
    }
    internal static EventChoice Describe(EventModel model,EventOption option,RunSnapshot s,IReadOnlyDictionary<string,double> vars)
    {
        var key=option.TextKey;var name=option.Title?.GetFormattedText()??key;
        if(option.IsLocked)return new(key,name,Enabled:false);
        if(option.IsProceed)return new(key,name);
        // Verified native ending dialogue advances text; its final PROCEED invokes WinRun.
        if(model is TheArchitect && key.StartsWith("THE_ARCHITECT.dialogue.",StringComparison.Ordinal))
            return new(key,name);
        EventChoice choice;
        if(model is AncientEventModel && option.Relic is {} relic)
        {
            var result=RelicValue.Evaluate(relic.Id.Entry,s,true);
            if(model is Neow&&model.Owner!=null&&NeowPolicy.Supports(s))
            {
                var nodes=RouteAdapter.Nodes(model.Owner.RunState);
                var start=nodes.OrderBy(n=>n.Row).FirstOrDefault()?.Id;
                result=NeowPolicy.Evaluate(relic.Id.Entry,s,start==null?0:NeowPolicy.BootsOpportunity(nodes,start));
                if(NeowPolicy.Excluded(relic.Id.Entry))
                    return new(key,name,Enabled:false,Notes:["当前助手不支持此跨角色／多人奖励，排除自动选择。"]);
            }
            choice=new(key,name,Supported:result.Supported,Benefit:result.Value,Selection:result.Selection,Notes:[result.Note]);
            // Resource costs already included in valuation, but must still veto lethal native effects.
            var loss=relic.Id.Entry switch{"PRECARIOUS_SHEARS"=>16,_=>0};
            var maxLoss=relic.Id.Entry switch{"LEAFY_POULTICE"=>12,"SERE_TALON"=>9,_=>0};
            if(s.Hp<=loss||s.MaxHp<=maxLoss)choice=choice with{Lethal=true};
        }
        else
        {
            try{choice=EventRules.Describe(model.Id.Entry,key,name,s,vars);}
            catch(Exception ex){choice=new(key,name,Supported:false,Notes:[ex.Message]);}
        }
        choice=Predict(model,option,s,choice);
        if(model.Owner!=null&&option.WillKillPlayer?.Invoke(model.Owner)==true)choice=choice with{Lethal=true};
        return choice;
    }
    private static EventChoice Predict(EventModel e,EventOption option,RunSnapshot s,EventChoice c)
    {
        var v=new StrategicValue(s);var o=option.TextKey.Split('.').Last();
        if(e.Owner==null)return c;
        if(e is ThisOrThat && o is "PLAIN" or "ORNATE")
        {
            var predicted=ThisOrThatForecast.Read(e.Owner);
            return ThisOrThatPolicy.Apply(c,predicted?.Id.ToString(),predicted?.Rarity==MegaCrit.Sts2.Core.Entities.Relics.RelicRarity.Rare) with
            {Notes=[predicted==null?"遗物预测不可用，按用户默认选择金币。":
                "原生奖励随机流与遗物袋副本预测："+predicted.Title.GetFormattedText()+"（"+predicted.Rarity+"）。不消耗真实遗物或随机数。"]};
        }
        if(e is Neow&&NeowPolicy.Supports(s))
        {
            if(o=="ARCANE_SCROLL"&&RewardForesightReader.Unsupported(e.Owner.RunState,e.Owner)==null)
            {
                var copy=RewardForesightReader.Clone(e.Owner.RunState);
                var card=RewardForesightReader.GenerateArcaneScroll(copy.Players.Single());
                if(card!=null)
                {
                    var path=s.Foresight?.Branches.FirstOrDefault()?.Path.Split('>');
                    var future=path is {Length:>0}?RewardForesightReader.Read(e.Owner.RunState,e.Owner,path,true):null;
                    return c with{Benefit=NeowPolicy.ScrollScore(s with{Foresight=future},card),
                        Notes=[..c.Notes??[],"辉眼：奥术卷轴副本预览 "+card.Name+"；后续分支从卷轴消耗后的随机状态计算。"]};
                }
            }
            if(o=="NEW_LEAF")
            {
                var best=e.Owner.Deck.Cards.Where(x=>x.IsTransformable)
                    .Select(x=>TransformForecast.Read(e,x,s)).OrderByDescending(x=>x.Value).FirstOrDefault();
                if(best!=null)return c with{Benefit=NeowPolicy.TransformScore(s,[best.Result],false),
                    Notes=[$"随机流 Niche 副本预测：{best.Original.Title} → {best.Result.Name}；蓝牌按第一档、金牌高于第一档，另计构筑适配。入牌额外触发未计入。"]};
            }
            if(o=="LEAFY_POULTICE")
            {
                var results=TransformForecast.Leafy(e);
                return c with{Benefit=NeowPolicy.TransformScore(s,results,true),
                    Notes=[$"随机流 Transformations 副本预测（先打击后防御）：{string.Join("、",results.Select(x=>x.Name))}。扣12上限；入牌额外触发未计入。"]};
            }
        }
        var rng=new Rng(e.Rng.ToSerializable());
        EventChoice Known(double value,string text)=>c with{Supported=true,Benefit=value,Notes=["已核对事件算法，使用随机流副本／已生成结果："+text]};
        if(TransformForecast.Supports(e)&&c.Selection=="transform"&&c.Transform==1)
        {
            var candidates=e.Owner.Deck.Cards.Where(x=>x.IsTransformable).Select(x=>TransformForecast.Read(e,x,s)).OrderByDescending(x=>x.Value).ToArray();
            if(candidates.Length>0)
            {
                var best=candidates[0];
                return Known(best.Value,best.Original.Title+" → "+best.Result.Name+"（原始转化牌；入牌触发的额外遗物效果未计入）") with{Transform=0};
            }
        }
        if(e is TabletOfTruth && o.StartsWith("DECIPHER"))
        {
            var pool=e.Owner.Deck.Cards.Where(x=>x.IsUpgradable).ToList();
            var all=Convert.ToInt32(GameActions.Field(e,"_decipherCount"))==4;
            var cards=all?pool.ToArray():pool.Count==0?[]:new[]{rng.NextItem(pool)!};
            return Known(cards.Sum(x=>v.Upgrade(SnapshotReader.ReadCard(x))),cards.Length==0?"无可升级卡牌":string.Join("、",cards.Select(x=>x.Title))) with{RandomUpgrade=0};
        }
        if(e is DoorsOfLightAndDark && o=="LIGHT")
        {
            var cards=e.Owner.Deck.Cards.Where(x=>x.IsUpgradable).ToList().StableShuffle(rng).Take((int)e.DynamicVars["Cards"].BaseValue).ToArray();
            return Known(cards.Sum(x=>v.Upgrade(SnapshotReader.ReadCard(x))),string.Join("、",cards.Select(x=>x.Title))) with{RandomUpgrade=0};
        }
        if(e is TrashHeap)
        {
            if(o=="GRAB")
            {
                var pool=(CardModel[])AccessTools.Property(e.GetType(),"Cards").GetValue(null)!;
                var card=rng.NextItem(pool)!.ToMutable();card.Owner=e.Owner;
                return Known(v.Add(SnapshotReader.ReadCard(card)),card.Title);
            }
            if(o=="DIVE_IN")
            {
                var pool=(RelicModel[])AccessTools.Property(e.GetType(),"Relics").GetValue(null)!;
                var relic=rng.NextItem(pool)!;
                return Known(RelicValue.Evaluate(relic.Id.Entry,s).Value,relic.Title.GetFormattedText());
            }
        }
        if(e is SlipperyBridge)
        {
            var card=GameActions.Field(e,"_randomCardToLose") as CardModel;
            if(card==null)return c;
            if(o=="OVERCOME")return Known(BridgePolicy.Removal(s,SnapshotReader.ReadCard(card)),card.Title);
            if(o.StartsWith("HOLD_ON"))
            {
                var stops=BridgeForecast.Read((SlipperyBridge)e,s);
                var best=BridgePolicy.BestContinuation(s,stops);
                if(best!=null)return c with{Supported=true,Benefit=best.Removal,HpLoss=best.Stop.HpLoss,
                    Burden=Math.Max(0,best.Cost-v.HpCost(best.Stop.HpLoss)),
                    Notes=[$"恨桥随机流副本前瞻：下一张 {stops[0].Card.Name}；计划再撑 {best.Stop.Steps} 次，累计损失 {best.Stop.HpLoss} 生命后移除 {best.Stop.Card.Name}。本次支付 {e.DynamicVars["HpLoss"].BaseValue} 生命；每次支付后重新评估，不连续盲点。"]};
            }
        }
        if(e is RelicTrader)
        {
            var index=o switch{"TOP"=>0,"MIDDLE"=>1,"BOTTOM"=>2,_=>-1};
            // Read initialized backing fields; getters would lazily pull real relics.
            var owned=GameActions.Field(e,"_ownedRelics") as IReadOnlyList<RelicModel>;
            var offered=GameActions.Field(e,"_newRelics") as IReadOnlyList<RelicModel>;
            if(index>=0&&owned?.Count>index&&offered?.Count>index)
                return Known(RelicValue.Evaluate(offered[index].Id.Entry,s).Value-RelicValue.Evaluate(owned[index].Id.Entry,s).Value,
                    owned[index].Title.GetFormattedText()+" → "+offered[index].Title.GetFormattedText());
        }
        if(e is DollRoom)
        {
            var pool=((Array)AccessTools.Field(e.GetType(),"_dolls").GetValue(null)!).Cast<object>()
                .Select(x=>(RelicModel)AccessTools.Field(x.GetType(),"relic").GetValue(x)!).ToArray();
            if(o=="RANDOM")
            {var relic=rng.NextItem(pool)!;return Known(RelicValue.Evaluate(relic.Id.Entry,s).Value,relic.Title.GetFormattedText());}
            // StableShuffle sorts the DollChoice by relic.CompareTo before consuming RNG.
            if(o is "EXAMINE" or "TAKE_SOME_TIME")
            {
                var offered=pool.ToList().StableShuffle(rng).Take(o=="EXAMINE"?3:2).ToArray();
                return Known(offered.Max(r=>RelicValue.Evaluate(r.Id.Entry,s).Value),string.Join("、",offered.Select(r=>r.Title.GetFormattedText())));
            }
            var selected=pool.FirstOrDefault(r=>r.Title.GetFormattedText()==option.Title.GetFormattedText());
            if(selected!=null)return Known(RelicValue.Evaluate(selected.Id.Entry,s).Value,selected.Title.GetFormattedText());
        }
        return c;
    }
}
