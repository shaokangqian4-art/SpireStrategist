using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckAdvice0646(RunState state,AdvisorService service)
    {
        var p=service.Player;
        var rendered=System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_RENDER_UI")=="1";
        NMapScreen.Instance!.Close(false);
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);
        await Until(()=>service.Decision is {Kind:"event"},"ancient advice");
        await Shot0620("0646-ancient");
        if(rendered)Check(service.Visuals.VisibleItemKey==service.Decision!.Evaluation.RecommendedKey,"ancient recommended option painted");
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance!.Close(false);
        foreach(var c in p.Deck.Cards.ToArray())await CardPileCmd.RemoveFromDeck(c,false);
        var holo=state.CreateCard<Hologram>(p);
        var defend=state.CreateCard<DefendDefect>(p);
        foreach(var c in new CardModel[]{holo,defend,state.CreateCard<Glacier>(p),state.CreateCard<Coolheaded>(p)})await CardPileCmd.Add(c,PileType.Deck);
        var upgrade=NDeckUpgradeSelectScreen.ShowScreen(p.Deck.Cards.ToArray(),new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt,1),state);
        var completion=upgrade.CardsSelected();
        await Until(()=>service.Selection?.Screen==upgrade,"manual smith target recommendation");
        var before=RewardForesightReader.Fingerprint(state,p);
        GameActions.Invoke(service,"Poll");
        Check(service.Selection!.Targets.Length==1&&service.Selection.Targets[0]!=defend,"smith advice selects nonbasic target");
        await Shot0620("0646-upgrade");
        if(rendered)Check(service.Visuals.VisibleItemKey?.StartsWith("selection:")==true,"smith recommendation painted");
        Check(!completion.IsCompleted&&before==RewardForesightReader.Fingerprint(state,p),"repeated advice does not select or consume RNG");
        if(rendered)await ClickUi0620(GameActions.Descendants<NCardHolder>(upgrade).First(h=>h.CardModel==defend).Hitbox);
        else GameActions.SelectUpgrade(upgrade,defend);
        await Delay(.4);GameActions.ConfirmUpgrade(upgrade);
        await Until(()=>completion.IsCompleted,"manual selection completed");
        Check((await completion).Single()==defend,"manual player may choose a different target");
        var enchant=NDeckEnchantSelectScreen.ShowScreen(p.Deck.Cards.ToArray(),ModelDb.Enchantment<Adroit>(),3,
            new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt,2){RequireManualConfirmation=true});
        await Until(()=>service.Selection?.Screen==enchant,"multi-target enchant advice");
        Check(service.Selection!.Targets.Length==2,"enchant advice respects required target count");
        await Shot0620("0646-enchant");
        if(rendered)Check(service.Visuals.VisibleItemKey?.StartsWith("selection:")==true,"enchant targets painted");
        // Closing this isolated preview does not apply an enchantment.
        GameActions.Invoke(enchant,"CloseSelection",GameActions.Field(enchant,"_closeButton"));
        await Delay(.5);
        for(var i=0;i<3;i++)
        {
            await CardPileCmd.Add(state.CreateCard<StrikeDefect>(p),PileType.Deck);
            await CardPileCmd.Add(state.CreateCard<DefendDefect>(p),PileType.Deck);
        }
        var removal=CardSelectCmd.FromDeckForRemoval(p,new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt,1));
        await Until(()=>service.Selection?.Screen is NDeckCardSelectScreen,"manual removal advice");
        Check(service.Selection!.Targets.Single()==defend,"manual removal recognizes native localized purpose");
        await Shot0620("0646-remove");
        var removalScreen=service.Selection.Screen;
        GameActions.Invoke(removalScreen,"OnCardClicked",defend);
        GameActions.Invoke(removalScreen,"ConfirmSelection",GameActions.Field(removalScreen,"_previewConfirmButton"));
        await Until(()=>removal.IsCompleted,"manual remove selection completed");
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);p.Gold=600;
        NMerchantRoom.Instance!.OpenInventory();await Until(()=>Screen() is NMerchantInventory,"native shop");
        await Delay(2);await Shot0620("0646-shop");
        var text=((RichTextLabel)GameActions.Field(service.Panel,"body")!).Text;
        Check(text.Contains("遗物")||text.Contains("Relic")||text.Contains("relic"),"shop includes relic advice");
        Check(text.Contains("药水")||text.Contains("Potion")||text.Contains("potion"),"shop includes potion advice");
        Check(!service.Autopilot.Enabled&&p.Gold==600,"all recommendation views preserve manual control");
    }
}
