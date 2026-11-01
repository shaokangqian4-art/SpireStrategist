namespace SpireStrategist.Core;

// A bounded first-component completion premium plus compensation for suppressed
// generic synergy. Requires owned partners; rarity/shared labels are insufficient.
internal static class DefectEngineLinks
{
    internal static double Budget(RunSnapshot s,CardSnapshot c)
    {
        var deck=s.Deck;var name=c.ClassName;
        int N(params string[] names)=>deck.Count(d=>names.Contains(d.ClassName));
        int T(params string[] tags)=>deck.Count(d=>tags.Any(t=>DefectPolicy.Tag(d,t)));
        if(N(name)>0)return 0; // Existing duplicate rules retain control.
        var physical=DefectComposition.Physical(s)>=2&&DefectComposition.Physical(s)>DefectComposition.Orbs(s);
        var powers=deck.Count(d=>d.Type=="Power");
        return name switch
        {
            "Storm" when !physical&&powers>=2&&N("Thunder","Subroutine")>0=>4,
            "Thunder" when !physical&&(N("Voltaic")>0&&T("lightning")>0||N("Storm")>0&&powers>=3)=>4,
            "Voltaic" when !physical&&N("Storm","Thunder")>0&&T("lightning","lightning_engine")>0=>4,
            "Subroutine" when powers>=3&&N("Storm","Thunder","EchoForm")>0=>4,
            "Defragment" when !physical&&T("lightning","frost","frost2","frost3","dark","glass","lightning_engine")>=3=>4,
            "Darkness" when !physical&&N("ConsumingShadow","ShadowShield","Null")>0&&N("Dualcast","Quadcast","MultiCast","Shatter")>0=>3,
            "MultiCast" or "Shatter" when !physical&&N("Darkness")>0=>3,
            "AllForOne" when N("AdaptiveStrike")>0||N("Claw")>=2=>4,
            "Feral" or "Scrape" when physical&&N("Claw")>=2=>3,
            "FlakCannon" when T("status_source")>=2=>4,
            "TrashToTreasure" when T("status_source")>=3&&N("Compact","FlakCannon")>0=>3,
            "SignalBoost" when powers>=3&&N("EchoForm","Defragment","Storm","Thunder")>0&&(s.Energy>=4||T("energy","plasma")>=2)=>3,
            _=>0
        };
    }
}
