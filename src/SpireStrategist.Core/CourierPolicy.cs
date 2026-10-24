namespace SpireStrategist.Core;

public static class CourierPolicy
{
    // Economy only. Native Foul Potion combat damage also hits the player; do not
    // treat it as a free combat potion or discard useful stock without a destination.
    public static double MerchantOpportunity(RunSnapshot s)
    {
        if(s.Relics.Contains("RELIC.ECTOPLASM"))return 0;
        if(s.Commerce is not {} c)return 0; // Missing route evidence is not a guaranteed shop.
        if(c.ReachableShops>0)return 1d/(1+.08*Math.Max(0,(c.NearestShopDistance??1)-1));
        if(c.FutureActs>0)return .35; // Future map not generated/selected: small prior only.
        return 0; // Unknown room might become a shop; never spend a combat slot on that hope.
    }
    public static bool MayReplace(RunSnapshot s)=>!s.Relics.Contains("RELIC.SOZU")&&MerchantOpportunity(s)>=.6;
    public static EventChoice Describe(EventChoice c,RunSnapshot s,int count)
    {
        var opportunity=MerchantOpportunity(s);
        var empty=s.PotionSlots.Count(p=>p==null);
        var replace=MayReplace(s)?s.PotionSlots.Count(p=>p!=null&&p!="POTION.FOUL_POTION"):0;
        var take=s.Relics.Contains("RELIC.SOZU")?0:Math.Min(count,empty+replace);
        return c with{Benefit=take*7*opportunity,
            Burden=take==0||opportunity==0?0:Math.Max(0,take-empty)*2,
            Notes=[$"后续已知可达商店 {s.Commerce?.ReachableShops.ToString()??"未知"}，未来幕 {s.Commerce?.FutureActs.ToString()??"未知"}；兑现系数 {opportunity:F2}",
                "无已知商店不计确定金币收益；污浊战斗中也会伤害玩家，不按免费群伤估值。无可靠去处不为它丢弃现有药水。"]};
    }
}
