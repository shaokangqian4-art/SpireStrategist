namespace SpireStrategist.Core;

// Output intent excludes starting pet cards, blocking and generic soul draw.
public static class NecrobinderDirection
{
    public enum Plan { Undecided, Doom, Souls, Ethereal, Osty }
    public static Plan Analyze(RunSnapshot s)
    {
        if(s.Character!="Necrobinder")return Plan.Undecided;
        var d=s.Deck.Where(c=>!MultiplayerAdvice.Ignore(c)).ToArray();
        int N(string tag)=>d.Count(c=>HeroPolicy.Tag(c,tag)&&!HeroPolicy.Tag(c,"basic"));
        bool H(params string[] ids)=>d.Any(c=>ids.Contains(c.ClassName));
        var scores=new Dictionary<Plan,double>{
            [Plan.Doom]=H("Countdown","NoEscape","Oblivion")?2+N("doom_source")*.7:0,
            [Plan.Souls]=H("Haunt","SoulStorm")?2+N("soul_source")*.8:0,
            [Plan.Ethereal]=H("BansheesCry","PullFromBelow")||H("Eidolon")&&NecrobinderMechanics.HasReplayDamage(s)?2+NecrobinderMechanics.PlayableEthereal(s)*.5:0,
            [Plan.Osty]=PetReady(s)?2+N("osty_attack")*.5:0};
        var ranked=scores.OrderByDescending(x=>x.Value).ToArray();
        return ranked[0].Value>=2.7&&ranked[0].Value>=ranked[1].Value+1?ranked[0].Key:Plan.Undecided;
    }
    public static Plan Output(CardSnapshot c)=>c.ClassName switch{
        "Calcify" or "Rattle" or "Squeeze" or "SicEm"=>Plan.Osty,
        "Countdown" or "NoEscape" or "Oblivion" or "TimesUp"=>Plan.Doom,
        "Haunt" or "SoulStorm"=>Plan.Souls,
        "BansheesCry" or "PullFromBelow" or "Eidolon"=>Plan.Ethereal,_=>Plan.Undecided};
    public static bool OffPlan(RunSnapshot s,CardSnapshot c)
    {
        var plan=Analyze(s);var target=Output(c);
        // Actual cross-line triggers justify a bridge, not the mere existence of tags.
        if(c.ClassName=="Oblivion"&&plan==Plan.Souls&&s.Deck.Count(x=>HeroPolicy.Tag(x,"soul_source"))>=2)return false;
        if(target==Plan.Ethereal&&NecrobinderMechanics.PlayableEthereal(s)>=3&&NecrobinderMechanics.HasReplayDamage(s))return false;
        return plan!=Plan.Undecided&&target!=Plan.Undecided&&target!=plan;
    }
    public static bool PetReady(RunSnapshot s)=>s.Deck.Any(c=>c.ClassName=="Calcify")&&
        s.Deck.Any(c=>c.ClassName is "Rattle" or "Squeeze")&&
        s.Deck.Count(c=>HeroPolicy.Tag(c,"osty_attack")&&!HeroPolicy.Tag(c,"basic"))>=2;
    // Necro Mastery converts Osty's LOST HP into damage; it does not buff attacks.
    // This is a summon/absorption bridge, including soul decks with Devour Life.
    public static bool ReflectionReady(RunSnapshot s)=>s.Deck.Any(c=>c.ClassName=="NecroMastery")&&
        s.Deck.Count(c=>c.ClassName!="NecroMastery"&&HeroPolicy.Tag(c,"summon")&&!HeroPolicy.Tag(c,"basic"))>=2;
    public static double BorrowedX(RunSnapshot s,CardSnapshot c)
    {
        var x=s.Deck.Count(d=>d.CostsX&&!MultiplayerAdvice.Ignore(d));
        return c.ClassName=="BorrowedTime"&&x>0?Math.Min(4,Math.Max(0,HeroPolicy.V(c,"Energy",4)-Math.Max(0,c.Cost)))*Math.Min(1,x*.65):
            c.CostsX&&s.Deck.Any(d=>d.ClassName=="BorrowedTime")?2:0;
    }
}
