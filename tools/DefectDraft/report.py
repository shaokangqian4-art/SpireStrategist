"""Build the local review artifact from model snapshots + a supplied game text export."""
import argparse
import json
from pathlib import Path
import evaluate as e

def main():
    p=argparse.ArgumentParser();p.add_argument('--localization',required=True);p.add_argument('--output',required=True)
    a=p.parse_args();loc=json.loads(Path(a.localization).read_text('utf-8-sig'))
    out=Path(a.output);out.mkdir(parents=True,exist_ok=True)
    lines=['全局战略助手：故障机器人抓牌权重校准稿 v0.5',
        '日期：2026-10-26；本机游戏数据：0.111.0；策略：defect-draft-0.5',
        '状态：实验先验，已接入 0.6.27 机器人实验接管；参考首批十局人工实战校准，尚未独立验证，完整自动运行仍在验证，不能视作成熟策略。',
        '来源：本机游戏 ModelDb 的 DefectCardPool，以及本机简体中文资源。91 个条目：80 张单人普通奖励候选、4 张基础牌、2 张先古牌、5 张多人专用牌。',
        'AllCards 忽略解锁状态：80 是候选范围，不表示当前账号每张都已经解锁。生成牌如燃料另属其他池，本表不把它们当普通抓牌候选。',
        '权重是作者制定的假设，不是官方强度、不代表胜率；中等置信度也不代表已经用对局验证。', '',
        '一、怎样读分数',
        '最终分 = 基础先验 + 阶段调整 + 当前缺口 + 已兑现的配合 + 已升级收益 - 抽牌占位 - 重复递减 - 启动风险。',
        '跳过为 0 分；已支持选项中比较最高分，持平倾向跳过。遇到未知候选、多人牌或未建模附魔时放弃明确推荐。',
        '“基础先验”不能单独排序；下表初始牌组示例是第一幕、75/75 血、3 费、4 打4防+电击+双重释放、初始核心的免费奖励，不是商店购买分。',
        '阶段采用当前幕及实际地图进度；没有用固定总楼层划分前中后期。遗传算法的培养期会随地图进度折扣。',
        '抽牌占位基准：-2.5。即时伤害缺口和防御缺口各最多按 2.0 系数补偿。所有正联动合计封顶 +5，防止叠标签产生虚高。',
        '重复惩罚 = 同名已有张数 × 该牌重复系数 + 二次系数 × 同名已有张数²。二次系数一般为 0.65；爪击为 0.25，仍需可靠回收才增分。不同卡的同功能饱和还会另行折扣。',
        '稀有度没有统一加分；强金卡依靠自己的机制与局面收益加分，白牌也不能因容易打出而无限多抓。', '',
        '二、先核准的二代机制',
        '偏差认知是本版本先古牌；碎片整理、遗传算法是金卡。',
        '超能光束的集中损失只持续本回合，仍需计算本回合冰霜防御受损。',
        '双重释放、四重释放、多重释放和循环作用于最右侧球；吞噬暗影每回合激发最左侧球。',
        '玻璃球基础被动从 4 起，每次有效被动触发后基础值减 1、最低 0，激发值为当前被动的两倍；集中仍会参与球值计算。',
        '等离子被动 1 能量、激发 2 能量，不受集中加成。',
        '满球位继续生成球会激发最先进入的球；暴涨当场删去球位时，溢出的球直接移除，不触发激发。机器人球位为零时不能继续生成球。',
        '漆黑生成一个黑暗球并触发所有黑暗被动；升级后被动触发两次。打碎对所有球激发两次，其他多次激发牌仍需安排目标球位置。',
        '迭代关注首次抽到状态；烟囱、化废为宝关注生成状态。压缩只变化手牌中的状态，不处理诅咒。',
        'EnergySurge、Hibernate、Ignition、ImitationLearning、OneForAll 在模型中标记为多人专用。', '',
        '三、本次调整与当前限制',
        'v0.5 新增当前牌组的多方向证据：能力、球、黑暗、状态、物理可同时存在，随新增和移除的牌重新计算，不锁定流派。基础打防不作为物理流信号。',
        '前期首张成长组件可以获得有限未来投资分；低血时折扣，按本幕地图相对进度缩短培养期。该分数上限为 4，与当前已兑现联动分别说明。',
        '方向成熟后，过渡攻击／格挡按功能及阶段降分，不按稀有度一刀切。球系缺少物理基础时降低物理组件；成熟主线降低新黑球支线，仍允许真实混合体系。',
        '万物一心只把不消耗的零费攻击／技能作为持续回收来源。消耗牌若尚未打出而被弃掉仍有一次性回收机会，目前保守不计；不是规则上禁止回收。',
        '已有升级全息时降低新增全息需求；成熟牌组对没有方向收益的新卡保留少量估值余量，避免 0.1 分之类的微小优势驱动扩充。',
        '状态清理额外包含敌方塞牌的分幕折扣先验；不是已读取到的确定未来。散射炮作用于所有未消耗牌堆，压缩只处理手牌。',
        '能力触发包含已有雷暴、子程序等引擎；雷暴产电支持雷霆和电流相生。已兑现球输出也参与伤害缺口估计。',
        '刮削看零费密度，万物一心不把零费能力当回收目标。物理循环提高暴涨价值，保留损失球位代价。',
        'v0.5 不再按产球总量统一惩罚暴涨：持续补球降低被动损失估计，并提供有限的提前顶球收益；蓄黑球则重点扣除成长空间。力量只按直接攻击兑现，敏捷只按直接格挡技能兑现。',
        '漆黑、扩容及双重／四重／多重释放、打碎加入蓄黑球联动；保留重复递减和正联动总封顶。多重释放仍需支付费用并留防守余量。',
        '球位风险是牌组层面的先验，没有读取战斗中当前球位、黑球数值及排列；多张暴涨的零球位风险提示不等于已确定会打到零球位。',
        '未升级核心积压会产生有限惩罚；不能假定每张卡都能敲。该惩罚不代表已适配火堆选择。',
        '本版只覆盖机器人卡池的免费选牌。无色牌、遗物组合、具体敌人、商店、删牌、火堆、路线、先古选择没有完整机器人模型。',
        '牌组含未知卡时显示警告；候选含未知卡则不推荐。球种／产球支持度是折扣估算，不是实战中已在场的球。',
        '没有把“已安装求解器”作为所有复杂牌的加分。求解器能否驾驭新角色，需要实测；先保持战斗执行方式固定。',
        '升级增分指拿到已升级卡的边际价值，不是火堆升级排序。随机牌不按最佳随机结果计分。', '',
        '四、逐卡抓牌表（名称后为稳定代码 ID）']
    names={n:loc.get(r['basic']['id'].split('.')[-1]+'.title',n) for n,r in e.DEFINITIONS.items()}
    rows=[]
    for i,(name,r) in enumerate(e.DEFINITIONS.items(),1):
        profile=e.PROFILES[name]; b=r['basic']; u=r['upgraded']; v=e.score(e.starter(),b)
        score='不参与单人比较' if v['score'] is None else str(v['score'])
        phase='/'.join(f'{x:+g}' for x in profile['phase'])
        status='多人专用' if r['multiplayerConstraint']=='MultiplayerOnly' else '初始牌' if b['rarity']=='Basic' else '先古牌' if b['rarity']=='Ancient' else '普通奖励候选（受解锁约束）'
        lines += ['',f'{i:02d}. {names[name]} [{name}]',f'范围：{status}；{b["rarity"]}／{b["type"]}',
            f'基础先验 {profile["base"]:+g}；第一/二/三幕调整 {phase}；重复系数 {profile["duplicate"]:g}；已升级增益 {profile["upgradeDelta"]:+g}',
            f'初始牌组示例净分：{score}；先验置信度：{profile["confidence"]}（非统计置信度，尚待新种子验证）。',
            '判断理由：'+profile['note'],
            '基础：费用 '+('X' if b['costsX'] else str(b['cost']))+'；关键词 '+(', '.join(b['keywords']) or '无')+'；变量 '+json.dumps(b['variables'],ensure_ascii=False),
            '升级：费用 '+('X' if u['costsX'] else str(u['cost']))+'；关键词 '+(', '.join(u['keywords']) or '无')+'；变量 '+json.dumps(u['variables'],ensure_ascii=False)]
        rows.append(dict(name=names[name],className=name,availability=status,base=profile['base'],phase=profile['phase'],
            duplicate=profile['duplicate'],upgradeDelta=profile['upgradeDelta'],starterScore=v['score'],note=profile['note']))
    lines += ['', '五、复算与数据留存',
        '可使用 tools/DefectDraft/evaluate.py 读取现有 session JSONL，输出当时全部候选的实验分数，并按 decisionId 对齐玩家实际选择。',
        '命令：python tools/DefectDraft/evaluate.py "某局session.jsonl" --output "该局机器人复算.json"',
        '报告生成：python tools/DefectDraft/report.py --localization "本机导出的cards.json" --output "报告目录"',
        '测试：python tools/DefectDraft/test_draft.py',
        '配置源：tools/DefectDraft/build_profiles.py；生成配置：data/defect-draft-v0.5.json；模型快照：data/defect-cards-v0.111.0.json。',
        '修改逐卡先验请改 build_profiles.py 后重新生成，避免生成文件与源码分歧。',
        '本稿不会上传日志、改动存档、触发游戏操作，也没有发布至 GitHub 或 Steam。']
    (out/'鸡煲抓牌权重_v0.5.txt').write_text('\n'.join(lines)+'\n','utf-8-sig')
    (out/'鸡煲逐卡权重_便于复盘.json').write_text(json.dumps(rows,ensure_ascii=False,indent=2)+'\n','utf-8')
    cases=[]
    for label,extra,act in [('初始牌组',[],1),('冰霜集中',['Glacier','Coolheaded','Defragment','ColdSnap'],2),
        ('状态引擎',['FightThrough','GunkUp','Overclock','TrashToTreasure'],2),
        ('零费回收',['Ftl','GoForTheEyes','BeamCell','Hologram','AllForOne'],2)]:
        state=e.starter();state['deck'] += [e.snapshot(n) for n in extra];state['act']=act
        scores=[dict(className=n,name=names[n],**e.score(state,e.snapshot(n))) for n,r in e.DEFINITIONS.items() if r['multiplayerConstraint']!='MultiplayerOnly' and r['basic']['rarity'] in {'Common','Uncommon','Rare'}]
        cases.append(dict(scenario=label,state=state,scores=sorted(scores,key=lambda r:-r['score'])))
    (out/'四类局面评分拆分.json').write_text(json.dumps(cases,ensure_ascii=False,indent=2)+'\n','utf-8')
    print(f'Wrote {len(rows)} profiles and {len(cases)} example contexts to {out}')

if __name__=='__main__':main()
