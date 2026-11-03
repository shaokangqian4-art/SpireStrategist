namespace SpireStrategist.Core;

// Character-specific adapters for the shared route/event/action machinery.
// Orb proxies are discounted capacity estimates; no Ironclad strength/exhaust
// archetype is assigned to Defect. Unknown effects remain explicitly unmodelled.
public static class DefectStrategy
{
    public static double CheeseValue(RunSnapshot s)
    {
        var v=new StrategicValue(s);
        var battles=Math.Clamp(v.Remaining*.45,1,30);
        return v.MaxHp(battles); // one max HP per combat, discounted remaining combat count
    }
    public static bool Supported(string character)=>character is "Ironclad" or "Defect" or "Silent" or "Regent" or "Necrobinder";
    public static bool Basic(CardSnapshot c)=>c.ClassName is "StrikeDefect" or "DefendDefect";
    public static double Upgrade(RunSnapshot s,CardSnapshot c)
    {
        if(HeroPolicy.Supports(s.Character))return HeroStrategy.Upgrade(s,c);
        if(s.Character=="Silent")return SilentStrategy.Upgrade(s,c);
        if(ApotheosisPolicy.UpgradeOverride(s,c) is {} apotheosis)return apotheosis;
        if(MultiplayerAdvice.Ignore(c))return 0;
        if(!c.CanUpgrade)return 0;
        if(DefectNeutral.Upgrade(c) is {} neutral)return neutral;
        var delta=DefectPolicy.UpgradeDelta(c);
        if(delta==null)return CardCatalog.Find(c)?.Upgrade??0;
        var result=2+delta.Value*3;
        if(Basic(c))result=3;
        if(c.ClassName=="Hologram")
        {
            var copies=s.Deck.Count(d=>d.ClassName=="Hologram");
            var repeat=s.Deck.Any(d=>d.ClassName=="AllForOne")||CopyEngine.Active(s);
            // Two exhaust copies can serve two one-shot recalls without spending a camp.
            result=copies>=2&&!repeat?8:16;
            if(s.Deck.Any(d=>d.ClassName=="Hologram"&&d.UpgradeLevel>0))result=6;
            if(CopyEngine.Adaptive(s))result+=3;
        }
        if(c.ClassName=="Coolheaded")result=16;
        if(c.ClassName is "Defragment" or "Storm" or "Darkness")result=17;
        if(c.ClassName is "Compact" or "Subroutine" or "Buffer")result=14;
        if(c.ClassName=="DoubleEnergy")result=14;
        if(c.ClassName=="Voltaic"&&s.Deck.Any(d=>DefectPolicy.Tag(d,"lightning"))
            &&DefectComposition.Physical(s)<2)result=17; // Removing Exhaust permits cumulative Lightning amplification across cycles.
        if(c.ClassName=="Zap")result=2+(s.Deck.Any(d=>d.ClassName=="AllForOne")?1:0);
        if(c.ClassName=="Dualcast")result=4+(s.Deck.Any(d=>d.ClassName is "Darkness" or "ConsumingShadow" or "Null")?5:0);
        if(c.ClassName=="AdaptiveStrike")result=CopyEngine.Active(s)?12:7;
        if(c.ClassName=="AllForOne")result=4; // extra damage, not stronger retrieval
        return UpgradeReview.Refine(s,c,result);
    }
    public static double Remove(RunSnapshot s,CardSnapshot c)
    {
        if(HeroPolicy.Supports(s.Character))return HeroStrategy.Remove(s,c);
        if(s.Character=="Silent")return SilentStrategy.Remove(s,c);
        if(c.ClassName=="AscendersBane"||c.Keywords.Contains("Eternal"))return -100;
        if(c.Type=="Curse")return c.ClassName is "Guilty" or "Clumsy"||c.Keywords.Contains("Ethereal")?3:18;
        if(c.Type=="Status")return 10;
        if(CopyEngine.SelfCopies(c)&&CopyEngine.Swift(c))return -40;
        var p=CardCatalog.Find(c);if(p==null)return -12;
        // UI snapshots are freshly allocated, while synthetic decks may reuse one
        // record for several copies. Remove exactly one matching deck slot in both cases.
        var index=Array.FindIndex(s.Deck,d=>ReferenceEquals(d,c));
        if(index<0)index=Array.FindIndex(s.Deck,d=>d.Id==c.Id&&d.ClassName==c.ClassName&&d.UpgradeLevel==c.UpgradeLevel
            &&d.Cost==c.Cost&&d.Enchantment==c.Enchantment&&d.EnchantmentAmount==c.EnchantmentAmount&&d.Affliction==c.Affliction
            &&d.Variables.Count==c.Variables.Count&&d.Variables.All(v=>c.Variables.TryGetValue(v.Key,out var x)&&x==v.Value));
        var without=s with{Deck=s.Deck.Where((_,i)=>i!=index).ToArray()};
        var readiness=Readiness(without);
        var score=new Policy().Evaluate(without,[new("c",c.Name,"card",Card:c)]).Options[0].Score;
        var value=Basic(c)?7-c.UpgradeLevel*2:Math.Clamp(-(score??12),-20,6);
        if(CopyEngine.Active(s)&&Basic(c))value+=4;
        if(CopyEngine.Swift(c))value-=4;
        // A starter deck being weak is not evidence that all of its basics are
        // indispensable. Protect actual remaining sources, not a global readiness floor.
        var attacks=without.Deck.Count(d=>d.Type=="Attack");
        var blocks=without.Deck.Count(d=>d.Type=="Skill"&&d.Variables.GetValueOrDefault("Block")>0);
        if(c.Type=="Attack"&&attacks<3&&readiness.Offense<.3)value-=12;
        if(c.ClassName=="DefendDefect"&&blocks<3&&readiness.Defense<.4)value-=10;
        if(s.Deck.Length<9)value-=8;
        return value;
    }
    public static OptionScore? NonCard(RunSnapshot s,DecisionOption o)
    {
        double value;string reason;
        if(o.Kind=="effect"&&o.EffectValue.HasValue){value=o.EffectValue.Value;reason="原生营火效果及机器人牌组估值";}
        else if(o.Kind=="smith")
        {
            if(ApotheosisPolicy.Finished(s))return new(o.Key,o.Name,true,o.Enabled,ApotheosisPolicy.CoveredSmithScore,[new(ApotheosisPolicy.CampReason,ApotheosisPolicy.CoveredSmithScore)],[]);
            var targets=s.Deck.Where(c=>c.CanUpgrade&&!MultiplayerAdvice.Ignore(c)).OrderByDescending(c=>Upgrade(s,c)).Take(Math.Max(1,o.SmithCount)).ToArray();
            value=targets.Sum(c=>Upgrade(s,c));reason="推荐升级："+string.Join("、",targets.Select(c=>c.Name));
        }
        else if(o.Kind=="heal")
        {
            var ratio=(double)s.Hp/Math.Max(1,s.MaxHp);
            value=Math.Min(s.MaxHp-s.Hp,o.HealAmount)*(.45+2.5*Math.Pow(1-ratio,2))*CardMechanics.HealthyHealingMultiplier(ratio);
            value*=Math.Clamp(1.1-1.5*ratio,.35,1);
            value+=o.EffectValue??0;
            reason=(o.EffectValue??0)==0?"机器人升级机会成本折扣后的实际恢复收益":"机器人恢复收益及休息附带奖励、代价";
        }
        else return null;
        var ratioNow=(double)s.Hp/Math.Max(1,s.MaxHp);
        var rawRisk=s.CampRouteRisk.GetValueOrDefault(o.Kind);
        var imminent=s.NextNodeTypes.Any(n=>n is "Boss" or "Elite" or "MiniBoss");
        // Route risk is a coarse survival prior, not a solver-certified death. Bound its
        // influence at viable HP; keep urgent healing dominant at critically low HP.
        var cap=ratioNow>=.4?(imminent?12:4):ratioNow>=.25?16:60;
        var risk=-Math.Min(cap,rawRisk*.15);
        var parts=new[]{new ScorePart(reason,value),new ScorePart("机器人后续路线风险（粗估折扣并限幅）",risk)};
        if(s.Character!="Defect")parts=parts.Select(p=>p with{Reason=p.Reason.Replace("机器人",HeroPolicy.Label(s.Character))}).ToArray();
        return new(o.Key,o.Name,true,o.Enabled,value+risk,parts,[]);
    }
    public static ShopEvaluation Shop(RunSnapshot s,ShopCard[] stock,int? removalPrice,int arrivalGold,int boughtCards,int? reserveOverride)
    {
        var reserve=reserveOverride??Math.Min(150,Math.Max(0,arrivalGold)*35/100);
        var choices=new List<ShopChoice>();
        double Cost(int price)=>price/40d*(s.Gold<150?1.5:1);
        if(s.PlayerCount<1||s.MaxHp<=0)return new(null,[],reserve,["需要有效的机器人状态"]);
        if(removalPrice is >=0&&removalPrice<=s.Gold)
            foreach(var (c,i) in s.Deck.Select((c,i)=>(c,i)))
            {
                var benefit=Remove(s,c)*.65;
                if(benefit<=0)continue;
                choices.Add(new("remove:"+i,c.Name,"remove",removalPrice.Value,benefit-Cost(removalPrice.Value),
                    ["机器人删牌：保护输出、防守及复制运转核心；与买牌比较净收益"],i));
            }
        foreach(var e in stock.Where(e=>e.Price>=0&&e.Price<=s.Gold))
        {
            var quote=new Policy().Evaluate(s with{Gold=s.Gold-e.Price},[new("c",e.Card.Name,"card",Card:e.Card)]).Options[0];
            var score=quote.Score;
            if(score==null)continue;
            var benefit=score.Value-Cost(e.Price)-1.5;
            var allowed=boughtCards<2&&(s.Gold-e.Price>=reserve||score>=7);
            choices.Add(new(e.Key,e.Card.Name,"card",e.Price,allowed?benefit:Math.Min(-1,benefit),
                [$"机器人牌组边际评分 {score:F1}，购买门槛及金币成本 {Cost(e.Price)+1.5:F1}","交易后重算；高收益核心可使用预留金币",..quote.Parts.Where(p=>p.Reason.StartsWith("辉眼：")).Select(p=>$"{p.Value:+0.0;-0.0;0} {p.Reason}")]));
        }
        if(s.Character!="Defect")choices=choices.Select(c=>c with{Reasons=c.Reasons.Select(r=>r.Replace("机器人",HeroPolicy.Label(s.Character))).ToArray()}).ToList();
        return new(choices.Where(c=>c.Score>0).OrderByDescending(c=>c.Score).ThenBy(c=>c.Price).FirstOrDefault(),choices.ToArray(),reserve,
            [HeroPolicy.Label(s.Character)+"策略：买牌与删牌直接比较，普通遗物沿用预算购买。"]);
    }
    public static DeckReadiness Readiness(RunSnapshot s)
    {
        var profiles=s.Deck.Select(c=>(Card:c,Profile:DefectNeutral.ProfileFor(c)??CardCatalog.Find(c))).Where(x=>x.Profile!=null).ToArray();
        double Sum(Func<CardProfile,double> f)=>profiles.GroupBy(x=>x.Card.ClassName).Sum(g=>g.Select((x,i)=>f(x.Profile!)*Math.Pow(.7,i)).Sum());
        var focus=s.Deck.Where(c=>DefectPolicy.Tag(c,"focus")).Sum(c=>(double)c.Variables.GetValueOrDefault("FocusPower",1));
        if(s.Relics.Any(r=>r.Split('.').Last()=="DATA_DISK"))focus+=1;
        var frost=s.Deck.Sum(c=>DefectPolicy.Tag(c,"frost3")?2:DefectPolicy.Tag(c,"frost2")?1.5:DefectPolicy.Tag(c,"frost")?1:0);
        var lightning=s.Deck.Count(c=>DefectPolicy.Tag(c,"lightning"));
        var powers=s.Deck.Count(c=>c.Type=="Power");
        var storm=s.Deck.Any(c=>c.ClassName=="Storm")?Math.Min(3,powers*.5):0;
        var dark=s.Deck.Count(c=>DefectPolicy.Tag(c,"dark"));
        var attack=Sum(p=>p.Attack)+lightning*.4+storm*.6+dark*.4;
        if(s.Relics.Any(r=>r.Split('.').Last()=="INFUSED_CORE"))attack+=1.2;
        var block=Sum(p=>p.Block)+frost*(.6+Math.Min(3,focus)*.25);
        var draw=Sum(p=>p.Draw);
        if(CopyEngine.Active(s))draw+=2;
        var growth=LongFightGrowth(s,draw,focus);
        var known=profiles.Length/Math.Max(1d,s.Deck.Length);
        var offense=Math.Clamp(attack/8,0,1)*known;var defense=Math.Clamp(block/7,0,1)*known;
        var cycling=Math.Clamp(draw/3,0,1)*known;var scaling=growth*known;
        var advanced=s.Deck.Count(c=>c.Rarity!="Basic"&&c.Type is "Attack" or "Skill" or "Power");
        var quality=Math.Clamp(.1*known+.4*advanced/Math.Max(1d,s.Deck.Length)+.15*offense+.15*defense+.08*cycling+.12*scaling,0,1);
        return new(quality,offense,defense,cycling,scaling){SustainedDefense=Math.Clamp((Sum(p=>p.Block)*5/Math.Max(10,s.Deck.Length)+frost*(.3+focus*.15))/3,0,1)};
    }
    private static double LongFightGrowth(RunSnapshot s,double draw,double focus)
    {
        // A completeness proxy, not DPS or a simulated boss win rate. Frost,
        // temporary Focus, energy and card rarity are not damage scaling alone.
        int N(params string[] names)=>s.Deck.Count(c=>names.Contains(c.ClassName));
        var dilution=Math.Min(.25,Math.Max(0,s.Deck.Length-15)*.012);
        var retrieval=Math.Min(.2,N("Hologram","AllForOne")*.08+draw*.02);
        var darkSources=N("Darkness","ConsumingShadow","Null","ShadowShield");
        var dark=darkSources==0?0:.15+Math.Min(.45,N("Darkness","ConsumingShadow")*.25+N("Null","ShadowShield")*.08)
            +Math.Min(.15,focus*.03)+Math.Min(.2,N("Dualcast","Quadcast","MultiCast")*.08)
            +Math.Min(.15,N("Loop")*.08)+retrieval-dilution;
        var producers=s.Deck.Count(c=>DefectPolicy.Tag(c,"lightning")&&!c.Keywords.Contains("Exhaust"));
        var storm=N("Storm")>0?Math.Min(.18,s.Deck.Count(c=>c.Type=="Power"&&c.ClassName!="Storm")*.035):0;
        var lightning=producers==0&&storm==0?0:.1+Math.Min(.25,producers*.08)+Math.Min(.2,focus*.05)+storm
            +(producers>=2||storm>=.1?Math.Min(.3,N("Thunder")*.2):0)+retrieval*.5-dilution;
        var physical=N("Claw","AdaptiveStrike")==0&&!(N("BulkUp","Feral")>0&&s.Deck.Count(c=>c.Type=="Attack")>=3)?0:.15+Math.Min(.4,N("Claw")*.12+N("AdaptiveStrike")*.1)
            +Math.Min(.2,N("BulkUp","Feral")*.08)+retrieval-dilution;
        if(CopyEngine.Active(s))physical=Math.Max(physical,.8+Math.Min(.2,N("AllForOne")*.2));
        var statuses=s.Deck.Count(c=>DefectPolicy.Tag(c,"status_source"));
        var status=N("FlakCannon")==0||statuses==0?0:.2+Math.Min(.35,statuses*.1)
            +Math.Min(.15,N("Iteration","Compact")*.075)+retrieval-dilution;
        var glass=N("Spinner")==0?0:.3+Math.Min(.2,focus*.05)+retrieval-dilution;
        var meteor=N("MeteorStrike")>0&&(s.Energy>=5||N("Supercritical","DoubleEnergy")>0)
            ?.3+Math.Min(.3,draw*.08)+retrieval-dilution:0;
        var best=new[]{dark,lightning,physical,status,glass,meteor}.Max();
        // Echo improves an existing repeatable damage route; merely owning a Power
        // cannot make several incomplete routes add up to a complete boss engine.
        if(N("EchoForm")>0&&best>.15)best+=.2;
        return Math.Clamp(best,0,1);
    }
}
