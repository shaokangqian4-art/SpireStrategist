namespace SpireStrategist.Core;

public static class HeroStrategy
{
    public static bool Basic(CardSnapshot c)=>c.ClassName is "StrikeRegent" or "DefendRegent" or "StrikeNecrobinder" or "DefendNecrobinder";
    public static CardProfile? ProfileFor(CardSnapshot c)
    {
        if(!HeroPolicy.Profiles.TryGetValue(c.ClassName,out var p)||MultiplayerAdvice.Ignore(c))return null;
        double V(string k)=>HeroPolicy.V(c,k);bool T(string t)=>p.Tags.Contains(t);
        var damage=Math.Max(V("Damage"),V("OstyDamage"));
        return new(p.Base,Attack:Math.Min(3,damage/10),Block:V("Block")/8,
            Draw:T("draw")?V("Cards")*.4:0,Energy:T("energy")?Math.Min(2,V("Energy"))*.6:0,
            Scaling:T("scaling")?.5:0,StrengthPayoff:T("osty_attack")?0:T("multihit")?1:0,
            ExhaustSource:T("cleanup")?1:0,Upgrade:Basic(c)?3:2+3*p.UpgradeDelta,Note:p.Note);
    }
    public static double Upgrade(RunSnapshot s,CardSnapshot c)
    {
        if(ApotheosisPolicy.UpgradeOverride(s,c) is {} ap)return ap;
        if(!c.CanUpgrade||MultiplayerAdvice.Ignore(c)||CardCatalog.HasUnmodelledModification(c))return 0;
        if(!HeroPolicy.Profiles.TryGetValue(c.ClassName,out var p))return DefectNeutral.Upgrade(c)??CardCatalog.Find(c)?.Upgrade??0;
        if(Basic(c))return 3;
        var a=HeroPolicy.Analyze(s);var value=2+3*p.UpgradeDelta;
        bool T(string t)=>p.Tags.Contains(t);
        if(T("sword_payoff")&&a.Forge==0||T("soul_payoff")&&a.Souls==0||T("ethereal_payoff")&&NecrobinderMechanics.PayoffSupply(s,c)<2||T("doom_payoff")&&a.Doom<8)value*=.45;
        if(c.ClassName is "SwordSage" or "Demesne" or "SentryMode" or "Pagestorm" or "MonarchsGaze")value+=2;
        if(T("forge")&&a.Primary=="铸剑"||T("star_source")&&a.StarDemand>a.Stars||T("osty_engine")&&a.OstyAttacks>=3||T("doom_engine")&&a.Doom>=15)value+=2;
        if(T("basic"))value=Math.Min(value,5); // Resource starters are not automatic top smith targets.
        if(s.Deck.Any(d=>d.ClassName==c.ClassName&&d.UpgradeLevel>0))value*=.8;
        if(s.Character=="Regent"&&c.Type=="Attack"&&s.Relics.Any(r=>r.Split('.').Last()=="MINIATURE_CANNON"))
            value+=Math.Min(6,RegentAttackLinks.Hits(s,c)*1.2);
        return Math.Clamp(UpgradeReview.Refine(s,c,value),0,19);
    }
    public static double Remove(RunSnapshot s,CardSnapshot c)
    {
        if(c.ClassName=="AscendersBane"||c.Keywords.Contains("Eternal"))return -100;
        if(c.Type=="Curse")return c.Keywords.Contains("Ethereal")||c.ClassName is "Guilty" or "Clumsy"?4:18;
        if(c.Type=="Status")return 10;
        if(CardCatalog.Find(c)==null||CardCatalog.HasUnmodelledModification(c))return -20;
        var index=Array.FindIndex(s.Deck,d=>ReferenceEquals(c,d));
        if(index<0)index=Array.FindIndex(s.Deck,d=>d.ClassName==c.ClassName&&d.UpgradeLevel==c.UpgradeLevel&&d.Enchantment==c.Enchantment);
        var without=s with{Deck=s.Deck.Where((_,i)=>i!=index).ToArray(),Foresight=null};
        var before=HeroPolicy.Analyze(s);var after=HeroPolicy.Analyze(without);
        var value=Basic(c)?9-c.UpgradeLevel*2:Math.Clamp(-new StrategicValue(without).Add(c),-20,5);
        if(before.DamageEngines>after.DamageEngines)value-=18;
        if(HeroPolicy.Tag(c,"forge")&&after.Forge==0||HeroPolicy.Tag(c,"soul_source")&&after.Souls==0&&before.Primary=="灵魂")value-=10;
        if(c.ClassName=="Venerate"&&after.Stars<after.StarDemand)value-=12;
        if(c.ClassName=="Bodyguard"&&before.OstyAttacks>=2&&after.Summon<5)value-=12;
        if(c.Type=="Attack"&&after.Immediate<2&&without.Deck.Count(d=>d.Type=="Attack")<3)value-=12;
        if(c.ClassName.StartsWith("Defend")&&after.Defense<2&&without.Deck.Count(d=>d.Variables.GetValueOrDefault("Block")>0)<3)value-=10;
        if(c.Modified)value-=4;if(s.Deck.Length<9)value-=8;
        return value;
    }
    public static DeckReadiness Readiness(RunSnapshot s)
    {
        var a=HeroPolicy.Analyze(s);var known=s.Deck.Count(c=>CardCatalog.Find(c)!=null)/Math.Max(1d,s.Deck.Length);
        var offense=Math.Clamp(a.Immediate/8,0,1)*known;var defense=Math.Clamp(a.Defense/7,0,1)*known;
        var cycling=Math.Clamp(a.Draw/7,0,1)*known;
        var growth=a.DamageEngines==0?0:Math.Clamp(.35+a.DamageEngines*.1+cycling*.2,0,1)*known;
        var quality=Math.Clamp(.1*known+.3*s.Deck.Count(c=>c.Rarity!="Basic"&&c.Type is "Attack" or "Skill" or "Power")/Math.Max(1d,s.Deck.Length)+.25*offense+.15*defense+.05*cycling+.15*growth,0,1);
        return new(quality,offense,defense,cycling,growth){SustainedDefense=defense*(.55+.45*cycling)};
    }
    public static IReadOnlyDictionary<string,double> NeutralFeatures(RunSnapshot s)
    {
        var a=HeroPolicy.Analyze(s);int N(Func<CardSnapshot,bool> f)=>s.Deck.Count(f);bool R(string id)=>s.Relics.Any(r=>r.Split('.').Last()==id);
        return new Dictionary<string,double>{
            ["early"]=s.Act==1?1:s.Act==2?.4:0,["recall"]=N(c=>HeroPolicy.Tag(c,"recall")),
            ["physical"]=Math.Min(5,N(c=>c.Type=="Attack"&&!HeroPolicy.Tag(c,"osty_attack")&&!Basic(c))),
            ["rare"]=Math.Min(6,N(c=>c.Rarity=="Rare")),["junk"]=Math.Min(8,N(c=>Basic(c)||c.Type is "Curse" or "Status")),
            ["defends"]=N(c=>c.ClassName=="Defend"+s.Character),["draw"]=Math.Min(5,a.Draw),["cycle"]=Math.Min(5,a.Draw),
            ["goodAttack"]=Math.Min(5,N(c=>c.Type=="Attack"&&!Basic(c))),["goodSkill"]=Math.Min(4,N(c=>c.Type=="Skill"&&!Basic(c))),
            ["goodTarget"]=Math.Min(7.5,N(c=>c.Rarity!="Basic"&&c.Type is "Skill" or "Power")),["energy"]=Math.Min(4,a.Energy+Math.Max(0,s.Energy-3)),
            ["expensive"]=Math.Min(5,N(c=>c.Cost>=2||c.CostsX)),["frost"]=0,["block"]=Math.Min(4,a.Defense),
            ["debuff"]=Math.Min(3,N(c=>HeroPolicy.Tag(c,"weak")||HeroPolicy.Tag(c,"vulnerable")||HeroPolicy.Tag(c,"doom_source"))),
            ["attackRatio"]=N(c=>c.Type=="Attack")/Math.Max(1d,s.Deck.Length),["xCount"]=Math.Min(3,N(c=>c.CostsX)),
            ["largeDeck"]=Math.Min(12,Math.Max(0,s.Deck.Length-20)),["ice"]=R("ICE_CREAM")?1:0,["pyramid"]=R("RUNIC_PYRAMID")?1:0,
            ["sozu"]=R("SOZU")?1:0,["ghost"]=R("GHOST_SEED")?1:0,["chemical"]=R("CHEMICAL_X")?1:0,["strikeDummy"]=R("STRIKE_DUMMY")?1:0,["fasten"]=0};
    }
}
