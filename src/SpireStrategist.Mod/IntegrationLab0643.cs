using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Rewards;
namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckPotions0643(RunState state,AdvisorService service)
    {
        var factory=typeof(ModelDb).GetMethods().Single(m=>m.Name=="Potion"&&m.IsGenericMethod&&m.GetParameters().Length==0);
        var catalogue=typeof(PotionModel).Assembly.GetTypes().Where(t=>t.Namespace=="MegaCrit.Sts2.Core.Models.Potions"&&!t.IsAbstract&&t.IsSubclassOf(typeof(PotionModel)))
            .Select(t=>(PotionModel)factory.MakeGenericMethod(t).Invoke(null,null)!)
            .Select(p=>new{name=p.GetType().Name,title=p.Title.GetFormattedText(),rarity=p.Rarity.ToString(),description=p.DynamicDescription.GetFormattedText()}).ToArray();
        System.IO.File.WriteAllText(System.IO.Path.Combine(Godot.OS.GetUserDataDir(),"potions-native.json"),System.Text.Json.JsonSerializer.Serialize(catalogue,Journal.Json));
        var p=service.Player;var auto=service.Autopilot;NMapScreen.Instance!.Close(false);
        // Native skipped rewards append to the current room's run history.
        if(state.CurrentMapPointHistoryEntry==null)
            state.AppendToMapPointHistory(MegaCrit.Sts2.Core.Map.MapPointType.Monster,MegaCrit.Sts2.Core.Rooms.RoomType.Monster,null);
        foreach(var old in p.Potions.ToArray())await PotionCmd.Discard(old);
        while(p.PotionSlots.Any(x=>x==null))await PotionCmd.TryToProcure(ModelDb.Potion<FirePotion>().ToMutable(),p);
        async Task Offer(PotionModel potion)
        {
            var task=new RewardsSet(p).WithCustomRewards([new PotionReward(potion,p)]).Offer();
            await Until(()=>Screen() is NRewardsScreen,"potion reward displayed");auto.Enable(true);
            await Until(()=>task.IsCompleted,"potion reward resolved",35);await task;auto.Pause("fixture boundary");
        }
        var block=ModelDb.Potion<BlockPotion>().ToMutable();await Offer(block);
        Check(p.Potions.Contains(block),"same tier defense replaces attack on full belt");
        var rainbow=ModelDb.Potion<EntropicBrew>().ToMutable();
        Check(rainbow.Title.GetFormattedText().Contains("混沌"),"native Entropic Brew identity confirmed as 混沌药水");
        await Offer(rainbow);Check(p.Potions.Contains(rainbow),"rainbow acquired on full belt");
        Check(p.Potions.Contains(block),"lowest-ranked attack was discarded before defense");
        var before=p.PotionSlots.ToArray();await Offer(ModelDb.Potion<FirePotion>().ToMutable());
        Check(p.PotionSlots.SequenceEqual(before),"lower reward cannot displace rainbow or defensive potion");
    }
}
