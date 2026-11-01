namespace SpireStrategist.Core;

// These numbers are hand-authored strategic proxies, not simulated damage or win probabilities.
// Curated profiles override the complete, versioned data registry.
public sealed record CardProfile(double Base, double Attack = 0, double Block = 0,
    double Draw = 0, double Energy = 0, double Scaling = 0, double ExhaustSource = 0,
    double ExhaustPayoff = 0, double Strength = 0, double StrengthPayoff = 0,
    double SelfDamage = 0, double Upgrade = 8, string Note = "");

public static class CardCatalog
{
    public static readonly IReadOnlyDictionary<string, CardProfile> Profiles =
        new Dictionary<string, CardProfile>(StringComparer.Ordinal)
        {
            ["StrikeIronclad"] = new(-3, Attack: .4, Upgrade: 3),
            ["DefendIronclad"] = new(-3, Block: .5, Upgrade: 3),
            ["Bash"] = new(2, Attack: 1, Upgrade: 4.5, Note: "升级增加2伤和1回合易伤；按其他易伤来源与实际联动评估，不作为默认关键升级。"),
            ["Anger"] = new(2, Attack: .8, Upgrade: 5, Note: "零费输出；复制导致的战斗内膨胀尚未模拟。"),
            ["ShrugItOff"] = new(2.5, Block: 1, Draw: .5, Upgrade: 8),
            ["PommelStrike"] = new(2.5, Attack: 1, Draw: .5, Upgrade: 13),
            ["Thunderclap"] = new(2, Attack: 1.1, Upgrade: 5, Note: "群体易伤；当前尚不按具体敌人估值。"),
            ["IronWave"] = new(2, Attack: .6, Block: .6, Upgrade: 6),
            ["TwinStrike"] = new(0, Attack: 1, StrengthPayoff: 1, Upgrade: 7, Note:"一费两段共10伤（升级14）；降低过渡先验，保留真实双段力量收益，不作为默认力量核心。"),
            ["BattleTrance"] = new(3, Draw: 1.5, Upgrade: 7, Note: "升级多抽1张；同回合禁抽限制使多张互相冲突。"),
            ["Offering"] = new(5, Draw: 1.5, Energy: 1.5, SelfDamage: 1, ExhaustSource: .2, Upgrade: 14),
            ["Inflame"] = new(3, Strength: 1, Upgrade: 6, Note:"固定力量持续本战，但不逐回合成长；按现有力量来源、阶段和重复张数评估。"),
            ["Bloodletting"] = new(1, Energy: 1.5, SelfDamage: .5, Upgrade: 10),
            ["ThinkingAhead"] = new(1, Draw: .5, ExhaustSource: .2, Upgrade: 10, Note:"抽二放回一，净手牌不增加；零费整理与自替换。升级后不消耗。"),
            ["ForgottenRitual"] = new(1, Energy: 1.5, ExhaustSource: .2, Upgrade: 11, Note:"付一得三，净产两费；需要先有一费启动。"),
            ["Stampede"] = new(0, Scaling: .3, Upgrade: 13, Note:"自动打出攻击的费用利用依赖牌组；不直接生成能量。"),
            ["Cascade"] = new(2, Draw: .8, Upgrade: 14, Note:"X费自动打出抽牌堆顶部的牌，不等于直接抽入手牌；顺序、目标和负面牌有风险。"),
            ["PactsEnd"] = new(-1, Attack: .8, ExhaustPayoff: .4, Upgrade: 6, Note:"消耗堆至少三张才能造成群伤；本身不提供消耗手段。"),
            ["Unmovable"] = new(0, Scaling: .5, Upgrade: 13, Note:"格挡引擎组件，按实际格挡来源估值。"),
            ["Thrash"] = new(1, Attack: .8, StrengthPayoff: 1, ExhaustSource: .8, Upgrade: 9, Note:"两次伤害后随机消耗手中攻击，继承单次伤害；可能吞掉关键攻击。"),
            ["Rupture"] = new(0, Scaling: .5, Upgrade: 11, Note:"自伤成长须有实际触发来源，不作为无条件力量。"),
            ["UltimateStrike"] = new(-1, Attack: 1.4, Upgrade: 6, Note:"高数值过渡攻击，缺输出时仍有价值；不当成长核心。"),
            ["UltimateDefend"] = new(-1, Block: 1.1, Upgrade: 5, Note:"高数值过渡防御，仍可支撑格挡体系。"),
            ["TrueGrit"] = new(2, Block: 1, ExhaustSource: .7, Upgrade: 14, Note: "未升级时的随机消耗存在不稳定性。"),
            ["BurningPact"] = new(2, Draw: 1, ExhaustSource: 1, Upgrade: 11),
            ["Stoke"] = new(0, ExhaustSource: 1.5, Upgrade: 9, Note: "消耗其他手牌并生成等量随机战士牌；不等于从牌库抽牌，也不提供能量。可能损失手中的关键牌，生成收益仍为近似。"),
            ["Headbutt"] = new(2, Attack: 1, Upgrade: 5, Note: "回收关键牌的价值仍采用粗略先验。"),
            ["BodySlam"] = new(0, Attack: .2, Upgrade: 15, Note: "伤害取决于出牌时格挡，不能按固定伤害理解。"),
            ["SecondWind"] = new(1, Block: 1, ExhaustSource: 1.5, Upgrade: 10),
            ["FiendFire"] = new(3, Attack: 1.8, ExhaustSource: 2, StrengthPayoff: 1.5, Upgrade: 14, Note:"消耗其余手牌，按消耗张数多段攻击；手牌量和留下的关键牌决定价值。"),
            ["Juggernaut"] = new(0, Scaling: .5, Upgrade: 9, Note:"格挡触发伤害，依赖格挡次数而非单次格挡数值。"),
            ["Corruption"] = new(1, Scaling: .8, ExhaustSource: 2, Upgrade: 16, Note: "技能消耗引擎；长战耗尽技能的代价尚未模拟。"),
            ["FeelNoPain"] = new(0, ExhaustPayoff: 1.5, Upgrade: 10),
            ["DarkEmbrace"] = new(0, ExhaustPayoff: 1.5, Upgrade: 15),
            ["Barricade"] = new(-1, Scaling: .5, Upgrade: 14, Note: "需要稳定产出格挡，启动成本较高。"),
            ["DemonForm"] = new(2, Scaling: 2, Strength: 2, Upgrade: 10, Note: "慢速成长，短战的启动代价较高。"),
            ["Feed"] = new(3, Attack: .8, Scaling: .7, Upgrade: 13, Note: "最大生命成长按先验估计，不保证每场都能触发。"),
            ["Uppercut"] = new(4, Attack: 1.2, Block: .4, Upgrade: 16),
            ["FlameBarrier"] = new(3, Block: 1.7, Attack: .3, Upgrade: 10),
            ["Impervious"] = new(4, Block: 2.5, ExhaustSource: .3, Upgrade: 9),
            ["Whirlwind"] = new(3, Attack: 1.6, StrengthPayoff: 1.3, Upgrade: 14, Note: "X 费群攻按粗略能力标签估值。"),
            ["Hemokinesis"] = new(3, Attack: 1.7, SelfDamage: .4, Upgrade: 10),
            ["SwordBoomerang"] = new(1, Attack: 1, StrengthPayoff: 1.5, Upgrade: 10),
            ["Armaments"] = new(1, Block: .6, Upgrade: 16, Note: "临时升级价值按先验估计。"),
            ["Bludgeon"] = new(3, Attack: 3.2, Upgrade: 11, Note:"三费32点集中伤害；升级42，免费施放与额外打出另计。"),
            ["Rage"] = new(0, Block: .6, Upgrade: 8, Note: "收益取决于每回合打出的攻击数量。"),
            ["Dismantle"] = new(2, Attack: .8, Upgrade: 7, Note: "敌人易伤时攻击两次；额外收益按牌组易伤来源估计。"),
            ["Tremble"] = new(1, ExhaustSource: .2, Upgrade: 4, Note: "一费三层易伤，自身消耗；按高效易伤覆盖和主宰等实际联动评价，痛击不视为同质量替代。"),
            ["Brand"] = new(1, Strength: .5, Scaling: .3, ExhaustSource: 1, SelfDamage: 1.0/6, Upgrade: 10),
            ["DrumOfBattle"] = new(1, Draw: 1, ExhaustPayoff: .5, Upgrade: 5, Note: "仅被消耗时获得能量，不能按打出即回能估值。"),
            ["PanicButton"] = new(-1, Block: 1.3, ExhaustSource: .2, Upgrade: 5, Note: "之后两回合无法获得格挡；已下调即时格挡收益。"),
            ["Restlessness"] = new(-1, Draw: .3, Energy: .3, Upgrade: 5, Note: "仅剩此牌时才能抽牌回能；触发率暂按低先验。"),
            ["Purity"] = new(0, ExhaustSource: 1.2, Upgrade: 5, Note: "消耗手牌而非抽牌。"),
            ["Scrawl"] = new(4, Draw: 2, ExhaustSource: .2, Upgrade: 14, Note: "补满手牌按近似收益估计。"),
            ["MasterOfStrategy"] = new(3, Draw: 1.5, ExhaustSource: .2, Upgrade: 10),
            ["Finesse"] = new(1, Block: .4, Draw: .5, Upgrade: 5),
            ["FlashOfSteel"] = new(1, Attack: .5, Draw: .5, Upgrade: 5),
            ["DarkShackles"] = new(2, Block: 1.1, ExhaustSource: .2, Upgrade: 7, Note: "减力量的防御收益取决于敌人行动。"),
            ["AscendersBane"] = new(-8, ExhaustSource: .1, Upgrade: 0),
            ["Caltrops"] = new(0,Attack:.4,Scaling:.3,Upgrade:5,Note:"反伤价值依赖敌人攻击频率。"),
            ["Clash"] = new(-4,Attack:.8,Upgrade:5,Note:"手中只能有攻击牌的限制显著降低可用性。"),
            ["Distraction"] = new(-1,ExhaustSource:.2,Upgrade:10,Note:"随机生成技能不等同于稳定抽牌。"),
            ["DualWield"] = new(0,Scaling:.3,Upgrade:6,Note:"复制攻击或能力，收益依赖具体目标。"),
            ["Entrench"] = new(-2,Block:.5,Scaling:.5,Upgrade:15,Note:"翻倍现有格挡，需要格挡引擎支持。"),
            ["HelloWorld"] = new(-2,Scaling:.3,Upgrade:3,Note:"持续生成随机普通牌可能稀释运转。"),
            ["Outmaneuver"] = new(-1,Energy:.7,Upgrade:6,Note:"能量在下一回合获得，已折扣延迟和空转风险。"),
            ["Rebound"] = new(1,Attack:.9,Upgrade:5,Note:"下一张牌回牌库顶的价值依赖出牌顺序。"),
            ["RipAndTear"] = new(1,Attack:1.4,StrengthPayoff:1,Upgrade:8,Note:"随机目标两次攻击。"),
            ["Stack"] = new(-1,Block:.7,Upgrade:6,Note:"格挡取决于弃牌堆，首轮价值偏低。")
        };

    public static bool HasUnmodelledModification(CardSnapshot card)=>
        card.Affliction!=null || card.Modified && card.Enchantment is not
            ("ENCHANTMENT.SOWN" or "ENCHANTMENT.SWIFT" or "ENCHANTMENT.ADROIT" or "ENCHANTMENT.NIMBLE" or
             "ENCHANTMENT.GOOPY" or "ENCHANTMENT.SHARP" or "ENCHANTMENT.VIGOROUS");

    public static CardProfile? Find(CardSnapshot card)
    {
        if(MultiplayerAdvice.Ignore(card))return null;
        if(card.ClassName=="Apotheosis")return new(0,Upgrade:20);
        CardProfile? p = HeroStrategy.ProfileFor(card) ?? SilentStrategy.ProfileFor(card) ?? DefectPolicy.ProfileFor(card) ?? Profiles.GetValueOrDefault(card.ClassName);
        p ??= ExtraCardModels.Find(card);
        p ??= DefectNeutral.ProfileFor(card);
        if (p == null && CardRegistry.All.TryGetValue(card.ClassName, out var definition)) p = CardRegistry.Estimate(card, definition);
        if (p == null && card.Type is "Curse" or "Status") p = new(-8, Upgrade: 0, Note: "诅咒/状态牌按负担处理；特殊联动未计入。");
        if (p != null && card.UpgradeLevel > 0)
        {
            if(card.ClassName=="PommelStrike")p=p with{Draw=1};
            if(card.ClassName=="ShrugItOff")p=p with{Block=1.1};
            if(card.ClassName=="TwinStrike")p=p with{Attack=1.4};
            if(card.ClassName=="SwordBoomerang")p=p with{Attack=1.2,StrengthPayoff=2};
            if(card.ClassName=="Bludgeon")p=p with{Attack=4.2};
            if(card.ClassName=="Bloodletting")p=p with{Energy=2.25};
            if(card.ClassName=="ForgottenRitual")p=p with{Energy=2.25};
            if(card.ClassName=="ThinkingAhead")p=p with{ExhaustSource=0};
        }
        if (p != null && card.Modified)
        {
            var amount = Math.Max(0, card.EnchantmentAmount);
            var onceFactor = card.Keywords.Contains("Exhaust") ? 1 : .5;
            var known = true;
            p = card.Enchantment switch
            {
                "ENCHANTMENT.SOWN" => p with { Energy = p.Energy + amount * .75 * onceFactor },
                "ENCHANTMENT.SWIFT" => p with { Draw = p.Draw + amount * .5 * onceFactor },
                "ENCHANTMENT.ADROIT" or "ENCHANTMENT.NIMBLE" => p with { Block = p.Block + amount / 10.0*(card.ClassName=="SecondWind"?2:1) },
                "ENCHANTMENT.GOOPY" => p with { Block=p.Block+amount/10.0,ExhaustSource=Math.Max(.2,p.ExhaustSource) },
                "ENCHANTMENT.SHARP" => p with { Attack = p.Attack + amount / 10.0 },
                "ENCHANTMENT.VIGOROUS" => p with { Attack = p.Attack + amount / 10.0 * onceFactor },
                _ => Unknown()
            };
            CardProfile Unknown() { known = false; return p; }
            p = p with { Note = p.Note + (known ? " 已估计此附魔收益；首次触发效果按一次计算。" : " 附魔/异变已记录，当前仅评价基础能力及实际费用，额外效果未计分。")
                + (known && card.Affliction != null ? " 异变额外效果未计分。" : "") };
        }
        return p;
    }
}
