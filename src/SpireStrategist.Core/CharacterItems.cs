namespace SpireStrategist.Core;

// Native 0.111.0 role pools, evaluated by mechanisms rather than the owning character's name.
public static class CharacterItems
{
    internal sealed class Context
    {
        public RunSnapshot S; public StrategicValue V; public HeroPolicy.Composition? Hero;
        public double Attacks,Hits,Draw,Shivs,Discard,Poison,Weak,Vulnerable,OrbSources,Dark,Stars,Spend,Forge,Generation,Osty,Summon,Souls,Ethereal,Exhaust,SelfHarm,Strength;
        public int Blocks,Retain,Expensive;
        public Context(RunSnapshot s)
        {
            S=s;V=new(s);Hero=HeroPolicy.Supports(s.Character)?HeroPolicy.Analyze(s):null;
            double Count(Func<CardSnapshot,bool> p)=>s.Deck.Count(p);
            bool Basic(CardSnapshot c)=>c.Rarity=="Basic";
            Attacks=s.Deck.Where(c=>c.Type=="Attack"&&!HeroPolicy.Tag(c,"osty_attack")).Sum(c=>Basic(c)?.2:1);
            Hits=s.Deck.Where(c=>c.Type=="Attack"&&!HeroPolicy.Tag(c,"osty_attack")).Sum(c=>EnchantmentValue.Hits(c)*(Basic(c)?.2:1));
            if(s.Character=="Silent"){var a=SilentPolicy.Analyze(s);Shivs=a.Shivs;Discard=a.Discard;Poison=a.PoisonSeeds;Weak=a.Weak;Draw=a.Draw;Attacks+=Shivs;Hits+=Shivs;}
            else {Draw=Hero?.Draw??V.Draw;Weak=Count(c=>c.Variables.GetValueOrDefault("WeakPower")>0);Poison=Count(c=>c.Variables.GetValueOrDefault("PoisonPower")>0);}
            Vulnerable=Count(c=>c.Variables.GetValueOrDefault("VulnerablePower")>0);
            OrbSources=Count(c=>new[]{"lightning","lightning_engine","frost","frost2","frost3","dark","glass"}.Any(t=>DefectPolicy.Tag(c,t)));
            Dark=Count(c=>DefectPolicy.Tag(c,"dark"));
            Stars=Hero?.Stars??0;Spend=Hero?.StarDemand??0;Forge=Hero?.Forge??0;Generation=(Hero?.Generation??0)+Shivs;
            if(Forge>0){Attacks+=1;Hits+=1;}
            Osty=Hero?.OstyAttacks??0;Summon=Hero?.Summon??0;Souls=Hero?.Souls??0;Ethereal=Hero?.Ethereal??0;
            Exhaust=Count(c=>c.Keywords.Contains("Exhaust")||c.Keywords.Contains("Ethereal"))+V.Exhaust*2+Shivs+Souls*2;
            SelfHarm=Count(c=>c.ClassName is "Offering" or "Bloodletting" or "Hemokinesis" or "Brand" or "Combust" or "BloodWall" or "CrimsonMantle");
            Strength=V.Strength;Blocks=(int)Count(c=>c.Variables.GetValueOrDefault("Block")>0);
            Retain=(int)Count(c=>c.Keywords.Contains("Retain"));if(V.Has("RUNIC_PYRAMID"))Retain+=3;
            Expensive=(int)Count(c=>c.Cost>=3&&!c.CostsX);
        }
        public bool Has(string name)=>S.Deck.Any(c=>c.ClassName==name);
        public double Cap(double value,double max=12)=>Math.Clamp(value,0,max);
    }
    private static readonly HashSet<string> RelicIds="BURNING_BLOOD BRIMSTONE CHARONS_ASHES DEMON_TONGUE PAPER_PHROG RED_SKULL RUINED_HELMET SELF_FORMING_CLAY RING_OF_THE_SNAKE HELICAL_DART NINJA_SCROLL PAPER_KRANE SNECKO_SKULL TINGSHA TOUGH_BANDAGES TWISTED_FUNNEL CRACKED_CORE DATA_DISK GOLD_PLATED_CABLES RUNIC_CAPACITOR EMOTION_CHIP SYMBIOTIC_VIRUS METRONOME POWER_CELL DIVINE_RIGHT FENCING_MANUAL GALACTIC_DUST LUNAR_PASTRY MINI_REGENT ORANGE_DOUGH REGALITE VITRUVIAN_MINION BOUND_PHYLACTERY BONE_FLUTE BOOK_REPAIR_KNIFE FUNERARY_MASK BIG_HAT BOOKMARK IVORY_TILE UNDYING_SIGIL LIZARD_TAIL JOSS_PAPER ORRERY".Split(' ').ToHashSet();
    public static RelicAssessment? Relic(string id,RunSnapshot s)
    {
        id=id.Split('.').Last();if(!RelicIds.Contains(id))return null;
        s=s with{Deck=s.Deck.Where(c=>!MultiplayerAdvice.Ignore(c)).ToArray()};var a=new Context(s);var h=Math.Clamp(a.V.Horizon,.5,1.6);
        (double Value,string Note)? model=id switch
        {
            "BURNING_BLOOD"=>(9*h,"战后恢复；不假定能靠遗物救回战斗内死亡。"),
            "BRIMSTONE"=>(2+a.Cap(a.Hits*2)-Math.Max(4,10-a.V.Block),"自身逐回合力量与敌方力量一起增长；缺防守或不靠攻击输出时降值。"),
            "CHARONS_ASHES"=>(1+a.Cap(a.Exhaust*1.3,18),"按真实消耗频率触发群伤，小刀与灵魂也可触发；无消耗来源低价值。"),
            "DEMON_TONGUE"=>(3+a.Cap(a.SelfHarm*4),"每回合仅第一次回合内失血恢复；主动卖血联动，不等于敌方回合回血。"),
            "PAPER_PHROG"=>(a.Vulnerable>0?3+a.Cap(a.Hits*1.5):1,"易伤增幅需要施加易伤及实际攻击伤害，毒、灾厄和球伤不按攻击增幅。"),
            "RED_SKULL"=>((s.Hp<=s.MaxHp*.5?3:1)+a.Cap(a.Hits)*(s.Hp<=s.MaxHp*.5?1:.35),"低于半血才有力量；估值不鼓励为了触发而主动送血。"),
            "RUINED_HELMET"=>(a.Strength>0?3+a.Cap(a.Strength*2+a.Hits):a.V.Has("VAJRA")?5:1,"只翻倍每战第一次获得的力量，不把后续所有力量增长翻倍。"),
            "SELF_FORMING_CLAY"=>(3+a.Cap(a.SelfHarm*3),"失血后提供下回合格挡；卖血联动有延迟，不能抵挡本次失血。"),
            "RING_OF_THE_SNAKE"=>(9,"开局额外过牌，所有体系均可受益。"),
            "HELICAL_DART"=>(1+a.Cap(a.Shivs*Math.Min(3,a.Blocks*.6)),"小刀提供的敏捷仅本回合有效，需要随后打出格挡牌。"),
            "NINJA_SCROLL"=>(5+a.Cap(a.Shivs*.6+s.Deck.Count(c=>SilentPolicy.Tag(c,"shiv_payoff"))*2,7),"只在开局生成三张小刀；不当作持续供刀。"),
            "PAPER_KRANE"=>(a.Weak>0?4+a.Cap(a.Weak*3):1,"提高已有虚弱的减伤，自己不会施加虚弱。"),
            "SNECKO_SKULL"=>(a.Poison>0?4+a.Cap(a.Poison*3+(a.Has("Envenom")?a.Shivs:0)):1,"按施毒次数额外加毒，不按现有毒层数倍增。"),
            "TINGSHA"=>(1+a.Cap(a.Discard*2.2),"只计回合内主动弃牌；正常回合结束丢牌不触发。"),
            "TOUGH_BANDAGES"=>(1+a.Cap(a.Discard*3.2,18),"主动弃牌触发格挡；没有弃牌入口时不因稀有而高估。"),
            "TWISTED_FUNNEL"=>(5+(a.Poison>0?4:0)+(a.Has("Catalyst")?3:0),"开局群体施毒有独立收益，有毒放大时额外受益。"),
            "CRACKED_CORE"=>(4,"开局一个闪电球，不是持续生成。"),
            "DATA_DISK"=>(a.OrbSources>0?2+a.Cap(a.OrbSources*2):1,"集中只提高可用充能球收益；纯物理或暴涨后无球位时折扣。"),
            "GOLD_PLATED_CABLES"=>(a.OrbSources>0?2+a.Cap(a.OrbSources+a.Dark*2):1,"最右球额外触发被动；不是额外激发，黑球可积累。"),
            "RUNIC_CAPACITOR"=>(a.OrbSources>0?2+a.Cap(a.OrbSources+a.Dark*2)-(a.Has("Thunder")&&a.Dark==0?2:0):1,"额外球位需要填充；黑球重被动积累，快速顶球激发并非必需球位。"),
            "EMOTION_CHIP"=>(a.OrbSources>0?2+a.Cap(a.OrbSources*1.2):1,"前回合受伤才重触发球被动；不主动制造战损。"),
            "SYMBIOTIC_VIRUS"=>(4+a.Cap(a.Dark*2+(a.Has("MultiCast")||a.Has("Quadcast")?4:0),9),"开局黑球可单独输出；黑球积累及多次激发进一步提高价值。"),
            "METRONOME"=>(a.OrbSources>=3?7+a.Cap(a.OrbSources,5):a.OrbSources>0?3:0,"每战首次生成七球才触发一次群伤，不能按持续伤害引擎估值。"),
            "POWER_CELL"=>(2+a.Cap(s.Deck.Count(c=>c.Cost==0&&!c.CostsX&&!HeroPolicy.Tag(c,"basic"))*2,10),"开局定向抽取零费牌；零能量星费牌仍需支付星星。"),
            "DIVINE_RIGHT"=>(8,"开局三颗星星，不是每回合获得。"),
            "FENCING_MANUAL"=>(4+(a.Forge>0?5:0)+(s.Deck.Any(c=>HeroPolicy.Tag(c,"sword_payoff")||HeroPolicy.Tag(c,"sword_access"))?5:0),"开局铸造十，提供剑入口；取剑、重放及已有铸造放大收益。"),
            "GALACTIC_DUST"=>(1+a.Cap(Math.Min(a.Stars+HeroPolicy.OpeningStars(s)*.3,a.Spend)),"每花十星给十格挡；生成星星本身不触发。"),
            "LUNAR_PASTRY"=>(2+a.Cap(a.Spend*1.3+(a.Has("BlackHole")?5:0)),"回合结束供星，服务星费与黑洞；无消费出口不自动高分。"),
            "MINI_REGENT"=>(a.Spend>0&&a.Attacks>0?3+a.Cap(a.Hits+Math.Min(a.Stars,a.Spend)):1,"每回合首次花星增加玩家力量；必须同时具备花星和攻击出口。"),
            "ORANGE_DOUGH"=>(5+a.Cap(s.Deck.Count(c=>HeroPolicy.Tag(c,"generation_payoff"))*2,6),"开局随机两张无色，生成联动有效；不保证得到可免费打出的核心。"),
            "REGALITE"=>(a.Generation>0||a.Forge>0?3+a.Cap(a.Generation*1.5+(a.Forge>0?1:0),7):1,"每回合第一次生成牌才起盾；普通抽牌不算生成。"),
            "VITRUVIAN_MINION"=>(s.Deck.Any(c=>c.ClassName is "Begone" or "Charge" or "Guards")?14:1,"只翻倍仆从牌的伤害和格挡，无仆从来源时不加成普通攻击。"),
            "BOUND_PHYLACTERY"=>(9,"每回合召唤奥斯提，不直接恢复玩家生命。"),
            "BONE_FLUTE"=>(1+a.Cap(a.Osty*2),"按奥斯提攻击触发格挡；玩家攻击不能触发。"),
            "BOOK_REPAIR_KNIFE"=>(a.Hero?.Doom>=10?6+a.Cap(a.Hero.Doom*.15,7)*h:1,"非爪牙敌人必须死于灾厄才恢复；直接攻击击杀不兑现。"),
            "FUNERARY_MASK"=>(6+a.Cap(s.Deck.Count(c=>HeroPolicy.Tag(c,"soul_payoff"))*2.5,8),"开局三张灵魂进抽牌堆，延迟抽到；不是持续生成，也不是立刻抽六张。"),
            "BIG_HAT"=>(5+a.Cap(s.Deck.Count(c=>HeroPolicy.Tag(c,"ethereal_payoff"))*2,7),"开局随机虚无两张可触发联动，但费用和抽到的功能不确定。"),
            "BOOKMARK"=>(a.Retain>0?3+a.Cap(a.Retain*2):1,"仅降低回合结束时被保留的随机一张牌费用；需要实际保留来源。"),
            "IVORY_TILE"=>(a.Expensive>0?3+a.Cap(a.Expensive*2.5):1,"实际支付至少三能量才退款；已经减到零费的高费牌不能按原价兑现。"),
            "UNDYING_SIGIL"=>(a.Hero?.Doom>=15?10:1,"灾厄达到敌方生命阈值才减伤，帮助度过等待斩杀的敌方回合。"),
            "LIZARD_TAIL"=>(18+Math.Min(8,s.MaxHp*.05),"一次致死保护，恢复半血；不是可重复回血。"),
            "JOSS_PAPER"=>(a.Exhaust>=5?3+a.Cap(a.Exhaust*.65):a.Exhaust*.6,"每消耗五张抽一张；包括小刀和灵魂，不把弃牌当消耗。"),
            "ORRERY"=>(s.Deck.Length>=25?5:10+(s.Act==1?3:0),"五次选牌机会可以跳过；不能假定必出五张优质核心。"),
            _=>null
        };
        return model is {} m?new(m.Value,Note:m.Note):null;
    }
    private static readonly HashSet<string> PotionIds="BloodPotion Ashwater SoldiersStew PoisonPotion CunningPotion GhostInAJar FocusPotion PotionOfCapacity EssenceOfDarkness StarPotion KingsCourage CosmicConcoction PotionOfDoom BoneBrew PotOfGhouls".Split(' ').ToHashSet();
    public static PotionAssessment? Potion(RunSnapshot s,PotionCandidate p)
    {
        if(!PotionIds.Contains(p.Name))return null;
        s=s with{Deck=s.Deck.Where(c=>!MultiplayerAdvice.Ignore(c)).ToArray()};
        var a=new Context(s);var boss=s.NextNodeTypes.Contains("Boss")||s.Act>=3;
        (double Value,bool Defense,string Note)? m=p.Name switch
        {
            "BloodPotion"=>(Math.Min(s.MaxHp-s.Hp,s.MaxHp*.2)*.55+(s.Hp==s.MaxHp?1:0),true,"最大生命比例恢复；只计实际缺血，不强求卖血体系。"),
            "Ashwater"=>(5+a.Cap(a.V.Basics*.4+s.Deck.Count(c=>c.Type is "Curse" or "Status")*2+a.V.Profiles.Sum(p=>p.ExhaustPayoff)*2),false,"主动清理手牌，适用各角色；消耗联动另加，不能清理未抽到的诅咒。"),
            "SoldiersStew"=>(a.Cap(s.Deck.Where(c=>c.Name.Contains("打击")||c.ClassName.Contains("Strike")).Sum(c=>c.Rarity=="Basic"?1:4),20),false,"只给名字含打击的牌重放；按现有目标质量，不因战士身份固定高分。"),
            "PoisonPotion"=>(boss?4+a.Cap(a.Poison*1.5+(a.Has("Catalyst")?5:0),10):7+a.Cap(a.Poison,5),false,"六层毒有独立收益，施毒与毒放大配套提高价值。"),
            "CunningPotion"=>(6+a.Cap(a.Shivs+a.Strength*2+(a.Has("Accuracy")?4:0),10),false,"三张小刀+可独立造成伤害；力量、小刀增伤和连打联动另算。"),
            "GhostInAJar"=>(22,true,"一回合无实体，跨体系防守；不是整场免伤。"),
            "FocusPotion"=>(a.OrbSources>0?6+a.Cap(a.OrbSources*2,12):0,false,"集中服务实际充能球；纯物理没有球来源则无收益。"),
            "PotionOfCapacity"=>(a.OrbSources>0?(a.Dark>0?14:a.OrbSources>=3?8:3):0,false,"需要填充球位；黑球被动积累更受益，顶球激发体系并非必需。"),
            "EssenceOfDarkness"=>(s.Character!="Defect"?0:a.Dark>0||a.Has("MultiCast")||a.Has("Quadcast")?18:a.OrbSources>0?3:2,false,"按球位生成黑球；需积累或激发，可能顶掉冰球而损失防守。"),
            "StarPotion"=>(s.Character=="Regent"?a.Spend>0?7+a.Cap(a.Spend-a.Stars,6):a.Has("BlackHole")?7:1:0,false,"三颗星星只服务真实星费或获星触发，不直接提供能量。"),
            "KingsCourage"=>(s.Character=="Regent"?5+a.Cap(a.Forge*.25+s.Deck.Count(c=>HeroPolicy.Tag(c,"sword_payoff")||HeroPolicy.Tag(c,"sword_access"))*2,12):0,false,"铸造十五，缺剑时也可生成剑；仍需能量及取剑机会兑现。"),
            "CosmicConcoction"=>(7+a.Cap(a.Generation+s.Deck.Count(c=>HeroPolicy.Tag(c,"generation_payoff"))*2,8),false,"三张随机升级无色牌，生成联动有收益；不能保证抽到所缺核心或付得起费用。"),
            "PotionOfDoom"=>(boss?4+a.Cap(s.Deck.Count(c=>HeroPolicy.Tag(c,"doom_payoff"))*3,12):10,false,"三十三灾厄不是立即伤害；小怪可达到斩杀阈值，Boss 更依赖放大或提前兑现。"),
            "BoneBrew"=>(s.Character=="Necrobinder"?9+a.Cap(a.Osty+s.Deck.Count(c=>HeroPolicy.Tag(c,"summon_payoff"))*3,9):0,true,"召唤十五提供奥斯提承伤与生命联动，不是玩家回复十五血。"),
            "PotOfGhouls"=>(10+a.Cap(a.Souls+s.Deck.Count(c=>HeroPolicy.Tag(c,"soul_payoff"))*2,8),false,"两张灵魂进手，提供过牌与灵魂触发；当手牌拥挤或禁止抽牌时不能全额兑现。"),
            _=>null
        };
        return m is {} item?new(item.Value,item.Defense,item.Note):null;
    }
}
