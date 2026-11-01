namespace SpireStrategist.Core;

// Shared Ironclad/Defect policy. Native Apotheosis is Innate, Exhaust, 2 -> 1 energy.
// It upgrades combat copies, not the persistent deck. This is a camp opportunity
// cost preference, never permission to change the saved upgrades or combat state.
public static class ApotheosisPolicy
{
    public static bool Owned(RunSnapshot s)=>s.Deck.Any(c=>c.ClassName=="Apotheosis"&&!MultiplayerAdvice.Ignore(c));
    public static bool Finished(RunSnapshot s)=>Owned(s)&&!s.Deck.Any(c=>c.ClassName=="Apotheosis"&&c.CanUpgrade&&!MultiplayerAdvice.Ignore(c));
    public static int PendingCores(RunSnapshot s)
    {
        var without=s with{Deck=s.Deck.Where(c=>c.ClassName!="Apotheosis").ToArray()};
        return without.Deck.Count(c=>c.CanUpgrade&&c.UpgradeLevel==0&&c.Rarity!="Basic"
            &&c.Type is "Attack" or "Skill" or "Power"&&!MultiplayerAdvice.Ignore(c)
            &&CardMechanics.Upgrade(without,c)>=12);
    }
    public static double Value(RunSnapshot s)
    {
        if(Owned(s))return -20;
        var cores=PendingCores(s);
        // Up to three important upgrades can usually be handled by existing camps.
        // This is a configurable-in-code prior, not a promise of three reachable camps.
        return cores<=3?-6+cores:Math.Min(24,9+(cores-4)*3);
    }
    public static string Reason(RunSnapshot s)=>$"神化：待敲关键牌 {PendingCores(s)} 张，3张以内低需求，4张起提高整组升级价值";
    public const string CampReason="神化已完成升级，火堆不再投资其他卡牌升级";
    public const double CoveredSmithScore=-100;
    public static double? UpgradeOverride(RunSnapshot s,CardSnapshot c)
    {
        if(c.ClassName=="Apotheosis")return c.CanUpgrade&&!MultiplayerAdvice.Ignore(c)?20:0;
        return Owned(s)?0:null;
    }
    public static OptionScore Score(RunSnapshot s,DecisionOption o,CardSnapshot c)
    {
        if(CardCatalog.HasUnmodelledModification(c))return new(o.Key,o.Name,false,o.Enabled,null,[],["附魔／异变未建模，不能据此推荐。"]);
        var value=Value(s);
        // Already upgraded reduces startup cost, but cannot manufacture upgrade demand.
        if(c.UpgradeLevel>0&&value>0)value+=1;
        return new(o.Key,o.Name,true,o.Enabled,value,[new(Reason(s),value)],
            ["拿到神化后优先升级神化；其余火堆行动按恢复和遗物收益选择。"]);
    }
}
