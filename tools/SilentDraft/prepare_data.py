"""Normalize a saved Spire Codex response and optional local Chinese localization.

No network access, game-file redistribution, or fallback across statistic cohorts.
Usage: prepare_data.py --metrics <response.json> --date YYYY-MM-DD
       [--localization <native-cards-localization.json>] [--out <directory>]
"""
import argparse
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
BRACKET='solo:standard:a10:v0.111.0'
URL='https://spire-codex.com/api/runs/metrics/cards?bracket='+BRACKET+'&character=SILENT'

def read(p):return json.loads(Path(p).read_text('utf-8-sig'))
def write(p,v):p.write_text(json.dumps(v,ensure_ascii=False,indent=2)+'\n','utf-8')

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--metrics',required=True)
    parser.add_argument('--date',required=True)
    parser.add_argument('--native',default=str(ROOT/'data/silent-cards-v0.111.0.json'))
    parser.add_argument('--localization')
    parser.add_argument('--out',default=str(ROOT/'data'))
    args=parser.parse_args()
    native=read(args.native);raw=read(args.metrics)
    assert raw['character']=='SILENT' and raw['bracket']==BRACKET, 'Wrong character/version/mode/ascension cohort'
    assert len(native)==91 and len({d['basic']['className'] for d in native})==91, 'Native pool changed: review schema and profiles'
    loc=read(args.localization) if args.localization else None
    for d in native:
        for key in ('basic','upgraded'):
            c=d[key];c['multiplayerOnly']=d['multiplayerConstraint']=='MultiplayerOnly'
            if loc:
                id=c['id'].removeprefix('CARD.')
                c['name']=loc[id+'.title'];c['description']=loc[id+'.description']
    byid={r['id']:r for r in raw['rows'] if not r['upgraded']}
    cards=[]
    for d in native:
        c=d['basic']
        if c['multiplayerOnly'] or c['rarity'] not in ('Common','Uncommon','Rare'):continue
        id=c['id'].removeprefix('CARD.');r=byid[id]
        cards.append(dict(className=c['className'],id=id,name=c['name'],rarity=c['rarity'],
            pickRate=r['pick_rate'],holderWinRate=r['win_rate'],winRateCi=r['win_rate_ci'],
            lift=r['lift'],sample=r['picks'],offered=r['offered'],picked=r['picked'],pickRateByAct=r['pick_rate_by_act']))
    assert len(cards)==80, 'Regular single-player coverage changed'
    stats=dict(source='Spire Codex community-submitted runs, NOT Mega Crit telemetry',url=URL,
        retrievedDate=args.date,dataThrough=raw['data_through'],bracket=BRACKET,
        characterRuns=raw['character_runs'],characterWins=raw['character_wins'],definitions={
            'pickRate':'Reward-screen selections / offers, percentage; not reward appearance probability',
            'holderWinRate':'Win outcome of seats holding/acquiring the card as defined by provider; not causal pick benefit',
            'sample':'Provider picks (acquisition/holder observations); NOT identical to reward-screen picked',
            'lift':'Provider floor-adjusted difference vs player expected win rate, percentage points; observational',
            'caveat':'Submitted saves, not random official sample; mods/solver use not controlled; upgraded rows separate; no extrapolation to win probability'
        },cards=cards)
    out=Path(args.out);out.mkdir(parents=True,exist_ok=True)
    write(out/'silent-cards-v0.111.0.json',native)
    write(out/f'silent-community-{args.date}.json',stats)
    print(f'Validated {len(native)} native entries and {len(cards)} statistic rows for {BRACKET}')

if __name__=='__main__':main()
