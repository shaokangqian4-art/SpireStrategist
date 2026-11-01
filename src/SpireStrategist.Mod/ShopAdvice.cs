using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal static class ShopAdvice
{
    internal static PotionCandidate Describe(MegaCrit.Sts2.Core.Models.PotionModel p)=>new(p.GetType().Name,p.Rarity.ToString(),p.GetType().Assembly==typeof(MegaCrit.Sts2.Core.Models.PotionModel).Assembly);
    internal static ShopChoice Potion(RunSnapshot state,NMerchantSlot slot,int reserve)
    {
        var entry=(MerchantPotionEntry)slot.Entry;var p=Describe(entry.Model!);
        var model=PotionPolicy.Evaluate(state,p);
        var score=PotionPolicy.PurchaseScore(state,p,entry.Cost);
        var allowed=entry.Cost<=state.Gold-reserve&&entry.EnoughGold&&double.IsFinite(score);
        var constraint=entry.Cost>state.Gold?"当前买不起":!state.PotionSlots.Any(x=>x==null)?"没有空药水栏":
            entry.Cost>state.Gold-reserve?"受保留金币限制":score<=0||!double.IsFinite(score)?"用途收益不足以覆盖价格或无法获得":"预算允许";
        return new(slot.GetInstanceId().ToString(),entry.Model!.Title.GetFormattedText(),"potion",entry.Cost,
            double.IsFinite(score)?score:0,[model?.Reason??"未知药水：不自动购买", "药水购买比较用途与价格，不按最低价补栏",constraint])
            {IntrinsicValue=model?.Value,Affordable=entry.Cost<=state.Gold,Eligible=allowed,Modeled=model!=null};
    }
    internal static ShopEvaluation Complete(AdvisorService service,RunSnapshot state,NMerchantInventory inventory,ShopEvaluation plan)
    {
        var stock=inventory.GetAllSlots().Where(x=>x.IsVisibleInTree()&&x.Entry is MerchantCardEntry{IsStocked:true,CreationResult:not null})
            .Select(x=>new ShopCard(x.GetInstanceId().ToString(),SnapshotReader.ReadCard(((MerchantCardEntry)x.Entry).CreationResult!.Card),x.Entry.Cost)).ToArray();
        plan=ShopReview.IncludeCards(state,stock,plan);
        var choices=plan.Choices.ToList();var v=new StrategicValue(state);
        var nodes=RouteAdapter.Nodes(service.State);
        var reserve=service.State.CurrentMapPoint is {} point?ShopBudget.Reserve(state,nodes,RouteAdapter.Key(point),ClimbController.RelicReserve):ClimbController.RelicReserve;
        var knownRelics=new List<ShopChoice>();var potions=new List<ShopChoice>();
        foreach(var slot in inventory.GetAllSlots().Where(x=>x.IsVisibleInTree()&&x.Entry.IsStocked))
        {
            var key=slot.GetInstanceId().ToString();if(choices.Any(c=>c.Key==key))continue;
            if(slot.Entry is MerchantRelicEntry {Model:not null} relic)
            {
                var model=RelicValue.Shop(relic.Model.Id.Entry,state);
                var score=model?.Value-v.Gold(relic.Cost);
                var affordable=relic.Cost<=state.Gold-reserve;
                var reason=model==null?"尚未建模：接管仅可能按剩余预算随机购买，不作强度推荐。":"遗物构筑收益扣除金币成本";
                var choice=new ShopChoice(key,relic.Model.Title.GetFormattedText(),"relic",relic.Cost,score??0,
                    [reason,model?.Note??"",$"保留金币：{reserve}",affordable?"预算允许":"当前预算不足"])
                    {IntrinsicValue=model?.Value,Affordable=relic.Cost<=state.Gold,Eligible=affordable,Modeled=model!=null};
                choices.Add(choice);if(score>0&&affordable)knownRelics.Add(choice);
            }
            else if(slot.Entry is MerchantPotionEntry potion)
            {
                var choice=Potion(state,slot,reserve);
                choices.Add(choice);if(choice.Eligible&&choice.Score>0)potions.Add(choice);
            }
        }
        var recommendation=plan.Recommendation??knownRelics.OrderByDescending(c=>c.Score).FirstOrDefault();
        // Unknown relics can be purchased by automation's random budget policy;
        // do not invent a named recommendation or promise potion priority over them.
        var unknownAvailable=choices.Any(c=>c.Kind=="relic"&&c.Price<=state.Gold-reserve&&c.Reasons.Any(r=>r.StartsWith("尚未建模")));
        if(recommendation==null&&!unknownAvailable)recommendation=potions.OrderByDescending(c=>c.Score).ThenBy(c=>c.Price).ThenBy(c=>c.Key).FirstOrDefault();
        return plan with{Recommendation=recommendation,Choices=choices.ToArray(),Notes=[..plan.Notes,"卡牌、删牌、遗物、药水均列出；不同购买阶段的分数不直接混排为优先级。"]};
    }
}
