using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;
internal static partial class AutopilotLab
{
    private static async Task CheckCamps0647(RunState state,AdvisorService service)
    {
        var p=service.Player;var auto=service.Autopilot;
        NMapScreen.Instance!.Close(false);
        foreach(var c in p.Deck.Cards.ToArray())await CardPileCmd.RemoveFromDeck(c,false);
        foreach(var c in new CardModel[]{state.CreateCard<Darkness>(p),state.CreateCard<Storm>(p),state.CreateCard<Defragment>(p),state.CreateCard<Coolheaded>(p)})
            await CardPileCmd.Add(c,PileType.Deck);
        var s=SnapshotReader.Read(state,p);
        Check(ApotheosisPolicy.PendingCores(s)==4&&RelicValue.Evaluate("JEWELRY_BOX",s).Value>0,"Jewelry Box uses native core upgrade demand");
        await RelicCmd.Obtain(ModelDb.Relic<JewelryBox>().ToMutable(),p);
        var apo=p.Deck.Cards.OfType<Apotheosis>().Single();
        Check(apo.Keywords.Contains(CardKeyword.Innate)&&apo.Keywords.Contains(CardKeyword.Exhaust),"native Apotheosis is Innate and Exhaust");
        async Task Camp(int hp)
        {
            auto.Pause("0647 fixture");await CreatureCmd.SetCurrentHp(p.Creature,hp);
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite,showTransition:false);
            await Until(()=>service.Decision?.Kind=="rest"&&service.RestRoom==NRestSiteRoom.Instance,"0647 camp advice");
        }
        async Task Finish(string label)
        {
            auto.Enable(true);await Until(()=>Screen() is NMapScreen,label,60);
            Check(auto.Enabled&&!auto.WaitingForManual,label+" retains control");
            auto.Pause("0647 boundary");NMapScreen.Instance!.Close(false);
        }
        await Camp(p.Creature.MaxHp);
        Check(service.Decision!.Evaluation.RecommendedKey=="SMITH","native camp recommends Apotheosis upgrade");
        await Shot0620("0647-apotheosis-smith");
        await Finish("0647 Apotheosis smith");
        Check(apo.CurrentUpgradeLevel==1&&p.Deck.Cards.Where(c=>c!=apo).All(c=>c.CurrentUpgradeLevel==0),"only Apotheosis upgraded persistently");
        await Camp(30);
        Check(service.Decision!.Evaluation.RecommendedKey=="HEAL","native next camp recommends recovery");
        await Shot0620("0647-apotheosis-heal");
        await Finish("0647 recovery");
        Check(p.Creature.CurrentHp>30&&p.Deck.Cards.Where(c=>c!=apo).All(c=>c.CurrentUpgradeLevel==0),"recovery without covered upgrades");
        var tent=ModelDb.Relic<MiniatureTent>().ToMutable();await RelicCmd.Obtain(tent,p);
        var girya=(Girya)ModelDb.Relic<Girya>().ToMutable();await RelicCmd.Obtain(girya,p);girya.TimesLifted=2;
        var pumpkin=(PumpkinCandle)ModelDb.Relic<PumpkinCandle>().ToMutable();await RelicCmd.Obtain(pumpkin,p);pumpkin.KindleCount=0;
        await Camp(30);
        var economy=RouteAdapter.Economy(service);
        Check(economy.CampAllActions&&economy.CampAlternatives.Single(a=>a.Id=="LIFT").Uses==1,"native tent and remaining Girya charge reach route model");
        Check(economy.CampAlternatives.Any(a=>a.Id=="KINDLE"&&a.Uses>0),"native candle reaches route model");
        await Finish("0647 tent actions");
        Check(girya.TimesLifted==3&&pumpkin.KindleCount==5&&p.Creature.CurrentHp>30,"tent performs healing lift and kindle once");
        Check(p.Deck.Cards.Where(c=>c!=apo).All(c=>c.CurrentUpgradeLevel==0),"tent does not spend its remaining smith action after Apotheosis");
        pumpkin.KindleCount=100;
        Check(CampMechanics.Effect(service,new KindleRestSiteOption(p),SnapshotReader.Read(state,p))==0,"already sufficient candle charges do not request redundant kindling");
        await RelicCmd.Remove(tent);await RelicCmd.Remove(girya);await RelicCmd.Remove(pumpkin);
        await RelicCmd.Obtain(ModelDb.Relic<StoneHumidifier>().ToMutable(),p);
        var max=p.Creature.MaxHp;await Camp(max);
        Check(service.Decision!.Evaluation.Options.Single(o=>o.Key=="HEAL").Score>0,"full-HP rest still values max-HP reward");
        await Finish("0647 max HP reward");
        Check(p.Creature.MaxHp==max+5,"actual max-HP rest reward completed once");
        await RelicCmd.Remove(p.GetRelic<StoneHumidifier>()!);
        state.AddModifierDebug(ModelDb.Modifier<Midas>().ToMutable());
        state.AddModifierDebug(ModelDb.Modifier<NightTerrors>().ToMutable());
        max=p.Creature.MaxHp;await Camp(10);
        economy=RouteAdapter.Economy(service);
        Check(!economy.CanSmith&&economy.CampHealToFull&&economy.CampHealMaxHpDelta==-5,"Midas and Night Terrors are reflected in route rules");
        Check(!NRestSiteRoom.Instance!.Options.Any(o=>o is SmithRestSiteOption),"native Midas removes smithing");
        Check(service.Decision!.Options.Single(o=>o.Kind=="heal").EffectValue<0,"Night Terrors max-HP cost reaches actual rest advice");
        await Finish("0647 Night Terrors");
        Check(p.Creature.MaxHp==max-5&&p.Creature.CurrentHp==max-5,"native rest fills HP then applies max-HP loss exactly once");
    }
}
