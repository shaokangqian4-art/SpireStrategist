"""Offline, deterministic Defect reward prior. Never executes game actions.

Accepts the existing decision JSON or session JSONL. Whole-file logs also join
human choices by decision ID. Unmodeled offers cause abstention, not score zero.
"""
import argparse
import json
import math
import direction
import composition
import damage_plan
import copy_engine
import neutral
import relics
import engine_links
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
CONFIG = json.loads((ROOT/'data/defect-draft-v0.14.json').read_text('utf-8-sig'))
PROFILES = CONFIG['cards']
DEFINITIONS = {r['basic']['className']:r for r in json.loads((ROOT/'data/defect-cards-v0.111.0.json').read_text('utf-8-sig'))}
NEUTRAL = {r['basic']['className']:r for r in json.loads((ROOT/'data/cards-v0.111.0.json').read_text('utf-8-sig')) if r['basic']['className'] in neutral.KNOWN}
C = CONFIG['coefficients']

def snapshot(name, upgraded=False):
    return dict((DEFINITIONS.get(name) or NEUTRAL[name])['upgraded' if upgraded else 'basic'])

def starter():
    return dict(character='Defect',playerCount=1,hp=75,maxHp=75,act=1,energy=3,
        deck=[snapshot(n) for n in ['StrikeDefect']*4+['DefendDefect']*4+['Zap','Dualcast']],
        relics=['CRACKED_CORE'])

def card_data(card):
    name = card.get('className','')
    defaults = DEFINITIONS.get(name,{}).get('upgraded' if card.get('upgradeLevel',0)>0 else 'basic',{})
    return {**defaults,**card}

def tags(card):
    return set(PROFILES.get(card.get('className'),{}).get('tags',[]))

def count(deck, tag):
    return sum(tag in tags(c) for c in deck)

def is_zero_attack(c):
    c=card_data(c)
    return c.get('type')=='Attack' and c.get('cost')==0 and not c.get('costsX',False)

def context(state):
    deck=[card_data(c) for c in state.get('deck',[])]
    nonbasic=[c for c in deck if c.get('rarity')!='Basic']
    types={t for c in deck for t in tags(c)}
    # Producer density is a discounted proxy, NOT simulated orbs in play.
    producers=sum(bool(tags(c)&{'lightning','frost','frost2','frost3','dark','glass'}) for c in deck)
    if any(r.split('.')[-1] in {'CRACKED_CORE','INFUSED_CORE'} for r in state.get('relics',[])):
        producers+=.5
    frost=sum(1 if 'frost' in tags(c) else 1.5 if 'frost2' in tags(c) else 2 if 'frost3' in tags(c) else 0 for c in deck)
    offense=sum(min(1.5,float(c.get('variables',{}).get('Damage',0))/8) for c in nonbasic if 'attack' in tags(c) and c['className']!='FlakCannon')
    defense=sum(min(1.5,float(c.get('variables',{}).get('Block',0))/8) for c in nonbasic)+frost*.65
    draw=sum(min(2,max(0,float(c.get('variables',{}).get('Cards',1))-1)) for c in deck if 'draw' in tags(c))
    powers=sum(c.get('type')=='Power' for c in deck)
    status=count(deck,'status_source')+sum(c['className']=='FightThrough' for c in deck)
    # Discount repeated power triggers; these are planning estimates, not simulated channels.
    storm=sum(min(3,max(0,powers-1)*.65)*float(c.get('variables',{}).get('StormPower',1)) for c in deck if c['className']=='Storm')
    lightning=count(deck,'lightning')+min(4,storm)
    producers+=min(3,storm)
    direct_offense=offense
    offense+=min(2,lightning*.35+count(deck,'dark')*.3+count(deck,'glass')*.35)+sum(c['className']=='FlakCannon' for c in deck)*min(1.5,status*.4)
    energy_support=count(deck,'energy')+count(deck,'plasma')+min(1,count(deck,'power_energy')*max(0,powers-1)*.4)
    clear=count(deck,'status_clear')
    zero_cards=sum(c.get('cost')==0 and not c.get('costsX',False) for c in deck)
    zero_density=zero_cards/max(1,len(deck))
    retrieval_zero=sum(c.get('cost')==0 and not c.get('costsX',False) and c.get('type') in {'Attack','Skill'} and 'Exhaust' not in c.get('keywords',[]) for c in deck)
    physical=sum(c['className'] in {'Claw','AllForOne','Scrape','Feral','HelixDrill','MomentumStrike','Ftl','GoForTheEyes'} for c in deck)
    upgrade_backlog=sum(not c.get('upgradeLevel',0) and c.get('canUpgrade',False) and PROFILES.get(c['className'],{}).get('upgradeDelta',0)>=2 for c in deck)
    spend=sum(c.get('cost',0)>=2 or c.get('costsX',False) for c in deck if c.get('rarity')!='Basic')
    zero=sum(is_zero_attack(c) for c in deck)
    act=max(1,min(3,int((state.get('map') or {}).get('contentAct') or state.get('act',1))))
    return dict(deck=deck,types=types,producers=producers,frost=frost,offense=offense,defense=defense,
        draw=draw,powers=powers,status=status,spend=spend,zero=zero,act=act,direct_offense=direct_offense,
        storm=storm,lightning=lightning,energy_support=energy_support,clear=clear,zero_density=zero_density,retrieval_zero=retrieval_zero,physical=physical,upgrade_backlog=upgrade_backlog,
        names=Counter(c['className'] for c in deck),energy=float(state.get('energy',3)))

def score(state, card):
    recovered=state.get('hpAfterPendingAncient')
    if recovered is not None and 0<state.get('hp',0)<recovered<=state.get('maxHp',0):
        state=dict(state,hp=recovered)
    neutral_result=neutral.score(state,card,PROFILES)
    if neutral_result is not None:return neutral_result
    c=card_data(card); name=c.get('className',''); p=PROFILES.get(name)
    if not p:
        return dict(supported=False,score=None,parts=[],notes=['尚未建立机器人语境模型；不能用战士权重替代。'])
    if DEFINITIONS[name]['multiplayerConstraint']=='MultiplayerOnly':
        return dict(supported=False,score=None,parts=[],notes=['多人专用牌，不参与单人建议。'])
    x=context(state); t=set(p['tags']); deck=x['deck']; parts=[]
    def add(reason, value):
        if value: parts.append(dict(reason=reason,value=round(float(value),3)))
    add('基础先验（尚未拟合）',p['base'])
    add('前／中／后期需求',p['phase'][x['act']-1])
    add('占用一次抽牌的基准成本',-C['cardTax'])
    if 'basic' not in t:
        if 'attack' in t and name!='FlakCannon':
            strength=min(1.5,float(c.get('variables',{}).get('Damage',0))/8)
            add('即时输出缺口',C['damageNeed']*max(0,1-x['offense']/4)*strength)
        if t&{'block','frost','frost2','frost3','weak'}:
            add('防御缺口',C['blockNeed']*max(0,1-x['defense']/5))
        if 'aoe' in t or 'glass' in t:
            add('缺少群体输出',1.2 if not x['types']&{'aoe','glass'} else 0)
    n=x['names'][name]
    power_targets=sum(d['type']=='Power' and d['className']!='CreativeAi' for d in deck)
    two_holograms=name=='Hologram' and n==1 and not c.get('upgradeLevel',0) and not next(d for d in deck if d['className']=='Hologram').get('upgradeLevel',0)
    add('第二张未敲全息提供另一次消耗回收' if two_holograms else '重复份数的边际递减',-.9 if two_holograms else -(.8*n+.35*n*n) if name=='SignalBoost' and power_targets>n else -(p['duplicate']*n+(.12 if name=='Claw' else .2 if name=='DoubleEnergy' else C['duplicateQuadratic'])*n*n))
    if c.get('upgradeLevel',0)>0: add('该牌已升级的实际机制增益先验',p['upgradeDelta'])
    # Only reliable net draw discounts tax; self-replacement is not draw profit.
    if 'draw' in t and c.get('type')!='Power' and name!='Reboot':
        cards=float(c.get('variables',{}).get('Cards',1))
        add('净抽牌与可用费用配合',min(2,max(0,cards-1))*.6*min(1,(x['spend']+1)/3))
        add('已有过牌饱和',-max(0,x['draw']-(12 if composition.physical(state)>=3 else 8))*.35)
    if 'conditional_draw' in t: add('有条件自替换折扣补偿',.7)
    if 'diversity_draw' in t: add('可实现球种的过牌折扣',min(2,len(x['types']&{'lightning','dark','glass','plasma'})+bool(x['frost']))*.7)
    synergies=[]
    def synergy(reason,value): synergies.append((reason,value))
    if t&{'focus','temp_focus'}:
        synergy('集中需要非等离子产球',min(4,x['producers']*.8)-2 if x['producers']>0 else -8)
        if 'temp_focus' in t: synergy('临时集中不算永久成长',-1.5 if x['producers']<3 else 0)
    if t&{'frost','frost2','frost3','lightning','dark','glass'}:
        synergy('现有永久集中支持产球',min(2,count(deck,'focus')))
    if 'slots' in t:
        synergy('扩容需要产球速度与球质量',min(4,x['producers']*.8)-3)
        synergy('球位组件饱和',-count(deck,'slots')*1.5)
        if count(deck,'dark'):
            synergy('扩容保护黑暗蓄力，减轻过早顶球',min(3,count(deck,'dark')*.7+x['names']['Darkness']*.8))
        elif x['storm']>0 or x['names']['Thunder']>0:
            synergy('扩容会推迟依赖顶球的激发',-1.2)
    if ('orb_payoff' in t or 'orb_attack' in t) and name!='Loop':
        synergy('球占用率与质量折扣',min(4,x['producers']*.9)-2)
    if name=='Loop':synergy('循环只重复最右侧被动，不因球多就视为核心',min(2,count(deck,'dark')*.7+count(deck,'focus')*.3) if count(deck,'dark')>0 else 1 if count(deck,'focus')>=2 and x['frost']>0 else -2.5)
    if 'evoke' in t or 'evoke_all' in t:
        synergy('激发黑暗与等离子',min(3,count(deck,'dark')+count(deck,'plasma')))
        if 'evoke_all' in t and x['frost']>=2: synergy('清空冰霜后仍需补球防御',-.8)
    if name=='Darkness':
        if x['names']['Storm'] and x['names']['Thunder'] and not x['names']['Darkness'] and not x['names']['ConsumingShadow']:
            synergy('成熟电球体系不另起蓄黑球支线',-7)
        synergy('漆黑触发已有黑暗被动',min(3,count(deck,'dark')*(1.1 if c.get('upgradeLevel',0)>0 else .7)))
        synergy('蓄黑球已有空间和激发出口',min(2,count(deck,'slots')*.6+sum(d['className'] in {'Dualcast','Quadcast','MultiCast','Shatter'} for d in deck)*.5))
    if name in {'Dualcast','Quadcast','MultiCast','Shatter'} and count(deck,'dark'):
        multiplier=1.8 if name=='Dualcast' else 2.8 if name=='Quadcast' else 2.4 if name=='Shatter' else min(2.8,max(0,x['energy']-1)*.7+x['energy_support']*.3)
        synergy('多次激发兑现蓄黑球（排球需另行实现）',min(3,(count(deck,'dark')*.6+x['names']['Darkness']*.6)*multiplier))
        if name=='MultiCast':synergy('X费黑球终端仍需留防守费用',-.5)
    if 'frost_payoff' in t: synergy('需要稳定冰霜',min(4,x['frost']*1.5)-3)
    if name=='Hailstorm':synergy('冰霜终端需要保有冰球，频繁产电会挤走冰球',-2 if x['frost']<2 else -1.5 if x['storm']>0 else 0)
    amp=min(3,sum(2 if d.get('upgradeLevel',0)>0 else 1 for d in deck if d['className']=='Voltaic')) if x['lightning']>0 or any(r.split('.')[-1]=='CRACKED_CORE' for r in state.get('relics',[])) else 0
    if 'lightning_payoff' in t: synergy('需要足够闪电生成',min(5,(x['lightning']+amp+(1 if name=='Voltaic' and any(r.split('.')[-1]=='CRACKED_CORE' for r in state.get('relics',[])) else 0))*1.2)-3)
    if name=='Thunder' and n==0 and amp>0:synergy('已有电流相生批量产电顶球，首张雷霆兑现连续激发',4 if any(d['className']=='Voltaic' and d.get('upgradeLevel',0)>0 for d in deck) else 2)
    if name=='Thunder': synergy('额外激发渠道',min(1.5,count(deck,'evoke')*.5))
    if 'diversity_payoff' in t: synergy('可靠球种折扣',min(3,len(x['types']&{'lightning','dark','glass','plasma'})+bool(x['frost']))-2)
    if 'status_clear' in t:
        # Enemy status pressure is a modest act-based prior, not an exact encounter forecast.
        synergy('敌方塞状态的保险价值（折扣先验）',[.7,1.2,1.6][x['act']-1])
        synergy('自造状态与全牌堆／手牌处理配合',min(4,x['status']*(1.3 if name=='FlakCannon' else 1.0)))
        synergy('第一张状态处理补缺',1.4 if x['clear']==0 else (-1.4 if x['status']<2 else .3))
        if name=='FlakCannon':synergy('无自造状态时散射炮可能空转',-3 if x['status']==0 else 0)
    elif t&{'status_payoff','status_draw','status_discount'}:
        synergy('真实状态生成来源',min(5,x['status']*1.3)-2.5)
    if 'status_source' in t:
        synergy('状态后续抽牌代价',-1.5)
        synergy('已有状态转化／输出引擎',min(3,count(deck,'status_payoff')+count(deck,'status_clear')+count(deck,'status_draw')*.5))
    if 'power_payoff' in t or 'power_discount' in t:
        synergy('可兑现能力牌支持度',min(5,x['powers']*1.2)-3)
    if 'zero_payoff' in t:
        # Scrape can keep skills; Feral specifically needs attacks.
        z=x['zero'] if name=='Feral' else x['retrieval_zero'] if name=='AllForOne' else sum(card_data(d).get('cost')==0 and not d.get('costsX',False) for d in deck)
        synergy('现有零费密度和质量',min(5,z*1.15)-3)
        if name=='AllForOne' and z==0:synergy('没有可回收零费牌，不预购回收终端',-3)
        if name=='Scrape':
            synergy('刮削留下零费牌的密度',min(3,float(c.get('variables',{}).get('Cards',4))*x['zero_density']*1.5)-1.5)
            synergy('丢弃非零费核心的风险',-1.5 if x['powers']>=2 else 0)
    if 'claw' in t:
        cycle=sum((1.5 if d.get('upgradeLevel',0)>0 else .6) if d['className']=='Hologram' else 2 if d['className']=='AllForOne' else 1.5 if d['className']=='Feral' else 1 if d['className']=='Scrape' and x['zero_density']>=.18 else 0 for d in deck)
        synergy('爪击必须已有回收或循环',min(4,cycle+x['draw']*.3)-1.5)
        synergy('已有爪群与可靠回收',min(1.5,x['names']['Claw']*.75) if cycle>=1.5 else 0)
    if 'physical_support' in t:
        synergy('直接攻击支持度；易伤不增强球',min(3,x['direct_offense']*.6)-1)
    if name=='BulkUp':synergy('物理循环兑现力量与敏捷',min(3,x['physical']*.75))
    if 'slot_loss' in t:
        dark=count(deck,'dark'); cycling=x['lightning']+x['frost']+count(deck,'glass')+count(deck,'plasma')
        synergy('减少球位压缩黑暗蓄力空间',-min(5,dark*.85+x['names']['Darkness']*.9+(count(deck,'slots')*.35+count(deck,'orb_payoff')*.3 if dark else 0)))
        passive=x['frost']*.3+count(deck,'focus')*.3+count(deck,'orb_payoff')*.4
        synergy('保留球被动的损失，按补球能力折扣',-min(2.5,passive)/(1+cycling*.25))
        if dark==0 and cycling>=2 and count(deck,'slot_loss')<2:
            synergy('少球位使新球更早顶球激发（非额外产球）',min(1.2,cycling*.2))
        synergy('减槽当场直接移除球，不触发激发',-.35 if x['producers']>0 or count(deck,'plasma') else 0)
        if count(deck,'slot_loss')>=2 and (x['producers']>0 or count(deck,'plasma')):
            synergy('多张减槽可能清空球位，零球位不能生成',-3)
    if name=='BulkUp':
        synergy('直接格挡技能兑现敏捷',min(2.2,sum(d.get('type')=='Skill' and float(d.get('variables',{}).get('Block',0))>0 for d in deck)*.25))
    if ('energy' in t or 'delayed_energy' in t) and name not in {'DoubleEnergy','Supercritical'}:
        synergy('能量需要花费出口和过牌',min(3,x['spend']*.45+x['draw']*.35)-1)
    if 'energy_payoff' in t:
        synergy('能量兑现先验',min(3,max(0,x['energy']-3)+x['energy_support']*.4+count(deck,'plasma')*.1))
    if 'exhaust' in t:
        synergy('处理诅咒状态及剩余基础牌',min(2,sum(d.get('type') in {'Curse','Status'} for d in deck)+sum(d.get('rarity')=='Basic' for d in deck)*.12))
    if name=='Hologram':
        targets=sum(c['rarity']!='Basic' and c.get('type') in {'Attack','Skill'} and c['className']!='Hologram' for c in deck)
        synergy('定向回收优质牌与运转组件',min(2.5,targets*.3+x['physical']*.2))
    if name=='Turbo':
        synergy('首张返能补足启动缺口',max(0,1.2-x['energy_support']*.6) if n==0 else 0)
        synergy('有过牌时兑现额外能量',min(1.5,x['draw']*.35))
    if name in {'Storm','Subroutine'}:
        synergy('现有能力引擎互相触发',min(1.5,sum(d['className'] in {'Storm','Thunder','Subroutine'} and d['className']!=name for d in deck)*.75))
    if name=='Storm' and n==0 and x['powers']>=2:
        synergy('补齐已有雷霆或子程序的产电核心',min(2,count(deck,'power_energy')+x['names']['Thunder']))
    if name=='Voltaic':
        synergy('雷暴产电与雷霆终端配合',min(3,x['storm']*.8+count(deck,'lightning_payoff')*.6) if x['storm']>0 else 0)
    if name=='CreativeAi':
        synergy('随机慢启动需要成熟的防守与费用',1.5 if x['defense']>=5 and x['energy_support']>=2 and x['powers']>=3 else -1.5)
    # Capped positive synergy prevents arbitrarily many tags inflating the same combo.
    positive=sum(max(0,v) for _,v in synergies)
    factor=min(1,C['synergyCap']/positive) if positive else 1
    for reason,value in synergies: add(reason,value*factor if value>0 else value)
    add('明确核心搭档的成型收益（含联动上限补偿）',min(engine_links.budget(state,c,PROFILES),2+max(0,positive-C['synergyCap'])))
    if 'meteor' in t:
        # Startup support is a possibility, not guaranteed same-hand energy.
        support=x['energy']>=5 or any(d['className']=='Supercritical' for d in deck) or (
            x['energy']>=4 and (count(deck,'energy')>0 or count(deck,'plasma')>0)) or any(d['className']=='DoubleEnergy' and (d.get('cost')==0 or x['energy']>=4) for d in deck) or (
            any(r.split('.')[-1]=='ICE_CREAM' for r in state.get('relics',[])) and any(d['className'] in {'DoubleEnergy','Turbo','Fusion'} for d in deck))
        add('五费启动可行性',-12 if not support else -2)
        if support: add('有启动渠道后可评估陨石循环',4)
        if x['names']['Catastrophe']:add('横祸可能免付启动费，但随机打出不能保证命中陨石',1 if support else 2)
    elif c.get('cost',0)>=3:
        add('高费启动与防守竞争',-max(0,c['cost']-x['energy']+1)*1.2)
    if c.get('type')=='Power':
        add('能力牌集中启动负担',-max(0,x['powers']-3-min(2,count(deck,'power_energy')))*.55)
        if float(state.get('hp',75))/max(1,float(state.get('maxHp',75)))<.35 and x['defense']<3:
            add('低血且防守不足，暂缓慢启动',-2)
    if 'growing' in t:
        add('此张已实际养成的格挡',min(6,max(0,float(c.get('variables',{}).get('Block',1))-1)/6))
        m=state.get('map') or {}
        if x['act']==1 and m.get('bossRow',0)>0:
            add('第一幕临近结束，培养时间折扣',-2*max(0,min(1,m.get('currentRow',0)/m['bossRow'])))
    if name=='Hyperbeam' and x['frost']>1: add('当回合降低冰霜防御',-.8)
    if 'glass' in t and x['act']==3 and count(deck,'focus')==0 and name!='Spinner': add('玻璃衰减，缺乏长战补偿',-1)
    if not c.get('upgradeLevel',0) and p['upgradeDelta']>=2:
        add('待敲核心积压，不能预支所有升级',-min(1.25,max(0,x['upgrade_backlog']-2)*.25))
    add('大牌组抽到关键组件更慢',-min(2,max(0,len(deck)-22)*.1))
    if name=='Coolheaded' and not c.get('upgradeLevel',0):
        add('未敲冷静仅自替换，升级与现有核心竞争',-1.5-min(1.5,x['upgrade_backlog']*.5))
        if x['act']==1 and count(deck,'focus')==0 and composition.readiness.coverage(state,PROFILES)[0]<.65:add('一幕缺即时战力，未敲冷静不能替代输出过渡',-1.5)
    if name in {'Turbo','Hotfix'} and n>0:
        add('重复短效资源牌需要明确收益余量',-1)
    if name=='Iteration' and n==0:
        add('首张迭代补偿状态抽牌：每回合首次触发，不消除状态',.8+min(2.4,x['status']*1.2))
    if name=='Supercritical':
        playable=[d for d in deck if d.get('type') in {'Attack','Skill','Power'}]
        average=sum(2 if d.get('costsX') else max(0,min(3,d.get('cost',0))) for d in playable)/max(1,len(playable))
        recalls=sum(d['className'] in {'Hologram','AllForOne'} for d in deck)
        hand_spend=average*(4+min(3,x['draw']*.5+recalls*.5))
        storage=2 if any(r.split('.')[-1]=='ICE_CREAM' for r in state.get('relics',[])) else 0
        usable=min(c.get('variables',{}).get('Energy',4),max(0,hand_spend-x['energy'])+x['spend']*.5+recalls*.3+storage)
        add('零费爆发供能可支付多张低费牌及回收，按可用量折扣',min(4,usable*.9)/(1+n))
        if n==0:add('首张无副作用爆发的择机使用价值',1.2*min(1,usable))
        add('已有供能饱和，额外能量不能当作必然兑现',-max(0,x['energy_support']-2)*.6)
    if name=='Reboot' and n==0:
        add('零费重启在出牌后重抽手牌，消耗后不长期占循环',1.4+min(2.1,c.get('variables',{}).get('Cards',4)*.35))
        add('重启帮助兑现已有供能并重新寻找关键牌',min(1.5,x['energy_support']*.4+sum(d['className'] in {'AdaptiveStrike','AllForOne','Darkness','Storm','Thunder','Defragment'} for d in deck)*.3))
    if name=='Defragment' and n>0 and x['frost']>=2:
        add('额外集中仍能强化现有冰霜防线',min(1.5,n))
    if name=='MultiCast' and not c.get('upgradeLevel',0) and x['energy']<=3 and any(d['className']=='Dualcast' and d.get('cost')==0 for d in deck) and x['names']['Hologram']>0 and not any(d['className'] in {'Turbo','DoubleEnergy','Supercritical'} for d in deck) and count(deck,'plasma')==0 and 'RELIC.CHEMICAL_X' not in state.get('relics',[]):
        add('缺爆发费用时，多重释放相对零费双放加全息的边际收益有限',-3)
    for reason,value in direction.adjustments(state,c,PROFILES):add(reason,value)
    for reason,value in composition.adjustments(state,c,PROFILES):add(reason,value)
    for reason,value in damage_plan.adjustments(state,c,PROFILES):add(reason,value)
    for reason,value in relics.adjustments(state,c,PROFILES):add(reason,value)
    if name=='SignalBoost':
        targets=sum(d['type']=='Power' and d['className']!='CreativeAi' for d in deck)
        add('信号增强按可复制能力数量评估重复份数',min(3,1.5+targets*.4) if targets>n else -3)
    if copy_engine.swift(c) and name not in {'AdaptiveStrike','Anger'}:add('迅捷首次打出抽牌，普通牌不重复触发',min(4,c.get('enchantmentAmount',0)*1.2))
    for reason,value in copy_engine.adjustments(state,c):add(reason,value)
    for reason,value in composition.transition_adjustments(state,c,PROFILES):add(reason,value)
    if name=='Compact' and n>0:
        if composition.needs_second_compact(state,PROFILES):add('高密度真实状态负担允许第二张压缩，收益仍封顶',min(3,1.5-sum(p['value'] for p in parts)))
        else:add('已有压缩且状态负担不足，不重复补同一工具',min(0,-.25-sum(p['value'] for p in parts)))
    notes=[p['note'],'所有权重为待验证先验；评分不是胜率。']
    if c.get('affliction') or c.get('enchantment') not in (None,'','ENCHANTMENT.SWIFT') or c.get('modified') and not c.get('enchantment'):
        notes.append('附魔／异变未建模，不能据此推荐。')
        return dict(supported=False,score=None,parts=parts,notes=notes)
    return dict(supported=True,score=round(sum(p['value'] for p in parts),3),parts=parts,notes=notes)

def evaluate(record):
    state=record['state']; options=record['options']
    warnings=['离线草案，不控制游戏；未校准遗物、具体敌人、隐藏未来和求解器执行水平。',
        '只评价免费卡牌奖励；不能直接作为商店价格、删牌或火堆升级价值。']
    valid=state.get('character')=='Defect' and state.get('playerCount',1)==1
    unknown=sorted({c.get('className','') for c in state.get('deck',[]) if c.get('className') not in PROFILES})
    if unknown: warnings.append('牌组中未建模（包括无色牌／诅咒等）：'+', '.join(unknown))
    rows=[]
    for o in options:
        if not valid: result=dict(supported=False,score=None,parts=[],notes=['仅单人机器人。'])
        elif not o.get('enabled',True): result=dict(supported=True,score=None,parts=[],notes=['不可选。'])
        elif o['kind']=='skip': result=dict(supported=True,score=0,parts=[],notes=[])
        elif o['kind']=='reroll': result=dict(supported=True,score=0,parts=[],notes=['原生免费重掷，不预知结果。'])
        elif o['kind']=='card' and o.get('card'): result=score(state,o['card'])
        else: result=dict(supported=False,score=None,parts=[],notes=['非抓牌选项，暂不比较。'])
        rows.append(dict(key=o['key'],name=o.get('name',''),enabled=o.get('enabled',True),**result))
    # Match the live reward-level priority: a ready first damage engine may beat
    # individually valuable utility. Single-card/shop/removal scores do not inherit it.
    if state.get('character')=='Defect' and not composition.has_damage_engine(state,PROFILES) and state.get('hp',0)>state.get('maxHp',1)*.25 and sum(o.get('enabled',True) and o['kind']=='card' for o in options)>=2 and any(o['kind']=='skip' and o.get('enabled',True) for o in options):
        eligible=set();deck=state['deck']
        for o in options:
            c=o.get('card') or {};name=c.get('className')
            if not o.get('enabled',True) or o['kind']!='card' or c.get('cost',99)>state.get('energy',3) or c.get('costsX'):continue
            if name not in {'AdaptiveStrike','Claw','AllForOne','Storm','Thunder','Voltaic','Darkness','ConsumingShadow','Spinner','FlakCannon'}:continue
            if name!='Claw' and any(d['className']==name for d in deck):continue
            if not composition.has_damage_engine(dict(state,deck=deck+[c]),PROFILES):continue
            if damage_plan.orb_committed(state) and name in {'AdaptiveStrike','Claw','AllForOne'}:continue
            if damage_plan.physical_committed(state) and name in {'Storm','Thunder','Voltaic','Darkness','ConsumingShadow','Spinner'}:continue
            eligible.add(o['key'])
        other=max([r['score'] for r in rows if r['enabled'] and r['supported'] and r['score'] is not None and r['key'] not in eligible] or [0])
        for r in rows:
            if r['key'] in eligible and r['supported'] and r['score'] is not None:
                bonus=min(8,max(0,max(2,other+1)-r['score']))
                if bonus>0:
                    r['parts'].append(dict(reason='现有组件已齐，优先补上首个可启动输出引擎',value=round(bonus,3)))
                    r['score']=round(r['score']+bonus,3)
    active=[r for r in rows if r['enabled']]
    best=None
    if active and all(r['supported'] and r['score'] is not None for r in active):
        # On a tie, keep the deck rather than introducing an arbitrary extra card.
        skips={o['key'] for o in options if o['kind']=='skip'}
        rerolls={o['key'] for o in options if o['kind']=='reroll'}
        best=max(active,key=lambda r:(r['score'],r['key'] in rerolls,r['key'] in skips,r['key']))['key']
    return dict(policyVersion=CONFIG['version'],recommendedKey=best,options=rows,warnings=warnings,
        confidence='low',decisionId=record.get('id'),runId=record.get('runId'))

def review_file(path):
    text=Path(path).read_text('utf-8-sig')
    try:
        obj=json.loads(text)
    except json.JSONDecodeError:
        obj=None
    if isinstance(obj,dict) and 'state' in obj and 'options' in obj:
        return [evaluate(obj)]
    entries=[json.loads(line) for line in text.splitlines() if line.strip()]
    choices={e['payload']['decisionId']:e['payload'] for e in entries if e.get('type')=='choice'}
    results=[]
    for e in entries:
        r=e.get('payload',{})
        if e.get('type')=='decision' and r.get('kind')=='cards' and r.get('state',{}).get('character')=='Defect':
            value=evaluate(r)
            value['actualChoice']=choices.get(r.get('id'))
            value['utc']=e.get('utc')
            results.append(value)
    return results

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('input',help='Existing decision JSON or session JSONL')
    parser.add_argument('--output',required=True)
    args=parser.parse_args()
    result=review_file(args.input)
    Path(args.output).write_text(json.dumps(result,ensure_ascii=False,indent=2,allow_nan=False)+'\n','utf-8')
    print(f'Rescored {len(result)} decisions; no game state changed.')

if __name__=='__main__': main()
