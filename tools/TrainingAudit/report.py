"""Readable local batch report from audit.py output; no model inference or uploads."""
import argparse
import json
from pathlib import Path


def render(audit):
    lines = ['机器人原生自动测试：逐场记录',
             '仅统计自然对局中 fullRun=true 且 outcome=win/loss 的结果。',
             f"当前已结算完整样本：{audit['wins']} 胜 / {audit['losses']} 负。",
             '小样本、不同构建和不同求解器预算不能合并解释为稳定胜率。',
             '流程中断及合成回归不计策略胜负；相同种子重跑也不是独立随机样本。', '']
    for case in audit['cases']:
        meta = case['metadata']; state = case['lastState']
        lines += [f"目录：{case['case']}", f"DLL SHA256：{meta.get('dllSha256')}",
                  '样本类型：'+{'natural':'自然新局','continuation':'隔离存档续测，不计完整自动胜率','fixture':'构造回归，不计胜率','excluded':'实验环境无效，排除'}.get(case.get('sampleKind'),'未分类'),
                  f"额外层数配置：{meta.get('longMapLayers')}；-1 表示沿用实验环境默认。",
                  f"最后快照：第 {state.get('act')} 幕 / {state.get('floor')} 层，生命 {state.get('hp')}/{state.get('maxHp')}，金币 {state.get('gold')}"]
        for env in case.get('environments', []):
            solver = env['solver']
            lines += [f"开局：{env['seed']}；求解器搜索上限 {solver.get('searchTimeLimitSeconds')} 秒，线程设置 {solver.get('searchMaxDegreeOfParallelism')}（空值为求解器默认）。",
                      f"开局日志策略标签：{env.get('recordedPolicy')}"]
        lines.append('实际决策策略：'+', '.join(sorted({d.get('policy') or '未记录' for d in case['decisions']})))
        if not case['ledger']:
            lines.append('本场尚未结算；不能计胜或负。')
        for result in case['ledger']:
            outcome = {'win':'胜利','loss':'失败','interrupted':'流程中断','running':'运行中'}.get(result['outcome'],result['outcome'])
            lines.append(f"结果：{result['seed']} / {outcome} / 完整自动资格={result['fullRun']} / 原因={result.get('reason','')}")
        for result in case.get('gameResults', []):
            end = result['state']
            lines.append(f"原生结算快照：{result['seed']} / 胜利={result['victory']} / {end.get('floor')} 层 / {end.get('hp')}/{end.get('maxHp')} HP / {len(result['deck'])} 张牌。结算后的销毁快照可能显示 0 HP，以此结算记录为准。")
        for fallback in case.get('fallbacks', []):
            lines.append(f"后备行为：{fallback['type']}；{fallback.get('reason')}")
        lines.append('实际免费选牌与营火：')
        previous_run = None
        for decision in case['decisions']:
            if decision.get('runId') != previous_run:
                previous_run = decision.get('runId')
                lines.append(f"  对局：{decision.get('seed') or previous_run}")
            ranked = sorted((o for o in decision['options'] if o['score'] is not None),key=lambda o:o['score'],reverse=True)
            scores = '；'.join(f"{o['name']} {o['score']:.2f}" for o in ranked[:3])
            lines.append(f"  {decision['floor']} 层 [{decision['kind']}] {decision['hp']}/{decision['maxHp']} HP，建议={decision['recommendation'] or '比较不完整'}，实际={decision['actualChoice'] or '未捕获/未完成'}；{scores}")
        lines += ['完整评分分解、候选与结算牌组见同名审计 JSON；原始对局日志保留在上述目录。', '']
    return '\n'.join(lines)+'\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('audit',type=Path)
    parser.add_argument('--output',type=Path,required=True)
    args = parser.parse_args()
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(render(json.loads(args.audit.read_text('utf-8-sig'))),encoding='utf-8')
    print(args.output)
