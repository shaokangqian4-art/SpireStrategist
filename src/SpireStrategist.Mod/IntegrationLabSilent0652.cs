using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckSilent0652(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);
        await Delay(.5);
        var model=(EventModel)GameActions.Field(NEventRoom.Instance!,"_event")!;
        foreach(var c in p.Deck.Cards.ToArray())await CardPileCmd.RemoveFromDeck(c,false);
        var trap=state.CreateCard<KnifeTrap>(p);var hidden=state.CreateCard<HiddenDaggers>(p);
        var exterminate=state.CreateCard<Exterminate>(p);var whistle=state.CreateCard<Whistle>(p);
        foreach(var c in new CardModel[]{trap,hidden,exterminate,whistle,state.CreateCard<LeadingStrike>(p),state.CreateCard<BladeOfInk>(p),state.CreateCard<DefendSilent>(p)})
            await CardPileCmd.Add(c,PileType.Deck);
        var girya=(Girya)ModelDb.Relic<Girya>().ToMutable();await RelicCmd.Obtain(girya,p);
        Check(SnapshotReader.Read(state,p).GiryaLifts==0,"untrained native Girya counter is zero");
        girya.TimesLifted=3;
        Check(SnapshotReader.Read(state,p).GiryaLifts==3,"completed native Girya training reaches policy snapshot");
        var before=SilentStrategy.Upgrade(SnapshotReader.Read(state,p),SnapshotReader.ReadCard(hidden));
        CardCmd.Upgrade(trap);
        Check(SilentStrategy.Upgrade(SnapshotReader.Read(state,p),SnapshotReader.ReadCard(hidden))<before*.5,"native KnifeTrap upgrade reduces only overlapping knife quality upgrade");
        foreach(var enchantment in new EnchantmentModel[]{ModelDb.Enchantment<Sharp>(),ModelDb.Enchantment<Corrupted>()})
        {
            CardModel expected=enchantment is Sharp?exterminate:whistle;
            var amount=enchantment is Sharp?2:1;
            var screen=NDeckEnchantSelectScreen.ShowScreen(p.Deck.Cards.Where(enchantment.CanEnchant).ToArray(),enchantment,amount,
                new CardSelectorPrefs(CardSelectorPrefs.EnchantSelectionPrompt,1){RequireManualConfirmation=true});
            var completion=screen.CardsSelected();
            await Until(()=>service.Selection?.Screen==screen,"native enchant recommendation: "+enchantment.Id);
            Check(service.Selection!.Targets.Single()==expected,"native target score chooses "+expected.Id);
            var fingerprint=RewardForesightReader.Fingerprint(state,p);
            GameActions.Invoke(service,"Poll");
            Check(fingerprint==RewardForesightReader.Fingerprint(state,p)&&!completion.IsCompleted,"advice never mutates deck or RNG");
            AccessTools.Field(typeof(PartialAutopilot),"ownedEvent").SetValue(auto,model);
            AccessTools.Field(typeof(PartialAutopilot),"eventSelection").SetValue(auto,"enchant:"+enchantment.Id.Entry);
            auto.Enable(true);
            await Until(()=>completion.IsCompleted,"native enchant takeover confirms "+enchantment.Id,45);
            auto.Pause("0652 fixture boundary");
            Check((await completion).Single()==expected,"automatic target equals recommendation");
            if(enchantment is Sharp)CardCmd.Enchant<Sharp>(expected,amount);else CardCmd.Enchant<Corrupted>(expected,amount);
            Check(expected.Enchantment?.Id==enchantment.Id,"native card accepts selected enchantment");
        }
    }
}
