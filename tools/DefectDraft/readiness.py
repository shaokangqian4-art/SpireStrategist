"""Independent attack/block coverage proxy used by transition acquisition."""
from collections import Counter

def coverage(s, profiles):
    import neutral
    deck=s['deck']; seen=Counter(); attack=block=known=0
    def tags(c): return set(profiles.get(c['className'],{}).get('tags',[]))
    for c in deck:
        name=c['className']; v=c.get('variables',{})
        if name in profiles:
            a=float(v.get('Damage',0))/10; b=float(v.get('Block',0))/10
        elif name in neutral.MODELS:
            a=min(2,float(v.get('Damage',0))/10) if c['type']=='Attack' else 0
            b=0 if name in {'TheGambit','PanicButton','Prolong'} else min(2,float(v.get('Block',0))/10)
        elif name=='AscendersBane': a=b=0
        else: continue
        known+=1; attack+=a*.7**seen[name]; block+=b*.7**seen[name]; seen[name]+=1
    focus=sum(float(c.get('variables',{}).get('FocusPower',1)) for c in deck if 'focus' in tags(c))
    relics={r.split('.')[-1] for r in s.get('relics',[])}
    if 'DATA_DISK' in relics: focus+=1
    frost=sum(2 if 'frost3' in tags(c) else 1.5 if 'frost2' in tags(c) else 1 if 'frost' in tags(c) else 0 for c in deck)
    storm=min(3,sum(c['type']=='Power' for c in deck)*.5) if any(c['className']=='Storm' for c in deck) else 0
    attack+=sum('lightning' in tags(c) for c in deck)*.4+storm*.6+sum('dark' in tags(c) for c in deck)*.4
    if 'INFUSED_CORE' in relics: attack+=1.2
    block+=frost*(.6+min(3,focus)*.25)
    ratio=known/max(1,len(deck))
    return min(1,max(0,attack/8))*ratio,min(1,max(0,block/7))*ratio
