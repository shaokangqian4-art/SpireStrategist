namespace SpireStrategist.Core;

// Character-specific commitments. Utility and status processing do not by themselves
// commit a deck to physical damage; starter Zap likewise does not commit it to orbs.
internal static class DefectComposition
{
    internal static double Physical(RunSnapshot s)=>s.Deck.Sum(c=>c.ClassName switch {
        "Claw"=>2, "AdaptiveStrike"=>3, "AllForOne" or "Scrape" or "Feral"=>2,
        "HelixDrill"=>1.5, "BulkUp"=>.5, _=>0});
    // Damage-direction commitment; shared Frost defense does not select a damage engine.
    internal static double Orbs(RunSnapshot s)=>s.Deck.Sum(c=>c.ClassName switch {
        "Defragment" or "Storm" or "Thunder" or "Voltaic" or "Darkness" or "ConsumingShadow" or "Hailstorm" or "Spinner"=>2,
        "Loop" or "Capacitor" or "IceLance" or "ShadowShield"=>1,
        "Subroutine"=>.5, _=>0});
    internal static bool FavoredCommon(CardSnapshot c)=>c.ClassName is "Claw" or "BeamCell" or "GoForTheEyes"
        or "BoostAway" or "Turbo" or "Coolheaded" or "Hotfix" or "Hologram";
    internal static bool CommonGate(RunSnapshot s,CardSnapshot c)=>c.Rarity=="Common"&&!FavoredCommon(c)
        &&!CopyEngine.Swift(c)
        &&!(c.ClassName=="GunkUp"&&Physical(s)>=3&&s.Deck.Count(d=>d.ClassName=="BulkUp")>=2);
    // A soft marginal penalty, never a rarity-based score ceiling. Support cards
    // do not establish attack/block coverage merely by being non-basic cards.
    internal static IEnumerable<ScorePart> TransitionAdjustments(RunSnapshot s,CardSnapshot c)
    {
        if(!CommonGate(s,c))yield break;
        var ready=DefectStrategy.Readiness(s);
        var attack=c.Type=="Attack";
        var block=c.Variables.GetValueOrDefault("Block")>0||DefectPolicy.Tag(c,"frost")||DefectPolicy.Tag(c,"frost2");
        var need=attack?1-ready.Offense:block?1-ready.Defense:0;
        var act=Math.Clamp(s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act,1,3);
        var copies=s.Deck.Count(d=>d.ClassName==c.ClassName);
        var penalty=(act==1?1.0:act==2?2.5:3.5)*(1-Math.Clamp(need,0,1))
            +Math.Min(1.5,copies*.5);
        if(!attack&&!block)penalty+=1;
        yield return new("过渡牌按实际攻防覆盖递减，不按非基础牌张数封顶",-penalty);
    }
    internal static bool NeedsSecondCompact(RunSnapshot s)
    {
        var burden=s.Deck.Sum(c=>c.Type=="Status"?1:DefectPolicy.Tag(c,"status_source")?(c.ClassName=="FightThrough"?2:1):0);
        var otherClear=s.Deck.Count(c=>c.ClassName=="FlakCannon");
        return s.Deck.Count(c=>c.ClassName=="Compact")==1&&burden>=4+2*otherClear&&burden>=s.Deck.Length*.2;
    }
    internal static bool HasDamageEngine(RunSnapshot s)
    {
        int N(params string[] names)=>s.Deck.Count(c=>names.Contains(c.ClassName));
        var recur=N("Hologram","AllForOne","Scrape","Feral")>0;
        return CopyEngine.Active(s)||DefectDamagePlan.RetrievalLoop(s)||(N("Claw")>=2&&recur)||(N("AdaptiveStrike")>0&&recur)
            ||(s.Deck.Any(c=>c.ClassName=="Voltaic"&&c.UpgradeLevel>0)&&s.Deck.Any(c=>DefectPolicy.Tag(c,"lightning"))&&s.Deck.Count(c=>DefectPolicy.Tag(c,"draw")||DefectPolicy.Tag(c,"energy"))>=2)
            ||N("Darkness","ConsumingShadow")>=2||(N("Darkness","ConsumingShadow")>0&&N("Loop")>0)
            ||(N("Darkness","ConsumingShadow")>0&&N("Null","ShadowShield")>0&&N("Dualcast","Quadcast","MultiCast","Shatter","ConsumingShadow")>0)
            ||(s.Deck.Any(c=>c.ClassName=="Darkness"&&c.UpgradeLevel>0)&&N("Dualcast","Quadcast","MultiCast","Shatter")>0)
            ||(N("Storm")>0&&s.Deck.Count(c=>c.Type=="Power")>=3&&N("Thunder","Defragment")>0)
            ||(N("Spinner")>0&&N("Defragment","BiasedCognition")>0)
            ||(N("FlakCannon")>0&&s.Deck.Count(c=>DefectPolicy.Tag(c,"status_source"))>=2);
    }
    internal static bool NeedsBridgeDamage(RunSnapshot s)
    {
        var act=s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act;
        if(act!=1||HasDamageEngine(s))return false;
        if(s.Map is {BossRow:>0} map&&map.CurrentRow/(double)map.BossRow>=.6)return false;
        var damage=s.Deck.Where(c=>c.Rarity!="Basic"&&c.Type=="Attack")
            .Sum(c=>Math.Min(1.5,(double)c.Variables.GetValueOrDefault("Damage")*Math.Max(1,(double)c.Variables.GetValueOrDefault("Repeat",1))/12));
        damage+=s.Deck.Sum(c=>c.ClassName switch{"BallLightning"=>.7,"Darkness" or "ConsumingShadow" or "Spinner"=>1.3,"ShadowShield" or "Null"=>.6,_=>0});
        return damage<2.5;
    }
    internal static bool BridgeCandidate(RunSnapshot s,CardSnapshot c)=>NeedsBridgeDamage(s)
        &&!s.Deck.Any(d=>d.ClassName==c.ClassName)
        &&(c.ClassName is "BallLightning" or "SweepingBeam" or "FocusedStrike" or "MomentumStrike")
        &&!(Physical(s)>=2&&Physical(s)>Orbs(s)&&c.ClassName=="BallLightning")
        &&!(Orbs(s)>=2&&Orbs(s)>=Physical(s)&&c.ClassName=="MomentumStrike");
    internal static IEnumerable<ScorePart> Adjustments(RunSnapshot s,CardSnapshot c)
    {
        var name=c.ClassName;var physical=Physical(s);var orb=Orbs(s);
        int N(string n)=>s.Deck.Count(d=>d.ClassName==n);
        var claw=N("Claw");var adaptive=N("AdaptiveStrike");
        var firstEngine=name is "Storm" or "Darkness" or "ConsumingShadow" or "Spinner" or "AdaptiveStrike"
            ||(name=="Voltaic"&&s.Deck.Any(d=>DefectPolicy.Tag(d,"lightning")||d.ClassName=="Storm"))
            ||(name=="Thunder"&&(N("Storm")>0||s.Deck.Count(d=>DefectPolicy.Tag(d,"lightning"))>=2));
        if(N(name)==0&&firstEngine&&!HasDamageEngine(s)
            &&!((name is "AdaptiveStrike" or "Claw")&&DefectDamagePlan.OrbCommitted(s)&&orb>=physical)
            &&!((name is "Storm" or "Thunder" or "Darkness" or "ConsumingShadow" or "Spinner" or "Voltaic")&&DefectDamagePlan.PhysicalCommitted(s)&&physical>orb))
            yield return new("尚缺长战输出主干，首张兼容引擎优先于继续堆功能牌",s.Act<=2?3:2);
        if(name=="Voltaic"&&N(name)==0&&!HasDamageEngine(s)&&physical<2
            &&s.Deck.Any(d=>DefectPolicy.Tag(d,"lightning"))
            &&s.Deck.Count(d=>DefectPolicy.Tag(d,"draw")||DefectPolicy.Tag(d,"energy"))>=2
            &&new StrategicValue(s).Remaining>=12&&s.Hp>=s.MaxHp*.35
            &&(c.UpgradeLevel>0||c.CanUpgrade&&!s.Relics.Any(r=>r.Split('.').Last()=="FUSION_HAMMER")))
            yield return new("首张电流相生投资敲后可重复倍增，需种子产电和过牌费用支撑",3);
        var physicalCard=name is "Claw" or "AdaptiveStrike" or "AllForOne" or "Scrape" or "Feral" or "BulkUp" or "HelixDrill" or "BeamCell" or "GoForTheEyes" or "MomentumStrike";
        var orbCard=name is "Defragment" or "Storm" or "Thunder" or "Darkness" or "ConsumingShadow" or "Capacitor" or "Hailstorm" or "Spinner" or "Loop" or "Voltaic" or "MultiCast" or "IceLance" or "Shatter" or "Null";
        // A genuine zero-cost skill retrieval engine is allowed; zero-cost powers/exhaust
        // cards cannot falsely justify All for One in an orb deck.
        var zeroSkills=s.Deck.Count(d=>d.Type=="Skill"&&d.Cost==0&&!d.CostsX&&!d.Keywords.Contains("Exhaust"));
        if(physicalCard&&orb>=2&&orb>=physical&&!(name=="AllForOne"&&zeroSkills>=4))
            yield return DefectDamagePlan.OrbCommitted(s)
                ?new("球系主线不再投资物理组件",-6-Math.Min(5,orb*.5))
                :new("仅有零散球系组件，保留转向成本但不封锁物理核心",-.75);
        if(orbCard&&physical>=2&&physical>orb)
            yield return DefectDamagePlan.PhysicalCommitted(s)
                ?new("物理主线不再投资集中与产球终端",-7-Math.Min(4,physical*.4))
                :new("仅有零散物理组件，保留转向成本但不封锁产球核心",-.75);
        if(DefectDamagePlan.PhysicalCommitted(s)&&physical>orb)
        {
            // Frost block/draw remains shared utility. Temporary focus and a new
            // damage orb are separate investments, not a reason to abandon Claw.
            if(name is "Hotfix" or "Glasswork")
                yield return new("物理输出不为临时集中或玻璃伤害额外占位",-6);
            if(name=="Hyperbeam"&&(claw>=2||adaptive>0))
                yield return new("已有重复物理输出，额外高费群伤挤占循环",-6);
            if(name=="Feral"&&N(name)==0)
            {
                var targets=s.Deck.Count(d=>d.Type=="Attack"&&d.Cost==0&&!d.CostsX);
                if(targets>=2||adaptive>0)
                    yield return new("首张野性回收零费物理核心，每回合一次",4);
            }
            if(name=="BulkUp"&&N(name)<2)
                yield return new("物理主线优先补充力量来源",2);
        }
        if(name=="Smokestack")
        {
            var sources=s.Deck.Count(d=>DefectPolicy.Tag(d,"status_source"));
            var nonBasic=s.Deck.Count(d=>d.Rarity!="Basic"&&d.Type is "Attack" or "Skill" or "Power");
            if(sources<4||sources<nonBasic*.35)
                yield return new("烟囱需要高密度主动造状态，少量功能牌不足以支持",-7);
        }
        if(name=="GunkUp"&&N("BulkUp")<2&&!s.Deck.Any(d=>d.ClassName is "Compact" or "FlakCannon" or "TrashToTreasure"))
            yield return new("污秽攻击缺少增力或状态处理，不用低效产状态补伤害",-3.5);
        if(name=="BulkUp"&&physical<2)yield return new("暴涨以物理收益为主，基础防御不证明需要敏捷引擎",-3);
        if(name=="Claw")
        {
            if(adaptive>0&&claw<3)yield return new("适应复制主线不另起爪群",-9);
            else if(claw>0&&claw<5&&physical>orb)
                yield return new("爪群三至五张共享成长，仍需过牌回收",3+Math.Min(2,claw*.5));
            if(claw>=5)yield return new("五张爪群后优先过牌与防守",-8);
        }
        if(name=="AdaptiveStrike"&&claw>=3&&adaptive==0&&physical>orb)
            yield return new("已有爪群可共用回收并补充复制攻击",2);
        if(physical>=3&&physical>orb&&name is "Hologram" or "Scrape" or "AllForOne" or "Skim" or "Overclock" or "Reboot")
            yield return new("物理引擎需要高密度过牌与回收",N(name)<2?2:0);
        if(name=="Overclock")
        {
            yield return new("零费净过牌支持所有主线",2);
            if(N("Scavenge")>0)yield return new("内存清理可处理超频灼伤",1.2);
        }
        if(name=="DoubleEnergy")
        {
            if(N(name)==0&&s.Act<=2&&new StrategicValue(s).Remaining>=10)
                yield return new("首张双倍能量保留未来成长价值，无需等待成型",2);
            var support=s.Deck.Count(d=>d.Cost>=2||d.CostsX||d.ClassName is "Skim" or "Overclock" or "Coolheaded" or "Hologram");
            var ice=s.Relics.Any(r=>r.Split('.').Last()=="ICE_CREAM");
            yield return new("双倍能量需要过牌和费用出口，支持两至三张",Math.Min(3,support*.5)+(ice?2:0));
            if(N(name)>=3)yield return new("三张翻倍后优先补抽牌和费用出口",-5);
        }
        if(name=="HelixDrill"&&s.Relics.Any(r=>r.Split('.').Last()=="ICE_CREAM"))
            yield return new("冰淇淋留存能量支持螺旋钻击的多段输出",3);
    }
}
