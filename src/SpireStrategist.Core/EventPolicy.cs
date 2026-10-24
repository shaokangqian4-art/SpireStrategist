namespace SpireStrategist.Core;

public sealed record EventChoice(string Key,string Name,bool Enabled=true,bool Supported=true,
    bool Lethal=false,double HpLoss=0,double Heal=0,double MaxHp=0,double Gold=0,
    int Remove=0,int Upgrade=0,int RandomUpgrade=0,int Transform=0,int Potions=0,
    double Benefit=0,double Burden=0,string Selection="",string[]? Notes=null,
    double Preference=0,string PreferenceReason="场景策略调整");

public static class EventPolicy
{
    public const string Version="events-reviewed-0.2-forecasts";
    public static Evaluation Evaluate(RunSnapshot state,IReadOnlyList<EventChoice> choices)
    {
        var v=new StrategicValue(state);
        var scores=choices.Select(c=>
        {
            if(!c.Enabled)return new OptionScore(c.Key,c.Name,true,false,null,[],[]);
            if(!c.Supported)return new OptionScore(c.Key,c.Name,false,true,null,[],c.Notes??["未适配的事件机制"]);
            if(c.Gold< -state.Gold)return new OptionScore(c.Key,c.Name,true,false,null,[],["金币不足，不能自动执行。"]);
            if(c.Lethal||Math.Min(state.Hp+Math.Max(0,c.MaxHp),state.MaxHp+c.MaxHp)-c.HpLoss<=0||state.MaxHp+c.MaxHp<=0)
                return new OptionScore(c.Key,c.Name,true,true,-10000,[new("直接致死：拒绝自动选择",-10000)],c.Notes??[]);
            ScorePart[] parts=[new("生命代价",-v.HpCost(c.HpLoss)),new("实际可回复生命",v.Heal(c.Heal)),
                new("最大生命与剩余路线",v.MaxHp(c.MaxHp)),new("金币与当前预算",v.Gold(c.Gold)),
                new("删牌边际收益",v.RemoveBest(c.Remove)),new("自选升级",v.UpgradeBest(c.Upgrade)),
                new("随机升级期望（非确定预测）",v.RandomUpgrade(c.RandomUpgrade)),new("变化低效牌的先验",v.TransformBest(c.Transform)),
                new("空药水栏收益",v.Potion(c.Potions)),new("已核对机制收益（待校准）",c.Benefit),new("负担及不确定性",-c.Burden),
                new(c.PreferenceReason,c.Preference)];
            return new OptionScore(c.Key,c.Name,true,true,Math.Round(parts.Sum(p=>p.Value),3),parts.Where(p=>p.Value!=0).ToArray(),c.Notes??[]);
        }).ToArray();
        var enabled=scores.Where(s=>s.Enabled).ToArray();
        var best=enabled.All(s=>s.Supported)?enabled.Where(s=>s.Score> -9999).OrderByDescending(s=>s.Score).ThenBy(s=>s.Key,StringComparer.Ordinal).FirstOrDefault()?.Key:null;
        return new(Version,best,scores,["点评作为条件假设；分数为人工先验，尚未经过跨种子胜率校准。","只将已核验的副本预测称为确定结果；未预测的随机收益按期望和风险计分。"]);
    }
}
