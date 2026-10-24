namespace SpireStrategist.Core;

public sealed record ToothPlan(int[] Indices,double Value,double UpgradeValue,double TemporaryRisk);

// Five cards return in random order, one upgraded card after each won combat.
// A small bounded beam compares sets; removing five independently "safe" cards can
// otherwise remove an entire damage/block engine. Scores are priors, not simulations.
public static class PaelsToothPolicy
{
    public static ToothPlan Plan(RunSnapshot s,CardSnapshot[]? candidates=null)
    {
        candidates??=s.Deck.Where(c=>c.CanUpgrade).ToArray();
        if(candidates.Length<5)return new([], -40,0,40);
        var v=new StrategicValue(s);
        var profiles=candidates.Select(CardCatalog.Find).ToArray();
        var upgrades=candidates.Select(c=>v.Upgrade(c)*Math.Clamp(v.Remaining/18,.25,1.25)).ToArray();
        double[] Features(CardProfile? p)=>p==null?[0,0,0,0,0,0]:
            [p.Attack,p.Block,p.Draw,p.Energy,p.Scaling+p.Strength,CardMechanics.ActiveExhaust(p)];
        var total=new double[6];
        foreach(var c in s.Deck)
        {var f=Features(CardCatalog.Find(c));for(var j=0;j<6;j++)total[j]+=f[j];}
        ToothPlan Score(int[] indices)
        {
            var lost=new double[6];
            foreach(var i in indices){var f=Features(profiles[i]);for(var j=0;j<6;j++)lost[j]+=f[j];}
            var risk=0d;
            double[] costs=[14,14,7,6,10,5];
            for(var j=0;j<6;j++)
                if(total[j]>.01)risk+=costs[j]*Math.Pow(lost[j]/total[j],2);
            risk*=1+(1-v.Ratio)*1.2;
            risk+=Math.Max(0,12-(s.Deck.Length-indices.Length))*2;
            // Even a strong deck has an opportunity cost while upgrades are delayed.
            risk+=indices.Length*.8;
            var gain=indices.Sum(i=>upgrades[i]);
            return new(indices,gain-risk,gain,risk);
        }
        var beam=new[]{Score([])};
        for(var count=1;count<=5;count++)
            beam=beam.SelectMany(p=>Enumerable.Range(0,candidates.Length)
                    .Where(i=>!p.Indices.Contains(i)&&candidates[i].CanUpgrade)
                    .Select(i=>Score(p.Indices.Append(i).Order().ToArray())))
                .DistinctBy(p=>string.Join(",",p.Indices))
                .OrderByDescending(p=>p.Value).ThenBy(p=>string.Join(",",p.Indices))
                .Take(12).ToArray();
        return beam.FirstOrDefault()??new([], -40,0,40);
    }
    public static RelicAssessment Evaluate(RunSnapshot s)
    {
        var p=Plan(s);
        return new(p.Value,"pael-upgrade",$"五张暂存、每战随机返还一张升级牌；升级收益 {p.UpgradeValue:F1}，暂时缺牌风险 {p.TemporaryRisk:F1}。不是永久删牌。");
    }
}
