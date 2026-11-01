using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckForesight0642(RunState state,AdvisorService service)
    {
        var p=service.Player;
        Check(RewardForesightReader.Unsupported(state,p)==null,"baseline Defect forecast supported");
        var before=RewardForesightReader.Fingerprint(state,p);
        for(var i=0;i<6;i++)
        {
            var copy=RewardForesightReader.Clone(state);var cpShop=copy.Players.Single();
            var other=RewardForesightReader.Clone(state);var op=other.Players.Single();
            for(var n=0;n<i;n++){cpShop.PlayerRng.Shops.NextInt(100);op.PlayerRng.Shops.NextInt(100);}
            var predictedShop=RewardForesightReader.GenerateShop(cpShop,"shop",1,1);
            var actualShop=MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory.CreateForNormalMerchant(op).CardEntries.ToArray();
            Check(predictedShop.Cards.Select(c=>c.Id).SequenceEqual(actualShop.Select(e=>e.CreationResult!.Card.Id.ToString()))
                &&predictedShop.Prices!.SequenceEqual(actualShop.Select(e=>e.Cost)),"future shop cards and prices match native inventory "+i);
            Check(JsonSerializer.Serialize(cpShop.PlayerRng.ToSerializable())==JsonSerializer.Serialize(op.PlayerRng.ToSerializable()),"future shop preserves native random-stream consumption "+i);
            Check(RewardForesightReader.GenerateArcaneScroll(cpShop)?.Rarity=="Rare","ancient scroll produces a rare preview "+i);
        }
        Check(before==RewardForesightReader.Fingerprint(state,p),"shop and ancient forecasts preserve real run state");
        for(var index=0;index<18;index++)
        {
            var predicted=RewardForesightReader.Clone(state);
            var actual=RewardForesightReader.Clone(state);
            predicted.CurrentActIndex=index%3;actual.CurrentActIndex=index%3;
            var pp=predicted.Players.Single();var ap=actual.Players.Single();
            // Vary reward RNG offsets and persistent rarity/potion odds across sequences.
            for(var n=0;n<index;n++){pp.PlayerRng.Rewards.NextInt(100);ap.PlayerRng.Rewards.NextInt(100);}
            foreach(var room in new[]{RoomType.Monster,RoomType.Elite,RoomType.Boss})
            {
                var forecast=RewardForesightReader.Generate(predicted,pp,room);
                var encounter=actual.Act.PullNextEncounter(room);
                var rewards=new RewardsSet(ap).WithRewardsFromRoom(new CombatRoom(encounter.ToMutable(),actual));
                await rewards.GenerateWithoutOffering();
                var real=rewards.Rewards.OfType<CardReward>().SelectMany(r=>r.Cards).Select(SnapshotReader.ReadCard).ToArray();
                Check(JsonSerializer.Serialize(forecast,Journal.Json)==JsonSerializer.Serialize(real,Journal.Json),"forecast equals native full reward page "+index+" "+room);
                Check(JsonSerializer.Serialize(pp.PlayerRng.ToSerializable())==JsonSerializer.Serialize(ap.PlayerRng.ToSerializable()),"all player RNG streams align after reward "+index+" "+room);
                Check(JsonSerializer.Serialize(pp.PlayerOdds.ToSerializable())==JsonSerializer.Serialize(ap.PlayerOdds.ToSerializable()),"rarity and potion odds align "+index+" "+room);
            }
        }
        Check(before==RewardForesightReader.Fingerprint(state,p),"54 native comparisons leave real run unchanged");
        var preview=RewardForesightReader.Read(state,p);
        Check(preview.SourceUnchanged&&preview.Branches.Length>0,"actual map has bounded future reward branches: "+preview.Status);
        Check(preview.Branches.Length<=8&&preview.Branches.All(b=>b.Rewards.Length<=3),"bounded forecast size");
        Check(before==RewardForesightReader.Fingerprint(state,p),"full map preview preserves real player, inventory, RNG and encounter pools");
        var clone=RewardForesightReader.Clone(state);var cp=clone.Players.Single();
        cp.AddRelicInternal(ModelDb.Relic<PrayerWheel>().ToMutable(),silent:true);
        Check(RewardForesightReader.Unsupported(clone,cp)!=null,"unmirrored extra rewards explicitly rejected");
        await CardPileCmd.Add(state.CreateCard<Hologram>(p),PileType.Deck);
        NMapScreen.Instance!.Close(false);
        var cards=new CardModel[]{state.CreateCard<AdaptiveStrike>(p),state.CreateCard<Coolheaded>(p),state.CreateCard<GoForTheEyes>(p)};
        var offer=new RewardsSet(p).WithCustomRewards([new CardReward(cards,CardCreationSource.Encounter,p,CardCreationOptions.ForRoom(p,RoomType.Monster))]).Offer();
        await Until(()=>Screen() is NRewardsScreen,"foresight actual reward screen");
        var button=GameActions.Descendants<NRewardButton>((Node)Screen()!).First(b=>b.Reward is CardReward);
        var claim=GameActions.ClaimReward(button);
        await Until(()=>service.Decision?.Kind=="cards","foresight actual choice evaluated");
        Check(service.Decision!.State.Foresight is {SourceUnchanged:true,Branches.Length:>0},"actual advisor attaches selected-route forecast to decision");
        Check(service.Decision.Options.First(o=>o.Key==service.Decision.Evaluation.RecommendedKey).Card?.ClassName=="AdaptiveStrike","native advisor completes available damage engine before more filler");
        await Delay(.3);await Shot0620("0642-foresight");
    }
}
