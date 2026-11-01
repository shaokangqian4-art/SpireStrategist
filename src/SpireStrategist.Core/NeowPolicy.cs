namespace SpireStrategist.Core;

// User-ranked opening choices for supported single-player characters. Scores are priorities,
// not win rates, and do not alter ordinary relic/shop valuation.
public static class NeowPolicy
{
    public static bool Supports(RunSnapshot s)=>!s.IsMultiplayer&&DefectStrategy.Supported(s.Character);
    public static bool Excluded(string id)=>id is "KALEIDOSCOPE" or "MASSIVE_SCROLL";
    public static RelicAssessment Evaluate(string id,RunSnapshot s,double bootsOpportunity=0)
    {
        var v=new StrategicValue(s);
        var selection=id switch{"PRECISE_SCISSORS"=>"neow-remove-one","PRECARIOUS_SHEARS"=>"neow-remove-pair","NEW_LEAF"=>"transform",_=>RelicValue.Evaluate(id,s,true).Selection};
        var value=id switch
        {
            "PRECISE_SCISSORS"=>v.RemoveBest(1)>0?32:0,
            "PRECARIOUS_SHEARS"=>v.RemoveBest(2)>0?32.5-Math.Max(0,.5-v.Ratio)*40:0,
            "WINGED_BOOTS"=>26+Math.Clamp(bootsOpportunity,0,4),
            "SILVER_CRUCIBLE"=>28,
            "NEW_LEAF"=>24,
            "LEAFY_POULTICE"=>24-Math.Max(0,55-s.MaxHp)*.5,
            "ARCANE_SCROLL" or "HEFTY_TABLET" or "SILKEN_TRESS"=>22,
            // Two unspecified relics do not justify adding another Strike/Defend pair
            // ahead of removal or transformation. This is not a prediction of the relics.
            "LARGE_CAPSULE"=>4,
            "NUTRITIOUS_OYSTER"=>2,
            "BOOMING_CONCH" or "POMANDER"=>3,
            "FISHING_ROD" or "LAVA_ROCK" or "NEOWS_TALISMAN"=>1,
            "DOWSING_ROD"=>0,
            "KALEIDOSCOPE" or "MASSIVE_SCROLL"=>-100,
            // Unranked middle choices retain their relative old estimate in a bounded tier.
            _=>16+Math.Clamp(RelicValue.Evaluate(id,s,true).Value,-4,4)
        };
        return new(value,selection,"捏奥共用实战优先级：所有已适配角色采用用户排序先验，非胜率；赠牌／变化另计实际结果与构筑适配。",!Excluded(id));
    }
    // Keep the opening prior even when the known card needs components not yet owned.
    // Its current deck fit is a bounded adjustment, not a replacement for that prior.
    public static double ScrollScore(RunSnapshot s,CardSnapshot card)=>
        Evaluate("ARCANE_SCROLL",s).Value+Math.Clamp(new StrategicValue(s).Add(card),-12,12);
    public static double TransformScore(RunSnapshot s,IReadOnlyList<CardSnapshot> results,bool losesMaxHp)
    {
        if(results.Count==0)return -20;
        var v=new StrategicValue(s);
        // The user's opening preference promotes a pair when it contains a blue/gold;
        // suitability still considers both cards instead of ignoring the second result.
        var tier=results.Max(c=>c.Rarity switch{"Rare"=>36d,"Uncommon"=>32d,_=>24d});
        var fit=results.Average(c=>Math.Clamp(v.Add(c)/10,-1,1));
        var investment=results.Select(c=>s.Character=="Silent"?SilentForecast.Evaluate(s,c).Value:DefectBuildForecast.Evaluate(s,c).Value).DefaultIfEmpty().Max();
        return tier+fit+Math.Min(6,investment)-(losesMaxHp?Math.Max(0,55-s.MaxHp)*.5:0);
    }
    public static int[] RemovalPlan(RunSnapshot s,IReadOnlyList<CardSnapshot> cards,int count)
    {
        var chosen=new List<int>();var v=new StrategicValue(s);
        var strike="Strike"+s.Character;
        var defend="Defend"+s.Character;
        void Basic(string type)
        {
            var target=Enumerable.Range(0,cards.Count).Where(i=>!chosen.Contains(i)&&cards[i].ClassName==type&&
                !cards[i].Modified&&cards[i].UpgradeLevel==0&&
                (v.Remove(cards[i])>0||type==defend&&s.Deck.Count(c=>c.ClassName==defend)>=3)).FirstOrDefault(-1);
            if(target>=0)chosen.Add(target);
        }
        Basic(strike);
        if(count==2)Basic(defend);
        chosen.AddRange(Enumerable.Range(0,cards.Count).Where(i=>!chosen.Contains(i))
            .OrderByDescending(i=>v.Remove(cards[i])).Take(Math.Max(0,count-chosen.Count)));
        return chosen.Take(count).ToArray();
    }
    // Bounded DAG planning: value of up to three same-row jumps versus normal edges.
    // It measures opportunities to reach camps/question nodes, not guaranteed survival.
    public static double BootsOpportunity(IReadOnlyList<RouteNode> nodes,string start)
    {
        var byId=nodes.ToDictionary(n=>n.Id);
        if(!byId.ContainsKey(start))return 0;
        var memo=new Dictionary<(string,int),double>();
        double Best(string id,int jumps)
        {
            if(memo.TryGetValue((id,jumps),out var cached))return cached;
            var n=byId[id];var reward=n.Kind switch{"RestSite"=>5d,"Unknown"=>3d,"Elite"=>2d,"Shop"=>1d,_=>0};
            var children=n.Children.Where(byId.ContainsKey).Where(k=>byId[k].Row>n.Row).ToArray();
            var next=children.Select(k=>Best(k,jumps)).DefaultIfEmpty(0).Max();
            if(jumps>0)
            {
                var rows=children.Select(k=>byId[k].Row).ToHashSet();
                next=Math.Max(next,nodes.Where(x=>rows.Contains(x.Row)&&!children.Contains(x.Id)).Select(x=>Best(x.Id,jumps-1)).DefaultIfEmpty(0).Max());
            }
            return memo[(id,jumps)]=reward+next;
        }
        return Math.Clamp((Best(start,3)-Best(start,0))*.5,0,4);
    }
}
