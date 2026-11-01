namespace SpireStrategist.Core;

// Shared effects use the same rule for every role. Neow's explicit ranking remains in NeowPolicy.
public static class AncientItems
{
    private static readonly HashSet<string> Supported="PAELS_CLAW NUTRITIOUS_SOUP DUSTY_TOME FIDDLE JEWELED_MASK MUSIC_BOX CROSSBOW BRILLIANT_SCARF IRON_CLUB VELVET_CHOKER SPIKED_GAUNTLETS PAELS_LEGION PAELS_TEARS PAELS_WING BLESSED_ANTLER BIIIG_HUG PRESERVED_FOG GLASS_EYE".Split(' ').ToHashSet();
    public static RelicAssessment? Evaluate(string id,RunSnapshot s)
    {
        id=id.Split('.').Last();if(!Supported.Contains(id))return null;
        var a=new CharacterItems.Context(s);var v=a.V;
        var plays=a.Draw+a.Shivs+a.Souls*2+s.Deck.Count(c=>c.Cost==0&&!c.CostsX)*.5;
        var cleanup=v.Exhaust+(s.Character=="Silent"&&SilentMechanics.CanCleanJunk(s)?2:0);
        double CardValue(CardSnapshot? c)=>c==null?0:Math.Clamp(v.Add(c),-8,20);
        var powers=s.Deck.Where(c=>c.Type=="Power").ToArray();
        var maskTargets=powers.Any(c=>!c.Keywords.Contains("Innate"))?powers.Where(c=>!c.Keywords.Contains("Innate")).ToArray():powers;
        (double Value,string Selection,string Note) model=id switch
        {
            "PAELS_CLAW"=>(s.Deck.Count(c=>(c.ClassName.StartsWith("Defend")||c.ClassName=="UltimateDefend")&&!c.Modified)*(2+Math.Min(1.5,cleanup)),"","按原生防御标签选取，包括基础防御与究极防御；其他格挡牌、召唤和奥斯提承伤不计入目标。"),
            "NUTRITIOUS_SOUP"=>(s.Deck.Count(c=>!c.Modified&&c.Rarity=="Basic"&&c.ClassName.StartsWith("Strike"))*2.2,"","原生代码仅附魔基础打击；不加最大生命，也不覆盖非基础打击。"),
            "DUSTY_TOME"=>(s.Character switch
            {
                "Regent"=>Math.Max(-4,CardValue(HeroPolicy.Card("TheSealedThrone",true))),
                "Necrobinder"=>Math.Max(-4,CardValue(HeroPolicy.Card("ForbiddenGrimoire",true))),
                "Silent"=>Math.Max(-4,CardValue(SilentPolicy.Card("WraithForm",true))),
                "Defect"=>Math.Max(-4,CardValue(DefectPolicy.Card("BiasedCognition",true))),
                _=>8+cleanup*2+v.Profiles.Sum(p=>p.ExhaustPayoff)*3
            },"","按本角色实际获得的升级先古卡估值；机器人偏差认知、猎人幽魂形态、储君封印王座、亡灵禁忌魔典分别匹配体系。"),
            "FIDDLE"=>(16-a.Draw*4-a.Souls*4-a.Shivs-a.Generation*.5,"","每回合多抽两张，但关闭回合内抽牌；灵魂及连打过牌不能继续按原收益计算。"),
            "JEWELED_MASK"=>(maskTargets.Length==0?0:Math.Clamp(maskTargets.Average(c=>Math.Max(0,v.Add(c))*.6+c.Cost*2.5+2),0,22),"","开局随机一张已有能力进手且本回合免费，优先非固有；按目标平均收益，不假定选中最强能力。"),
            "MUSIC_BOX"=>(s.Deck.Any(c=>c.Type=="Attack")?3+a.Cap(a.Hits*.6+s.Deck.Count(c=>c.Type=="Attack"&&c.Cost==0)*1.5,10):1,"","每回合首张攻击生成虚无复制品；能量和星费仍需支付，玩家可安排首击顺序。"),
            "CROSSBOW"=>(5+a.Cap(a.Strength+a.Generation*.5+a.Attacks*.3,8),"","每回合随机一张攻击本回合免费；通用即时输出，力量与生成联动加分，不保证属于当前体系。"),
            "BRILLIANT_SCARF"=>(2+a.Cap(plays*.7,7)+Math.Min(8,s.Deck.Count(c=>c.Cost>=2||HeroPolicy.Stars(c)>=2)*.9)*(plays>=3?1:.3),"","每回合第五张手牌同时免能量和星费；需打得出五张且有昂贵出口。"),
            "IRON_CLUB"=>(5+a.Cap(plays,12),"","每四次打牌抽一；小刀、灵魂和零费连打均贡献次数。"),
            "VELVET_CHOKER"=>(v.Energy-Math.Min(40,plays*2.5+a.Souls*2),"","每回合最多六张：惩罚真实连打需求，灵魂、小刀、生成牌也占次数。"),
            "SPIKED_GAUNTLETS"=>(v.Energy-powers.Sum(c=>3+(c.Cost>=2?1.5:0)),"","每回合加能量但所有能力加费；按已有能力的启动负担估值，不把星费当能量费。"),
            "PAELS_LEGION"=>(s.Deck.Where(c=>c.Variables.GetValueOrDefault("Block")>0).Select(c=>Math.Min(14,(double)c.Variables["Block"]*.45)).DefaultIfEmpty(0).Max()+2,"","翻倍单次格挡后有冷却；大额格挡受益，召唤承伤不算获得格挡。"),
            "PAELS_TEARS"=>(3+Math.Min(5,a.Expensive*1.5)+(v.Has("ICE_CREAM")?4:0)-Math.Min(3,plays*.3),"","留能量到回合结束换取下回合额外能量；先扣当前回合不花能量的机会成本。"),
            "PAELS_WING"=>(5*Math.Clamp(v.Horizon,.3,1.7)-(a.Hero?.DamageEngines==0?5:0),"","跳过两次卡牌奖励换遗物；仍缺输出核心时失去抓牌机会更昂贵。"),
            "BLESSED_ANTLER"=>(v.Energy-10+Math.Min(8,cleanup*2)-(s.Deck.Length<15?3:0),"","加能量会塞三张晕眩；处理状态与消耗收益能缓解，薄牌组抽牌污染更明显。"),
            "BIIIG_HUG"=>(v.RemoveBest(4)-(s.Deck.Length<16?16:7)+Math.Min(7,cleanup*1.5),"remove","删四张后每次洗牌塞煤灰；精简收益必须扣除反复污染成本。"),
            "PRESERVED_FOG"=>(v.RemoveBest(3)-v.Curse("Folly"),"remove","删三张同时加入愚行；使用当前角色诅咒代价，不把删牌视为无代价。"),
            "GLASS_EYE"=>(s.Deck.Length>=25?6:12*Math.Clamp(v.Horizon,.5,1.4),"","两次普通、两次罕见、一次稀有选牌，可跳过；不是强制五次入组，也不保证五张核心。"),
            _=>throw new InvalidOperationException(id)
        };
        return new(model.Value,model.Selection,model.Note);
    }
}
