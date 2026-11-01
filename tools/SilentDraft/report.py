"""Readable review from the exact native data, pinned statistics and C# scores."""
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'资料/猎人初版'

def main():
    OUT.mkdir(parents=True,exist_ok=True)
    prior=json.loads((ROOT/'data/silent-draft-v0.2.json').read_text('utf-8'))
    stats=json.loads((ROOT/'data/silent-community-2026-11-01.json').read_text('utf-8'))
    native=json.loads((ROOT/'data/silent-cards-v0.111.0.json').read_text('utf-8'))
    examples=json.loads((OUT/'六类局面评分明细.json').read_text('utf-8'))
    initial={x['className']:x for x in examples[0]['scores'] if not x['upgraded']}
    smap={x['className']:x for x in stats['cards']}
    lines=['全局战略助手 · 猎人首版抓牌模型',
        '日期：2026-11-01；游戏：0.111.0；模型：silent-draft-0.2。',
        '状态：本地 0.6.51 已接入建议与全局接管；捏奥共用用户排序；原生接管验证另见全局接管验证.txt（0.6.50历史记录）；Steam 未更新。',
        '本稿供首轮人工审阅，不宣称已训练、实战胜率提升或战损预测准确。', '',
        '1. 资料与统计口径',
        '原生池91条 = 80张单人常规奖励候选 + 4基础 + 2先古 + 5多人。常规候选范围忽略账号解锁，不包括另池生成的小刀。',
        '未查到公开的官方逐卡抓取率/胜率完整表。官方公告给出总体统计，并推荐收集自身数据的社区项目：',
        'https://www.megacrit.com/news/2026-5-22-neowsletter-issue-22/',
        '本稿来源：Spire Codex 社区上传存档，不是 Mega Crit 遥测。固定查询：'+stats['url'],
        '样本截止：'+stats['dataThrough']+'；抓取日期：'+stats['retrievedDate'],
        f"过滤：{stats['bracket']}，SILENT；猎人 {stats['characterRuns']} 局、{stats['characterWins']} 胜，80张常规牌有逐卡统计。",
        '已限制单人、标准、A10、0.111.0；不能排除模组/求解器、自选上传与玩家水平偏差。',
        '抓取率是奖励选取/展示，不是出现概率；持有胜率不代表拿这张牌造成的胜率增益。sample=picks为站点持有/获取观测，奖励选取另存picked，不能混算分母。',
        'lift为站点按获取楼层及玩家历史表现修正的观察差值，仍非因果；升级状态分行，本稿参考基础行。同稀有度比较有价值，但不能排除所有偏差。',
        '另外查过OP.GG和STS Tracker作口径对照；最终数值只采用固定切片，不拼接各站不同版本。', '',
        '2. 本机官方生成机制（不等于强度）',
        'A7稀缺及以上零偏移基础稀有概率：普通战1.49%、精英5%、商店4.5%；首领卡牌奖励100%稀有。低进阶对应3%、10%、9%。',
        '稀有偏移初始-5个百分点，非稀有结果后增加（A7+每次0.5个百分点，低进阶1个百分点），稀有后重置，上限+40个百分点。',
        '实际以随机数与基础稀有+偏移比较；负阈值时稀有不可能出现，也影响后续非普通阈值。零偏移概率不是每次的实际概率。',
        '商店读取偏移但不推进赔率；首领奖励使用零偏移；特殊事件/遗物另有规则。某张牌还受池子、解锁、重复排除影响。',
        '根据本机CardRarityOdds与AscensionLevel核对。v0.2 接入条件前瞻，但不把掉率作为强度分。', '',
        '3. 体系：入口、运转、输出、防守分别判断',
        '小刀：墨之刃/先制打击/斗篷与匕首等产刀 → 精准/幻影之刃放大 → 刀刃陷阱回放。刀扇自带四刀，可以自启动。',
        '毒：毒雾/弹跳药瓶等施毒 → 触媒/毒性爆发；咕嘟冒泡不能独立起毒，蜃景依赖已经叠起的毒。',
        '弃牌奇巧：杂技/隐秘匕首/早有准备/必备工具等主动弃牌 → 本能反应/战术大师/磨蚀等兑现。只有生存者不算可靠启动。',
        '运转输出：抽牌/弃牌/小刀配群蛇形态、谋杀或速行者；只有供能过牌和防御不能记为已经有输出。',
        '虚弱攻击：可靠虚弱配跟踪；多段/小刀可用力量。毒伤、群蛇固定伤害不吃攻击易伤/跟踪。',
        '特殊：华丽收场需要抽空牌堆，少量过牌不等于无限；夜魇需要强目标、费用和防守窗口。',
        '小刀和毒是主要输出方向，成型后通常不混抓另一整套。弃牌、过牌、供能和防御是辅助层，按主线需求配置。',
        '涂毒可对现有多次命中产生收益，但不自动获得跨抓触媒/施毒组件的许可；单张过渡牌也不会把方向永久锁死。',
        '路线使用猎人当前输出、成长、防御及共用战损规划；未来牌不算已有战力。', '',
        '4. 已核对的版本差异',
        '计算下注升级增加保留、仍然消耗；刀刃之舞也消耗。不能套旧版无限循环。',
        '本能反应/战术大师是三费奇巧，主要靠主动弃牌免费打出；不是无条件抽牌供能。',
        '计划妥当是两费金卡保留全手，升级降费；独门技术抽二并给这些牌保留；墨影小刀施加一虚弱。',
        '谋划专家需先打技能才得到奇巧；不立即全手零费。触媒每次额外触毒仍消耗一毒层；速行者不算起手自然抽牌。', '',
        '5. 权重解释',
        '总分=机制先验+有限统计修正+阶段需求+缺口+联动+遗物联动+条件前瞻+已有升级+补成输出链-抽牌占位-重复/饱和-启动负担。跳过=0。',
        '条件前瞻最多比较8条候选路径、每条3次供牌；奖励一次选一张，商店受价格和预算限制，增分上限9。未来牌不算现有战力。',
        '抽牌占位2.5；普通正联动合计上限6；补成已有支撑的长战输出链另加首幕2/后幕3。均为初版假设，不是拟合参数。',
        '统计修正：同稀有度中位数作比较，raw=0.06×lift偏差+0.008×抓取率偏差，截断±0.8，再乘sample/(sample+1000)。样本<200或展示<500不修正。',
        '重复惩罚=已有同名数×该牌系数+0.45×已有同名数平方；同功能饱和和条件配套另计。',
        '表中升级差值用于已升级奖励；营火另由 SilentStrategy.Upgrade 计算。初始牌负分不等于可立刻全删。',
        '商店价格、事件代价、变牌旧牌价值走各自策略入口；无色牌使用猎人特征。未知牌/未知附魔不伪造完整排序；多人牌忽略。', '',
        '6. 逐卡参数（不是固定排行榜）',
        '初始示例使用5打5防+中和+生存者，60/70血，首幕，3费；基础先验与最终分不要混淆。', '']
    records=[]
    for d in native:
        b=d['basic'];id=b['className'];p=prior['cards'].get(id);r=smap.get(id)
        lines.append(f"{b['name']} / {id} [{b['rarity']}]")
        if not p:
            lines.append('多人专属：忽略，不评分。\n');continue
        lines.append(f"基础 {p['base']:+.2f}；统计 {p['evidenceAdjustment']:+.3f}；分幕 {p['phase']}；重复 {p['duplicate']}；已敲 +{p['upgradeDelta']}；初始牌组总分 {initial[id]['score']:.3f}。")
        if r:lines.append(f"社区：抓取 {r['pickRate']}%，持有胜率 {r['holderWinRate']}%（95%区间 {r['winRateCi']}），修正差 {r['lift']}pp；样本 {r['sample']}，奖励选取/展示 {r['picked']}/{r['offered']}。")
        else:lines.append('基础/先古牌：不套常规奖励统计。')
        lines.extend(['功能：'+', '.join(p['tags']),p['note'],''])
        records.append(dict(card=b,upgraded=d['upgraded'],profile=p,community=r,initialScore=initial[id]['score']))
    lines+=['7. 人工构造情景（不是实战胜率）']
    for e in examples:
        scores=sorted((x for x in e['scores'] if not x['upgraded'] and x['className'] not in ['Suppress','WraithForm']),key=lambda x:x['score'],reverse=True)
        lines.extend([e['name'],'主输出：'+e['composition']['PrimaryDamage'],'；'.join(e['composition']['Plans']),'分数前六：'+'；'.join(f"{x['name']} {x['score']:.2f}" for x in scores[:6]),''])
    lines+=['下一步：用有版本记录的自然样本复盘；接管流程可用不等于胜率已经校准。']
    (OUT/'猎人抓牌建模_v0.2.txt').write_text('\n'.join(lines)+'\n','utf-8-sig')
    (OUT/'逐卡权重与统计.json').write_text(json.dumps(records,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(f'Report: {len(records)} modeled cards, {len(stats["cards"])} statistical rows')

if __name__=='__main__':main()
