namespace SpireStrategist.Core;

// Transparent priors used for event opportunity costs. None are fitted win probabilities.
public sealed class StrategicValue(RunSnapshot state)
{
    public RunSnapshot State {get;}=state;
    public CardProfile[] Profiles=>State.Deck.Select(CardCatalog.Find).OfType<CardProfile>().ToArray();
    public double Draw=>Profiles.Sum(p=>p.Draw);
    public double Exhaust=>Profiles.Sum(p=>p.ExhaustSource);
    public double Strength=>Profiles.Sum(p=>p.Strength);
    public double Multi=>Profiles.Sum(p=>p.StrengthPayoff);
    public double Block=>Profiles.Sum(p=>p.Block);
    public double Attack=>Profiles.Sum(p=>p.Attack);
    public int Basics=>State.Deck.Count(c=>c.ClassName is "StrikeIronclad" or "DefendIronclad" or "StrikeDefect" or "DefendDefect" or "StrikeSilent" or "DefendSilent" or "StrikeRegent" or "DefendRegent" or "StrikeNecrobinder" or "DefendNecrobinder");
    public int Powers=>State.Deck.Count(c=>c.Type=="Power");
    public double Ratio=>State.Hp/Math.Max(1d,State.MaxHp);
    public double Remaining=>State.Map is {} m?Math.Max(1,m.BossRow-m.CurrentRow)+Math.Max(0,m.TotalActs-State.Act)*Math.Max(1,m.Rows):18;
    public double Horizon=>Math.Clamp(Remaining/25, .3, 2.5);
    public bool Has(string id)=>State.Relics.Any(r=>r==id||r=="RELIC."+id);
    public double HpCost(double amount)=>amount*(.55+3*Math.Pow(1-Ratio,2))*(State.NextNodeTypes.Length>0 && State.NextNodeTypes.All(n=>n=="RestSite")?.6:1);
    public double Heal(double amount)=>Math.Min(Math.Max(0,State.MaxHp-State.Hp),amount)*(.55+2.5*Math.Pow(1-Ratio,2))*CardMechanics.HealthyHealingMultiplier(Ratio);
    public double MaxHp(double amount)=>amount*(.8+.45*Horizon);
    public double Gold(double amount)=>Has("ECTOPLASM")&&amount>0?0:amount*.055*(State.Gold<100?1.3:State.Gold>500?.65:1);
    public double Add(CardSnapshot c)=>new Policy().Evaluate(State,[new("c",c.Name,"card",Card:c)]).Options[0].Score??-12;
    public double Upgrade(CardSnapshot c)=>CardMechanics.Upgrade(State,c)*.65;
    public double Remove(CardSnapshot c)
    {
        if(HeroPolicy.Supports(State.Character))return HeroStrategy.Remove(State,c);
        if(State.Character=="Silent")return SilentStrategy.Remove(State,c);
        if(State.Character=="Defect")return DefectStrategy.Remove(State,c);
        if(c.ClassName=="AscendersBane"||c.Keywords.Contains("Eternal"))return -100;
        if(c.Type=="Curse" && c.ClassName=="Guilty")
            return 4*Math.Clamp((double)c.Variables.GetValueOrDefault("Combats",5)/5,0,1);
        if(c.Type=="Curse")return c.ClassName is "Guilt" or "Clumsy" || c.Keywords.Contains("Ethereal") || c.Keywords.Contains("Retain") ? 4 : 18;
        if(c.Type=="Status")return 10;
        var p=CardCatalog.Find(c);if(p==null)return -12;
        if(c.Modified)return -4;
        var basic=c.ClassName is "StrikeIronclad" or "DefendIronclad" or "StrikeDefect" or "DefendDefect" or "StrikeSilent" or "DefendSilent" or "StrikeRegent" or "DefendRegent" or "StrikeNecrobinder" or "DefendNecrobinder";
        var value=basic?8+Math.Min(4,(State.Deck.Length-10)*.4)-c.UpgradeLevel*2:Math.Clamp(-Add(c),-16,5);
        if(c.Type=="Attack"&&Attack-p.Attack<2)value-=12;
        if(c.ClassName=="DefendIronclad"&&Block-p.Block<2)value-=10;
        if(Has("GHOST_SEED")&&basic)value-=1.5;
        return value;
    }
    public double RemoveBest(int n)=>State.Deck.Select(Remove).OrderDescending().Take(n).Sum();
    public double UpgradeBest(int n)=>State.Deck.Select(Upgrade).OrderDescending().Take(n).Sum();
    public double RandomUpgrade(int n)
    {var a=State.Deck.Where(c=>c.CanUpgrade).Select(Upgrade).ToArray();return a.Length==0?0:a.Average()*Math.Min(n,a.Length);}
    // Native random transforms stay in the original pool. A curse becomes another curse,
    // so it cannot receive the benefit of deleting a curse without replacement.
    public double Transform(CardSnapshot c)=>c.Type is "Curse" or "Status"?-5:Remove(c)*.65+1;
    public double TransformBest(int n)=>State.Deck.Select(Transform).OrderDescending().Take(n).Sum();
    public double Potion(int n)=>Has("SOZU")?0:Math.Min(n,State.PotionSlots.Count(p=>p==null))*5;
    public double Energy=>13+Math.Min(8,State.Deck.Count(c=>c.Cost>=2)*1.2)-Math.Max(0,State.Energy-3)*3;
    public double Curse(string name="")=>(name is "Guilt"?5:name is "Greed" or "CurseOfTheBell"?19:12)
        +(State.Character=="Silent"&&!SilentMechanics.CanCleanJunk(State)?3:0);
    public double EnthralledCost()
    {
        // Enthralled blocks normal card plays while in hand and is Eternal. Hand
        // exhaust cards cannot be played to clear it; Compact only handles statuses.
        // Toasty Mittens selects a card automatically before normal plays instead.
        if(Has("TOASTY_MITTENS"))return 9;
        return 22+Math.Max(0,24-State.Deck.Length)*.9+Math.Min(6,Draw*1.5)+(Has("RUNIC_PYRAMID")?8:0);
    }
    private double BlockEnchant(CardSnapshot c,double amount)
    {
        var once=c.Type=="Power"||c.Keywords.Contains("Exhaust")||c.Keywords.Contains("Ethereal");
        var basic=c.ClassName is "StrikeIronclad" or "DefendIronclad" or "StrikeDefect" or "DefendDefect" or "StrikeSilent" or "DefendSilent" or "StrikeRegent" or "DefendRegent" or "StrikeNecrobinder" or "DefendNecrobinder";
        // Compare expected uses of the added block, not the original block amount.
        // Both event previews and the actual target selector call this evaluator.
        return amount*.9*(once?.65:1.25)*(c.Cost<=1?1.2:.85)*(basic?.65:1)
            +(c.Cost==0&&!c.CostsX?.35:0)+Math.Clamp(Add(c),0,10)*.1;
    }
    public double Enchant(CardSnapshot c,string name,double amount=1)
    {
        var p=CardCatalog.Find(c);if(p==null||c.Modified)return -10;
        if(c.Type is "Curse" or "Status"||c.Keywords.Contains("Unplayable"))return -10;
        if(EnchantmentValue.Evaluate(this,c,name,amount,p) is {} modeled)return modeled;
        return name switch
        {
            // Adroit adds block whenever the card is played, including attacks without
            // native block. Favor inexpensive reusable useful cards over soon-removed basics.
            "ADROIT" => BlockEnchant(c,amount),
            "NIMBLE" => p.Block>0?BlockEnchant(c,amount):-10,
            // Goopy changes the card to Exhaust and grows across combats; it is
            // deliberately not treated as ordinary repeatable added block.
            "GOOPY" => p.Block>0?amount*.9+Exhaust:-10,
            "SOULS_POWER" => c.Keywords.Contains("Exhaust")?Math.Max(0,Add(c))*.8-Exhaust:-10,
            "SLITHER" => c.CostsX?-15:(c.Cost-1.5)*6,
            "SPIRAL" => Math.Max(1,p.Attack*5+p.Block*4),
            "CLONE" => Math.Max(-5,Add(c)-2)*Math.Min(2,Horizon),
            "PERFECT_FIT" => c.Type=="Power"||c.Keywords.Contains("Exhaust")||c.Keywords.Contains("Ethereal") ? 0 : Math.Max(-5,Add(c)-2),
            _ => -10
        };
    }
    public double BestEnchant(string name,double amount=1,int count=1)=>State.Deck.Select(c=>Enchant(c,name,amount)).Where(v=>v>-10).OrderDescending().Take(count).Sum();
}
