namespace SpireStrategist.Core;

// Copies enter discard, not the hand. Swift triggers once PER INSTANCE; a new
// self-copy occurs before Swift triggers, preserving the unused status in the child.
// Cloning an already-used instance does NOT reset Swift. These are bounded priors, not an
// assertion of an infinite combo or of safe execution against every enemy.
public static class CopyEngine
{
    public static bool SelfCopies(CardSnapshot c)=>c.ClassName is "AdaptiveStrike" or "Anger";
    public static bool Swift(CardSnapshot c)=>c.Enchantment=="ENCHANTMENT.SWIFT"&&c.EnchantmentAmount>0&&string.IsNullOrEmpty(c.Affliction);
    public static bool Active(RunSnapshot s)=>s.Deck.Any(c=>SelfCopies(c)&&Swift(c));
    public static bool Adaptive(RunSnapshot s)=>s.Deck.Any(c=>c.ClassName=="AdaptiveStrike");
    public static IEnumerable<ScorePart> Adjustments(RunSnapshot s,CardSnapshot c)
    {
        var adaptive=Adaptive(s);var active=Active(s);var name=c.ClassName;
        var recall=s.Deck.Any(d=>d.ClassName=="AllForOne");
        var copies=s.Deck.Count(d=>d.ClassName==name);
        if(name=="AllForOne"&&adaptive&&copies==0)
            yield return new("适应打击生成可回收零费复制品，需先付费启动",active?7:5);
        if(name=="AdaptiveStrike"&&copies==0&&recall)
            yield return new("已有万物一心可取回零费复制品",5);
        if(SelfCopies(c)&&Swift(c))
            yield return new("迅捷随新复制品继承，每个实例首次打出抽牌",Math.Min(8,c.EnchantmentAmount*2.5));
        if(!adaptive&&!active)yield break;
        if(name is "ColdSnap" or "BallLightning" or "MomentumStrike" or "FocusedStrike" or "RocketPunch" or "Claw" or "HelixDrill")
            yield return new("已有复制输出，额外攻击占用首轮与回收后的抽牌",active?-6:recall?-4:-3);
        if(name is "Scavenge" or "Purity")
            yield return new("主动消耗基础牌帮助复制体系瘦身，保留核心",copies==0?4:-1);
        if(name=="Hologram"&&copies==0)
            yield return new("定向找回复制品或万物一心",2.5);
        if(active)
        {
            if(!c.Keywords.Contains("Exhaust")&&name is not ("AdaptiveStrike" or "AllForOne" or "Hologram" or "Scavenge" or "Purity" or "Turbo" or "Compact"))
                yield return new("复制引擎成立后提高新增常驻牌的门槛",-2);
            if(name is "Iteration" or "Scrape" or "MachineLearning")
                yield return new("迅捷复制已提供抽牌，不额外扩展抽牌支线",-5);
            if(name is "Storm" or "Subroutine" or "Thunder" or "CreativeAi" or "Darkness" or "TrashToTreasure")
                yield return new("已成立的复制引擎不再投资无关成长支线",-5);
            if(name=="AdaptiveStrike"&&copies>0)
                yield return new("一张附魔复制核心已能自增，无需第二张原牌",-4);
        }
    }
}
