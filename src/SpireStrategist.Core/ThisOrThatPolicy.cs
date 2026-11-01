namespace SpireStrategist.Core;

public static class ThisOrThatPolicy
{
    // Explicit user preference, not an estimated relic value or learned win probability.
    // A bag already owned is not the exception: this must be the predicted reward.
    public static EventChoice Apply(EventChoice choice,string? predictedRelic=null,bool predictedRare=false)
    {
        var ornate=choice.Key.Split('.').Last()=="ORNATE";
        var bag=predictedRelic?.Replace("RELIC.","")=="BAG_OF_PREPARATION";
        return choice with {Preference=ornate==(bag||predictedRare)?1000:0,
            PreferenceReason="用户指定：默认卖血拿钱，预测得到准备背包或稀有遗物时选遗物；保留直接致死保护"};
    }
}
