"""Bounded deck-direction priors. No future rewards or player choices are consulted."""
import composition
import damage_plan
import readiness
def adjustments(state, card, profiles):
    deck=state.get('deck',[]);name=card['className']
    def tags(c):return set(profiles.get(c['className'],{}).get('tags',[]))
    def number(*names):return sum(c['className'] in names for c in deck)
    def tagged(*names):return sum(bool(tags(c)&set(names)) for c in deck)
    def evidence(names):
        # Repeated weak pieces cannot manufacture a fully developed strategy.
        return sum(w*min(1.5,number(n)) for n,w in names.items())
    power=evidence(dict(Storm=2,Subroutine=2,Thunder=2,EchoForm=1.5,CreativeAi=1,MachineLearning=.5))
    dark=evidence(dict(Darkness=2,ConsumingShadow=1.5,ShadowShield=1,Null=1))
    physical=composition.physical(state)
    orb=composition.orbs(state)
    status=tagged('status_source')*.5+evidence(dict(Compact=1,FlakCannon=1.5,TrashToTreasure=2,Smokestack=2,Iteration=1.5))
    strongest=max(power,dark,physical,orb,status)
    act=max(1,min(3,int((state.get('map') or {}).get('contentAct') or state.get('act',1))))
    m=state.get('map') or {};progress=max(0,min(1,m.get('currentRow',0)/m['bossRow'])) if m.get('bossRow',0)>0 else .5
    horizon=(1-progress*.5) if act==1 else .35 if act==2 else 0
    healthy=min(1,max(.2,state.get('hp',75)/max(1,state.get('maxHp',75))/.5))
    copies=number(name);parts=[]
    def add(reason,v):
        if v:parts.append((reason,v))
    affinity=0
    if name in {'Storm','Subroutine','Thunder','EchoForm','MachineLearning'}:affinity=power
    elif name in {'Darkness','ConsumingShadow','ShadowShield','Null','MultiCast','Shatter','Capacitor'}:affinity=dark
    elif name in {'AdaptiveStrike','Claw','AllForOne','Scrape','Feral','BulkUp','HelixDrill'}:affinity=physical
    elif name in {'Compact','FlakCannon','TrashToTreasure','Smokestack','Iteration'}:affinity=status
    elif name=='Defragment':affinity=orb
    elif name=='Spinner':affinity=evidence(dict(Spinner=2,Glasswork=1,Defragment=1))
    core=name in {'AdaptiveStrike','Storm','Subroutine','Thunder','Darkness','Defragment','Claw','Scrape','TrashToTreasure','Iteration','Spinner'}
    investment=0
    if copies==0:
        investment=min(2.8,affinity*.8)*healthy
        # First engine can be considered before its payoff exists; not every speculative terminal.
        compatible=not(name in {'AdaptiveStrike','Claw','Scrape'} and damage_plan.orb_committed(state) and orb>=physical) and not(name=='Darkness' and power>=3 and dark==0)
        if damage_plan.physical_committed(state) and physical>orb and name in {'Storm','Thunder','Darkness','Spinner'}:compatible=False
        if core and compatible:investment+=1.6*horizon*healthy
    add('已有组件形成方向，保留有限未来投资',min(4,investment))
    if name=='Storm' and copies==0 and act==1 and composition.physical(state)<2:
        add('前期首张雷暴可建立产电方向，不要求先有能力牌',2.5*horizon*healthy)
    if name=='Hailstorm' and act>=2:
        add('冰雹固定伤害不随冰霜数量或集中成长',-2)
    if name=='EchoForm' and copies==0 and number('Storm','Subroutine','Thunder','Defragment','Loop','AllForOne')<2 and sum(c.get('rarity')!='Basic' and c.get('type') in {'Attack','Skill','Power'} for c in deck)>=2 and sum(c.get('cost',1)<=1 and c.get('type') in {'Attack','Skill'} for c in deck)>=2:
        add('缺少长战引擎时投资首张回响，不要求能力流已成型',3*healthy)
    # Physical/orb incompatibility is charged once by composition.
    if name in {'Darkness','ConsumingShadow','Null'} and dark==0 and power>=3 and act>=2 and composition.physical(state)<=composition.orbs(state):
        add('成熟主线不为新黑球支线额外占位',-min(4,power*.7))
    dark_committed=number('Darkness','ConsumingShadow')>0 and number('MultiCast','Quadcast','Shatter')>0 and number('Storm','Thunder','Voltaic')==0
    if act>=2 and dark_committed and name in {'Storm','Thunder'}:
        add('黑球蓄积与激发主线已成型，不为无配套产电支线占位',-6)
    filler=name in {'ColdSnap','BallLightning','FocusedStrike','ChargeBattery','Leap','CompileDriver','SweepingBeam','MomentumStrike','GunkUp','RocketPunch','Glasswork'}
    if filler and composition.has_damage_engine(state,profiles) and not composition.bridge_candidate(state,card,profiles):
        urgency=1
        # Keep emergency defense while lowering low-impact additions to a mature engine.
        frost=tagged('frost','frost2','frost3')
        if tags(card)&{'block','frost','frost2','frost3'} and state.get('hp',75)/max(1,state.get('maxHp',75))<.35 and frost<2:urgency=.4
        coverage=readiness.coverage(state,profiles)[0 if card['type']=='Attack' else 1]
        add('方向成型后过渡组件占用启动与抽牌',-min(4,(act-1)+strongest*.25)*urgency*coverage)
    if name=='Hologram' and any(c['className']=='Hologram' and c.get('upgradeLevel',0)>0 for c in deck):
        add('已有不消耗全息，新增回收需求递减',-1.5 if physical<3 else -.5)
    if name=='Hyperbeam' and orb>=2:
        add('球系启动回合损失集中与被动防线',-min(4,tagged('frost','frost2','frost3')*.8+tagged('focus')*.5))
    if act>=2 and strongest>=4 and affinity==0:add('成熟牌组新增卡的估值余量',-.6)
    return parts
