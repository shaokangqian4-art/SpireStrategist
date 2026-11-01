namespace SpireStrategist.Core;

public static class MultiplayerAdvice
{
    public const string Notice="多人建议模式：仅评估本人的已建模内容；不接管，不评估队友配合，多人专属牌忽略评分。";
    public const string Ignored="多人专属牌未建模，忽略评分；请自行判断。";
    public static bool Ignore(CardSnapshot c)=>c.MultiplayerOnly||ExtraCardModels.MultiplayerOnly.Contains(c.ClassName)||DefectPolicy.MultiplayerCard(c.ClassName)||SilentPolicy.MultiplayerCard(c.ClassName)||HeroPolicy.MultiplayerCard(c.ClassName);
    public static bool Ignore(DecisionOption o)=>o.Card is {} c&&Ignore(c);
}
