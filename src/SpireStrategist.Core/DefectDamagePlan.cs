namespace SpireStrategist.Core;

// Distinguish a tentative component from a working damage plan. Draw, energy,
// Frost defense and attack count are not evidence that boss damage is solved.
internal static class DefectDamagePlan
{
    internal static bool DarkCommitted(RunSnapshot s)=>s.Deck.Any(c=>c.ClassName is "Darkness" or "ConsumingShadow")
        &&s.Deck.Any(c=>c.ClassName is "MultiCast" or "Quadcast" or "Shatter")
        &&!s.Deck.Any(c=>c.ClassName is "Storm" or "Thunder" or "Voltaic");
    internal static bool PhysicalCommitted(RunSnapshot s)
    {
        int N(params string[] names)=>s.Deck.Count(c=>names.Contains(c.ClassName));
        return RetrievalLoop(s) || CopyEngine.Active(s)
            || N("AdaptiveStrike")>0&&N("Hologram","AllForOne","Scrape","Feral","BulkUp")>0 || N("Claw")>=2
            || N("Claw")>0&&N("Hologram","AllForOne","Scrape","Feral","BulkUp")>0
            || N("HelixDrill")>0&&N("DoubleEnergy","Turbo","Supercritical","BulkUp")>0;
    }
    internal static bool RetrievalLoop(RunSnapshot s)=>s.Deck.Any(c=>c.ClassName=="AllForOne")
        &&s.Deck.Any(c=>c.ClassName=="Hologram"&&c.UpgradeLevel>0)
        &&s.Deck.Any(c=>c.ClassName=="Turbo")
        &&s.Deck.Any(c=>c.Type=="Attack"&&c.Cost==0&&!c.CostsX&&!c.Keywords.Contains("Exhaust"));
    internal static string Summary(RunSnapshot s)=>PhysicalCommitted(s)
        ?"输出规划：物理核心已有投入；优先补回收、力量与启动，冰霜防守仍可共用。"
        :OrbCommitted(s)?"输出规划：球系核心已有配套；优先补集中、产球与对应终端。"
        :"输出规划：尚未建立稳定主线；运转与过渡攻击不能代替输出核心。";
    internal static bool OrbCommitted(RunSnapshot s)
    {
        int N(params string[] names)=>s.Deck.Count(c=>names.Contains(c.ClassName));
        var anchors=N("Storm","Thunder","Voltaic","Darkness","ConsumingShadow","Spinner");
        return anchors>=2 || anchors>0&&N("Defragment","BiasedCognition")>0
            || N("Darkness","ConsumingShadow")>0&&N("Null","ShadowShield","Loop","MultiCast","Shatter","Quadcast")>0
            || N("Storm")>0&&s.Deck.Count(c=>c.Type=="Power")>=3;
    }
    internal static IEnumerable<ScorePart> Adjustments(RunSnapshot s,CardSnapshot c)
    {
        int N(params string[] names)=>s.Deck.Count(d=>names.Contains(d.ClassName));
        var act=Math.Clamp(s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act,1,3);
        var physical=PhysicalCommitted(s);var orb=OrbCommitted(s);
        var copies=N(c.ClassName);var engine=DefectComposition.HasDamageEngine(s);
        // These are alternative short-fight damage tools, not three distinct needs.
        // Preserve FTL with repeatable zero-cost retrieval and Null as a dark seed.
        var bridge=c.ClassName is "Ftl" or "Sunder" or "Null";
        var supported=c.ClassName=="Ftl"&&physical&&N("AllForOne","Feral")>0
            ||c.ClassName=="Null"&&!physical&&N("Darkness","ConsumingShadow")>0;
        if(bridge&&!supported)
        {
            var coverage=s.Deck.Count(d=>d.Rarity!="Basic"&&d.Type=="Attack");
            var burden=act==1?Math.Max(0,coverage-1)*.9:1.5+(act-2)*1.5+Math.Min(2,coverage*.6);
            if(engine)burden+=1;
            if(burden>0)yield return new("过渡攻击共用补伤害预算，不能逐张当成新缺口",-Math.Min(6,burden));
        }
        if(c.ClassName=="AdaptiveStrike"&&copies==0&&!orb)
        {
            var draw=s.Deck.Count(d=>DefectPolicy.Tag(d,"draw")||d.ClassName=="Reboot");
            var recall=N("Hologram","Feral","Scrape");
            yield return new("适应打击自身生成零费复制输出，现有过牌与回收帮助兑现",2+Math.Min(3,draw*.6+recall*.8+N("Scavenge")*.5));
        }
        // Credit completing an actual engine, rather than all broadly related tags.
        if(copies==0&&!engine&&DefectComposition.HasDamageEngine(s with{Deck=[..s.Deck,c]})
            &&!(orb&&c.ClassName is "AdaptiveStrike" or "Claw")
            &&!(physical&&c.ClassName is "Storm" or "Thunder" or "Voltaic" or "Darkness" or "ConsumingShadow" or "Spinner"))
            yield return new("这张牌补齐现有输出引擎，而非继续增加辅助组件",3);
        // Do not punish first defensive/utility tools or blindly buy an unusable core.
        // Only repeated role investment is discounted when there is still no engine.
        if(!engine&&s.Deck.Count(d=>d.Rarity!="Basic"&&d.Type is "Attack" or "Skill" or "Power")>=5
            &&c.ClassName is "Reboot" or "Hologram" or "MachineLearning" or "Turbo" or "DoubleEnergy" or "Scavenge")
        {
            var utility=N("Reboot","Hologram","MachineLearning","Turbo","DoubleEnergy","Scavenge","Skim","Overclock");
            if(utility>=3)yield return new("输出主干未建立，已有多张运转供能不再重复占位",-Math.Min(3,(utility-2)*.75));
        }
    }
}
