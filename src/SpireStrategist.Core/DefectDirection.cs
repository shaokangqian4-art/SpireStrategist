namespace SpireStrategist.Core;

// Bounded planning priors from the current deck, not a locked archetype or a future reward forecast.
internal static class DefectDirection
{
    internal static IEnumerable<ScorePart> Adjustments(RunSnapshot s,CardSnapshot c,Func<CardSnapshot,string,bool> has)
    {
        var deck=s.Deck;var name=c.ClassName;
        int Number(params string[] names)=>deck.Count(d=>names.Contains(d.ClassName));
        int Tagged(params string[] tags)=>deck.Count(d=>tags.Any(t=>has(d,t)));
        double Evidence(params (string Name,double Weight)[] values)=>values.Sum(v=>v.Weight*Math.Min(1.5,Number(v.Name)));
        var power=Evidence(("Storm",2),("Subroutine",2),("Thunder",2),("EchoForm",1.5),("CreativeAi",1),("MachineLearning",.5));
        var dark=Evidence(("Darkness",2),("ConsumingShadow",1.5),("ShadowShield",1),("Null",1));
        var physical=DefectComposition.Physical(s);
        var orb=DefectComposition.Orbs(s);
        var status=Tagged("status_source")*.5+Evidence(("Compact",1),("FlakCannon",1.5),("TrashToTreasure",2),("Smokestack",2),("Iteration",1.5));
        var strongest=new[]{power,dark,physical,orb,status}.Max();
        var act=Math.Clamp(s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act,1,3);
        var progress=s.Map is {BossRow:>0} m?Math.Clamp((double)m.CurrentRow/m.BossRow,0,1):.5;
        var horizon=act==1?1-progress*.5:act==2?.35:0;
        var healthy=Math.Min(1,Math.Max(.2,(double)s.Hp/Math.Max(1,s.MaxHp)/.5));
        var copies=Number(name);var parts=new List<ScorePart>();
        void Add(string reason,double value){if(value!=0)parts.Add(new(reason,value));}
        var affinity=name switch {
            "Storm" or "Subroutine" or "Thunder" or "EchoForm" or "MachineLearning"=>power,
            "Darkness" or "ConsumingShadow" or "ShadowShield" or "Null" or "MultiCast" or "Shatter" or "Capacitor"=>dark,
            "AdaptiveStrike" or "Claw" or "AllForOne" or "Scrape" or "Feral" or "BulkUp" or "HelixDrill"=>physical,
            "Compact" or "FlakCannon" or "TrashToTreasure" or "Smokestack" or "Iteration"=>status,
            "Defragment"=>orb,
            // Dark/Frost pieces do not themselves form a Glass engine. Generic
            // Focus support is already scored by DefectPolicy.
            "Spinner"=>Evidence(("Spinner",2),("Glasswork",1),("Defragment",1)),
            _=>0};
        var core=name is "AdaptiveStrike" or "Storm" or "Subroutine" or "Thunder" or "Darkness" or "Defragment" or "Claw" or "Scrape" or "TrashToTreasure" or "Iteration" or "Spinner";
        double investment=0;
        if(copies==0)
        {
            investment=Math.Min(2.8,affinity*.8)*healthy;
            var compatible=!((name is "AdaptiveStrike" or "Claw" or "Scrape")&&DefectDamagePlan.OrbCommitted(s)&&orb>=physical)&&!(name=="Darkness"&&power>=3&&dark==0);
            if(DefectDamagePlan.PhysicalCommitted(s)&&physical>orb
                &&name is "Storm" or "Thunder" or "Darkness" or "Spinner")compatible=false;
            if(core&&compatible)investment+=1.6*horizon*healthy;
        }
        Add("已有组件形成方向，保留有限未来投资",Math.Min(4,investment));
        if(name=="Storm"&&copies==0&&act==1&&DefectComposition.Physical(s)<2)
            Add("前期首张雷暴可建立产电方向，不要求先有能力牌",2.5*horizon*healthy);
        // Native Hailstorm deals a fixed amount once per turn if at least one
        // Frost is held. More Frost/Focus does not multiply that damage.
        if(name=="Hailstorm"&&act>=2)
            Add("冰雹固定伤害不随冰霜数量或集中成长",-2);
        // Echo is a repeat-use engine for both physical and orb decks, not merely
        // another expensive Power. Invest in the first one before all payoffs exist.
        if(name=="EchoForm"&&copies==0
            &&Number("Storm","Subroutine","Thunder","Defragment","Loop","AllForOne")<2
            &&deck.Count(d=>d.Rarity!="Basic"&&d.Type is "Attack" or "Skill" or "Power")>=2
            &&deck.Count(d=>d.Cost<=1&&d.Type is "Attack" or "Skill")>=2)
            Add("缺少长战引擎时投资首张回响，不要求能力流已成型",3*healthy);
        // Physical/orb incompatibility is charged once by DefectComposition.
        if((name is "Darkness" or "ConsumingShadow" or "Null")&&dark==0&&power>=3&&act>=2
            &&DefectComposition.Physical(s)<=DefectComposition.Orbs(s))
            Add("成熟主线不为新黑球支线额外占位",-Math.Min(4,power*.7));
        if(act>=2&&DefectDamagePlan.DarkCommitted(s)&&name is "Storm" or "Thunder")
            Add("黑球蓄积与激发主线已成型，不为无配套产电支线占位",-6);
        var filler=name is "ColdSnap" or "BallLightning" or "FocusedStrike" or "ChargeBattery" or "Leap" or "CompileDriver" or "SweepingBeam" or "MomentumStrike" or "GunkUp" or "RocketPunch" or "Glasswork";
        if(filler&&DefectComposition.HasDamageEngine(s)&&!DefectComposition.BridgeCandidate(s,c))
        {
            double urgency=1;
            if(new[]{"block","frost","frost2","frost3"}.Any(t=>has(c,t))&&(double)s.Hp/Math.Max(1,s.MaxHp)<.35&&Tagged("frost","frost2","frost3")<2)urgency=.4;
            var readiness=DefectStrategy.Readiness(s);
            var coverage=c.Type=="Attack"?readiness.Offense:readiness.Defense;
            Add("方向成型后过渡组件占用启动与抽牌",-Math.Min(4,(act-1)+strongest*.25)*urgency*coverage);
        }
        if(name=="Hologram"&&deck.Any(d=>d.ClassName=="Hologram"&&d.UpgradeLevel>0))Add("已有不消耗全息，新增回收需求递减",physical<3?-1.5:-.5);
        if(name=="Hyperbeam"&&orb>=2)Add("球系启动回合损失集中与被动防线",-Math.Min(4,Tagged("frost","frost2","frost3")*.8+Tagged("focus")*.5));
        if(act>=2&&strongest>=4&&affinity==0)Add("成熟牌组新增卡的估值余量",-.6);
        return parts;
    }
}
