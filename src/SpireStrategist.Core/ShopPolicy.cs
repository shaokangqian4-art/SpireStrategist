namespace SpireStrategist.Core;

public sealed record ShopCard(string Key,CardSnapshot Card,int Price);
public sealed record ShopChoice(string Key,string Name,string Kind,int Price,double Score,string[] Reasons,int DeckIndex=-1)
{
    public double? IntrinsicValue {get;init;}
    public bool Affordable {get;init;}=true;
    public bool Modeled {get;init;}=true;
    public bool Eligible {get;init;}=true;
}
public sealed record ShopEvaluation(ShopChoice? Recommendation,ShopChoice[] Choices,int Reserve,string[] Notes);

public static class ShopPolicy
{
    // Conservative initial policy, not a trained estimate of victory probability.
    public static ShopEvaluation Evaluate(RunSnapshot s,ShopCard[] stock,int? removalPrice,int arrivalGold,int boughtCards=0,int? reserveOverride=null)
    {
        if(s.Character is "Defect" or "Silent" or "Regent" or "Necrobinder")return DefectStrategy.Shop(s,stock,removalPrice,arrivalGold,boughtCards,reserveOverride);
        var choices=new List<ShopChoice>();
        var reserve=reserveOverride??Math.Min(150,Math.Max(0,arrivalGold)*35/100);
        var profiles=s.Deck.Select(CardCatalog.Find).Where(p=>p!=null).Cast<CardProfile>().ToArray();
        var cycle=profiles.Sum(p=>p.Draw)/Math.Max(1,s.Deck.Length)*15>=3;
        var exhaust=profiles.Sum(p=>p.ExhaustSource)>=3;
        var ghost=s.Relics.Any(r=>r is "RELIC.GHOST_SEED" or "GHOST_SEED");
        var mitigation=(ghost?.85:1)*(cycle?.9:1)*(exhaust?.85:1);
        if(s.Character!="Ironclad" || s.PlayerCount<1 || s.MaxHp<=0)
            return new(null,[],reserve,["仅支持有效单人战士状态。"]);
        if(removalPrice is >=0 && removalPrice<=s.Gold)
        {
            var attacks=profiles.Sum(p=>p.Attack);var blocks=profiles.Sum(p=>p.Block);
            foreach(var pair in s.Deck.Select((c,i)=>(c,i)).Where(x=>((x.c.ClassName is "StrikeIronclad" or "DefendIronclad") || x.c.Type=="Curse") && !x.c.Modified && x.c.ClassName!="AscendersBane" && !x.c.Keywords.Contains("Eternal")))
            {
                var strike=pair.c.ClassName=="StrikeIronclad";
                var replacement=strike?attacks-2:blocks-2;
                var curse=pair.c.Type=="Curse";
                // Removing one basic improves draw quality, but does not add an engine.
                // Keep this on the same marginal-benefit scale as card acquisition.
                var curseValue=curse?new StrategicValue(s).Remove(pair.c):0;
                var value=curse?(curseValue<=4?3:curseValue+2):(4+Math.Clamp(replacement, -2,3)-pair.c.UpgradeLevel*2)*mitigation;
                var profile=CardCatalog.Find(pair.c);
                if(!curse&&profile!=null&&(strike?attacks-profile.Attack<2:blocks-profile.Block<1.5))
                    value-=8; // Do not remove the remaining functional damage/block floor.
                var priceCost=GoldCost(s,removalPrice.Value);
                choices.Add(new("remove:"+pair.i,pair.c.Name,"remove",removalPrice.Value,value-priceCost,
                    [$"删{(curse?"诅咒":"基础牌")}收益 {value:F1}；虚无/保留诅咒低于基础牌，其他可删诅咒优先",$"价格机会成本 -{priceCost:F1}",
                     $"幽灵种子={ghost}，强过牌={cycle}，强消耗={exhaust}；删牌收益倍率 {mitigation:F2}"],pair.i));
            }
        }
        foreach(var entry in stock.Where(e=>e.Price>=0 && e.Price<=s.Gold))
        {
            // Known enchantments already have a shared card model. Rejecting every
            // modified card silently removed Ironclad shop recommendations.
            if(CardCatalog.HasUnmodelledModification(entry.Card) || CardCatalog.Find(entry.Card)==null) continue;
            var score=new Policy().Evaluate(s,[new("card",entry.Card.Name,"card",Card:entry.Card),new("skip","存钱","skip")]).Options[0].Score;
            if(score==null)continue;
            var duplicates=CardMechanics.Copies(s,entry.Card);
            var p=CardCatalog.Find(entry.Card)!;
            var engine=p.Draw+p.Energy;
            var supportedSecond=entry.Card.ClassName=="Dominate"&&IroncladSynergies.SupportedSecondDominate(s);
            var extraDuplicate=duplicates*(supportedSecond?.5:engine>=1.5?1:3);
            var priceCost=GoldCost(s,entry.Price);
            var value=score.Value-priceCost-1.5-extraDuplicate;
            // A reserve is for uncertain future opportunities, not a veto on a
            // strong, affordable first copy already in front of us. No rarity bonus.
            var useReserve=score.Value>=7&&(duplicates==0||supportedSecond)&&value>0;
            var allowed=boughtCards<2 && (s.Gold-entry.Price>=reserve||useReserve);
            choices.Add(new(entry.Key,entry.Card.Name,"card",entry.Price,allowed?value:Math.Min(-1,value),
                [$"牌组增益先验 {score:F1}，购牌门槛 -1.5",$"价格机会成本 -{priceCost:F1}，额外重复折扣 -{extraDuplicate:F1}",
                 allowed?(s.Gold-entry.Price<reserve?"高收益首张牌或已配套的第二张主宰可使用预留金币；交易后重新评估":"交易后重新评估"):
                    $"保留 {reserve} 金币；每店最多自动购牌 2 张"]));
        }
        // Explain mutually exclusive purchases without subtracting the alternative
        // twice: direct net-score comparison already accounts for the lost option.
        choices=choices.Select(choice=>
        {
            var alternative=choices.Where(c=>c.Kind!=choice.Kind&&c.Score>0&&c.Price+choice.Price>s.Gold)
                .OrderByDescending(c=>c.Score).FirstOrDefault();
            return alternative==null?choice:choice with{Reasons=choice.Reasons.Append(
                $"预算二选一：{(alternative.Kind=="remove"?"删":"买")}{alternative.Name} {alternative.Price} 金币 / {alternative.Score:F1} 分；本项分差 {choice.Score-alternative.Score:+0.0;-0.0;0.0}").ToArray()};
        }).ToList();
        var best=choices.Where(c=>c.Score>0).OrderByDescending(c=>c.Score).ThenBy(c=>c.Price).FirstOrDefault();
        return new(best,choices.ToArray(),reserve,["价格读取当前实际折扣价；买牌与删牌使用相同金币成本。分数是待校准启发式，不是胜率。","关键遗物先评估；普通买牌与删牌比较净收益，每次交易后重算。其他遗物和药水由自动爬塔后续阶段处理。"]);
    }
    private static double GoldCost(RunSnapshot s,int price)=>price/40d*(s.Gold<150?1.5:1);
}
