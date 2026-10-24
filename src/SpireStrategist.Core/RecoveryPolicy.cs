namespace SpireStrategist.Core;

public sealed record RecoveryEvaluation(string Action, string CardName, double? MarginalScore, string[] Reasons);

// The snapshot excludes the stolen card. Leaving it behind costs no gold.
// Capacity guards prevent a negative reward score from deleting necessary starter cards.
public static class RecoveryPolicy
{
    public static RecoveryEvaluation Evaluate(RunSnapshot state, CardSnapshot card,bool continuous=false,bool skipAllowed=true)
    {
        RecoveryEvaluation Result(string action, double? score, params string[] reasons)
        {
            if(continuous&&state.Character=="Ironclad"&&state.PlayerCount==1&&state.MaxHp>0)
            {
                if(action=="manual")return new("recover",card.Name,score,
                    ["全局接管：无法可靠判断永久舍弃收益，保守取回原有牌并继续；不代表该牌已完成建模。",..reasons]);
                if(action=="leave"&&!skipAllowed)return new("recover",card.Name,score,
                    ["全局接管：此界面禁止跳过，取回原有牌以完成奖励结算。",..reasons]);
            }
            return new(action,card.Name,score,reasons);
        }
        var p = CardCatalog.Find(card);
        if (state.Character != "Ironclad" || state.PlayerCount != 1 || state.MaxHp <= 0 || p == null ||
            CardCatalog.HasUnmodelledModification(card) ||
            state.Deck.Any(c => CardCatalog.HasUnmodelledModification(c) || CardCatalog.Find(c) == null))
            return Result("manual", null, "存在未适配或附魔/异变效果，暂不自动决定永久舍弃。");
        var profiles = state.Deck.Select(c => CardCatalog.Find(c)!).ToArray();
        var marginal = new Policy().Evaluate(state, [new("recover", card.Name, "card", Card: card), new("leave", "不取回", "skip")]).Options[0].Score;
        if (marginal == null) return Result("manual", null, "无法计算取回后的边际价值。");
        if (state.Deck.Length < 8)
            return Result("recover", marginal, "剩余牌组较薄，当前模型不能确认继续减牌是否安全。");
        if (p.Attack > 0 && profiles.Sum(x => x.Attack) < 3.5)
            return Result("recover", marginal, "剩余输出尚不足，过渡攻击仍承担必要输出。");
        if (p.Block > 0 && profiles.Sum(x => x.Block) < 3)
            return Result("recover", marginal, "剩余防御尚不足，保留当前格挡来源。");
        var duplicate = state.Deck.Any(c => c.Id == card.Id);
        if (!duplicate && (p.Scaling > 0 || p.Draw > 0 || p.Energy > 0 || p.ExhaustPayoff > 0 ||
            p.ExhaustSource >= .5 && profiles.Sum(x => x.ExhaustPayoff) > 0))
            return Result("recover", marginal, "这是独有的成长、运转或消耗组件，保留结构性作用。");
        if (marginal >= -2)
            return Result("recover", marginal, "仍有收益或接近临界值，保守取回；尚不足以确认免费删牌更好。");
        if (!CardCatalog.Profiles.ContainsKey(card.ClassName)&&ExtraCardModels.Find(card)==null)
            return Result("manual", marginal, "该牌只有通用近似评分，特殊机制尚未充分建模，不能据此永久舍弃。");
        return Result("leave", marginal, "现有牌组已覆盖该牌主要作用，取回的边际评分明显为负。", "不取回等同免费删去该牌；这是启发式判断，不是胜率预测。");
    }
}
