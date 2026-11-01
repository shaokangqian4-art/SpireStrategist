"""Explicit physical/orb commitments and common-card acquisition policy."""
import copy_engine
import damage_plan
import readiness

def physical(s):
 return sum(dict(Claw=2,AdaptiveStrike=3,AllForOne=2,Scrape=2,Feral=2,HelixDrill=1.5,BulkUp=.5).get(c['className'],0) for c in s['deck'])
def orbs(s):
 return sum(dict(Defragment=2,Storm=2,Thunder=2,Voltaic=2,Darkness=2,ConsumingShadow=2,Hailstorm=2,Spinner=2,Loop=1,Capacitor=1,IceLance=1,ShadowShield=1,Subroutine=.5).get(c['className'],0) for c in s['deck'])
def common_gate(s,c):
 return c.get('rarity')=='Common' and c['className'] not in {'Claw','BeamCell','GoForTheEyes','BoostAway','Turbo','Coolheaded','Hotfix','Hologram'} and not copy_engine.swift(c) and not(c['className']=='GunkUp' and physical(s)>=3 and sum(d['className']=='BulkUp' for d in s['deck'])>=2)

def has_damage_engine(s,profiles):
 def n(*names):return sum(c['className'] in names for c in s['deck'])
 recur=n('Hologram','AllForOne','Scrape','Feral')>0
 return (any(c["className"]=="Voltaic" and c.get("upgradeLevel",0)>0 for c in s["deck"]) and any("lightning" in profiles.get(c["className"],{}).get("tags",[]) for c in s["deck"]) and sum(bool(set(profiles.get(c["className"],{}).get("tags",[])) & {"draw","energy"}) for c in s["deck"])>=2) or damage_plan.retrieval_loop(s) or copy_engine.active(s) or (n('Claw')>=2 and recur) or (n('AdaptiveStrike')>0 and recur) or (any(c['className']=='Darkness' and c.get('upgradeLevel',0)>0 for c in s['deck']) and n('Dualcast','Quadcast','MultiCast','Shatter')>0) or (n('Darkness','ConsumingShadow')>0 and n('Null','ShadowShield')>0 and n('Dualcast','Quadcast','MultiCast','Shatter','ConsumingShadow')>0) or n('Darkness','ConsumingShadow')>=2 or (n('Darkness','ConsumingShadow')>0 and n('Loop')>0) or (n('Storm')>0 and sum(c['type']=='Power' for c in s['deck'])>=3 and n('Thunder','Defragment')>0) or (n('Spinner')>0 and n('Defragment','BiasedCognition')>0) or (n('FlakCannon')>0 and sum('status_source' in profiles.get(c['className'],{}).get('tags',[]) for c in s['deck'])>=2)

def needs_second_compact(s,profiles):
 burden=sum(1 if c['type']=='Status' else (2 if c['className']=='FightThrough' else 1) if 'status_source' in profiles.get(c['className'],{}).get('tags',[]) else 0 for c in s['deck'])
 return sum(c['className']=='Compact' for c in s['deck'])==1 and burden>=4+2*sum(c['className']=='FlakCannon' for c in s['deck']) and burden>=len(s['deck'])*.2

def needs_bridge_damage(s,profiles):
 act=(s.get('map') or {}).get('contentAct') or s.get('act',1)
 if act!=1 or has_damage_engine(s,profiles):return False
 m=s.get('map') or {}
 if m.get('bossRow',0)>0 and m.get('currentRow',0)/m['bossRow']>=.6:return False
 damage=sum(min(1.5,float(c.get('variables',{}).get('Damage',0))*max(1,float(c.get('variables',{}).get('Repeat',1)))/12) for c in s['deck'] if c.get('rarity')!='Basic' and c['type']=='Attack')
 damage+=sum(dict(BallLightning=.7,Darkness=1.3,ConsumingShadow=1.3,Spinner=1.3,ShadowShield=.6,Null=.6).get(c['className'],0) for c in s['deck'])
 return damage<2.5

def bridge_candidate(s,c,profiles):
 name=c['className'];p=physical(s);orb=orbs(s)
 return needs_bridge_damage(s,profiles) and not any(d['className']==name for d in s['deck']) and name in {'BallLightning','SweepingBeam','FocusedStrike','MomentumStrike'} and not(p>=2 and p>orb and name=='BallLightning') and not(orb>=2 and orb>=p and name=='MomentumStrike')
def adjustments(s,c,profiles):
 name=c['className'];p=physical(s);orb=orbs(s);parts=[]
 def n(name):return sum(d['className']==name for d in s['deck'])
 def add(r,v):
  if v:parts.append((r,v))
 claw=n('Claw');adaptive=n('AdaptiveStrike')
 first_engine=name in {'Storm','Darkness','ConsumingShadow','Spinner','AdaptiveStrike'} or (name=='Thunder' and (n('Storm')>0 or sum('lightning' in profiles.get(d['className'],{}).get('tags',[]) for d in s['deck'])>=2))
 first_engine=first_engine or (name=='Voltaic' and any('lightning' in profiles.get(d['className'],{}).get('tags',[]) or d['className']=='Storm' for d in s['deck']))
 if n(name)==0 and first_engine and not has_damage_engine(s,profiles) and not(name in {'AdaptiveStrike','Claw'} and damage_plan.orb_committed(s) and orb>=p) and not(name in {'Storm','Thunder','Darkness','ConsumingShadow','Spinner','Voltaic'} and damage_plan.physical_committed(s) and p>orb):
  add('尚缺长战输出主干，首张兼容引擎优先于继续堆功能牌',3 if s.get('act',1)<=2 else 2)
 m=s.get('map');remaining=max(1,m['bossRow']-m['currentRow'])+max(0,m.get('totalActs',0)-s.get('act',1))*max(1,m.get('rows',0)) if m else 18
 if name=='Voltaic' and n(name)==0 and not has_damage_engine(s,profiles) and p<2 and any('lightning' in profiles.get(d['className'],{}).get('tags',[]) for d in s['deck']) and sum(bool(set(profiles.get(d['className'],{}).get('tags',[]))&{'draw','energy'}) for d in s['deck'])>=2 and remaining>=12 and s['hp']>=s['maxHp']*.35 and (c.get('upgradeLevel',0)>0 or c.get('canUpgrade',False) and not any(r.split('.')[-1]=='FUSION_HAMMER' for r in s.get('relics',[]))):
  add('首张电流相生投资敲后可重复倍增，需种子产电和过牌费用支撑',3)
 pc=name in {'Claw','AdaptiveStrike','AllForOne','Scrape','Feral','BulkUp','HelixDrill','BeamCell','GoForTheEyes','MomentumStrike'}
 oc=name in {'Defragment','Storm','Thunder','Darkness','ConsumingShadow','Capacitor','Hailstorm','Spinner','Loop','Voltaic','MultiCast','IceLance','Shatter','Null'}
 zs=sum(d['type']=='Skill' and d.get('cost')==0 and not d.get('costsX') and 'Exhaust' not in d.get('keywords',[]) for d in s['deck'])
 if pc and orb>=2 and orb>=p and not(name=='AllForOne' and zs>=4):
  if damage_plan.orb_committed(s):add('球系主线不再投资物理组件',-6-min(5,orb*.5))
  else:add('仅有零散球系组件，保留转向成本但不封锁物理核心',-.75)
 if oc and p>=2 and p>orb:
  if damage_plan.physical_committed(s):add('物理主线不再投资集中与产球终端',-7-min(4,p*.4))
  else:add('仅有零散物理组件，保留转向成本但不封锁产球核心',-.75)
 if damage_plan.physical_committed(s) and p>orb:
  if name in {'Hotfix','Glasswork'}:add('物理输出不为临时集中或玻璃伤害额外占位',-6)
  if name=='Hyperbeam' and (claw>=2 or adaptive):add('已有重复物理输出，额外高费群伤挤占循环',-6)
  if name=='Feral' and n(name)==0:
   targets=sum(d['type']=='Attack' and d.get('cost')==0 and not d.get('costsX') for d in s['deck'])
   if targets>=2 or adaptive:add('首张野性回收零费物理核心，每回合一次',4)
  if name=='BulkUp' and n(name)<2:add('物理主线优先补充力量来源',2)
 if name=='GunkUp' and n('BulkUp')<2 and not any(n(x) for x in ('Compact','FlakCannon','TrashToTreasure')):add('污秽攻击缺少增力或状态处理，不用低效产状态补伤害',-3.5)
 if name=='Smokestack':
  sources=sum('status_source' in profiles.get(d['className'],{}).get('tags',[]) for d in s['deck'])
  nonbasic=sum(d.get('rarity')!='Basic' and d['type'] in {'Attack','Skill','Power'} for d in s['deck'])
  if sources<4 or sources<nonbasic*.35:add('烟囱需要高密度主动造状态，少量功能牌不足以支持',-7)
 if name=='BulkUp' and p<2:add('暴涨以物理收益为主，基础防御不证明需要敏捷引擎',-3)
 if name=='Claw':
  if adaptive and claw<3:add('适应复制主线不另起爪群',-9)
  elif 0<claw<5 and p>orb:add('爪群三至五张共享成长，仍需过牌回收',3+min(2,claw*.5))
  if claw>=5:add('五张爪群后优先过牌与防守',-8)
 if name=='AdaptiveStrike' and claw>=3 and adaptive==0 and p>orb:add('已有爪群可共用回收并补充复制攻击',2)
 if p>=3 and p>orb and name in {'Hologram','Scrape','AllForOne','Skim','Overclock','Reboot'}:add('物理引擎需要高密度过牌与回收',2 if n(name)<2 else 0)
 if name=='Overclock':
  add('零费净过牌支持所有主线',2)
  if n('Scavenge'):add('内存清理可处理超频灼伤',1.2)
 if name=='HelixDrill' and any(r.split('.')[-1]=='ICE_CREAM' for r in s.get('relics',[])):add('冰淇淋留存能量支持螺旋钻击的多段输出',3)
 if name=='DoubleEnergy':
  m=s.get('map');remaining=max(1,m['bossRow']-m['currentRow'])+max(0,m.get('totalActs',0)-s.get('act',1))*max(1,m.get('rows',0)) if m else 18
  if n(name)==0 and s.get('act',1)<=2 and remaining>=10:add('首张双倍能量保留未来成长价值，无需等待成型',2)
  support=sum(d.get('cost',0)>=2 or d.get('costsX') or d['className'] in {'Skim','Overclock','Coolheaded','Hologram'} for d in s['deck'])
  ice=any(r.split('.')[-1]=='ICE_CREAM' for r in s.get('relics',[]))
  add('双倍能量需要过牌和费用出口，支持两至三张',min(3,support*.5)+(2 if ice else 0))
  if n(name)>=3:add('三张翻倍后优先补抽牌和费用出口',-5)
 return parts


def transition_adjustments(s,c,profiles):
 if not common_gate(s,c):return []
 offense,defense=readiness.coverage(s,profiles)
 attack=c['type']=='Attack';block=c.get('variables',{}).get('Block',0)>0 or bool(set(profiles.get(c['className'],{}).get('tags',[]))&{'frost','frost2'})
 need=1-offense if attack else 1-defense if block else 0
 act=max(1,min(3,(s.get('map') or {}).get('contentAct') or s.get('act',1)))
 copies=sum(d['className']==c['className'] for d in s['deck'])
 penalty=(1 if act==1 else 2.5 if act==2 else 3.5)*(1-min(1,max(0,need)))+min(1.5,copies*.5)
 if not attack and not block:penalty+=1
 return [('过渡牌按实际攻防覆盖递减，不按非基础牌张数封顶',-penalty)]
