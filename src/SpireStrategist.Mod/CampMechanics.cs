using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Modifiers;
using SpireStrategist.Core;

namespace SpireStrategist.Mod;

// Pure estimates only: never call reward generation hooks while inspecting a camp.
internal static class CampMechanics
{
    internal static double HealExtra(AdvisorService service,RunSnapshot s,bool mimicked=false)
    {
        var v=new StrategicValue(s);
        var value=0d;
        if(service.Player.GetRelic<StoneHumidifier>() is {} stone)value+=v.MaxHp((double)stone.DynamicVars.MaxHp.BaseValue);
        if(service.Player.GetRelic<DreamCatcher>()!=null)value+=2; // optional unknown card, not a guaranteed acquisition
        if(service.Player.GetRelic<TinyMailbox>()!=null&&service.Player.GetRelic<Sozu>()==null)
            value+=Math.Min(2,s.PotionSlots.Count(p=>p==null))*2;
        if(!mimicked&&service.State.Modifiers.Any(m=>m is NightTerrors))value-=v.MaxHp(Math.Min(5,s.MaxHp-1));
        return value;
    }
    internal static int KindleUses(AdvisorService service,RunSnapshot s)
    {
        var charges=service.Player.GetRelic<PumpkinCandle>()?.KindleCount??0;
        var battles=Math.Max(1,new StrategicValue(s).Remaining*.6);
        return Math.Max(0,(int)Math.Ceiling((battles-charges)/5));
    }
    internal static double? Effect(AdvisorService service,RestSiteOption option,RunSnapshot s)
    {
        if(option is HealRestSiteOption)return HealExtra(service,s);
        if(option is KindleRestSiteOption&&KindleUses(service,s)==0)return 0;
        if(option is CookRestSiteOption)
        {
            var v=new StrategicValue(s);
            return service.Player.Deck.Cards.Where(c=>c.IsRemovable).Select(c=>v.Remove(SnapshotReader.ReadCard(c)))
                .OrderDescending().Take(2).Sum()+v.MaxHp(5);
        }
        if(option is CloneRestSiteOption)
        {
            var projected=s;var value=0d;
            foreach(var card in s.Deck.Where(c=>c.Enchantment=="ENCHANTMENT.CLONE"))
            {value+=new StrategicValue(projected).Add(card);projected=projected with{Deck=[..projected.Deck,card]};}
            return value;
        }
        return AdvisorService.RestEffect(option,s);
    }
    internal static CampAlternative[] Alternatives(AdvisorService service,IEnumerable<RestSiteOption> options,RunSnapshot s)=>
        options.Where(o=>o.IsEnabled&&o is not (HealRestSiteOption or SmithRestSiteOption or MendRestSiteOption))
            .Where(o=>Effect(service,o,s)!=null).Select(o=>new CampAlternative(o.OptionId,Effect(service,o,s)!.Value,o switch
            {
                LiftRestSiteOption=>Math.Max(0,3-(service.Player.GetRelic<Girya>()?.TimesLifted??3)),
                DigRestSiteOption=>12,
                KindleRestSiteOption=>KindleUses(service,s),
                // Deck-changing options are credited once; actual room completion rebuilds the projection.
                CookRestSiteOption or CloneRestSiteOption or HatchRestSiteOption=>1,
                _=>0
            })).ToArray();
}
