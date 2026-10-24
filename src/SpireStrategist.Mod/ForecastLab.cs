using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.Text.Json;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckAncientHealing(RunState state,AdvisorService service)
    {
        var player=service.Player;var creature=player.Creature;
        var rate=RunManager.Instance.HasAscension(AscensionLevel.WearyTraveler)?.8m:1m;
        Check((int)AscensionLevel.WearyTraveler==2,"installed WearyTraveler threshold is ascension 2");
        // StartNewSingleplayerRun returns on the map, before the first ancient is entered.
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ModelDb.Event<Neow>(),showTransition:false);await Delay(.5);
        Check(creature.CurrentHp==(int)(creature.MaxHp*rate),"native Neow start HP follows ascension healing");
        var rows=new List<object>();
        foreach(var ancient in new AncientEventModel[]{ModelDb.Event<Neow>(),ModelDb.Event<Tezcatara>()})
        foreach(var before in new[]{1,31,79,80})
        {
            await CreatureCmd.SetCurrentHp(creature,before);
            var old=creature.CurrentHp;var max=creature.MaxHp;
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:ancient,showTransition:false);await Delay(.5);
            var actual=(AncientEventModel)GameActions.Field(NEventRoom.Instance!,"_event")!;
            var baseHp=ancient is Neow?0:old;
            var expected=(int)(baseHp+(max-baseHp)*rate);
            Check(creature.CurrentHp==expected&&actual.HealedAmount==expected-baseHp,$"native {ancient.Id.Entry} A{state.AscensionLevel} {old}/{max} -> {expected}/{max}");
            Check(SnapshotReader.Read(state,player).Hp==expected,"advisor uses actual post-ancient HP");
            rows.Add(new{ancient=ancient.Id.Entry,ascension=state.AscensionLevel,before=old,max,baseHp,rate,expected,actual=creature.CurrentHp,healed=actual.HealedAmount});
        }
        File.WriteAllText(Path.Combine(OS.GetUserDataDir(),"ancient-healing.json"),JsonSerializer.Serialize(rows,Journal.Json));
    }

    private static async Task CheckExpandedForecasts(RunState state,AdvisorService service)
    {
        var player=service.Player;var auto=service.Autopilot;var evidence=new List<object>();
        foreach(var canonical in new EventModel[]{ModelDb.Event<AromaOfChaos>(),ModelDb.Event<WhisperingHollow>(),ModelDb.Event<TabletOfTruth>()})
        {
            auto.Pause("forecast fixture");
            await RunManager.Instance.EnterRoomDebug(RoomType.Event,model:canonical,showTransition:false);await Delay(1);
            for(var step=0;step<(canonical is TabletOfTruth?5:1);step++)
            {
                var room=NEventRoom.Instance!;var plan=EventAdapter.Read(service,room)!;
                var button=plan.Buttons.Single(b=>canonical is TabletOfTruth?b.Option.TextKey.Split('.').Last().StartsWith("DECIPHER"):b.Option.TextKey.EndsWith(canonical is AromaOfChaos?".LET_GO":".HUG"));
                var choice=plan.Choices.Single(c=>c.Key==button.GetInstanceId().ToString());
                var before=JsonSerializer.Serialize(SnapshotReader.Read(state,player),Journal.Json);var rng=plan.Model.Rng.ToSerializable();
                for(var i=0;i<20;i++)EventAdapter.Read(service,room);
                Check(before==JsonSerializer.Serialize(SnapshotReader.Read(state,player),Journal.Json)&&rng.Equals(plan.Model.Rng.ToSerializable()),"20 forecast reads preserve state and RNG "+canonical.Id.Entry+" step "+step);
                var deck=player.Deck.Cards.ToArray();var levels=deck.Select(c=>c.CurrentUpgradeLevel).ToArray();
                var upgrades=deck.Select(c=>new StrategicValue(plan.State).Upgrade(SnapshotReader.ReadCard(c))).ToArray();
                if(canonical is TabletOfTruth)
                {
                    Check(choice.RandomUpgrade==0,"tablet forecast replaces average upgrade prior");
                    GameActions.Invoke(button,"OnRelease");await Delay(3);
                    var actual=deck.Select((c,i)=>c.CurrentUpgradeLevel>levels[i]?upgrades[i]:0).Sum();
                    Check(Math.Abs(actual-choice.Benefit)<.001,"tablet forecast matches actual upgrades step "+step);
                    evidence.Add(new{eventId=canonical.Id.Entry,step,choice,actualValue=actual});
                }
                else
                {
                    var best=deck.Where(c=>c.IsTransformable).Select(c=>TransformForecast.Read(plan.Model,c,plan.State)).OrderByDescending(x=>x.Value).First();
                    Check(choice.Transform==0&&Math.Abs(choice.Benefit-best.Value)<.001,"forecast enters event decision score "+canonical.Id.Entry);
                    AccessTools.Field(typeof(PartialAutopilot),"ownedEvent").SetValue(auto,plan.Model);
                    AccessTools.Field(typeof(PartialAutopilot),"eventSelection").SetValue(auto,"transform");
                    GameActions.Invoke(button,"OnRelease");
                    await Until(()=>Screen() is NDeckTransformSelectScreen or NDeckCardSelectScreen,"native transform selector");
                    auto.Enable(true);await Until(()=>plan.Model.IsFinished,"forecast-guided transform completes",45);auto.Pause("fixture complete");
                    var removed=deck.Except(player.Deck.Cards).Single();var added=player.Deck.Cards.Except(deck).Single();
                    Check(ReferenceEquals(removed,best.Original)&&added.Id.ToString()==best.Result.Id,"forecast chooses original and matches actual result "+canonical.Id.Entry);
                    evidence.Add(new{eventId=canonical.Id.Entry,choice,original=removed.Id.ToString(),predicted=best.Result.Id,actual=added.Id.ToString()});
                }
            }
        }
        File.WriteAllText(Path.Combine(OS.GetUserDataDir(),"expanded-forecasts.json"),JsonSerializer.Serialize(evidence,Journal.Json));
    }
}
