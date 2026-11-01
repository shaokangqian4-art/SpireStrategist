namespace SpireStrategist.Core;

// Bounded strategic proxies, not simulated win rates. Keep defense and cycling distinct.
public sealed record DeckReadiness(double Quality,double Offense,double Defense,double Cycling,double Growth)
{
    public double SustainedDefense {get;init;}
    public static DeckReadiness Read(RunSnapshot s)
    {
        if(HeroPolicy.Supports(s.Character))return HeroStrategy.Readiness(s);
        if(s.Character=="Silent")return SilentStrategy.Readiness(s);
        if(s.Character=="Defect")return DefectStrategy.Readiness(s);
        var cards=s.Deck.Select(c=>(Card:c,Profile:CardCatalog.Find(c))).Where(x=>x.Profile!=null).ToArray();
        var known=cards.Length/Math.Max(1d,s.Deck.Length);
        var seen=new Dictionary<string,int>();
        double attack=0,block=0,draw=0,growth=0,exhaust=0,advanced=0,paidBlock=0,automaticBlock=0;
        foreach(var (c,profile) in cards)
        {
            var p=profile!;var copy=seen.GetValueOrDefault(c.ClassName);seen[c.ClassName]=copy+1;
            var repeat=c.ClassName=="BattleTrance"?Math.Pow(.15,copy):Math.Pow(.7,copy);
            var basic=c.ClassName is "StrikeIronclad" or "DefendIronclad" or "Bash";
            attack+=p.Attack*(basic?1:repeat);
            block+=p.Block*(c.Keywords.Contains("Exhaust")?.45:c.ClassName=="CrimsonMantle"?2:1)*(basic?1:repeat);
            if(c.ClassName=="CrimsonMantle")automaticBlock+=IroncladSynergies.Variable(c,"CrimsonMantlePower")*.8*repeat;
            else paidBlock+=p.Block*(c.Keywords.Contains("Exhaust")?.3:1)*(basic?1:repeat);
            draw+=p.Draw*repeat;
            // Fixed strength and max-HP gains are not turn-by-turn combat growth.
            var recurring=c.ClassName switch
            {
                "Inflame" or "SetupStrike" or "Dominate" or "Brand" or "FightMe" or "Feed"=>0,
                "Juggernaut"=>Math.Max(0,IroncladSynergies.BlockTriggers(s)-1)*.4,
                "Rupture"=>Math.Min(p.Scaling,cards.Sum(x=>x.Profile!.SelfDamage)),
                _=>p.Scaling
            };
            growth+=recurring*repeat;
            exhaust+=CardMechanics.ActiveExhaust(p)*repeat;
            if(!basic&&(p.Base>=1||p.Draw+p.Energy>=1||p.ExhaustPayoff>0))advanced+=repeat;
        }
        var hasPain=cards.Any(x=>x.Card.ClassName=="FeelNoPain");
        if(hasPain)
        {
            block+=Math.Min(3,exhaust)*1.4;
            automaticBlock+=Math.Min(2,exhaust)*3;
        }
        if(cards.Any(x=>x.Card.ClassName is "Barricade" or "Unmovable"))growth=Math.Max(growth,Math.Min(3,block*.45));
        if(cards.Any(x=>x.Card.ClassName=="DarkEmbrace"))draw+=Math.Min(2,exhaust);
        // Persistent strength contributes when it can be converted to attacks, but
        // a couple of Inflames cannot alone fill the boss-growth axis to 100%.
        var strengthPayoff=cards.Sum(x=>x.Profile!.StrengthPayoff);
        growth+=Math.Min(1.5,IroncladSynergies.SupportedStrength(s)*.2)*(.4+.6*Math.Clamp(strengthPayoff/2,0,1));
        var offense=Math.Clamp(attack/9,0,1)*known;
        var defense=Math.Clamp(block/7,0,1)*known;
        var cycling=Math.Clamp(draw/3,0,1)*known;
        var scaling=Math.Clamp(growth/3,0,1)*known;
        var quality=Math.Clamp(.1*known+.4*advanced/Math.Max(1,s.Deck.Length)
            +.15*offense+.15*defense+.08*cycling+.12*scaling,0,1);
        // Long fights must draw and pay for defense again. Count automatic block
        // separately; do not treat every block card as available each turn.
        var handBudget=5+Math.Min(2,draw*.5);
        var playable=cards.Where(x=>x.Card.Cost>=0).ToArray();
        var averageCost=playable.Length==0?1:playable.Average(x=>x.Card.CostsX?2:Math.Max(.3,x.Card.Cost));
        var energy=s.Energy+Math.Min(1,cards.Sum(x=>x.Profile!.Energy)*.3);
        var payable=Math.Clamp(energy/Math.Max(3,handBudget*averageCost),.35,1);
        var sustained=Math.Clamp((paidBlock*10*handBudget/Math.Max(10,s.Deck.Length)*payable+automaticBlock)/18,0,1)*known;
        return new(quality,offense,defense,cycling,scaling){SustainedDefense=sustained};
    }
}
