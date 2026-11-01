"""Reviewed 0.111.0 native ColorlessCardPool mechanisms; weights are priors, not fitted win rates."""
import json
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
# name | net base | upgrade increment | duplicate penalty | contextual coefficients | mechanism
ROWS='''Alchemize|3|1|2|sozu:-12|Generates a combat potion; Sozu prevents obtaining it.
Anointed|0|1|2|rare:0.65|Tutors random rare cards from the draw pile, limited by hand space; upgraded version retains.
Automation|-1|1.5|3|draw:0.65,cycle:0.6|Every ten cards drawn yields energy; needs sustained draw, not just a large deck.
BeatDown|-2|1|3|goodAttack:0.6,physical:0.5|Autoplays random attacks from discard; basics dilute the pool and first-cycle discard may be empty.
Bolas|-2|0.4|3|physical:0.65|Returns to hand after being played on the previous turn; free repeat attack occupies a hand slot.
Calamity|-3|1|4|physical:0.5,energy:0.25|Attacks generate random attacks that still cost energy; not free draw or a reason to collect filler.
Catastrophe|2|2|2.5|expensive:0.5|Autoplays two or three random playable draw-pile cards; discounts high-cost cards without guaranteeing a target.
DarkShackles|4|1.5|2.5||Temporary enemy Strength reduction; does not protect against non-attack effects.
Discovery|1.5|0.5|2.5||Chooses one of three random character cards, free this turn; discount random outcomes.
DramaticEntrance|0.5|0.5|3|early:1|Innate one-use AoE competes with engine setup in the opening hand.
Entropy|-1|0.5|3|junk:0.5|Transforms a hand card each turn in combat; replacement is random and not permanent removal.
Equilibrium|1|1|2.5|ice:1.5,expensive:0.25,pyramid:-3|Retains the hand for one turn and blocks; helps line up energy and expensive cards.
EternalArmor|0|1|3|energy:0.3|Persistent Plating with a three-energy setup cost; slow opening and repeated hits limit value.
Fasten|-2|1|4|defends:0.65,ghost:-3|Improves Defend-tag cards only; not Dexterity or a buff to all block.
Finesse|1|0.5|2|recall:2|Free self-replacement and small block; All for One can recover it.
Fisticuffs|-1|0.5|3|physical:0.65|Attack damage also becomes block; benefits from attack scaling, not Focus.
FlashOfSteel|0.5|0.3|2|recall:2,physical:0.2|Free attack with one draw; recycling is useful but it is not net draw by itself.
GoldAxe|-2|1|3|cycle:0.8|Damage grows with cards played during this combat, not gold; needs a fast long-fight engine.
HandOfGreed|-1|0.7|4|early:4,physical:0.3|Gold requires a killing blow; future shops and early fights matter more than rarity.
HiddenGem|4.5|1.5|2.5|goodTarget:0.4|Enchants a random eligible draw-pile card with two or three replays for this combat; not a chosen best card.
Impatience|3|1|2.5|attackRatio:-8|Draws only with no attacks in hand; attack-heavy decks cannot count it as reliable draw.
JackOfAllTrades|0|0.8|3||Random colorless generation, with no cost discount; one-use and uncertain output.
Jackpot|-1|1.5|3|physical:0.5,energy:0.4|Three-cost attack generates three random zero-cost character cards; upgraded version upgrades them.
MasterOfStrategy|4|1|2||Zero-cost net draw that exhausts; useful setup without a lasting deck burden in combat.
Mayhem|-1|1.5|4|expensive:0.4,xCount:-0.5|Autoplays the top card each turn; timing, targets and X-cost cards can waste effects.
MindBlast|-2|1|3|largeDeck:0.15|Opening attack scales with remaining draw pile; never rewards adding weak cards merely to increase size.
Nostalgia|-1|1|3|cycle:0.6,goodAttack:0.2|Returns the first discarded Attack or Skill each turn to the draw-pile top; can repeat filler instead of a core card.
Omnislice|-1|0.5|3|physical:0.6,early:0.5|Free multi-target attack; direct damage scales with attacks rather than orbs.
Panache|-2|1|4|cycle:1,physical:0.3|Deals AoE after five cards played in a turn; needs sustainable card throughput.
PanicButton|0|0.5|3|frost:0.8|Large immediate block followed by two turns of card-block suppression; Frost can cover the gap.
PrepTime|-2|1|4|physical:0.7|Grants Vigor each turn for an attack, not Strength or Focus.
Production|1|1|2|expensive:0.4,ice:1|One-use energy; retained energy helps, but it does not draw a payoff.
Prolong|-2|0.5|3|frost:0.5,block:0.3|Next-turn block copies current block; cannot count the best-case block without setup.
Prowess|-2|1|3|physical:0.6,block:0.2|Strength and Dexterity help attacks and card block; neither improves orb damage.
Purity|-2|0.7|2|junk:0.7|Retained selective hand exhaustion removes curses, statuses or spent transition cards; costs a draw slot.
Rend|-1|0.7|3|debuff:0.7,physical:0.3|Damage depends on distinct enemy debuffs, not total stacks of one debuff.
Restlessness|-3|1|3|cycle:0.8,pyramid:-2,junk:-0.2|Requires being the only card in hand before giving draw and energy; retention alone does not solve this condition.
RollingBoulder|-1|1|4|energy:0.3,block:0.3|Slow three-energy scaling AoE; requires time and defense rather than instant damage.
Salvo|-1|0.5|3|ice:1,expensive:0.2|Attack that retains the whole hand this turn; useful timing utility, not an orb engine.
Scrawl|4|0.7|2|pyramid:-2|Draws until the hand is full; upgraded version retains, it does not become zero-cost.
SecretTechnique|2|0.5|2|recall:1,goodSkill:0.3|Tutors a Skill from the draw pile; reliable access to a useful engine component.
SecretWeapon|1|0.5|2|recall:3,physical:0.25|Tutors an Attack, especially All for One or a physical finisher.
SeekerStrike|0|0.5|3|physical:0.3|Chooses among three random draw-pile cards; weaker selection than a full-pile tutor.
Shockwave|3|1|3|physical:0.4|AoE Weak plus Vulnerable; Vulnerable improves attacks, not normal orb damage.
Splash|0.5|0.8|3||Chooses a random off-class attack and makes it free this turn; uncertain target quality.
Stratagem|0|1.5|3|cycle:0.7|Tutors after shuffling; needs repeated cycling, not simply a small starting deck.
TheBomb|0.5|0.7|3|early:1,block:0.3|Delayed AoE resolves after three turns; survival and kill timing matter.
TheGambit|-25|0|5||Huge block but subsequent unblocked powered attack damage kills the player; never treat it as ordinary defense.
ThinkingAhead|0.5|0.7|2|ice:0.5|Draw two then put one on top; filters and arranges a hand but only modest net draw.
ThrummingHatchet|-2|0.5|3|physical:0.6|Returns after being played on the previous turn; repeat damage still costs energy and hand space.
UltimateDefend|-2|0.5|4|fasten:2|One-energy block with no draw, scaling or exhaustion; only explicit Defend synergies rescue it.
UltimateStrike|-3|0.5|4|strikeDummy:1.5|One-energy damage with no cycling or lasting scaling; low-priority transition even though it has a large number.
Volley|-1|1|3|physical:0.6,energy:0.4,ice:1,chemical:2|X repeated attacks convert energy and Strength into damage; Chemical X adds two hits.'''

def build():
 cards={}
 for row in ROWS.splitlines():
  name,base,upgrade,duplicate,weights,note=row.split('|')
  cards[name]=dict(base=float(base),upgrade=float(upgrade),duplicate=float(duplicate),weights={k:float(v) for k,v in (x.split(':') for x in weights.split(',') if x)},note=note)
 native=json.loads((ROOT/'data/cards-v0.111.0.json').read_text('utf-8-sig'))
 all_names={r['basic']['className'] for r in native if r.get('pool')=='ColorlessCardPool'}
 excluded=set('BeaconOfHope BelieveInYou Coordinate GangUp HuddleUp Intercept Knockdown Lift Mimic Rally TagTeam TheBall'.split())
 if all_names: assert set(cards)|excluded==all_names,(all_names-set(cards)-excluded,set(cards)-all_names)
 (ROOT/'data/defect-neutral-v1.json').write_text(json.dumps(dict(gameVersion='0.111.0',status='mechanism-reviewed-heuristics-not-trained-winrates',excludedMultiplayer=sorted(excluded),cards=cards),ensure_ascii=False,indent=2)+'\n','utf-8')

if __name__=='__main__': build()
