using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireStrategist.Mod;

internal static partial class AutopilotLab
{
    private static async Task CheckRestRewards067(RunState state,AdvisorService service)
    {
        var player=service.Player;var auto=service.Autopilot;
        async Task ClearPotions()
        {foreach(var potion in player.Potions.ToArray())await PotionCmd.Discard(potion);}
        async Task Rest(string label,bool hasCards=false,bool rewards=true,int expectedHp=29,bool pause=false,bool review=false)
        {
            auto.Pause("fixture setup");
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            await CreatureCmd.SetCurrentHp(player.Creature,5);
            await Until(()=>service.Decision?.Kind=="rest"&&service.Decision.State.Hp==5,label+" advice");
            var deckBefore=player.Deck.Cards.Count;
            auto.Enable(true);
            if(rewards)
            {
                await Until(()=>Screen() is NRewardsScreen,label+" native nested rewards");
                Check(service.RestBusy&&AccessTools.Field(typeof(PartialAutopilot),"operation").GetValue(auto) is Task {IsCompleted:false},
                    label+" parent rest is still waiting for rewards");
            }
            if(pause)
            {
                auto.Pause("fixture pauses during sleep rewards");
                await Delay(3);
                Check(Screen() is NRewardsScreen&&player.Deck.Cards.Count==deckBefore&&!auto.Enabled,
                    "explicit pause leaves nested rewards untouched");
                auto.Enable(true);
            }
            var expectedCards=0;
            if(hasCards)
            {
                await Until(()=>Screen() is NCardRewardSelectionScreen&&service.Decision?.Kind=="cards",label+" native card choice");
                expectedCards=service.Decision!.Evaluation.RecommendedKey?.StartsWith("card:")==true?1:0;
                if(review)
                {
                    service.Panel.SetReviewPaused(true);
                    await Delay(32);
                    Check(auto.Enabled&&Screen() is NCardRewardSelectionScreen&&player.Deck.Cards.Count==deckBefore,
                        "reviewing a nested card reward for over 30 seconds never times out or selects");
                    service.Panel.SetReviewPaused(false);
                }
            }
            await Until(()=>Screen() is NMapScreen,label+" returns to map",40);
            Check(auto.Enabled&&auto.FullAutomatic&&!auto.WaitingForManual,label+" retains automatic control");
            Check(player.Creature.CurrentHp==expectedHp,label+" heals exactly once");
            Check(player.Deck.Cards.Count==deckBefore+expectedCards,label+" follows card advice without extra picks");
            Check(AccessTools.Field(typeof(PartialAutopilot),"operation").GetValue(auto)==null&&
                ((System.Collections.ICollection)AccessTools.Field(typeof(PartialAutopilot),"parentOperations").GetValue(auto)!).Count==0,
                label+" completes child and parent actions before leaving");
            auto.Pause("fixture boundary");NMapScreen.Instance!.Close(false);
        }
        var dream=ModelDb.Relic<DreamCatcher>().ToMutable();
        await RelicCmd.Obtain(dream,player);
        await Rest("DreamCatcher",hasCards:true,pause:true);
        await RelicCmd.Remove(dream);
        var mailbox=ModelDb.Relic<TinyMailbox>().ToMutable();
        await RelicCmd.Obtain(mailbox,player);
        await ClearPotions();
        await Rest("TinyMailbox empty belt");
        Check(player.Potions.Count()==2,"empty belt receives both native sleep potions");
        await ClearPotions();
        var existing=ModelDb.Potion<BlockPotion>().ToMutable();
        await PotionCmd.TryToProcure(existing,player);
        await Rest("TinyMailbox one free slot");
        Check(player.Potions.Count()==2&&player.Potions.Contains(existing),"one slot takes one potion and preserves the existing one");
        var full=player.Potions.ToArray();
        await Rest("TinyMailbox full belt");
        Check(full.SequenceEqual(player.Potions),"full belt leaves all extra potions without replacement");
        // Native removal invalidates that model instance; reacquisition uses a fresh relic.
        dream=ModelDb.Relic<DreamCatcher>().ToMutable();
        await RelicCmd.Obtain(dream,player);
        await ClearPotions();
        await Rest("DreamCatcher and TinyMailbox",hasCards:true,review:true);
        Check(player.Potions.Count()==2,"combined sleep rewards collect both potions after the card decision");
        await RelicCmd.Remove(dream);
        await ClearPotions();
        var sozu=ModelDb.Relic<Sozu>().ToMutable();await RelicCmd.Obtain(sozu,player);
        await Rest("TinyMailbox with Sozu",rewards:false);
        Check(!player.Potions.Any(),"Sozu cannot trap the assistant trying to fill empty slots");
        await RelicCmd.Remove(sozu);await RelicCmd.Remove(mailbox);
        await RelicCmd.Obtain(ModelDb.Relic<RegalPillow>().ToMutable(),player);
        await RelicCmd.Obtain(ModelDb.Relic<StoneHumidifier>().ToMutable(),player);
        var maxHp=player.Creature.MaxHp;
        await Rest("RegalPillow and StoneHumidifier",rewards:false,expectedHp:49);
        Check(player.Creature.MaxHp==maxHp+5,"sleep max-HP reward applied once");
    }
}
