namespace SpireStrategist.Core;

// Acquisition intent comes from owned output, never from future rewards or resource
// supply alone. Recomputed each decision: a genuine mixed deck is not forcibly locked.
public static class RegentDirection
{
    public enum Plan { Undecided, Sword, Stars, Generation, StrengthAttacks }
    public sealed record Direction(double SwordInvestment,double StarInvestment,int StarOutputs,Plan Committed);
    public static double StarOutput(CardSnapshot c)
    {
        if(MultiplayerAdvice.Ignore(c)||HeroPolicy.Tag(c,"basic")||HeroPolicy.Tag(c,"star_source"))return 0;
        if(HeroPolicy.Tag(c,"star_engine")||HeroPolicy.Tag(c,"star_finisher")||HeroPolicy.Tag(c,"star_payoff"))return 1.75;
        if(!HeroPolicy.Tag(c,"attack")||HeroPolicy.Stars(c)<=0)return 0;
        // These attacks also solve draw/defense problems in a sword deck.
        return c.ClassName is "GuidingStar" or "DyingStar" or "MeteorShower" or "GammaBlast"?.5:1;
    }
    public static bool SwordCore(CardSnapshot c)=>c.ClassName is "SwordSage" or "Conqueror" or "BeatIntoShape" or "TheSmith";
    public static bool SwordOpportunity(RunSnapshot s)=>s.Deck.Any(SwordCore)||
        !s.IsMultiplayer&&s.Foresight is {SourceUnchanged:true} f&&f.Branches.Any(b=>b.Rewards.Take(3).Any(p=>p.Reliability>=.6&&p.Cards.Where((c,i)=>p.Room!="Shop"||p.Prices is {} prices&&i<prices.Length&&prices[i]<=s.Gold).Any(SwordCore)));
    private static double SwordInvestment(CardSnapshot c)
    {
        if(MultiplayerAdvice.Ignore(c))return 0;
        return c.ClassName switch{
            "Furnace" or "BeatIntoShape" or "TheSmith"=>2,
            "SwordSage"=>1.75,
            "SummonForth" or "Conqueror" or "SeekingEdge"=>1.5,
            "Parry" or "RefineBlade" or "WroughtInWar"=>.75,
            "Bulwark" or "SpoilsOfBattle"=>.4,
            _=>0}; // Big Bang and copying arbitrary colorless cards do not select sword.
    }
    public static Direction Analyze(RunSnapshot s)
    {
        if(s.Character!="Regent")return new(0,0,0,Plan.Undecided);
        var deck=s.Deck.Where(c=>!MultiplayerAdvice.Ignore(c)).ToArray();
        var sword=deck.Sum(SwordInvestment);var star=deck.Sum(StarOutput);
        var outputs=deck.Count(c=>StarOutput(c)>0);
        var supplied=deck.Any(c=>HeroPolicy.Tag(c,"star_source")&&!HeroPolicy.Tag(c,"basic"));
        var generation=deck.Count(c=>HeroPolicy.Tag(c,"generation")&&c.ClassName!="BigBang");
        // An invested generator package is already a direction, even before a
        // dedicated damage payoff arrives. One incidental generated card isn't.
        var generationCore=deck.Any(c=>c.ClassName is "Supermassive" or "Arsenal")||
            deck.Any(c=>c.ClassName=="SpectrumShift")&&deck.Where(c=>HeroPolicy.Tag(c,"generation")&&c.ClassName is not ("BigBang" or "HeirloomHammer")).Select(c=>c.ClassName).Distinct().Count()>=2;
        var genScore=(generationCore?3:0)+generation*.8;
        var forged=deck.Any(c=>HeroPolicy.Tag(c,"forge")&&SwordInvestment(c)>=.75);
        var plan=generationCore&&generation>=2&&genScore>star+1&&genScore>sword+1?Plan.Generation:
            (outputs>=3&&star>=3||outputs>=2&&star>=3.5&&supplied)&&star>=sword+1.5&&star>=sword*1.6?Plan.Stars:
            deck.Any(SwordCore)&&forged&&sword>=3.5&&sword>=star+1.5&&sword>=star*1.6?Plan.Sword:Plan.Undecided;
        // A real strength outlet can already be the main engine while star cards
        // provide block/energy. One incidental forge card must not vote it away.
        if(OwnedAttackPlan.StrengthCore(s)&&sword<3.5&&star<5)plan=Plan.StrengthAttacks;
        return new(sword,star,outputs,plan);
    }
    public static bool SwordOnly(CardSnapshot c)=>c.ClassName is
        "Furnace" or "BeatIntoShape" or "TheSmith" or "SwordSage" or "SummonForth" or
        "Conqueror" or "SeekingEdge" or "Parry" or "RefineBlade";
    public static bool StarOnly(CardSnapshot c)=>StarOutput(c)>0&&
        c.ClassName is not ("Stardust" or "GuidingStar" or "DyingStar" or "MeteorShower" or "GammaBlast");
    public static bool OffPlan(Direction d,CardSnapshot c)=>
        d.Committed is Plan.Stars or Plan.Sword && c.ClassName=="Supermassive" ||
        d.Committed is Plan.Stars or Plan.Generation or Plan.StrengthAttacks&&SwordOnly(c)||d.Committed is Plan.Sword or Plan.Generation&&StarOnly(c);
    public static int RelevantEngines(HeroPolicy.Composition a,Direction d)=>a.DamageEngines-
        (d.Committed is Plan.Stars or Plan.StrengthAttacks?a.SwordEngines:d.Committed==Plan.Sword?a.StarEngines:d.Committed==Plan.Generation?a.SwordEngines+a.StarEngines:0);

    public static bool ConnectsOwned(RunSnapshot s,CardSnapshot c,Direction direction)
    {
        if(s.Character!="Regent")return true;
        if(OffPlan(direction,c))return false;
        if(SwordOnly(c))return s.Deck.Any(d=>SwordOnly(d)&&d.ClassName!=c.ClassName);
        return true;
    }
}
