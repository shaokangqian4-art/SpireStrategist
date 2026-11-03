namespace SpireStrategist.Core;

public static class CampAndMushroomPolicy
{
    // Max HP still actually increases by five. Only its strategic reward is
    // discounted: a healthy rest must not manufacture a need for healing.
    public static double HumidifierValue(RunSnapshot s,double maxHpGain)
    {
        var ratio=s.Hp/(double)Math.Max(1,s.MaxHp);
        var factor=ratio<=.4?1:ratio>=.65?.2:1-(ratio-.4)/.25*.8;
        return new StrategicValue(s).MaxHp(maxHpGain)*factor;
    }

    public static EventChoice Mushroom(EventChoice c,RunSnapshot s)
    {
        var protectedOpening=s.Character=="Silent"||s.Relics.Any(r=>r.Split('.').Last()=="BAG_OF_PREPARATION");
        // An explicit conservative threshold, not current missing HP. A wounded
        // 90-max-HP deck is not a low-max-HP deck.
        var dangerousMaximum=s.MaxHp<=45;
        var escapeLethal=s.Hp<=15;
        var eligible=protectedOpening&&dangerousMaximum;
        return c with{MaxHp=20,Burden=eligible?7:24,
            Preference=eligible||escapeLethal?0:-100,
            PreferenceReason="大蘑菇：须准备背包或猎手，且最大生命不高于45；直接致死的另一选项除外",
            Notes=["原生效果：增加20最大生命，但每场战斗首回合少抽2张；后续回合不受影响。",
                eligible?"首回合有补偿且血量上限危险，允许与扣血随机升级比较。":escapeLethal?
                "另一个选项名义上扣15生命，当前不能承受；保留生存退路。":
                "不以当前缺血代替血量上限危险；通常选择扣15生命、随机升级两张。"]};
    }
}
