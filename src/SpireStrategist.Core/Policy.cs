namespace SpireStrategist.Core;

public sealed class Policy
{
    public const string Version = "ironclad-mechanisms-0.6.22";
    private readonly PolicyWeights w;
    public Policy(PolicyWeights? weights = null, bool allowExperimentalStoke = false)
    {
        w = weights ?? new();
        // Retained parameter for old offline tools; feedback now authorizes the
        // contextual Stoke policy in normal play too.
        w.Validate();
    }

    public Evaluation Evaluate(RunSnapshot state, IReadOnlyList<DecisionOption> options)
    {
        if (HeroPolicy.Supports(state.Character)) return HeroPolicy.Evaluate(state, options);
        if (state.Character == "Silent") return SilentPolicy.Evaluate(state, options);
        if (state.Character == "Defect") return DefectPolicy.Evaluate(state, options);
        var warnings = new List<string>
        {
            "启发式分数，不是胜率；参数尚未训练。",
            "部分遗物联动采用机制先验，药水及复杂战斗联动尚未完整建模。"
        };
        if (state.Character != "Ironclad" || state.PlayerCount < 1 || state.MaxHp <= 0)
            return new(Version, null, options.Select(o => Unsupported(o, "仅支持单人铁甲战士有效局内状态。")).ToArray(), ["当前状态不在支持范围内。"]);

        var unknownDeck = state.Deck.Where(c => CardCatalog.Find(c) == null).Select(c => c.Name).Distinct().ToArray();
        if (unknownDeck.Length > 0)
            warnings.Add("牌组有未适配卡牌，能力评估不完整：" + string.Join("、", unknownDeck));
        if (state.Deck.Any(c => c.Modified)) warnings.Add("附魔/异变卡已保留基础评价，额外效果尚未全部计分。");
        if (state.Hidden?.Error != null) warnings.Add("隐藏信息读取受限：" + state.Hidden.Error);

        if(state.IsMultiplayer)warnings.Add(MultiplayerAdvice.Notice);
        var ignored=options.Where(MultiplayerAdvice.Ignore).Select(o=>o.Key).ToHashSet();
        var scores = options.Select(o => ignored.Contains(o.Key)?Unsupported(o,MultiplayerAdvice.Ignored):Score(state, o)).ToArray();
        var enabled = scores.Where(s => s.Enabled&&!ignored.Contains(s.Key)).ToArray();
        string? recommendation = null;
        if (enabled.Length > 0 && enabled.All(s => s.Supported && s.Score.HasValue))
            recommendation = enabled.OrderByDescending(s => s.Score).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key;
        else
            warnings.Add("存在未适配选项，暂不作整体推荐；已适配选项的分数仅供参考。");
        return new(Version, recommendation, scores, warnings.ToArray());
    }

    private OptionScore Score(RunSnapshot s, DecisionOption o)
    {
        if (!o.Enabled) return new(o.Key, o.Name, true, false, null, [], ["当前不可选。"]);
        if(o.Kind=="effect"&&o.EffectValue is {} effect)return Result(o,[new("已核对的遗物效果估值",effect)],"人工机制估值，未完成实战校准。");
        if (o.Kind == "skip") return Result(o, [new("保持牌组，作为比较基准", 0)]);
        if (o.Kind == "card" && o.Card != null) return ScoreCard(s, o, o.Card);
        if (o.Kind == "heal")
        {
            var heal = Math.Clamp(o.HealAmount, 0, Math.Max(0, s.MaxHp - s.Hp));
            var ratio = Math.Clamp((double)s.Hp / s.MaxHp, 0, 1);
            var value = heal / s.MaxHp * w.HealValue * CardMechanics.RestHealingMultiplier(ratio);
            var history=CardMechanics.RecentCombatHealingMultiplier(s);
            return Result(o, [new($"实际可回复 {heal:0.#} 点生命", value),
                new("休息附带奖励与代价（不含重复回血）",o.EffectValue??0),
                new($"低血量紧迫性（当前 {ratio:P0}）", value * w.HealUrgency * Math.Pow(1 - ratio, 2)),
                new("同幕最近三次精英/首领低承伤且未用药，小幅降低休息收益",value*(history-1)),
                new("当前营火行动之后的路线危险差额",-s.CampRouteRisk.GetValueOrDefault("heal"))],
                "近期总承伤仅作保守参考，不是下一首领战损预测；不使用回血抵消承伤。");
        }
        if (o.Kind == "smith")
        {
            if(ApotheosisPolicy.Finished(s))return Result(o,[new(ApotheosisPolicy.CampReason,ApotheosisPolicy.CoveredSmithScore)]);
            var best = s.Deck.Where(c => c.CanUpgrade && CardCatalog.Find(c) != null)
                .Select(c => (Card: c, Value: CardMechanics.Upgrade(s,c) * w.UpgradeValue * 1.2))
                .OrderByDescending(x => x.Value).Take(o.SmithCount).ToArray();
            if (best.Length == 0) return Unsupported(o, "没有已适配的可升级目标。");
            return Result(o, [new($"优先升级：{string.Join('、',best.Select(x=>x.Card.Name))}", best.Sum(x=>x.Value)),
                new("当前营火行动之后的路线危险差额",-s.CampRouteRisk.GetValueOrDefault("smith"))],
                "目标来自已适配卡牌的人工优先级；尚未模拟升级后的战损差异。");
        }
        return Unsupported(o, "该选项效果尚未适配。");
    }

    private OptionScore ScoreCard(RunSnapshot s, DecisionOption o, CardSnapshot c)
    {
        if(c.ClassName=="Apotheosis")return ApotheosisPolicy.Score(s,o,c);
        var p = CardCatalog.Find(c);
        if (p == null) return Unsupported(o, "该卡或其附魔/异变尚未适配，已记录完整可读数据。");
        var deck = s.Deck.Select(CardCatalog.Find).Where(p => p != null).Cast<CardProfile>().ToArray();
        var attackNeed = Math.Clamp((7 - deck.Sum(p => p.Attack)) / 5, 0, 1);
        var blockNeed = Math.Clamp((6 - deck.Sum(p => p.Block)) / 4, 0, 1);
        var scalingNeed = 1 / (1 + deck.Sum(p => p.Scaling));
        var energySupport = Math.Clamp((s.Energy - 3) * .5 + deck.Sum(p => p.Energy) * .3, 0, 1.5);
        var duplicateCount = CardMechanics.Copies(s,c);
        var load = CardMechanics.DrawLoad(c,p);
        var activeExhaust = deck.Sum(CardMechanics.ActiveExhaust);
        var exhaustPayoff = deck.Sum(x=>x.ExhaustPayoff);
        var strength = deck.Sum(x=>x.Strength);
        var multi = deck.Sum(x=>x.StrengthPayoff);
        var engine = p.Draw+p.Energy>=1;
        var duplicatePenalty = duplicateCount*w.DuplicatePenalty*(engine?.55:1)
            + .35*duplicateCount*duplicateCount;
        if(c.ClassName=="Dominate"&&IroncladSynergies.SupportedSecondDominate(s))duplicatePenalty*=.35;
        var hpRatio = Math.Clamp((double)s.Hp / s.MaxHp, 0, 1);
        var parts = new List<ScorePart>
        {
            new("卡牌基础先验", p.Base),
            new("补足前期输出", attackNeed * p.Attack * w.DamageNeed),
            new("补足防御", blockNeed * p.Block * w.BlockNeed),
            new("抽牌与运转", p.Draw * w.Draw * (1 + .25 * energySupport)),
            new("补充能量（过牌、费用需求与现有产能）", p.Energy * w.Energy * CardMechanics.EnergyDemand(s)),
            new("持续成长", p.Scaling * w.ScalingNeed * scalingNeed * (s.Act >= 2 ? 1.3 : 1)),
            new("消耗体系缺口（收益组件与主动烧牌分别递减）", w.Synergy * (p.ExhaustPayoff * Math.Min(2,activeExhaust)/(1+exhaustPayoff)
                + CardMechanics.ActiveExhaust(p)*Math.Min(1.5,exhaustPayoff)/(1+activeExhaust))),
            new("力量与多段的边际联动（同职能已足够时递减）", w.Synergy * (p.StrengthPayoff * Math.Min(1.5,strength)/(1+multi/2)
                + p.Strength * Math.Min(1.5,multi)/(1+strength))),
            new("已有升级", c.UpgradeLevel > 0 ? 1.5 : 0),
            new("净抽牌占位（已扣除可兑现的自替换）", -w.CardTax*load),
            new("重复卡边际收益递减", -duplicatePenalty),
            new("有效牌组膨胀", -Math.Max(0, s.Deck.Sum(d=>CardCatalog.Find(d) is {} dp?CardMechanics.DrawLoad(d,dp):1) - 15) * w.DeckSizePenalty*load),
            new("高费启动负担", -Math.Max(0, c.Cost - 1) * w.ExpensivePenalty * Math.Max(0, 1 - .5 * energySupport)),
            new("自伤代价（低血量保留强惩罚）", -p.SelfDamage * w.HpRisk * (.65 + 4 * Math.Pow(1 - hpRatio, 2)))
        };
        parts.AddRange(ExtraCardModels.Context(s,c));
        if(c.ClassName is "PommelStrike" or "ShrugItOff")
        {
            var paidCyclers=s.Deck.Count(d=>d.ClassName is "PommelStrike" or "ShrugItOff");
            var netDraw=p.Draw*2-1;
            parts.Add(new("一费过渡过牌重复占用出牌预算",-Math.Max(0,paidCyclers-1)*(netDraw>0?.35:.8)));
        }
        if(c.ClassName is "TrueGrit" or "BurningPact" or "Stoke" or "SecondWind" or "FiendFire" or "Brand" or "Purity" or "Cinder")
        {
            var nuisance=s.Deck.Sum(d=>d.Type is "Curse" or "Status"?
                (d.Keywords.Contains("Ethereal")?.3:1.2):d.ClassName is "StrikeIronclad" or "DefendIronclad"?.18:0);
            var targeted=c.ClassName is "BurningPact" or "Brand" or "Purity"||c.ClassName=="TrueGrit"&&c.UpgradeLevel>0;
            var cleanup=Math.Min(5,.8+nuisance)*(targeted?1:.65)/(1+activeExhaust);
            parts.Add(new("主动清理诅咒、状态和过渡牌（不要求消耗流）",cleanup));
        }
        if(c.ClassName=="SecondWind")
        {
            var nonAttack=s.Deck.Count(d=>d.Type!="Attack")/(double)Math.Max(1,s.Deck.Length);
            parts.Add(new("按非攻击手牌估计的多次格挡",Math.Clamp(nonAttack*4-1,0,2)*(c.UpgradeLevel>0?2.4:1.8)));
        }
        if (c.ClassName == "BattleTrance" && duplicateCount>0)
            parts.Add(new("同回合禁抽使多张战斗专注互相冲突",-duplicateCount*5));
        if(c.ClassName=="FeelNoPain")
        {
            // Defensive exhaust payoff is not interchangeable with draw/energy payoff.
            var existing=s.Deck.Count(d=>d.ClassName=="FeelNoPain");
            var armor=s.Deck.Any(d=>d.ClassName is "Barricade" or "BodySlam" or "Juggernaut");
            var mass=s.Deck.Any(d=>d.ClassName is "SecondWind" or "Stoke" or "FiendFire" or "Corruption");
            parts.Add(new("消耗转格挡补齐防御，联动存盾／转伤（按实际消耗手段）",
                Math.Min(2,activeExhaust)*(1+(armor?.75:0)+(mass?.5:0))/(1+existing*2)));
        }
        if(c.ClassName=="Shockwave")
        {
            var weaken=s.Deck.Count(d=>d.ClassName is "Shockwave" or "Uppercut" or "Disarm");
            parts.Add(new("多回合群体虚弱与易伤覆盖，已有减伤来源时递减",3/(1+weaken)));
        }
        if (c.ClassName == "DemonForm") parts.Add(new("多张高费慢启动能力的拥堵",-duplicateCount*3));
        if (c.ClassName == "ThinkingAhead") parts.Add(new("整理手牌与下一抽；不是净增加两张手牌",1));
        if (c.ClassName == "Cascade") parts.Add(new("牌库自动出牌带来的费用利用（存在目标和顺序风险）",Math.Min(3,s.Deck.Count(d=>d.Cost>=2)*.65)-1));
        if (c.ClassName == "Stampede") parts.Add(new("高费攻击的自动打出支持（非固定产能）",Math.Min(5,s.Deck.Count(d=>d.Type=="Attack"&&d.Cost>=2)*1.8)-1.5));
        if (c.ClassName == "PactsEnd") parts.Add(new("必须先消耗三张的启动限制",activeExhaust<1?-5:activeExhaust<2?-2:1));
        if (c.ClassName == "Thrash") parts.Add(new("吞噬攻击成长与随机丢失攻击风险",Math.Clamp(s.Deck.Where(d=>d.Type=="Attack").Select(d=>CardRegistry.Var(d,"Damage")).DefaultIfEmpty().Average()/6-2,-2,3)));
        if (c.ClassName == "Rupture") parts.Add(new("实际自伤来源支撑",Math.Min(4,deck.Sum(x=>x.SelfDamage)*3)-2));
        if (p.SelfDamage>0 && s.Deck.Any(d=>d.ClassName=="Rupture"))parts.Add(new("自伤触发成长（不抵消低血量风险）",Math.Min(2,p.SelfDamage*2)));
        if(c.ClassName=="IronWave")
            parts.Add(new("铁斩波攻防同时兑现折扣（并非每次打出都需要格挡；暂按半份次要收益）",
                -.5*Math.Min(attackNeed*p.Attack*w.DamageNeed,blockNeed*p.Block*w.BlockNeed)));
        if (c.ClassName == "BodySlam")
            parts.Add(new("格挡产出支撑", Math.Clamp(deck.Sum(p => p.Block) - 3, -2, 3) * 2));
        if (c.ClassName == "Barricade")
            parts.Add(new("格挡产出支撑", Math.Clamp(deck.Sum(p => p.Block) - 4, -2, 3) * 2));
        if(c.ClassName=="Entrench")parts.Add(new("格挡翻倍及保留支撑",Math.Clamp(deck.Sum(p=>p.Block)-4,-3,3)*2+(s.Deck.Any(d=>d.ClassName=="Barricade")?3:0)));
        if(c.ClassName=="Unmovable")parts.Add(new("稳定格挡来源与保留引擎",Math.Clamp(deck.Sum(p=>p.Block)-3,-2,3)*1.5+(s.Deck.Any(d=>d.ClassName=="Barricade")?2:0)));
        if(c.ClassName=="Juggernaut")parts.Add(new("可兑现的重复格挡触发（基础防御、费用与一次性来源折扣）",
            Math.Clamp(IroncladSynergies.BlockTriggers(s)*3-3,-3,10)));
        if(c.ClassName=="Inflame")
        {
            var phase=s.Act<=1?0:s.Act==2?.5:1.2;
            var betterGrowth=s.Deck.Any(d=>d.ClassName=="DemonForm")||
                s.Deck.Any(d=>d.ClassName=="Dominate")&&IroncladSynergies.VulnerableCoverage(s,true)>=.5;
            parts.Add(new("中后期固定力量的边际需求与重复启动负担",
                -phase*(betterGrowth?1.5:1)-duplicateCount*(s.Act>=2?3:1.5)));
        }
        if(c.ClassName=="Corruption")parts.Add(new("技能费用节省与提前耗尽的权衡",Math.Min(5,s.Deck.Where(d=>d.Type=="Skill").Sum(d=>Math.Max(0,d.Cost))*.4)-1));
        if(c.ClassName=="BodySlam"&&s.Deck.Any(d=>d.ClassName is "Barricade" or "Unmovable" or "Entrench"))parts.Add(new("格挡转伤引擎",3/(1d+duplicateCount)));
        if(c.ClassName=="Barricade"&&s.Deck.Any(d=>d.ClassName is "BodySlam" or "Unmovable" or "Entrench"))parts.Add(new("保留格挡引擎",3/(1d+duplicateCount)));
        if(c.ClassName=="Clash")parts.Add(new("非攻击牌阻塞",-Math.Min(5,s.Deck.Count(d=>d.Type!="Attack")*.25)));
        if (c.ClassName == "TrueGrit" && c.UpgradeLevel == 0)
            parts.Add(new("随机消耗不稳定性", -1.5));
        if (c.ClassName == "Stoke")
        {
            // Random replacement is not deck cycling. Estimate only the chance of replacing weak cards.
            var weak = s.Deck.Count(d => d.ClassName is "StrikeIronclad" or "DefendIronclad" || d.Type is "Curse" or "Status");
            parts.Add(new("将低效手牌换为随机牌的机会（粗略先验）", 3.0 * weak / Math.Max(1, s.Deck.Length)));
            parts.Add(new("整手随机替换的不稳定性", -1.5));
            var phase=s.Act<=1?1:s.Act==2?.75:.15;
            var weakShare=weak/(double)Math.Max(1,s.Deck.Length);
            var unfinished=Math.Clamp(weakShare/.5,0,1);
            var copies=1+duplicateCount;
            parts.Add(new("前中期未成型牌组的随机替换机会（按弱牌占比，额外楼层不缩短阶段）",
                phase*(2+2*weakShare)*unfinished/copies));
            parts.Add(new("已适配求解器的添柴操作支持（反馈先验，非保证找到好牌）",
                s.CombatSolverAvailable?1.5*unfinished/copies:0));
            var support=s.Deck.Count(d=>d.ClassName is "DarkEmbrace" or "DrumOfBattle" or "HowlFromBeyond" or "AshenStrike");
            parts.Add(new("添柴整手消耗的具体触发支持",Math.Min(3,support*.8)/copies));
        }
        if(s.Deck.Any(d=>d.ClassName=="Stoke")&&c.ClassName is "DarkEmbrace" or "DrumOfBattle" or "HowlFromBeyond" or "AshenStrike")
        {
            var support=c.ClassName switch{"DarkEmbrace"=>2d,"DrumOfBattle"=>c.UpgradeLevel>0?2.5:2,
                "HowlFromBeyond"=>2.5,_=>1.5};
            parts.Add(new("已有添柴：抽牌／被消耗回能／进入消耗堆／消耗张数的额外联动",support/(1+duplicateCount)));
        }
        var vulnerableSources = s.Deck.Count(d => d.ClassName is "Bash" or "Thunderclap" or "Tremble" or "Uppercut" or "Shockwave" or "Taunt");
        if (c.ClassName == "Dismantle") parts.Add(new("易伤支持额外攻击", Math.Min(2, vulnerableSources) * 1.5));
        if (c.ClassName == "Tremble")
        {
            var coverage=IroncladSynergies.VulnerableCoverage(s);
            parts.Add(new("低费多回合易伤覆盖（不把痛击当作充分启动）",3.5/(1+coverage*.7)));
            parts.Add(new("易伤启动现有主宰／延长／减伤等组件",Math.Min(3.5,IroncladSynergies.VulnerablePayoffs(s))/(1+duplicateCount)));
        }
        if(CopyEngine.Active(s)||CopyEngine.SelfCopies(c)&&CopyEngine.Swift(c))
            parts.AddRange(CopyEngine.Adjustments(s,c));
        return Result(o, parts, p.Note);
    }

    private static OptionScore Result(DecisionOption o, IEnumerable<ScorePart> parts, params string[] notes)
    {
        var values = parts.Where(p => Math.Abs(p.Value) > .00001 || o.Kind == "skip").ToArray();
        return new(o.Key, o.Name, true, o.Enabled, Math.Round(values.Sum(p => p.Value), 3), values,
            notes.Where(n => n.Length > 0).ToArray());
    }

    private static OptionScore Unsupported(DecisionOption o, string note) =>
        new(o.Key, o.Name, false, o.Enabled, null, [], [note]);
}
