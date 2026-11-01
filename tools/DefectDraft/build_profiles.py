"""Author-maintained experimental priors. Regenerate JSON and review sheets locally."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# class | base | act1 | act2 | act3 | duplicate | upgrade delta | roles | judgment
# Values are hypotheses, not fitted coefficients, card tiers, or win probabilities.
# 0.13 review: Spire Codex A10 choice/lift evidence prompted native-mechanics
# review of six underpriced engines/resources; these values are NOT a stats fit.
# https://spire-codex.com/stats/cards?bracket=a10 (2026-10-27)
ROWS = '''AdaptiveStrike|4|1|0|-1|3|1|attack,zero_setup|本体先复制到弃牌堆再触发迅捷；首次生成的零费复制品可继承未用迅捷。万物一心帮助启动回收，已用实例复制不重置迅捷。
AllForOne|3.5|0|1|1|3.5|1|attack,zero_payoff,recur|回收弃牌堆零费牌；零费能力和打出后消耗的牌不作持续回收目标。有足量可循环零费技能时也可用于球系，不按物理专属硬禁。
BallLightning|4|1|0|-1|3|1|attack,lightning|前期补伤害并产球；已有足够伤害时第二三张显著降分。
Barrage|1|1|0|0|3|1.5|attack,orb_attack|依据可填满的球位估算命中，不按纸面球位直接当满球。
BeamCell|1|1|0|-1|3|1.5|attack,zero,physical_support|易伤主要帮助攻击牌；不能给充能球伤害乘易伤。
BiasedCognition|7|0|1|1|3|1|focus,scaling|先古牌，短战爆发强；长战集中下降，不能按永久集中估值。
BoostAway|3|1|0|0|2.5|1|block,zero,status_source|免费格挡但塞晕眩；状态联动存在时再提高。
BootSequence|4|1|1|0|4|1|block,zero,opening|固有防御保护启动，但多张会挤占首回合关键牌。
Buffer|5|0|1|1|3|3|block,scaling|防大伤害与启动保护；面对多段小伤害价值下降，升级提升明显。
BulkUp|2|0|1|1|3.5|2|physical_support,slot_loss,scaling|少球位不等于产球失效：满球后新球会顶球激发。重点评估蓄黑球和被动收益损失；暴涨当场删除的球不激发，零球位不能生成球。力量敏捷仍须有兑现方式。
Capacitor|0|-1|0|1|4|1|slots|蓄黑球时提供保留空间，避免新球过早顶掉核心；依赖顶球激发的构筑扩容会延后激发，不能一律按产球速度增分。
Chaos|1|0|0|0|3|3|random_orb|未升级仅随机一球；升级增加产量，不能把随机结果当指定冰霜。
ChargeBattery|4|1|0|-1|3|1|block,delayed_energy|可靠过渡防御；次回合能量不是本回合启动费用。
Chill|4|1|1|0|3|2|frost,zero,opening|多敌时强，单体仅一球；升级不再消耗，价值随补球需求变化。
Claw|2|0|0|0|0.15|1.5|attack,zero,claw|需要可实现的回收与低费循环；已有全息、万物一心、爪群时成长价值提高，不因一张爪无脑堆。
ColdSnap|3.5|1|0|-1|3|1|attack,frost|补伤害同时补冰霜；允许第一张过渡，但不无限堆低效攻击。
Compact|3.5|0|0.5|1|4|2.5|block,status_clear|第一张提供对敌方状态牌的泛用保险；自造状态时增强，与散射炮可互补。升级还将状态变化为升级燃料。
CompileDriver|3|1|1|0|3|1|attack,diversity_draw|过牌量来自已在场的球种，牌组拥有不同产球牌不保证同时到场。
ConsumingShadow|5|0|1|1|3|1|dark,evoke,scaling|生成黑暗并每回合激发最左球；与保留黑暗长时间蓄力存在冲突。
Coolant|2|-1|0|1|4|1.5|block,diversity_payoff,scaling|多球种持续防御；按可靠球种折扣，第一回合没有即时格挡。
Coolheaded|5|0|1|1|2.8|2.5|frost,draw|优秀防御循环组件；未升级仅自替换，不能给净赚牌分；多抓仍降分。
CreativeAi|3|-2|-1|0|5|1|scaling,power_generator,slow|三费随机慢启动；低于已有子程序、雷暴等确定性配合，不因金卡或能力流标签自动增分。
Darkness|3|0|1|1|3|3|dark,scaling|生成黑暗并触发所有黑暗被动，升级触发两次；已有黑暗、保留空间及多次激发时提高，不只当普通产一球。
DefendDefect|-6|0|0|0|4|1|basic,block|基础牌仅作为牌组能力和稀释参考，不主动新增。
Defragment|7|0|1|2|2.5|3|focus,scaling|集中支持闪电冰霜黑暗玻璃，不增强等离子；无相应产球则降低。
DoubleEnergy|5|0|1|1|0.8|2|energy,energy_multiply|需要起始能量、过牌与花费出口；未升级先花费再翻倍，不能当固定净赚。
Dualcast|1|1|0|0|3|2|evoke,basic|激发最右球两次；已有蓄黑球终端时提高，仍需排球。新增重复份数不能只因是激发牌就无限抓。
EchoForm|8|-1|1|2|5|0.5|scaling,slow|长战上限高但三费启动；先解决防御，不把升级去虚无等同于翻倍收益。
EnergySurge|0|0|0|0|3|1|multiplayer|多人专用，单人不评分。
Feral|0|-1|0|1|4|2|zero_payoff,scaling|只回收本回合首张零费攻击；零费技能不计入支持度。
FightThrough|3|1|0|-1|3|1.5|block,status_source|高即时格挡换两伤口；状态牌会影响下一轮抽牌。
FlakCannon|4|-1|0|1|4|2|attack,status_payoff,status_clear|清理状态并按数量多段输出；敌方塞牌按折扣先验，不假装已知未来。自造状态时可与压缩并存。
FocusedStrike|4|1|0|-1|3|1.5|attack,temp_focus|集中只持续本回合，按当回合球触发估值，不能当成长核心。
Ftl|3|1|0|0|3|1|attack,zero,conditional_draw|条件抽牌，后抽到时可能失效；不视作无条件零成本循环。
Fusion|3|-1|1|1|3|2|plasma,energy|等离子需要启动和排球；能量不等于过牌，升级移除消耗。
GeneticAlgorithm|1|4|0|-3|4|1|block,growing|越早越有培养期；后期新抓不能按养成成品评价，现有成长值另算。
Glacier|7|0|1|1|3|1|block,frost2|即时防御和持续防御兼具；重复份数由防御缺口和回转决定。
Glasswork|4|2|0|-1|3|1|block,glass|玻璃群伤补多敌能力；基础被动逐次衰减，不能算无限稳定输出。
GoForTheEyes|3|1|0|0|3|2|attack,zero,weak|攻击意图才挂虚弱；可补减伤，重复收益递减。
GunkUp|3|2|0|-1|3|1|attack,status_source|前期十二点伤害，代价是黏液；状态体系才放宽后续抓取。
Hailstorm|1|0|1|1|3|1.5|aoe,frost_payoff,scaling|必须稳定保有冰霜；是持续群伤，不是第一回合立即清场。
HelixDrill|2|-1|1|2|3|2|attack,zero,energy_payoff|按实际能花出去的能量计命中，不能把能量牌数量直接当输出。
Hibernate|0|0|0|0|3|1|multiplayer|模型标为多人专用，单人排除。
Hologram|4.5|0|1|1|3.8|2.5|block,recur|优先补第一张定向回收；已有优质回收目标或零费循环时增分，重复仍递减。升级去消耗很重要，但不等于无需费用。
Hotfix|3.5|0|1|1|2|1|temp_focus,zero|临时集中需本回合多球触发；未升级消耗，不按永久集中评分。
Hyperbeam|6|2|0|-1|3|1.5|attack,aoe,temp_focus_loss|本版本只当回合失去集中；群伤出色，但当回合冰霜防御仍可能受损。
IceLance|4|0|1|0|3|1|attack,frost3,expensive|三费攻防合一，手牌卡费时降分；球不是立刻按激发防御结算。
Ignition|0|0|0|0|3|1|multiplayer|目标另一名玩家，单人排除。
ImitationLearning|0|0|0|0|3|1|multiplayer|目标另一名玩家，单人排除。
Iteration|0|-1|0|1|4|1.5|status_draw,scaling|每回合首次抽到状态才触发；状态生成不等于立即过牌，诅咒不触发。
Leap|2|1|-1|-2|3|1|block|纯过渡格挡，后期已有冰霜等防线时降低。
LightningRod|4|1|0|0|3|1|block,lightning|闪电在后两个回合生成，有延迟；与立即产两球区分。
Loop|2|-1|1|1|3|2|orb_payoff,scaling|触发最右球；需要稳定球质量，非立即双倍所有球。
MachineLearning|5|-1|0|1|4|0|draw,scaling|下回合才开始赚牌；升级固有不自动加分，避免挤掉启动防御。
MeteorStrike|2|-1|0|1|4|1|attack,plasma,meteor|五费启动是硬门槛；本牌生成的等离子不能反过来支付首次费用。
Modded|2|0|1|1|4|1.5|slots,draw|首次零费，此后费用上涨；不能当永远零费抽牌扩容。
MomentumStrike|4|2|0|-1|3|1|attack,zero_setup|首次仍一费，之后零费；回收联动需要先打出，不当开局零费牌。
MultiCast|2|-1|0|1|3|1.5|evoke,energy_payoff|最右球多次激发的 X 费终端；蓄黑球与费用充足时提高，仍需排球和防守。
Null|4|0|1|0|3|1.5|attack,dark,weak|两费挂虚弱并产黑暗；和一费过渡攻击比较时计入启动成本。
OneForAll|0|0|0|0|3|1|multiplayer|模型标为多人专用，单人排除。
Overclock|5|0|1|1|1.6|1.5|draw,zero,status_source|净抽一张但塞灼伤；状态引擎和费用充足时提高，不无脑堆。
Quadcast|4|0|1|1|3|2|evoke,basic|先古牌，激发最右球四次；提高蓄黑球兑现价值，不能把左侧成熟黑球假设为已在右侧。
Rainbow|2|0|1|0|3|1|lightning,frost,dark,diversity|补球种和一次补球；两费启动，升级去消耗不一定更好。
Reboot|5|0|1|1|3|1|draw,zero|重洗手牌与弃牌，有机会重抽废牌；不当稳定定向检索。
Refract|3|1|0|-1|3|1|attack,glass,expensive|三费多段加两玻璃，偏多敌；需要能量和当回合防守。
RocketPunch|2|1|0|0|3|1.5|attack,draw,status_discount|状态生成降低下次费用；不能在没有来源时按零费抽牌攻击估值。
Scavenge|3|0|1|1|3|1|exhaust,delayed_energy|可处理诅咒与过渡牌，能量下回合才到；不等同于本回合启动。
Scrape|2.5|0|0|0|3.5|1.5|attack,zero_payoff|按零费密度估计留下的牌数，零费数量不能代替密度；非零费核心可能被丢弃。
ShadowShield|4|0|1|0|3|1|block,dark|即时格挡兼黑暗启动；两费要考虑和其他启动牌争费用。
Shatter|5|0|1|1|3|1|attack,aoe,evoke_all|所有球双激发，补充蓄黑球终结与群伤；清球后失去后续被动，不能重复计入防御。
SignalBoost|4|0|0|0|4|2|power_payoff|有值得复制且付得起的能力牌才抓；先打它本身也占牌和能量。
Skim|3|0|1|1|3|1.5|draw|一费抽三净赚牌；需要能量或低费牌兑现，多张仍有卡手成本。
Smokestack|-1|-1|0|1|4|1.5|status_payoff,aoe,scaling|触发是生成状态，非抽到；无来源时不赌未来拼图。
Spinner|4|0|1|1|3|1.5|glass,scaling|持续补玻璃弥补衰减，升级立即产一球；仍需首回合防御。
Storm|4|0|1|1|3.5|3|power_payoff,lightning_engine|能力机产电核心；按已有能力触发量估算，升级每次生成两球。允许有限前期投资，已有能力方向时提高，仍考虑启动费用。
StrikeDefect|-6|0|0|0|4|1|basic,attack|初始输出只算有限即时能力，不主动新增。
Subroutine|4|0|1|1|3.5|2.5|power_payoff,power_energy|能力机费用核心；包括雷暴、雷霆等能力作为触发支持，不当无条件返能。
Sunder|5|2|0|-1|3|1.5|attack,expensive|前期斩杀退款强；无法斩杀首领时不能稳定计三点返能。
Supercritical|5|0|1|1|3|1.5|energy,zero|零费消耗获得四能量；需要过牌或昂贵牌兑现，单张不等于陨石必启动。
SweepingBeam|4|2|0|-1|3|1|attack,aoe,draw|前期补群伤和自替换；无净抽牌，后期重复过渡牌降分。
Synchronize|0|-1|0|0|3|2|temp_focus,diversity_payoff|按当回合可实现球种与触发估值，不当永久集中。
Synthesis|2|1|0|0|3|1|attack,power_discount|要先花两费；下一张能力减费，需可用高费能力才赚。
Tempest|1|-1|0|1|3|1.5|lightning,energy_payoff|X费，按剩余费用和集中兑现；不计为零费或稳定固定数量产球。
TeslaCoil|1|0|0|1|3|2|attack,zero,lightning_payoff|需要已有闪电球；能定向触发但不是平白生成闪电。
Thunder|3|0|1|1|3.5|1.5|lightning_payoff,scaling|雷暴的持续产电支持激发终端；仍需产生和激发闪电，不能把它自身当产球牌。
TrashToTreasure|0|-1|1|1|4|2|status_payoff,random_orb_engine|生成状态才随机产球；不把诅咒或仅抽到状态当作触发。
Turbo|4.5|0|1|0.5|3.5|1.5|energy,zero,status_source|按能量缺口、过牌与高费核心评估第一张；虚空仍有后续代价，不是每套牌无条件必抓。
Uproar|2|1|0|-1|3|1|attack,expensive|随机打出抽牌堆攻击，有高费优质攻击时更好；低质攻击池降分。
Voltaic|2|-1|0|1|4|1.5|lightning_payoff,expensive|雷暴持续产电与雷霆支持时是终端；不能用自身重放闪电反推首次产电，仍需支付三费。
WhiteNoise|3|0|0|0|3|1.5|power_generator|随机能力当回合免费，先验收益折扣；不按命中回响的最好结果计分。
Zap|1|1|0|0|3|2|lightning,basic|初始产球，升级降费；新增份数按现有产球量而非固定强度判断。'''

def build():
    cards = json.loads((ROOT/'data/defect-cards-v0.111.0.json').read_text('utf-8-sig'))
    profiles = {}
    for line in ROWS.splitlines():
        name, base, a1, a2, a3, dup, upgrade, tags, note = line.split('|')
        assert name not in profiles, name
        profiles[name] = dict(base=float(base), phase=[float(a1),float(a2),float(a3)],
            duplicate=float(dup), upgradeDelta=float(upgrade), tags=tags.split(','), note=note,
            confidence='low' if name in {'BulkUp','FlakCannon','Coolant','Feral','Modded','Synchronize','Voltaic','Uproar'} else 'medium')
    assert set(profiles)=={c['basic']['className'] for c in cards}
    doc=dict(version='defect-draft-0.14',gameVersion='0.111.0',status='manual-review-calibrated-prior-not-winrate-trained',
        scope='Singleplayer Defect experimental automation; card priors plus character adapters for route, shop and rest. Combat requires CombatSolver; unknown effects use explicit fallbacks.',
        coefficients=dict(cardTax=2.5,damageNeed=2.0,blockNeed=2.0,duplicateQuadratic=.65,synergyCap=5.0),
        cards=profiles)
    (ROOT/'data/defect-draft-v0.14.json').write_text(json.dumps(doc,ensure_ascii=False,indent=2)+'\n','utf-8')

if __name__=='__main__': build()
