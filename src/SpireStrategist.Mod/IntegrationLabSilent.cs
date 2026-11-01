using Godot;
using HarmonyLib;
using System.Text.Json;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckMapTransition(RunState state,AdvisorService service)
    {
        var auto=service.Autopilot;var map=NMapScreen.Instance!;
        await CreatureCmd.SetCurrentHp(service.Player.Creature,service.Player.Creature.MaxHp);
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);
        var ancient=state.CurrentRoom;
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster,showTransition:false);
        await Until(()=>MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress,"combat begins after ancient");
        map.Close(false);
        var coord=state.CurrentMapCoord;
        var ownership=AccessTools.Field(typeof(PartialAutopilot),"pendingTravelFrom");
        ownership.SetValue(auto,ancient);map.Open(false);auto.Enable(true);auto.Tick();
        Check(!map.IsOpen&&state.CurrentMapCoord==coord,"owned leftover map closes without another map vote");
        auto.Pause("fixture boundary");
        ownership.SetValue(auto,ancient);map.Open(true);auto.Enable(true);auto.Tick();
        Check(map.IsOpen&&ownership.GetValue(auto)==null,"player top-bar map remains open and loses automation ownership");
        auto.Pause("fixture boundary");map.Close(false);
        ownership.SetValue(auto,ancient);map.Open(false);auto.Tick();
        Check(map.IsOpen,"disabled takeover never closes player's screen");
        map.Close(false);
    }
    private static async Task CheckSilent(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        Check(p.Character.GetType().Name=="Silent"&&DefectStrategy.Supported("Silent"),"native Silent support and default manual mode");
        var before=RewardForesightReader.Fingerprint(state,p);
        foreach(var kind in new[]{RoomType.Monster,RoomType.Elite,RoomType.Boss})
        {
            var predicted=RewardForesightReader.Clone(state);var real=RewardForesightReader.Clone(state);
            var pp=predicted.Players.Single();var rp=real.Players.Single();
            var cards=RewardForesightReader.Generate(predicted,pp,kind);
            var encounter=real.Act.PullNextEncounter(kind);
            var rewards=new RewardsSet(rp).WithRewardsFromRoom(new CombatRoom(encounter.ToMutable(),real));
            await rewards.GenerateWithoutOffering();
            var actual=rewards.Rewards.OfType<CardReward>().SelectMany(r=>r.Cards).Select(SnapshotReader.ReadCard).ToArray();
            Check(JsonSerializer.Serialize(cards,Journal.Json)==JsonSerializer.Serialize(actual,Journal.Json),"Silent forecast equals native reward: "+kind);
        }
        var shopCopy=RewardForesightReader.Clone(state);var shopOther=RewardForesightReader.Clone(state);
        var preview=RewardForesightReader.GenerateShop(shopCopy.Players.Single(),"shop",1,1);
        var nativeShop=MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory.CreateForNormalMerchant(shopOther.Players.Single());
        Check(preview.Cards.Select(c=>c.Id).SequenceEqual(nativeShop.CardEntries.Select(c=>c.CreationResult!.Card.Id.ToString())),"Silent shop forecast matches native stock");
        Check(before==RewardForesightReader.Fingerprint(state,p),"forecasts leave live RNG deck and inventory unchanged");
        await CardPileCmd.Add(state.CreateCard<BladeOfInk>(p),PileType.Deck);
        await CardPileCmd.Add(state.CreateCard<Accuracy>(p),PileType.Deck);
        await CardPileCmd.Add(state.CreateCard<Acrobatics>(p),PileType.Deck);
        await CreatureCmd.SetCurrentHp(p.Creature,p.Creature.MaxHp);
        var upgrades=p.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel);
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        await Until(()=>service.Decision?.Kind=="rest","Silent camp advice");
        if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_RENDER_UI")=="1")
            await Until(()=>service.Visuals.VisibleItemKey==service.Decision!.Evaluation.RecommendedKey,"Silent camp marker painted");
        auto.Enable(true);
        await Until(()=>Screen() is NMapScreen,"Silent smith and return to map",45);auto.Pause("fixture boundary");
        Check(p.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)>upgrades,"Silent camp upgrades useful cards");
        NMapScreen.Instance!.Close(false);
        var trap=state.CreateCard<KnifeTrap>(p);
        var reward=new CardReward(new CardModel[]{trap,state.CreateCard<Slice>(p),state.CreateCard<BubbleBubble>(p)},CardCreationSource.Encounter,p,CardCreationOptions.ForRoom(p,RoomType.Monster));
        var offer=new RewardsSet(p).WithCustomRewards([reward]).Offer();
        await Until(()=>Screen() is NRewardsScreen,"Silent card reward opens");
        var button=GameActions.Descendants<NRewardButton>((Node)Screen()!).First(b=>b.Reward is CardReward);
        var claim=GameActions.ClaimReward(button);
        await Until(()=>service.Decision?.Kind=="cards","Silent card advice visible");
        Check(service.Decision!.State.Foresight!=null,"Silent actual reward advisor attaches foresight status");
        if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_RENDER_UI")=="1")
            await Until(()=>service.Visuals.VisibleItemKey==service.Decision!.Evaluation.RecommendedKey,"Silent recommended card marker painted");
        await Shot0620("silent-card-recommendation");auto.Enable(true);
        await Until(()=>offer.IsCompleted,"Silent automatic reward acquisition",45);await offer;auto.Pause("fixture boundary");
        Check(p.Deck.Cards.Contains(trap),"Silent acquires enabled knife core rather than off-plan poison");
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);p.Gold=650;
        NMerchantRoom.Instance!.OpenInventory();await Until(()=>Screen() is NMerchantInventory,"Silent merchant opens");
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"Silent merchant finishes transactions and leaves",120);auto.Pause("fixture boundary");
        Check(p.Gold<650,"Silent merchant actually transacted");
        foreach(var canonical in new EventModel[]{ModelDb.Event<DoorsOfLightAndDark>(),ModelDb.Event<SapphireSeed>(),ModelDb.Event<Neow>()})
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:canonical,showTransition:false);
            await Delay(.6);auto.Enable(true);await Until(()=>Screen() is NMapScreen,"Silent event completed: "+canonical.Id,75);auto.Pause("fixture boundary");
        }
        // Direct native pickup previews exercise the same owned selection pipeline.
        foreach(var relic in new RelicModel[]{ModelDb.Relic<EmptyCage>().ToMutable(),ModelDb.Relic<HeftyTablet>().ToMutable(),ModelDb.Relic<PandorasBox>().ToMutable()})
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);await Delay(.5);
            var model=(EventModel)GameActions.Field(NEventRoom.Instance!,"_event")!;
            AccessTools.Field(typeof(PartialAutopilot),"ownedEvent").SetValue(auto,model);
            AccessTools.Field(typeof(PartialAutopilot),"eventSelection").SetValue(auto,RelicValue.Evaluate(relic.Id.Entry,SnapshotReader.Read(state,p),true).Selection);
            auto.Enable(true);
            var pickup=RelicCmd.Obtain(relic,p);
            await Until(()=>pickup.IsCompleted,"Silent relic selector completed: "+relic.Id,75);await pickup;auto.Pause("fixture boundary");
        }
        Check(!auto.Enabled,"Silent user can stop takeover");
        await Shot0620("silent-native-complete");
    }
}
