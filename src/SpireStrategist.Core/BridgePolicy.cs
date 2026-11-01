namespace SpireStrategist.Core;

public sealed record BridgeStop(CardSnapshot Card,int Steps,int HpLoss);
public sealed record BridgePlan(BridgeStop Stop,double Removal,double Cost)
{
    public double Score=>Removal-Cost;
}

// Stops describe one copied native RNG sequence, not freely selectable cards.
public static class BridgePolicy
{
    public static double Removal(RunSnapshot s,CardSnapshot card)
    {
        var value=new StrategicValue(s).Remove(card);
        if(s.Character=="Defect"&&DefectComposition.HasDamageEngine(s))
        {
            var index=Array.FindIndex(s.Deck,c=>ReferenceEquals(c,card));
            if(index<0)index=Array.FindIndex(s.Deck,c=>c.Id==card.Id&&c.UpgradeLevel==card.UpgradeLevel
                &&c.Cost==card.Cost&&c.Enchantment==card.Enchantment&&c.EnchantmentAmount==card.EnchantmentAmount&&c.Affliction==card.Affliction);
            if(index>=0&&!DefectComposition.HasDamageEngine(s with{Deck=s.Deck.Where((_,i)=>i!=index).ToArray()}))
                value=Math.Min(value,-18); // Existing engine loss is not another acquisition decision.
        }
        return value;
    }

    public static BridgePlan? BestContinuation(RunSnapshot s,IEnumerable<BridgeStop> stops)
    {
        BridgePlan? best=null;
        foreach(var stop in stops.Where(x=>x.Steps>0&&x.HpLoss>0&&x.HpLoss<s.Hp))
        {
            // Later payments hurt more as HP falls; do not price every reroll at starting HP.
            var cost=Enumerable.Range(0,stop.HpLoss).Sum(i=>new StrategicValue(s with{Hp=s.Hp-i}).HpCost(1));
            var reserve=s.NextNodeTypes.Length>0&&s.NextNodeTypes.All(n=>n=="RestSite")?5:Math.Max(8,s.MaxHp*.2);
            cost+=Math.Pow(Math.Max(0,reserve-(s.Hp-stop.HpLoss)),2)*.06;
            var plan=new BridgePlan(stop,Removal(s,stop.Card),cost);
            if(best==null||plan.Score>best.Score)best=plan;
        }
        return best;
    }
}
