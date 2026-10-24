using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

internal sealed partial class PartialAutopilot
{
    private bool TickPriorityShop(NMerchantRoom room,NMerchantSlot[] slots,RunSnapshot state)
    {
        var relics=slots.Where(s=>s.Entry is MerchantRelicEntry {Model:not null})
            .Select(s=>{var e=(MerchantRelicEntry)s.Entry;return new ShopRelic(s.GetInstanceId().ToString(),e.Model!.Id.Entry,e.Model.Title.GetFormattedText(),e.Cost);}).ToArray();
        var cards=slots.Where(s=>s.Entry is MerchantCardEntry {CreationResult:not null})
            .Select(s=>new ShopCard(s.GetInstanceId().ToString(),SnapshotReader.ReadCard(((MerchantCardEntry)s.Entry).CreationResult!.Card),s.Entry.Cost)).ToArray();
        var plan=PriorityShopPolicy.Evaluate(state,relics,cards,shopBoughtCards);
        if(plan==null)return false;
        var key=plan.Card?.Key??plan.Relic.Key;
        var slot=slots.Single(s=>s.GetInstanceId().ToString()==key);
        var choice=new ShopChoice(key,plan.Card?.Card.Name??plan.Relic.Name,plan.Card!=null?"card":"relic",
            slot.Entry.Cost,100,[plan.Reason,$"本次计划总价 {plan.TotalPrice}，现款 {state.Gold}"]);
        service.ShowShop(state,new(choice,[choice],0,[plan.Reason,"优先清单执行后重新评估删牌、买牌；随机遗物仍保留预算。"]));
        Wait($"接管中 · 优先购买 {choice.Name}（{choice.Price} 金币）");
        if(Claim($"priority-shop:{room.GetInstanceId()}:{key}:{state.Gold}:{choice.Price}",slot.Entry.EnoughGold,8000))
        {
            service.RecordAuto("shop_priority",new{state,plan,choice});
            Track(()=>BuyShopSlot(slot,plan.Card!=null));
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
            .OrderBy(s=>((MerchantRelicEntry)s.Entry).Model!.Id.ToString(),StringComparer.Ordinal).ToArray();
        if(relics.Length>0)
        {
            var fingerprint=string.Join("|",relics.Select(s=>((MerchantRelicEntry)s.Entry).Model!.Id+":"+s.Entry.Cost));
            var index=ClimbController.RandomIndex(shopRandomSalt+":"+state.Floor+":"+state.Gold+":"+fingerprint,relics.Length);
            var slot=relics[index];
            var choice=new ShopChoice(slot.GetInstanceId().ToString(),((MerchantRelicEntry)slot.Entry).Model!.Title.GetFormattedText(),"relic",slot.Entry.Cost,0,[$"预算剩余步骤：随机遗物，保留 {reserve} 金币"]);
            service.ShowShop(state,new(choice,[choice],reserve,["此处为随机预算购买，不代表遗物强度排名。"]));
            Wait($"接管中 · 随机购买遗物（保留 {reserve} 金；可停止接管）");
            if(Claim("climb-relic:"+room.GetInstanceId()+":"+state.Gold+":"+fingerprint,delay:8000))
            {
                service.RecordAuto("climb_random_relic",new{state,reserve,index,shopRandomSalt,looping=ClimbController.Enabled,
                    candidates=relics.Select(s=>new{id=((MerchantRelicEntry)s.Entry).Model!.Id.ToString(),cost=s.Entry.Cost})});
                Track(()=>BuyShopSlot(slot,false));
            }
            return;
        }
        var potion=state.PotionSlots.Any(p=>p==null)&&!state.Relics.Contains("RELIC.SOZU")
            ?slots.Where(s=>s.Entry is MerchantPotionEntry&&s.Entry.Cost<=state.Gold-reserve&&s.Entry.EnoughGold)
                .OrderBy(s=>s.Entry.Cost).ThenBy(s=>s.GetInstanceId()).FirstOrDefault():null;
        if(potion!=null)
        {
            var choice=new ShopChoice(potion.GetInstanceId().ToString(),"补充药水","potion",potion.Entry.Cost,0,["最后补充空药水栏"]);
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
