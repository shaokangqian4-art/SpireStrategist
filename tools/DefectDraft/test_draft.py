import copy
import json
import tempfile
import unittest
from pathlib import Path
import evaluate as e

class DraftTests(unittest.TestCase):
    def test_actual_boss_damage_plans(self):
        rows=json.loads((e.ROOT/'tests/SpireStrategist.Tests/Fixtures/defect-damage-plan-0639.json').read_text('utf-8'))
        for row in rows:
            s=copy.deepcopy(row['state']);s['hp']=s.get('hpAfterPendingAncient') or s['hp']
            result=e.evaluate(dict(state=s,options=row['options']))
            selected=next(o for o in row['options'] if o['key']==result['recommendedKey'])
            self.assertEqual(selected['card']['className'],row['expected'],row['label'])
    def test_adaptive_normal_copies_and_mature_orbs(self):
        s=self.with_cards('Glacier','Coolheaded','Hologram','Turbo','Scavenge')
        for name in ('Reboot','Ftl','Sunder','Null'):self.assertGreater(self.score('AdaptiveStrike',s),self.score(name,s))
        mature=copy.deepcopy(s);mature['deck'] += [e.snapshot(n) for n in ('Storm','Thunder','Defragment')]
        self.assertLess(self.score('AdaptiveStrike',mature),0)
        dark=copy.deepcopy(s);dark['deck'] += [e.snapshot(n) for n in ('ConsumingShadow','Null')]
        self.assertLess(self.score('AdaptiveStrike',dark),0)
        tentative=self.with_cards('Spinner','ChargeBattery')
        self.assertLess(self.score('GoForTheEyes',tentative),self.score('AdaptiveStrike',tentative))
        self.assertLess(self.score('Scrape',tentative),0)
        duplicate=copy.deepcopy(s);duplicate['deck'].append(e.snapshot('AdaptiveStrike'))
        self.assertLess(self.score('AdaptiveStrike',duplicate),self.score('AdaptiveStrike',s)-5)
    def test_shared_bridge_budget_and_actual_partners(self):
        s=self.with_cards('SweepingBeam','FocusedStrike','Ftl','Reboot','Hologram','Turbo',act=3)
        for n in ('Sunder','Null'):self.assertLess(self.score(n,s),0)
        physical=self.with_cards('AdaptiveStrike','AllForOne','Turbo',act=3)
        dark=self.with_cards('Darkness','Defragment','MultiCast',act=3)
        for state,name in ((physical,'Ftl'),(dark,'Null')):
            self.assertFalse(any(p['reason'].startswith('过渡攻击共用') for p in e.score(state,e.snapshot(name))['parts']))
        self.assertGreater(self.score('Sunder'),0)
    def test_retrieval_completion_needs_all_owned_parts(self):
        s=self.with_cards('Turbo','Ftl');s['deck'].append(e.snapshot('Hologram',True))
        def completion(state):return sum(p['value'] for p in e.score(state,e.snapshot('AllForOne'))['parts'] if p['reason'].startswith('这张牌补齐'))
        self.assertGreater(completion(s),0)
        for name in ('Turbo','Ftl','Hologram'):
            less=copy.deepcopy(s);less['deck']=[c for c in less['deck'] if c['className']!=name]
            self.assertEqual(completion(less),0)
        s['deck']=[e.snapshot('Hologram') if c['className']=='Hologram' else c for c in s['deck']]
        self.assertEqual(completion(s),0)
    def test_colorless_functionality_and_bad_flat_stats(self):
        self.assertEqual(len(e.NEUTRAL),53)
        for name in e.NEUTRAL:
            self.assertIsNotNone(e.score(e.starter(),e.snapshot(name))['score'])
        self.assertLess(self.score('UltimateStrike'),0)
        self.assertLess(self.score('UltimateDefend'),0)
        self.assertLess(self.score('TheGambit',up=True),-20)
        self.assertGreater(self.score('MasterOfStrategy'),self.score('UltimateDefend'))
        s=e.starter();s['relics']=['SOZU']
        self.assertLess(self.score('Alchemize',s),0)
    def test_relic_synergy_and_conditional_meteor_startup(self):
        s=e.starter();s['deck'] += [e.snapshot(n) for n in ['Storm','Thunder','DoubleEnergy','SignalBoost','Glacier']]
        no_ice=self.score('MeteorStrike',s);s['relics']=['ICE_CREAM']
        self.assertGreater(self.score('MeteorStrike',s),no_ice+8)
        self.assertGreater(self.score('SignalBoost',s),0)
        self.assertLess(self.score('Darkness',s),0)
        before=self.score('Storm',s);s['relics'].append('MUMMIFIED_HAND')
        self.assertGreater(self.score('Storm',s),before)
    def test_compact_repeat_needs_real_status_burden(self):
        s=self.with_cards('Turbo','Overclock')
        self.assertGreater(self.score('Compact',s),0)
        for owned_up in (False,True):
            for hp in (5,39,75):
                covered=copy.deepcopy(s);covered['hp']=hp;covered['deck'].append(e.snapshot('Compact',owned_up))
                for offered_up in (False,True):self.assertLess(self.score('Compact',covered,offered_up),0)
        heavy=self.with_cards('Turbo','Overclock','FightThrough','GunkUp','Compact')
        self.assertGreater(self.score('Compact',heavy),0)
        third=copy.deepcopy(heavy);third['deck'].append(e.snapshot('Compact'));self.assertLess(self.score('Compact',third),0)
        heavy['deck'].append(e.snapshot('FlakCannon'));self.assertLess(self.score('Compact',heavy),0)
    def test_utility_does_not_satisfy_damage(self):
        s=self.with_cards('Hologram','Turbo','Leap',act=1)
        for name in ('BallLightning','SweepingBeam'):self.assertGreater(self.score(name,s),0)
        before={n:self.score(n,s) for n in ('BallLightning','SweepingBeam')}
        s['deck'] += [e.snapshot(n) for n in ('Darkness','ConsumingShadow','Loop')]
        for name in before:self.assertLess(self.score(name,s),before[name])
    def test_concrete_engine_cap_and_nonpartners(self):
        s=self.with_cards('Voltaic','Storm','Subroutine','Defragment','BallLightning','Glacier','Turbo',act=2)
        s['deck']=[e.snapshot('Voltaic',True) if d['className']=='Voltaic' else d for d in s['deck']]
        def restored(state,name):return sum(p['value'] for p in e.score(state,e.snapshot(name))['parts'] if p['reason'].startswith('明确核心搭档'))
        self.assertGreater(restored(s,'Thunder'),0)
        self.assertGreater(self.score('Thunder',s),self.score('ChargeBattery',s))
        self.assertEqual(restored(s,'ChargeBattery'),0)
        duplicate=copy.deepcopy(s);duplicate['deck'].append(e.snapshot('Thunder'))
        self.assertEqual(restored(duplicate,'Thunder'),0)
        self.assertEqual(restored(self.with_cards('Glacier','Turbo'),'Thunder'),0)
        self.assertLess(self.score('SweepingBeam',s),self.score('SweepingBeam',self.with_cards('Hologram','Turbo','Leap',act=2)))
    def setUp(self): self.s=e.starter()
    def score(self,name,s=None,up=False): return e.score(s or self.s,e.snapshot(name,up))['score']
    def with_cards(self,*names,act=None):
        s=copy.deepcopy(self.s);s['deck'] += [e.snapshot(n) for n in names]
        if act:s['act']=act
        return s
    def test_all_pool_entries_classified(self):
        self.assertEqual(len(e.PROFILES),91)
        self.assertEqual(set(e.PROFILES),set(e.DEFINITIONS))
        self.assertEqual(sum(r['multiplayerConstraint']=='MultiplayerOnly' for r in e.DEFINITIONS.values()),5)
    def test_x_terminal_requires_increment_over_free_retrieval(self):
        s=self.with_cards('Darkness','Hologram','Coolheaded','Coolheaded','Defragment',act=3)
        s['deck']=[e.snapshot('Dualcast',True) if c['className']=='Dualcast' else c for c in s['deck']]
        parts=e.score(s,e.snapshot('MultiCast'))['parts']
        self.assertTrue(any('缺爆发费用' in p['reason'] for p in parts))
        for extra in ('Turbo','Fusion'):
            powered=copy.deepcopy(s);powered['deck'].append(e.snapshot(extra))
            self.assertFalse(any('缺爆发费用' in p['reason'] for p in e.score(powered,e.snapshot('MultiCast'))['parts']))
        self.assertFalse(any('缺爆发费用' in p['reason'] for p in e.score(s,e.snapshot('MultiCast',True))['parts']))
        self.assertTrue(any('冰霜防线' in p['reason'] for p in e.score(s,e.snapshot('Defragment'))['parts']))
    def test_solo_cards_have_finite_scores(self):
        for act in (1,2,3):
            self.s['act']=act
            for n,row in e.DEFINITIONS.items():
                for up in (False,True):
                    v=self.score(n,up=up)
                    if row['multiplayerConstraint']=='MultiplayerOnly': self.assertIsNone(v)
                    else:self.assertTrue(e.math.isfinite(v),n)
    def test_first_floor_needs_damage_not_speculative_claws(self):
        self.assertGreater(self.score('ColdSnap'),self.score('Claw'))
        self.assertLess(self.score('Claw'),0)
    def test_second_and_third_copy_diminish(self):
        first=self.score('Coolheaded')
        second=self.score('Coolheaded',self.with_cards('Coolheaded'))
        third=self.score('Coolheaded',self.with_cards('Coolheaded','Coolheaded'))
        self.assertGreater(first,second);self.assertGreater(second,third);self.assertLess(third,0)
    def test_status_payoffs_need_actual_generators(self):
        for n in ('Smokestack','TrashToTreasure','Iteration','FlakCannon'):
            self.assertLess(self.score(n),0,n)
            self.assertGreater(self.score(n,self.with_cards('FightThrough','GunkUp','Overclock')),self.score(n),n)
    def test_curses_are_not_status_generators(self):
        s=self.with_cards();s['deck'] += [dict(className='Regret',type='Curse')]*3
        self.assertEqual(self.score('Smokestack'),self.score('Smokestack',s))
    def test_plasma_does_not_support_focus(self):
        s=self.with_cards();s['deck']=[e.snapshot('Fusion')]*3;s['relics']=[]
        self.assertLess(self.score('Defragment',s),0)
        s['deck'] += [e.snapshot('Glacier'),e.snapshot('BallLightning')]
        self.assertGreater(self.score('Defragment',s),0)
    def test_meteor_cannot_pay_its_own_initial_cost(self):
        self.assertLess(self.score('MeteorStrike'),0)
        s=self.with_cards();s['energy']=5
        self.assertGreater(self.score('MeteorStrike',s),0)
        # A second copy is not itself a startup plan.
        self.assertLess(self.score('MeteorStrike',self.with_cards('MeteorStrike')),0)
    def test_expansion_requires_channeling(self):
        self.assertLess(self.score('Capacitor'),0)
        s=self.with_cards('Glacier','ColdSnap','BallLightning','Defragment','Spinner')
        self.assertGreater(self.score('Capacitor',s),self.score('Capacitor'))
    def test_x_cost_does_not_count_as_zero_cost(self):
        self.assertFalse(e.is_zero_attack(dict(className='UnknownXAttack',type='Attack',cost=0,costsX=True)))
        s=self.with_cards('Tempest','MultiCast')
        def retrieval_parts(state):
            return [p for p in e.score(state,e.snapshot('AllForOne'))['parts'] if '零费' in p['reason']]
        self.assertEqual(retrieval_parts(s),retrieval_parts(self.s))
        self.assertNotEqual(retrieval_parts(self.with_cards('Ftl')),retrieval_parts(self.s))
    def test_true_zero_cards_support_retrieval(self):
        s=self.with_cards('Ftl','GoForTheEyes','BeamCell','Turbo')
        self.assertGreater(self.score('AllForOne',s),self.score('AllForOne'))
    def test_card_specific_upgrade_values(self):
        self.assertGreater(self.score('Defragment',up=True)-self.score('Defragment'),
                           self.score('MachineLearning',up=True)-self.score('MachineLearning'))
    def test_new_late_algorithm_is_not_pretrained(self):
        late=self.with_cards(act=3)
        self.assertLess(self.score('GeneticAlgorithm',late),self.score('GeneticAlgorithm'))
        grown=e.snapshot('GeneticAlgorithm');grown['variables']={'Block':40,'Increase':3}
        self.assertGreater(e.score(late,grown)['score'],self.score('GeneticAlgorithm',late))
    def test_no_fixed_floor_count_for_extra_layers(self):
        a=self.with_cards();a['floor']=3
        b=copy.deepcopy(a);b['floor']=30
        self.assertEqual(self.score('ColdSnap',a),self.score('ColdSnap',b))
    def test_unknown_offered_card_causes_abstention(self):
        r=dict(state=self.s,options=[dict(key='x',kind='card',card={'className':'UnknownModCard'}),dict(key='s',kind='skip')])
        self.assertIsNone(e.evaluate(r)['recommendedKey'])
    def test_modified_offer_causes_abstention(self):
        c=e.snapshot('Glacier');c['enchantment']='NEW_ENCHANTMENT'
        self.assertFalse(e.score(self.s,c)['supported'])
    def test_journal_joins_actual_manual_choice_and_skip(self):
        r=dict(id='d1',runId='run1',kind='cards',state=self.s,options=[dict(key='c',kind='card',card=e.snapshot('Claw')),dict(key='s',kind='skip')])
        entries=[dict(type='decision',payload=r),dict(type='choice',payload=dict(decisionId='d1',key='c'))]
        with tempfile.TemporaryDirectory() as d:
            p=Path(d)/'sample.jsonl';p.write_text('\n'.join(json.dumps(x) for x in entries),'utf-8')
            out=e.review_file(p)
        self.assertEqual(len(out),1);self.assertEqual(out[0]['actualChoice']['key'],'c')
        self.assertEqual(out[0]['recommendedKey'],'s')
    def test_generic_status_insurance_without_self_generated_status(self):
        s=self.with_cards(act=2)
        self.assertGreater(self.score('Compact',s),0)
        covered=self.with_cards('Compact',act=2)
        self.assertLess(self.score('FlakCannon',covered),self.score('FlakCannon',s))
        sources=self.with_cards('Turbo','Overclock','FightThrough','Compact',act=2)
        self.assertGreater(self.score('FlakCannon',sources),0)
    def test_core_cards_compete_with_urgent_defense(self):
        s=self.with_cards('Storm','Subroutine',act=2);s['hp']=10
        self.assertGreater(self.score('Glacier',s),self.score('Turbo',s))
        self.assertGreater(self.score('Hologram',s),self.score('Hologram',self.with_cards('Hologram',act=2)))
    def test_storm_channels_support_thunder_and_voltaic(self):
        s=self.with_cards('Storm','Subroutine','Buffer',act=2)
        for n in ('Thunder','Voltaic'):
            self.assertGreater(self.score(n,s),self.score(n,self.with_cards(act=2)))
        s['deck'].append(e.snapshot('Thunder'))
        self.assertGreater(self.score('Voltaic',s),0)
    def test_power_engines_support_each_other_without_creative_ai(self):
        s=self.with_cards('Thunder','Buffer','Subroutine',act=2)
        self.assertGreater(self.score('Storm',s),self.score('CreativeAi',s))
        self.assertGreater(self.score('Storm',s),0)
        # A first Storm is now a bounded early investment, not blocked until
        # the player already owns the very Powers it is meant to enable.
        self.assertGreater(self.score('Storm'),0)
        self.assertGreater(self.score('Storm',s),self.score('Storm'))
        self.assertLess(self.score('Subroutine'),0)
    def test_physical_strength_and_orb_slot_tradeoff(self):
        physical=self.with_cards('Claw','Scrape','AllForOne','Feral','Hologram','Ftl',act=2)
        orb=self.with_cards('Glacier','Darkness','Loop','Defragment','Coolheaded',act=2)
        self.assertGreater(self.score('BulkUp',physical),0)
        self.assertLess(self.score('BulkUp',orb),0)
        self.assertGreater(self.score('Claw',physical),self.score('Claw',orb))
    def test_all_for_one_needs_retrievable_zero_cards(self):
        self.assertLess(self.score('AllForOne'),0)
        s=self.with_cards('Subroutine');s['deck'][-1]=e.snapshot('Subroutine',True)
        self.assertEqual(self.score('AllForOne'),self.score('AllForOne',s))
    def test_scrape_cares_about_density_not_only_count(self):
        dense=self.with_cards('Claw','Ftl','GoForTheEyes','Turbo',act=2)
        dilute=copy.deepcopy(dense);dilute['deck'] += [e.snapshot('DefendDefect')]*9
        self.assertGreater(self.score('Scrape',dense),self.score('Scrape',dilute))
    def test_upgrade_backlog_does_not_pretend_all_cards_upgraded(self):
        s=self.with_cards('Hologram','Storm','Subroutine','Coolheaded','Defragment',act=2)
        v=e.score(s,e.snapshot('Compact'))
        self.assertTrue(any('待敲核心' in p['reason'] and p['value']<0 for p in v['parts']))
        u=e.score(s,e.snapshot('Compact',True))
        self.assertFalse(any('待敲核心' in p['reason'] for p in u['parts']))
        self.assertGreater(u['score'],v['score'])
    def test_established_orb_engine_reduces_filler_attack_urgency(self):
        plain=self.with_cards('Buffer','Subroutine','Thunder',act=2)
        ready=self.with_cards('Buffer','Subroutine','Thunder','Storm',act=2)
        def need(s):
            return next(p['value'] for p in e.score(s,e.snapshot('SweepingBeam'))['parts'] if p['reason']=='即时输出缺口')
        self.assertLess(need(ready),need(plain))
        self.assertGreater(self.score('Storm',plain),self.score('SweepingBeam',plain))
    def test_supported_upgraded_third_claw_is_not_blanket_rejected(self):
        s=self.with_cards('Claw','Claw','AllForOne','Feral','Hologram','Scrape','Ftl','Turbo',act=2)
        self.assertGreater(self.score('Claw',s,up=True),0)
        self.assertGreater(self.score('Claw',self.with_cards('Claw','Claw'),up=True),0) # committed claw line may invest in third copy
        self.assertLess(self.score('Claw',self.with_cards(*(['Claw']*5))),0)
    def part(self,name,state,reason,up=False):
        return sum(p['value'] for p in e.score(state,e.snapshot(name,up))['parts'] if reason in p['reason'])
    def test_bulk_up_distinguishes_overflow_from_dark_growth(self):
        cycle=self.with_cards('Storm','Thunder','BallLightning','Coolheaded',act=2)
        dark=self.with_cards('Darkness','Capacitor','Loop','Coolheaded',act=2)
        self.assertGreater(self.score('BulkUp',cycle),self.score('BulkUp',dark))
        self.assertGreater(self.part('BulkUp',cycle,'更早顶球'),0)
        self.assertEqual(self.part('BulkUp',dark,'更早顶球'),0)
        self.assertLess(self.part('BulkUp',dark,'黑暗蓄力空间'),-1)
    def test_bulk_up_retains_passive_and_immediate_removal_costs(self):
        frost=self.with_cards('Glacier','Loop','Defragment',act=2)
        faster=self.with_cards('Glacier','Loop','Defragment','Coolheaded','ColdSnap',act=2)
        self.assertLess(self.part('BulkUp',frost,'保留球被动'),0)
        self.assertLess(self.part('BulkUp',faster,'保留球被动'),0)
        self.assertLess(self.part('BulkUp',frost,'减槽当场'),0)
    def test_third_slot_loss_does_not_claim_overflow_benefit(self):
        s=self.with_cards('BulkUp','BulkUp','Storm','Thunder','BallLightning',act=2)
        self.assertEqual(self.part('BulkUp',s,'更早顶球'),0)
        self.assertLess(self.part('BulkUp',s,'零球位'),0)
    def test_strength_support_does_not_count_orb_damage(self):
        orb=self.with_cards('Storm','Thunder','Darkness','Loop',act=2)
        self.assertEqual(self.part('BulkUp',orb,'直接攻击支持度'),self.part('BulkUp',self.s,'直接攻击支持度'))
        physical=self.with_cards('Claw','Ftl','Scrape',act=2)
        self.assertGreater(self.part('BulkUp',physical,'直接攻击支持度'),self.part('BulkUp',orb,'直接攻击支持度'))
    def test_darkness_passives_and_upgrade_have_value(self):
        dark=self.with_cards('Darkness','Capacitor',act=2)
        self.assertGreater(self.part('Darkness',dark,'漆黑触发'),0)
        self.assertGreater(self.part('Darkness',dark,'漆黑触发',True),self.part('Darkness',dark,'漆黑触发'))
        self.assertGreater(self.score('Darkness',dark,True),self.score('Darkness',dark))
    def test_slots_protect_dark_but_delay_storm_overflow(self):
        dark=self.with_cards('Darkness','Loop',act=2)
        storm=self.with_cards('Storm','Thunder','Buffer',act=2)
        self.assertGreater(self.part('Capacitor',dark,'保护黑暗'),0)
        self.assertLess(self.part('Capacitor',storm,'推迟'),0)
        self.assertEqual(self.part('Capacitor',dark,'推迟'),0)
    def test_multi_evokes_reward_dark_without_assuming_position(self):
        dark=self.with_cards('Darkness','Capacitor',act=2)
        for card in ('Dualcast','Quadcast','MultiCast','Shatter'):
            self.assertGreater(self.part(card,dark,'排球需另行实现'),0,card)
            self.assertEqual(self.part(card,self.s,'排球需另行实现'),0,card)
            self.assertGreater(self.score(card,dark),self.score(card),card)
        self.assertLess(self.part('MultiCast',dark,'留防守费用'),0)
    def test_dark_engine_does_not_remove_duplicate_penalties(self):
        dark=self.with_cards('Darkness','Capacitor','Loop',act=2)
        for name in ('Darkness','Capacitor','MultiCast','Shatter'):
            many=copy.deepcopy(dark);many['deck'] += [e.snapshot(name)]*3
            self.assertLess(self.score(name,many),self.score(name,dark),name)
    def test_early_engine_investment_without_waiting_for_full_combo(self):
        starter=self.with_cards();starter['map']={'bossRow':16,'currentRow':2}
        thunder=self.with_cards('Thunder','IceLance');thunder['map']=starter['map']
        self.assertGreater(self.part('Storm',starter,'未来投资'),0)
        self.assertGreater(self.score('Subroutine',thunder),self.score('CompileDriver',thunder))
        emergency=copy.deepcopy(starter);emergency['hp']=8
        self.assertLess(self.part('Storm',emergency,'未来投资'),self.part('Storm',starter,'未来投资'))
    def test_direction_changes_with_real_deck_and_allows_mixed_status(self):
        orb=self.with_cards('Storm','Thunder','Defragment','Glacier',act=2)
        self.assertLess(self.part('Scrape',orb,'物理组件'),0)
        hybrid=copy.deepcopy(orb);hybrid['deck'] += [e.snapshot(n) for n in ('Claw','AllForOne','Feral')]
        self.assertLess(self.part('Scrape',hybrid,'物理组件'),0) # a few off-line pieces no longer cancel an orb commitment
        self.assertGreater(self.score('Compact',orb),0)
    def test_mature_engine_rejects_filler_and_unrelated_dark_branch(self):
        orb=self.with_cards('Storm','Thunder','Defragment','Glacier','Loop','EchoForm',act=3)
        self.assertLess(self.score('ColdSnap',orb,True),self.score('ColdSnap',self.with_cards(act=3),True))
        self.assertLess(self.part('ColdSnap',orb,'过渡组件'),0)
        self.assertLess(self.score('ChargeBattery',orb),0)
        self.assertLess(self.part('Darkness',orb,'新黑球支线'),0)
        dark=copy.deepcopy(orb);dark['deck'].append(e.snapshot('Darkness'))
        self.assertEqual(self.part('Darkness',dark,'新黑球支线'),0)
    def test_all_for_one_does_not_treat_exhaust_skills_as_repeatable(self):
        s=self.with_cards('Turbo','Supercritical','Reboot','BootSequence')
        self.assertEqual(e.context(s)['retrieval_zero'],1) # Turbo remains repeatable; the other three Exhaust.
        self.assertEqual(self.part('AllForOne',s,'现有零费密度'),self.part('AllForOne',self.with_cards('Turbo'),'现有零费密度'))
        self.assertLess(self.score('AllForOne',self.with_cards('Supercritical','Reboot','BootSequence')),0)
    def test_all_for_one_can_support_orbs_with_sustainable_zero_skills(self):
        s=self.with_cards('Storm','Thunder','Defragment',act=2)
        for name in ('Zap','Dualcast','Coolheaded','Hologram'):
            # Synthetic zero-cost skill snapshots, including a valid upgrade with no Exhaust.
            c=e.snapshot(name,True);c['cost']=0;c['keywords']=[];s['deck'].append(c)
        self.assertGreaterEqual(e.context(s)['retrieval_zero'],4)
        self.assertEqual(self.part('AllForOne',s,'物理组件'),0)
    def test_repeat_hologram_accounts_for_existing_upgrade(self):
        a=self.with_cards('Hologram',act=3);b=copy.deepcopy(a);b['deck'][-1]=e.snapshot('Hologram',True)
        self.assertLess(self.part('Hologram',b,'不消耗全息'),0)
        self.assertEqual(self.part('Hologram',a,'不消耗全息'),0)
    def test_future_investment_uses_relative_map_progress(self):
        a=self.with_cards('Thunder');a['map']={'bossRow':16,'currentRow':4}
        b=copy.deepcopy(a);b['map']={'bossRow':32,'currentRow':8}
        self.assertEqual(self.score('Storm',a),self.score('Storm',b))
        b['map']['currentRow']=30
        self.assertLess(self.part('Storm',b,'未来投资'),self.part('Storm',a,'未来投资'))
    def test_optional_native_reroll(self):
        options=[dict(key='card',name='Glacier',kind='card',card=e.snapshot('Glacier')),dict(key='skip',kind='skip'),dict(key='reroll',kind='reroll')]
        self.assertEqual(e.evaluate(dict(state=self.s,options=options))['recommendedKey'],'card')
        options[0]['card']=e.snapshot('StrikeDefect')
        self.assertEqual(e.evaluate(dict(state=self.s,options=options))['recommendedKey'],'reroll')
        options[2]['enabled']=False
        self.assertEqual(e.evaluate(dict(state=self.s,options=options))['recommendedKey'],'skip')
    def test_physical_frost_defense_and_recovery(self):
        s=self.with_cards('Claw','Claw','Glacier','Coolheaded')
        self.assertLess(self.score('Thunder',s),0)
        self.assertGreater(self.score('Claw',s),0)
        self.assertLess(self.score('Hotfix',s),0)
        self.assertLess(self.score('Glasswork',s),0)
        s['deck'].append(e.snapshot('Claw'));s['act']=3
        self.assertGreater(self.score('Feral',s),3)
        self.assertLess(self.score('Hyperbeam',s),0)
    def test_smokestack_requires_status_density(self):
        s=self.with_cards('Claw','Claw','Glacier','Turbo','BoostAway',act=3)
        self.assertLess(self.score('Smokestack',s,up=True),0)
        s=self.with_cards('Turbo','BoostAway','Overclock','GunkUp','Compact','FlakCannon',act=2)
        self.assertEqual(self.part('Smokestack',s,'烟囱需要高密度'),0)
    def test_score_parts_sum(self):
        result=e.score(self.s,e.snapshot('Glacier'))
        self.assertAlmostEqual(result['score'],sum(p['value'] for p in result['parts']),places=3)
    def test_first_echo_engine_is_not_duplicate_power_bonus(self):
        s=self.with_cards('Modded','SweepingBeam','Null')
        self.assertGreater(self.part('EchoForm',s,'缺少长战引擎'),0)
        s['deck'].append(e.snapshot('EchoForm'))
        self.assertEqual(self.part('EchoForm',s,'缺少长战引擎'),0)
    def test_early_storm_and_flat_hailstorm(self):
        s=self.with_cards('ChargeBattery')
        self.assertGreater(self.score('Storm',s),self.score('Synthesis',s))
        s['deck'].append(e.snapshot('Claw'))
        committed=copy.deepcopy(s);committed['deck'].append(e.snapshot('Claw'))
        self.assertGreater(self.score('Storm',s),self.score('Storm',committed))
        frost=self.with_cards('Glacier','Glacier','Coolheaded','Defragment','Loop',act=3)
        self.assertLess(self.score('Hailstorm',frost),0)

    def test_actual_feedback_regressions(self):
        data=json.loads((e.ROOT/'tests/SpireStrategist.Tests/Fixtures/defect-feedback-regression.json').read_text('utf-8'))
        s=lambda floor:data[str(floor)]['state']
        self.assertGreater(self.score('Iteration',s(13)),0)
        self.assertLess(self.score('Turbo',s(24)),0)
        self.assertLess(self.score('Hotfix',s(45)),0)
        self.assertGreater(self.score('Rainbow',s(31)),self.score('Coolheaded',s(31)))
        self.assertGreater(self.score('Coolheaded',s(31),up=True),self.score('Coolheaded',s(31))+2)
        self.assertGreater(self.score('Supercritical',s(39)),self.score('SignalBoost',s(39)))
        projected=dict(s(33),hpAfterPendingAncient=60)
        self.assertGreater(self.score('EchoForm',projected),self.score('Spinner',projected))
        self.assertEqual(projected['hp'],2)

if __name__=='__main__':unittest.main(verbosity=2)
