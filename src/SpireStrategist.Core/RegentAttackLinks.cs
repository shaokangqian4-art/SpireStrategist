namespace SpireStrategist.Core;

// Expected hits on one target, not target count or a promise of a perfect hand.
// All multipliers are bounded by the current deck's payable resource supply.
public static class RegentAttackLinks
{
    public static double Hits(RunSnapshot s,CardSnapshot c)
    {
        if(c.Type!="Attack")return 0;
        if(s.Character!="Regent")return OwnedAttackPlan.Hits(s,c);
        var a=HeroPolicy.Analyze(s);
        var energy=Math.Max(0,s.Energy)+Math.Min(3,a.Energy);
        var stars=Math.Max(0,a.Stars+HeroPolicy.OpeningStars(s)*.5);
        var hits=c.ClassName switch{
            "AstralPulse"=>2,
            "Volley"=>OwnedAttackPlan.Hits(s,c),
            "HeavenlyDrill"=>energy>=4?Math.Min(8,energy*2):energy,
            "Stardust"=>Math.Min(6,stars),
            "LunarBlast"=>OwnedAttackPlan.Hits(s,c),
            _=>EnchantmentValue.Hits(c)};
        if(HeroPolicy.Stars(c)>0)hits*=Math.Clamp(stars/HeroPolicy.Stars(c),0,1);
        if(c.Cost>0&&!c.CostsX)hits*=Math.Clamp(energy/c.Cost,0,1);
        return hits;
    }
    public static double BestMulti(RunSnapshot s)=>s.Deck.Where(c=>!HeroStrategy.Basic(c)&&
        !RegentDirection.OffPlan(RegentDirection.Analyze(s),c)).Select(c=>Math.Max(0,Hits(s,c)-1)).DefaultIfEmpty().Max();
    public static double FlatBonus(RunSnapshot s,CardSnapshot c)
    {
        bool R(string id)=>s.Relics.Any(r=>r.Split('.').Last()==id);
        var bonus=(R("MINIATURE_CANNON")&&c.UpgradeLevel>0?3:0)+(R("MYSTIC_LIGHTER")&&c.Enchantment!=null?9:0);
        var enchant=c.Enchantment?.Split('.').Last();
        if(enchant is "SHARP" or "MOMENTUM")bonus+=c.EnchantmentAmount;
        return bonus;
    }
    public static IEnumerable<ScorePart> Adjust(RunSnapshot s,CardSnapshot c)
    {
        if(s.Character!="Regent")yield break;
        if(c.Type=="Attack"){
            var extra=Math.Max(0,Hits(s,c)-1);
            var flat=FlatBonus(s,c);
            var strength=OwnedAttackPlan.Strength(s);
            if(extra>0&&strength>0)yield return new("已有力量与可支付多段攻击联动",Math.Min(4,extra*strength*.35));
            if(flat>0)yield return new("攻击每段增伤：附魔、大炮与打火机按触发条件估值",Math.Min(5,flat*Hits(s,c)*.18));
            if(extra>0&&s.Deck.Any(d=>d.ClassName=="Terraforming"))
                yield return new("地形改造活力作用于下一张多段攻击，不是持续力量",Math.Min(3,extra*.65));
        }
        if(c.ClassName=="Terraforming")yield return new("地形改造按现有可支付多段主力估值",Math.Min(4,BestMulti(s)*.8));
        if(c.ClassName is "Resonance" or "Monologue")yield return new("低先验力量辅助仅按已有多段出口回补",Math.Min(2,BestMulti(s)*.5));
    }
    public static RelicAssessment? Relic(string id,RunSnapshot s)
    {
        if(s.Character!="Regent")return null;
        var attacks=s.Deck.Where(c=>c.Type=="Attack"&&!HeroStrategy.Basic(c)&&!RegentDirection.OffPlan(RegentDirection.Analyze(s),c)).ToArray();
        if(id=="MINIATURE_CANNON")return new(2+Math.Min(16,attacks.Sum(c=>Hits(s,c)*(c.UpgradeLevel>0?1.6:c.CanUpgrade?.35:0))),"","大炮仅给已升级攻击逐段增伤；未来升级只折价计入。");
        if(id=="MYSTIC_LIGHTER")return new(1+Math.Min(22,attacks.Where(c=>c.Enchantment!=null).Sum(c=>Hits(s,c)*3)),"","打火机仅给已有附魔的攻击逐段增伤，不假设未来必有附魔。");
        return null;
    }
}
