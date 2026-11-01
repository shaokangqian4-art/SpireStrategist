namespace SpireStrategist.Core;

public sealed record RouteNode(string Id,string Kind,string[] Children,int Row=-1);
public sealed record NormalEncounterForecast(bool Weak,double MinGold,double MaxGold);
public sealed record CampAlternative(string Id,double Value,int Uses);
public sealed record RouteEconomy
{
    public double GoldMultiplier {get;init;}=1;
    public double GoldPerRoom {get;init;}
    public double GoldPerCombat {get;init;}
    public double GoldPerCard {get;init;}
    public double TurnGoldCost {get;init;}
    public double PriceMultiplier {get;init;}=1;
    public double ShopHealing {get;init;}
    public double? CampHealing {get;init;}
    public bool CanRest {get;init;}=true;
    public bool CanSmith {get;init;}=true;
    public CampAlternative[] CampAlternatives {get;init;}=[];
    public bool CampAllActions {get;init;}
    public double CampEntryHealing {get;init;}
    public double CampEntryValue {get;init;}
    public bool CampEntryAlreadyArmed {get;init;}
    public bool CurrentCampEntered {get;init;}
    public double CampHealExtra {get;init;}
    public int CampHealMaxHpDelta {get;init;}
    public bool CampHealToFull {get;init;}
    public double NormalGoldMin {get;init;}=10;
    public double NormalGoldMax {get;init;}=20;
    public double EliteGoldMin {get;init;}=25;
    public double EliteGoldMax {get;init;}=35;
    public double BossGoldMin {get;init;}=100;
    public double BossGoldMax {get;init;}=100;
    public double CombatRewardMultiplier {get;init;}=1;
    public double CombatRiskMultiplier {get;init;}=1;
    public double PossibleRelicGold {get;init;}
    public NormalEncounterForecast[] NormalEncounters {get;init;}=[];
    // Monster, elite, treasure, shop. Negative odds mean disabled, as in the native game.
    public double[] UnknownOdds {get;init;}=[.1,-1,.02,.03];
    public double[] UnknownBaseOdds {get;init;}=[.1,-1,.02,.03];
    public double[] UnknownIncrements {get;init;}=[.1,-1,.02,.03];
    public bool[] UnknownAllowed {get;init;}=[true,true,true,true];
    public bool UnknownAllowsEvent {get;init;}=true;
    public int TutorialUnknownsVisited {get;init;}=-1;
    public string[] FlightRoots {get;init;}=[];
    public int FlightCharges {get;init;}
    public bool PreviousWasShop {get;init;}
    public string[] Notes {get;init;}=[];
}
public sealed record ShopForecast(string Node,double Expected,double Low,double High,double PossibleRelicBonus);
public sealed record RouteScore(string Node,string Kind,double Score,double ImmediateScore,string[] Path,
    ShopForecast[] Shops,string[] Reasons)
{
    public double RiskPenalty {get;init;}
    public double ProjectedHp {get;init;}
    public string[] CampPlans {get;init;}=[];
}
public sealed record RouteEvaluation(string? RecommendedNode,double DeckQuality,double KeyUpgradeCoverage,RouteScore[] Options,string[] Notes);

public static class RoutePolicy
{
    // Four diverse representatives per node, twelve depths; branch on available camp actions.
    // This is a bounded beam over the map DAG, not exhaustive run search. Recompute after every real room.
    private sealed record Projection(double Hp,double Gold,double Low,double High,double RelicBonus,
        double UpgradeCoverage,int KeyRemaining,double Value,double Weight,string[] Path,ShopForecast[] Shops,
        double[] NormalCounts,double[] Odds,int Unknowns,double Risk,int[] Upgraded,string[] CampPlans)
    {
        public string[] UsedCampAlternatives {get;init;}=[];
        public double MaxHp {get;init;}
        public bool CampEntryArmed {get;init;}
        public double ComparableValue=>Value/Math.Max(1,Weight)-Risk;
    }
    public static RouteEvaluation Evaluate(RunSnapshot s,RouteNode[] nodes,string[] legalRoots,RouteEconomy economy,string? firstCampAction=null)
    {
        var profiles=s.Deck.Select(c=>(Card:c,Profile:CardCatalog.Find(c))).ToArray();
        var known=profiles.Where(p=>p.Profile!=null).ToArray();
        var readiness=DeckReadiness.Read(s);
        var quality=readiness.Quality;
        var earlyDefect=s.Character is "Defect" or "Silent" or "Regent" or "Necrobinder"&&(s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act)==1;
        // Utility and future synergy are not evidence of immediate elite readiness.
        var earlyElitePenalty=earlyDefect
            ? 12+20*(1-readiness.Offense)+((s.Character=="Silent"?SilentPolicy.Analyze(s).DamageEngines>0:DefectComposition.HasDamageEngine(s))?0:12):0;
        // Use the SAME contextual upgrade model as actual smithing. A fixed catalog
        // threshold previously omitted Stoke entirely (catalog Upgrade = 9).
        var keys=known.Where(p=>CardMechanics.Upgrade(s,p.Card with{CanUpgrade=true})>=10).ToArray();
        var remaining=keys.Count(p=>p.Card.CanUpgrade && p.Card.UpgradeLevel==0);
        var coverage=keys.Length==0?0:keys.Count(p=>p.Card.UpgradeLevel>0)/(double)keys.Length;
        var graph=nodes.ToDictionary(n=>n.Id);
        var options=new List<RouteScore>();
        foreach(var root in legalRoots.Distinct().Order(StringComparer.Ordinal))
        {
            if(!graph.TryGetValue(root,out var first))continue;
            var initial=new Projection(s.Hp,s.Gold,s.Gold,s.Gold,0,coverage,remaining,0,0,[],[],[1],economy.UnknownOdds,0,0,[],[]){MaxHp=s.MaxHp,CampEntryArmed=economy.CampEntryAlreadyArmed};
            var firstStates=Expand(initial,first,0).ToArray();
            var immediate=firstStates.Max(p=>p.Value);
            var frontier=new Dictionary<string,List<Projection>>{{root,Keep(firstStates)}};
            var terminals=new List<Projection>();
            for(var depth=1;depth<12 && frontier.Count>0;depth++)
            {
                var layer=new Dictionary<string,List<Projection>>();
                foreach(var pair in frontier.OrderBy(p=>p.Key,StringComparer.Ordinal))
                foreach(var current in pair.Value)
                {
                    var children=graph[pair.Key].Children.Where(c=>graph.ContainsKey(c) && !current.Path.Contains(c)).Order(StringComparer.Ordinal).ToArray();
                    if(current.Hp<=0 || children.Length==0){terminals.Add(current);continue;}
                    foreach(var child in children)
                    {
                        if(!layer.TryGetValue(child,out var states))layer[child]=states=[];
                        states.AddRange(Expand(current,graph[child],depth));
                        layer[child]=Keep(states);
                    }
                }
                frontier=layer;
            }
            terminals.AddRange(frontier.Values.SelectMany(v=>v));
            var best=terminals.OrderByDescending(p=>p.ComparableValue).First();
            options.Add(new(root,first.Kind,best.ComparableValue,immediate,best.Path,best.Shops,
                [$"当前血量 {s.Hp}/{s.MaxHp}；牌组能力指标 {quality:P0}（非胜率）",$"关键升级已完成 {coverage:P0}，剩余 {remaining} 张",
                 $"当前节点 {immediate:F1} 分；路线净分 {best.ComparableValue:F1}；独立危险扣分 {best.Risk:F1}（不被房间数量稀释）",
                 $"前瞻末生命约 {Math.Max(0,best.Hp):F1}；战损估算不是求解器已验证结果",
                 $"输出 {readiness.Offense:P0}，防御储备 {readiness.Defense:P0}，长战供防 {readiness.SustainedDefense:P0}，运转 {readiness.Cycling:P0}，成长 {readiness.Growth:P0}；均为启发式指标，非战损保证",
                 ..best.CampPlans])
                {RiskPenalty=best.Risk,ProjectedHp=Math.Max(0,best.Hp),CampPlans=best.CampPlans});
        }
        // Charge cost is applied after normalization, so a long projection cannot hide it.
        // Compare question-room opportunities with the best natural route, not merely the next icon.
        var naturalQuestions=options.Where(o=>!economy.FlightRoots.Contains(o.Node))
            .Select(o=>o.Path.Count(id=>graph[id].Kind=="Unknown")).DefaultIfEmpty(0).Max();
        options=options.Select(o=>
        {
            if(!economy.FlightRoots.Contains(o.Node))return o;
            var extra=Math.Max(0,o.Path.Count(id=>graph[id].Kind=="Unknown")-naturalQuestions);
            var remainingRows=s.Map is {} map?Math.Max(0,map.BossRow-map.CurrentRow):12;
            var cost=(18+12d/Math.Max(1,economy.FlightCharges))*Math.Clamp(remainingRows/8d,.25,1);
            var reward=Math.Min(16,extra*8);
            return o with{Score=o.Score-cost+reward,Reasons=[..o.Reasons,
                $"跨线消耗飞鞋 1 次（剩 {economy.FlightCharges}）；保留次数扣 {cost:F1}，比自然路线多 {extra} 个问号加 {reward:F1}"]};
        }).ToList();
        var ordered=options.OrderByDescending(o=>o.Score).ThenBy(o=>economy.FlightRoots.Contains(o.Node))
            .ThenBy(o=>o.Node,StringComparer.Ordinal).ToArray();
        return new(ordered.FirstOrDefault()?.Node,quality,coverage,ordered,
            ["火堆优先；健康且成型、关键升级完成时提高精英；问号优于普通战。",
             "前瞻最多 12 个节点，每层每节点保留最多4个不同血量/升级代表，火堆比较恢复、升级与遗物行动；每个真实房间后重新评分。飞鞋按次数机会成本判断。",
             "商店后继续检查必经战斗与恢复点；未知购买不计战力。商品预算按花到剩100估计，不是已经发生的交易。",
             "收益按折扣加权平均比较，危险单独扣分；预计生命耗尽后停止该分支，不能被后方火堆虚构救活。",
             "战损、事件收支、后续加牌和新遗物均为估算；金币区间为情景范围，不是保证。",
             ..(earlyDefect?new[]{HeroPolicy.Label(s.Character)+"第一幕降低精英收益；缺少成型输出时提高战损预算，已有大战损不按未来房间数自动淡化。"}:Array.Empty<string>()),..economy.Notes]);

        // Preserve a high-HP and an upgraded alternative even if immediate utility is lower.
        static List<Projection> Keep(IEnumerable<Projection> candidates)
        {
            var all=candidates.OrderByDescending(p=>p.ComparableValue).ToArray();
            if(all.Length<=4)return all.ToList();
            return new[]{all[0],all.OrderByDescending(p=>p.Hp).ThenByDescending(p=>p.ComparableValue).First(),
                all.OrderByDescending(p=>p.UpgradeCoverage).ThenByDescending(p=>p.ComparableValue).First()}
                .Concat(all).Distinct().Take(4).ToList();
        }
        IEnumerable<Projection> Expand(Projection p,RouteNode n,int depth)
        {
            if(n.Kind=="RestSite")
            {
                var projected=s with{Deck=s.Deck.Select((c,i)=>p.Upgraded.Contains(i)?c with{CanUpgrade=false,UpgradeLevel=c.UpgradeLevel+1}:c).ToArray()};
                var smith=economy.CanSmith&&projected.Deck.Any(c=>c.CanUpgrade&&CardMechanics.Upgrade(projected,c)>0);
                var entryHeal=depth==0&&economy.CurrentCampEntered?0:economy.CampEntryHealing;
                var heal=economy.CanRest&&((economy.CampHealing??p.MaxHp*.3)>0&&p.Hp+entryHeal<p.MaxHp||economy.CampHealExtra>0);
                var alternatives=economy.CampAlternatives.Where(a=>a.Value>0&&p.UsedCampAlternatives.Count(id=>id==a.Id)<a.Uses).ToArray();
                if(depth==0&&firstCampAction is {} forced)
                {yield return Step(p,n,depth,out _,forced);yield break;}
                if(economy.CampAllActions)
                {
                    var actions=(heal?new[]{"heal"}:Array.Empty<string>()).Concat(smith?["smith"]:[]).Concat(alternatives.Select(a=>a.Id)).ToArray();
                    yield return Step(p,n,depth,out _,actions.Length==0&&economy.CanRest?"heal":string.Join('|',actions));yield break;
                }
                if(heal)yield return Step(p,n,depth,out _,"heal");
                if(smith)yield return Step(p,n,depth,out _,"smith");
                foreach(var alternative in alternatives)yield return Step(p,n,depth,out _,alternative.Id);
                if(heal||smith||alternatives.Length>0)yield break;
                // Native camps require a completed action before proceeding. A full-HP
                // Night Terrors rest still has a real cost when no useful alternative exists.
                if(economy.CanRest){yield return Step(p,n,depth,out _,"heal");yield break;}
            }
            yield return Step(p,n,depth,out _);
        }
        double BattleLoss(string kind,double upgrade,int depth,double weak=0)
        {
            var boss=kind is "Boss" or "MiniBoss";
            var combatQuality=Math.Min(quality,boss?.35+.4*readiness.SustainedDefense+.25*readiness.Growth:.6+.4*readiness.Defense);
            var basis=boss?55:kind=="Elite"?30:9*weak+18*(1-weak);
            var estimate=basis*(1-.65*combatQuality)*(1-.2*upgrade)
                *(1+.12*(Math.Max(1,s.Map?.ContentAct??s.Act)-1))*economy.CombatRiskMultiplier;
            if(earlyDefect&&kind=="Elite"&&!(s.Character=="Silent"?SilentPolicy.Analyze(s).DamageEngines>0:DefectComposition.HasDamageEngine(s)))
                estimate=Math.Max(estimate,30*(1-.15*upgrade)*economy.CombatRiskMultiplier);
            // Gross damage, not net loss: recent healing/potions must not hide a fragile deck.
            var recent=s.RecentBattles.Where(b=>b.Act==s.Act&&b.MaxHp>0&&b.Room==(boss?"Boss":kind)).TakeLast(3).ToArray();
            if(recent.Length>0)
                estimate=Math.Max(estimate,(earlyDefect&&kind=="Elite"?.9:.7)*recent.Max(b=>b.DamageTaken/(double)b.MaxHp)*s.MaxHp
                    *(earlyDefect&&kind=="Elite"?1:Math.Pow(.96,depth)));
            return estimate;
        }
        Projection Step(Projection p,RouteNode n,int depth,out double local,string? campAction=null)
        {
            var hp=p.Hp;var maxHp=p.MaxHp;var ratio=hp/Math.Max(1,maxHp);
            var campEntryArmed=p.CampEntryArmed;
            var gold=p.Gold+economy.GoldPerRoom*economy.GoldMultiplier;
            var low=p.Low+economy.GoldPerRoom*economy.GoldMultiplier;
            var high=p.High+economy.GoldPerRoom*economy.GoldMultiplier;
            var bonus=p.RelicBonus;var upgrade=p.UpgradeCoverage;var left=p.KeyRemaining;
            var shops=p.Shops;
            var risk=p.Risk;var upgraded=p.Upgraded;var campPlans=p.CampPlans;
            var usedCampAlternatives=p.UsedCampAlternatives;
            var counts=p.NormalCounts;var odds=p.Odds;var unknowns=p.Unknowns;
            var weak=counts.Select((v,i)=>i<economy.NormalEncounters.Length&&economy.NormalEncounters[i].Weak?v:0).Sum();
            var normalLoss=BattleLoss("Monster",upgrade,depth,weak);
            var normalMin=counts.Select((v,i)=>v*(i<economy.NormalEncounters.Length?economy.NormalEncounters[i].MinGold:economy.NormalGoldMin)).Sum();
            var normalMax=counts.Select((v,i)=>v*(i<economy.NormalEncounters.Length?economy.NormalEncounters[i].MaxGold:economy.NormalGoldMax)).Sum();
            var progress=s.Map is {} m && n.Row>=0?Math.Clamp(n.Row/(double)Math.Max(1,m.BossRow),0,1):0;
            var ready=ratio>=.8 && quality>=.6 && upgrade>=.6;
            local=n.Kind switch
            {
                "RestSite" => (ready?48:100)+45*(1-ratio)+Math.Min(20,left*4),
                "Elite" => 25+45*quality+35*ratio+35*upgrade-150*Math.Max(0,.7-ratio)-120*Math.Max(0,.6-quality)+(ready?30:0)-earlyElitePenalty,
                "Unknown" => 58-15*(1-ratio),
                "Monster" => 27+11*weak-25*(1-ratio),
                "Shop" => 12+Math.Min(115,Math.Max(0,gold+bonus*.2)/Math.Max(.1,economy.PriceMultiplier)/3)
                    +40*progress*progress*Math.Clamp(gold/180,0,1)
                    +Math.Min(s.MaxHp-hp,economy.ShopHealing)*1.5
                    +(s.Relics.Contains("RELIC.ECTOPLASM")?0:Math.Min(3,s.PotionSlots.Count(p=>p=="POTION.FOUL_POTION"))*15),
                "Treasure" => 80,
                "Boss" => 20,
                "MiniBoss" => 5+35*quality+20*ratio-180*Math.Max(0,.9-ratio)-120*Math.Max(0,.6-quality),
                "Ancient" => 25,
                _ => -100
            };
            if(n.Kind=="Shop")
            {
                shops=[..shops,new(n.Id,Math.Round(gold),Math.Round(Math.Max(0,low)),Math.Round(high),Math.Round(bonus,1))];
                // Never credit hypothetical purchased healing/cards as combat strength.
                // Reserve a spending budget so later shops do not reuse the same money.
                hp=Math.Min(maxHp,hp+economy.ShopHealing);
                gold=Math.Min(gold,100);low=0;bonus=0;
            }
            if(n.Kind is "Monster" or "Elite" or "Boss" or "MiniBoss")
            {
                var elite=n.Kind!="Monster";
                var boss=n.Kind is "Boss" or "MiniBoss";
                var min=(boss?economy.BossGoldMin:elite?economy.EliteGoldMin:normalMin)*economy.CombatRewardMultiplier;
                var max=(boss?economy.BossGoldMax:elite?economy.EliteGoldMax:normalMax)*economy.CombatRewardMultiplier;
                var turns=boss?9:elite?6:4;
                gold+=((min+max)/2+economy.GoldPerCombat+economy.GoldPerCard*.5)*economy.GoldMultiplier-turns*economy.TurnGoldCost;
                low+=(min+economy.GoldPerCombat)*economy.GoldMultiplier-(turns+3)*economy.TurnGoldCost;
                high+=(max+economy.GoldPerCombat+economy.GoldPerCard)*economy.GoldMultiplier;
                var loss=elite?BattleLoss(n.Kind,upgrade,depth):normalLoss;
                if(hp<=loss+8)risk+=(140+Math.Max(0,loss-hp)*8)*Math.Pow(.9,depth);
                hp-=loss;
                campEntryArmed=false;
                if(elite)bonus+=economy.PossibleRelicGold*economy.GoldMultiplier;
                else counts=Advance(counts,1);
            }
            else if(n.Kind=="RestSite")
            {
                if(!(depth==0&&economy.CurrentCampEntered))
                {
                    var entry=Math.Min(maxHp-hp,economy.CampEntryHealing);hp+=entry;
                    if(entry>0)campPlans=[..campPlans,$"{n.Id} 火堆入场被动恢复 +{entry:F1} 生命"];
                    if(!campEntryArmed)local+=economy.CampEntryValue;
                }
                campEntryArmed=true;
                foreach(var action in (campAction??"").Split('|',StringSplitOptions.RemoveEmptyEntries))
                {
                    var projected=s with{Hp=(int)Math.Max(0,hp),
                        MaxHp=(int)maxHp,
                        Deck=s.Deck.Select((c,i)=>upgraded.Contains(i)?c with{CanUpgrade=false,UpgradeLevel=c.UpgradeLevel+1}:c).ToArray()};
                    var heal=Math.Max(0,economy.CampHealToFull?maxHp:economy.CampHealing??maxHp*.3);
                    if(action=="heal"&&economy.CanRest)
                    {
                        var actual=Math.Min(maxHp-hp,heal);hp+=actual;
                        local+=economy.CampHealExtra;
                        // Gaining max HP also heals that amount; losing it clamps current HP.
                        maxHp=Math.Max(1,maxHp+economy.CampHealMaxHpDelta);
                        hp=Math.Min(maxHp,hp+Math.Max(0,economy.CampHealMaxHpDelta));
                        campPlans=[..campPlans,$"{n.Id} 火堆预计休息 +{actual:F1} 生命，按到达时预计战损规划恢复"];
                    }
                    else if(action=="smith"&&economy.CanSmith)
                    {
                        var target=projected.Deck.Select((c,i)=>(Card:c,Index:i,Value:CardMechanics.Upgrade(projected,c)))
                            .Where(x=>x.Card.CanUpgrade&&x.Value>0).OrderByDescending(x=>x.Value).FirstOrDefault();
                        if(target.Card!=null)
                        {
                            upgraded=[..upgraded,target.Index];
                            if(keys.Any(k=>ReferenceEquals(k.Card,s.Deck[target.Index])))
                            {left--;upgrade=Math.Min(1,upgrade+1d/Math.Max(1,keys.Length));}
                            local+=Math.Min(30,target.Value);
                            campPlans=[..campPlans,$"{n.Id} 火堆预计升级 {target.Card.Name}，机制收益 {target.Value:F1}"];
                        }
                    }
                    else if(economy.CampAlternatives.FirstOrDefault(a=>a.Id==action&&usedCampAlternatives.Count(id=>id==a.Id)<a.Uses) is {} alternative)
                    {
                        local+=Math.Min(30,alternative.Value);
                        usedCampAlternatives=[..usedCampAlternatives,alternative.Id];
                        campPlans=[..campPlans,$"{n.Id} 火堆预计遗物行动 {alternative.Id}，收益估计 {alternative.Value:F1}"];
                    }
                // Keep the camp's safe-room preference even without an immediate action.
                // No HP, upgrade or hypothetical relic combat power is credited here.
                }
            }
            else if(n.Kind=="Unknown")
            {
                var allowed=(bool[])economy.UnknownAllowed.Clone();
                if((depth==0&&economy.PreviousWasShop)||n.Children.Length>0&&n.Children.All(id=>graph.TryGetValue(id,out var child)&&child.Kind=="Shop"))allowed[3]=false;
                var chances=new double[4];var total=0d;
                for(var i=0;i<4;i++)if(allowed[i]&&odds[i]>=0)
                {chances[i]=Math.Clamp(odds[i],0,1-total);total+=chances[i];}
                if(!economy.UnknownAllowsEvent&&total<1)
                {
                    // Native RoomType enum order: Monster, Elite, Treasure, Shop.
                    var fallback=Enumerable.Range(0,4).FirstOrDefault(i=>allowed[i],-1);
                    if(fallback>=0){chances[fallback]+=1-total;total=1;}
                }
                var tutorial=economy.TutorialUnknownsVisited<0?-1:economy.TutorialUnknownsVisited+unknowns;
                if(tutorial is >=0 and <=2){chances=[tutorial==2?1:0,0,0,0];total=chances.Sum();}
                var eventChance=1-total;
                var eliteLoss=BattleLoss("Elite",upgrade,depth);
                local=eventChance*local+chances[0]*(27+11*weak-25*(1-ratio))
                    +chances[1]*(25+45*quality+35*ratio-120*Math.Max(0,.6-quality))
                    +chances[2]*80+chances[3]*(12+Math.Min(115,gold/Math.Max(.1,economy.PriceMultiplier)/3));
                if(hp<=normalLoss+8)risk+=140*chances[0]*Math.Pow(.9,depth);
                if(hp<=eliteLoss+8)risk+=140*chances[1]*Math.Pow(.9,depth);
                gold+=(chances[0]*((normalMin+normalMax)/2*economy.CombatRewardMultiplier+economy.GoldPerCombat+economy.GoldPerCard*.5)
                    +chances[1]*((economy.EliteGoldMin+economy.EliteGoldMax)/2*economy.CombatRewardMultiplier+economy.GoldPerCombat+economy.GoldPerCard*.5))*economy.GoldMultiplier
                    -(chances[0]*4+chances[1]*6)*economy.TurnGoldCost;
                low-=30;high+=Math.Max(50,normalMax*economy.CombatRewardMultiplier+economy.GoldPerCombat)*economy.GoldMultiplier;
                hp=hp-eventChance*3-chances[0]*normalLoss-chances[1]*eliteLoss;
                counts=Advance(counts,chances[0]);
                if(tutorial<0||tutorial>2)
                    odds=odds.Select((v,i)=>!allowed[i]?v:chances[i]*economy.UnknownBaseOdds[i]+(1-chances[i])*(v+economy.UnknownIncrements[i])).ToArray();
                unknowns++;
            }
            else if(n.Kind=="Treasure")bonus+=economy.PossibleRelicGold*economy.GoldMultiplier;
            // An event-mixture estimate can also exhaust HP. It must not become an
            // attractive short route merely because only fractional combat risk was charged.
            if(hp<=0)risk=Math.Max(risk,300*Math.Pow(.9,depth));
            return new(hp,Math.Max(0,gold),Math.Max(0,low),Math.Max(0,high),bonus,upgrade,left,
                p.Value+local*Math.Pow(.8,depth),p.Weight+Math.Pow(.8,depth),[..p.Path,n.Id],shops,counts,odds,unknowns,risk,upgraded,campPlans)
                {UsedCampAlternatives=usedCampAlternatives,MaxHp=maxHp,CampEntryArmed=campEntryArmed};
        }
        static double[] Advance(double[] counts,double chance)
        {
            var next=new double[counts.Length+1];
            for(var i=0;i<counts.Length;i++){next[i]+=counts[i]*(1-chance);next[i+1]+=counts[i]*chance;}
            return next;
        }
    }
}
