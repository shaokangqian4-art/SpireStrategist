namespace SpireStrategist.Core;

// Presentation is deliberately independent of the executor's affordability filter.
public static class ShopReview
{
    public static ShopEvaluation IncludeCards(RunSnapshot state,ShopCard[] stock,ShopEvaluation plan)
    {
        var choices=plan.Choices.Select(c=>c with{Affordable=c.Price<=state.Gold}).ToList();
        foreach(var item in stock.Where(c=>c.Price>=0))
        {
            var quote=new Policy().Evaluate(state with{Gold=Math.Max(0,state.Gold-item.Price)},[new("review",item.Card.Name,"card",Card:item.Card)]).Options[0];
            var index=choices.FindIndex(c=>c.Key==item.Key);var affordable=item.Price<=state.Gold;
            var explanation=new[]{"卡牌评分与当前购买能力分开；买不起仍显示。"}
                .Concat(quote.Parts.OrderByDescending(p=>Math.Abs(p.Value)).Take(4).Select(p=>$"{p.Value:+0.0;-0.0;0}  {p.Reason}"))
                .Concat(!affordable?["当前买不起；仅供构筑参考，不会自动购买。"]:Array.Empty<string>()).ToArray();
            var choice=index>=0?choices[index]:new ShopChoice(item.Key,item.Card.Name,"card",item.Price,quote.Score??0,
                quote.Score.HasValue?explanation:["此牌尚未建模或属于忽略的多人牌，不作推荐。"]){Eligible=false};
            choice=choice with{IntrinsicValue=quote.Score,Affordable=affordable,Modeled=quote.Score.HasValue};
            if(index>=0)choices[index]=choice;else choices.Add(choice);
        }
        return plan with{Choices=choices.ToArray(),Recommendation=plan.Recommendation is {} r?choices.FirstOrDefault(c=>c.Key==r.Key)??r:null};
    }
}
