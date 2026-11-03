namespace SpireStrategist.Core;

// Shared acquisition cost: being a usable attack is not enough to improve an
// established deck. Apply only to modeled transition attacks with an owned
// alternative; preserve new functional coverage and unformed early decks.
public static class TransitionMargin
{
    private static bool Tag(CardSnapshot c,string tag)=>HeroPolicy.Tag(c,tag)||SilentPolicy.Tag(c,tag)||DefectPolicy.Tag(c,tag);
    private static bool Transition(CardSnapshot c)
    {
        if(Tag(c,"transition"))return true;
        // Defect's plain attacks do not carry a transition tag. Exclude the
        // mechanisms that can grow into a core before comparing raw damage.
        if(DefectPolicy.Card(c.ClassName)!=null&&!new[]{"claw","scaling","dark","physical_core","zero_setup","energy_payoff","meteor","power_payoff","focus","orb_attack","exhaust","draw","conditional_draw","energy"}.Any(t=>DefectPolicy.Tag(c,t)))return true;
        // Explicitly reviewed numeric-only profiles. Do not infer that Headbutt,
        // Anger or Body Slam lack a mechanism merely because their tags are absent.
        return c.ClassName is "IronWave" or "TwinStrike" or "Thunderclap" or "UltimateStrike";
    }
    public static ScorePart? Assess(RunSnapshot s,CardSnapshot c)
    {
        var act=s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act;
        if(act<2||c.Type!="Attack"||c.Rarity=="Basic"||c.Modified||!Transition(c))return null;
        var owned=s.Deck.Where(d=>d.Rarity!="Basic"&&!MultiplayerAdvice.Ignore(d)).ToArray();
        if(owned.Length<6)return null;
        if(new[]{"poison","shiv_source","play_damage","sly","skill_discount","discard","delayed_draw","energy_sink"}.Any(t=>Tag(c,t)))return null;
        // Draw, resources, cleanup, scaling and new debuffs can justify a card
        // despite modest printed damage. Do not turn this into a rarity ban.
        foreach(var tag in new[]{"draw","energy","cleanup","scaling","soul_source","doom_source","ethereal_source","star_source","weak","vulnerable","strength_down","aoe","frost","lightning","physical_support"})
            if(Tag(c,tag)&&!owned.Any(d=>Tag(d,tag)))return null;
        var p=CardCatalog.Find(c);
        if(p==null||p.Draw>0||p.Energy>0||p.ExhaustSource>0||p.Scaling>0)return null;
        if(HeroPolicy.V(c,"Block")>0&&owned.Sum(d=>CardCatalog.Find(d)?.Block??0)<3)return null;
        foreach(var key in new[]{"WeakPower","VulnerablePower","StrengthPower"})
            if(HeroPolicy.V(c,key)!=0&&!owned.Any(d=>HeroPolicy.V(d,key)!=0))return null;
        var candidate=OwnedAttackPlan.Efficiency(s,c);
        if(candidate<=0)return null;
        var alternatives=owned.Where(d=>d.Type=="Attack"&&!d.Keywords.Contains("Exhaust")&&!d.Keywords.Contains("Ethereal")&&d.ClassName!=c.ClassName)
            .Select(d=>(Card:d,Value:OwnedAttackPlan.Efficiency(s,d))).Where(x=>x.Value>=12&&x.Value>candidate*1.25).OrderByDescending(x=>x.Value).ToArray();
        if(alternatives.Length==0)return null;
        var ready=HeroPolicy.Supports(s.Character)?HeroPolicy.Analyze(s).DamageEngines>0:
            s.Character=="Silent"?SilentPolicy.Analyze(s).DamageEngines>0:
            owned.Count(d=>d.Type=="Attack"&&OwnedAttackPlan.Efficiency(s,d)>=12)>=2;
        if(!ready)return null;
        var best=alternatives[0];
        // Low density/large deck makes drawing another filler instead of the
        // established outlet more expensive. No forecast value enters this test.
        var drawCompetition=.5+Math.Min(.75,Math.Max(0,s.Deck.Length-15)*.05);
        var tax=Math.Min(4,(best.Value/candidate-1)*3+drawCompetition+(act==3?.5:0));
        return new($"已有输出 {best.Card.Name}：同类过渡的边际收益不足，计入挤占核心抽牌的代价",-Math.Round(tax,3));
    }
    public static Evaluation Apply(RunSnapshot s,IReadOnlyList<DecisionOption> options,Evaluation result)
    {
        var scores=result.Options.Select(x=>{
            var o=options.First(o=>o.Key==x.Key);
            if(!x.Supported||!x.Enabled||x.Score is not {} score||o.Kind!="card"||o.Card==null||Assess(s,o.Card) is not {} tax)return x;
            return x with{Score=Math.Round(score+tax.Value,3),Parts=[..x.Parts,tax]};
        }).ToArray();
        if(scores.SequenceEqual(result.Options))return result;
        var skip=options.Where(o=>o.Kind=="skip").Select(o=>o.Key).ToHashSet();
        return result with{Options=scores,RecommendedKey=result.RecommendedKey==null?null:
            scores.Where(x=>x.Supported&&x.Enabled&&x.Score.HasValue).OrderByDescending(x=>x.Score)
                .ThenByDescending(x=>skip.Contains(x.Key)).ThenByDescending(x=>x.Key==result.RecommendedKey).FirstOrDefault()?.Key};
    }
}
