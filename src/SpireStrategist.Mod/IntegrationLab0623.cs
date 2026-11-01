using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;
using System.IO.Compression;
using System.Text.Json;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckDefect0623(RunState state,AdvisorService service)
    {
        var p=service.Player;var panel=service.Panel;
        await Delay(1.2);
        Check(p.Character.GetType().Name=="Defect","native Defect run created");
        Check(panel.DefectShadow,"Defect shadow mode defaults on");
        service.Autopilot.Enable(true);await Delay(.3);
        Check(!service.Autopilot.Enabled&&!service.Autopilot.FullAutomatic,"Defect takeover cannot be armed");
        ClimbController.SetEnabled(true);
        Check(!ClimbController.Enabled,"Defect continuous climbing cannot be armed");
        var autoButton=(Button)GameActions.Field(panel,"autoButton")!;
        await Delay(1.2);Check(autoButton.Disabled,"Defect takeover UI disabled");
        Check(service.Visuals.RouteSegments==0&&service.Decision==null,"Defect map has no Ironclad route recommendation");
        await Shot0620("defect-01-map");
        NMapScreen.Instance!.Close(false);
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop,showTransition:false);
        NMerchantRoom.Instance!.OpenInventory();await Delay(1.3);
        Check(service.Decision==null&&service.Visuals.VisibleItemKey==null,"Defect shop has no Ironclad purchase recommendation");
        var gold=p.Gold;await Delay(2);Check(p.Gold==gold,"Defect shop does not transact");
        GameActions.Invoke(NMerchantRoom.Instance.Inventory,"Close");
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
        await Delay(1.4);
        Check(service.Decision?.Evaluation.RecommendedKey==null,"Defect rest does not reuse Ironclad upgrade/heal policy");
        await RunManager.Instance.EnterRoomDebug(RoomType.Map,showTransition:false);NMapScreen.Instance!.Close(false);await Delay(.5);
        var manualIds=new List<(string Id,string Key)>();
        async Task Reward(bool skip,bool unknown=false)
        {
            var cards=unknown?new CardModel[]{state.CreateCard<Offering>(p)}:
                new CardModel[]{state.CreateCard<ColdSnap>(p),state.CreateCard<Claw>(p),state.CreateCard<Glacier>(p)};
            var offer=new RewardsSet(p).WithCustomRewards([new CardReward(cards,CardCreationSource.Encounter,p,CardCreationOptions.ForRoom(p,RoomType.Monster))]).Offer();
            await Until(()=>Screen() is NRewardsScreen,"Defect reward screen");
            var button=GameActions.Descendants<NRewardButton>((Node)Screen()!).First(b=>b.Reward is CardReward);
            var claim=GameActions.ClaimReward(button);
            await Until(()=>service.Decision?.Kind=="cards","Defect native reward evaluated");await Delay(.4);
            var d=service.Decision!;
            Check(d.State.Character=="Defect"&&d.Evaluation.PolicyVersion==DefectPolicy.Version,"Defect reward uses new policy and native snapshot");
            if(!unknown)
            {
                Check(d.Options.Count(o=>o.Card!=null)==3&&d.Evaluation.RecommendedKey!=null,"all native offered cards recorded and scored");
                if(panel.DefectShadow)
                {
                    Check(service.Visuals.VisibleItemKey==null,"shadow hides recommendation frame");
                    var body=(RichTextLabel)GameActions.Field(panel,"body")!;
                    Check(!body.Text.Contains(cards[0].Title),"shadow hides card scores");
                    await Shot0620("defect-02-shadow");
                    panel.SetDefectShadow(false);await Delay(.4);
                    Check(!AdvisorDisplaySettings.Load().DefectShadow,"shadow setting persisted");
                    Check(service.Decision!.Id==d.Id,"display toggle keeps the same decision identity");
                    await Shot0620("defect-03-advice");
                    if(System.Environment.GetEnvironmentVariable("SPIRE_STRATEGIST_RENDER_UI")=="1")
                        Check(service.Visuals.VisibleItemKey==d.Evaluation.RecommendedKey,"Defect frame matches recommendation");
                }
            }
            else Check(d.Evaluation.RecommendedKey==null&&d.Evaluation.Options.Any(o=>!o.Supported),"unknown reward abstains instead of applying Ironclad model");
            var before=p.Deck.Cards.Count;
            if(skip)
            {
                var choice=d.Options.First(o=>o.Kind=="skip");manualIds.Add((d.Id,choice.Key));
                GameActions.SelectSkip(service.CardScreen!,int.Parse(choice.Key.Split(':')[1]));
            }
            else
            {
                // Deliberately choose the lower-scored Claw: record behavior, never force advice.
                manualIds.Add((d.Id,"card:1"));GameActions.SelectCard(service.CardScreen!,cards[1]);
            }
            await Until(()=>claim.IsCompleted,"manual Defect choice resolves");await claim;
            Check(p.Deck.Cards.Count==before+(skip?0:1),"manual card choice changes deck exactly once");
            if(Screen() is NRewardsScreen)RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
            await offer;await Delay(.5);
        }
        await Reward(false);await Reward(true);await Reward(true,true);
        panel.SetDefectShadow(true);Check(AdvisorDisplaySettings.Load().DefectShadow,"shadow setting can be restored");
        var journal=(Journal)GameActions.Field(service,"journal")!;
        var log=Path.Combine(journal.DirectoryPath,Directory.GetFiles(journal.DirectoryPath,"session-*.jsonl").Select(Path.GetFileName).Single()!);
        var entries=File.ReadLines(log).Select(line=>JsonDocument.Parse(line)).ToArray();
        try
        {
            foreach(var (id,key) in manualIds)
                Check(entries.Count(e=>e.RootElement.GetProperty("type").GetString()=="choice"&&
                    e.RootElement.GetProperty("payload").GetProperty("decisionId").GetString()==id&&
                    e.RootElement.GetProperty("payload").GetProperty("key").GetString()==key)==1,"actual manual choice or skip logged once: "+key);
            Check(!entries.Any(e=>e.RootElement.GetProperty("type").GetString() is "shop_preview" or "event_decision"),"no Ironclad shop/event evaluations leaked into Defect log");
        }
        finally{foreach(var entry in entries)entry.Dispose();}
        service.RecordRunResult(true);service.RecordRunResult(false);service.RecordRunResult(true);
        var terminal=File.ReadLines(log).Select(line=>JsonDocument.Parse(line)).ToArray();
        try
        {
            var results=terminal.Where(e=>e.RootElement.GetProperty("type").GetString()=="run_result").ToArray();
            Check(results.Length==1&&results[0].RootElement.GetProperty("payload").GetProperty("victory").GetBoolean(),"native cleanup cannot overwrite or double-count first terminal result");
        }
        finally{foreach(var item in terminal)item.Dispose();}
        var zipPath=journal.ExportFeedback(new{character="Defect",test=true});
        using var zip=ZipFile.OpenRead(zipPath);
        Check(zip.Entries.Any(e=>e.Name.EndsWith(".jsonl")),"feedback ZIP contains Defect session log");
        await Shot0620("defect-04-finished");
    }
}
