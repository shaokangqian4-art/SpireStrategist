using System.Text.Json;
namespace SpireStrategist.Core;

// The two remaining native characters share evaluation plumbing, not an archetype.
// Every acquisition prior is explicit; support cards never vote for a damage plan.
public static class HeroPolicy
{
    public static bool Supports(string hero)=>hero is "Regent" or "Necrobinder";
    public static string Label(string hero)=>hero switch{"Regent"=>"储君","Necrobinder"=>"亡灵契约师","Silent"=>"猎人","Defect"=>"机器人",_=>"铁甲战士"};
    public static string Version(string hero)=>hero=="Regent"?"regent-draft-0.6":hero=="Necrobinder"?"necrobinder-draft-0.3":hero.ToLowerInvariant()+"-draft-0.1";
    public static string VersionFor(string hero)=>hero=="Silent"?SilentPolicy.Version:hero=="Defect"?DefectPolicy.Version:Supports(hero)?Version(hero):Policy.Version;
    public static string Scope(string hero)=>Label(hero)+"接管：选牌、营火、路线、商店与原生事件；战斗交给 CombatSolver。首版机制权重，尚待实战校准。";
    public sealed record Profile(double Base,double Duplicate,double UpgradeDelta,double[] Phase,string[] Tags,string Note,double EvidenceAdjustment);
    private sealed record Prior(Dictionary<string,Profile> Cards);
    private sealed record Definition(string MultiplayerConstraint,CardSnapshot Basic,CardSnapshot Upgraded);
    private static T Read<T>(string name)
    {
        using var stream=typeof(HeroPolicy).Assembly.GetManifestResourceStream("SpireStrategist."+name+".json")??throw new InvalidDataException(name);
        return JsonSerializer.Deserialize<T>(stream,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;
    }
    private static readonly Dictionary<string,Profile> profiles=Read<Prior>("RegentPrior").Cards.Concat(Read<Prior>("NecrobinderPrior").Cards).ToDictionary(x=>x.Key,x=>x.Value);
    private static readonly Dictionary<string,Definition> definitions=Read<Definition[]>("RegentCards").Concat(Read<Definition[]>("NecrobinderCards")).ToDictionary(x=>x.Basic.ClassName);
    public static IReadOnlyDictionary<string,Profile> Profiles=>profiles;
    public static CardSnapshot? Card(string name,bool upgraded=false)=>definitions.TryGetValue(name,out var d)?upgraded?d.Upgraded:d.Basic:null;
    public static bool MultiplayerCard(string name)=>definitions.TryGetValue(name,out var d)&&d.MultiplayerConstraint=="MultiplayerOnly";
    public static bool Tag(CardSnapshot c,string tag)=>!c.MultiplayerOnly&&profiles.TryGetValue(c.ClassName,out var p)&&p.Tags.Contains(tag);
    public static double V(CardSnapshot c,string key,double fallback=0)=>c.Variables.TryGetValue(key,out var value)?(double)value:fallback;
    public static int Stars(CardSnapshot c)=>c.StarCost>=0?c.StarCost:Card(c.ClassName,c.UpgradeLevel>0)?.StarCost??-1;
    public static double OpeningStars(RunSnapshot s)=>s.Relics.Any(r=>r.Split('.').Last()=="DIVINE_DESTINY")?7:s.Relics.Any(r=>r.Split('.').Last()=="DIVINE_RIGHT")?3:0;
    public sealed record Composition(double Immediate,double Defense,double Draw,double Energy,double Stars,double StarDemand,
        double Forge,int Generation,double Doom,double Summon,int OstyAttacks,int Souls,int Ethereal,
        int DamageEngines,string Primary,string[] Plans,string[] Missing)
    {
        public int SwordEngines {get;init;}
        public int StarEngines {get;init;}
    }
    public static Composition Analyze(RunSnapshot s)
    {
        var d=s.Deck.Where(c=>!MultiplayerAdvice.Ignore(c)).ToArray();int N(string tag)=>d.Count(c=>Tag(c,tag));
        bool H(string name)=>d.Any(c=>c.ClassName==name);bool R(string id)=>s.Relics.Any(r=>r.Split('.').Last()==id);
        var density=Math.Clamp(15d/Math.Max(10,d.Length),.5,1.25);
        var stars=d.Sum(c=>Tag(c,"star_source")?c.ClassName switch{
            "Genesis"=>V(c,"StarsPerTurn",2)*2,"TheSealedThrone"=>6,"KnockoutBlow"=>.5,
            "HiddenCache"=>V(c,"Stars",1)+V(c,"StarNextTurnPower",3)*.6,"Convergence"=>V(c,"Stars",1)*.6,
            _=>Math.Max(0,V(c,"Stars")-Math.Max(0,Stars(c)))}:0)*density;
        if(R("LUNAR_PASTRY"))stars+=1;
        var demand=d.Sum(c=>Math.Max(0,Stars(c))+(c.CostsStarX?3:0))*density;
        var forge=d.Sum(c=>Tag(c,"forge")?c.ClassName=="Furnace"?V(c,"Forge",5)*2:
            c.ClassName=="BeatIntoShape"?V(c,"CalculationBase",5)*1.5:V(c,"Forge"):0)*density;
        if(R("FENCING_MANUAL"))forge+=5; // Opening forge, not renewable growth.
        var souls=N("soul_source");var osty=N("osty_attack");var ethereal=d.Count(c=>c.Keywords.Contains("Ethereal"))+N("ethereal_source");
        var playableEthereal=NecrobinderMechanics.PlayableEthereal(s);
        var summon=d.Sum(c=>Tag(c,"summon")?c.ClassName=="DevourLife"?souls*1.5:c.ClassName=="SicEm"?Math.Min(3,osty):V(c,"Summon"):0)*density;
        summon+=R("PHYLACTERY_UNBOUND")?6:R("BOUND_PHYLACTERY")?2:0; // Discounted native opening/turn supply, never player healing.
        var doom=d.Sum(c=>Tag(c,"doom_source")?c.ClassName switch{
            "Countdown"=>V(c,"CountdownPower",6)*2,"BlightStrike"=>V(c,"Damage")*.6,
            "NoEscape"=>V(c,"CalculationBase",10),"Oblivion"=>V(c,"DoomPower",3)*3,_=>V(c,"DoomPower")}:0)*density;
        var draw=d.Sum(c=>Tag(c,"draw")?c.ClassName switch{
            "Glimmer" or "PhotonCut"=>Math.Max(0,V(c,"Cards")-1),"Fetch"=>.7,
            "ForegoneConclusion"=>V(c,"Cards")*.7,"PaleBlueDot"=>N("draw")>=2?1:.3,
            "Pagestorm"=>Math.Min(3,ethereal*.4),_=>V(c,"Cards",1)}:0)*density+souls*1.2;
        var energy=d.Sum(c=>Tag(c,"energy")?c.ClassName switch{
            "Friendship" or "Demesne"=>1.5,"Orbit"=>0,"BorrowedTime"=>d.Any(x=>x.CostsX)?Math.Max(0,V(c,"Energy",4)-c.Cost)*.6:.5,
            _=>V(c,"Energy",1)}:0)*density+N("next_energy")*.5;
        if(s.Character=="Regent")energy+=RegentOrbit.Refund(s,draw,d.Count(c=>c.ClassName=="Orbit"))*.65;
        double Reliability(CardSnapshot c)
        {
            var star=Stars(c);var r=star>0?Math.Clamp((stars+OpeningStars(s)*.5)/Math.Max(3,demand),.2,1):1;
            if(c.Cost>=3&&!c.CostsX)r*=Math.Clamp((3+energy)/(c.Cost+1),.25,1);
            if(c.ClassName=="BansheesCry")r*=Math.Clamp(playableEthereal/4d,.05,1);
            if(c.ClassName=="TimesUp")r*=Math.Clamp(doom/20,0,1);
            return r;
        }
        var immediate=d.Sum(c=>{
            var damage=V(c,"Damage");if(Tag(c,"osty_attack"))damage=V(c,"OstyDamage",c.ClassName=="Unleash"?6+Math.Min(8,summon*.3):c.ClassName=="Squeeze"?25+5*osty:0)*.75;
            if(c.ClassName=="AstralPulse")damage*=2;
            if(c.ClassName is "CelestialMight" or "SevenStars")damage*=V(c,"Repeat",1);
            if(c.ClassName is "LunarBlast" or "Radiate" or "PullFromBelow")damage*=.35;
            if(c.ClassName=="Supermassive")damage=5+N("generation")*3;
            if(c.ClassName=="CrescentSpear")damage=8+d.Count(x=>Stars(x)>0)*2;
            if(c.ClassName=="DeathMarch")damage=8+Math.Min(3,draw)*2;
            if(c.ClassName=="TimesUp")damage=doom*.6;
            if(c.ClassName=="HeavenlyDrill")damage=V(c,"Damage",8)*(energy>=1?6:2);
            if(c.ClassName=="Eradicate")damage=V(c,"Damage",11)*2;
            if(c.ClassName=="Stardust")damage=V(c,"Damage",5)*Math.Min(3,stars);
            if(s.Character=="Regent"&&c.Type=="Attack"&&Stars(c)<=0&&!c.CostsStarX)
            {
                if(c.ClassName is "Volley" or "LunarBlast")damage=V(c,"Damage")*OwnedAttackPlan.Hits(s,c);
                damage+=OwnedAttackPlan.Strength(s)*OwnedAttackPlan.Hits(s,c);
            }
            return damage/10*Reliability(c)*(Tag(c,"basic")?.25:1);
        })*density+Math.Min(3,doom/20)+Math.Min(3,forge/12);
        var defense=d.Sum(c=>V(c,"Block")/8*Reliability(c)*(Tag(c,"basic")?.25:1))*density+N("weak")*.35+N("strength_down")*.6;
        defense+=Math.Min(2.5,summon/12); // Partial damage absorption; not healing or a second block pool.
        if(H("Shroud"))defense+=Math.Min(2,N("doom_source")*.4);
        if(H("SpiritOfAsh"))defense+=Math.Min(2,playableEthereal*.3);
        if(H("ChildOfTheStars"))defense+=Math.Min(2,Math.Min(stars,demand)*.18);
        if(H("Parry")&&forge>0)defense+=1;
        var plans=new List<string>();var engines=0;var primary="未确定";var swordEngines=0;var starEngines=0;
        if(s.Character=="Regent")
        {
            var direction=RegentDirection.Analyze(s);
            var sword=direction.SwordInvestment;var star=direction.StarInvestment;
            var gen=N("generation_engine")*2+(H("Arsenal")?2:0)+N("generation")*.5;
            primary=sword>=2&&sword>=Math.Max(star,gen)?"铸剑":star>=2&&star>=gen?"星星":gen>=2?"生成":"未确定";
            if(direction.Committed==RegentDirection.Plan.Stars)primary="星星";
            if(direction.Committed==RegentDirection.Plan.Sword)primary="铸剑";
            if(direction.Committed==RegentDirection.Plan.Generation)primary="生成";
            if(direction.Committed==RegentDirection.Plan.StrengthAttacks)primary="力量多段";
            if(OwnedAttackPlan.StrengthCore(s)){engines++;plans.Add("力量多段：已有力量来源与可支付的重复输出；辅助缺口不等于没有主线");}
            if(primary=="铸剑"&&!d.Any(RegentDirection.SwordCore))primary="未确定";
            if(d.Any(RegentDirection.SwordCore)&&forge>=10&&(N("sword_engine")>0||N("sword_payoff")>0)){engines++;swordEngines=1;plans.Add("铸剑输出：已有铸造和增长／放大");}
            if(stars>=3&&(H("BlackHole")||N("star_finisher")>0||H("Radiate"))){engines++;starEngines=1;plans.Add("星星输出：已有供星与消费出口");}
            if(N("generation")>=2&&(H("Supermassive")||H("Arsenal"))){engines++;plans.Add("生成输出：已有生成与伤害成长");}
            if(H("KinglyPunch")&&draw>=3&&s.Deck.Length<=16||H("MakeItSo")&&draw>=4){engines++;plans.Add("持续攻击：已有长战出口");}
        }
        else
        {
            primary=NecrobinderDirection.Analyze(s) switch{NecrobinderDirection.Plan.Doom=>"灾厄",NecrobinderDirection.Plan.Souls=>"灵魂",NecrobinderDirection.Plan.Ethereal=>"虚无",NecrobinderDirection.Plan.Osty=>"奥斯提",_=>"未确定"};
            if(NecrobinderDirection.PetReady(s)){engines++;plans.Add("奥斯提：已有攻击／承伤与成长");}
            if(NecrobinderDirection.ReflectionReady(s)){engines++;plans.Add("亡灵精通：召唤承伤转伤害，不依赖奥斯提攻击次数");}
            if(doom>=15&&(H("NoEscape")||H("Countdown")||H("Oblivion")&&draw>=3||H("ReaperForm")&&immediate>=4)){engines++;plans.Add("灾厄：已有施加与长战增长");}
            if(souls>=1&&(H("Haunt")||H("SoulStorm"))){engines++;plans.Add("灵魂：已有灵魂生成和伤害出口");}
            if(playableEthereal>=3&&(H("BansheesCry")||H("PullFromBelow")||H("Eidolon")&&NecrobinderMechanics.HasReplayDamage(s))){engines++;plans.Add("虚无：已有虚无供给及输出／回放");}
            if(H("Hang")||H("TheScythe")||H("SentryMode")){engines++;plans.Add("独立输出：已有可成长攻击或持续生成");}
        }
        var missing=new List<string>();if(engines==0)missing.Add("缺少长战输出，优先寻找可启动的核心");
        if(immediate<4)missing.Add("即时伤害不足，避免早期连续精英");if(defense<3)missing.Add("防守薄弱");
        if(s.Character=="Regent"&&demand>stars+3)missing.Add("星消耗高于可持续供星");
        return new(immediate,defense,draw,energy,stars,demand,forge,N("generation"),doom,summon,osty,souls,ethereal,engines,primary,plans.ToArray(),missing.ToArray())
            {SwordEngines=swordEngines,StarEngines=starEngines};
    }
    public static Evaluation Evaluate(RunSnapshot s,IReadOnlyList<DecisionOption> options)
    {
        var a=Analyze(s);List<string> warnings=[Scope(s.Character),..a.Plans,..a.Missing];if(s.IsMultiplayer)warnings.Add(MultiplayerAdvice.Notice);
        var direction=RegentDirection.Analyze(s);
        if(direction.Committed==RegentDirection.Plan.Stars)warnings.Add("星光输出已投入：补供星与运转，不为辉眼改走铸剑。");
        if(direction.Committed==RegentDirection.Plan.Sword)warnings.Add("铸剑主线已投入：补取剑与放大，星光辅助按实际用途保留。");
        if(direction.Committed==RegentDirection.Plan.StrengthAttacks)warnings.Add("力量多段主线已投入：辉眼补运转、防守与放大，不另起铸剑体系。");
        OptionScore Unknown(DecisionOption o,string reason)=>new(o.Key,o.Name,false,o.Enabled,null,[],[reason]);
        var ignored=options.Where(MultiplayerAdvice.Ignore).Select(o=>o.Key).ToHashSet();
        var scores=options.Select(o=>!Supports(s.Character)||s.MaxHp<=0||s.PlayerCount<1?Unknown(o,"角色或局面无效。"):
            !o.Enabled?new(o.Key,o.Name,true,false,null,[],[]):ignored.Contains(o.Key)?Unknown(o,MultiplayerAdvice.Ignored):
            o.Kind is "skip" or "reroll"?new(o.Key,o.Name,true,true,0,[],[]):
            o.Kind=="card"&&o.Card!=null?Score(s,a,direction,o,o.Card):DefectStrategy.NonCard(s,o)??Unknown(o,"此操作尚未建模。")).ToArray();
        var active=scores.Where(x=>x.Enabled&&!ignored.Contains(x.Key)).ToArray();var skips=options.Where(o=>o.Kind=="skip").Select(o=>o.Key).ToHashSet();
        var best=active.Length>0&&active.All(x=>x.Supported&&x.Score.HasValue)?active.OrderByDescending(x=>x.Score).ThenByDescending(x=>skips.Contains(x.Key)).ThenBy(x=>x.Key).First().Key:null;
        return new(Version(s.Character),best,scores,warnings.ToArray());
    }
    private static OptionScore Score(RunSnapshot s,Composition a,RegentDirection.Direction direction,DecisionOption o,CardSnapshot c)
    {
        if(!profiles.TryGetValue(c.ClassName,out var p))return DefectNeutral.Score(s,o,c)??new(o.Key,o.Name,false,o.Enabled,null,[],["此牌尚未建立当前角色的模型。"]);
        if(CardCatalog.HasUnmodelledModification(c))return new(o.Key,o.Name,false,o.Enabled,null,[],["未知附魔／异变，保留手动选择。"]);
        var n=s.Deck.Count(d=>d.ClassName==c.ClassName);var act=Math.Clamp(s.Map?.ContentAct is >0?s.Map.ContentAct:s.Act,1,3);
        var parts=new List<ScorePart>();double positive=0;void Add(string why,double v){if(Math.Abs(v)>.00001)parts.Add(new(why,Math.Round(v,3)));}
        void Link(string why,double v){if(v>0){if(s.Character=="Regent"&&n>0)v/=1+n;v=Math.Min(v,8-positive);positive+=v;}Add(why,v);}
        bool T(string t)=>p.Tags.Contains(t);bool H(string name)=>s.Deck.Any(d=>d.ClassName==name);int N(string tag)=>s.Deck.Count(d=>Tag(d,tag));
        Add(s.Character=="Regent"?"基础强度先验（跨稀有度榜单分档，非胜率）":"基础机制先验（未实战校准）",p.Base);Add("同版本单人A10同稀有度统计修正（有限幅度）",p.EvidenceAdjustment);
        Add("前／中／后期需求",p.Phase[act-1]);Add("占用一次抽牌的基准成本",-2.5);
        var duplicate=p.Duplicate*n+.4*n*n;
        if(c.ClassName=="Orbit"&&RegentOrbit.SupportedSecond(s,a.Draw))duplicate*=.35;
        Add("重复份数递减",-duplicate);
        if(c.UpgradeLevel>0)Add("已升级的实际收益",p.UpgradeDelta);
        if(T("basic"))return Finish();
        if(T("transition"))Add("已有即时输出后少添过渡牌",-Math.Max(0,(T("block")&&!T("attack")&&!T("osty_attack")?a.Defense-3:a.Immediate-4))*.4-Math.Max(0,s.Deck.Length-18)*.12);
        if(T("attack")||T("osty_attack"))Add("即时输出缺口",1.5*Math.Clamp((4-a.Immediate)/4,0,1));
        if(T("block")||T("weak"))Add("防守缺口",1.1*Math.Clamp((3-a.Defense)/3,0,1));
        if(T("draw"))Add("过牌重复功能饱和",-Math.Max(0,a.Draw-10)*.3);
        if(s.Character=="Regent")
        {
            if(c.ClassName=="Orbit")Link("环绕轨道跨回合累计能量花费：按可支付手牌估计回流，不把星费算能量",Math.Min(5,RegentOrbit.Marginal(s,a.Draw)*2.5));
            if(T("transition")&&T("attack")&&act>=2&&s.Deck.Count(d=>!HeroStrategy.Basic(d))>=6&&
                !(a.Immediate<2&&s.Deck.Count(d=>d.Type=="Attack"&&!HeroStrategy.Basic(d))<2)&&
                !((T("block")||T("weak")||T("strength_down"))&&a.Defense<3))
                Add("中后期低收益过渡的替换门槛：不能靠略高于零继续扩张",act==3?-1.5:-.5);
            var cost=Stars(c);
            if(cost>0||c.CostsStarX)Link("星费与实际供星：零能量不等于免费",Math.Clamp((a.Stars+OpeningStars(s)*.5-a.StarDemand-cost*.6)*.3,-4,1.5));
            if(T("star_source"))Link("供星服务已有消费与黑洞",Math.Clamp((a.StarDemand-a.Stars)*.3+(H("BlackHole")?1.5:0),-1.8,3));
            if(T("star_engine")||T("star_finisher"))Link("供星支撑长战星系出口",Math.Min(3,a.Stars*.45));
            if(direction.Committed!=RegentDirection.Plan.Stars)
            {
                if(T("forge"))Link("铸造支持已有剑与取剑组件",Math.Min(3,N("sword_payoff")+N("sword_access")+N("sword_engine")*.5));
                if(T("sword_payoff"))Link("剑放大需要真实铸造入口",a.Forge>0?Math.Min(3,a.Forge/8):-4);
                if(T("sword_access"))Link("稳定取剑提高铸造兑现率",Math.Min(2,a.Forge/12));
                if(a.Forge>=6&&direction.SwordInvestment>=2&&n==0)
                {
                    if(c.ClassName=="SummonForth")Link("铸剑核心：首张征召上前补稳定取剑",N("sword_access")==0?2.5:.5);
                    if(c.ClassName=="SwordSage")Link("铸剑核心：重放放大已有铸造",N("sword_access")>0?2.5:1.5);
                    if(c.ClassName=="SeekingEdge")Link("铸剑核心：首张群攻扩展剑的覆盖",2);
                    if(c.ClassName=="Conqueror")Link("铸剑核心：双倍伤害需要同回合取剑",N("sword_access")>0?2:.5);
                }
            }
            if(T("generation_payoff")||T("generation_engine"))Link("生成联动不把抽牌算生成",a.Generation>0?Math.Min(3,a.Generation): -2.5);
            if(T("generation"))Link("生成服务已有力量、格挡和累积攻击",Math.Min(3,N("generation_payoff")+N("generation_engine")));
            if(T("skill_payoff")||T("skill_engine"))Link("技能链需要运转与低费技能",a.Draw>=3?Math.Min(3,a.Draw*.3):-2);
            if(T("star_block"))Link("星费防守与伤害共享预算",Math.Min(2,Math.Min(a.Stars,a.StarDemand)*.2));
            if(c.ClassName=="KinglyPunch")Add("王者之拳依赖小牌组重复抽到；厚牌组与重复补牌折扣",-Math.Max(0,s.Deck.Length-14)*.28-(act-1)*.8-n*1.5);
            if(c.ClassName=="Radiate")Link("辐射需当回合供星",a.Stars>=4?2:-3);
            if(c.ClassName=="IAmInvincible")Link("顶抽防御需要控制顶牌",N("topdeck")>0?1.5:-1);
            if(c.ClassName=="Royalties")Add("末段经济收益按可达商店折扣",s.Commerce is {ReachableShops:0,FutureActs:0}?-8:0);
        }
        else
        {
            if(c.ClassName=="BorrowedTime"||c.CostsX)Link("预借时间与X费出口：按现有配套估值，保留涨费代价",NecrobinderDirection.BorrowedX(s,c));
            if(NecrobinderDirection.Output(c)==NecrobinderDirection.Plan.Osty&&!NecrobinderDirection.PetReady(s))
                Add("奥斯提过渡不代表输出主线，孤立专属组件折扣",-2);
            if(c.ClassName=="NecroMastery")Link("亡灵精通依赖召唤承伤，不因普通奥斯提攻击加分",Math.Min(3,a.Summon*.2));
            else if(T("osty_engine"))Link("奥斯提输出链的实际攻击与召唤",Math.Min(3,a.OstyAttacks*.65+a.Summon*.08));
            if(T("osty_attack"))Link("奥斯提攻击兑现钙化与多次攻击",Math.Min(3,s.Deck.Count(d=>d.ClassName is "Calcify" or "Rattle" or "SicEm")*.9));
            if(T("summon_payoff"))Link("大奥斯提收益依赖真实召唤",a.Summon>=8?Math.Min(3,a.Summon*.12):-2);
            if(T("soul_payoff"))Link("灵魂终端需要灵魂来源",a.Souls>0?Math.Min(3,a.Souls*1.2):-3.2);
            if(T("soul_source"))Link("灵魂接通已有伤害与召唤终端",Math.Min(3,N("soul_payoff")*1.4));
            if(T("doom_payoff")||T("doom_block"))Link("灾厄配套按实际施加能力",a.Doom>=10?Math.Min(3,a.Doom*.08):-2.5);
            if(T("doom_source"))Link("灾厄支持已有放大、格挡和即时斩杀",Math.Min(3,N("doom_payoff")+N("doom_block")*.6));
            if(T("ethereal_payoff"))
            {
                var supply=NecrobinderMechanics.PayoffSupply(s,c);
                Link(c.ClassName=="Pagestorm"?"虚无抽到触发：诅咒可触发书页风暴":"虚无打出联动：排除不能打出的诅咒与状态",supply>=2?Math.Min(3,supply*.5):-3);
            }
            if(T("ethereal")||T("ethereal_source"))Link("虚无供应连接已有输出／抽牌",Math.Min(3,N("ethereal_payoff")*.8));
            if(T("expensive_payoff"))Link("高费牌打出频率",Math.Min(2,s.Deck.Count(d=>d.Cost>=2&&!d.CostsX)*.5)-1);
            if(T("sacrifice"))Add("献祭会丢失已养大的奥斯提",a.Primary=="奥斯提"&&a.Summon>=12?-2:0);
            if(T("strength_loss"))Add("玩家力量损失只惩罚玩家攻击主线",s.Deck.Count(d=>d.Type=="Attack"&&!Tag(d,"osty_attack")&&!Tag(d,"basic"))>=5?-2:0);
            if(T("surcharge"))Add("涨费妨碍零费灵魂与连打",-Math.Min(3,a.Souls*.8+a.Draw*.1));
            if(T("self_doom"))Add("自身灾厄在低血和缺输出长战中的风险",s.Hp<20?-4:a.DamageEngines==0?-1.5:0);
            if(c.ClassName=="Hang")Link("回收过牌帮助同目标翻倍",Math.Min(3,a.Draw*.25+N("recall")));
        }
        if(T("cleanup"))Link("主动处理基础牌与污染，不把自消耗当清理",Math.Min(2,s.Deck.Count(d=>HeroStrategy.Basic(d)||d.Type is "Status" or "Curse")*.2));
        if(T("self_status"))Add("自产状态的回转负担",N("cleanup")>0?-.5:-2);
        if(T("energy"))Add("供能重复功能饱和",-Math.Max(0,a.Energy-4)*.35);
        if(c.Cost>=2&&!c.CostsX)Add("高费启动负担",-Math.Max(0,c.Cost-1)*Math.Max(.15,.5-a.Energy*.08));
        var after=Analyze(s with{Deck=[..s.Deck,c]});
        if(n==0&&RegentDirection.ConnectsOwned(s,c,direction)&&RegentDirection.RelevantEngines(after,direction)>RegentDirection.RelevantEngines(a,direction))Add("接通现有组件的长战输出链",a.DamageEngines==0?4:1.5);
        if(n==0&&a.DamageEngines==0&&!RegentDirection.OffPlan(direction,c)&&(T("sword_engine")||T("star_engine")||T("osty_engine")||T("doom_engine")||T("soul_engine")||T("autonomous")))
            Add("尚缺核心输出，允许投资可发展的入口",1.4);
        if(a.DamageEngines==0&&a.Defense>=4&&T("block")&&!T("attack")&&!T("osty_attack"))Add("已有防守但缺长战输出",-1.8);
        if(n>0&&a.DamageEngines==0&&(T("draw")||T("energy")||T("block")))Add("缺输出时不堆重复辅助",-1.2);
        if(c.Enchantment?.Split('.').Last()=="SWIFT")Add("迅捷首次打出抽牌",Math.Min(4,c.EnchantmentAmount*1.2));
        if(c.Enchantment=="ENCHANTMENT.GLAM")Add("华彩：每场首次打出重放一次",NativeEnchantments.Glam(c));
        parts.AddRange(HeroRelics.CardAdjustments(s,c));
        parts.AddRange(RegentAttackLinks.Adjust(s,c));
        parts.AddRange(RegentEconomy.Adjust(s,a,c));
        var future=HeroForecast.Evaluate(s,c);if(future.Value>0)Add("辉眼："+future.Evidence+"；条件构筑投资，不计当前战力",future.Value);
        if(s.Character=="Regent"&&RegentDirection.SwordOnly(c)&&!RegentDirection.SwordCore(c)&&!RegentDirection.SwordOpportunity(s))
            Add("没有真实或可靠预见的铸剑核心，不因过渡铸造转流",-Math.Max(0,parts.Sum(x=>x.Value)+.25));
        if(NecrobinderDirection.OffPlan(s,c))Add("已有输出主线，缺少真实跨体系联动时不另起输出体系",-Math.Max(0,parts.Sum(x=>x.Value)+.25));
        if(RegentDirection.OffPlan(direction,c))Add("已有明确输出主线，不另起依赖专属组件的输出体系",-Math.Max(0,parts.Sum(x=>x.Value)+.25));
        return Finish();
        OptionScore Finish()=>new(o.Key,o.Name,true,o.Enabled,Math.Round(parts.Sum(x=>x.Value),3),parts.ToArray(),[p.Note]);
    }
}
