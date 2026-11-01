"""Independent colorless feature evaluation against shared reviewed priors."""
import json
from pathlib import Path
import copy_engine
import composition
import relics
MODELS=json.loads((Path(__file__).resolve().parents[2]/'data/defect-neutral-v1.json').read_text('utf-8'))['cards']
KNOWN=set(MODELS)
def features(s,profiles):
 d=s.get('deck',[])
 def n(*names):return sum(c['className'] in names for c in d)
 def r(name):return any(x.split('.')[-1]==name for x in s.get('relics',[]))
 def tag(c,t):return t in profiles.get(c['className'],{}).get('tags',[])
 act=(s.get('map') or {}).get('contentAct',0) or s.get('act',1)
 return dict(early=1 if act==1 else .4 if act==2 else 0,recall=int(n('AllForOne')>0),physical=min(5,composition.physical(s)),
  rare=min(6,sum(c.get('rarity')=='Rare' for c in d)),junk=min(8,sum(c['className'] in {'StrikeDefect','DefendDefect'} or c['type'] in {'Curse','Status'} for c in d)),
  defends=min(6,n('DefendDefect','UltimateDefend')),draw=min(5,sum(tag(c,'draw') or c['className'] in {'MasterOfStrategy','Scrawl'} for c in d)),
  cycle=min(5,n('AllForOne','Scrape','Hologram','Skim','Overclock','Coolheaded','Finesse','FlashOfSteel','Scrawl')),
  goodAttack=min(5,sum(c['type']=='Attack' and c.get('rarity')!='Basic' and c.get('cost',0)>=1 for c in d)),
  goodSkill=min(4,n('DoubleEnergy','Hologram','Compact','AllForOne','Catastrophe')),
  goodTarget=min(7.5,sum(c.get('rarity')!='Basic' and c['type'] in {'Skill','Power'} for c in d)),
  energy=min(4,n('DoubleEnergy','Turbo','Fusion','Supercritical','Production')+max(0,s.get('energy',3)-3)),
  expensive=min(5,sum(bool(c.get('rarity')!='Basic' and (c.get('cost',0)>=2 or c.get('costsX'))) for c in d)),
  frost=min(4,sum(any(tag(c,t) for t in ('frost','frost2','frost3')) for c in d)),
  block=min(4,sum(c.get('rarity')!='Basic' and c.get('variables',{}).get('Block',0)>0 for c in d)),
  debuff=min(3,n('BeamCell','GoForTheEyes','Shockwave','DarkShackles')),
  attackRatio=sum(c['type']=='Attack' for c in d)/max(1,len(d)),xCount=min(3,sum(bool(c.get('costsX')) for c in d)),largeDeck=min(12,max(0,len(d)-20)),
  ice=int(r('ICE_CREAM')),pyramid=int(r('RUNIC_PYRAMID')),sozu=int(r('SOZU')),ghost=int(r('GHOST_SEED')),chemical=int(r('CHEMICAL_X')),
  strikeDummy=int(r('STRIKE_DUMMY')),fasten=int(n('Fasten')>0))
def score(s,c,profiles):
 name=c['className']
 if name not in KNOWN:return None
 if c.get('affliction') or c.get('enchantment') not in (None,'','ENCHANTMENT.SWIFT') or c.get('modified') and not c.get('enchantment'):
  return dict(supported=False,score=None,parts=[],notes=['Unmodelled modification'])
 m=MODELS[name];f=features(s,profiles);n=sum(d['className']==name for d in s.get('deck',[]))
 parts=[dict(reason='neutral net value',value=m['base']+(m['upgrade'] if c.get('upgradeLevel',0)>0 else 0)),dict(reason='mechanism fit',value=sum(v*f[k] for k,v in m['weights'].items())),dict(reason='duplicates',value=-n*m['duplicate']-n*n*.5)]
 parts += [dict(reason=r,value=v) for r,v in relics.adjustments(s,c,profiles)+list(copy_engine.adjustments(s,c))]
 if copy_engine.swift(c):parts.append(dict(reason='Swift',value=min(4,c.get('enchantmentAmount',0)*1.2)))
 return dict(supported=True,score=round(sum(p['value'] for p in parts),3),parts=parts,notes=['Reviewed mechanism priors'])
