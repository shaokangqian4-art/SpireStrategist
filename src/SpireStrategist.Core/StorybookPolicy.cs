namespace SpireStrategist.Core;

// Acquisition risk, not a command to the combat solver. Native Brightest Flame
// loses two MAX HP on every play, is not Exhaust, and still pays this when upgraded.
public static class StorybookPolicy
{
    public static RelicAssessment Evaluate(RunSnapshot s)
    {
        var v=new StrategicValue(s);
        // A disclosed scenario estimate, not a promise about future rooms/plays.
        var fights=Math.Max(1,v.Remaining*.45);
        var playsPerFight=.75*(1+Math.Min(1,v.Draw/6));
        var loss=2*fights*playsPerFight;
        var projected=s.MaxHp-loss;
        var buffer=Math.Max(40,s.MaxHp*.5);
        var hpCost=loss*(.25+Math.Clamp((100-s.MaxHp)/50d,0,1.5));
        var reserveCost=Math.Max(0,buffer-projected)*.8+Math.Max(0,25-s.Hp)*.25;
        var benefit=10+Math.Min(5,s.Deck.Count(c=>c.Cost>=2||c.CostsX)*.8)-Math.Max(0,s.Energy-3)*1.5;
        return new(benefit-hpCost-reserveCost,Note:
            "至亮之焰每次打出失去2点最大生命，升级不免除代价；按剩余路程、回转频率和血量余量估计累计损耗，不把休息回血当作恢复上限。低上限长流程优先其他先古收益。");
    }
}
