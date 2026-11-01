"""Frozen synthetic cross-language fixtures, no user run data."""
import json
import evaluate as e

cases=[]
for act,extras,energy,hp in [(1,[],3,75),(2,['Glacier','Defragment','Coolheaded','ColdSnap'],3,25),
    (2,['FightThrough','GunkUp','Overclock','TrashToTreasure'],3,60),
    (3,['Ftl','GoForTheEyes','BeamCell','Hologram','AllForOne','Claw'],4,60),
    (3,['Fusion','Supercritical','MeteorStrike','Loop','Skim'],5,75),
    (2,['Storm','Thunder','Subroutine','Buffer','CreativeAi'],3,60),
    (2,['Turbo','Overclock','FightThrough','Compact','FlakCannon'],3,60),
    (2,['Claw','Claw','AllForOne','Scrape','Feral','Hologram','Ftl'],3,60),
    (3,['Glacier','Defragment','Coolheaded','Darkness','Loop','Storm'],3,60),
    (2,['Darkness','Darkness','Capacitor','Loop','MultiCast','Shatter'],4,60),
    (2,['Storm','Thunder','Buffer','BallLightning','Coolheaded'],3,60),
    (2,['BulkUp','BulkUp','Storm','Thunder','BallLightning'],3,60),
    (1,['IceLance','Thunder'],3,57),
    (1,['Storm','Subroutine'],3,8),
    (3,['Storm','Thunder','Defragment','Glacier','Loop','EchoForm'],3,60),
    (2,['Turbo','Supercritical','Reboot','BootSequence'],3,60),
    (2,['Claw','AllForOne','Feral','Storm','Thunder','Defragment'],3,60),
    (2,['AdaptiveStrike','AllForOne','Hologram'],3,60),
    (3,['Darkness','Hologram','Coolheaded','Coolheaded','Defragment'],3,60),
    (3,['Darkness','Hologram','Coolheaded','Coolheaded','Defragment'],6,60),
    (2,['Claw','Claw','Claw','Hologram','AllForOne','Scrape'],3,60),
    (2,['Claw','Claw','Claw','Claw','Claw','Hologram','AllForOne'],3,60),
    (2,['DoubleEnergy','DoubleEnergy','Hologram','Skim','MultiCast'],3,60),
    (1,['LightningRod'],3,37),
    (1,['LightningRod','SweepingBeam'],3,36),
    (2,['Storm','Thunder','Glacier','DoubleEnergy','SignalBoost','Catastrophe'],3,24),
    (2,['Claw','AllForOne','Scrape','Finesse','FlashOfSteel','Volley'],4,60),
    (3,['Darkness','Capacitor','Loop','Defragment','MasterOfStrategy'],3,30),
    (1,['Spinner','FightThrough','DoubleEnergy','BootSequence'],3,60),
    (1,['Hologram','Turbo','Ftl'],3,60),
    (3,['Hologram','Scavenge','Reboot','Sunder','Ftl','Null'],3,60),
    (1,['Glacier','Coolheaded','Hologram','Turbo','Scavenge'],3,60)]:
    state=e.starter();state.update(act=act,hp=hp,energy=energy)
    state['deck'] += [e.snapshot(n) for n in extras]
    if 'DoubleEnergy' in extras or extras in (['LightningRod'],['LightningRod','SweepingBeam']):state['relics'].append('RELIC.ICE_CREAM')
    if 'SignalBoost' in extras:state['relics']+=['RELIC.INFUSED_CORE','RELIC.MUMMIFIED_HAND','RELIC.PERMAFROST']
    if 'Volley' in extras:state['relics']+=['RELIC.CHEMICAL_X','RELIC.KUNAI','RELIC.SHURIKEN','RELIC.SOZU']
    if 'MasterOfStrategy' in extras:state['relics']+=['RELIC.DATA_DISK','RELIC.RUNIC_CAPACITOR','RELIC.GOLD_PLATED_CABLES','RELIC.RUNIC_PYRAMID']
    state['map']={'contentAct':act,'currentRow':4 if hp>30 else 12,'bossRow':16}
    if 'Hologram' in extras:state['deck']=[e.snapshot('Hologram',True) if c['className']=='Hologram' else c for c in state['deck']]
    if 'Hologram' in extras and 'Darkness' in extras:state['deck']=[e.snapshot('Dualcast',True) if c['className']=='Dualcast' else c for c in state['deck']]
    if 'AdaptiveStrike' in extras:state['deck']=[dict(c,enchantment='ENCHANTMENT.SWIFT',enchantmentAmount=2,modified=True) if c['className']=='AdaptiveStrike' else c for c in state['deck']]
    scores=[dict(name=n,upgraded=up,score=e.score(state,e.snapshot(n,up))['score']) for n in list(e.PROFILES)+list(e.NEUTRAL) for up in (False,True)]
    cases.append(dict(state=state,scores=scores))
(e.ROOT/'tests/SpireStrategist.Tests/Fixtures/defect-prior-v14.json').write_text(json.dumps(cases,ensure_ascii=False,indent=2)+'\n','utf-8')
