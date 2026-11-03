using System.Text.Json;

namespace SpireStrategist.Core;

// Hunter acquisition model with shared native actions and hunter-specific adapters.
public static class SilentPolicy
{
    public const string Version = "silent-draft-0.2";
    public const string Scope = "猎人接管：选牌、营火、路线、商店与原生事件；战斗交给 CombatSolver。机制权重尚待实战校准。";
    public sealed record Profile(double Base, double[] Phase, double Duplicate, double UpgradeDelta,
        double EvidenceAdjustment, string[] Tags, string Note);
    private sealed record Prior(string Version, Dictionary<string, Profile> Cards);
    private sealed record Definition(string MultiplayerConstraint, CardSnapshot Basic, CardSnapshot Upgraded);
    private static T Read<T>(string name)
    {
        using var stream=typeof(SilentPolicy).Assembly.GetManifestResourceStream("SpireStrategist."+name+".json")
            ?? throw new InvalidDataException(name);
        return JsonSerializer.Deserialize<T>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    private static readonly Prior prior=Read<Prior>("SilentPrior");
    private static readonly Dictionary<string,Definition> definitions=Read<Definition[]>("SilentCards").ToDictionary(x=>x.Basic.ClassName);
    public static IReadOnlyDictionary<string,Profile> Profiles => prior.Cards;
    public static CardSnapshot? Card(string name,bool upgraded=false) => definitions.TryGetValue(name,out var d)?upgraded?d.Upgraded:d.Basic:null;
    public static bool MultiplayerCard(string name)=>definitions.TryGetValue(name,out var d)&&d.MultiplayerConstraint=="MultiplayerOnly";
    public static bool Tag(CardSnapshot c,string tag)=>!c.MultiplayerOnly&&!MultiplayerCard(c.ClassName)
        &&prior.Cards.TryGetValue(c.ClassName,out var p)&&p.Tags.Contains(tag);
    private static double V(CardSnapshot c,string key,double fallback=0)=>c.Variables.TryGetValue(key,out var n)?(double)n:fallback;

    // All counts are actual owned cards. Pending rewards/forecasts are never deck members.
    public sealed record Composition(double Immediate, double Defense, double Draw, double Discard,
        double Shivs, double PoisonSeeds, double Poison, double CheapPlays, double Energy,
        int Sly, int Weak, int Retain, int DamageEngines, string[] Plans, string[] Missing,
        string PrimaryDamage, double ShivInvestment, double PoisonInvestment);

    public static Composition Analyze(RunSnapshot s)
    {
        var deck=s.Deck.Where(c=>!c.MultiplayerOnly&&!MultiplayerCard(c.ClassName)).ToArray();
        bool Has(string name)=>deck.Any(c=>c.ClassName==name);
        bool Relic(string name)=>s.Relics.Any(r=>r.Split('.').Last()==name);
        int N(string tag)=>deck.Count(c=>Tag(c,tag));
        // Distinguish an incidental transition/bridge from investment in a damage plan.
        // Active discard, draw and defense are support, never votes for another archetype.
        var shivInvestment=N("shiv_source")*.7+N("shiv_payoff")*2
            +(N("shiv_source")>=2&&Has("Finisher")?1.2:0);
        var poisonInvestment=deck.Sum(c=>Tag(c,"poison_seed")?(c.ClassName=="PoisonedStab"?.25:1.4):0)
            +N("poison_payoff")*1.8+N("poison_conditional")*.6;
        var primary=shivInvestment>=1.8&&shivInvestment>=poisonInvestment+1?"小刀":
            poisonInvestment>=1.8&&poisonInvestment>=shivInvestment+1?"毒":"未确定";
        var density=Math.Clamp(15d/Math.Max(10,deck.Length),.55,1.3);
        var discard=deck.Sum(c=>c.ClassName switch {
            "Survivor"=>.3,"Acrobatics" or "Prepared" or "DaggerThrow"=>1,
            "HiddenDaggers"=>1.4,"CalculatedGamble"=>c.UpgradeLevel>0?1.4:1.2,
            "ToolsOfTheTrade"=>1.6,"StormOfSteel"=>1.1,"ShadowStep"=>.5,_=>0})*density;
        double SlyReliability(CardSnapshot c)=>Tag(c,"sly")?Math.Clamp(discard/2.5,.1,1):1;
        var draw=deck.Sum(c=>c.ClassName switch {
            "Acrobatics"=>V(c,"Cards",3),"Backflip" or "Adrenaline" or "Expertise"=>V(c,"Cards",2),
            "EscapePlan"=>.6,"Prepared"=>.5*V(c,"Cards",1),"Reflex"=>V(c,"Cards",2)*SlyReliability(c),
            "CalculatedGamble"=>c.UpgradeLevel>0?2:1.8,"ToolsOfTheTrade"=>1,"Predator"=>.7,_=>0})*density;
        var shivs=deck.Sum(c=>c.ClassName switch {
            "BladeDance"=>V(c,"Cards",3)*.6,"BladeOfInk"=>V(c,"Cards",2),
            "CloakAndDagger"=>V(c,"Cards",1),"LeadingStrike"=>2,"HiddenDaggers"=>1.6,
            "InfiniteBlades"=>1.2,"UpMySleeve"=>V(c,"Cards",3)*.65,"FanOfKnives"=>V(c,"Shivs",4)*.5,
            "StormOfSteel"=>Math.Min(3,1.2+draw*.15),_=>0})*density;
        if(Relic("NINJA_SCROLL"))shivs+=1; // Three opening-only knives, not repeatable generation.
        var seeds=deck.Where(c=>Tag(c,"poison_seed")).Sum(c=>c.ClassName=="PoisonedStab"?.5:1);
        if(Relic("TWISTED_FUNNEL"))seeds+=.5;
        if(Has("Envenom")&&shivs>=1.5)seeds+=1;
        if(Has("CorrosiveWave")&&draw>=4)seeds+=.8;
        var poison=deck.Sum(c=>c.ClassName switch {
            "BouncingFlask"=>V(c,"PoisonPower",3)*V(c,"Repeat",3)*.65,
            "NoxiousFumes"=>V(c,"PoisonPerTurn",2)*2.5,
            "BubbleBubble"=>seeds>0?V(c,"PoisonPower",9)*.8:0,
            "Envenom"=>shivs>=1.5?shivs*V(c,"EnvenomPower",1):0,
            "CorrosiveWave"=>draw>=4?draw*.5*V(c,"CorrosiveWave",2):0,
            _=>V(c,"PoisonPower")})*density;
        if(Relic("TWISTED_FUNNEL"))poison+=4;
        if(Relic("SNECKO_SKULL"))poison+=Math.Min(4,seeds);
        var immediate=deck.Where(c=>!Tag(c,"basic")).Sum(c=>
            (V(c,"Damage")*(Tag(c,"multihit")?(c.ClassName=="Ricochet"?V(c,"Repeat",4):c.ClassName=="DaggerSpray"?2:1):1)/10
            +(c.ClassName=="MementoMori"?(9+4*Math.Min(3,discard))/10:0))*SlyReliability(c));
        immediate+=shivs*.4+Math.Min(2,poison*.12);
        var strength=SilentMechanics.StrengthSupport(s,shivs);
        immediate+=Math.Min(4,shivs*strength*.1);
        var defense=deck.Where(c=>!Tag(c,"basic")).Sum(c=>V(c,"Block")/8*SlyReliability(c))
            +N("weak")*.3+N("strength_down")*.6+N("dex")*.5;
        var cheap=deck.Count(c=>!Tag(c,"basic")&&c.Cost==0&&!c.CostsX)*.5+shivs+Math.Min(3,discard*N("sly")*.4);
        if(Has("Afterimage"))defense+=Math.Min(3,1+cheap*.2+draw*.15);
        if(Relic("TOUGH_BANDAGES"))defense+=Math.Min(3,discard*.6);
        if(Relic("HELICAL_DART"))defense+=Math.Min(2,shivs*.3);
        if(Relic("PAPER_KRANE"))defense+=Math.Min(1,N("weak")*.3);
        var energy=deck.Sum(c=>c.ClassName=="Adrenaline"?V(c,"Energy",1):c.ClassName=="Tactician"?V(c,"Energy",1)*SlyReliability(c):c.ClassName=="Sidestep"?.4:0);
        var plans=new List<string>();var engines=0;
        if(shivs>=1.5&&(Has("KnifeTrap")||Has("Accuracy")||Has("PhantomBlades"))) { plans.Add("小刀：已有产刀及输出放大／回放");engines++; }
        else if(shivs>=1)plans.Add("小刀入口：已有产刀，尚缺长战放大");
        if(shivs>=1.5&&strength>=2&&engines==0){plans.Add("小刀：已有产刀和实际力量来源");engines++;}
        if(seeds>0&&(Has("Accelerant")||Has("Outbreak")||Has("NoxiousFumes")&&poison>=8)) {plans.Add("毒：已有施毒与持续增长／触发");engines++;}
        else if(seeds>0)plans.Add("毒入口：已有施毒，尚缺足量增长");
        if(Has("SerpentForm")&&cheap+draw>=4||Has("Murder")&&draw>=5||Has("Speedster")&&draw>=6)
        { plans.Add("运转伤害：已有出牌／抽牌伤害出口");engines++; }
        if(Has("Tracking")&&N("weak")>=2&&immediate>=4) { plans.Add("虚弱攻击：已有虚弱覆盖及攻击出口");engines++; }
        if(discard>=2&&N("sly")>0)plans.Add("弃牌奇巧：运转支撑（不独立代表输出充足）");
        if(Relic("TINGSHA")&&discard>=3){plans.Add("铜钹：主动弃牌形成额外持续输出");engines++;}
        if(plans.Count==0)plans.Add("开放构筑：尚未形成可靠输出链");
        var missing=new List<string>();
        if(immediate<3)missing.Add("即时输出");
        if(engines==0)missing.Add("长战输出／成长");
        if(defense<2.5)missing.Add("持续防守");
        if(N("sly")>0&&discard<2)missing.Add("可靠主动弃牌");
        return new(immediate,defense,draw,discard,shivs,seeds,poison,cheap,energy,N("sly"),N("weak"),N("retain"),engines,plans.ToArray(),missing.ToArray(),primary,shivInvestment,poisonInvestment);
    }

    public static Evaluation Evaluate(RunSnapshot s,IReadOnlyList<DecisionOption> options)
    {
        if(prior.Version!=Version)throw new InvalidDataException("Silent prior/code version mismatch");
        var view=Analyze(s);
        var warnings=new List<string>{s.IsMultiplayer?MultiplayerAdvice.Notice:Scope,
            "基础先验是机制假设；社区统计仅作有限修正，分数不是胜率。",
            "主要输出方向："+view.PrimaryDamage+"；弃牌、过牌、供能、防守按辅助需求评估。",
            string.Join("；",view.Plans),"当前缺口："+(view.Missing.Length==0?"需进一步战斗验证":string.Join("、",view.Missing))};
        var unknown=s.Deck.Where(c=>!prior.Cards.ContainsKey(c.ClassName)&&!MultiplayerCard(c.ClassName)&&c.Type is not ("Curse" or "Status")).Select(c=>c.Name).Distinct().ToArray();
        if(unknown.Length>0)warnings.Add("未建模牌组内容，体系估计不完整："+string.Join("、",unknown));
        if(s.IsMultiplayer)warnings.Add(MultiplayerAdvice.Notice);
        var ignored=options.Where(o=>o.Card is {} c&&(c.MultiplayerOnly||MultiplayerCard(c.ClassName))).Select(o=>o.Key).ToHashSet();
        OptionScore Unsupported(DecisionOption o,string why)=>new(o.Key,o.Name,false,o.Enabled,null,[],[why]);
        var scores=options.Select(o=>s.Character!="Silent"||s.MaxHp<=0||s.PlayerCount<1?Unsupported(o,"仅适用有效猎人局面。"):
            !o.Enabled?new OptionScore(o.Key,o.Name,true,false,null,[],["当前不可选。"]):
            ignored.Contains(o.Key)?Unsupported(o,MultiplayerAdvice.Ignored):
            o.Kind=="skip"?new OptionScore(o.Key,o.Name,true,true,0,[],[]):
            o.Kind=="reroll"?new OptionScore(o.Key,o.Name,true,true,0,[],["免费重掷只在没有明确正收益选择时使用。"]):
            o.Kind=="card"&&o.Card!=null?Score(s,view,o,o.Card):DefectStrategy.NonCard(s,o)??Unsupported(o,"尚未适配此动作。")).ToArray();
        var active=scores.Where(x=>x.Enabled&&!ignored.Contains(x.Key)).ToArray();
        var skips=options.Where(x=>x.Kind=="skip").Select(x=>x.Key).ToHashSet();
        var best=active.Length>0&&active.All(x=>x.Supported&&x.Score.HasValue)
            ?active.OrderByDescending(x=>x.Score).ThenByDescending(x=>skips.Contains(x.Key)).ThenBy(x=>x.Key,StringComparer.Ordinal).First().Key:null;
        if(best==null)warnings.Add("存在未知选项，不作完整排序；已建模分数仅供参考。");
        return new(Version,best,scores,warnings.ToArray());
    }

    private static OptionScore Score(RunSnapshot s,Composition a,DecisionOption o,CardSnapshot c)
    {
        OptionScore Unknown(string reason)=>new(o.Key,o.Name,false,o.Enabled,null,[],[reason]);
        if(!prior.Cards.TryGetValue(c.ClassName,out var p))return DefectNeutral.Score(s,o,c)??Unknown("尚未建立此卡的猎人语境模型。");
        if(CardCatalog.HasUnmodelledModification(c))return Unknown("未知附魔／异变额外机制，保留手动选择。");
        var name=c.ClassName;var act=Math.Clamp(s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act,1,3);
        bool T(string tag)=>p.Tags.Contains(tag);
        int N(string tag)=>s.Deck.Count(x=>Tag(x,tag));
        bool Has(string id)=>s.Deck.Any(x=>x.ClassName==id);
        var n=s.Deck.Count(x=>x.ClassName==name);
        var parts=new List<ScorePart>();double positive=0;
        void Add(string reason,double value){if(Math.Abs(value)>.00001)parts.Add(new(reason,Math.Round(value,3)));}
        void Link(string reason,double value)
        {
            if(value>0){value=Math.Min(value,Math.Max(0,6-positive));positive+=value;}
            Add(reason,value);
        }
        Add("基础机制先验（未实战校准）",p.Base);
        Add("同版本单人A10同稀有度统计修正（有限幅度）",p.EvidenceAdjustment);
        Add("前／中／后期需求",p.Phase[act-1]);Add("占用一次抽牌的基准成本",-2.5);
        if(name=="CalculatedGamble")
        {
            // Exhausting hand refresh: a thick deck can use several, but never unlimited copies.
            var capacity=1+Math.Clamp((s.Deck.Length-15)/15d,0,2);
            var excess=Math.Max(0,n-capacity);
            Add("计算下注：按牌组厚度评估重复刷新需求",-.65*n-.2*n*n-1.6*excess*excess);
            Link("大牌组刷新手牌与金字塔腾出手牌位",Math.Min(2,Math.Max(0,s.Deck.Length-20)*.08)+(SilentMechanics.Has(s,"RUNIC_PYRAMID")?1.2:0));
        }
        else Add("重复份数递减",-p.Duplicate*n-.45*n*n);
        if(name=="KnifeTrap"&&n>0)Add("已有可反复回放的刀刃陷阱，重复二费终端及敲牌需求",-8*n);
        if(c.UpgradeLevel>0&&!SilentMechanics.RedundantRetainUpgrade(s,c))Add("已升级的实际收益",p.UpgradeDelta);
        if(c.Enchantment=="ENCHANTMENT.GLAM")Add("华彩：每场首次打出重放一次",NativeEnchantments.Glam(c));
        if(!string.IsNullOrEmpty(c.Enchantment))
        {
            var delta=c.Enchantment.Split('.').Last() switch{
                "SWIFT"=>Math.Min(4,c.EnchantmentAmount*1.2),"SOWN"=>Math.Min(4,c.EnchantmentAmount*1.4),
                "ADROIT" or "NIMBLE"=>c.EnchantmentAmount*.35,"GOOPY"=>c.EnchantmentAmount*.25-(T("block")?1:0),
                "SHARP"=>c.EnchantmentAmount*.25,"VIGOROUS"=>c.EnchantmentAmount*.15,_=>0};
            Add("已核验附魔的有限边际收益",delta);
        }
        if(T("basic"))return new(o.Key,o.Name,true,true,Math.Round(parts.Sum(x=>x.Value),3),parts.ToArray(),[p.Note]);
        var knifeCard=T("shiv_source")||T("shiv_payoff");
        var poisonCard=T("poison_seed")||T("poison_payoff")||T("poison_conditional")||T("poison_block");
        var offPlan=SilentMechanics.OffPlan(a,c);
        if(offPlan)
            Add("主要输出体系不匹配：避免另起刀／毒组件挤占主线",T("block")||T("weak")?-.9:-4);
        else if(a.PrimaryDamage=="小刀"&&knifeCard||a.PrimaryDamage=="毒"&&poisonCard)
            Add("已有主输出方向的配套投资",1.2);
        if(T("attack")&&!T("empty_draw")&&!T("finisher")&&(!T("sly")||a.Discard>=1.5))
            Add("当前即时输出缺口",1.8*Math.Clamp((4-a.Immediate)/4,0,1));
        if(T("block")||T("weak")||T("strength_down")||T("play_block"))Add("当前防守缺口",1.2*Math.Clamp((4-a.Defense)/4,0,1));
        if(T("transition"))Add("过渡伤害已足够时减少冗余",-Math.Max(0,a.Immediate-4)*.35-(act==3&&a.DamageEngines>0?1:0));
        if(T("sly"))Link("奇巧需要可靠主动弃牌，不依赖回合结束丢手牌",a.Discard<1?-3.5:a.Discard<2?-1:Math.Min(4,a.Discard));
        if(T("discard"))Link("弃牌兑现奇巧与弃牌伤害",Math.Min(3,a.Sly*.8+(Has("MementoMori")?.7:0)+(Has("MasterPlanner")?1:0)));
        if(T("shiv_payoff"))
        {
            var available=a.Shivs+(name=="FanOfKnives"?V(c,"Shivs",4)*.5:0);
            Link("现有产刀及本牌自带小刀支持刀系收益",available<1?-4:Math.Min(4,available*.9));
        }
        if(T("shiv_source"))Link("产刀与加伤、回放、多次出牌收益",Math.Min(3.5,N("shiv_payoff")*1.1+N("play_block")*.7+N("play_damage")*.5+(Has("Envenom")?.8:0)));
        if(name=="Envenom")Link("攻击命中次数支持涂毒，受格挡阻断",a.Shivs>=1?Math.Min(3,a.Shivs*.6):N("multihit")>=2?1.5:-1.8);
        if(name=="Accelerant")Link("触媒需要可持续施毒",a.PoisonSeeds>0?Math.Min(4,a.Poison*.3):-4.5);
        if(T("poison_conditional"))Link("已有中毒才能施毒",a.PoisonSeeds>0?Math.Min(2,a.PoisonSeeds): -5);
        if(T("poison_seed"))Link("施毒支持已有触媒、蜃景与毒性爆发",Math.Min(3,N("poison_payoff")*1.2+N("poison_block")*.8+(Has("Outbreak")?.8:0)));
        if(name=="Outbreak")Link("毒性爆发自带启动并即时触发已有毒",Math.Min(3,a.Poison*.15+N("poison_payoff")));
        if(name=="CorrosiveWave")Link("腐蚀波只计算打出后的当回合抽牌",a.Draw<3?-3:Math.Min(3,(a.Draw-2)*.5));
        if(T("poison_block"))Link("蜃景需要实际毒层数",a.Poison<5?-4:Math.Min(3,a.Poison*.2));
        if(name=="SerpentForm")Link("群蛇以实际多次出牌兑现伤害",a.CheapPlays+a.Draw>=3?Math.Min(4,(a.CheapPlays+a.Draw)*.35):-.8);
        if(name=="Murder")Link("谋杀需要累计抽牌和启动费用",a.Draw<3?-3:Math.Min(4,(a.Draw-2)*.55));
        if(name=="Speedster")Link("速行者只计回合内额外抽牌",a.Draw<4?-3:Math.Min(3,(a.Draw-2)*.4));
        if(T("play_block"))Link("余像与出牌频率",Math.Min(3,(a.CheapPlays+a.Draw)*.25));
        if(T("draw"))
        {
            Link("过牌服务实际输出出口",Math.Min(2,a.DamageEngines*.8+N("discard_damage")*.4));
            Add("过牌同功能饱和",-Math.Max(0,a.Draw-11)*.22);
        }
        if(name=="Tracking")Link("虚弱攻击增伤需要虚弱覆盖",a.Weak>=2?Math.Min(3,a.Immediate*.45):a.Weak==1?.3:-3);
        if(T("attack_payoff"))Link("终结技需要同回合先出攻击",a.Shivs>=1?Math.Min(3,a.Shivs*.7): -2.5);
        if(T("multihit")||T("shiv_source")||name=="KnifeTrap")
            Add("已获得力量与持续力量来源支持小刀／多段（哑铃按已锻炼次数）",Math.Min(3,SilentMechanics.StrengthSupport(s,a.Shivs)*.6));
        if(T("discard_damage"))Link("本回合弃牌量形成输出",a.Discard>=2?Math.Min(3,a.Discard*.7):-1.5);
        if(name=="Strangle")Link("紧勒只计同回合后续出牌",Math.Min(2,a.CheapPlays*.25));
        if(T("skill_payoff"))Link("飞镖需要可留在手里的技能",Math.Min(2.5,s.Deck.Count(x=>x.Type=="Skill")*.12+a.Retain*.4));
        if(T("dex")||T("temp_dex"))
        {
            var blocks=s.Deck.Count(x=>V(x,"Block")>0);
            Link("敏捷需要实际格挡牌",Math.Clamp((blocks-3)*.35,-2,2));
            if(T("temp_dex"))Add("临时敏捷占位且要求同回合配齐",-1.2);
        }
        if(T("block"))Link("现有敏捷支持格挡",Math.Min(2,N("dex")*(T("dex_payoff")?1:.5)));
        if(T("block_payoff")&&!T("poison_block"))Link("防御放大需要实际防御来源",a.Defense<2?-2:Math.Min(2,a.Defense*.25));
        if(T("carry_block"))Link("留格挡需要溢出防御或敏捷",a.Defense<3?-.8:Math.Min(2,N("dex")*.7+N("play_block")*.7));
        if(T("empty_draw"))
        {
            // Deck control is evidence, not proof that the solver can reach zero draw pile.
            var playable=s.Deck.Count(x=>x.Type is not ("Curse" or "Status"));
            Link("华丽收场需抽空牌堆及控制时机",playable<=16&&a.Draw>=5&&(a.Retain>0||a.Discard>=3)?4:-6);
        }
        if(name=="MasterPlanner")Link("谋划专家需先打技能再主动弃置",a.Discard>=2&&a.Draw>=3?Math.Min(3,a.Discard*.7):-3);
        if(name=="HandTrick")Link("本回合授予奇巧后还需主动弃牌",a.Discard>=2?1.2:-.5);
        if(T("cost_cheat"))
        {
            var costly=s.Deck.Count(x=>x.Cost>=2&&!x.CostsX);
            Link("子弹时间需要大手牌和可兑现费用出口",SilentMechanics.Has(s,"RUNIC_PYRAMID")?4:costly>=3&&(a.Draw>=3||a.Retain>0)?3:-2.5);
            if(a.Draw>=6)Add("禁抽打断持续抽牌链",-1.5);
        }
        if(T("energy")||T("next_energy"))
        {
            var spend=s.Deck.Count(x=>x.Cost>=2||x.CostsX)+a.Draw*.2;
            Link("供能与可兑现费用出口",Math.Clamp(spend*.3-.5,-.5,1.5));
            Add("供能同功能饱和",-Math.Max(0,a.Energy-3)*.45);
        }
        if(T("copy"))
        {
            var targets=s.Deck.Count(x=>x.ClassName is "Adrenaline" or "Afterimage" or "WraithForm" or "KnifeTrap" or "Outbreak" or "Footwork");
            Link("夜魇需要有价值目标且能活过等待回合",targets>0?Math.Min(2.5,targets*.8+a.Energy*.3):-2.5);
        }
        if(T("repeat_skill"))Link("爆发需要实际值得重放的技能",Math.Min(3,s.Deck.Count(x=>x.Type=="Skill"&&!Tag(x,"basic")&&(Tag(x,"poison")||Tag(x,"shiv_source")||Tag(x,"draw")||V(x,"Block")>=10))*.65));
        if(T("next_attack"))Link("下回合翻倍需要攻击主线而非毒伤",a.Immediate>=5&&a.Shivs>=1?1.5:-2);
        if(T("empty_hand")&&a.Retain>0)Add("大手牌保留降低精确切击伤害",-1.5);
        if(name=="WellLaidPlans")Link("保留帮助配齐有条件组件",Math.Min(2,N("sly")*.4+N("finisher")*.5+N("copy")*.5));
        if(T("opening"))Add("固有牌竞争起手位",-Math.Max(0,s.Deck.Count(x=>x.Keywords.Contains("Innate"))-1)*.8);
        if(c.Cost>=2&&!T("sly")&&!T("skill_discount")&&!c.CostsX)
            Add("高费启动的即时负担",-Math.Max(0,c.Cost-1)*Math.Max(.15,.6-a.Energy*.1-(s.Energy-3)*.15));
        var after=Analyze(s with{Deck=[..s.Deck,c]});
        if(n==0&&!offPlan&&after.DamageEngines>a.DamageEngines)
            Add("补成已有支撑的长战输出链",act==1?2:3);
        else if(n==0&&!offPlan&&a.DamageEngines==0&&(T("finisher")||name is "NoxiousFumes" or "Accuracy")
            &&(a.Shivs>=1&&T("shiv_payoff")||a.PoisonSeeds>0&&T("poison")||a.Draw>=3&&T("draw_damage")))
            Add("尚缺长战输出，投资已有入口的终端",1.5);
        if(n>0&&a.DamageEngines==0&&(T("block")||T("draw")||T("energy"))&&!T("attack")&&!T("finisher"))
            Add("已有辅助但仍缺输出，不继续用重复辅助挤占机会",-1.2);
        if(act>=2&&a.DamageEngines==0&&a.Defense>=4
            &&(T("block")||T("dex")||T("play_block"))&&!T("attack")&&!T("finisher"))
            Add("防守已有基础但长战仍无输出，降低继续纯补防优先级",-2);
        // A useful hybrid card does not authorize building a second damage package.
        Add("大牌组新增普通过渡的回转负担",T("transition")?-Math.Max(0,s.Deck.Length-18)*.12:0);
        if(T("transition")&&act>=2&&a.Immediate>=4&&!SilentMechanics.CanCleanJunk(s))
            Add("没有主动清理其他牌的手段，已有输出时少添冗余过渡",-.6);
        parts.AddRange(SilentRelics.CardAdjustments(s,c));
        var forecast=SilentForecast.Evaluate(s,c);
        if(forecast.Value>0)Add("辉眼："+forecast.Evidence+"；条件构筑投资，不计入当前战力",forecast.Value);
        if(act==3&&offPlan&&a.DamageEngines>0&&!T("attack")&&!T("block")&&!T("weak"))
            Add("第三幕已有输出链，不为未来配套另起输出体系",-Math.Max(0,parts.Sum(x=>x.Value)+.25));
        return new(o.Key,o.Name,true,true,Math.Round(parts.Sum(x=>x.Value),3),parts.ToArray(),[p.Note]);
    }
}
