namespace SpireStrategist.Core;

// Bounded, conditional priors. Coverage and expected triggers are proxies, NOT
// simulated turns or guaranteed damage. No floor constants or live RNG calls.
public static class IroncladSynergies
{
    public static double Variable(CardSnapshot c,string key)
    {
        if(c.Variables.TryGetValue(key,out var value))return (double)value;
        return CardRegistry.All.TryGetValue(c.ClassName,out var d)
            ?CardRegistry.Var(c.UpgradeLevel>0?d.Upgraded:d.Basic,key):0;
    }
    public static double VulnerableCoverage(RunSnapshot s,bool excludeDominate=false)
    {
        var seen=new Dictionary<string,int>();double total=0;
        foreach(var c in s.Deck)
        {
            var upgraded=c.UpgradeLevel>0;
            var quality=c.ClassName switch
            {
                "Bash"=>upgraded?.4:.25, "Tremble"=>upgraded?1.3:1.1,
                "Break"=>upgraded?2.2:1.8, "Thunderclap"=>.45,
                "Shockwave"=>upgraded?1.5:1.1, "Uppercut"=>upgraded?.9:.45,
                "Taunt"=>.4, "Dominate"=>excludeDominate?0:upgraded?.55:.3, _=>0
            };
            var n=seen.GetValueOrDefault(c.ClassName);seen[c.ClassName]=n+1;
            total+=quality*Math.Pow(.65,n);
        }
        return total*15/Math.Max(15,s.Deck.Length);
    }
    public static double VulnerablePayoffs(RunSnapshot s)=>Math.Min(5,s.Deck
        .GroupBy(c=>c.ClassName).Sum(g=>(g.Key switch
        {
            "Dominate"=>1.4, "MoltenFist"=>1.2, "Colossus" or "Cruelty" or "Vicious"=>.8,
            "Bully" or "Dismantle"=>.6, _=>0
        })*(1+Math.Min(2,g.Count()-1)*.35)));

    public static bool SupportedSecondDominate(RunSnapshot s)=>
        s.Deck.Count(c=>c.ClassName=="Dominate")==1 && VulnerableCoverage(s,true)>=.5 &&
        s.Deck.Any(c=>CardCatalog.Find(c)?.StrengthPayoff>=1);

    // Conservative amount after setup; excludes temporary Setup Strike strength.
    public static double SupportedStrength(RunSnapshot s)
    {
        var seen=new Dictionary<string,int>();double total=0;
        foreach(var c in s.Deck)
        {
            var amount=c.ClassName switch
            {
                "Inflame"=>Variable(c,"StrengthPower"),
                "Dominate"=>Math.Min(7,Variable(c,"VulnerablePower")+2*VulnerableCoverage(s,true)
                    +(s.Deck.Any(d=>d.ClassName=="MoltenFist")?1:0)),
                "DemonForm"=>Variable(c,"StrengthPower")*1.5,
                "Brand" or "FightMe" or "Prowess"=>Variable(c,"StrengthPower"), _=>0
            };
            var n=seen.GetValueOrDefault(c.ClassName);seen[c.ClassName]=n+1;
            total+=amount*Math.Pow(.65,n);
        }
        if(s.Relics.Any(r=>r is "RELIC.SPARKLING_ROUGE" or "SPARKLING_ROUGE"))total+=.7;
        return Math.Clamp(total,0,10);
    }

    public static double BlockTriggers(RunSnapshot s)
    {
        double paid=0,automatic=0;var seen=new Dictionary<string,int>();
        foreach(var c in s.Deck)
        {
            var p=CardCatalog.Find(c);if(p==null)continue;
            var n=seen.GetValueOrDefault(c.ClassName);seen[c.ClassName]=n+1;
            var repeat=Math.Pow(.65,n);
            if(c.ClassName=="CrimsonMantle")automatic+=.8*repeat;
            else if(c.ClassName=="FeelNoPain")automatic+=Math.Min(3,s.Deck.Select(CardCatalog.Find)
                .OfType<CardProfile>().Sum(CardMechanics.ActiveExhaust))*repeat;
            else if(c.ClassName=="SecondWind")paid+=2.2*repeat;
            else if(c.ClassName=="Rage")paid+=Math.Min(2,s.Deck.Count(d=>d.Type=="Attack")/4d)*repeat;
            else if(c.ClassName=="DefendIronclad")paid+=.2*repeat;
            else if(p.Block>.3&&c.Type=="Skill")paid+=repeat*(c.Keywords.Contains("Exhaust")?.35:1)/Math.Max(1,c.Cost);
        }
        if(s.Relics.Any(r=>r is "RELIC.ORNAMENTAL_FAN" or "ORNAMENTAL_FAN"))
            automatic+=Math.Min(.6,s.Deck.Count(c=>c.Type=="Attack")/20d);
        return Math.Clamp(automatic+paid*5/Math.Max(12,s.Deck.Length),0,5);
    }

    public static double MultiHitImprovement(RunSnapshot s,CardSnapshot card)
    {
        var hits=Math.Max(1,Variable(card,"Repeat"));
        var attacks=s.Deck.Where(c=>c.Type=="Attack"&&c.ClassName!=card.ClassName).ToArray();
        var averageHits=attacks.Length==0?1:attacks.Average(c=>Math.Max(1,Variable(c,"Repeat")));
        // Value the quality replacement even when raw attack count is saturated.
        return Math.Min(8,SupportedStrength(s)*Math.Max(0,hits-averageHits)*.55
            *(1+.15*Math.Min(2,VulnerableCoverage(s))))/(1+CardMechanics.Copies(s,card));
    }

    public static RelicAssessment ArchaicTooth(RunSnapshot s)
    {
        var bash=s.Deck.FirstOrDefault(c=>c.ClassName=="Bash");
        if(s.Character!="Ironclad"||bash==null)return new(-100,"","没有可供战士先古替换的痛击。",s.Character=="Ironclad");
        var definition=CardRegistry.All["Break"];
        var target=bash.UpgradeLevel>0?definition.Upgraded:definition.Basic;
        var gain=8+Math.Max(0,Variable(target,"Damage")-Variable(bash,"Damage"))*.55
            +Math.Max(0,Variable(target,"VulnerablePower")-Variable(bash,"VulnerablePower"))*1.6
            +Math.Max(0,bash.Cost-target.Cost)*6+Math.Min(7,VulnerablePayoffs(s)*1.8);
        return new(gain,"","痛击原位替换为破击：费用、伤害、易伤提升及现有联动；继承原升级，未对未知附魔额外加分。");
    }
}
