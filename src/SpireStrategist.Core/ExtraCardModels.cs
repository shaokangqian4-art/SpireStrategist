namespace SpireStrategist.Core;

// Audited against installed 0.111.0 Card/Power implementations. These are explicit,
// conditional strategic priors, not a trained simulator or guarantees of triggering.
public static class ExtraCardModels
{
    public static readonly HashSet<string> MultiplayerOnly=new(StringComparer.Ordinal)
    {"Blaze","DemonicShield","Midnight","Outrage","Tank","BeaconOfHope","BelieveInYou","Coordinate",
     "GangUp","HuddleUp","Intercept","Knockdown","Lift","Mimic","Rally","TagTeam","TheBall"};
    private static CardSnapshot Definition(CardSnapshot c)=>c.Variables.Count>0?c:
        CardRegistry.All.TryGetValue(c.ClassName,out var d)?c.UpgradeLevel>0?d.Upgraded:d.Basic:c;
    public static CardProfile? Find(CardSnapshot card)
    {
        if(MultiplayerOnly.Contains(card.ClassName))return new(-30,Upgrade:0,Note:"原生限定多人卡；单人战士不按队友收益推荐。");
        var c=Definition(card);
        double V(string k)=>CardRegistry.Var(c,k);
        var a=Math.Max(V("Damage"),V("CalculatedDamage"))/10;
        var b=Math.Max(V("Block"),V("CalculatedBlock"))/10;
        var x=c.Keywords.Contains("Exhaust")?.2:0;
        var u=card.UpgradeLevel>0;
        return c.ClassName switch
        {
            "Exterminate"=>new(3,Attack:a*V("Repeat")*1.4,StrengthPayoff:1.5,Upgrade:9,
                Note:"杀灭：四段群攻，锐利逐段增加伤害；不把产刀牌当作本体多段。"),
            "Whistle"=>new(4,Attack:a,Block:1,Upgrade:12,
                Note:"吹哨：33/44伤害并眩晕，消耗自身；不是主动消耗其他手牌的工具。"),
            "GiantRock"=>new(2,Attack:u?2.4:2,Upgrade:7,Note:"原始力量生成的一费20伤（升级24伤）攻击；无永久成长。"),
            "Aggression"=>new(2,Draw:.65,Scaling:.35,Upgrade:4,Note:"每回合从弃牌堆随机回收并升级攻击；首回合和无攻击弃牌时空转。"),
            "AshenStrike"=>new(1,Attack:a,ExhaustPayoff:.8,Upgrade:10,Note:"6+消耗堆张数×3（升级×4）；另计实际消耗来源，非固定6伤。"),
            "BloodWall"=>new(1,Block:b,SelfDamage:V("HpLoss")/6,Upgrade:7,Note:"两费自伤格挡；低血时仍计自伤风险。"),
            "Break"=>new(2,Attack:a,Upgrade:10,Note:"高伤加易伤；本卡为特殊获得牌，按实际费用评价。"),
            "Breakthrough"=>new(2,Attack:a*1.5,SelfDamage:V("HpLoss")/6,Upgrade:8,Note:"群攻自伤；群体收益采用保守折扣。"),
            "Bully"=>new(1,Attack:.4,Upgrade:7,Note:"零费，按目标易伤层数成长；易伤来源另计。"),
            "Cinder"=>new(2,Attack:a,ExhaustSource:.7,Upgrade:8,Note:"攻击后随机消耗一张手牌；保留误烧关键牌风险。"),
            "Colossus"=>new(2,Block:b,Upgrade:7,Note:"本回合来自易伤敌人的攻击减半；不是无条件全伤害减半。"),
            "Conflagration"=>new(2,Attack:a*V("Repeat")*1.4,StrengthPayoff:1.5,Upgrade:12,Note:"群体多段伤害；多段受力量支撑。"),
            "CrimsonMantle"=>new(2,Block:V("CrimsonMantlePower")/10,Scaling:.7,SelfDamage:1d/6,Upgrade:10,Note:"每回合自伤并获得格挡；长期收益与生命成本并存。"),
            "Cruelty"=>new(0,Scaling:.3,Upgrade:12,Note:"提高易伤倍率；必须有可兑现的易伤及攻击。"),
            "Dominate"=>new(2,Strength:.5,Scaling:.25,ExhaustSource:x,Upgrade:9,Note:"先给予易伤，再按目标易伤层数获得力量；不等同零收益消耗技能。"),
            "EvilEye"=>new(2,Block:b,Upgrade:9,Note:"本回合已消耗牌时格挡两次；条件收益单列。"),
            "ExpectAFight"=>new(1,Block:b,Upgrade:10,Note:"格挡随当前力量增加；三费启动，不能预设已有高力量。"),
            "FightMe"=>new(1,Attack:a*2,Strength:V("StrengthPower")/2,Scaling:.5,StrengthPayoff:1,Upgrade:11,Note:"双段并获得力量，同时给敌人力量；危险代价另计。"),
            "Havoc"=>new(1,ExhaustSource:.8,Upgrade:14,Note:"免费打出牌库顶并强制消耗；需要排序或高费目标，随机烧关键牌有风险。"),
            "Hellraiser"=>new(0,Scaling:.4,Upgrade:14,Note:"抽到打击标签牌时自动打出；不能把所有攻击都当成打击。"),
            "HowlFromBeyond"=>new(1,Attack:a*1.4,Scaling:.4,Upgrade:9,Note:"在消耗堆中每回合自动施放；必须先有把它送入消耗堆的手段。"),
            "InfernalBlade"=>new(1,Attack:.7,ExhaustSource:x,Upgrade:12,Note:"随机攻击当回合免费；随机质量折扣，不算稳定过牌。"),
            "Inferno"=>new(1,Attack:V("InfernoPower")/10,Scaling:.65,SelfDamage:1d/6,Upgrade:10,Note:"每回合自伤，回合内失血触发群伤；计为真实自伤来源。"),
            "Juggling"=>new(0,Scaling:.3,Upgrade:4,Note:"每回合第三张攻击复制入手；仍需费用，不是立即抽牌。"),
            "Mangle"=>new(2,Attack:a,Block:1.4,Upgrade:10,Note:"攻击并暂时大幅减力量；三费防攻一体，不能当永久减力量。"),
            "MoltenFist"=>new(2,Attack:a,ExhaustSource:x,Upgrade:8,Note:"一费10伤（升级14），自身消耗；攻击后才翻倍已有易伤，不凭空施加第一层易伤，也不能消耗其他废牌。"),
            "NotYet"=>new(2,ExhaustSource:x,Upgrade:8,Note:"战中治疗，按缺血与未来战损机会估计，不能当作当前已回血。"),
            "OneTwoPunch"=>new(1,Upgrade:14,Note:"使接下来的攻击额外打出；升级影响两张，需高质量攻击和可用费用。"),
            "PerfectedStrike"=>new(0,Attack:.6,Upgrade:9,Note:"按战斗牌组的打击标签数量增伤；删打击会减弱，动态计数。"),
            "Pillage"=>new(1,Attack:a,Draw:.5,Upgrade:6,Note:"持续抽攻击直到首张非攻击；收益由攻击密度决定。"),
            "PrimalForce"=>new(0,Upgrade:9,Note:"将手中攻击变为巨石；前期提升基础攻击，成熟核心被替换也有损失。"),
            "Pyre"=>new(1,Energy:V("Energy")*.5,Scaling:.4,Upgrade:14,Note:"以后每回合获得能量；不是打出当回合回费，启动已折扣。"),
            "Rampage"=>new(0,Attack:a,Scaling:.2,Upgrade:6,Note:"重复打出才增加本卡伤害；循环速度和竞争攻击限制成长。"),
            "SetupStrike"=>new(0,Attack:a,Strength:.5,StrengthPayoff:0,Upgrade:7,Note:"力量只持续本回合；不计为持续成长，需后续攻击兑现。"),
            "Spite"=>new(1,Attack:a,Upgrade:8,Note:"本回合失血时才多段；未满足时只攻击一次。"),
            "Stomp"=>new(1,Attack:a*1.4,Upgrade:7,Note:"本回合每打出攻击减一费；按攻击密度折扣启动费用。"),
            "StoneArmor"=>new(2,Block:V("PlatingPower")/10,Scaling:.7,Upgrade:9,Note:"覆甲持续产防，但被破防会衰减；不计无限稳定防御。"),
            "Taunt"=>new(1,Block:b,Upgrade:7,Note:"格挡并给予易伤；与现有易伤功能存在重复。"),
            "TearAsunder"=>new(1,Attack:a,StrengthPayoff:.8,Upgrade:11,Note:"按本战失血次数增加攻击次数；自伤源比盲目挨打可靠。"),
            "Unrelenting"=>new(2,Attack:a,Upgrade:9,Note:"下一张攻击免费；按牌组中其他高费攻击评价费用收益。"),
            "Vicious"=>new(0,Draw:.25,Upgrade:12,Note:"施加易伤触发抽牌；触发密度另计，不是无条件每回合抽牌。"),
            "Alchemize"=>new(1,ExhaustSource:x,Upgrade:12,Note:"生成药水；受药水空位、使用节奏和添水遗物约束。"),
            "Anointed"=>new(0,Draw:.3,ExhaustSource:x,Upgrade:7,Note:"从牌库检索稀有牌；需足够金卡，不能把普通牌当抽牌目标。"),
            "Automation"=>new(0,Energy:.25,Upgrade:12,Note:"每抽十张才获得能量；随过牌频率估值。"),
            "BeatDown"=>new(1,Upgrade:11,Note:"从弃牌堆自动打出数张攻击；首轮可能无目标，高费攻击支撑收益。"),
            "Bolas"=>new(1,Attack:a,Draw:.2,Upgrade:5,Note:"打出后下回合回手；零费但低单卡质量。"),
            "Calamity"=>new(-1,Scaling:.4,Upgrade:12,Note:"攻击后生成随机攻击；三费启动和生成牌费用限制收益。"),
            "Catastrophe"=>new(0,Upgrade:10,Note:"从抽牌堆随机自动打出多张牌，优先可打出牌；不生成攻击。"),
            "Discovery"=>new(2,ExhaustSource:x,Upgrade:8,Note:"三选一生成当回合免费牌；升级可重复，随机池不等于已有牌库。"),
            "DramaticEntrance"=>new(3,Attack:a*1.5,ExhaustSource:x,Upgrade:7,Note:"固有零费群攻；占首回合手牌，后续不循环。"),
            "Entropy"=>new(-1,Scaling:.2,Upgrade:4,Note:"每回合可变化手牌；能处理废牌，但替换结果随机。"),
            "Equilibrium"=>new(2,Block:b,Upgrade:8,Note:"格挡并保留手牌一回合，帮助排序，也可能堵手。"),
            "EternalArmor"=>new(1,Block:V("PlatingPower")/10,Scaling:1,Upgrade:8,Note:"高费覆甲；回合收益会随破防衰减。"),
            "Fasten"=>new(0,Scaling:.3,Upgrade:8,Note:"提高防御标签牌格挡；不是对所有防御技能生效。"),
            "Fisticuffs"=>new(2,Attack:a,Block:a*.75,Upgrade:8,Note:"按造成伤害获得格挡；力量、易伤提高攻防，格挡需求不同步折扣。"),
            "GoldAxe"=>new(0,Attack:.3,Scaling:.3,Upgrade:5,Note:"伤害随本战打牌次数增长；早抽低收益，按运转速度估计。"),
            "HandOfGreed"=>new(3,Attack:a,Upgrade:11,Note:"致死赚金币；剩余可消费路线决定经济价值，不保证每战收割。"),
            "HiddenGem"=>new(1,Scaling:.4,Upgrade:10,Note:"随机赋予抽牌堆一张牌额外打出；需要再次抽到并打出。"),
            "Impatience"=>new(0,Draw:.25,Upgrade:7,Note:"手中无攻击才能抽牌；攻击密集时严格折扣。"),
            "JackOfAllTrades"=>new(0,ExhaustSource:x,Upgrade:8,Note:"随机生成无色牌，不是抽牌；需支付生成牌费用。"),
            "Jackpot"=>new(2,Attack:a,Upgrade:9,Note:"生成零费牌可帮助续打；三费起手负担仍存在。"),
            "Mayhem"=>new(1,Scaling:.5,Upgrade:14,Note:"每回合自动打出牌库顶；有免费高费收益及顺序/误打风险。"),
            "MindBlast"=>new(1,Attack:.5,Upgrade:12,Note:"固有，伤害取决于剩余抽牌堆；按牌组大小估计首轮收益。"),
            "Nostalgia"=>new(0,Scaling:.3,Upgrade:12,Note:"首批攻击/技能回到牌库顶；回收核心与堵住下一抽并存。"),
            "Omnislice"=>new(2,Attack:a*1.3,Upgrade:8,Note:"零费伤害向其他敌人扩散；群体收益按保守系数。"),
            "Panache"=>new(0,Scaling:.3,Upgrade:8,Note:"一回合打五张才群伤；依赖低费和运转。"),
            "PrepTime"=>new(1,Attack:.3,Scaling:.4,Upgrade:8,Note:"每回合给予活力；优先配合高质量、多段攻击。"),
            "Production"=>new(3,Energy:V("Energy")*.75,ExhaustSource:x,Upgrade:12,Note:"零费产费，按过牌及费用需求评价。"),
            "Prolong"=>new(0,Upgrade:7,Note:"把当前格挡用于下回合；必须先有可观格挡，非即时防御。"),
            "Prowess"=>new(2,Strength:V("StrengthPower")/2,Block:V("DexterityPower")*.3,Scaling:.7,Upgrade:12,Note:"获得力量与敏捷；收益取决于攻击和格挡触发频率。"),
            "Rend"=>new(2,Attack:a,Upgrade:10,Note:"按目标非临时负面效果种类加伤；不能按易伤层数加伤。"),
            "RollingBoulder"=>new(0,Scaling:1.5,Upgrade:10,Note:"逐回合增强的伤害，三费慢启动；短战机会成本较高。"),
            "Salvo"=>new(2,Attack:a,Upgrade:7,Note:"攻击并保留全手牌一回合；排序收益不等同直接抽牌。"),
            "SecretTechnique"=>new(2,Draw:.5,ExhaustSource:x,Upgrade:9,Note:"零费检索技能；没有目标时空转，不凭空增加手牌数。"),
            "SecretWeapon"=>new(1,Draw:.5,ExhaustSource:x,Upgrade:8,Note:"零费检索攻击；高质量攻击目标决定价值。"),
            "SeekerStrike"=>new(2,Attack:a,Draw:.5,Upgrade:7,Note:"攻击后从有限牌库候选中检索一张；不是抽三张。"),
            "Shockwave"=>new(3,Block:1,Attack:.3,ExhaustSource:x,Upgrade:13,Note:"群体虚弱与易伤；多回合减伤，不能只读伤害变量。"),
            "Splash"=>new(1,Attack:.6,Upgrade:8,Note:"三选一跨角色攻击，当回合免费；按随机质量折扣。"),
            "Stratagem"=>new(0,Draw:.3,Upgrade:12,Note:"洗牌时检索；小循环和过牌提升触发率。"),
            "TheBomb"=>new(1,Attack:.5,Scaling:.7,Upgrade:9,Note:"三回合后群伤；需要撑到爆炸，不能当即时40伤。"),
            "TheGambit"=>new(-25,Block:2,Upgrade:0,Note:"以后受到未挡住的攻击会死亡；缺少战斗级无伤证明时不自动抓。"),
            "ThrummingHatchet"=>new(1,Attack:a,Draw:.25,Upgrade:6,Note:"打出后下回合回手；持续占费，已有强攻击时可能拥堵。"),
            "Volley"=>new(2,Attack:1.4,StrengthPayoff:1,Upgrade:12,Note:"X费随机多段攻击；按实际产费与力量评价，不视为零费。"),
            _=>null
        };
    }

    public static IEnumerable<ScorePart> Context(RunSnapshot s,CardSnapshot original)
    {
        var c=Definition(original);var id=c.ClassName;
        double V(string key)=>CardRegistry.Var(c,key);
        var profiles=s.Deck.Select(CardCatalog.Find).OfType<CardProfile>().ToArray();
        var strength=profiles.Sum(p=>p.Strength);
        var exhaust=profiles.Sum(CardMechanics.ActiveExhaust);
        var self=profiles.Sum(p=>p.SelfDamage);
        var draws=profiles.Sum(p=>p.Draw);
        var attackCount=s.Deck.Count(d=>d.Type=="Attack");
        var attackDensity=attackCount/(double)Math.Max(1,s.Deck.Length);
        var vulnerable=s.Deck.Count(d=>d.ClassName is "Bash" or "Uppercut" or "Shockwave" or "Tremble" or "Taunt" or "Dominate" or "Thunderclap" or "Break");
        var heavy=s.Deck.Where(d=>d.Type=="Attack"&&!d.CostsX).Select(d=>(double)d.Cost).OrderDescending().Take(3).ToArray();
        var heavyValue=heavy.Length==0?0:heavy.Average();
        var strikeCount=s.Deck.Count(d=>StrikeTags.Contains(d.ClassName));
        var copies=s.Deck.Count(d=>d.ClassName==id);
        double value=0;string reason="";
        void Set(double v,string r){value=v;reason=r;}
        switch(id)
        {
            case "AshenStrike":
                var once=s.Deck.Count(d=>d.Keywords.Contains("Exhaust")||d.Keywords.Contains("Ethereal")||d.Enchantment=="ENCHANTMENT.GOOPY");
                Set(Math.Min(8,(exhaust*1.4+once*.35)*V("ExtraDamage")/3),"可兑现的消耗堆成长（非假设首回合全消耗）");break;
            case "Dominate": Set(Math.Min(8,1+IroncladSynergies.VulnerableCoverage(s)*1.8+
                (s.Deck.Any(d=>d.ClassName=="MoltenFist")?1.2:0)),"易伤质量、叠层与延长支持本战力量；第二张可再次兑现");break;
            case "Break":Set(3+Math.Min(4,IroncladSynergies.VulnerablePayoffs(s)),"一费高伤五层易伤的质量提升与体系启动");break;
            case "Conflagration":Set(IroncladSynergies.MultiHitImprovement(s,c),"多段力量兑现及替换低效攻击的质量提升，不受攻击张数饱和抹除");break;
            case "CrimsonMantle":
                var ratio=s.Hp/Math.Max(1d,s.MaxHp);
                var turns=s.Hp<=4?0:ratio<.25?1:s.Act>=2?3:2;
                var recurring=V("CrimsonMantlePower")*.22*turns-turns*(.45+1.5*Math.Pow(1-ratio,2));
                Set(recurring-(turns==0?6:0)+(turns>0&&s.Deck.Any(d=>d.ClassName=="Juggernaut")?1.5:0),
                    "后续有效回合自动供防与格挡触发，扣除持续自伤；不计当回合救命");break;
            case "Colossus":Set(vulnerable==0?-2:Math.Min(4,1+vulnerable),"易伤覆盖支持本回合减伤");break;
            case "Cruelty":Set(vulnerable==0?-3:Math.Min(5,vulnerable*V("CrueltyPower")/25),"易伤覆盖与倍率增益");break;
            case "Bully":Set(Math.Min(3,vulnerable*.8),"易伤层数支持");break;
            case "MoltenFist":
                var coverage=vulnerable*15d/Math.Max(15,s.Deck.Length);
                var payoff=s.Deck.Count(d=>d.ClassName is "Bully" or "Dominate" or "Cruelty" or "Vicious" or "Colossus");
                Set((Math.Min(3.5,coverage*1.4)+(vulnerable>0?Math.Min(3,payoff*.9):0))/(1+copies),
                    "已有易伤的延长与易伤体系联动（需要先施加易伤，重复熔融之拳收益递减）");break;
            case "TwinStrike":
                Set(-Math.Min(2,s.Deck.Count(d=>d.ClassName is "SwordBoomerang" or "Whirlwind" or "Pummel" or "FiendFire")*.8),
                    "已有更强多段攻击，降低双重打击的补位需求");break;
            case "Cinder":Set(-1,"随机烧掉关键手牌风险");break;
            case "EvilEye":Set(Math.Min(3,exhaust*1.4),"先消耗再打出可获得两次格挡");break;
            case "ExpectAFight":Set(Math.Min(8,strength*1.8),"当前牌组力量转格挡");break;
            case "FightMe":Set(-1.5-2*Math.Pow(1-s.Hp/Math.Max(1d,s.MaxHp),2),"给予敌方力量的长战和低血风险");break;
            case "Havoc":Set(Math.Min(4,heavyValue)+(s.Deck.Any(d=>d.ClassName=="Headbutt")?1:0)-1.5,"免费出牌与牌库顶排序，扣除误烧");break;
            case "Hellraiser":Set(Math.Min(9,strikeCount*1.15)-2,"打击标签密度支持自动施放");break;
            case "HowlFromBeyond":Set(Math.Min(7,exhaust*2.5)-2,"送入消耗堆后的持续免费群攻");break;
            case "Juggling":Set(attackDensity>.55&&draws>1?2:-1,"同回合打三张攻击的可行性");break;
            case "NotYet":Set(Math.Min(5,(s.MaxHp-s.Hp+8)*.12),"缺血及后续战损提供治疗空间");break;
            case "OneTwoPunch":Set(Math.Min(7,heavyValue*(c.UpgradeLevel>0?2:1.3)),"额外施放高质量攻击；升级增加可配合张数");break;
            case "PerfectedStrike":Set(Math.Min(6,strikeCount*V("ExtraDamage")*.35),"实际打击标签增伤，不按全部攻击计数");break;
            case "Pillage":Set(Math.Min(3,attackDensity*2),"攻击密度带来的连续抽牌");break;
            case "PrimalForce":
                Set(Math.Min(6,s.Deck.Count(d=>d.Type=="Attack"&&d.Cost<=1&&CardRegistry.Var(d,"Damage")<=10)*.9)
                    -Math.Min(4,s.Deck.Count(d=>d.Type=="Attack"&&d.Cost>=2)*.7),"弱攻击变巨石收益，扣除替换成熟核心的风险");break;
            case "Rampage":Set(Math.Clamp(12d/Math.Max(8,s.Deck.Length)+draws*.2-1,-1,1),"重复打出成长受循环速度限制");break;
            case "Spite":Set(Math.Min(3,self*2),"自伤源支持条件多段");break;
            case "Stomp":Set(Math.Min(2,attackDensity*3.5)*2,"本回合先打攻击减费，返还部分名义三费惩罚");break;
            case "TearAsunder":Set(Math.Min(6,self*2.5),"本战失血次数成长，优先可靠自伤");break;
            case "Unrelenting":Set(Math.Min(5,heavyValue*1.7),"下一张高费攻击免费");break;
            case "Vicious":Set(Math.Min(5,vulnerable*1.3)-1,"施加易伤触发过牌");break;
            case "Alchemize":Set(s.Relics.Contains("RELIC.SOZU")?-15:Math.Min(4,s.PotionSlots.Count(p=>p==null)*2+1),"药水生产及空位限制");break;
            case "Anointed":Set(Math.Min(7,s.Deck.Count(d=>d.Rarity=="Rare")*1.7)-2,"可检索的实际金卡数量");break;
            case "Automation":Set(Math.Min(4,draws*.7),"十次抽牌产费的触发频率");break;
            case "BeatDown":Set(Math.Min(8,heavyValue*2.4),"弃牌堆高费攻击的免费施放，首轮折扣");break;
            case "Calamity":Set(attackDensity>.5&&CardMechanics.EnergyDemand(s)<1?2:0,"有余费时才兑现随机攻击生成");break;
            case "Catastrophe":Set(Math.Min(7,(c.UpgradeLevel>0?3:2)*(1+heavyValue*.4)),"随机牌库牌免费施放的折扣收益");break;
            case "Bludgeon":Set(Math.Min(4,s.Deck.Count(d=>d.ClassName is "Unrelenting" or "Stampede" or "BeatDown" or "OneTwoPunch")*1.4),"高伤攻击与免费施放、额外施放配合");break;
            case "Fasten":Set(Math.Min(6,s.Deck.Count(d=>d.ClassName is "DefendIronclad" or "UltimateDefend")*.9)-1,"实际防御标签牌数量");break;
            case "GoldAxe":Set(Math.Min(5,draws*.8+1),"长战打牌次数成长");break;
            case "HandOfGreed":Set(Math.Min(4,new StrategicValue(s).Horizon*1.5),"剩余可消费路线中的击杀金币机会");break;
            case "Impatience":Set(2-attackDensity*5,"无攻击手牌条件的可达性");break;
            case "Mayhem":Set(Math.Min(4,heavyValue+1)-Math.Min(2,s.Deck.Count(d=>d.ClassName is "Feed" or "Havoc")),"免费施放费用收益与顺序风险");break;
            case "MindBlast":Set(Math.Min(6,Math.Max(0,s.Deck.Length-7)*.25),"首轮剩余牌库决定伤害");break;
            case "Panache":Set(Math.Min(4,s.Deck.Count(d=>d.Cost==0&&!d.CostsX)*.5+draws*.4)-1,"低费运转支持一回合五次出牌");break;
            case "Prolong":Set(Math.Min(5,profiles.Sum(p=>p.Block)*.6)-2,"足够格挡才能向下一回合转移");break;
            case "Rend":Set(Math.Min(3,vulnerable>0?1.3:0)+(s.Deck.Any(d=>d.ClassName is "Uppercut" or "Shockwave")?1:0),"可提供的不同负面效果种类");break;
            case "SecretTechnique":Set(s.Deck.Any(d=>d.Type=="Skill"&&d.Rarity=="Rare")?1.5:0,"检索高价值技能");break;
            case "SecretWeapon":Set(Math.Min(3,heavyValue),"检索可兑现的强攻击");break;
            case "Stratagem":Set(Math.Clamp(draws*12/Math.Max(10,s.Deck.Length),0,3),"循环速度支持洗牌检索");break;
        }
        if(reason.Length>0)yield return new(reason,value/(1+copies*(id=="Dominate"&&IroncladSynergies.SupportedSecondDominate(s)?.25:.6)));
    }
    // Native CardTag.Strike, audited from the installed model declarations.
    public static readonly HashSet<string> StrikeTags=new(StringComparer.Ordinal)
    {"StrikeIronclad","PommelStrike","PerfectedStrike","TwinStrike","SetupStrike","AshenStrike","UltimateStrike","SeekerStrike"};
}
