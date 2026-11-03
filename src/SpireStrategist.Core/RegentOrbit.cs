namespace SpireStrategist.Core;

// OrbitPower keeps its spend counter across turns. Each played instance refunds
// one energy per four spent; spending stars is not spending energy.
public static class RegentOrbit
{
    public static double Refund(RunSnapshot s,double draw,int copies)
    {
        if(copies<=0)return 0;
        var repeatable=s.Deck.Where(c=>c.Type!="Power"&&c.Type!="Curse"&&c.Type!="Status"&&!c.Keywords.Contains("Exhaust")).ToArray();
        if(repeatable.Length==0)return 0;
        var fixedCost=repeatable.Sum(c=>c.CostsX?0:Math.Max(0,c.Cost));
        var x=repeatable.Count(c=>c.CostsX&&!c.CostsStarX);
        var capacity=(double)fixedCost/repeatable.Length*(5+Math.Min(5,Math.Max(0,draw)*.8))+Math.Min(2,x);
        var budget=Math.Max(0,s.Energy);
        // Bounded steady-state estimate, not a guarantee of drawing every outlet
        // or of an infinite loop. Four+ instances still remain capacity-limited.
        var recycled=(double)budget*copies/Math.Max(1,4-copies);
        return Math.Clamp(Math.Min(Math.Max(0,capacity-budget),recycled),0,6);
    }
    public static double Marginal(RunSnapshot s,double draw)
    {
        var n=s.Deck.Count(c=>c.ClassName=="Orbit");
        return Math.Max(0,Refund(s,draw,n+1)-Refund(s,draw,n));
    }
    public static bool SupportedSecond(RunSnapshot s,double draw)=>s.Deck.Count(c=>c.ClassName=="Orbit")==1&&draw>=3&&Marginal(s,draw)>=.7;
}
