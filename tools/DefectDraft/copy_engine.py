def swift(c):return c.get('enchantment')=='ENCHANTMENT.SWIFT' and c.get('enchantmentAmount',0)>0 and not c.get('affliction')
def active(s):return any(c['className'] in {'AdaptiveStrike','Anger'} and swift(c) for c in s.get('deck',[]))
def adjustments(s,c):
 d=s.get('deck',[]);name=c['className'];selfcopy=name in {'AdaptiveStrike','Anger'}
 adaptive=any(x['className']=='AdaptiveStrike' for x in d)
 active=any(x['className'] in {'AdaptiveStrike','Anger'} and swift(x) for x in d)
 recall=any(x['className']=='AllForOne' for x in d);copies=sum(x['className']==name for x in d)
 if name=='AllForOne' and adaptive and not copies:yield ('适应打击生成可回收零费复制品，需先付费启动',7 if active else 5)
 if name=='AdaptiveStrike' and not copies and recall:yield ('已有万物一心可取回零费复制品',5)
 if selfcopy and swift(c):yield ('迅捷随新复制品继承，每个实例首次打出抽牌',min(8,c['enchantmentAmount']*2.5))
 if not adaptive and not active:return
 if name in {'ColdSnap','BallLightning','MomentumStrike','FocusedStrike','RocketPunch','Claw','HelixDrill'}:yield ('已有复制输出，额外攻击占用首轮与回收后的抽牌',-6 if active else -4 if recall else -3)
 if name in {'Scavenge','Purity'}:yield ('主动消耗基础牌帮助复制体系瘦身，保留核心',4 if not copies else -1)
 if name=='Hologram' and not copies:yield ('定向找回复制品或万物一心',2.5)
 if active:
  if "Exhaust" not in c.get("keywords",[]) and name not in {"AdaptiveStrike","AllForOne","Hologram","Scavenge","Purity","Turbo","Compact"}:yield ("复制引擎成立后提高新增常驻牌的门槛",-2)
  if name in {'Iteration','Scrape','MachineLearning'}:yield ('迅捷复制已提供抽牌，不额外扩展抽牌支线',-5)
  if name in {'Storm','Subroutine','Thunder','CreativeAi','Darkness','TrashToTreasure'}:yield ('已成立的复制引擎不再投资无关成长支线',-5)
  if name=='AdaptiveStrike' and copies:yield ('一张附魔复制核心已能自增，无需第二张原牌',-4)
