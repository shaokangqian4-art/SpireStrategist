using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal sealed partial class PartialAutopilot
{
    private Task? eventAction;
    private NCardBundle? plannedBundle;
    private bool TickCustomEvent(object? screen)
    {
        if(eventAction is {IsCompleted:false}){Wait("接管中 · 等待事件动作完成");return true;}
        if(eventAction is {IsCompleted:true}){var task=eventAction;eventAction=null;task.GetAwaiter().GetResult();}
        if(screen is NFakeMerchant fake)
        {
            ownedEvent=GameActions.Field(fake,"_event") as FakeMerchant;
            if(ownedEvent==null)return false;
            var state=SnapshotReader.Read(service.State,service.Player);var value=new StrategicValue(state);
            var potion=service.Player.Potions.OfType<FoulPotion>().FirstOrDefault(p=>p.PassesCustomUsabilityCheck);
            var expectedLoot=((FakeMerchant)ownedEvent).Inventory.RelicEntries.Where(e=>e.Model!=null).Sum(e=>Math.Max(0,RelicValue.Evaluate(e.Model!.Id.Entry,state).Value));
            // Potion targeting is only legal while the inventory is CLOSED.
            if(potion!=null&&value.Ratio>=.8&&value.Attack>=8&&value.Block>=5&&value.Draw>=2&&expectedLoot>22)
            {
                Wait("接管中 · 向假商人投掷污浊药水（采用保守战损先验）");
                if(!potion.IsQueued&&Claim("fake-fight:"+fake.GetInstanceId(),fake.MerchantButton.IsEnabled))
                {service.RecordAuto("fake_combat_prior",new{state.Hp,attack=value.Attack,block=value.Block,draw=value.Draw,expectedLoot});potion.EnqueueManualUse(null);}
                return true;
            }
            Wait("接管中 · 查看假商人陈列");
            if(Claim("fake-open:"+fake.GetInstanceId(),fake.MerchantButton.IsEnabled))GameActions.Invoke(fake,"OpenInventory");
            return true;
        }
        if(screen is NFakeMerchantInventory inventory && NEventRoom.Instance?.CustomEventNode is NFakeMerchant merchant)
        {
            ownedEvent=GameActions.Field(merchant,"_event") as FakeMerchant;
            var s=SnapshotReader.Read(service.State,service.Player);var v=new StrategicValue(s);
            var options=GameActions.Descendants<NMerchantRelic>(inventory).Where(n=>n.IsVisibleInTree()&&n.Entry is MerchantRelicEntry {Model:not null}&&n.Entry.EnoughGold)
                .Select(n=>(Slot:n,Entry:(MerchantRelicEntry)n.Entry,Value:RelicValue.Evaluate(((MerchantRelicEntry)n.Entry).Model!.Id.Entry,s).Value-v.Gold(n.Entry.Cost)))
                .OrderByDescending(x=>x.Value).ToArray();
            var best=options.FirstOrDefault();
            if(best.Slot!=null&&best.Value>2)
            {
                Wait("接管中 · 假商人购买 "+best.Entry.Model!.Title.GetFormattedText());
                if(Claim($"fake-buy:{inventory.GetInstanceId()}:{best.Slot.GetInstanceId()}:{s.Gold}"))
                {service.RecordAuto("fake_purchase",new{relic=best.Entry.Model.Id.ToString(),price=best.Entry.Cost,score=best.Value});eventAction=BuyShopSlot(best.Slot,false);}
            }
            else
            {
                Wait("接管中 · 假商人购买结束，离开");
                if(Claim("fake-leave:"+inventory.GetInstanceId())){GameActions.Invoke(inventory,"Close");GameActions.Invoke(merchant,"HideScreen",GameActions.Field(merchant,"_proceedButton"));}
            }
            return true;
        }
        if(!OwnsCurrentEvent&&!OwnsPickup)return false;
        if(screen is NCrystalSphereScreen crystal){TickCrystal(crystal);return true;}
        if(screen is NChooseACardSelectionScreen choose)
        {
            if(GameActions.Field(choose,"_screenComplete") is true)return true;
            var v=new StrategicValue(SnapshotReader.Read(service.State,service.Player));
            var best=GameActions.Descendants<NGridCardHolder>(choose).Where(h=>h.IsVisibleInTree()).OrderByDescending(h=>v.Add(SnapshotReader.ReadCard(h.CardModel))).FirstOrDefault();
            if(best==null)return true;
            var skip=GameActions.Field(choose,"_canSkip") is true && (eventSelection=="skip-foreign"||v.Add(SnapshotReader.ReadCard(best.CardModel))<=0);
            Wait("接管中 · "+(skip?"跳过事件赠牌":"选择事件赠牌 "+best.CardModel.Title));
            if(Claim("event-offer:"+choose.GetInstanceId()))
            {if(skip)GameActions.Invoke(choose,"OnSkipButtonReleased",GameActions.Field(choose,"_skipButton"));else GameActions.Invoke(choose,"SelectHolder",best);}
            return true;
        }
        if(screen is NChooseABundleSelectionScreen bundle)
        {
            var selected=GameActions.Field(bundle,"_selectedBundle") as NCardBundle;
            if(selected!=null)
            {
                if(selected!=plannedBundle){Manual("检测到人工选择的卡包，请手动确认。");return true;}
                var confirm=(NClickableControl)GameActions.Field(bundle,"_previewConfirmButton")!;
                Wait("接管中 · 确认事件卡包");
                if(Claim("bundle-confirm:"+bundle.GetInstanceId(),confirm.IsEnabled&&confirm.IsVisibleInTree()))GameActions.Invoke(bundle,"ConfirmSelection",confirm);
            }
            else
            {
                var v=new StrategicValue(SnapshotReader.Read(service.State,service.Player));
                var best=GameActions.Descendants<NCardBundle>(bundle).Where(n=>n.IsVisibleInTree()).OrderByDescending(n=>n.Bundle.Sum(c=>v.Add(SnapshotReader.ReadCard(c)))).FirstOrDefault();
                Wait("接管中 · 评估完整卡包的收益和冗余");
                if(best!=null&&Claim("bundle-select:"+bundle.GetInstanceId())){plannedBundle=best;GameActions.Invoke(bundle,"OnBundleClicked",best);}
            }
            return true;
        }
        if(screen is NChooseARelicSelection relics)
        {
            if(GameActions.Field(relics,"_screenComplete") is true)return true;
            var s=SnapshotReader.Read(service.State,service.Player);
            var best=GameActions.Descendants<NRelicBasicHolder>(relics).Where(n=>n.IsVisibleInTree()).OrderByDescending(n=>RelicValue.Evaluate(n.Relic.Model.Id.Entry,s).Value).FirstOrDefault();
            if(best==null)return true;
            Wait("接管中 · 选择事件遗物 "+best.Relic.Model.Title.GetFormattedText());
            if(Claim("event-relic:"+relics.GetInstanceId()))GameActions.Invoke(relics,"SelectHolder",best);
            return true;
        }
        return false;
    }
    private bool LeaveHarmfulFakeRelic(MegaCrit.Sts2.Core.Rewards.Reward? reward)=>reward is MegaCrit.Sts2.Core.Rewards.RelicReward {Relic:not null} r&&
        r.Relic.Id.Entry=="FAKE_SNECKO_EYE"&&RelicValue.Evaluate(r.Relic.Id.Entry,SnapshotReader.Read(service.State,service.Player)).Value<0;
    private void TickCrystal(NCrystalSphereScreen screen)
    {
        if(GameActions.Field(screen,"_entity") is not CrystalSphereMinigame game || !game.PlacedAllItems){Wait("接管中 · 等待水晶球生成");return;}
        if(game.IsFinished)
        {Proceed(screen,(NClickableControl)GameActions.Field(screen,"_proceedButton")!,"OnProceedButtonPressed");return;}
        var v=new StrategicValue(SnapshotReader.Read(service.State,service.Player));
        double Worth(CrystalSphereItem item)=>item.GetType().Name switch
        {"CrystalSphereRelic"=>10,"CrystalSphereCardReward"=>4,"CrystalSpherePotion"=>v.Potion(1),"CrystalSphereGold"=>v.Gold(item.Size.X==2?30:10),_=>-30};
        var hidden=game.cells.Cast<CrystalSphereCell>().Where(c=>c.IsHidden).ToArray();
        // Bounded greedy coverage of an already generated board; never roll rewards or search future runs.
        var candidates=from cell in hidden from big in new[]{false,true}
            let affected=hidden.Where(c=>big?Math.Abs(c.X-cell.X)<=1&&Math.Abs(c.Y-cell.Y)<=1:c==cell).ToArray()
            where !affected.Any(c=>c.Item is {IsGood:false})
            let score=game.Items.Where(i=>i.IsGood).Sum(i=>
            {
                var remaining=hidden.Where(c=>c.Item==i).ToArray();
                if(remaining.Length==0)return 0d;
                var hit=affected.Count(c=>c.Item==i);var after=remaining.Length-hit;
                if(hit==0||after>9*(game.DivinationCount-1))return 0d;
                return Worth(i)*hit/remaining.Length*(after==0?2:1);
            })
            orderby score descending, affected.Length descending, cell.X,cell.Y
            select new{cell,big,score};
        var target=candidates.FirstOrDefault();
        if(target==null){Manual("水晶球剩余操作无法避开诅咒，请手动决定。");return;}
        var node=GameActions.Descendants<NCrystalSphereCell>(screen).FirstOrDefault(n=>n.Entity==target.cell);
        Wait("接管中 · 水晶球读取已生成布局，优先收益并避开诅咒");
        if(node!=null&&Claim($"crystal:{screen.GetInstanceId()}:{game.DivinationCount}"))
        {
            GameActions.Invoke(screen,target.big?"SetBigDivination":"SetSmallDivination",GameActions.Field(screen,target.big?"_bigDivinationButton":"_smallDivinationButton"));
            service.RecordAuto("crystal",new{x=target.cell.X,y=target.cell.Y,target.big,target.score,remaining=game.DivinationCount});
            eventAction=(Task)GameActions.Invoke(screen,"OnCellClicked",node)!;
        }
    }
}
