namespace SpireStrategist.Core;

// Acquisition capacity: a combo being strong does not mean another copy can be paid for.
public static class RegentEconomy
{
    public static double SetupPressure(RunSnapshot s,HeroPolicy.Composition a)=>Math.Clamp(
        // Reserve one energy for immediate survival. Aggregate deck energy is
        // conditional on drawing/playing its sources, so credit only a fraction.
        (s.Deck.Where(c=>c.Type=="Power").Sum(c=>Math.Max(0,c.Cost))-Math.Max(1,s.Energy-1)-Math.Min(1,a.Energy*.25))/3,0,2);
    public static IEnumerable<ScorePart> Adjust(RunSnapshot s,HeroPolicy.Composition a,CardSnapshot c)
    {
        if(s.Character!="Regent")yield break;
        if(c.ClassName=="Stardust"&&RegentDirection.Analyze(s).Committed==RegentDirection.Plan.Generation)
        {
            // Stars already committed to draw, energy and defensive skills cannot
            // simultaneously pay an X finisher. Keep genuinely spare-star bridges.
            var spare=Math.Max(0,a.Stars-a.StarDemand);
            if(spare<3)yield return new("无色主线先支付现有星费运转与防守，星尘只使用剩余星",-Math.Round((3-spare)*1.5,3));
        }
        var copies=s.Deck.Count(d=>d.ClassName==c.ClassName);
        var pyramid=s.Relics.Any(r=>r.Split('.').Last()=="RUNIC_PYRAMID");
        var pressure=Math.Clamp(SetupPressure(s,a)+(c.Type=="Power"?Math.Max(0,c.Cost)/3d:0),0,2);
        if(copies>0&&c.ClassName=="Conqueror")
            yield return new("征服者重复不叠乘伤害，同回合出剑与支付能力限制",-copies*(2+(pyramid?1:0)));
        if(copies>0&&c.Type=="Power")
            yield return new("重复能力需要额外启动回合，已有成长不能替代支付能力",-copies*(1+pressure*(c.ClassName=="Orbit"&&RegentOrbit.SupportedSecond(s,a.Draw)?.5:1.5)));
        if(pyramid&&(c.Cost>=2&&!c.CostsX||c.Type=="Power"||c.ClassName is "Conqueror" or "SummonForth" or "HeirloomHammer"))
            yield return new("金字塔保留不提供费用，慢启动与条件组件占手",-(.6+pressure*.8+Math.Max(0,c.Cost-s.Energy)*.8));
        var sword=s.Deck.Any(RegentDirection.SwordCore)&&a.Forge>0;
        // Parry requires drawing and playing the sword; it is not opening block.
        var openingDefense=a.Defense-(s.Deck.Any(d=>d.ClassName=="Parry")?1:0);
        var deficit=Math.Clamp((4.5-openingDefense)/3,0,1);
        var immediateBlock=HeroPolicy.V(c,"Block")>0&&c.ClassName!="ParticleWall";
        var immediateDebuff=HeroPolicy.Tag(c,"weak")||HeroPolicy.Tag(c,"strength_down");
        var affordable=HeroPolicy.Stars(c)<=a.Stars+HeroPolicy.OpeningStars(s)*.5;
        if(sword&&deficit>0&&affordable&&(immediateBlock||immediateDebuff))
            yield return new("铸剑启动期防守缺口：先撑到成长兑现",Math.Round(3*deficit,3));
        if(sword&&copies>0&&deficit>0&&(HeroPolicy.Tag(c,"forge")||HeroPolicy.Tag(c,"sword_payoff")))
            yield return new("防守未就绪时不继续堆重复铸剑组件",Math.Round(-2*deficit,3));
    }
}
