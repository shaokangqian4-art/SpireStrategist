"""Reproducible Regent/Necrobinder mechanism priors, native 0.111.0 only.
Input statistics and localized native exports are checked in under data/; no network during builds.
Columns: class | base | duplicate | upgrade delta | tags | mechanism note.
Scores are acquisition estimates, not probabilities or validated win rates.
"""
import json, pathlib, statistics
ROOT=pathlib.Path(__file__).resolve().parents[2]
ROWS={
'regent':r'''
Alignment|4.8|3|1.2|energy|花星换能量，不是无条件零费供能。
Arsenal|5.8|4|1.6|generation_payoff strength scaling|每次生成牌获得力量；需要生成频率和攻击出口。
AstralPulse|4|3|1.3|attack aoe multihit transition|花星的群体两段伤害，不能按免费白卡计算。
BeatIntoShape|5.8|3|2|forge sword_engine multihit_payoff|先命中再按本回合命中次数铸造，连击提高成长。
Begone|2.8|4|1.5|transform generation cleanup|将一张手牌变仆从打击；能处理杂牌但转化消耗一费。
BigBang|7|3|2|draw energy star_source forge generation|一次性抽牌、两种资源及铸造；升级固有，非永久供能。
BlackHole|5.6|3.2|2|star_engine scaling aoe|每次获得或花星触发，不按星数量逐点触发。
Bombardment|5.8|3|1.8|attack scaling autonomous|三费启动后消耗区每回合自动攻击，独立长战输出。
Bulwark|4.7|3|1.5|block forge|两费防御兼铸造；需要能量安排。
BundleOfJoy|4.4|4|1.2|generation draw_random|随机无色牌不是抽牌；依赖生成联动且仍需支付牌费。
CelestialMight|4.1|3|1.8|attack multihit transition|两费三段过渡，力量收益高，不能替代成长核心。
Charge|3.5|4|1.3|transform generation cleanup|把抽牌堆两张牌变仆从，优先基础与污染牌，保护核心。
ChildOfTheStars|5.2|3|2|star_block block|按花掉的星数产格挡，须有实际星收入和消费。
CloakOfStars|3.3|3.5|1.4|block|星费防御会与星费攻击竞争资源。
CollisionCourse|3.2|3.5|1.2|attack transition self_status|星费攻击加碎屑手牌；不能忽略污染。
Comet|6.1|3.6|1.5|attack star_finisher weak vulnerable|五颗星的大伤害与三层双减益，需要供星。
Conqueror|5.2|4|1|forge sword_payoff|本回合剑翻倍，需要同回合拿到并打出君王之剑。
Convergence|4.2|3|1|retain energy star_source|保留手牌并把能量和星延至下回合，不能算当回合供能。
CosmicIndifference|3.5|3.5|1|block recall|格挡与弃牌顶抽，回收核心但占下次抽牌位。
CrashLanding|3.7|5|1|attack aoe self_status|群伤后碎屑填手，必须折算污染与抽牌阻塞。
CrescentSpear|3.1|3.5|1|attack transition|按星费牌数量增伤，不等于持续成长发动机。
CrushUnder|4.1|3|1.3|attack aoe weak transition|早期群伤加当回合降力量，长战成长有限。
DecisionsDecisions|6.3|3.5|1.5|draw repeat_skill|抽牌后重放技能三次；需实际高价值技能并考虑费用。
DefendRegent|.5|4|.5|basic block|初始防御，升级低优先级。
Devastate|4.3|4|1.4|attack conditional transition|一能量加四星的高伤单击，没有额外禁抽限制；不能按一费35裸伤评价。
DyingStar|5.3|3|1.4|attack aoe strength_down ethereal|群体大幅暂降力量，主要是高压回合防守。
FallingStar|2.5|4|.7|basic attack weak vulnerable|初始星费双减益，不是免费挂减益。
ForegoneConclusion|4.5|3.5|1.4|tutor draw|下回合定向取牌，慢速组件配齐。
Furnace|6|3|2|forge sword_engine scaling|每回合铸造，提供独立的剑成长入口。
GammaBlast|4.6|3|1.3|attack weak vulnerable transition|星费双减益与过渡输出。
GatherLight|4.2|2.8|1.2|block star_source|当回合防御兼供星，支持星消费链。
Genesis|6|3.5|2.5|star_source scaling|每回合稳定供星，仍需星消费伤害出口。
Glimmer|5|2.6|1.6|draw topdeck|抽三放一，净抽两；可配合顶抽机制。
Glitterstream|2.8|3.8|1|block transition|两费跨回合防御，已有防守时少抓。
Glow|4.7|2.6|1.7|draw star_source|供星加本回合和下回合各抽牌，资源与运转桥梁。
Guards|4|5|1.3|transform generation cleanup block|整手转仆从捐躯，适合清杂牌，不能当无条件核心保留。
GuidingStar|4.5|3|1.4|attack draw transition|过渡伤害与下回合抽牌，须支付星费。
HeavenlyDrill|6.2|3|1.5|attack x finisher multihit|能量X达到四翻倍；需要储能或供能才能爆发。
Hegemony|3.9|3.2|1.4|attack next_energy transition|两费伤害换下回合供能，有等待成本。
HeirloomHammer|4.8|3.5|1.3|attack generation sword_payoff|复制无色牌，君王之剑也是目标；费用和手牌配齐制约。
HiddenCache|4.2|2.6|1.2|star_source next_star|一星立即三星彩下回合，不能当即有四星。
IAmInvincible|4.3|4|1.3|block topdeck_payoff|顶抽自动格挡依赖顶牌控制，不保证每回合触发。
KinglyKick|3.6|4|1|attack draw_payoff transition|四费每抽到降费，起手慢，长战收益需回转。
KinglyPunch|4.4|3.5|1.8|attack draw_engine scaling|反复抽到永久本战增伤，快回转支持长战。
KnockoutBlow|3.8|3.5|1.3|attack star_source transition|击杀才获得星，Boss无小怪时不能当稳定供星。
KnowThyPlace|3.7|4|1.5|weak vulnerable|零能量减益仍需星费，升级变免费需实际数据。
LunarBlast|4.3|3.5|1.3|attack skill_payoff multihit|先打技能再出伤，单独抽到没有基础命中次数。
MakeItSo|5.2|3.5|1.3|attack skill_engine scaling|三技能回手形成运转伤害，需要低费技能和抽牌。
ManifestAuthority|4.3|3|1.3|block generation|格挡与随机无色生成，随机牌不视作稳定抽牌。
MeteorShower|6.5|3|1.6|attack aoe weak vulnerable ancient|先古群体双减益，资源成本按实际星费。
MonarchsGaze|5.3|4|3|strength_down block multihit_payoff|攻击降低敌方当回合力量，依赖多次命中，升级降费关键。
Monologue|3.9|3.5|1.2|strength play_payoff|本回合力量增长，先多次出牌再攻击，非永久力量。
NeutronAegis|5.3|3.5|1.7|block sustained|覆甲提供持续防御，受伤害和启动资源限制。
Orbit|4.5|4|3|energy engine|花能量返能量；已有供能与过牌才提高循环收益。
PaleBlueDot|4.4|3|2.5|draw play_payoff|每回合五张牌门槛，额外抽牌从下回合兑现。
Parry|5|4|1.7|block sword_payoff|剑同时起盾，需要稳定取剑与出剑。
ParticleWall|4.1|4|1.6|block star_block|回手防御需反复花星，绝不是无限免费盾。
Patter|3.3|3.5|1|block strength transition|基础防御附少量活力，不当作成长核心。
PhotonCut|3.8|3|1.6|attack draw topdeck transition|抽后放顶，基础版净抽零，升级增加净过牌。
PillarOfCreation|4.8|3.5|1.8|generation_payoff block|生成卡牌才起盾，不能把所有抽牌算生成。
Prophesize|5.2|2.8|1.6|draw|两费大抽牌，需费用与手牌容量。
Quasar|4.5|2.8|1.2|generation|发现无色牌，可选择但仍须支付目标费用。
Radiate|4.1|3.5|1.3|attack star_payoff aoe conditional|仅本回合获得的星转群伤，已有存星不触发。
RefineBlade|4.7|2.6|1.7|forge next_energy|铸造兼下回合供能，适合剑主线。
Reflect|4.7|3|1.4|block reflect|格挡反伤需要敌人本回合攻击，不是稳定Boss输出。
Resonance|5.2|3|2.2|strength strength_down|永久双方力量差，攻击主线多段收益更高。
RoyalGamble|4.7|4|1|star_source|一次九星但消耗且支付牌面星费，按净资源计。
Royalties|3.8|5|.8|economy|付费投资未来商店，末段无商店时大幅折扣。
SeekingEdge|6|5|1.5|forge sword_payoff aoe|铸造并让剑群攻；重复群攻属性没有额外价值。
SevenStars|6.4|4|3|attack multihit aoe star_finisher|七段群伤但同时高星费，升级省能量不省星。
ShiningStrike|4.6|3|1.2|attack star_source topdeck|持续顶抽供星也会占用抽牌槽，避免重复。
SolarStrike|4.1|3|1.5|attack star_source transition|过渡攻击兼供星，升级供星翻倍。
SpectrumShift|4.8|4|2.8|generation scaling|每回合生成随机无色，升级降低启动费。
SpoilsOfBattle|5.5|2.5|1.2|forge draw|铸造并抽二，剑体系运转，但仍需出剑费用。
Stardust|5.1|3.4|1.6|attack star_finisher multihit x|星星X随机命中，分散目标，不按能量X计算。
StrikeRegent|.5|4|.5|basic attack|初始打击，优先精简。
SummonForth|5.7|2.8|1.3|forge sword_access|从任意位置取剑并铸造，解决出剑频率。
Supermassive|4.9|3.5|1.4|attack generation_engine scaling|本战生成牌累计增伤，缺少生成时只有五点。
SwordSage|6|5|3|sword_payoff scaling|剑重放能放大铸造，升级降费关键，需有铸造入口。
Terraforming|3.2|4|1.2|strength transition|一次活力，不可当永久力量引擎。
TheSealedThrone|8|5|3|star_source scaling ancient|每打牌供星，先古资源引擎；仍需输出出口。
TheSmith|6|3|1.8|forge sword_engine|单次大量铸造开启剑输出；消耗与重复需求按原生数据。
Tyranny|5.6|4|1.5|draw cleanup scaling|每回合抽一并消耗手牌，精简与循环，升级固有。
Venerate|2|4|1|basic star_source|初始供星；仅在已有替代供星时删除。
VoidForm|5.1|5|2|cost_cheat engine|结束当前回合换每回合前两张免费；不能无视敌方攻击。
WroughtInWar|4.3|3|1.3|attack forge transition|早期伤害兼铸造，剑入口可用而非无限抓过渡。
''',
'necrobinder':r'''
Afterlife|3.6|3.5|1.4|summon|一次性召唤，增加奥斯提生命不是玩家回血。
BansheesCry|5.4|4|2|attack aoe ethereal_payoff|九费每打虚无牌降二费，需要虚无频率才能启动。
BlightStrike|4.4|3|1.3|attack doom_source transition|实际造成伤害才附灾厄，敌方格挡影响产量。
Bodyguard|2.3|4|1|basic summon|初始召唤，保护奥斯提主线时不能当普通防御删。
BoneShards|4.1|3.8|1.4|osty_attack aoe block sacrifice|需要活着的奥斯提并献祭，不能与常驻大奥斯提无代价叠加。
BorrowedTime|3.7|4|1.8|energy surcharge|获得能量同时所有牌涨费，零费灵魂链明显受损。
Bury|4.1|3.5|1.2|attack expensive transition|高费大伤，需供能或虚无降费配套。
Calcify|5.7|3.2|2|osty_engine scaling|奥斯提专属每次攻击加伤，需要奥斯提攻击牌。
CallOfTheVoid|4.5|4|1.5|ethereal_source generation|每回合随机虚无牌，为虚无体系提供频率，随机性仍在。
CaptureSpirit|4.6|2.6|1.4|soul_source|三张灵魂入抽牌堆，未来过牌与灵魂触发，不是即时抽三。
Cleanse|5.1|2.8|1|summon cleanup|从抽牌堆定向消耗，处理状态、诅咒和基础牌。
Countdown|5.2|3.5|2|doom_source doom_engine scaling|每回合六灾厄，慢速长战成长；伤害不是即时六点。
DanseMacabre|4.5|4|1.7|block expensive_payoff|两费以上卡触发防守，零费灵魂不触发。
DeathMarch|4.8|3.4|2|attack draw_engine|按本回合额外抽牌增长，灵魂支持伤害。
Deathbringer|5.3|3|1.5|doom_source aoe weak|群体灾厄兼虚弱，不是即时群伤。
DeathsDoor|4.5|2.8|1.3|block doom_block|同回合施灾厄后十八盾，需实际施加频率。
Debilitate|4.6|3.5|1.5|attack vulnerable weak payoff_debuff|强化已有虚弱易伤，自己不施加二者。
DefendNecrobinder|.5|4|.5|basic block|初始防御，低升级优先级。
Defile|3.7|3.5|1.2|attack ethereal transition|一费十三虚无伤害，合格过渡但不持续成长。
Defy|4.1|3|1.3|block weak ethereal|防御加虚弱，兼容虚无体系。
Delay|3.8|3.5|1.8|block next_energy|先两费防御，下回合供能，不能算即时能量。
Demesne|6|4|3|draw energy ethereal scaling|持续供能过牌，三费虚无有启动窗口，升级降费关键。
DevourLife|5|4|2|summon soul_payoff|打灵魂召唤，实际灵魂来源决定生效频率。
Dirge|5.1|3|1.5|summon soul_source x|能量X转召唤与灵魂，兼容召唤和灵魂体系。
DrainPower|3|4|1|attack transition|随机升级弃牌不保证升级核心，不替代营火。
Dredge|5.6|2.8|1.3|recall draw|弃牌回收三张，消耗；升级保留帮助配齐。
Eidolon|5.6|4|3|ethereal_payoff finisher|重放消耗区虚无牌，需足量已消耗虚无且考虑二费启动。
EndOfDays|5.5|4|1.6|doom_source doom_payoff aoe|即时灾厄斩杀省等待回合，需结合敌人生命与现有灾厄。
EnfeeblingTouch|5|3|1.5|strength_down ethereal block|大幅当回合降力量，多段敌人防守价值高。
Eradicate|5.7|3|1.7|attack x finisher multihit|能量X多段伤害可保留，独立爆发出口。
Fear|3.5|3.5|1.4|attack vulnerable ethereal transition|虚无过渡与易伤，辅助实际攻击主线。
Fetch|4.7|2.5|1.1|osty_attack draw|每回合首次才抽牌，不得按反复回手无限抽。
Flatten|4.3|3|1.4|osty_attack transition|奥斯提前置攻击后免费，需要同回合顺序。
ForbiddenGrimoire|6.5|7|2|ancient economy cleanup|战后永久删牌，前期精简价值高，结束前必须实际打出。
Friendship|5.6|3.5|1.1|energy scaling strength_loss|玩家掉力量换每回合能量，不等于扣奥斯提自身力量。
GraveWarden|4.5|2.8|1.3|block soul_source|格挡附一灵魂，稳健兼容但不能替代输出核心。
Graveblast|4.9|2.6|1.4|attack recall|低伤定向回收，价值来自回收关键牌。
Hang|6.6|2.4|1.4|attack autonomous scaling|对同一敌人反复翻倍，独立Boss输出，需回收过牌。
Haunt|5.9|3|1.8|soul_payoff soul_engine scaling|打灵魂触发生命流失，灵魂生成决定持续输出。
HighFive|4.8|3.2|1.5|osty_attack aoe vulnerable|奥斯提群攻易伤，支持召唤攻击但非玩家力量收益。
Invoke|4.1|2.8|1.5|summon next_energy|下回合召唤供能，有等待成本。
Lethality|4.6|3.8|1.8|attack_payoff ethereal|每回合首张攻击加伤，应有高伤首击，非所有攻击增伤。
Melancholy|3.5|4|1.3|block death_payoff|生物死亡才降费，单Boss缺少小怪时不保证便宜。
Misery|4.8|4|1.1|attack aoe doom_payoff|传播现有减益，单目标Boss时价值明显折扣。
NecroMastery|6|4|1.6|summon osty_engine scaling|奥斯提实际损血反伤，牺牲或死亡不无条件按最大血触发。
NegativePulse|4.2|3|1.6|block doom_source aoe|防守兼群体灾厄，低伤害立即性。
Neurosurge|6.1|4.5|1.2|energy draw self_doom|强供能抽牌但自身持续灾厄，低血长战有致死时限。
NoEscape|5.8|3|1.7|doom_source doom_payoff doom_engine|灾厄按已有层数增加，提供真正长战放大。
Oblivion|5.8|3.5|1.7|doom_source doom_engine play_payoff|本回合后续每次出牌施灾厄，灵魂链支持高频。
Pagestorm|5.2|4|3|draw ethereal_payoff|抽虚无才追加抽牌，需虚无密度，升级降费重要。
Parse|5|2.6|1.5|draw ethereal|抽三虚无，净过牌与虚无联动，不是烧其他牌。
Poke|3.9|3|1.2|osty_attack transition|零费奥斯提攻击，需其存活，支持多次攻击。
Protector|6.5|3|3|osty_attack summon_payoff ancient|按奥斯提最大生命成长，非玩家最大生命。
PullAggro|3.6|3.5|1.1|summon block transition|两费召唤与格挡，防守收益不能双算同一伤害。
PullFromBelow|5.1|3.4|1.8|attack ethereal_payoff scaling multihit|累计打出虚无的伤害终端，需虚无来源。
Putrefy|4.7|3.3|1.3|weak vulnerable|一次双减益，多体系兼容，不是成长输出。
Rattle|5.3|3|1.5|osty_attack osty_engine multihit|同回合奥斯提攻击次数终端，需要低费前置攻击。
Reanimate|4.7|4|1.4|summon|三费大量召唤，适合大奥斯提但不凭空产生输出。
Reap|3|4|1|attack expensive transition|三费二十七过渡，只有高费联动或缺伤时考虑。
ReaperForm|5.8|4|1.5|doom_payoff attack_payoff scaling|攻击实际伤害追加灾厄，需攻击和三费启动能力。
Reave|4.2|3|1.4|attack soul_source transition|过渡攻击兼灵魂，灵魂升级有运转收益。
RightHandHand|4.7|3|1.2|osty_attack expensive_payoff scaling|高费牌回收零费奥斯提攻击，需高费打出频率。
Sacrifice|4.4|4|3|block sacrifice summon_payoff|牺牲奥斯提换盾，失去已养大奥斯提有实际代价。
Scourge|4.8|2.8|1.8|doom_source draw|灾厄加过牌；升级净抽增加，非即时伤害。
SculptingStrike|3.8|3.2|1.3|attack ethereal_source cleanup transition|手牌加虚无可清污染，须留到回合结束才消耗。
Seance|4.8|4|3|soul_source cleanup ethereal|将抽牌堆杂牌变灵魂，保护输出核心与供能。
SentryMode|5.6|4|3|generation autonomous scaling|每回合生成扫荡凝视，持续输出但仍需打出费用。
Severance|4.7|2.8|1.3|attack soul_source|三处各加灵魂，手牌的一张即时可用。
SharedFate|4.7|4|1.5|strength_down strength_loss|永久敌方降力量，玩家攻击主线承担自己的力量损失。
Shroud|5|3.5|1.7|block doom_block|每次施灾厄格挡，不按灾厄层数逐点起盾。
SicEm|5.1|3|1.4|osty_attack summon osty_engine|后续奥斯提攻击该目标才召唤，需要攻击频率。
SleightOfFlesh|5.3|3.5|1.8|doom_payoff scaling debuff_engine|每次施加负面触发伤害，需施加频率而非层数。
Snap|3.6|3.5|1.1|osty_attack retain transition|攻击加单牌保留，组件桥梁而非输出核心。
SoulStorm|6|3|1.8|attack soul_payoff soul_engine scaling|消耗区灵魂累计伤害，需灵魂来源，非抽牌数量。
Sow|3.7|3.5|1.2|attack aoe transition|一费群伤可保留，早期过渡不应后期堆积。
SpiritOfAsh|5.2|3.5|1.5|block ethereal_payoff|打出虚无起盾，回合结束虚无消耗不算打出。
Spur|4.4|3|1.6|summon heal_osty|召唤并治疗奥斯提，不能当玩家营火回血。
Squeeze|5.1|3.5|1.4|osty_attack osty_engine expensive|奥斯提攻击数量放大三费终端，不能按玩家力量成长。
StrikeNecrobinder|.5|4|.5|basic attack|初始打击优先精简。
TheScythe|6|4|1.6|attack autonomous scaling|每局永久成长，越早获得越有积累机会，每战消耗。
TimesUp|5.4|3.5|1|attack doom_payoff|灾厄转即时攻击，缺乏灾厄时没有基础伤害。
Transfigure|5.2|4|1.5|repeat_skill surcharge|单牌重放加费，需具体目标和支付能力。
Undeath|4.2|5|1.2|block generation|零费盾自复制会膨胀牌堆，重复抓取惩罚高。
Unleash|2.8|4|1|basic osty_attack summon_payoff|按奥斯提现有生命加伤，不是玩家生命。
Veilpiercer|4.4|3|1.1|attack ethereal_payoff transition|下一张虚无免费，可帮助高费虚无启动。
Wisp|4.3|3|1|energy|一次供能消耗，升级保留有配齐价值。
'''}

def main():
 # Rebuild from checked-in localized native exports and normalized evidence.
 # Raw locally downloaded observations are optional, never a build dependency.
 english=dict(line.split('|',1) for line in (pathlib.Path(__file__).parent/'english-notes.txt').read_text('utf-8').splitlines() if line.strip())
 catalogpath=ROOT/'localization/en.json';catalog=json.loads(catalogpath.read_text('utf-8-sig'))
 catalog.update(dict(line.split('|',1) for line in (pathlib.Path(__file__).parent/'english-ui.txt').read_text('utf-8').splitlines() if line.strip()))
 for zh,en in list(catalog.items()):
  if zh.startswith('机器人') and any(word in zh for word in ['删牌：保护输出','牌组边际评分','升级机会成本','恢复收益及休息','后续路线风险']):
   for zhhero,enhero in [('储君','Regent'),('亡灵契约师','Necrobinder')]:catalog[zh.replace('机器人',zhhero)]=en.replace('Defect',enhero)
 catalog['原生营火效果及储君牌组估值']='Native camp effects and Regent deck valuation'
 catalog['原生营火效果及亡灵契约师牌组估值']='Native camp effects and Necrobinder deck valuation'
 for hero,rows in ROWS.items():
  nativepath=ROOT/f'data/{hero}-cards-v0.111.0.json';native=json.loads(nativepath.read_text('utf-8-sig'))
  saved=json.loads((ROOT/f'data/{hero}-community-2026-11-01.json').read_text('utf-8-sig'))
  assert saved['bracket']=='solo:standard:a10:v0.111.0'
  out={};evidence=saved['cards']
  for row in rows.strip().splitlines():
   name,base,duplicate,upgrade,tags,note=row.split('|');tags=tags.split();phase=[.6,-.6,-1.2] if 'transition'in tags else [0,.2,.3]
   if 'economy'in tags:phase=[1,0,-4]
   q=next(x for x in evidence if x['className']==name);adjust=0
   peers=[x for x in evidence if x['rarity']==q['rarity'] and x['sample']>=200 and x['lift'] is not None and x['pickRate'] is not None]
   if q['sample']>=200 and q['offered']>=500 and peers and q['lift'] is not None and q['pickRate'] is not None:
    adjust=max(-.8,min(.8,(q['lift']-statistics.median(x['lift'] for x in peers))*.06+(q['pickRate']-statistics.median(x['pickRate'] for x in peers))*.008))*q['sample']/(q['sample']+1000)
   out[name]=dict(base=float(base),duplicate=float(duplicate),upgradeDelta=float(upgrade),phase=phase,tags=tags,note=note,evidenceAdjustment=round(adjust,3))
   catalog[note]=english[name]
  excluded=[r['basic']['className'] for r in native if r['multiplayerConstraint']=='MultiplayerOnly'];expected={r['basic']['className'] for r in native}-set(excluded)
  assert set(out)==expected,(hero,expected-set(out),set(out)-expected)
  nativepath.write_text(json.dumps(native,ensure_ascii=False,indent=2)+'\n','utf-8')
  (ROOT/f'data/{hero}-draft-v0.1.json').write_text(json.dumps(dict(version=hero+'-draft-0.1',gameVersion='0.111.0',excludedMultiplayer=excluded,cards=out),ensure_ascii=False,indent=2)+'\n','utf-8')
  reportdir=ROOT/'资料/储君与骨妹初版';reportdir.mkdir(parents=True,exist_ok=True)
  names={r['basic']['className']:r['basic']['name'] for r in native}
  report=['0.6.53 首版机制参数；不是抓取概率，也不是胜率。','最终分数还包括资源、牌组联动、缺口、重复、阶段、升级及辉眼等；不按本表简单排序。','基础先验 / 同稀有度观察修正 / 第二张起重复系数 / 升级收益 / 第一、二、三幕修正 / 功能 / 机制说明','']
  for name,p in out.items():report.append(f'{names[name]} ({name})：{p["base"]} / {p["evidenceAdjustment"]:+.3f} / {p["duplicate"]} / {p["upgradeDelta"]} / {p["phase"]} / {", ".join(p["tags"])}\n  {p["note"]}')
  (reportdir/f'{hero}-逐卡参数.txt').write_text('\n'.join(report)+'\n','utf-8')
  print(hero,len(out),'modeled;',len(excluded),'multiplayer ignored; community runs',saved['runs'])
 catalogpath.write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n','utf-8')
if __name__=='__main__':main()
