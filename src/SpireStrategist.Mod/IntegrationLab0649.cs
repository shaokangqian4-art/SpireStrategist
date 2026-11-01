using SpireStrategist.Core;
using MegaCrit.Sts2.Core.Entities.Merchant;
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
    private static async Task CheckAdvice0649(RunState state,AdvisorService service)
    {
        var p=service.Player;
        var rendered=System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_RENDER_UI")=="1";
        NMapScreen.Instance!.Close(false);
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);
        await Until(()=>service.Decision is {Kind:"event"},"ancient advice");
        await Shot0620("0649-ancient");
        if(rendered)Check(service.Visuals.VisibleItemKey==service.Decision!.Evaluation.RecommendedKey,"ancient recommended option painted");
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance!.Close(false);
        foreach(var c in p.Deck.Cards.ToArray())await CardPileCmd.RemoveFromDeck(c,false);
        CardModel holo=p.Character.GetType().Name=="Defect"?state.CreateCard<Hologram>(p):state.CreateCard<Offering>(p);
        CardModel defend=p.Character.GetType().Name=="Defect"?state.CreateCard<DefendDefect>(p):state.CreateCard<DefendIronclad>(p);
        foreach(var c in new CardModel[]{holo,defend,p.Character.GetType().Name=="Defect"?state.CreateCard<Glacier>(p):state.CreateCard<ShrugItOff>(p),p.Character.GetType().Name=="Defect"?state.CreateCard<Coolheaded>(p):state.CreateCard<BattleTrance>(p)})await CardPileCmd.Add(c,PileType.Deck);
        var upgrade=NDeckUpgradeSelectScreen.ShowScreen(p.Deck.Cards.ToArray(),new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt,1),state);
        var completion=upgrade.CardsSelected();
        await Until(()=>service.Selection?.Screen==upgrade,"manual smith target recommendation");
        var before=RewardForesightReader.Fingerprint(state,p);
        GameActions.Invoke(service,"Poll");
        Check(service.Selection!.Targets.Length==1&&service.Selection.Targets[0]!=defend,"smith advice selects nonbasic target");
        await Shot0620("0649-upgrade");
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
        await Shot0620("0649-enchant");
        if(rendered)Check(service.Visuals.VisibleItemKey?.StartsWith("selection:")==true,"enchant targets painted");
        // Closing this isolated preview does not apply an enchantment.
        GameActions.Invoke(enchant,"CloseSelection",GameActions.Field(enchant,"_closeButton"));
        await Delay(.5);
        for(var i=0;i<3;i++)
        {
            await CardPileCmd.Add(p.Character.GetType().Name=="Defect"?state.CreateCard<StrikeDefect>(p):state.CreateCard<StrikeIronclad>(p),PileType.Deck);
            await CardPileCmd.Add(p.Character.GetType().Name=="Defect"?state.CreateCard<DefendDefect>(p):state.CreateCard<DefendIronclad>(p),PileType.Deck);
        }
        var removal=CardSelectCmd.FromDeckForRemoval(p,new CardSelectorPrefs(CardSelectorPrefs.RemoveSelectionPrompt,1));
        await Until(()=>service.Selection?.Screen is NDeckCardSelectScreen,"manual removal advice");
        Check(service.Selection!.Targets.Length==1,"manual removal recognizes native localized purpose");
        await Shot0620("0649-remove");
        var removalScreen=service.Selection.Screen;
        GameActions.Invoke(removalScreen,"OnCardClicked",defend);
        GameActions.Invoke(removalScreen,"ConfirmSelection",GameActions.Field(removalScreen,"_previewConfirmButton"));
        await Until(()=>removal.IsCompleted,"manual remove selection completed");
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);p.Gold=600;
        NMerchantRoom.Instance!.OpenInventory();await Until(()=>Screen() is NMerchantInventory,"native shop");
        await Delay(2);await Shot0620("0649-shop");
        if(rendered)Check(service.Visuals.VisibleItemKey!=null,"shop actual recommendation painted for "+p.Character.GetType().Name);
        var inventory=NMerchantRoom.Instance!.Inventory;
        var snapshot=SnapshotReader.Read(state,p);
        var active=typeof(AdvisorService).GetField("active",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
        active.SetValue(service,false); // Isolate rendering contract from the one-second policy refresh.
        foreach(var slot in inventory.GetAllSlots().Where(x=>x.Entry.IsStocked))
        {
            var kind=slot.Entry is MerchantCardRemovalEntry?"remove":slot.Entry is MerchantRelicEntry?"relic":slot.Entry is MerchantPotionEntry?"potion":"card";
            var choice=new ShopChoice(slot.GetInstanceId().ToString(),"paint contract",kind,slot.Entry.Cost,1,[]);
            GameActions.Invoke(service.Visuals,"Shop",new ShopEvaluation(choice,[choice],0,[]));
            await Delay(.1);
            if(rendered)Check(service.Visuals.VisibleItemKey==choice.Key,"native shop frame: "+kind+":"+choice.Key);
        }
        active.SetValue(service,true);
        GameActions.Invoke(service,"Poll");await Delay(.3);
        var text=((RichTextLabel)GameActions.Field(service.Panel,"body")!).Text;
        Check(text.Contains("遗物")||text.Contains("Relic")||text.Contains("relic"),"shop includes relic advice");
        Check(text.Contains("药水")||text.Contains("Potion")||text.Contains("potion"),"shop includes potion advice");
        Check(!service.Autopilot.Enabled&&p.Gold==600,"all recommendation views preserve manual control");
        var tabs=(TabContainer)GameActions.Field(service.Panel,"tabs")!;
        var toggles=GameActions.Descendants<CheckButton>(tabs.GetChild(3)).ToArray();
        toggles[0].ButtonPressed=false;await Delay(.2);
        if(rendered)Check(service.Visuals.VisibleItemKey==null,"item toggle hides shop mark");
        toggles[0].ButtonPressed=true;await Delay(.2);
        if(rendered)Check(service.Visuals.VisibleItemKey!=null,"item toggle restores shop mark");
        GameActions.Invoke(inventory,"Close");
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        await Until(()=>service.Decision?.Kind=="rest","camp recommendation");await Delay(.4);
        await Shot0620("0649-camp");
        if(rendered)Check(service.Visuals.VisibleItemKey==service.Decision!.Evaluation.RecommendedKey,"camp action recommended frame");
        var factory=typeof(ModelDb).GetMethods().Single(m=>m.Name=="Potion"&&m.IsGenericMethod&&m.GetParameters().Length==0);
        foreach(var t in typeof(PotionModel).Assembly.GetTypes().Where(t=>t.Namespace=="MegaCrit.Sts2.Core.Models.Potions"&&!t.IsAbstract&&t.IsSubclassOf(typeof(PotionModel))))
        {
            var potion=(PotionModel)factory.MakeGenericMethod(t).Invoke(null,null)!;
            Check(PotionPolicy.Evaluate(snapshot,ShopAdvice.Describe(potion))!=null,"native potion modeled: "+t.Name);
        }
    }
}
