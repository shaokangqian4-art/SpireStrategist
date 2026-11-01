# 架构与模块定位

## 一次决策的数据流

`原生游戏 → SnapshotReader/各界面适配器 → Core 策略 → Evaluation/ShopEvaluation → 面板、推荐标记、日志 → 接管执行器`

运行时是本地 C# 规则和条件前瞻，无在线 GPT 调用，没有后台自动训练。开发时根据实战、异议和回归调整模型。CombatSolver 是另一个模组，负责战斗路线搜索与执行，不能把它的搜索能力描述成本项目独立实现。

| 层 | 入口 | 职责 |
|---|---|---|
| 无游戏依赖的数据与估值 | `src/SpireStrategist.Core/Contracts.cs`、`Policy.cs`、`StrategicValue.cs` | 快照、候选、评分分解；加牌、删牌、升级分别估值 |
| 卡牌机制 | `CardCatalog.cs`、`CardMechanics.cs`、`ExtraCardModels.cs` | 原生资料、附魔/异变、升级及特殊效果 |
| 战士 | `IroncladSynergies.cs`、`Policy.cs`、`data/cards-v0.111.0.json` | 消耗、力量多段、格挡等实际配合 |
| 机器人 | `DefectPolicy.cs`、`DefectComposition.cs`、`DefectDirection.cs`、`DefectDamagePlan.cs`、`DefectEngineLinks.cs` | 构筑方向、输出链、辅助饱和与核心投资 |
| 猎人 | `SilentPolicy.cs`、`SilentStrategy.cs`、`SilentRelics.cs`、`SilentForecast.cs` | 小刀/毒主线、共享辅助、升级/删除、战力、遗物与条件投资 |
| 路线/营火 | `RoutePolicy.cs`、`CampRoutePolicy.cs`、`ApotheosisPolicy.cs` | 到达血量、未来战损、升级及遗物行动 |
| 商店/药水 | `PriorityShopPolicy.cs`、`ShopPolicy.cs`、`ShopBudget.cs`、`RelicValue.cs`、`PotionPolicy.cs`、`CourierPolicy.cs` | 购买阶段、金币机会成本、联动、替换 |
| 事件/先古 | `EventRules.cs`、`EventPolicy.cs`、`NeowPolicy.cs`、`BridgePolicy.cs` | 原生代价、选择目标、有限多步前瞻 |
| 游戏适配 | `src/SpireStrategist.Mod/AdvisorService.cs`、`SnapshotReader.cs`、`EventAdapter.cs`、`SelectionAdvice.cs`、`ShopAdvice.cs` | 本机身份、前台界面、统一生成建议 |
| 可视化 | `AdvisorPanel.cs`、`RecommendationOverlay.cs` | 分类信息、持久化设置、只绘制的框与路线 |
| 自动执行 | `PartialAutopilot.cs` 及各 partial、`ClimbController.cs`、`SolverBridge.cs` | 原生动作、等待/打断、循环、求解器交接 |
| 条件预测 | `RewardForesightReader`、`DefectForesight`、`DefectBuildForecast`、`SilentForecast`、`BridgeForecast` | 复制随机流，不消费真实 RNG |
| 记录 | `Journal.cs`、循环批次代码 | 候选、实际选择、异议、版本、结算与导出 |

以上文件没有写目录的均在 Core，带游戏对象的适配/预测文件在 Mod；搜索类名定位即可。

## 评分和版本

基础先验是卡牌在未充分考虑当前局面前的初始评价，不是最终强度、抓取概率或胜率。最终评分再加入攻防缺口、输出成长、回转、能量、重复、体系配合、价格及预测投资。分数跨购买阶段不能直接混排。

模组版本位于 `Directory.Build.props` 和 manifest/UI。机器人先验版本由 `DefectPriorPath` 指定，当前为 `data/defect-draft-v0.14.json`；不要把目录内所有旧 JSON 同时修改。Core、实际打包 DLL 和 Python 离线评估有一致性检查，避免只修复一个评估器。

## 重要边界

- UI 读同一个评估结果，不另写一套选牌逻辑；接管正在执行的候选也要显示。
- 一个窗口有不同卡牌类型、附魔和多选数量，不应按角色复制整个窗口适配器。
- 读取建议和绘制标记不选牌、不买东西、不推进随机数。
- 未知效果明确标记；不为显示一个框而编造“强度最高”的结论。
- 建议模式和多人模式必须保持只读；自动动作由专门入口统一授权和限制。
