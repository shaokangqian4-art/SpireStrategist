"""Generate an auditable table from native metadata and the implemented explanations."""
import json, re
from pathlib import Path

root=Path(__file__).resolve().parents[2]
data=json.loads((root/'data/native-items-v0.111.0.json').read_text(encoding='utf-8'))
notes={}
for line in (root/'src/SpireStrategist.Core/CharacterItems.cs').read_text(encoding='utf-8').splitlines():
    key=re.match(r'\s*"([A-Za-z_]+)"=>',line)
    if key: notes[key[1]]=re.findall(r'"([^"\n]*)"',line)[-1]
roles={'Ironclad':'铁甲战士','Silent':'静默猎手','Defect':'故障机器人','Regent':'储君','Necrobinder':'亡灵契约师'}
out=['# 0.6.54 商店显示与物品机制审计','',
     '适用于原生 0.111.0。评分是可解释的机制先验，尚未通过大批胜率样本校准。此次没有改动逐卡基础先验，也没有发布 Steam/GitHub。','',
     '## 商店显示','',
     '- 所有在售卡牌都显示边际评分；买不起时标出“买不起”，不再从建议中消失。未知牌、多人专属牌不虚构分数。',
     '- 卡牌边际评分、物品适配评分与交易评分分开。预算不足、预留金币、药水栏已满与不值得购买分别解释。',
     '- 展示补全不改变原购买计划；不能因为显示了正分就购买预算外商品。五个角色共用此路径。','',
     '## 角色专属物品','',
     '以下 40 件遗物包含初始和商店品质，15 种药水覆盖每个角色普通、罕见、稀有各一种。角色归属用于核对覆盖，实际评分依据当前牌组触发机制。物品稀有不代表适配：低效稀有药水不会只凭品质替换有用药水。混沌、复活最高优先级及污浊兑现条件保留。','',
     '| 角色 | 类别 | 原生类名 | 品质 | 当前规则 |','|---|---|---|---|---|']
for kind,suffix in [('relics','RelicPool'),('potions','PotionPool')]:
    for hero,label in roles.items():
        for item in data[kind]:
            if item['pool']!=hero+suffix: continue
            key=item['id'].split('.')[-1] if kind=='relics' else item['className']
            out.append('| '+' | '.join([label,'遗物' if kind=='relics' else '药水',item['className'],item['rarity'],notes[key]])+' |')
out+=['','## 先古重点修正','',
      '18 件先古进入共用构筑评估：佩尔之爪、营养汤、尘封魔典、小提琴、珠宝面具、音乐盒、弩、艳丽围巾、铁棒、天鹅绒颈圈、尖刺护手、佩尔的士兵、佩尔之泪、佩尔之翼、祝福鹿角、大大大拥抱、保存的雾与玻璃眼（以原生 ID 为准）。其他先古沿用现有模型，不能将本次覆盖理解为所有先古都已精确校准。','',
      '- `PaelsClaw` / `Goopy.CanEnchant` 使用 Defend 标签：基础防御和究极防御有效；不把召唤或其他格挡牌当作附魔目标。',
      '- `NutritiousSoup` 原生 `AfterObtained` 要求 Basic 稀有度及 Strike 标签。虽然中文文本写“所有打击”，本版本代码并不覆盖非基础打击，也不加最大生命。',
      '- `BrilliantScarf` 同时实现能量与星费归零；储君星费出口也能受益。',
      '- `JeweledMask` 使用 `SetToFreeThisTurn`，只在首回合免费，并优先选择非固有能力；评分不假定总能选中最优牌。',
      '- `DustyTome` 按各角色实际升级先古牌判断，而非照搬战士消耗能力模型。',
      '- 灵魂、小刀、生成与零费连打影响小提琴和颈圈的代价；召唤承伤不算格挡；星费不等于能量费。',
      '- 开局捏奥保留用户指定的跨角色优先级；随机奖励不当作必然获得核心。','',
      '截图中的蜥蜴尾巴、金纸和星系仪也补入了共用商店模型：分别考虑一次复活、真实消耗频率、可跳过的五次选牌机会。','',
      '## 验证及边界','',
      '规则回归覆盖五角色低金币商店、40/15 原生池覆盖、同角色不同体系差异、稀有药水不适配替换、先古目标与费用限制，以及英文说明。实际游戏隔离测试检查 13 金币时七张卡牌都可评估、没有购买推荐，并继续验证正常预算交易和事件接管。具体结果见本次验证记录。',
      '模型仍按局外牌组估计战斗收益；没有完整模拟每个遗物在每一回合的所有触发，随机生成牌也不等于保证获得目标。','',
      '复现：`GameLab export-items` 导出本地游戏元数据，`python tools/ItemAudit/report.py` 更新本表。仅导出 ID、物品池、品质和数值；不分发游戏 DLL、资源或反编译源码。']
(root/'docs/item-model-audit-0.6.54.md').write_text('\n'.join(out)+'\n',encoding='utf-8')
