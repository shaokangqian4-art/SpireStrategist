using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckRestPassive068(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        var feather=ModelDb.Relic<EternalFeather>().ToMutable();
        var tea=(VenerableTeaSet)ModelDb.Relic<VenerableTeaSet>().ToMutable();
        var fake=(FakeVenerableTeaSet)ModelDb.Relic<FakeVenerableTeaSet>().ToMutable();
        foreach(var r in new RelicModel[]{feather,tea,fake})await RelicCmd.Obtain(r,p);
        await CardPileCmd.Add(state.CreateCard<BattleTrance>(p),PileType.Deck);
        await CreatureCmd.SetCurrentHp(p.Creature,55);
        var healed=55+p.Deck.Cards.Count/5*3;
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        await Until(()=>p.Creature.CurrentHp==healed,"eternal feather heals on entry");
        Check(tea.GainEnergyInNextCombat&&fake.GainEnergyInNextCombat,"both tea sets arm without sleeping");
        var upgrades=p.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel);
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"passive camp relics and smith");
        Check(p.Creature.CurrentHp==healed&&p.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)==upgrades+1,
            "feather heal is counted before deciding to smith, tea sets need no sleep");
        auto.Pause("passive boundary");NMapScreen.Instance!.Close(false);
        foreach(var r in new RelicModel[]{feather,tea,fake})await RelicCmd.Remove(r);
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance!.Close(false);
        var growth=ModelDb.Relic<PaelsGrowth>().ToMutable();
        GameActions.Invoke(auto,"PrepareRelicSelection",growth);
        var obtaining=RelicCmd.Obtain(growth,p);
        auto.Enable(true);await Until(()=>obtaining.IsCompleted,"growth initial enchant selector");await obtaining;
        auto.Pause("growth fixture");
        foreach(var c in p.Deck.Cards.Where(c=>c.IsUpgradable).ToArray())CardCmd.Upgrade(c);
        await CreatureCmd.SetCurrentHp(p.Creature,80);
        var count=p.Deck.Cards.Count;
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"Paels Growth clone",60);
        Check(p.Deck.Cards.Count==count+1&&auto.Enabled&&!auto.WaitingForManual,"growth clones marked card and leaves without stall");
        auto.Pause("growth boundary");NMapScreen.Instance!.Close(false);await RelicCmd.Remove(growth);
        await RelicCmd.Obtain(ModelDb.Relic<MeatCleaver>().ToMutable(),p);
        var max=p.Creature.MaxHp;count=p.Deck.Cards.Count;
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        auto.Enable(true);await Until(()=>Screen() is NMapScreen,"MeatCleaver cook",60);
        Check(p.Deck.Cards.Count==count-2&&p.Creature.MaxHp==max+5&&auto.Enabled,"cook selects two cards and completes max-HP effect");
        auto.Pause("passive camp suite complete");
    }
    private static async Task CheckRest068(RunState state,AdvisorService service)
    {
        var player=service.Player;var auto=service.Autopilot;
        async Task Start(int hp)
        {
            auto.Pause("camp fixture setup");
            await CreatureCmd.SetCurrentHp(player.Creature,hp);
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            await Until(()=>service.Decision?.Kind=="rest"&&service.RestRoom==NRestSiteRoom.Instance,"fresh camp decision");
            auto.Enable(true);
        }
        async Task End(string label)
        {
            await Until(()=>Screen() is NMapScreen,label+" completes and leaves",60);
            Check(auto.Enabled&&!auto.WaitingForManual,label+" no timeout or manual stall");
            auto.Pause("camp fixture boundary");NMapScreen.Instance!.Close(false);
        }
        var tent=ModelDb.Relic<MiniatureTent>().ToMutable();await RelicCmd.Obtain(tent,player);
        var key=state.CreateCard<BattleTrance>(player);await CardPileCmd.Add(key,PileType.Deck);
        await Start(55);
        await Until(()=>key.CurrentUpgradeLevel>0,"tent upgrades key card first");
        Check(player.Creature.CurrentHp==55,"55/80 smith precedes sleep");
        await End("tent smith then heal");
        Check(player.Creature.CurrentHp==79,"tent permits heal after smith once");
        await RelicCmd.Remove(tent);
        foreach(var card in player.Deck.Cards.Where(c=>c.IsUpgradable).ToArray())CardCmd.Upgrade(card);
        var pumpkin=(PumpkinCandle)ModelDb.Relic<PumpkinCandle>().ToMutable();await RelicCmd.Obtain(pumpkin,player);
        pumpkin.KindleCount=0;
        await Start(80);await End("pumpkin kindle");
        Check(pumpkin.KindleCount==5,"pumpkin grants exactly five combat charges");
        await RelicCmd.Remove(pumpkin);
        var girya=(Girya)ModelDb.Relic<Girya>().ToMutable();await RelicCmd.Obtain(girya,player);
        for(var i=1;i<=3;i++)
        {
            await Start(80);await End("girya lift "+i);
            Check(girya.TimesLifted==i,"girya adds exactly one permanent strength "+i);
        }
        await Start(80);
        Check(!NRestSiteRoom.Instance!.Options.Any(o=>o.OptionId=="LIFT"),"girya stops offering lifts at three");
        await End("girya exhausted");
        await RelicCmd.Remove(girya);
        await CardPileCmd.Add(state.CreateCard<ByrdonisEgg>(player),PileType.Deck);
        await Start(80);await End("native egg hatch");
        Check(player.Relics.Any(r=>r is Byrdpip)&&!player.Deck.Cards.Any(c=>c is ByrdonisEgg)&&player.Deck.Cards.Any(c=>c is ByrdSwoop),
            "egg still exists and hatches into Byrdpip plus ByrdSwoop");
        foreach(var card in player.Deck.Cards.Where(c=>c.IsUpgradable).ToArray())CardCmd.Upgrade(card);
        var shovel=ModelDb.Relic<Shovel>().ToMutable();await RelicCmd.Obtain(shovel,player);
        TestRngInjector.SetRelicOverride<Vajra>();
        await Start(80);await End("shovel ordinary relic");
        Check(player.Relics.Any(r=>r is Vajra),"shovel takes native generated relic");
        var count=player.Deck.Cards.Count;
        TestRngInjector.SetRelicOverride<EmptyCage>();
        await Start(80);await End("shovel EmptyCage selector");
        Check(player.Relics.Any(r=>r is EmptyCage)&&player.Deck.Cards.Count==count-2,"dig owns its removal selector");
        TestRngInjector.SetRelicOverride<Astrolabe>();
        await Start(80);await End("shovel Astrolabe selector");
        Check(player.Relics.Any(r=>r is Astrolabe)&&player.Deck.Cards.Count==count-2,"dig owns transform and upgrade selector");
        foreach(var card in player.Deck.Cards.Where(c=>c.IsUpgradable).ToArray())CardCmd.Upgrade(card);
        TestRngInjector.SetRelicOverride<DollysMirror>();
        await Start(80);await End("shovel mirror selector");
        Check(player.Relics.Any(r=>r is DollysMirror)&&player.Deck.Cards.Count==count-1,"dig owns duplicate selector");
        tent=ModelDb.Relic<MiniatureTent>().ToMutable();await RelicCmd.Obtain(tent,player);
        pumpkin=(PumpkinCandle)ModelDb.Relic<PumpkinCandle>().ToMutable();await RelicCmd.Obtain(pumpkin,player);pumpkin.KindleCount=0;
        girya=(Girya)ModelDb.Relic<Girya>().ToMutable();await RelicCmd.Obtain(girya,player);
        foreach(var card in player.Deck.Cards.Where(c=>c.IsUpgradable).ToArray())CardCmd.Upgrade(card);
        TestRngInjector.SetRelicOverride<Anchor>();
        await Start(80);await End("tent with pumpkin girya shovel");
        Check(girya.TimesLifted==1&&pumpkin.KindleCount==5&&player.Relics.Any(r=>r is Anchor),"tent executes each available distinct relic action once");
        Check(player.Creature.CurrentHp==80,"full HP extra sleep is unnecessary");
    }

    private static async Task CheckShop068(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        async Task<NMerchantInventory> Shop()
        {
            auto.Pause("priority fixture");
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);await Delay(.5);
            NMerchantRoom.Instance!.OpenInventory();
            await Until(()=>Screen() is NMerchantInventory,"open shop fixture");
            var inventory=(NMerchantInventory)Screen()!;
            foreach(var slot in inventory.GetAllSlots())
            {
                if(slot.Entry is MerchantCardEntry c)AccessTools.Property(c.GetType(),"CreationResult").SetValue(c,null);
                if(slot.Entry is MerchantRelicEntry r)AccessTools.Property(r.GetType(),"Model").SetValue(r,null);
            }
            return inventory;
        }
        void Stock(MerchantRelicEntry entry,RelicModel relic,int price)
        {GameActions.Invoke(entry,"SetModel",relic);AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(entry,price);}
        async Task Done(string label)
        {
            foreach(var slot in ((NMerchantInventory)Screen()!).GetAllSlots())GameActions.Invoke(slot,"UpdateVisual");
            auto.Enable(true);await Until(()=>Screen() is MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen,label,60);
            Check(auto.FullAutomatic&&!auto.WaitingForManual,label+" exits shop automatically");
            auto.Pause("fixture boundary");
            MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance!.Close(false);
        }
        var shop=await Shop();
        var entries=shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantRelicEntry>().ToArray();
        Stock(entries[0],ModelDb.Relic<IceCream>().ToMutable(),200);
        p.Gold=200;var count=p.Deck.Cards.Count;
        await Done("ice cream before removal");
        Check(p.Relics.Any(r=>r is IceCream)&&p.Gold==0&&p.Deck.Cards.Count==count,"priority purchase precedes affordable removal and can use random-relic reserve");
        await RelicCmd.Remove(p.GetRelic<IceCream>()!);
        shop=await Shop();entries=shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantRelicEntry>().ToArray();
        Stock(entries[0],ModelDb.Relic<MembershipCard>().ToMutable(),100);
        Stock(entries[1],ModelDb.Relic<IceCream>().ToMutable(),200);
        p.Gold=200;
        await Done("membership recalculates prices");
        Check(p.Relics.Any(r=>r is MembershipCard)&&p.Relics.Any(r=>r is IceCream)&&p.Gold==0,"discount bought before ice cream with exact combined discounted budget");
        await RelicCmd.Remove(p.GetRelic<MembershipCard>()!);await RelicCmd.Remove(p.GetRelic<IceCream>()!);
        shop=await Shop();entries=shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantRelicEntry>().ToArray();
        Stock(entries[0],ModelDb.Relic<ChemicalX>().ToMutable(),200);
        var ce=shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantCardEntry>().First();
        var whirlwind=state.CreateCard<Whirlwind>(p);
        AccessTools.Property(ce.GetType(),"CreationResult").SetValue(ce,new CardCreationResult(whirlwind));
        AccessTools.Field(typeof(MerchantEntry),"_cost").SetValue(ce,25);
        p.Gold=225;count=p.Deck.Cards.Count;
        await Done("X card plus Chemical X bundle");
        Check(p.Gold==0&&p.Deck.Cards.Contains(whirlwind)&&p.Relics.Any(r=>r is ChemicalX)&&p.Deck.Cards.Count==count+1,
            "joint purchase obtains both items before spending on removal");
        await RelicCmd.Remove(p.GetRelic<ChemicalX>()!);
        shop=await Shop();entries=shop.GetAllSlots().Select(s=>s.Entry).OfType<MerchantRelicEntry>().ToArray();
        Stock(entries[0],ModelDb.Relic<ChemicalX>().ToMutable(),200);p.Gold=200;
        await Done("existing X priority");
        Check(p.Relics.Any(r=>r is ChemicalX)&&p.Gold==0,"existing X deck buys Chemical X without extra card");
    }

    private static async Task CheckRoute068(RunState state,AdvisorService service)
    {
        var p=service.Player;
        var before=JsonSerializer.Serialize(state.Rng.ToSerializable());
        var saved=state.Act.ToSave().SerializableRooms;
        var e=RouteAdapter.Economy(service);
        Check(e.NormalEncounters.Length>0&&e.NormalEncounters[0].Weak==ModelDb.GetById<EncounterModel>(saved.NormalEncounterIds![saved.NormalEncountersVisited]).IsWeak,
            "adapter reads actual next encounter weak flag");
        Check(Math.Abs(e.UnknownOdds[0]-state.Odds.UnknownMapPoint.MonsterOdds)<.0001,"native current question odds read directly");
        await RelicCmd.Obtain(ModelDb.Relic<JuzuBracelet>().ToMutable(),p);
        Check(!RouteAdapter.Economy(service).UnknownAllowed[0],"native Juzu hook suppresses question monsters");
        await RelicCmd.Remove(p.GetRelic<JuzuBracelet>()!);
        state.Odds.UnknownMapPoint.MonsterOdds=-1;
        Check(RouteAdapter.Economy(service).UnknownOdds[0]<0,"negative modifier odds stay disabled");
        state.Odds.UnknownMapPoint.MonsterOdds=.4f;
        var consumed=state.Act.PullNextEncounter(RoomType.Monster);
        state.Act.MarkRoomVisited(RoomType.Monster);
        var after=RouteAdapter.Economy(service);
        Check(state.Act.ToSave().SerializableRooms.NormalEncountersVisited==saved.NormalEncountersVisited+1&&
            after.NormalEncounters[0].Weak==e.NormalEncounters[1].Weak,"normal pool advances using native encounter counter");
        Check(before==JsonSerializer.Serialize(state.Rng.ToSerializable()),"forecast reads and pre-generated encounter visits do not consume live run RNG");
        var nodes=RouteAdapter.Nodes(state);
        Check(state.Map.GetAllMapPoints().All(p=>nodes.Single(n=>n.Id==RouteAdapter.Key(p)).Row==p.coord.row),"actual map rows exposed to late-shop policy");
        while(state.Act.ToSave().SerializableRooms.NormalEncountersVisited<saved.NormalEncounterIds!.Count)
            state.Act.MarkRoomVisited(RoomType.Monster);
        var wrapped=state.Act.PullNextEncounter(RoomType.Monster);
        Check(RouteAdapter.Economy(service).NormalEncounters[0].Weak==wrapped.IsWeak,
            "exhausted encounter sequence follows native or long-map wrap behavior");
        var boots=(WingedBoots)ModelDb.Relic<WingedBoots>().ToMutable();await RelicCmd.Obtain(boots,p);
        var points=state.Map.GetAllMapPoints().ToArray();
        var parent=points.First(n=>n.Children.Count>0&&points.Any(c=>c.coord.row==n.coord.row+1&&!n.Children.Contains(c)));
        state.ClearVisitedMapCoordsDebug();state.AddVisitedMapCoord(parent.coord);
        var flight=RouteAdapter.Economy(service);
        Check(flight.FlightCharges==3&&flight.FlightRoots.Length>0&&flight.FlightRoots.All(id=>!state.CurrentMapPoint!.Children.Any(c=>RouteAdapter.Key(c)==id)),
            "flight charges and non-natural edges come from native map");
        state.AddVisitedMapCoord(parent.Children.First().coord);
        await boots.AfterRoomEntered(new MapRoom());
        Check(boots.TimesUsed==0,"native boots preserve charge on natural edge");
        state.ClearVisitedMapCoordsDebug();state.AddVisitedMapCoord(parent.coord);
        state.AddVisitedMapCoord(points.Single(c=>RouteAdapter.Key(c)==flight.FlightRoots[0]).coord);
        await boots.AfterRoomEntered(new MapRoom());
        Check(boots.TimesUsed==1&&RouteAdapter.Economy(service).FlightCharges==2,"native flight consumes exactly one charge and forecast refreshes");
        boots.TimesUsed=3;
        Check(RouteAdapter.Economy(service).FlightRoots.Length==0,"used-up boots do not add phantom flight costs");
    }
}
