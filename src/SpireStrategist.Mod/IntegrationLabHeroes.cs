using Godot;
using MegaCrit.Sts2.Core.Combat;
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
    private static async Task CheckHeroCombat(RunState state,AdvisorService service)
    {
        // Native starter deck against a fixed weak encounter, not a run/win-rate sample.
        var player=service.Player;
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
        NMapScreen.Instance!.Close(false);
        var encounter=ModelDb.GetById<EncounterModel>(new ModelId("ENCOUNTER","FUZZY_WURM_CRAWLER_WEAK")).ToMutable();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster,model:encounter,showTransition:false);
        await Until(()=>CombatManager.Instance.IsInProgress,"native hero combat starts",45);
        var solver=new SolverBridge();Check(solver.Bind()==null,"solver binds for native hero");
        service.Autopilot.Enable(true);
        await Until(()=>solver.FullAuto||!CombatManager.Instance.IsInProgress,"hero combat automatic handoff",60);
        await Until(()=>!CombatManager.Instance.IsInProgress,"hero native combat resolves",160);
        service.Autopilot.Pause("combat fixture complete");
        Check(player.Creature.CurrentHp>0,"native hero starter combat won by solver");
        Check(!service.Autopilot.Enabled,"combat fixture hands control back");
    }
    private static async Task CheckHeroNative(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        var hero=p.Character.GetType().Name;var regent=hero=="Regent";
        Check(HeroPolicy.Supports(hero)&&DefectStrategy.Supported(hero),"native hero support and default manual mode");
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
            Check(JsonSerializer.Serialize(cards,Journal.Json)==JsonSerializer.Serialize(actual,Journal.Json),"Hero forecast equals native reward: "+kind);
        }
        var shopCopy=RewardForesightReader.Clone(state);var shopOther=RewardForesightReader.Clone(state);
        var preview=RewardForesightReader.GenerateShop(shopCopy.Players.Single(),"shop",1,1);
        var nativeShop=MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory.CreateForNormalMerchant(shopOther.Players.Single());
        Check(preview.Cards.Select(c=>c.Id).SequenceEqual(nativeShop.CardEntries.Select(c=>c.CreationResult!.Card.Id.ToString())),"Hero shop forecast matches native stock");
        Check(before==RewardForesightReader.Fingerprint(state,p),"forecasts leave live RNG deck and inventory unchanged");
        if(regent){await CardPileCmd.Add(state.CreateCard<Furnace>(p),PileType.Deck);await CardPileCmd.Add(state.CreateCard<SpoilsOfBattle>(p),PileType.Deck);await CardPileCmd.Add(state.CreateCard<Bulwark>(p),PileType.Deck);}
        else{await CardPileCmd.Add(state.CreateCard<CaptureSpirit>(p),PileType.Deck);await CardPileCmd.Add(state.CreateCard<Haunt>(p),PileType.Deck);await CardPileCmd.Add(state.CreateCard<Parse>(p),PileType.Deck);}
        await CreatureCmd.SetCurrentHp(p.Creature,p.Creature.MaxHp);
        var upgrades=p.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel);
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        await Until(()=>service.Decision?.Kind=="rest","Hero camp advice");
        if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_RENDER_UI")=="1")
            await Until(()=>service.Visuals.VisibleItemKey==service.Decision!.Evaluation.RecommendedKey,"Hero camp marker painted");
        auto.Enable(true);
        await Until(()=>Screen() is NMapScreen,"Hero smith and return to map",45);auto.Pause("fixture boundary");
        Check(p.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)>upgrades,"Hero camp upgrades useful cards");
        NMapScreen.Instance!.Close(false);
        CardModel trap=regent?state.CreateCard<SwordSage>(p):state.CreateCard<SoulStorm>(p);
        var reward=new CardReward(new CardModel[]{trap,regent?state.CreateCard<Glitterstream>(p):state.CreateCard<PullAggro>(p),regent?state.CreateCard<CrescentSpear>(p):state.CreateCard<DrainPower>(p)},CardCreationSource.Encounter,p,CardCreationOptions.ForRoom(p,RoomType.Monster));
        var offer=new RewardsSet(p).WithCustomRewards([reward]).Offer();
        await Until(()=>Screen() is NRewardsScreen,"Hero card reward opens");
        var button=GameActions.Descendants<NRewardButton>((Node)Screen()!).First(b=>b.Reward is CardReward);
        var claim=GameActions.ClaimReward(button);
        await Until(()=>service.Decision?.Kind=="cards","Hero card advice visible");
        Check(service.Decision!.State.Foresight!=null,"Hero actual reward advisor attaches foresight status");
        if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_RENDER_UI")=="1")
            await Until(()=>service.Visuals.VisibleItemKey==service.Decision!.Evaluation.RecommendedKey,"Hero recommended card marker painted");
        await Shot0620("hero-card-recommendation");auto.Enable(true);
        await Until(()=>offer.IsCompleted,"Hero automatic reward acquisition",45);await offer;auto.Pause("fixture boundary");
        Check(p.Deck.Cards.Contains(trap),"Hero acquires supported output core rather than redundant transition");
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);p.Gold=650;
        NMerchantRoom.Instance!.OpenInventory();await Until(()=>Screen() is NMerchantInventory,"Hero merchant opens");
        p.Gold=13;
        var lowState=SnapshotReader.Read(state,p);var inventory=NMerchantRoom.Instance.Inventory;
        var cardSlots=inventory.GetAllSlots().Where(x=>x.IsVisibleInTree()&&x.Entry is MegaCrit.Sts2.Core.Entities.Merchant.MerchantCardEntry {IsStocked:true,CreationResult:not null}).ToArray();
        Check(cardSlots.Length==7,"native merchant has seven visible card offers");
        var lowPlan=ShopAdvice.Complete(service,lowState,inventory,ShopPolicy.Evaluate(lowState,[],null,13));
        Check(cardSlots.All(slot=>lowPlan.Choices.Any(c=>c.Key==slot.GetInstanceId().ToString()&&c.Kind=="card"&&c.IntrinsicValue.HasValue&&!c.Affordable)),"Every unaffordable native shop card still has a score");
        Check(lowPlan.Recommendation==null,"13 gold display scores never become purchases");
        service.ShowShop(lowState,lowPlan);await Delay(.5);await Shot0620("hero-shop-unaffordable-scores");
        Check(p.Gold==13,"reviewing unaffordable stock preserves gold");
        p.Gold=650;
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"Hero merchant finishes transactions and leaves",120);auto.Pause("fixture boundary");
        Check(p.Gold<650,"Hero merchant actually transacted");
        foreach(var canonical in new EventModel[]{ModelDb.Event<DoorsOfLightAndDark>(),ModelDb.Event<SapphireSeed>(),ModelDb.Event<Neow>()})
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:canonical,showTransition:false);
            await Delay(.6);auto.Enable(true);await Until(()=>Screen() is NMapScreen,"Hero event completed: "+canonical.Id,75);auto.Pause("fixture boundary");
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
            await Until(()=>pickup.IsCompleted,"Hero relic selector completed: "+relic.Id,75);await pickup;auto.Pause("fixture boundary");
        }
        Check(!auto.Enabled,"Hero user can stop takeover");
        await Shot0620("hero-native-complete");
    }
}
