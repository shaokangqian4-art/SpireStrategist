"""Bounded first-component completion value plus generic-cap compensation."""
import composition

def budget(s,c,profiles):
    deck=s['deck'];name=c['className']
    def n(*names):return sum(d['className'] in names for d in deck)
    def t(*tags):return sum(bool(set(profiles.get(d['className'],{}).get('tags',[])) & set(tags)) for d in deck)
    if n(name):return 0
    physical=composition.physical(s)>=2 and composition.physical(s)>composition.orbs(s)
    powers=sum(d['type']=='Power' for d in deck)
    if name=='Storm' and not physical and powers>=2 and n('Thunder','Subroutine'):return 4
    if name=='Thunder' and not physical and (n('Voltaic') and t('lightning') or n('Storm') and powers>=3):return 4
    if name=='Voltaic' and not physical and n('Storm','Thunder') and t('lightning','lightning_engine'):return 4
    if name=='Subroutine' and powers>=3 and n('Storm','Thunder','EchoForm'):return 4
    if name=='Defragment' and not physical and t('lightning','frost','frost2','frost3','dark','glass','lightning_engine')>=3:return 4
    if name=='Darkness' and not physical and n('ConsumingShadow','ShadowShield','Null') and n('Dualcast','Quadcast','MultiCast','Shatter'):return 3
    if name in ('MultiCast','Shatter') and not physical and n('Darkness'):return 3
    if name=='AllForOne' and (n('AdaptiveStrike') or n('Claw')>=2):return 4
    if name in ('Feral','Scrape') and physical and n('Claw')>=2:return 3
    if name=='FlakCannon' and t('status_source')>=2:return 4
    if name=='TrashToTreasure' and t('status_source')>=3 and n('Compact','FlakCannon'):return 3
    if name=='SignalBoost' and powers>=3 and n('EchoForm','Defragment','Storm','Thunder') and (s.get('energy',3)>=4 or t('energy','plasma')>=2):return 3
    return 0
