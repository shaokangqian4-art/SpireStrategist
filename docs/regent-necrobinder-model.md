# 储君与亡灵契约师：机制模型和全局接管

本地版本 0.6.53，原生游戏 0.111.0。两角色均开放单人建议、全局接管和循环爬塔；初次安装仍不自动开启接管。自动战斗和循环需要独立安装 CombatSolver。多人只读建议，不接管、不调用辉眼，忽略多人专属牌。

这是首版机制模型，不是已经训练完成的胜率模型。没有把人工构造测试的胜利、补血、直接进入房间或模拟结算计入自然胜率。暂未发布此次 Steam/GitHub 更新。

## 与猎人相同的开发与校准流程

1. 从本机同版本原生类导出基础、升级、资源费用、关键词、多人限制，不靠名称猜机制。
2. 逐卡建立基础先验、重复递减、升级收益、阶段修正、功能标签；以输出链和资源缺口解释选择。
3. 加入专属遗物、先古、无色牌、药水和通用事件交互；捏奥沿用全角色共用优先级。
4. 分别验证选牌、升级、删牌、商店、事件、附魔、原生界面、求解器交接、循环开局和中止。
5. 社区试玩记录候选、推荐分解、实际选择、异议及结局。按同一根因修正一次，再用保留种子验证；不根据一两场胜负批量调稀有度。

## 数据与统计口径

每个角色原生导出 91 张，其中 86 张单人/基础/先古牌逐卡建模、5 张多人牌忽略。资源位于 `data/{regent,necrobinder}-cards-v0.111.0.json`，参数在同名前缀的 `draft-v0.1.json`。

未发现公开完整的官方逐卡抓取率/胜率数据。官方 [2026-05-22 公告](https://www.megacrit.com/news/2026-5-22-neowsletter-issue-22/) 推荐社区统计项目；这不等于社区数据是官方遥测。本次采用 [储君](https://spire-codex.com/api/runs/metrics/cards?bracket=solo:standard:a10:v0.111.0&character=REGENT) 与 [亡灵契约师](https://spire-codex.com/api/runs/metrics/cards?bracket=solo:standard:a10:v0.111.0&character=NECROBINDER) 的同版本单人标准 A10 观察数据，分别 14,465 / 13,925 局，截止 2026-10-31。查询和样本口径保存在 `data/*-community-2026-11-01.json`。

抓取率以“见到这张牌”为条件，不是牌的出现概率。先验优先使用原生机制；同稀有度统计仅小幅修正，选择数不足 200 或出现数不足 500 不修正，修正绝对值上限 0.8 并继续按样本量收缩。玩家水平、模组、获取阶段和构筑都有混杂；持有胜率不是卡牌因果强度。

出现概率继续使用共用原生生成器和原生稀有度状态；不把社区抓取率写成牌池随机概率。

## 储君

- 星星：供星、实际星费、消费出口分开；零能量不代表免费，星 X 与能量 X 分开保存。开局的 3/7 星是一次性资源，不是每轮供给。
- 铸剑：铸造、取剑、出剑费用、放大/重放分别建模；增加铸造不等于每次生成新剑。缺少铸造时降低孤立放大组件，但允许有配套前景的首张核心投资。
- 生成：真正生成牌才触发生成联动，普通抽牌不算；生成伤害、力量和格挡应有兑现出口。
- 连打与持续攻击可以独立形成长战输出；早期过渡只在当前战力缺口时增加价值，不用无限辅助挤掉核心。

## 亡灵契约师

- 奥斯提：召唤、攻击次数、生命成长、献祭、放大区分；玩家力量不增加奥斯提攻击，召唤不当玩家回血。献祭必须扣除已养大的奥斯提的价值。
- 灾厄：按敌方回合结束的斩杀阈值处理，不当作每回合持续掉血；施加、放大、提前兑现、防守和自施灾厄风险分别估值。
- 灵魂：区分生成、抽取、消耗与输出；普通过牌不当灵魂来源。灵魂与奥斯提等存在原生桥接，不机械互斥。
- 虚无：打出与回合结束消耗不同；自虚无不等于清除其他牌。高费虚无还要有减费、密度和回放条件。
- 高费与回收辅助依赖具体目标，不作为独立输出体系投票。

## 遗物、先古、事件和辉眼

16 个角色原生遗物（各 8 个）、升级初始遗物及有实际角色差异的通用遗物显式估值；共用遗物/事件继续使用既有模型。原生先古换牌为储君 FallingStar → MeteorShower、亡灵契约师 Unleash → Protector；Dusty Tome 使用当前角色先古卡。未知第三方效果继续保留手动处理。

选牌、商店、事件、先古出现的真实候选均走对应角色模型。敲牌与删牌独立评价，删掉最后的可用输出/资源引擎会受保护。营火与路线使用当前牌组战力、血量和后续恢复点，成型前谨慎连续精英。

辉眼会影响当前选牌：最多 8 条候选分支、每支 3 页、每页最多 8 候选，单次奖励只选一张，商店扣实际价格和剩余金币。只使用已验证源状态未改变的预测。当前实现预览沿路战斗奖励和商店；尚未到达事件、先古和后续交易不伪造确定结果。未来配套只增加条件投资分，不提前增加当前战力或减少当前预计战损。

## 参数、复现与反馈

- `tools/HeroDraft/build_profiles.py` 从已保存的原生导出与规范统计重建参数、英文文案及 `资料/储君与骨妹初版/*-逐卡参数.txt`；不依赖私人存档或联网。
- `tests/SpireStrategist.Tests/HeroTests.cs`：完整覆盖、星费、核心选择、重复、关键删牌、升级、前瞻预算、多人门控、遗物和中英文。
- 原生隔离用例：`regent-native` / `necrobinder-native`；战斗交接：`regent-combat` / `necrobinder-combat`；循环生命周期：`regent-loop` / `necrobinder-loop`。
- 运行例：`./scripts/test-autopilot.ps1 -EventLab regent-native -Seed SS-REGENT-0653 -LongMapLayers 0 -TimeoutSeconds 300`。
- 后续自然样本用 `regent-training` / `necrobinder-training`，必须提供复制到隔离环境的已解锁 `-ProgressFile`；不得把构造用例归入自然样本。
- 试玩者无需 GPT：在局内保存“异议”，尽量写预期选择及原因，结算后导出反馈 ZIP 交给作者。按角色、难度、种子、版本和是否人工干预区分，获胜与失败都保留。

English: Both characters now share the existing single-player advice/takeover/loop infrastructure, with separate native-mechanic deck models. Each has 86 modeled cards and five intentionally ignored multiplayer cards. CombatSolver remains a separate dependency. These initial priors have integration coverage, not a calibrated win-rate claim. Foresight influences acquisition only; it never grants future combat strength. Language continues to follow the game, with a saved manual override in Settings.
