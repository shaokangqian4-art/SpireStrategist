using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SpireStrategist.Core;
using MegaCrit.Sts2.Core.Models.Potions;

namespace SpireStrategist.Mod;

internal sealed partial class PartialAutopilot
{
    private int shopFoulUses;
    private bool TickFoulMerchant(NMerchantRoom room)
    {
        // Only called for a real MerchantRoom. FakeMerchant can start combat and
        // must keep its separate risk assessment. Use the native action queue.
        var potion=service.Player.Potions.OfType<FoulPotion>().FirstOrDefault();
        if(potion==null)return false;
        if(potion.IsQueued){Wait("接管中 · 等待污浊药水兑换完成");return true;}
        if(room.Inventory.IsOpen)
        {
            Wait("接管中 · 关闭商品界面，准备向普通商人投掷污浊药水");
            if(Claim($"foul-close:{room.GetInstanceId()}:{shopFoulUses}",GameActions.Field(room.Inventory,"_isInputBlocked") is not true))
                GameActions.Invoke(room.Inventory,"Close");
            return true;
        }
        Wait("接管中 · 向普通商人投掷污浊药水，随后重新评估商店");
        if(Claim($"foul-use:{room.GetInstanceId()}:{shopFoulUses}",potion.PassesCustomUsabilityCheck&&room.MerchantButton.IsEnabled))
        {
            service.RecordAuto("shop_foul_potion",new{state=SnapshotReader.Read(service.State,service.Player),sequence=shopFoulUses,source="native potion action"});
            potion.EnqueueManualUse(null);
            shopFoulUses++;
            leavingShop=null;
        }
        return true;
    }

    private bool TickPriorityShop(NMerchantRoom room,NMerchantSlot[] slots,RunSnapshot state)
    {
        var relics=slots.Where(s=>s.Entry is MerchantRelicEntry {Model:not null})
            .Select(s=>{var e=(MerchantRelicEntry)s.Entry;return new ShopRelic(s.GetInstanceId().ToString(),e.Model!.Id.Entry,e.Model.Title.GetFormattedText(),e.Cost);}).ToArray();
        var cards=slots.Where(s=>s.Entry is MerchantCardEntry {CreationResult:not null})
            .Select(s=>new ShopCard(s.GetInstanceId().ToString(),SnapshotReader.ReadCard(((MerchantCardEntry)s.Entry).CreationResult!.Card),s.Entry.Cost)).ToArray();
        var reserve=service.State.CurrentMapPoint is {} point?ShopBudget.Reserve(state,RouteAdapter.Nodes(service.State),RouteAdapter.Key(point),ClimbController.RelicReserve):ClimbController.RelicReserve;
        var plan=PriorityShopPolicy.Evaluate(state,relics,cards,shopBoughtCards,
            slots.FirstOrDefault(s=>s.Entry is MerchantCardRemovalEntry)?.Entry.Cost,reserve);
        if(plan==null)return false;
        var key=plan.FirstCard?.Key??plan.Relic.Key;
        var slot=slots.Single(s=>s.GetInstanceId().ToString()==key);
        var choice=new ShopChoice(key,plan.FirstCard?.Card.Name??plan.Relic.Name,plan.FirstCard!=null?"card":"relic",
            slot.Entry.Cost,100,[plan.Reason,$"本次计划总价 {plan.TotalPrice}，现款 {state.Gold}"]);
        service.ShowShop(state,new(choice,[choice],0,[plan.Reason,"优先清单执行后重新评估删牌、买牌；随机遗物仍保留预算。"]));
        Wait($"接管中 · 优先购买 {choice.Name}（{choice.Price} 金币）");
        if(Claim($"priority-shop:{room.GetInstanceId()}:{key}:{state.Gold}:{choice.Price}",slot.Entry.EnoughGold,8000))
        {
            service.RecordAuto("shop_priority",new{state,plan,choice});
            Track(()=>BuyShopSlot(slot,plan.FirstCard!=null));
        }
        return true;
    }
    private ulong? leavingShop;
    private readonly string shopRandomSalt=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
    private void TickClimbShop(NMerchantRoom room,NMerchantInventory inventory,NMerchantSlot[] slots,RunSnapshot state)
    {
        var nodes=RouteAdapter.Nodes(service.State);
        var reserve=service.State.CurrentMapPoint is {} point?
            ShopBudget.Reserve(state,nodes,RouteAdapter.Key(point),ClimbController.RelicReserve):ClimbController.RelicReserve;
        var relics=slots.Where(s=>s.Entry is MerchantRelicEntry {Model:not null}&&s.Entry.Cost<=state.Gold-reserve&&s.Entry.EnoughGold)
            .Where(s=>RelicValue.Shop(((MerchantRelicEntry)s.Entry).Model!.Id.Entry,state) is not {} model||model.Value-new StrategicValue(state).Gold(s.Entry.Cost)>0)
            .OrderBy(s=>((MerchantRelicEntry)s.Entry).Model!.Id.ToString(),StringComparer.Ordinal).ToArray();
        if(relics.Length>0)
        {
            var fingerprint=string.Join("|",relics.Select(s=>((MerchantRelicEntry)s.Entry).Model!.Id+":"+s.Entry.Cost));
            var index=ClimbController.RandomIndex(shopRandomSalt+":"+state.Floor+":"+state.Gold+":"+fingerprint,relics.Length);
            var modeled=relics.Select((s,i)=>(Index:i,Value:RelicValue.Shop(((MerchantRelicEntry)s.Entry).Model!.Id.Entry,state)?.Value-new StrategicValue(state).Gold(s.Entry.Cost)))
                .Where(x=>x.Value>0).OrderByDescending(x=>x.Value).ThenBy(x=>x.Index).FirstOrDefault();
            var ranked=modeled.Value.HasValue;
            if(ranked)index=modeled.Index;
            var slot=relics[index];
            var reason=ranked?"按已建模遗物的构筑收益与价格选择":"此处为随机预算购买，不代表遗物强度排名。";
            var choice=new ShopChoice(slot.GetInstanceId().ToString(),((MerchantRelicEntry)slot.Entry).Model!.Title.GetFormattedText(),"relic",slot.Entry.Cost,modeled.Value??0,[reason,$"预算剩余步骤：随机遗物，保留 {reserve} 金币"]);
            service.ShowShop(state,new(choice,[choice],reserve,[reason]));
            Wait(ranked?"接管中 · 购买适配当前构筑的遗物":$"接管中 · 随机购买遗物（保留 {reserve} 金；可停止接管）");
            if(Claim("climb-relic:"+room.GetInstanceId()+":"+state.Gold+":"+fingerprint,delay:8000))
            {
                service.RecordAuto(ranked?"climb_modeled_relic":"climb_random_relic",new{state,reserve,index,shopRandomSalt,looping=ClimbController.Enabled,score=modeled.Value,
                    candidates=relics.Select(s=>new{id=((MerchantRelicEntry)s.Entry).Model!.Id.ToString(),cost=s.Entry.Cost})});
                Track(()=>BuyShopSlot(slot,false));
            }
            return;
        }
        var potionPlan=slots.Where(s=>s.Entry is MerchantPotionEntry {Model:not null})
            .Select(s=>(Slot:s,Choice:ShopAdvice.Potion(state,s,reserve))).Where(x=>x.Choice.Eligible&&x.Choice.Score>0)
            .OrderByDescending(x=>x.Choice.Score).ThenBy(x=>x.Choice.Price).FirstOrDefault();
        var potion=potionPlan.Slot;
        if(potion!=null)
        {
            var choice=potionPlan.Choice;
            service.ShowShop(state,new(choice,[choice],reserve,[]));
            Wait("接管中 · 最后补充空药水栏（可停止接管）");
            if(Claim("climb-potion:"+room.GetInstanceId()+":"+potion.GetInstanceId()+":"+state.Gold,delay:8000))
            {service.RecordAuto("climb_buy_potion",new{state,reserve,price=potion.Entry.Cost});Track(()=>BuyShopSlot(potion,false));}
            return;
        }
        Wait("接管中 · 商店处理完成，离开");
        if(Claim("climb-shop-close:"+room.GetInstanceId()))
        {
            leavingShop=room.GetInstanceId();
            GameActions.Invoke(inventory,"Close");
        }
    }
}
