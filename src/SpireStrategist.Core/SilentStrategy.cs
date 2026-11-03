namespace SpireStrategist.Core;

// Hunter-specific retention, upgrade and readiness. Shared executors consume these
// estimates; they are not simulated combat results or borrowed Ironclad scaling.
public static class SilentStrategy
{
    public static bool Basic(CardSnapshot c)=>c.ClassName is "StrikeSilent" or "DefendSilent";
    public static CardProfile? ProfileFor(CardSnapshot c)
    {
        if(!SilentPolicy.Profiles.TryGetValue(c.ClassName,out var p)||c.MultiplayerOnly)return null;
        double V(string k)=> (double)c.Variables.GetValueOrDefault(k);
        bool T(string t)=>p.Tags.Contains(t);
        return new(p.Base,Attack:V("Damage")/10+(T("shiv_source")?.8:0),Block:V("Block")/8+(T("weak")?.3:0),
            Draw:T("draw")?V("Cards")*.4:0,Energy:T("energy")?V("Energy")*.7:0,
            Scaling:T("scaling")?.5:0,StrengthPayoff:T("shiv_source")?1:T("multihit")?.7:0,
            Upgrade:Basic(c)?3:2+3*p.UpgradeDelta,Note:p.Note);
    }
    public static double Upgrade(RunSnapshot s,CardSnapshot c)
    {
        if(ApotheosisPolicy.UpgradeOverride(s,c) is {} ap)return ap;
        if(!c.CanUpgrade||MultiplayerAdvice.Ignore(c)||CardCatalog.HasUnmodelledModification(c))return 0;
        if(SilentMechanics.RedundantRetainUpgrade(s,c))return 0;
        if(!SilentPolicy.Profiles.TryGetValue(c.ClassName,out var p))return DefectNeutral.Upgrade(c)??CardCatalog.Find(c)?.Upgrade??0;
        if(Basic(c))return 3;
        var a=SilentPolicy.Analyze(s);var result=2+3*p.UpgradeDelta;
        if(c.ClassName=="Neutralize")result=8;
        if(c.ClassName=="Survivor")result=4;
        if(c.ClassName is "Accuracy" or "KnifeTrap" or "PhantomBlades")result*=a.Shivs>=1.5?1.3:.55;
        if(c.ClassName=="KnifeTrap")result=SilentMechanics.UpgradedTrap(s)?5:a.Shivs>=1.5?16:4;
        // These two upgrades change knife quality only. Extra knife count, block,
        // draw and cost upgrades elsewhere retain their full independent value.
        if(SilentMechanics.RedundantShivUpgrade(s,c))result*=.35;
        if(c.ClassName is "Accelerant" or "BubbleBubble" or "Mirage")result*=a.PoisonSeeds>0?1.25:.5;
        if(c.ClassName is "Reflex" or "Tactician" or "MasterPlanner")result*=a.Discard>=2?1.2:.5;
        if(c.ClassName=="WellLaidPlans")result=14;
        if(c.ClassName=="CalculatedGamble")result=6; // Retain, NOT removal of Exhaust.
        if(c.ClassName=="Afterimage")result=12; // Innate reliability, not more block per play.
        return Math.Clamp(UpgradeReview.Refine(s,c,result),0,19);
    }
    public static double Remove(RunSnapshot s,CardSnapshot c)
    {
        if(c.ClassName=="AscendersBane"||c.Keywords.Contains("Eternal"))return -100;
        if(c.Type=="Curse")return c.ClassName=="Guilty"?4:c.Keywords.Contains("Ethereal")||c.ClassName=="Clumsy"?4:18;
        if(c.Type=="Status")return 10;
        if(CardCatalog.Find(c)==null||CardCatalog.HasUnmodelledModification(c))return -20;
        var index=Array.FindIndex(s.Deck,d=>ReferenceEquals(c,d));
        if(index<0)index=Array.FindIndex(s.Deck,d=>d.ClassName==c.ClassName&&d.UpgradeLevel==c.UpgradeLevel&&d.Enchantment==c.Enchantment);
        var without=s with{Deck=s.Deck.Where((_,i)=>i!=index).ToArray(),Foresight=null};
        var before=SilentPolicy.Analyze(s);var after=SilentPolicy.Analyze(without);
        var value=Basic(c)?8-c.UpgradeLevel*2:Math.Clamp(-new StrategicValue(without).Add(c),-20,5);
        if(Basic(c)&&!SilentMechanics.CanCleanJunk(s))value+=2;
        if(before.DamageEngines>after.DamageEngines)value-=18;
        if(!Basic(c)&&(SilentPolicy.Tag(c,"shiv_payoff")&&before.PrimaryDamage=="小刀"||SilentPolicy.Tag(c,"poison_seed")&&before.PrimaryDamage=="毒"))value-=8;
        if(c.Type=="Attack"&&without.Deck.Count(d=>d.Type=="Attack")<3&&after.Immediate<2)value-=12;
        if(c.ClassName=="DefendSilent"&&without.Deck.Count(d=>d.Variables.GetValueOrDefault("Block")>0)<3&&after.Defense<2)value-=10;
        if(c.Modified)value-=4;
        if(s.Deck.Length<9)value-=8;
        return value;
    }
    public static DeckReadiness Readiness(RunSnapshot s)
    {
        var a=SilentPolicy.Analyze(s);
        var basics=s.Deck.Count(Basic);
        var known=s.Deck.Count(c=>CardCatalog.Find(c)!=null)/Math.Max(1d,s.Deck.Length);
        var offense=Math.Clamp((a.Immediate+basics*.18)/8,0,1)*known;
        var defense=Math.Clamp((a.Defense+basics*.2)/7,0,1)*known;
        var cycling=Math.Clamp(a.Draw/7,0,1)*known;
        var growth=Math.Clamp(a.DamageEngines>0?.35+Math.Min(.35,a.Shivs*.04+a.Poison*.012)+cycling*.2:0,0,1)*known;
        var advanced=s.Deck.Count(c=>c.Rarity!="Basic"&&c.Type is "Attack" or "Skill" or "Power");
        var quality=Math.Clamp(.1*known+.35*advanced/Math.Max(1d,s.Deck.Length)+.2*offense+.15*defense+.05*cycling+.15*growth,0,1);
        return new(quality,offense,defense,cycling,growth){SustainedDefense=Math.Clamp(defense*(.55+.45*cycling),0,1)};
    }
    public static IReadOnlyDictionary<string,double> NeutralFeatures(RunSnapshot s)
    {
        var a=SilentPolicy.Analyze(s);bool R(string id)=>s.Relics.Any(r=>r.Split('.').Last()==id);
        return new Dictionary<string,double>{
            ["early"]=s.Act==1?1:s.Act==2?.4:0,["recall"]=0,["physical"]=a.PrimaryDamage=="毒"?0:Math.Min(5,a.Shivs+a.Immediate*.4),
            ["rare"]=Math.Min(6,s.Deck.Count(c=>c.Rarity=="Rare")),["junk"]=Math.Min(8,s.Deck.Count(c=>Basic(c)||c.Type is "Curse" or "Status")),
            ["defends"]=Math.Min(6,s.Deck.Count(c=>c.ClassName=="DefendSilent")),["draw"]=Math.Min(5,a.Draw),["cycle"]=Math.Min(5,a.Draw+a.Discard),
            ["goodAttack"]=Math.Min(5,s.Deck.Count(c=>c.Type=="Attack"&&!Basic(c))),["goodSkill"]=Math.Min(4,s.Deck.Count(c=>c.Type=="Skill"&&!Basic(c))),
            ["goodTarget"]=Math.Min(7.5,s.Deck.Count(c=>c.Rarity!="Basic"&&c.Type is "Skill" or "Power")),["energy"]=Math.Min(4,a.Energy+Math.Max(0,s.Energy-3)),
            ["expensive"]=Math.Min(5,s.Deck.Count(c=>c.Cost>=2&&!c.Keywords.Contains("Sly")||c.CostsX)),["frost"]=0,
            ["block"]=Math.Min(4,a.Defense),["debuff"]=Math.Min(3,a.Weak),["attackRatio"]=s.Deck.Count(c=>c.Type=="Attack")/Math.Max(1d,s.Deck.Length),
            ["xCount"]=Math.Min(3,s.Deck.Count(c=>c.CostsX)),["largeDeck"]=Math.Min(12,Math.Max(0,s.Deck.Length-20)),
            ["ice"]=R("ICE_CREAM")?1:0,["pyramid"]=R("RUNIC_PYRAMID")?1:0,["sozu"]=R("SOZU")?1:0,["ghost"]=R("GHOST_SEED")?1:0,
            ["chemical"]=R("CHEMICAL_X")?1:0,["strikeDummy"]=R("STRIKE_DUMMY")?1:0,["fasten"]=0};
    }
}
