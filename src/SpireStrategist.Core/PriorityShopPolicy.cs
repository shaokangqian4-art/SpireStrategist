namespace SpireStrategist.Core;

public sealed record ShopRelic(string Key,string Id,string Name,int Price);
public sealed record PriorityPurchase(ShopRelic Relic,ShopCard? Card,string Reason)
{
    public int TotalPrice=>Relic.Price+(Card?.Price??0);
    public bool RelicFirst {get;init;}
    public ShopCard? FirstCard=>RelicFirst?null:Card;
}

// Explicit whitelist, before ordinary removal/cards. The 100-gold reserve is for
// later random relic spending, not a restriction on these requested priority buys.
public static class PriorityShopPolicy
{
    public static PriorityPurchase? Evaluate(RunSnapshot s,ShopRelic[] relics,ShopCard[] cards,int boughtCards=0,int? removalPrice=null,int relicReserve=100)
    {
        if(!DefectStrategy.Supported(s.Character)||s.PlayerCount!=1||s.MaxHp<=0)return null;
        var eligible=relics.Where(r=>r.Price>=0&&r.Price<=s.Gold&&!s.Relics.Contains(r.Id)&&!s.Relics.Contains("RELIC."+r.Id));
        // Buying the discount first changes every remaining price; caller re-reads stock.
        foreach(var id in new[]{"MEMBERSHIP_CARD","GHOST_SEED","MINIATURE_TENT","ICE_CREAM","CHEMICAL_X"})
        {
            if((id is "MEMBERSHIP_CARD" or "MINIATURE_TENT")&&s.Act>2)continue;
            foreach(var relic in eligible.Where(r=>r.Id.Replace("RELIC.","")==id).OrderBy(r=>r.Price).ThenBy(r=>r.Key,StringComparer.Ordinal))
            {
                if(id=="GHOST_SEED")
                {
                    var basics=s.Deck.Count(c=>(c.ClassName is "StrikeIronclad" or "DefendIronclad" or "StrikeDefect" or "DefendDefect" or "StrikeSilent" or "DefendSilent" or "StrikeRegent" or "DefendRegent" or "StrikeNecrobinder" or "DefendNecrobinder")&&!c.Keywords.Contains("Ethereal"));
                    if(basics<5||basics/(double)Math.Max(1,s.Deck.Length)<.3)continue;
                    return new(relic,null,$"尚有 {basics} 张未虚无打防，占牌组 {basics/(double)s.Deck.Length:P0}，优先幽灵种子；它只让未打出的打防在回合结束时消耗，不改善首轮抽牌，也不永久删牌。");
                }
                if(id!="CHEMICAL_X")return new(relic,null,id switch
                {
                    "MEMBERSHIP_CARD"=>"前两幕优先打折卡，购买后按实际折后价格重算商店。",
                    "MINIATURE_TENT"=>"前两幕优先帐篷，提高后续火堆多项行动收益。",
                    _=>"优先冰淇淋，保留跨回合能量。"
                });
                if(s.Deck.Any(c=>c.CostsX))return new(relic,null,"牌组已有 X 牌，优先化学物 X。");
                // A card must be worthwhile on its own, before granting Chemical X synergy.
                var cardPlan=ShopPolicy.Evaluate(s,cards.Where(c=>c.Card.CostsX).ToArray(),null,s.Gold,boughtCards,0);
                var choice=cardPlan.Choices.Where(c=>c.Kind=="card"&&c.Score>0&&c.Price+relic.Price<=s.Gold)
                    .OrderByDescending(c=>c.Score).FirstOrDefault();
                if(choice!=null)return new(relic,cards.Single(c=>c.Key==choice.Key),
                    "先确认 X 牌本身值得购买，且现款足够同时购买该牌与化学物 X；先买牌再买遗物。");
            }
        }
        // Order an already affordable pair, not "buy the book at any price".
        // It counts cards added AFTER acquisition: five cards heal twenty HP.
        // Never buy unwanted cards to chase a trigger or displace an exclusive core.
        var ordinary=ShopPolicy.Evaluate(s,cards,removalPrice,s.Gold,boughtCards);
        if(ordinary.Recommendation is {Kind:"card"} wanted)
        foreach(var book in eligible.Where(r=>r.Id.Split('.').Last()=="BOOK_OF_FIVE_RINGS").OrderBy(r=>r.Price))
        {
            var model=RelicValue.Shop(book.Id,s);
            if(book.Price+wanted.Price+relicReserve>s.Gold||model!=null&&model.Value-new StrategicValue(s).Gold(book.Price)<=0)continue;
            return new(book,cards.Single(c=>c.Key==wanted.Key),
                "五轮书先于已计划购买的卡牌：拿到遗物后每新增5张牌回复20生命，购牌计数从获得后开始；两者买完仍保留预算，成交后重算，不为回血额外抓牌。") {RelicFirst=true};
        }
        if(s.Character=="Defect")
        {
            // Evaluate concrete engine relics before spending on filler. Use the
            // same gold cost as cards/removal, and compare before committing cash.
            var alternative=ShopPolicy.Evaluate(s,cards,removalPrice,s.Gold,boughtCards).Recommendation?.Score??0;
            var candidate=eligible.Where(r=>DefectRelics.EnginePurchase(s,r.Id))
                .Select(r=>(Relic:r,Value:(RelicValue.Shop(r.Id,s)?.Value??0)-r.Price/40d*(s.Gold<150?1.5:1)))
                .Where(r=>r.Value>=6&&r.Value>alternative+2)
                .OrderByDescending(r=>r.Value).ThenBy(r=>r.Relic.Price).FirstOrDefault();
            if(candidate.Relic!=null)return new(candidate.Relic,null,"已有牌组与遗物形成明确核心联动，净收益超过当前买牌或删牌；购买后重算。");
        }
        return null;
    }
}
