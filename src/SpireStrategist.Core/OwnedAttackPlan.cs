namespace SpireStrategist.Core;

// Owned, payable damage only. No forecast, acquisition scorer or inferred future relics.
// Kept independent of HeroPolicy.Analyze to avoid recursive composition evaluation.
public static class OwnedAttackPlan
{
    public static double Strength(RunSnapshot s)
    {
        bool R(string id)=>s.Relics.Any(r=>r.Split('.').Last()==id);
        return (R("SWORD_OF_JADE")?3:0)+(R("VAJRA")?1:0)+(R("TOASTY_MITTENS")?2:0)
            +(R("GIRYA")?Math.Clamp(s.GiryaLifts??0,0,3):0)
            +Math.Min(4,s.Deck.Sum(c=>CardCatalog.Find(c)?.Strength??0));
    }
    public static double Energy(RunSnapshot s)=>Math.Max(0,s.Energy)+Math.Min(2,s.Deck.Sum(c=>CardCatalog.Find(c)?.Energy??0)*.4);
    public static double Hits(RunSnapshot s,CardSnapshot c)
    {
        if(c.Type!="Attack")return 0;
        var energy=Energy(s);
        return c.ClassName switch{
            "Volley"=>Math.Clamp(energy+(s.Relics.Any(r=>r.Split('.').Last()=="CHEMICAL_X")?2:0),0,6),
            "LunarBlast"=>Math.Clamp(s.Deck.Count(x=>x.Type=="Skill"&&x.Cost<=1&&!x.Keywords.Contains("Unplayable"))*.3,0,4),
            "AstralPulse"=>2,
            _=>EnchantmentValue.Hits(c)};
    }
    public static bool StrengthOutlet(RunSnapshot s,CardSnapshot c)=>c.Type=="Attack"&&c.Rarity!="Basic"&&
        !c.Keywords.Contains("Exhaust")&&!c.Keywords.Contains("Ethereal")&&
        c.ClassName!="SovereignBlade"&&HeroPolicy.Stars(c)<=0&&!c.CostsStarX&&Hits(s,c)>=2&&
        (c.CostsX||c.Cost<=Energy(s));
    public static bool StrengthCore(RunSnapshot s)=>Strength(s)>=2&&s.Deck.Any(c=>StrengthOutlet(s,c)&&
        (c.ClassName is "Volley" or "LunarBlast"||s.Deck.Count(d=>StrengthOutlet(s,d))>=2));
    public static double Efficiency(RunSnapshot s,CardSnapshot c)
    {
        if(c.Type!="Attack"||c.CostsStarX||HeroPolicy.Stars(c)>0||c.Keywords.Contains("Unplayable")||!c.CostsX&&c.Cost>Energy(s))return 0;
        var hits=Hits(s,c);var damage=HeroPolicy.V(c,"Damage");
        if(damage<=0)return 0;
        return (damage+Strength(s))*hits/Math.Max(1,c.CostsX?Energy(s):c.Cost);
    }
}
