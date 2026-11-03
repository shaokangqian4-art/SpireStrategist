using System.Text.Json;

namespace SpireStrategist.Core;

// Reviewed deterministic priors shared with tools/DefectDraft; no game references.
public static class DefectPolicy
{
    public const string Version = "defect-draft-0.14";
    public const string Scope = "机器人接管：选牌、营火、路线、商店与原生事件；战斗需要 CombatSolver。未知效果会记录并采用明确后备策略。";
    private sealed record Profile(double Base, double[] Phase, double Duplicate, double UpgradeDelta, string[] Tags);
    private sealed record Coefficients(double CardTax, double DamageNeed, double BlockNeed, double DuplicateQuadratic, double SynergyCap);
    private sealed record Prior(Dictionary<string,Profile> Cards, Coefficients Coefficients);
    private sealed record Definition(string MultiplayerConstraint, CardSnapshot Basic, CardSnapshot Upgraded);
    private static T Read<T>(string resource)
    {
        using var stream=typeof(DefectPolicy).Assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException(resource);
        return JsonSerializer.Deserialize<T>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    private static readonly Prior prior=Read<Prior>("SpireStrategist.DefectPrior.json");
    private static readonly Dictionary<string,Definition> definitions=Read<Definition[]>("SpireStrategist.DefectCards.json").ToDictionary(d=>d.Basic.ClassName);
    public static CardSnapshot? Card(string name,bool upgraded=false)=>definitions.TryGetValue(name,out var d)?upgraded?d.Upgraded:d.Basic:null;
    private static bool Has(CardSnapshot c,string tag)=>!MultiplayerAdvice.Ignore(c)&&prior.Cards.TryGetValue(c.ClassName,out var p)&&p.Tags.Contains(tag);
    private static bool Any(CardSnapshot c,params string[] tags)=>tags.Any(t=>Has(c,t));
    private static double Var(CardSnapshot c,string name,double fallback=0)=>c.Variables.TryGetValue(name,out var v)?(double)v:fallback;
    private static int Count(IEnumerable<CardSnapshot> cards,string tag)=>cards.Count(c=>Has(c,tag));

    public static bool Tag(CardSnapshot c,string tag)=>Has(c,tag);
    public static bool MultiplayerCard(string name)=>definitions.TryGetValue(name,out var d)&&d.MultiplayerConstraint=="MultiplayerOnly";
    public static double? UpgradeDelta(CardSnapshot c)=>prior.Cards.TryGetValue(c.ClassName,out var p)?p.UpgradeDelta:null;
    public static CardProfile? ProfileFor(CardSnapshot c)
    {
        if(MultiplayerAdvice.Ignore(c))return null;
        if(!prior.Cards.TryGetValue(c.ClassName,out var p))return null;
        return new(p.Base,Attack:Var(c,"Damage")/10,Block:Var(c,"Block")/10,
            Draw:Has(c,"draw")?Math.Max(.3,(Var(c,"Cards",1)-1)*.6):0,
            Energy:Has(c,"energy")?Var(c,"Energy",1)*.6:0,
            Scaling:Any(c,"focus","power_payoff","dark","claw")?.7:0,
            ExhaustSource:Has(c,"exhaust")?1:0,Upgrade:2+p.UpgradeDelta*3);
    }
    public static Evaluation Evaluate(RunSnapshot state,IReadOnlyList<DecisionOption> options)
    {
        var warnings=new List<string>{Scope,"已参考首批人工实战校准；尚未独立验证，分数不是胜率。", "未完整建模无色牌、遗物、具体敌人及求解器执行水平。"};
        warnings.Insert(0,DefectDamagePlan.Summary(state));
        if(state.Foresight is {} sight)
        {
            warnings.Insert(1,sight.Status);
            foreach(var branch in sight.Branches.Take(3))
                warnings.Add("辉眼条件路线 "+branch.Path+"："+string.Join(" → ",branch.Rewards.Select(r=>string.Join(" / ",r.Cards.Select(c=>c.Name)))));
        }
        var unknown=state.Deck.Where(c=>!prior.Cards.ContainsKey(c.ClassName)&&!DefectNeutral.Known(c.ClassName)&&c.Type is not ("Curse" or "Status")).Select(c=>c.Name).Distinct().ToArray();
        if(unknown.Length>0)warnings.Add("牌组有未适配卡牌，能力评估不完整："+string.Join("、",unknown));
        var valid=state.Character=="Defect"&&state.PlayerCount>=1&&state.MaxHp>0;
        if(state.IsMultiplayer){warnings.Remove(Scope);warnings.Add(MultiplayerAdvice.Notice);}
        var ignored=options.Where(MultiplayerAdvice.Ignore).Select(o=>o.Key).ToHashSet();
        var cardState=state;
        if(state.Hp>0&&state.HpAfterPendingAncient is int recovered&&recovered>state.Hp&&recovered<=state.MaxHp)
        {
            cardState=state with{Hp=recovered};
            warnings.Add($"本幕首领已结束，选牌按下一先古回血后的预计 {recovered} 生命评估；当前生命仍为 {state.Hp}。其他模组可能改变回血。");
        }
        OptionScore Unsupported(DecisionOption o,string note)=>new(o.Key,o.Name,false,o.Enabled,null,[],[note]);
        var scores=options.Select(o=> !valid?Unsupported(o,"仅支持单人故障机器人有效局内状态。"):
            !o.Enabled?new OptionScore(o.Key,o.Name,true,false,null,[],["当前不可选。"]):
            ignored.Contains(o.Key)?Unsupported(o,MultiplayerAdvice.Ignored):
            o.Kind=="skip"?new OptionScore(o.Key,o.Name,true,true,0,[],[]):
            o.Kind=="reroll"?new OptionScore(o.Key,o.Name,true,true,0,[],["原生免费重掷仅在没有已建模正收益选项时使用；不预知重掷结果。"]):
            o.Kind=="card"&&o.Card!=null?Score(cardState,o,o.Card):DefectStrategy.NonCard(state,o)??Unsupported(o,"此类选择尚未适配机器人，请手动处理。")).ToArray();
        DefectForesight.PromoteCompletedEngine(cardState,options,scores);
        var active=scores.Where(s=>s.Enabled&&!ignored.Contains(s.Key)).ToArray();
        var skips=options.Where(o=>o.Kind=="skip").Select(o=>o.Key).ToHashSet();
        var rerolls=options.Where(o=>o.Kind=="reroll").Select(o=>o.Key).ToHashSet();
        string? best=active.Length>0&&active.All(s=>s.Supported&&s.Score.HasValue)?
            active.OrderByDescending(s=>s.Score).ThenByDescending(s=>rerolls.Contains(s.Key)).ThenByDescending(s=>skips.Contains(s.Key)).ThenByDescending(s=>s.Key,StringComparer.Ordinal).First().Key:null;
        if(best==null)warnings.Add("存在未适配选项，暂不作整体推荐；已适配选项的分数仅供参考。");
        return new(Version,best,scores,warnings.ToArray());
    }

    // Explicit automation fallback only. Keep the main recommendation unknown when
    // a candidate cannot be evaluated; never treat that candidate's value as zero.
    public static DecisionOption? KnownFreeReward(IReadOnlyList<DecisionOption> options,Evaluation evaluation)
    {
        var active=options.Where(o=>o.Enabled).ToArray();
        if(!active.Any(o=>o.Kind=="skip")||active.Any(o=>o.Kind is not ("card" or "skip" or "reroll")))return null;
        return active.Select(o=>(Option:o,Score:evaluation.Options.FirstOrDefault(e=>e.Key==o.Key)))
            .Where(x=>x.Score is {Supported:true,Enabled:true,Score:not null}&&x.Score.Score>=0)
            .OrderByDescending(x=>x.Score!.Score).ThenByDescending(x=>x.Option.Kind=="reroll").ThenByDescending(x=>x.Option.Kind=="skip")
            .ThenByDescending(x=>x.Option.Key,StringComparer.Ordinal).Select(x=>x.Option).FirstOrDefault();
    }

    private static OptionScore Score(RunSnapshot s,DecisionOption o,CardSnapshot c)
    {
        var name=c.ClassName;
        OptionScore Unsupported(string reason)=>new(o.Key,o.Name,false,o.Enabled,null,[],[reason]);
        if(!prior.Cards.TryGetValue(name,out var p))return DefectNeutral.Score(s,o,c)??Unsupported("尚未建立机器人语境模型；不能用战士权重替代。");
        if(definitions[name].MultiplayerConstraint=="MultiplayerOnly")return Unsupported("多人专用牌，不参与单人建议。");
        if(CardCatalog.HasUnmodelledModification(c))
            return Unsupported("附魔／异变未建模，不能据此推荐。");
        var deck=s.Deck;var nonbasic=deck.Where(d=>d.Rarity!="Basic").ToArray();
        var tags=deck.Where(d=>!MultiplayerAdvice.Ignore(d)).SelectMany(d=>prior.Cards.TryGetValue(d.ClassName,out var profile)?profile.Tags:[]).ToHashSet();
        bool T(string tag)=>p.Tags.Contains(tag);
        bool A(params string[] values)=>values.Any(T);
        int N(string tag)=>Count(deck,tag);
        var producers=deck.Count(d=>Any(d,"lightning","frost","frost2","frost3","dark","glass"))+
            (s.Relics.Any(r=>r.Split('.').Last() is "CRACKED_CORE" or "INFUSED_CORE")?.5:0);
        var frost=deck.Sum(d=>Has(d,"frost")?1:Has(d,"frost2")?1.5:Has(d,"frost3")?2:0);
        var offense=nonbasic.Where(d=>Has(d,"attack")&&d.ClassName!="FlakCannon").Sum(d=>Math.Min(1.5,Var(d,"Damage")/8));
        var defense=nonbasic.Sum(d=>Math.Min(1.5,Var(d,"Block")/8))+frost*.65;
        var draw=deck.Where(d=>Has(d,"draw")).Sum(d=>Math.Min(2,Math.Max(0,Var(d,"Cards",1)-1)));
        var powers=deck.Count(d=>d.Type=="Power");
        var status=N("status_source")+deck.Count(d=>d.ClassName=="FightThrough");
        var storm=deck.Where(d=>d.ClassName=="Storm").Sum(d=>Math.Min(3,Math.Max(0,powers-1)*.65)*Var(d,"StormPower",1));
        var lightning=N("lightning")+Math.Min(4,storm);producers+=Math.Min(3,storm);
        // Owned Voltaic can amplify an existing Lightning seed; an offered copy
        // cannot bootstrap itself out of zero channels.
        var lightningAmplifier=lightning>0||s.Relics.Any(r=>r.Split('.').Last()=="CRACKED_CORE")
            ?Math.Min(3,deck.Where(d=>d.ClassName=="Voltaic").Sum(d=>d.UpgradeLevel>0?2:1)):0;
        var directOffense=offense;
        offense+=Math.Min(2,lightning*.35+N("dark")*.3+N("glass")*.35)+deck.Count(d=>d.ClassName=="FlakCannon")*Math.Min(1.5,status*.4);
        var energySupport=N("energy")+N("plasma")+Math.Min(1,N("power_energy")*Math.Max(0,powers-1)*.4);
        var clear=N("status_clear");
        var zeroDensity=(double)deck.Count(d=>d.Cost==0&&!d.CostsX)/Math.Max(1,deck.Length);
        var retrievalZero=deck.Count(d=>d.Cost==0&&!d.CostsX&&(d.Type is "Attack" or "Skill")&&!d.Keywords.Contains("Exhaust"));
        var physical=deck.Count(d=>d.ClassName is "Claw" or "AllForOne" or "Scrape" or "Feral" or "HelixDrill" or "MomentumStrike" or "Ftl" or "GoForTheEyes");
        var upgradeBacklog=deck.Count(d=>d.UpgradeLevel==0&&d.CanUpgrade&&prior.Cards.TryGetValue(d.ClassName,out var dp)&&dp.UpgradeDelta>=2);
        var spend=nonbasic.Count(d=>d.Cost>=2||d.CostsX);
        var zero=deck.Count(d=>d.Type=="Attack"&&d.Cost==0&&!d.CostsX);
        var act=Math.Clamp(s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act,1,3);
        var k=prior.Coefficients;var parts=new List<ScorePart>();
        void Add(string reason,double value){if(value!=0)parts.Add(new(reason,Math.Round(value,3)));}
        Add("基础先验（尚未拟合）",p.Base);Add("前／中／后期需求",p.Phase[act-1]);Add("占用一次抽牌的基准成本",-k.CardTax);
        if(!T("basic"))
        {
            if(T("attack")&&name!="FlakCannon")Add("即时输出缺口",k.DamageNeed*Math.Max(0,1-offense/4)*Math.Min(1.5,Var(c,"Damage")/8));
            if(A("block","frost","frost2","frost3","weak"))Add("防御缺口",k.BlockNeed*Math.Max(0,1-defense/5));
            if(A("aoe","glass"))Add("缺少群体输出",tags.Contains("aoe")||tags.Contains("glass")?0:1.2);
        }
        var n=deck.Count(d=>d.ClassName==name);
        var reusablePowerTargets=deck.Count(d=>d.Type=="Power"&&d.ClassName!="CreativeAi");
        var twoExhaustingHolograms=name=="Hologram"&&n==1&&c.UpgradeLevel==0
            &&deck.Single(d=>d.ClassName=="Hologram").UpgradeLevel==0;
        Add(twoExhaustingHolograms?"第二张未敲全息提供另一次消耗回收":"重复份数的边际递减",
            twoExhaustingHolograms?-.9:name=="SignalBoost"&&reusablePowerTargets>n?-(.8*n+.35*n*n):-(p.Duplicate*n+(name=="Claw"?.12:name=="DoubleEnergy"?.2:k.DuplicateQuadratic)*n*n));
        if(c.UpgradeLevel>0)Add("该牌已升级的实际机制增益先验",p.UpgradeDelta);
        if(T("draw")&&c.Type!="Power"&&name!="Reboot")
        {
            Add("净抽牌与可用费用配合",Math.Min(2,Math.Max(0,Var(c,"Cards",1)-1))*.6*Math.Min(1,(spend+1d)/3));
            Add("已有过牌饱和",-Math.Max(0,draw-(DefectComposition.Physical(s)>=3?12:8))*.35);
        }
        if(T("conditional_draw"))Add("有条件自替换折扣补偿",.7);
        var diversity=new[]{"lightning","dark","glass","plasma"}.Count(tags.Contains)+(frost>0?1:0);
        if(T("diversity_draw"))Add("可实现球种的过牌折扣",Math.Min(2,diversity)*.7);
        var synergies=new List<ScorePart>();void Synergy(string reason,double value)=>synergies.Add(new(reason,value));
        if(A("focus","temp_focus"))
        {
            Synergy("集中需要非等离子产球",producers>0?Math.Min(4,producers*.8)-2:-8);
            if(T("temp_focus"))Synergy("临时集中不算永久成长",producers<3?-1.5:0);
        }
        if(A("frost","frost2","frost3","lightning","dark","glass"))Synergy("现有永久集中支持产球",Math.Min(2,N("focus")));
        if(T("slots"))
        {
            Synergy("扩容需要产球速度与球质量",Math.Min(4,producers*.8)-3);
            Synergy("球位组件饱和",-N("slots")*1.5);
            if(N("dark")>0)Synergy("扩容保护黑暗蓄力，减轻过早顶球",Math.Min(3,N("dark")*.7+deck.Count(d=>d.ClassName=="Darkness")*.8));
            else if(storm>0||deck.Any(d=>d.ClassName=="Thunder"))Synergy("扩容会推迟依赖顶球的激发",-1.2);
        }
        if(A("orb_payoff","orb_attack")&&name!="Loop")Synergy("球占用率与质量折扣",Math.Min(4,producers*.9)-2);
        if(name=="Loop")Synergy("循环只重复最右侧被动，不因球多就视为核心",N("dark")>0?Math.Min(2,N("dark")*.7+N("focus")*.3):N("focus")>=2&&frost>0?1:-2.5);
        if(A("evoke","evoke_all"))
        {
            Synergy("激发黑暗与等离子",Math.Min(3,N("dark")+N("plasma")));
            if(T("evoke_all")&&frost>=2)Synergy("清空冰霜后仍需补球防御",-.8);
        }
        if(name=="Darkness")
        {
            Synergy("漆黑触发已有黑暗被动",Math.Min(3,N("dark")*(c.UpgradeLevel>0?1.1:.7)));
            Synergy("蓄黑球已有空间和激发出口",Math.Min(2,N("slots")*.6+deck.Count(d=>d.ClassName is "Dualcast" or "Quadcast" or "MultiCast" or "Shatter")*.5));
            if(deck.Any(d=>d.ClassName=="Storm")&&deck.Any(d=>d.ClassName=="Thunder")&&!deck.Any(d=>d.ClassName is "Darkness" or "ConsumingShadow"))
                Synergy("成熟电球体系不另起蓄黑球支线",-7);
        }
        if((name is "Dualcast" or "Quadcast" or "MultiCast" or "Shatter")&&N("dark")>0)
        {
            var multiplier=name=="Dualcast"?1.8:name=="Quadcast"?2.8:name=="Shatter"?2.4:Math.Min(2.8,Math.Max(0,s.Energy-1)*.7+energySupport*.3);
            Synergy("多次激发兑现蓄黑球（排球需另行实现）",Math.Min(3,(N("dark")*.6+deck.Count(d=>d.ClassName=="Darkness")*.6)*multiplier));
            if(name=="MultiCast")Synergy("X费黑球终端仍需留防守费用",-.5);
        }
        if(T("frost_payoff"))Synergy("需要稳定冰霜",Math.Min(4,frost*1.5)-3);
        if(name=="Hailstorm")Synergy("冰霜终端需要保有冰球，频繁产电会挤走冰球",frost<2?-2:storm>0?-1.5:0);
        if(T("lightning_payoff"))Synergy("需要足够闪电生成",Math.Min(5,(lightning+lightningAmplifier+(name=="Voltaic"&&s.Relics.Any(r=>r.Split('.').Last()=="CRACKED_CORE")?1:0))*1.2)-3);
        if(name=="Thunder")Synergy("额外激发渠道",Math.Min(1.5,N("evoke")*.5));
        if(name=="Thunder"&&n==0&&lightningAmplifier>0)
            Synergy("已有电流相生批量产电顶球，首张雷霆兑现连续激发",deck.Any(d=>d.ClassName=="Voltaic"&&d.UpgradeLevel>0)?4:2);
        if(T("diversity_payoff"))Synergy("可靠球种折扣",Math.Min(3,diversity)-2);
        if(T("status_clear"))
        {
            Synergy("敌方塞状态的保险价值（折扣先验）",new[]{.7,1.2,1.6}[act-1]);
            Synergy("自造状态与全牌堆／手牌处理配合",Math.Min(4,status*(name=="FlakCannon"?1.3:1.0)));
            Synergy("第一张状态处理补缺",clear==0?1.4:status<2?-1.4:.3);
            if(name=="FlakCannon")Synergy("无自造状态时散射炮可能空转",status==0?-3:0);
        }
        else if(A("status_payoff","status_draw","status_discount"))Synergy("真实状态生成来源",Math.Min(5,status*1.3)-2.5);
        if(T("status_source"))
        {
            Synergy("状态后续抽牌代价",-1.5);
            Synergy("已有状态转化／输出引擎",Math.Min(3,N("status_payoff")+N("status_clear")+N("status_draw")*.5));
        }
        if(A("power_payoff","power_discount"))Synergy("可兑现能力牌支持度",Math.Min(5,powers*1.2)-3);
        if(T("zero_payoff"))
        {
            var z=name=="Feral"?zero:name=="AllForOne"?retrievalZero:deck.Count(d=>d.Cost==0&&!d.CostsX);
            Synergy("现有零费密度和质量",Math.Min(5,z*1.15)-3);
            if(name=="AllForOne"&&z==0)Synergy("没有可回收零费牌，不预购回收终端",-3);
            if(name=="Scrape")
            {
                Synergy("刮削留下零费牌的密度",Math.Min(3,Var(c,"Cards",4)*zeroDensity*1.5)-1.5);
                Synergy("丢弃非零费核心的风险",powers>=2?-1.5:0);
            }
        }
        if(T("claw"))
        {
            var cycle=deck.Sum(d=>d.ClassName=="Hologram"?(d.UpgradeLevel>0?1.5:.6):d.ClassName=="AllForOne"?2:d.ClassName=="Feral"?1.5:d.ClassName=="Scrape"&&zeroDensity>=.18?1:0);
            Synergy("爪击必须已有回收或循环",Math.Min(4,cycle+draw*.3)-1.5);
            Synergy("已有爪群与可靠回收",cycle>=1.5?Math.Min(1.5,deck.Count(d=>d.ClassName=="Claw")*.75):0);
        }
        if(T("physical_support"))Synergy("直接攻击支持度；易伤不增强球",Math.Min(3,directOffense*.6)-1);
        if(name=="BulkUp")Synergy("物理循环兑现力量与敏捷",Math.Min(3,physical*.75));
        if(T("slot_loss"))
        {
            var dark=N("dark");var cycling=lightning+frost+N("glass")+N("plasma");
            Synergy("减少球位压缩黑暗蓄力空间",-Math.Min(5,dark*.85+deck.Count(d=>d.ClassName=="Darkness")*.9+(dark>0?N("slots")*.35+N("orb_payoff")*.3:0)));
            var passive=frost*.3+N("focus")*.3+N("orb_payoff")*.4;
            Synergy("保留球被动的损失，按补球能力折扣",-Math.Min(2.5,passive)/(1+cycling*.25));
            if(dark==0&&cycling>=2&&N("slot_loss")<2)Synergy("少球位使新球更早顶球激发（非额外产球）",Math.Min(1.2,cycling*.2));
            Synergy("减槽当场直接移除球，不触发激发",producers>0||N("plasma")>0?-.35:0);
            if(N("slot_loss")>=2&&(producers>0||N("plasma")>0))Synergy("多张减槽可能清空球位，零球位不能生成",-3);
        }
        if(name=="BulkUp")Synergy("直接格挡技能兑现敏捷",Math.Min(2.2,deck.Count(d=>d.Type=="Skill"&&Var(d,"Block")>0)*.25));
        if(A("energy","delayed_energy")&&name is not ("DoubleEnergy" or "Supercritical"))Synergy("能量需要花费出口和过牌",Math.Min(3,spend*.45+draw*.35)-1);
        if(T("energy_payoff"))Synergy("能量兑现先验",Math.Min(3,Math.Max(0,s.Energy-3)+energySupport*.4+N("plasma")*.1));
        if(T("exhaust"))Synergy("处理诅咒状态及剩余基础牌",Math.Min(2,deck.Count(d=>d.Type is "Curse" or "Status")+deck.Count(d=>d.Rarity=="Basic")*.12));
        if(name=="Hologram")
        {
            var targets=deck.Count(d=>d.Rarity!="Basic"&&d.Type is "Attack" or "Skill"&&d.ClassName!="Hologram");
            Synergy("定向回收优质牌与运转组件",Math.Min(2.5,targets*.3+physical*.2));
        }
        if(name=="Turbo")
        {
            Synergy("首张返能补足启动缺口",n==0?Math.Max(0,1.2-energySupport*.6):0);
            Synergy("有过牌时兑现额外能量",Math.Min(1.5,draw*.35));
        }
        if(name is "Storm" or "Subroutine")Synergy("现有能力引擎互相触发",Math.Min(1.5,deck.Count(d=>d.ClassName!=name&&d.ClassName is "Storm" or "Thunder" or "Subroutine")*.75));
        if(name=="Storm"&&n==0&&powers>=2)Synergy("补齐已有雷霆或子程序的产电核心",Math.Min(2,N("power_energy")+deck.Count(d=>d.ClassName=="Thunder")));
        if(name=="Voltaic")Synergy("雷暴产电与雷霆终端配合",storm>0?Math.Min(3,storm*.8+N("lightning_payoff")*.6):0);
        if(name=="CreativeAi")Synergy("随机慢启动需要成熟的防守与费用",defense>=5&&energySupport>=2&&powers>=3?1.5:-1.5);
        var positive=synergies.Sum(p=>Math.Max(0,p.Value));var factor=positive>0?Math.Min(1,k.SynergyCap/positive):1;
        foreach(var part in synergies)Add(part.Reason,part.Value>0?part.Value*factor:part.Value);
        Add("明确核心搭档的成型收益（含联动上限补偿）",Math.Min(DefectEngineLinks.Budget(s,c),2+Math.Max(0,positive-k.SynergyCap)));
        if(T("meteor"))
        {
            var support=s.Energy>=5||deck.Any(d=>d.ClassName=="Supercritical")||(s.Energy>=4&&(N("energy")>0||N("plasma")>0))
                ||deck.Any(d=>d.ClassName=="DoubleEnergy"&&(d.Cost==0||s.Energy>=4))
                ||(s.Relics.Any(r=>r.Split('.').Last()=="ICE_CREAM")&&deck.Any(d=>d.ClassName is "DoubleEnergy" or "Turbo" or "Fusion"));
            Add("五费启动可行性",support?-2:-12);if(support)Add("有启动渠道后可评估陨石循环",4);
            if(deck.Any(d=>d.ClassName=="Catastrophe"))Add("横祸可能免付启动费，但随机打出不能保证命中陨石",support?1:2);
        }
        else if(c.Cost>=3)Add("高费启动与防守竞争",-Math.Max(0,c.Cost-s.Energy+1)*1.2);
        if(c.Type=="Power")
        {
            Add("能力牌集中启动负担",-Math.Max(0,powers-3-Math.Min(2,N("power_energy")))*.55);
            if((double)s.Hp/Math.Max(1,s.MaxHp)<.35&&defense<3)Add("低血且防守不足，暂缓慢启动",-2);
        }
        if(T("growing"))
        {
            Add("此张已实际养成的格挡",Math.Min(6,Math.Max(0,Var(c,"Block",1)-1)/6));
            if(act==1&&s.Map is {BossRow:>0} map)Add("第一幕临近结束，培养时间折扣",-2*Math.Clamp((double)map.CurrentRow/map.BossRow,0,1));
        }
        if(name=="Hyperbeam"&&frost>1)Add("当回合降低冰霜防御",-.8);
        if(T("glass")&&act==3&&N("focus")==0&&name!="Spinner")Add("玻璃衰减，缺乏长战补偿",-1);
        if(c.UpgradeLevel==0&&p.UpgradeDelta>=2)Add("待敲核心积压，不能预支所有升级",-Math.Min(1.25,Math.Max(0,upgradeBacklog-2)*.25));
        Add("大牌组抽到关键组件更慢",-Math.Min(2,Math.Max(0,deck.Length-22)*.1));
        // Utility must be valued by its actual role; a one-card replacement is
        // not a draw engine, and another marginal copy is not a new capability.
        if(name=="Coolheaded"&&c.UpgradeLevel==0)
        {
            Add("未敲冷静仅自替换，升级与现有核心竞争",-1.5-Math.Min(1.5,upgradeBacklog*.5));
            if(act==1&&N("focus")==0&&DefectStrategy.Readiness(s).Offense<.65)
                Add("一幕缺即时战力，未敲冷静不能替代输出过渡",-1.5);
        }
        if((name is "Turbo" or "Hotfix")&&n>0)
            Add("重复短效资源牌需要明确收益余量",-1);
        if(name=="Iteration"&&n==0)
            Add("首张迭代补偿状态抽牌：每回合首次触发，不消除状态",.8+Math.Min(2.4,status*1.2));
        if(name=="Supercritical")
        {
            var playable=deck.Where(d=>d.Type is "Attack" or "Skill" or "Power").ToArray();
            var average=playable.Length==0?0:playable.Average(d=>d.CostsX?2:Math.Clamp(d.Cost,0,3));
            var recalls=deck.Count(d=>d.ClassName is "Hologram" or "AllForOne");
            var handSpend=average*(4+Math.Min(3,draw*.5+recalls*.5));
            var storage=s.Relics.Any(r=>r.Split('.').Last()=="ICE_CREAM")?2:0;
            var usable=Math.Min(Var(c,"Energy",4),Math.Max(0,handSpend-s.Energy)+spend*.5+recalls*.3+storage);
            Add("零费爆发供能可支付多张低费牌及回收，按可用量折扣",Math.Min(4,usable*.9)/(1+n));
            if(n==0)Add("首张无副作用爆发的择机使用价值",1.2*Math.Min(1,usable));
            Add("已有供能饱和，额外能量不能当作必然兑现",-Math.Max(0,energySupport-2)*.6);
        }
        if(name=="Reboot"&&n==0)
        {
            // Native Reboot shuffles the remaining hand into draw and draws 4 (6+).
            // It neither permanently removes bad cards nor grants four NET draws.
            Add("零费重启在出牌后重抽手牌，消耗后不长期占循环",1.4+Math.Min(2.1,Var(c,"Cards",4)*.35));
            Add("重启帮助兑现已有供能并重新寻找关键牌",Math.Min(1.5,energySupport*.4+deck.Count(d=>d.ClassName is "AdaptiveStrike" or "AllForOne" or "Darkness" or "Storm" or "Thunder" or "Defragment")*.3));
        }
        if(name=="Defragment"&&n>0&&frost>=2)
            Add("额外集中仍能强化现有冰霜防线",Math.Min(1.5,n));
        if(name=="MultiCast"&&c.UpgradeLevel==0&&s.Energy<=3
            &&deck.Any(d=>d.ClassName=="Dualcast"&&d.Cost==0)&&deck.Any(d=>d.ClassName=="Hologram")
            &&!deck.Any(d=>d.ClassName is "Turbo" or "DoubleEnergy" or "Supercritical")&&N("plasma")==0
            &&!s.Relics.Contains("RELIC.CHEMICAL_X"))
            Add("缺爆发费用时，多重释放相对零费双放加全息的边际收益有限",-3);
        foreach(var part in DefectDirection.Adjustments(s,c,Has))Add(part.Reason,part.Value);
        foreach(var part in DefectComposition.Adjustments(s,c))Add(part.Reason,part.Value);
        foreach(var part in DefectDamagePlan.Adjustments(s,c))Add(part.Reason,part.Value);
        foreach(var part in DefectForesight.Adjustments(s,c))Add(part.Reason,part.Value);
        foreach(var part in DefectRelics.CardAdjustments(s,c))Add(part.Reason,part.Value);
        if(name=="SignalBoost")
        {
            var targets=deck.Count(d=>d.Type=="Power"&&d.ClassName!="CreativeAi");
            Add("信号增强按可复制能力数量评估重复份数",targets>n?Math.Min(3,1.5+targets*.4): -3);
        }
        if(c.Enchantment=="ENCHANTMENT.GLAM")Add("华彩：每场首次打出重放一次",NativeEnchantments.Glam(c));
        if(CopyEngine.Swift(c)&&!CopyEngine.SelfCopies(c))Add("迅捷首次打出抽牌，普通牌不重复触发",Math.Min(4,c.EnchantmentAmount*1.2));
        foreach(var part in CopyEngine.Adjustments(s,c))Add(part.Reason,part.Value);
        foreach(var part in DefectComposition.TransitionAdjustments(s,c))Add(part.Reason,part.Value);
        if(name=="Compact"&&n>0)
        {
            if(DefectComposition.NeedsSecondCompact(s))
                Add("高密度真实状态负担允许第二张压缩，收益仍封顶",Math.Min(3,1.5-parts.Sum(p=>p.Value)));
            else Add("已有压缩且状态负担不足，不重复补同一工具",Math.Min(0,-.25-parts.Sum(p=>p.Value)));
        }
        return new(o.Key,o.Name,true,o.Enabled,Math.Round(parts.Sum(p=>p.Value),3),parts.ToArray(),["已参考首批人工实战校准；尚未独立验证，分数不是胜率。"]);
    }
}
