"""Damage anchors versus tentative components; mirrors DefectDamagePlan.cs."""
import composition
import copy_engine

def physical_committed(s):
    def n(*names):return sum(c['className'] in names for c in s['deck'])
    return bool(retrieval_loop(s) or copy_engine.active(s) or n('AdaptiveStrike') and n('Hologram','AllForOne','Scrape','Feral','BulkUp') or n('Claw')>=2 or n('Claw') and n('Hologram','AllForOne','Scrape','Feral','BulkUp') or n('HelixDrill') and n('DoubleEnergy','Turbo','Supercritical','BulkUp'))

def orb_committed(s):
    def n(*names):return sum(c['className'] in names for c in s['deck'])
    anchors=n('Storm','Thunder','Voltaic','Darkness','ConsumingShadow','Spinner')
    return bool(anchors>=2 or anchors and n('Defragment','BiasedCognition') or n('Darkness','ConsumingShadow') and n('Null','ShadowShield','Loop','MultiCast','Shatter','Quadcast') or n('Storm') and sum(c['type']=='Power' for c in s['deck'])>=3)

def adjustments(s,c,profiles):
    def n(*names):return sum(d['className'] in names for d in s['deck'])
    act=max(1,min(3,(s.get('map') or {}).get('contentAct') or s.get('act',1)))
    physical=physical_committed(s);orb=orb_committed(s);name=c['className'];copies=n(name)
    engine=composition.has_damage_engine(s,profiles);parts=[]
    supported=name=='Ftl' and physical and n('AllForOne','Feral')>0 or name=='Null' and not physical and n('Darkness','ConsumingShadow')>0
    if name in {'Ftl','Sunder','Null'} and not supported:
        coverage=sum(d.get('rarity')!='Basic' and d['type']=='Attack' for d in s['deck'])
        burden=max(0,coverage-1)*.9 if act==1 else 1.5+(act-2)*1.5+min(2,coverage*.6)
        if engine:burden+=1
        if burden>0:parts.append(('过渡攻击共用补伤害预算，不能逐张当成新缺口',-min(6,burden)))
    if name=='AdaptiveStrike' and copies==0 and not orb:
        draw=sum('draw' in profiles.get(d['className'],{}).get('tags',[]) or d['className']=='Reboot' for d in s['deck'])
        recall=n('Hologram','Feral','Scrape')
        parts.append(('适应打击自身生成零费复制输出，现有过牌与回收帮助兑现',2+min(3,draw*.6+recall*.8+n('Scavenge')*.5)))
    if copies==0 and not engine and composition.has_damage_engine(dict(s,deck=s['deck']+[c]),profiles) and not(orb and name in {'AdaptiveStrike','Claw'}) and not(physical and name in {'Storm','Thunder','Voltaic','Darkness','ConsumingShadow','Spinner'}):
        parts.append(('这张牌补齐现有输出引擎，而非继续增加辅助组件',3))
    if not engine and sum(d.get('rarity')!='Basic' and d['type'] in {'Attack','Skill','Power'} for d in s['deck'])>=5 and name in {'Reboot','Hologram','MachineLearning','Turbo','DoubleEnergy','Scavenge'}:
        utility=n('Reboot','Hologram','MachineLearning','Turbo','DoubleEnergy','Scavenge','Skim','Overclock')
        if utility>=3:parts.append(('输出主干未建立，已有多张运转供能不再重复占位',-min(3,(utility-2)*.75)))
    return parts

def retrieval_loop(s):
    return any(c["className"]=="AllForOne" for c in s["deck"]) and any(c["className"]=="Hologram" and c.get("upgradeLevel",0)>0 for c in s["deck"]) and any(c["className"]=="Turbo" for c in s["deck"]) and any(c["type"]=="Attack" and c.get("cost")==0 and not c.get("costsX") and "Exhaust" not in c.get("keywords",[]) for c in s["deck"])
