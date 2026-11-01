using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckEvents(RunState state,AdvisorService service,string mode)
    {
        var player=service.Player;var auto=service.Autopilot;
        if(mode=="defect-foresight-0642"){await CheckForesight0642(state,service);return;}
        if(mode=="foul-shop-0638"){await CheckFoulShop0638(state,service);return;}
        if(mode=="enchant-0621"){await CheckEnchant0621(state,service);return;}
        if(mode=="global-shop-0619"){await CheckGlobalShop0619(state,service);return;}
        if(mode=="ancient-healing"){await CheckAncientHealing(state,service);return;}
        if(mode=="climb-064"){await CheckClimb064(state,service);return;}
        if(mode=="ui-0640"){await CheckUi0640(state,service);return;}
        if(mode=="ui-0620"){await CheckUi0620(state,service);return;}
        if(mode=="climb-shop-064"){await CheckClimbShop064(state,service);return;}
        if(mode=="tooth-065"){await CheckTooth065(state,service);return;}
        if(mode=="rest-rewards-067"){await CheckRestRewards067(state,service);return;}
        if(mode=="rest-068"){await CheckRest068(state,service);return;}
        if(mode=="rest-passive-068"){await CheckRestPassive068(state,service);return;}
        if(mode=="shop-068"){await CheckShop068(state,service);return;}
        if(mode=="shop-069"){await CheckShop069(state,service);return;}
        if(mode=="feedback-0610"){await CheckFeedback0610(state,service);return;}
        if(mode=="feedback-0611"){await CheckFeedback0611(state,service);return;}
        if(mode=="feedback-0613"){await CheckFeedback0613(state,service);return;}
        if(mode=="recovery-0614"){await CheckRecovery0614(state,service);return;}
        if(mode=="pandora-0615"){await CheckPandora0615(state,service);return;}
        if(mode=="parasol-0616"){await CheckParasol0616(state,service);return;}
        if(mode=="camp-0617"){await CheckCamp0617(state,service);return;}
        if(mode=="route-068"){await CheckRoute068(state,service);return;}
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);
        NMapScreen.Instance!.Close(false);
        player.Gold=2000;
        foreach(var card in (player.Character.GetType().Name=="Silent"?new CardModel[]{state.CreateCard<BladeOfInk>(player),state.CreateCard<Accuracy>(player),state.CreateCard<Acrobatics>(player),state.CreateCard<Backflip>(player),state.CreateCard<LanternKey>(player)}:new CardModel[]{state.CreateCard<ShrugItOff>(player),state.CreateCard<BattleTrance>(player),state.CreateCard<Inflame>(player),state.CreateCard<Impervious>(player),state.CreateCard<LanternKey>(player)}))
            await CardPileCmd.Add(card,PileType.Deck);
        foreach(var relic in new RelicModel[]{ModelDb.Relic<Vajra>().ToMutable(),ModelDb.Relic<Anchor>().ToMutable(),ModelDb.Relic<BagOfPreparation>().ToMutable(),ModelDb.Relic<OddlySmoothStone>().ToMutable(),ModelDb.Relic<HappyFlower>().ToMutable()})await RelicCmd.Obtain(relic,player);
        await PotionCmd.TryToProcure(ModelDb.Potion<BlockPotion>().ToMutable(),player);
        if(mode=="predictions"){await CheckEventPredictions(state,service);return;}
        if(mode=="forecast-061"){await CheckExpandedForecasts(state,service);return;}
        if(mode=="feedback-063"){await CheckFeedback063(state,service);return;}
        if(mode=="event-safety")
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<AromaOfChaos>(),showTransition:false);await Delay(1);
            var deck=player.Deck.Cards.ToArray();var hp=player.Creature.CurrentHp;
            auto.Enable(true);await Delay(.5);auto.Pause("test pending event cancellation");await Delay(3);
            Check(deck.SequenceEqual(player.Deck.Cards)&&hp==player.Creature.CurrentHp&&Screen() is NEventRoom,"pause cancels pending event action");
            auto.Enable(true);await Until(()=>Screen() is NDeckCardSelectScreen or NDeckTransformSelectScreen or NDeckUpgradeSelectScreen,"event opens native card selector");
            var selector=Screen()!;auto.Pause("stop before event card choice");await Delay(3);
            Check(((HashSet<CardModel>)GameActions.Field(selector,"_selectedCards")!).Count==0,"pause leaves event selector untouched");
            auto.Enable(true);await Until(()=>auto.WaitingForManual,"unowned selector waits for manual");
            Check(((HashSet<CardModel>)GameActions.Field(selector,"_selectedCards")!).Count==0&&auto.FullAutomatic,"re-enable does not seize pending manual event selection");return;
        }
        if(mode=="fake-combat")
        {
            foreach(var c in player.Deck.Cards.Where(c=>c.Id.Entry is "STRIKE_IRONCLAD" or "DEFEND_IRONCLAD").ToArray())await CardPileCmd.RemoveFromDeck(c);
            foreach(var id in new[]{"INFLAME","TWIN_STRIKE","TWIN_STRIKE","POMMEL_STRIKE","POMMEL_STRIKE","SHRUG_IT_OFF","SHRUG_IT_OFF","IMPERVIOUS","OFFERING","BATTLE_TRANCE","UPPERCUT","UPPERCUT","WHIRLWIND","TRUE_GRIT"})
                await CardPileCmd.Add(state.CreateCard(ModelDb.GetById<CardModel>(new ModelId("CARD",id)),player),PileType.Deck);
            await PotionCmd.TryToProcure(ModelDb.Potion<FoulPotion>().ToMutable(),player);
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<FakeMerchant>(),showTransition:false);await Delay(1);
            var model=(FakeMerchant)GameActions.Field(NEventRoom.Instance!,"_event")!;
            auto.Enable(true);await Until(()=>model.StartedFight,"fake merchant potion starts actual combat",30);
            Check(!player.Potions.OfType<FoulPotion>().Any(),"fake fight consumes actual potion");
            await Until(()=>Screen() is NMapScreen,"fake combat solved and rewards completed",240);
            Check(player.Creature.CurrentHp>0&&player.Relics.Any(r=>r is FakeMerchantsRug),"native fake fight won and rug collected");
            Check(!player.Relics.Any(r=>r is FakeSneckoEye),"harmful fake confusion relic left behind");return;
        }
        if(mode=="camp")
        {
            await CreatureCmd.SetCurrentHp(player.Creature,player.Creature.MaxHp);
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);await Delay(.5);
            var rest=NRestSiteRoom.Instance!;rest.Options.OfType<MegaCrit.Sts2.Core.Entities.RestSite.SmithRestSiteOption>().Single().SmithCount=2;
            var upgrades=player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel);
            auto.Enable(true);await Until(()=>Screen() is NMapScreen,"multi-smith native camp",60);auto.Pause("fixture");
            Check(player.Deck.Cards.Sum(c=>c.CurrentUpgradeLevel)==upgrades+2,"camp upgrades two selected cards");NMapScreen.Instance!.Close(false);
            await RelicCmd.Obtain(ModelDb.Relic<MeatCleaver>().ToMutable(),player);
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);await Delay(.5);
            var count=player.Deck.Cards.Count;var max=player.Creature.MaxHp;
            auto.Enable(true);await Until(()=>Screen() is NMapScreen,"cook native camp",60);auto.Pause("fixture");
            Check(player.Deck.Cards.Count==count-2&&player.Creature.MaxHp==max+5,"cook removes two cards and gains max HP");return;
        }
        if(mode=="selectors")
        {
            foreach(var id in new[]{"EMPTY_CAGE","ASTROLABE","PAELS_GROWTH","YUMMY_COOKIE","HEFTY_TABLET","SCROLL_BOXES"})
            {
                await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);await Delay(.5);
                var model=(EventModel)GameActions.Field(NEventRoom.Instance!,"_event")!;
                var assessment=RelicValue.Evaluate(id,SnapshotReader.Read(state,player),true);
                AccessTools.Field(typeof(PartialAutopilot),"ownedEvent").SetValue(auto,model);
                AccessTools.Field(typeof(PartialAutopilot),"eventSelection").SetValue(auto,assessment.Selection);
                var task=RelicCmd.Obtain(ModelDb.GetById<RelicModel>(new ModelId("RELIC",id)).ToMutable(),player);
                await Until(()=>Screen() is NDeckCardSelectScreen or NDeckEnchantSelectScreen or NDeckTransformSelectScreen or NChooseACardSelectionScreen or NDeckUpgradeSelectScreen or NChooseABundleSelectionScreen,"relic selector "+id);
                auto.Enable(true);await Until(()=>task.IsCompleted,"relic selector completed "+id,60);await task;auto.Pause("selector fixture");
                Check(player.Relics.Any(r=>r.Id.Entry==id),"native pickup selectors "+id);
            }
            return;
        }
        if(mode=="audit")
        {
            var rows=new List<object>();
            foreach(var canonical in ModelDb.AllEvents)
            {
                if(canonical is FakeMerchant){rows.Add(new{id=canonical.Id.Entry,custom=true});continue;}
                if(canonical is ColorfulPhilosophers)AccessTools.Field(player.GetType(),"<UnlockState>k__BackingField").SetValue(player,MegaCrit.Sts2.Core.Unlocks.UnlockState.all);
                await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:canonical,showTransition:false);
                await Delay(.8);
                var room=NEventRoom.Instance!;
                var plan=EventAdapter.Read(service,room);
                Check(plan!=null&&plan.Choices.Where(c=>c.Enabled).All(c=>c.Supported),"initial options recognized "+canonical.Id.Entry);
                rows.Add(new{id=canonical.Id.Entry,choices=plan?.Choices,decision=plan?.Evaluation,variables=plan?.Model.DynamicVars.ToDictionary(x=>x.Key,x=>(double)x.Value.BaseValue),textKeys=plan?.Buttons.Select(b=>b.Option.TextKey).ToArray()});
                GD.Print("[StrategistLab] audit "+canonical.Id.Entry+" unsupported="+string.Join(',',plan?.Choices.Where(c=>c.Enabled&&!c.Supported).Select(c=>c.Name)??[]));
            }
            foreach(var ancient in ModelDb.AllAncients)
                foreach(var option in ancient.AllPossibleOptions)
                    if(option.Relic is {} relic)
                    {
                        var result=RelicValue.Evaluate(relic.Id.Entry,SnapshotReader.Read(state,player),true);
                        if(!result.Supported)throw new Exception("Unrecognized ancient relic: "+relic.Id);
                        rows.Add(new{ancient=ancient.Id.Entry,relic=relic.Id.Entry,result});
                    }
            File.WriteAllText(Path.Combine(OS.GetUserDataDir(),"event-audit.json"),JsonSerializer.Serialize(rows,Journal.Json));
            Check(rows.Count>100,"event and ancient runtime audit exported");
            return;
        }
        var names=mode=="smoke"?new[]{"AbyssalBaths","Amalgamator","AromaOfChaos","DoorsOfLightAndDark","SapphireSeed","SelfHelpBook","MorphicGrove","TrashHeap","DollRoom","BrainLeech"}:mode.Split(',');
        foreach(var name in names)
        {
            auto.Pause("event fixture");
            var canonical=ModelDb.AllEvents.Cast<EventModel>().Concat(ModelDb.AllAncients).SingleOrDefault(e=>e.GetType().Name==name)??throw new Exception("Unknown fixture event: "+name);
            if(canonical is ColorfulPhilosophers)AccessTools.Field(player.GetType(),"<UnlockState>k__BackingField").SetValue(player,MegaCrit.Sts2.Core.Unlocks.UnlockState.all);
            await CreatureCmd.SetCurrentHp(player.Creature,player.Creature.MaxHp);player.Gold=600;
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:canonical,showTransition:false);
            await Delay(1);
            var model=(EventModel)GameActions.Field(NEventRoom.Instance!,"_event")!;
            if(canonical is ColorfulPhilosophers)Check(EventAdapter.Read(service,NEventRoom.Instance!)!.Buttons.Any(b=>!b.Option.IsProceed),"colorful fixture contains actual unlocked foreign choices");
            auto.Enable(true);
            await Until(()=>Screen() is NMapScreen,"event finishes and proceeds "+name,100);
            Check(model.IsFinished&&auto.Enabled&&auto.FullAutomatic,"native event completes continuously "+name);
            auto.Pause("event fixture boundary");NMapScreen.Instance!.Close(false);await Delay(.5);
        }
    }
    private static async Task CheckEventPredictions(RunState state,AdvisorService service)
    {
        foreach(var spec in new[]{"DoorsOfLightAndDark:LIGHT","TrashHeap:GRAB","TrashHeap:DIVE_IN","SlipperyBridge:HOLD_ON_0","DollRoom:TAKE_SOME_TIME"})
        {
            var parts=spec.Split(':');var canonical=ModelDb.AllEvents.Single(e=>e.GetType().Name==parts[0]);
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:canonical,showTransition:false);await Delay(1);
            var room=NEventRoom.Instance!;var plan=EventAdapter.Read(service,room)!;
            var button=plan.Buttons.Single(b=>b.Option.TextKey.EndsWith("."+parts[1]));
            var choice=plan.Choices.Single(c=>c.Key==button.GetInstanceId().ToString());
            var rng=JsonSerializer.Serialize(HiddenReader.Read(state,service.Player).Streams);var eventRng=plan.Model.Rng.ToSerializable();
            for(var i=0;i<20;i++)EventAdapter.Read(service,room);
            Check(eventRng.Equals(plan.Model.Rng.ToSerializable())&&rng==JsonSerializer.Serialize(HiddenReader.Read(state,service.Player).Streams),"20 prediction reads preserve RNG "+spec);
            var deck=service.Player.Deck.Cards.ToArray();var levels=deck.Select(c=>c.CurrentUpgradeLevel).ToArray();var relics=service.Player.Relics.ToArray();
            var values=deck.Select(c=>new StrategicValue(plan.State).Upgrade(SnapshotReader.ReadCard(c))).ToArray();
            GameActions.Invoke(button,"OnRelease");await Delay(2);
            var note=string.Join(';',choice.Notes??[]);
            if(parts[0]=="DoorsOfLightAndDark")
            {
                var actual=deck.Select((c,i)=>c.CurrentUpgradeLevel>levels[i]?values[i]:0).Sum();
                Check(Math.Abs(actual-choice.Benefit)<.001,"predicted upgrades equal actual native upgrades");
            }
            else if(parts[0]=="TrashHeap")
            {
                var actual=parts[1]=="GRAB"?service.Player.Deck.Cards.Except(deck).Single().Title:service.Player.Relics.Except(relics).Single().Title.GetFormattedText();
                Check(note.Contains(actual),"predicted actual reward "+spec);
            }
            else if(parts[0]=="SlipperyBridge")Check(note.Contains(((CardModel)GameActions.Field(plan.Model,"_randomCardToLose")!).Title),"predicted bridge next target equals actual");
            else
            {
                var after=EventAdapter.Read(service,room)!;
                Check(after.Buttons.Length==2&&after.Buttons.All(b=>note.Contains(b.Option.Title.GetFormattedText())),"predicted doll alternatives equal actual");
            }
        }
    }
}
