using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal sealed record SelectionPlan(Control Screen, CardModel[] Targets, RunSnapshot State, Evaluation Evaluation);

// Read-only: never starts a selection, clicks a card, or changes the player's selection.
internal static class SelectionAdvice
{
    internal static SelectionPlan? Read(AdvisorService service,Control screen,string purpose)
    {
        if(screen is NChooseACardSelectionScreen choose)
        {
            var holders=GameActions.Descendants<MegaCrit.Sts2.Core.Nodes.Cards.Holders.NGridCardHolder>(choose).Where(h=>h.IsVisibleInTree()).ToArray();
            if(holders.Length==0||holders.Any(h=>h.CardModel.Owner!=service.Player))return null;
            var state=SnapshotReader.Read(service.State,service.Player);
            var choiceOptions=holders.Select((h,i)=>new DecisionOption(i.ToString(),h.CardModel.Title,"card",Card:SnapshotReader.ReadCard(h.CardModel))).ToList();
            if(GameActions.Field(choose,"_canSkip") is true)choiceOptions.Add(new("skip","跳过","skip"));
            var evaluation=new Policy().Evaluate(state,choiceOptions);
            var target=int.TryParse(evaluation.RecommendedKey,out var index)?new[]{holders[index].CardModel}:Array.Empty<CardModel>();
            return new(choose,target,state,evaluation);
        }
        if(screen is not (NDeckUpgradeSelectScreen or NDeckEnchantSelectScreen or NDeckTransformSelectScreen or NDeckCardSelectScreen or NSimpleCardSelectScreen))return null;
        if(GameActions.Field(screen,"_cards") is not IEnumerable<CardModel> source ||
            GameActions.Field(screen,"_prefs") is not CardSelectorPrefs prefs)return null;
        var cards=source.ToArray();
        if(cards.Any(c=>c.Owner!=service.Player))return null;
        var s=SnapshotReader.Read(service.State,service.Player);var v=new StrategicValue(s);
        if(screen is NDeckUpgradeSelectScreen)purpose="upgrade";
        else if(screen is NDeckEnchantSelectScreen)purpose="enchant";
        else if(screen is NDeckTransformSelectScreen)purpose="transform";
        else if(screen is NSimpleCardSelectScreen)purpose="add";
        else if(prefs.Prompt?.GetFormattedText()==new CardSelectorPrefs(new MegaCrit.Sts2.Core.Localization.LocString("relics","DOLLYS_MIRROR.selectionScreenPrompt"),prefs.MinSelect,prefs.MaxSelect).Prompt.GetFormattedText())purpose="duplicate";
        else if(string.IsNullOrEmpty(purpose)&&prefs.Prompt?.GetFormattedText()==
            new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt,prefs.MinSelect,prefs.MaxSelect).Prompt.GetFormattedText())purpose="remove";
        if(purpose is not ("upgrade" or "enchant" or "transform" or "add" or "remove" or "duplicate" or "pael-upgrade" or "neow-remove-one" or "neow-remove-pair"))return null;
        var enchant=GameActions.Field(screen,"_enchantment") as EnchantmentModel;
        var eventModel=service.State.CurrentRoom is MegaCrit.Sts2.Core.Rooms.EventRoom && MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom.Instance is {} eventRoom
            ? GameActions.Field(eventRoom,"_event") as EventModel:null;
        double Value(CardModel card)
        {
            var c=SnapshotReader.ReadCard(card);
            if(MultiplayerAdvice.Ignore(c))return double.NaN;
            if(purpose=="transform"&&prefs.MaxSelect==1&&s.PlayerCount==1&&TransformForecast.Supports(eventModel))
                return TransformForecast.Read(eventModel!,card,s).Value;
            return purpose switch{
                "upgrade" or "pael-upgrade"=>v.Upgrade(c),
                "enchant" when enchant!=null=>v.Enchant(c,enchant.Id.Entry,Convert.ToDouble(GameActions.Field(screen,"_enchantmentAmount"))),
                "transform"=>v.Transform(c),"add" or "duplicate"=>v.Add(c),
                "remove" or "neow-remove-one" or "neow-remove-pair"=>v.Remove(c),_=>double.NaN};
        }
        var scored=cards.Select((c,i)=>(Card:c,Index:i,Value:Value(c))).ToArray();
        var ranked=scored.Where(x=>double.IsFinite(x.Value)).OrderByDescending(x=>x.Value).ThenBy(x=>x.Index).ToArray();
        var count=Math.Clamp(ranked.Count(x=>x.Value>0),prefs.MinSelect,prefs.MaxSelect);
        if(ranked.Length<count)return null;
        var targets=ranked.Take(count).Select(x=>x.Card).ToArray();
        if(purpose=="pael-upgrade")
        {
            var plan=PaelsToothPolicy.Plan(s,cards.Select(SnapshotReader.ReadCard).ToArray());
            if(count!=5||plan.Indices.Length!=5)return null;
            targets=plan.Indices.Select(i=>cards[i]).ToArray();
        }
        else if(purpose is "neow-remove-one" or "neow-remove-pair")
            targets=NeowPolicy.RemovalPlan(s,cards.Select(SnapshotReader.ReadCard).ToArray(),count).Select(i=>cards[i]).ToArray();
        var label=purpose switch{"upgrade" or "pael-upgrade"=>"升级","enchant"=>"附魔","transform"=>"变化","duplicate"=>"复制","add"=>"获得",_=>"移除"};
        var options=scored.Select(x=>new OptionScore(x.Index.ToString(),x.Card.Title,double.IsFinite(x.Value),true,
            double.IsFinite(x.Value)?x.Value:null,[],
            purpose=="upgrade"&&s.Character=="Silent"&&SilentMechanics.UpgradeNote(s,SnapshotReader.ReadCard(x.Card)) is {} note?[label,note]:[label])).ToArray();
        var warnings=new[]{"推荐目标："+(targets.Length==0?"不选牌":string.Join("、",targets.Select(c=>c.Title))),"仅显示建议，不改变你的手动选择。"};
        return new(screen,targets,s,new(Policy.Version,targets.Length==0?null:Array.IndexOf(cards,targets[0]).ToString(),options,warnings));
    }
}
