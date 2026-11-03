namespace SpireStrategist.Core;

public static class NecrobinderMechanics
{
    public static bool Playable(CardSnapshot c)=>c.Type is "Attack" or "Skill" or "Power"
        &&!c.Keywords.Contains("Unplayable");
    public static int PlayableEthereal(RunSnapshot s)=>s.Deck.Count(c=>Playable(c)&&c.Keywords.Contains("Ethereal"))
        +s.Deck.Count(c=>HeroPolicy.Tag(c,"ethereal_source"));

    public static int PayoffSupply(RunSnapshot s,CardSnapshot payoff)=>payoff.ClassName=="Pagestorm"
        ?s.Deck.Count(c=>c.Keywords.Contains("Ethereal"))+s.Deck.Count(c=>HeroPolicy.Tag(c,"ethereal_source"))
        :PlayableEthereal(s);

    // Replaying defenses/buffs is useful, but must not create a new damage engine
    // (and therefore a seven-point foresight bonus) without a damage-bearing target.
    public static bool HasReplayDamage(RunSnapshot s)
    {
        bool Output(CardSnapshot c)=>Playable(c)&&(c.Type=="Attack"&&!HeroStrategy.Basic(c)
            ||HeroPolicy.Tag(c,"doom_source")||HeroPolicy.Tag(c,"soul_engine")||HeroPolicy.Tag(c,"osty_engine"));
        return s.Deck.Any(c=>c.Keywords.Contains("Ethereal")&&Output(c))
            ||s.Deck.Any(c=>HeroPolicy.Tag(c,"ethereal_source"))&&s.Deck.Any(Output);
    }
}
