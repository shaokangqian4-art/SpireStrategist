"""Card/relic synergy mirror; event relic valuations remain in the C# core."""
import composition
def adjustments(s,c,profiles):
 owned={r.split('.')[-1] for r in s.get('relics',[])};tags=set(profiles.get(c['className'],{}).get('tags',[]));name=c['className'];v=0
 if 'DATA_DISK' in owned and tags & {'frost','frost2','frost3','lightning','dark','glass'}:v+=.8
 if 'INFUSED_CORE' in owned and name in {'Thunder','Storm','Voltaic'}:v+=1.2
 if 'GOLD_PLATED_CABLES' in owned and tags & {'focus','dark','frost','frost2','frost3'}:v+=.6
 if 'RUNIC_CAPACITOR' in owned and name=='Darkness':v+=.7
 if 'MUMMIFIED_HAND' in owned and c['type']=='Power':v+=1.2
 if 'PERMAFROST' in owned and c['type']=='Power' and sum(d['type']=='Power' for d in s['deck'])<3:v+=.4
 if 'CHEMICAL_X' in owned and c.get('costsX') and name!='Volley':v+=2
 if composition.physical(s)>=3 and c['type']=='Attack' and c.get('cost',0)<=1:v+=min(1.5,len(owned & {'KUNAI','SHURIKEN','ORNAMENTAL_FAN','KUSARIGAMA','NUNCHAKU'})*.4)
 return [('已持有遗物的实际机制联动',v)] if v else []
