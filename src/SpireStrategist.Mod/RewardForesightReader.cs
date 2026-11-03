using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

// No live reward generation, seed rewind, room entry or player action. All generation
// below is on newly deserialized models. No After* hook or UI command is executed.
internal static class RewardForesightReader
{
    internal const string Conditions="辉眼：沿推荐路线预览至多三次战斗奖励或商店供牌；商店按现款约束。未到达的事件、先古及后续交易尚未模拟，不保证获得。";
    internal static string Fingerprint(IRunState run,Player player)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new{player=player.ToSerializable(),rng=run.Rng.ToSerializable(),odds=run.Odds.ToSerializable(),
            act=run.CurrentActIndex,coord=run.CurrentMapCoord,acts=run.Acts.Select(a=>a.ToSave()).ToArray(),
            sharedRelics=run.SharedRelicGrabBag.ToSerializable()},Journal.Json))));

    internal static RunState Clone(IRunState run)=>RunState.FromSerializable(new SerializableRun
    {
        Players=run.Players.Select(p=>p.ToSerializable()).ToList(),
        Acts=run.Acts.Select(a=>a.ToSave()).ToList(), CurrentActIndex=run.CurrentActIndex,
        Modifiers=[],GameMode=run.GameMode,Ascension=run.AscensionLevel,
        SerializableRng=run.Rng.ToSerializable(),SerializableOdds=run.Odds.ToSerializable(),
        SerializableSharedRelicGrabBag=run.SharedRelicGrabBag.ToSerializable()
    });

    // Reward-affecting relics must be audited individually before support. Refuse
    // unsupported behavior rather than silently ignoring it and issuing exact claims.
    internal static string? Unsupported(IRunState run,Player player)
    {
        if(run.Players.Count!=1||SnapshotReader.IsMultiplayer(run)||run.Modifiers.Count>0)return "辉眼暂不可用：多人或特殊模式修改器。";
        var native=typeof(CardModel).Assembly;
        var models=player.Relics.Cast<AbstractModel>().Concat(player.Deck.Cards)
            .Concat(player.Deck.Cards.Select(c=>(AbstractModel?)c.Enchantment).OfType<AbstractModel>())
            .Concat(player.Deck.Cards.Select(c=>(AbstractModel?)c.Affliction).OfType<AbstractModel>());
        foreach(var model in models)
        {
            if(model.GetType().Assembly!=native)return "辉眼暂不可用：存在未适配的第三方卡牌或遗物。";
            // The one-shot reward modifier has already been consumed on the live reward.
            if(model is MegaCrit.Sts2.Core.Models.Relics.SilkenTress {IsUsedUp:true})continue;
            foreach(var method in model.GetType().GetMethods())
                if(method.DeclaringType==model.GetType()&&method.IsVirtual&&
                    (method.Name.Contains("Merchant")||method.Name.Contains("CardReward")||method.Name.Contains("ModifyRewards")||
                     method.Name.Contains("AfterModifyingRewards")))
                    return "辉眼暂不可用：奖励修改尚未适配（"+model.GetType().Name+"）。";
        }
        return null;
    }

    internal static CardSnapshot[] Generate(RunState copy,Player player,RoomType room)
    {
        var encounter=copy.Act.PullNextEncounter(room);
        // RewardsSet.WithRewardsFromRoom suppresses all final-act boss rewards.
        if(room==RoomType.Boss&&copy.CurrentActIndex>=copy.Acts.Count-1)return [];
        // Native RewardsSet rolls potion odds while assembling the reward list,
        // then populates gold -> potion (if any) -> cards -> elite relic.
        var potion=player.PlayerOdds.PotionReward.Roll(player,room);
        new GoldReward(encounter.MinGoldReward,encounter.MaxGoldReward,player).Populate();
        if(potion)new PotionReward(player).Populate();
        var options=CardCreationOptions.ForRoom(player,room).WithFlags(CardCreationFlags.IsFromCombat|CardCreationFlags.NoModifyHooks);
        var cards=CardFactory.CreateForReward(player,3,options).Select(c=>SnapshotReader.ReadCard(c.Card)).ToArray();
        if(room==RoomType.Elite)new RelicReward(player).Populate();
        return cards;
    }

    internal static RewardForesight Read(IRunState run,Player player,string[]? preferredPath=null,bool afterArcaneScroll=false)
    {
        var stamp=Fingerprint(run,player);
        try
        {
            if(Unsupported(run,player) is {} reason)return new(stamp,true,[],reason);
            var nodes=RouteAdapter.Nodes(run).ToDictionary(n=>n.Id);
            var start=RouteAdapter.Key(run.CurrentMapPoint??run.Map.StartingMapPoint);
            var queue=new Queue<(string Id,List<(RouteNode Node,int Distance,double Reliability)> Fights,string Path,int Distance,bool Uncertain)>();
            var allowed=preferredPath?.ToHashSet();
            foreach(var child in preferredPath is {Length:>0}?[preferredPath[0]]:nodes[start].Children)queue.Enqueue((child,[],child,1,false));
            var paths=new List<(string Path,List<(RouteNode Node,int Distance,double Reliability)> Fights)>();
            var visited=0;
            while(queue.Count>0&&visited++<160&&paths.Count<12)
            {
                var entry=queue.Dequeue();if(!nodes.TryGetValue(entry.Id,out var node))continue;
                var fights=entry.Fights.ToList();var uncertain=entry.Uncertain;
                if(node.Kind is "Monster" or "Elite" or "Boss" or "Shop")
                    fights.Add((node,entry.Distance,(uncertain?.45:.85)*Math.Pow(.7,fights.Count)));
                else if(node.Kind!="RestSite")uncertain=true;
                var children=node.Children.Where(c=>allowed==null||allowed.Contains(c)).ToArray();
                if(fights.Count==3||entry.Distance>=8||children.Length==0||node.Kind is "Boss" or "MiniBoss")
                {if(fights.Count>0)paths.Add((entry.Path,fights));continue;}
                if(node.Kind is "Elite" or "Shop")uncertain=true; // acquisitions can change later rewards
                foreach(var child in children)queue.Enqueue((child,fights,entry.Path+">"+child,entry.Distance+1,uncertain));
            }
            var branches=new List<RewardBranch>();
            foreach(var path in paths.GroupBy(p=>string.Join('/',p.Fights.Select(f=>f.Node.Kind))).Select(g=>g.OrderByDescending(p=>p.Fights.Sum(f=>f.Reliability)).First()).Take(8))
            {
                var copy=Clone(run);var p=copy.Players.Single(x=>x.NetId==player.NetId);
                if(afterArcaneScroll)GenerateArcaneScroll(p);
                var rewards=path.Fights.Select(f=>f.Node.Kind=="Shop"?GenerateShop(p,f.Node.Id,f.Distance,f.Reliability):
                    new FutureReward(f.Node.Id,f.Node.Kind,f.Distance,f.Reliability,
                    Generate(copy,p,Enum.Parse<RoomType>(f.Node.Kind)))).Where(r=>r.Cards.Length>0).ToArray();
                if(rewards.Length>0)branches.Add(new(path.Path,rewards));
            }
            if(stamp!=Fingerprint(run,player))return new(stamp,false,[],"辉眼已失效：读取期间真实状态变化。");
            return new(stamp,true,branches.ToArray(),branches.Count>0?Conditions:"辉眼：当前幕内没有可预览的后续战斗奖励。");
        }
        catch(Exception ex){return new(stamp,stamp==Fingerprint(run,player),[],"辉眼暂不可用："+ex.GetBaseException().Message);}
    }
    internal static FutureReward GenerateShop(Player copy,string node,int distance,double reliability)
    {
        var inventory=MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory.CreateForNormalMerchant(copy);
        var entries=inventory.CardEntries.Where(e=>e.CreationResult!=null).ToArray();
        return new(node,"Shop",distance,reliability,entries.Select(e=>SnapshotReader.ReadCard(e.CreationResult!.Card)).ToArray(),entries.Select(e=>e.Cost).ToArray());
    }
    internal static CardSnapshot? GenerateArcaneScroll(Player copy)
    {
        var options=new CardCreationOptions([copy.Character.CardPool],CardCreationSource.Other,CardRarityOddsType.Uniform,c=>c.Rarity==CardRarity.Rare)
            .WithFlags(CardCreationFlags.NoUpgradeRoll);
        return CardFactory.CreateForReward(copy,1,options).Select(c=>SnapshotReader.ReadCard(c.Card)).FirstOrDefault();
    }
}
