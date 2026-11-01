"""Build reviewed Silent priors, with a small, auditable community-stat adjustment.

No live downloads. Native exports and a pinned public statistical snapshot are inputs.
The numeric priors and coefficients are hypotheses, not fitted win probabilities.
"""
import json
import statistics
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# class | mechanism base | act1,2,3 | duplicate | upgraded delta | functional tags | review
ROWS = r'''
Abrasive|5.8|0,0,0|2.7|1|sly dex thorns|奇巧提供弃牌免费启动；荆棘依赖敌方攻击频率，不能当主动输出核心。
Accelerant|3.6|0,0.7,0.7|2.8|2|poison_payoff scaling|已有稳定施毒时的伤害放大器；每次触发都会减一层毒，不是无损乘法。
Accuracy|3.4|0,0.5,0.5|2.2|1.6|shiv_payoff scaling|需要实际产刀；多份加伤可叠加，但产刀量不增加时收益递减。
Acrobatics|5.4|0,0.5,0.5|1.2|1.5|draw discard|抽三弃一，算上自身净手牌仅加一；弃牌触发奇巧，不能同时当三个净抽牌。
Adrenaline|7.8|0,0,0|0.7|1.5|draw energy exhaust|零费抽二供能且消耗，启动和爆发均强；仍比较更紧缺的输出核心。
Afterimage|7.2|0,0,0|2.2|0.8|play_block scaling|多次出牌转格挡，适配小刀及弃牌运转；并不解决输出缺口。
Anticipate|1.6|0,-0.3,-0.5|3|1.2|temp_dex|仅本回合敏捷；需要同回合多张格挡牌，不计作永久成长。
Assassinate|4.6|1,0,-0.6|3|1.2|attack weak_open exhaust|固有零费伤害易伤，重视首轮，第二份挤占起手；易伤不增幅毒。
Backflip|5.4|0,0.3,0.3|1.3|0.8|draw block|五格挡抽二，是通用运转兼防御；不能用不断补后空翻代替输出主线。
Backstab|3.4|1.4,0,-1|3.5|0.7|attack opening exhaust|零费固有即时伤害，前期止损；不把首轮一次攻击当长战成长。
BladeOfInk|6.5|0.5,0.4,0.2|1.8|1.8|shiv_source weak attack|可重复产墨影小刀，每把施加一虚弱；本机墨影不是抽牌附魔。
BladeDance|4.5|0.8,0,-0.5|2|1.4|shiv_source attack exhaust|一费三把小刀，但本体消耗；有刀刃陷阱可回放小刀，不算可无限重复的产刀。
Blur|4.1|0,0.5,0.6|2|0.8|block carry_block|保留现有格挡到下一回合；有格挡盈余才值钱，不凭空生成壁垒。
BouncingFlask|3.8|0.5,0,0|2.2|1.1|poison_seed poison attack|单体九层毒，多敌分散；毒伤延迟且会衰减，不能算立即九伤三次。
BubbleBubble|3.6|0,0.5,0.5|2|1.2|poison_conditional poison|先有中毒才施加九层，本身不是独立毒启动器。
BulletTime|3.6|-0.5,0.5,0.8|4|2.4|cost_cheat|需要大手牌和高费出口；本回合禁抽，惩罚依赖继续抽牌的运转链。
Burst|4.5|0,0.5,0.5|3|1.8|repeat_skill|重放的是技能；结合实际产刀、施毒、过牌与防御目标，不能直接重放能力。
CalculatedGamble|5.4|0,0.7,0.7|1.6|1.2|draw discard refresh exhaust|弃全手再抽等量，刷新不是永久烧牌；本版本升级增加保留，仍然消耗，不能按旧版无限循环建模。
CloakAndDagger|4.5|0.6,0,0|2|1.4|block shiv_source attack|格挡附带小刀，升级加一刀；小刀加伤与敏捷均可受益。
CorrosiveWave|3.2|-0.6,0.5,1|3|1.6|poison_draw poison_payoff|只持续本回合，打出后抽牌才施毒；没有同回合过牌时不能作为稳定毒源。
DaggerSpray|3.1|1.2,-0.3,-1|3|1.1|attack aoe multihit transition|前期补群攻，多段吃力量；已补足即时输出后明显递减。
DaggerThrow|3.4|0.8,-0.3,-0.8|2.6|0.7|attack discard transition|抽一弃一含自身净少一牌；可触发奇巧，不能当稳定正过牌核心。
Dash|3.8|1,-0.4,-0.8|3|1|attack block transition|两费攻防一体帮助过渡；仍需为后续输出增长留空间。
DeadlyPoison|3.1|0.5,0,-0.5|2.5|0.9|poison_seed poison transition|一费施毒入口，有触媒或持续补毒时更好；单张并不代表毒流成型。
DefendSilent|-5|0,0,0|3|0.3|basic block|基础防御不是奖励抓取目标；保留与删牌必须另行评估。
Deflect|3|0,0.1,0.2|2|0.9|block cheap|零费格挡需要过牌或敏捷支持；抽牌占位仍有成本。
DodgeAndRoll|3|0.3,0,0|2.6|1.2|block dex_payoff|两回合格挡两次吃敏捷；下一回合价值打折，不当本回合八格挡。
EchoingSlash|3.9|1.4,0,-0.8|3|1|attack aoe transition|一费群攻，击杀后再触发；有斩杀目标才增加连锁收益。
Envenom|3.7|-0.4,0.5,0.6|3|1.8|hit_poison scaling|每次未被格挡的攻击才施毒；多刀/多段可接毒，不能硬拆成互斥流派。
EscapePlan|4.5|0,0.2,0.2|1.8|0.6|draw cheap conditional_block|抽一自替换，抽到技能才给格挡；技能密度和出牌惩罚都影响实际价值。
Expertise|4|0,0.2,0.2|2.4|1.4|draw retain|本版本抽二并给这些牌当回合保留，不是旧版抽至六张。
Expose|4.9|0.2,0.2,0.2|3|0.7|vulnerable exhaust strip_artifact|清格挡人工制品并施加易伤；攻击流增伤，毒系主要受益于清人工制品。
FanOfKnives|5.1|0.5,0.5,0.2|3.5|1|shiv_source shiv_payoff aoe attack|能力启动自带四把刀，把所有小刀改为群攻；不要求事先已有产刀才能入选。
Finisher|2.8|-0.3,0.4,0.6|3|1.2|attack attack_payoff multihit|需要本回合先打多次攻击；有产刀才是有效终端，不能裸当多段高伤。
Flechettes|3|0.4,0,-0.2|3|1|attack skill_payoff multihit|按当前手牌技能数打多段；技能保留和大手牌支持，不按整个牌组技能数计伤害。
FlickFlack|2.5|0.8,-0.4,-0.8|3|0.7|attack aoe sly transition|被主动弃置时奇巧免费群攻；缺弃牌时只是低数值过渡。
Sidestep|2.8|0,0.3,0.2|3|1.2|next_energy|供能发生在下一回合，不修复当前启动缺口；评估实际高费出口。
Footwork|5.9|0.2,0.2,0.2|2|1.5|dex scaling|稳定敏捷成长，普通格挡牌受益；不增幅余像的固定格挡，不是输出核心。
GrandFinale|1.4|-0.8,0,0|5|0.6|attack aoe empty_draw|必须抽牌堆为空；精简、保留、可控过牌仅提高可行性，不宣称必定无限。
HandTrick|3.3|0.3,0.2,0|2.4|0.8|block grant_sly|当回合给一张技能奇巧，需要之后主动弃置才能兑现。
Haze|3.8|0.3,0.3,0|2.5|1.5|poison_seed poison weak aoe|群体施毒及虚弱，二费有启动负担；升级延长虚弱，不能只按毒数判断。
HiddenDaggers|4.7|0.5,0.5,0.2|1.8|1.1|discard shiv_source attack cheap|零费弃二产二刀，弃牌与产刀的桥梁；没有奇巧时有实际手牌损耗。
InfiniteBlades|3.6|0,0.3,0.2|2.5|0.3|shiv_source slow scaling|每回合仅一刀，长战稳定产刀；不是本回合爆发，固有升级会占起手。
KnifeTrap|4.7|0,0.7,1|3|1.8|shiv_payoff finisher scaling|回放已消耗小刀，是刀系长战输出；需先产刀并打出，不能空场启动。
LeadingStrike|4|0.8,0,-0.3|2.3|0.7|attack shiv_source transition|低本体伤害加两刀，能享受刀增伤；补足产刀后不靠多抓堆牌。
LegSweep|5.4|0.2,0.3,0.3|2|1.1|block weak|大量格挡和持续虚弱兼顾；需要二费，不解决长战输出。
Malaise|6.2|0,0.3,0.3|2.2|1.7|weak strength_down exhaust|X费降力量及虚弱，强防御功能；按多段敌人/费用看价值，不计为自身力量成长。
MasterPlanner|3|0,0.5,0.8|6|1.8|grant_sly engine|技能先正常打过才获奇巧；单层能力不叠加，需要弃牌与可回转技能，不能当立即全手免费。
MementoMori|2.7|0.4,0.2,0|3|1.1|attack discard_damage|本回合弃牌量转伤害，弃全手仍需重抽或保留此牌再打；不是跨回合成长。
Mirage|2.8|-0.3,0.5,0.7|2.5|1.8|poison_block block_payoff exhaust|格挡依赖敌方已叠的毒；零毒时无收益，升级去消耗支持持续防御。
Murder|3|-0.6,0.4,1|4|2.1|attack draw_damage finisher scaling|战斗累计抽牌提供伤害，初始很低；稳定过牌/保留才能成为有效长战终端。
Neutralize|-2|0,0,0|3|1|basic attack weak|零费虚弱基础牌，其已有保留价值不等于值得再抓一张。
Nightmare|3|-0.5,0.4,0.8|4|1.8|copy delayed|复制目标下回合到手，要支付目标费用；强目标、防守窗口和供能缺一不可。
NoxiousFumes|4.5|0.3,0.5,0.4|2|1.2|poison_seed poison slow scaling aoe|持续全体施毒，具备独立长战增长并可供触媒；启动慢，仍需即时伤害。
Outbreak|5|-0.1,0.7,0.8|3|1.3|poison_seed poison finisher aoe|自带全体九毒并立刻触发，可从零毒启动；有触媒/既有毒进一步受益，三费需预算。
PhantomBlades|3.7|0,0.4,0.4|4.5|0.8|shiv_payoff retain|所有刀保留，每回合仅第一刀加伤；不按每把刀都加九计算。
PiercingWail|5|0.3,0.3,0.3|1.7|0.7|strength_down exhaust|全体当回合降力量，对多段强；不能永久封锁敌人，也不替代所有持续防守。
Pinpoint|2.7|0.4,0,-0.3|3|0.8|attack skill_discount transition|本回合技能打出后减费，不是牌组有技能就零费；启动顺序影响收益。
PoisonedStab|2.8|0.9,-0.4,-0.8|3|1|attack poison_seed poison transition|即时伤害加少量毒，可补早期缺口；低毒源不应独自证明成熟毒体系。
Pounce|3|0.8,0,-0.3|3|1.2|attack skill_discount transition|下一张技能免费，需要手里有值得减费的技能；对廉价技能组不虚构省费。
PreciseCut|2.4|0.7,-0.2,-0.5|3|0.7|attack cheap empty_hand transition|手牌越多伤害越低；和大手牌保留有张力，弃牌可改善。
Predator|3.1|1,-0.2,-0.6|3|1|attack delayed_draw transition|下回合抽二，不是当回合过牌；前期补单体，后期重复更差。
Prepared|3.1|0,0.5,0.5|1.6|1.7|discard filter cheap|抽一弃一不增加手牌，升级抽弃二；配奇巧与弃牌收益，不裸当零费抽一。
Reflex|2.8|-0.5,0.5,0.7|2|1.5|sly draw|三费抽二，主要靠主动弃置免费打出；一个生存者不足以视为可靠启动。
Ricochet|2.4|0.7,-0.2,-0.6|3|0.9|attack multihit sly transition|两费随机四段，奇巧可绕过费用；多敌不可当定点斩杀。
SerpentForm|4.5|-0.2,0.8,1|3|1.8|play_damage finisher scaling|每次出牌对随机敌人造成固定伤害，小刀/奇巧/廉价过牌都是入口；三费启动需防守。
ShadowStep|2.8|-0.6,0.3,0.6|4|1.3|discard next_attack|弃掉全手换下回合攻击翻倍；不增幅毒或群蛇固定伤害，计算本回合损失。
Shadowmeld|4.3|0,0.5,0.7|3|1.3|block_payoff|仅本回合格挡翻倍，必须有可兑现的格挡来源。
Skewer|2.8|0.6,0,-0.3|3|1.2|attack multihit energy_sink transition|X费多段出口，有额外能量或力量时更好；不把X牌看成零费攻击。
Slice|2|0.8,-0.5,-1|3|0.7|attack cheap transition|零费单次伤害，占抽牌；缺伤害时可过渡，不因为能打出就持续抓。
Snakebite|3.3|0.4,0.1,0|2.7|1.2|poison_seed poison retain|二费七毒可保留，比较毒密度与费用；保留帮助配触媒但不制造能量。
Speedster|2.4|-0.6,0.4,0.7|3|0.4|draw_damage aoe scaling|只对回合进行中的抽牌触发，不把起手自然抽牌计为输出；缺过牌不能独立救缺伤害。
StormOfSteel|3.3|-0.4,0.4,0.7|3|1.4|discard shiv_source attack refresh|弃全手换小刀，能触发奇巧；会失去原手牌，不当纯正收益产刀。
Strangle|2.7|0.5,0.3,0|3|1.2|attack play_damage transition|只在本回合后续出牌触发；运转可提高，但不等价于持续群蛇形态。
StrikeSilent|-6|0,0,0|3|0.3|basic attack|基础打击不作为奖励抓取目标；删牌逻辑另行评估。
SuckerPunch|2.9|0.8,-0.3,-0.7|3|1|attack weak transition|过渡伤害带虚弱；已有中和/墨之刃/扫腿时不无限补相同功能。
Suppress|6|0.5,0.2,0.2|3|1.3|attack weak opening|先古强化中和，固有零费强虚弱；不能套用普通中和或普通抓牌概率。
Survivor|-2|0,0,0|3|0.5|basic block discard|基础弃牌入口；仅一张不足以保障奇巧链稳定，已有牌保留另评。
Tactician|2.6|-0.6,0.5,0.7|2.5|1.7|sly energy|手打三费回一费亏费；主动弃置才是供能，需要可靠弃牌和费用出口。
TheHunt|3.8|1.2,0,-1|3|0.8|attack exhaust reward_growth|斩杀再获卡牌奖励是早期成长机会；未知奖励不是已经到手的战力。
ToolsOfTheTrade|6|0,0.4,0.4|2.6|1.2|draw discard slow engine|每回合抽一弃一是选牌过滤并触发奇巧，不是每回合净加一手牌。
Tracking|3.6|0,0.5,0.5|6|1.6|weak_payoff scaling|对虚弱敌人的攻击增伤，需要可靠虚弱及攻击伤害；毒和群蛇固定伤害不受益。
Untouchable|2.8|0.1,0.3,0.3|2.4|1|sly block|两费六格挡很差，弃牌免费触发才有价值。
UpMySleeve|2.7|0.1,0.2,0.2|3|1.3|shiv_source attack ramp_cost|初次两费产三刀，打出后本场降费；循环速度决定后续收益，不按永久零费。
WellLaidPlans|5.5|-0.2,0.7,0.8|6|2|retain engine|二费金卡，全部手牌不丢弃；帮助组合配齐，但手牌上限和启动费用仍是限制。
WraithForm|6.8|0,0.3,0.4|3|2.2|intangible exhaust_window|先古牌；限时无实体换持续掉敏捷，需在窗口内结束或另有防御；不是普通金卡候选。
'''

def main():
    native = json.loads((ROOT/'data/silent-cards-v0.111.0.json').read_text('utf-8'))
    stats = json.loads((ROOT/'data/silent-community-2026-11-01.json').read_text('utf-8'))
    statmap = {r['className']:r for r in stats['cards']}
    profiles = {}
    for row in ROWS.strip().splitlines():
        name,base,phase,duplicate,upgrade,tags,note = row.split('|')
        profiles[name] = dict(base=float(base),phase=list(map(float,phase.split(','))),duplicate=float(duplicate),
            upgradeDelta=float(upgrade),tags=tags.split(),note=note,confidence='initial-mechanism-prior-not-run-validated')
    excluded = [r['basic']['className'] for r in native if r['multiplayerConstraint']=='MultiplayerOnly']
    expected = {r['basic']['className'] for r in native} - set(excluded)
    assert set(profiles)==expected,(set(profiles)-expected,expected-set(profiles))
    for name,p in profiles.items():
        r=statmap.get(name)
        # Compare only same-rarity, same-version, same-mode, same-ascension observations.
        # Raw holder win rate remains inspectable but is NOT a causal strength coefficient.
        if not r or r['sample'] < 200 or r['offered'] < 500 or r['lift'] is None:
            p['evidenceAdjustment']=0.0
            continue
        peers=[x for x in stats['cards'] if x['rarity']==r['rarity'] and x['sample']>=200 and x['lift'] is not None]
        medlift=statistics.median(x['lift'] for x in peers)
        medpick=statistics.median(x['pickRate'] for x in peers)
        raw=(r['lift']-medlift)*.06+(r['pickRate']-medpick)*.008
        p['evidenceAdjustment']=round(max(-.8,min(.8,raw))*r['sample']/(r['sample']+1000),3)
    doc=dict(version='silent-draft-0.2',gameVersion='0.111.0',
        scope='Mechanism-based Silent advice, shared global takeover and bounded counterfactual foresight; not win-rate calibrated.',
        statistics='silent-community-2026-11-01.json',excludedMultiplayer=excluded,
        coefficients=dict(cardTax=2.5,positiveSynergyCap=6,duplicateQuadratic=.45),cards=profiles)
    (ROOT/'data/silent-draft-v0.2.json').write_text(json.dumps(doc,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(f'{len(profiles)} profiles; {len(excluded)} multiplayer exclusions')

if __name__=='__main__':main()
