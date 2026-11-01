namespace SpireStrategist.Core;

// A bounded investment bonus based on conditional, reachable reward branches.
// Alternatives in one reward are mutually exclusive; unrelated branches never sum.
public static class DefectForesight
{
    public static void PromoteCompletedEngine(RunSnapshot s,IReadOnlyList<DecisionOption> options,OptionScore[] scores)
    {
        // Free acquisition only: neither shop budgets nor removal/transform choices
        // inherit this priority. Do not invent a missing second component.
        if(options.Count(o=>o.Enabled&&o.Kind=="card")<2||s.Character!="Defect"||DefectComposition.HasDamageEngine(s)||s.Hp<=s.MaxHp*.25||!options.Any(o=>o.Kind=="skip"&&o.Enabled))return;
        var eligible=options.Where(o=>o.Enabled&&o.Kind=="card"&&o.Card is {} c&&c.Cost<=s.Energy&&!c.CostsX
            &&c.ClassName is "AdaptiveStrike" or "Claw" or "AllForOne" or "Storm" or "Thunder" or "Voltaic" or "Darkness" or "ConsumingShadow" or "Spinner" or "FlakCannon")
            .Where(o=>!s.Deck.Any(c=>c.ClassName==o.Card!.ClassName)||o.Card!.ClassName=="Claw")
            .Where(o=>DefectComposition.HasDamageEngine(s with{Deck=[..s.Deck,o.Card!]}))
            .Where(o=>!(DefectDamagePlan.OrbCommitted(s)&&o.Card!.ClassName is "AdaptiveStrike" or "Claw" or "AllForOne")
                &&!(DefectDamagePlan.PhysicalCommitted(s)&&o.Card!.ClassName is "Storm" or "Thunder" or "Voltaic" or "Darkness" or "ConsumingShadow" or "Spinner"))
            .Select(o=>o.Key).ToHashSet();
        var other=scores.Where(x=>x.Enabled&&x.Supported&&x.Score.HasValue&&!eligible.Contains(x.Key)).Select(x=>x.Score!.Value).DefaultIfEmpty(0).Max();
        for(var i=0;i<scores.Length;i++)
        {
            var old=scores[i];if(!eligible.Contains(old.Key)||!old.Supported||old.Score is not {} value)continue;
            var bonus=Math.Min(8,Math.Max(0,Math.Max(2,other+1)-value));
            if(bonus>0)scores[i]=old with{Score=Math.Round(value+bonus,3),Parts=[..old.Parts,new("现有组件已齐，优先补上首个可启动输出引擎",Math.Round(bonus,3))]};
        }
    }
    public static IEnumerable<ScorePart> Adjustments(RunSnapshot s,CardSnapshot card)
    {
        if(s.Character!="Defect"||s.Foresight is not {SourceUnchanged:true,Branches.Length:>0} sight)yield break;
        var copies=s.Deck.Count(c=>c.ClassName==card.ClassName);
        if(copies>0&&(card.ClassName!="Claw"||copies>=5))yield break;
        var name=card.ClassName;
        if(DefectDamagePlan.DarkCommitted(s)&&name is "Storm" or "Thunder")yield break;
        var physical=name is "AdaptiveStrike" or "Claw" or "AllForOne" or "Scrape" or "BulkUp" or "Feral";
        var orb=name is "Storm" or "Thunder" or "Voltaic" or "Darkness" or "ConsumingShadow" or "Spinner" or "Defragment";
        if(physical&&DefectDamagePlan.OrbCommitted(s)||orb&&DefectDamagePlan.PhysicalCommitted(s))yield break;
        double Link(CardSnapshot future)
        {
            var f=future.ClassName;
            // Current damage anchors can be acquired ahead of their missing support.
            if(name=="AdaptiveStrike"&&f is "AllForOne" or "Scrape" or "Feral" or "Hologram")return 12;
            if(name=="Claw"&&f is "Claw" or "AllForOne" or "Scrape" or "Feral")return 9;
            if(name=="AllForOne"&&f is "AdaptiveStrike" or "Claw")return 9;
            if(name is "Storm" or "Thunder"&&f is "Storm" or "Thunder" or "Voltaic" or "Defragment" or "Subroutine")return 12;
            if(name=="Voltaic"&&f is "Storm" or "Thunder")return 13;
            if(name is "Darkness" or "ConsumingShadow"&&f is "Darkness" or "ConsumingShadow" or "Quadcast" or "MultiCast" or "Shatter" or "Defragment")return 11;
            if(name=="Spinner"&&f is "Defragment" or "BiasedCognition")return 11;
            if(name=="Defragment"&&f is "Storm" or "Thunder" or "Darkness" or "Spinner")return 7;
            if(name=="FlakCannon"&&DefectPolicy.Tag(future,"status_source"))return 8;
            // Future payload must not make generic utilities beat the actual core.
            if(name is "Hologram" or "DoubleEnergy" or "Scrape" or "Feral"&&f is "AdaptiveStrike" or "AllForOne" or "Voltaic" or "MeteorStrike")return 2;
            return 0;
        }
        var best=0d;
        foreach(var branch in sight.Branches)
        {
            var values=branch.Rewards.Take(3).Where(r=>r.Room!="Shop").Select(r=>r.Cards.Select(f=>Link(f)*Math.Clamp(r.Reliability,0,1)
                /(1+.08*Math.Max(0,r.Distance-1))).DefaultIfEmpty().Max()).OrderDescending().ToArray();
            var bonus=values.FirstOrDefault()+.2*values.Skip(1).Sum();
            best=Math.Max(best,bonus);
        }
        best=Math.Min(10,best);
        var plan=DefectBuildForecast.Evaluate(s,card);
        if(plan.Value>best)
            yield return new("辉眼：构筑分支投资，"+plan.Evidence+"（条件预览，未计入现有战力）",plan.Value);
        else if(best>0)yield return new("辉眼：近期条件奖励中有核心配套，提前投资（未计入现有战力）",Math.Round(best,3));
    }
}
