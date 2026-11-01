"""Compact native run evidence for drafting review; does not grade choices as correct."""
import argparse
import collections
import json
from pathlib import Path


def review(case):
    root = case / 'roaming/SlayTheSpire2/SpireStrategist'
    decisions, choices, floors, upgrades, events, results = {}, {}, {}, [], [], []
    types = collections.Counter()
    def state_summary(s):
        return {**{k:s.get(k) for k in ('act','floor','hp','maxHp','gold','room')},
                'deckSize':len(s.get('deck',[]))}
    for path in sorted(root.glob('session-*.jsonl')):
        for line in path.open(encoding='utf-8-sig'):
            try:
                r = json.loads(line)
            except json.JSONDecodeError:
                continue
            t, d = r['type'], r['payload']; types[t] += 1
            if t == 'state':
                s = d['state']; key = (d['runId'],s['act'],s['floor'])
                entry = floors.setdefault(key,dict(runId=d['runId'],first=state_summary(s)))
                entry['last'] = state_summary(s)
            elif t == 'decision':
                s = d['state']
                # Candidate and deck effects are kept for faithful offline replay.
                decisions[d['id']] = dict(id=d['id'],runId=d['runId'],kind=d['kind'],
                    state=s,options=d['options'],evaluation=d['evaluation'])
            elif t == 'choice':
                choices[d['decisionId']] = d['key']
            elif t == 'auto_partial_reward_choice':
                detail = d.get('payload',d)
                if isinstance(detail.get('selected'),str):
                    choices[detail['decisionId']] = detail['selected']
            elif t == 'upgrade':
                upgrades.append(dict(decisionId=d['decisionId'],card=d['card']['name'],className=d['card']['className']))
            elif t == 'auto_event':
                detail = d.get('payload',d)
                events.append(dict(state=state_summary(detail.get('state',{})),event=detail.get('eventId'),
                    option=detail.get('textKey'),choice=detail.get('choice')))
            elif t == 'run_result':
                results.append(dict(runId=d['runId'],victory=d['victory'],state=state_summary(d['state']),
                    deck=[dict(name=c['name'],className=c['className'],upgrade=c['upgradeLevel']) for c in d['state']['deck']]))
    draft = []
    for key,d in decisions.items():
        selected = next((o for o in d['options'] if o['key']==choices.get(key)),None)
        chosen_score = next((o for o in d['evaluation']['options'] if o['key']==choices.get(key)),{})
        d['actualChoice'] = choices.get(key)
        if d['kind']=='cards' and selected:
            card = selected.get('card') or {}
            draft.append(dict(decisionId=key,runId=d['runId'],**state_summary(d['state']),
                selected=selected.get('name'),kind=selected['kind'],className=card.get('className'),
                rarity=card.get('rarity'),score=chosen_score.get('score')))
    return dict(case=case.name,events=dict(types),results=results,floors=list(floors.values()),
        draft=draft,upgrades=upgrades,eventChoices=events,decisions=list(decisions.values()))


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('cases',nargs='+',type=Path)
    parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args()
    reviewed=[review(c) for c in args.cases]
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(reviewed,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for c in reviewed:
        picked=[d for d in c['draft'] if d['kind']=='card']
        skipped=sum(d['kind']=='skip' for d in c['draft'])
        print(c['case'],'observed picks',len(picked),'skips',skipped,
              'rarities',dict(collections.Counter(d['rarity'] for d in picked)))
        for d in picked:
            if d['score'] is not None and d['score']<1:
                print('  low-margin',d['floor'],d['selected'],d['score'])
