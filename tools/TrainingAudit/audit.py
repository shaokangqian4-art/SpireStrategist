"""Read isolated native training cases; never count fixtures or unfinished runs as losses.

Usage: python tools/TrainingAudit/audit.py CASE [CASE ...] --output artifacts/audit.json
Copies no raw saves, account identifiers, or private progress files into the report.
"""
import argparse
import collections
import json
from pathlib import Path


def read(path, default=None):
    try:
        return json.loads(path.read_text('utf-8-sig'))
    except (FileNotFoundError, json.JSONDecodeError):
        return default


def inspect(case):
    meta = read(case / 'metadata.json', {})
    root = case / 'roaming/SlayTheSpire2'
    advisor = root / 'SpireStrategist'
    excluded = (case / 'EXCLUDED.txt').exists() or meta.get('eventLab') != 'defect-training'
    sample_kind = 'excluded' if (case / 'EXCLUDED.txt').exists() else 'continuation' if meta.get('eventLab')=='defect-resume' else 'natural' if meta.get('eventLab')=='defect-training' else 'fixture'
    outcome = read(case / 'result.json') or read(root / 'strategist-autopilot-result.json')
    ledger = []
    for path in sorted((advisor / 'climb-batches').glob('*/summary.json')):
        ledger.extend(read(path, {}).get('results', []))
    ledger = list({r['id']: r for r in ledger}.values())
    events = collections.Counter()
    decisions = {}
    choices = {}
    results = []
    fallbacks = []
    environments = []
    last = {}
    for path in sorted((advisor / 'climb-batches').glob('*/*.jsonl')):
        for line in path.open(encoding='utf-8-sig'):
            try:
                row = json.loads(line)
            except json.JSONDecodeError:
                continue
            if row.get('type') == 'run_start':
                payload = row['payload']; settings = payload.get('solverSettings') or {}
                environments.append(dict(seed=(payload.get('run') or {}).get('seed'),
                    recordedPolicy=payload.get('policy'), gameVersion=payload.get('gameVersion'),
                    solver={k:settings.get(k) for k in ('performancePreset','searchTimeLimitSeconds','searchMaxDegreeOfParallelism')}))
    for path in sorted(advisor.glob('session-*.jsonl')):
        with path.open(encoding='utf-8-sig') as source:
            for line in source:
                try:
                    row = json.loads(line)
                except json.JSONDecodeError:  # allow the live process's unfinished final line
                    continue
                events[row['type']] += 1
                payload = row.get('payload', {})
                if row['type'] == 'state':
                    last = payload.get('state', {})
                if row['type'] == 'choice':
                    choices[payload['decisionId']] = payload.get('key')
                if row['type'] == 'run_result':
                    state = payload.get('state', {})
                    results.append(dict(runId=payload.get('runId'), seed=(state.get('hidden') or {}).get('seed'), victory=payload.get('victory'),
                        state={k:state.get(k) for k in ('act','floor','hp','maxHp','gold')},
                        deck=[dict(name=c['className'],upgrade=c.get('upgradeLevel',0),enchantment=c.get('enchantment')) for c in state.get('deck',[])]))
                if row['type'] in ('auto_partial_reward_choice','auto_unmodelled_reward_skip','auto_error'):
                    detail = payload.get('payload',payload)
                    if row['type']=='auto_partial_reward_choice' and isinstance(detail.get('selected'),str):
                        choices[detail['decisionId']] = detail['selected']
                    fallbacks.append(dict(type=row['type'], decisionId=detail.get('decisionId'),
                        selected=detail.get('selected'), reason=detail.get('reason')))
                if row['type'] == 'decision':
                    names = {o['key']: o.get('name', o['kind']) for o in payload['options']}
                    evaluation = payload['evaluation']
                    decisions[payload['id']] = dict(
                        decisionId=payload['id'], runId=payload.get('runId'), seed=(payload['state'].get('hidden') or {}).get('seed'), policy=evaluation.get('policyVersion'), floor=payload['state']['floor'], kind=payload['kind'],
                        hp=payload['state']['hp'], maxHp=payload['state']['maxHp'],
                        recommendation=names.get(evaluation['recommendedKey']),
                        options=[dict(key=o['key'],name=o['name'], score=o['score'],supported=o.get('supported'),parts=o.get('parts',[])) for o in evaluation['options']])
    for key, decision in decisions.items():
        chosen = choices.get(key)
        decision['actualChoice'] = next((o['name'] for o in decision['options'] if o['key']==chosen),None)
    eligible = [r for r in ledger if not excluded and r['fullRun'] and r['outcome'] in ('win', 'loss')]
    return dict(case=case.name, metadata=meta, excluded=excluded, sampleKind=sample_kind,
                harness=outcome, ledger=ledger, eligibleResults=eligible,
                events=dict(events), environments=environments, gameResults=results, fallbacks=fallbacks,
                lastState={k:last.get(k) for k in ('character','act','floor','hp','maxHp','gold')},
                lastDeck=[dict(name=c['className'], upgrade=c.get('upgradeLevel', 0), enchantment=c.get('enchantment'))
                          for c in last.get('deck', [])], decisions=list(decisions.values()))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('cases', type=Path, nargs='+')
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    cases = [inspect(case) for case in args.cases]
    outcomes = [r for case in cases for r in case['eligibleResults']]
    report = dict(cases=cases, wins=sum(r['outcome']=='win' for r in outcomes),
                  losses=sum(r['outcome']=='loss' for r in outcomes),
                  note='Separate policies, map lengths and solver budgets before comparing. Small samples are not stable win-rate estimates; timeouts and synthetic fixtures are excluded.')
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    for case in cases:
        state = case['lastState']
        print(case['case'], case['sampleKind'], state,
              [(r['seed'], r['outcome'], r['fullRun']) for r in case['ledger']])
    print('Natural completed outcomes:', report['wins'], 'wins,', report['losses'], 'losses')


if __name__ == '__main__':
    main()
