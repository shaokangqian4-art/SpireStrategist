# 猎人机制模型与全局接管：2026-11-01

当前本地版本 0.6.55，模型 `silent-draft-0.2`，游戏数据 0.111.0。已接入猎人单人建议、全局接管和循环；默认手动建议，循环需 CombatSolver。未发布 Steam/GitHub，不宣称已校准实战胜率。0.6.51 将猎人捏奥恢复为全角色共用用户优先级；无独立的猎人捏奥基础分支。

## 文件

- `data/silent-cards-v0.111.0.json`：本机原生牌池，基础与升级状态；91 条，含 80 张单人常规牌、4 基础、2 先古、5 多人。名称和描述来自本机同版本中文资源。
- `data/silent-community-2026-11-01.json`：Spire Codex 单人/标准/A10/0.111.0 猎人统计，记录查询地址、时间、分母定义和限制。不是官方遥测。
- `tools/SilentDraft/build_profiles.py` / `data/silent-draft-v0.2.json`：86 张逐卡机制先验，5 张多人牌忽略；同稀有度统计只提供有限修正。
- `src/SpireStrategist.Core/SilentPolicy.cs`：实际牌组分析、条件联动、免费抓牌分解；由共用 `Policy` 分派，路线、商店与事件使用各自已接入的策略入口。
- `tests/SpireStrategist.Tests/SilentTests.cs`：机制与决策回归，包含输出出口、未知牌、组合可行性和支持范围。
- `资料/猎人初版/猎人抓牌建模_v0.2.txt`：中文全文及逐卡参数，同目录有统计和六类情景评分 JSON。

## 研究方法

官方 [2026 年 5 月公告](https://www.megacrit.com/news/2026-5-22-neowsletter-issue-22/) 给出整体统计，并推荐收集自身数据的社区项目。未找到公开的官方逐卡完整抓取/胜率表。采用 [Spire Codex 固定筛选 API](https://spire-codex.com/api/runs/metrics/cards?bracket=solo:standard:a10:v0.111.0&character=SILENT)；不能把推荐社区站理解成官方提供统计。

基础先验来自当前原生机制；有限统计修正限定同稀有度、版本、角色、难度、模式，不直接用持有胜率排序。小刀与毒是主要输出方向，通常不另起一套；主动弃牌、过牌、费用、防御是辅助层。涂毒等单卡可以有现成收益，但不因此放开跨体系组件；单张过渡也不会永久锁定主线。当前方向由已拥有的核心与配套证据计算，来源于用户的实战优先级，尚待试玩验证。

强辅助首张仍可优先，但运转成熟、没有长战输出时，有真实启动支持的群蛇形态、刀刃陷阱等需要优先比较。未来可能出现的组件不是现有战力。

重点版本差异：计算下注升级仍消耗、只增加保留；刀刃之舞消耗；本能反应/战术大师是三费奇巧；计划妥当是二费金卡保留全手；墨影小刀施加虚弱；独门技术抽二并保留；谋划专家先打技能再获得奇巧；触媒每次额外触发仍消耗一层毒。

## 重现

使用 .NET 9。本机 SDK 为 `.tools/dotnet/dotnet.exe`，设置 `DOTNET_CLI_HOME=.tools/dotnet-home`、`NUGET_PACKAGES=.tools/nuget` 并传真实 `GameDir`。

```text
dotnet run --project tools/GameLab -c Release -p:GameDir=<game> -- export-silent
python tools/SilentDraft/build_profiles.py
dotnet run --project tests/SpireStrategist.Tests -c Release
dotnet run --project tools/SilentDraft -c Release -- examples 资料/猎人初版/六类局面评分明细.json
python tools/SilentDraft/report.py
```

只重建离线模型与报告时，从 `build_profiles.py` 开始即可，版本化数据已在仓库。更新数据时，先保存固定查询的原始 JSON，再运行下列命令，然后重新生成 profile。脚本验证筛选条件与 80 张牌覆盖，拒绝静默切换数据口径。

```text
python tools/SilentDraft/prepare_data.py --metrics <saved-response.json> --date 2026-11-01 --localization <local-native-cards-localization.json>
```

原生中文资源只用于合并 `name/description`（ID 去 `CARD.` 后接 `.title/.description`），不能从旧版网页取文本。模型不依赖描述字符串算分。`GameLab` 的运行时程序集定位目前仍为本机安装路径；换机器须调整其 resolver，不能只改编译 `GameDir`。

单一奖励回放：`SilentDraft evaluate <input.json> <output.json>`，输入有 `state`（RunSnapshot）和 `options`（DecisionOption[]）。此命令只回放给定决策；商店请使用 ShopPolicy（价格/删除竞争），事件使用 EventPolicy（真实代价），保留/删除/升级使用 SilentStrategy/StrategicValue。

## 新增模块与边界

- `SilentStrategy`：牌组战力、无色牌特征、升级/删除边际价值；保护现有输出链，允许基础牌退出。不把 Composition 当作已校准的精英/Boss 战损预测。
- `SilentRelics`：全部 8 个原生猎人专属遗物、初始遗物升级，以及苦无/手里剑等通用联动、古老牙齿/尘封魔典/佩尔之爪等角色敏感先古效果；其余共用遗物沿用 RelicValue。部分通用值仍是启发式，不能称为所有效果的完整模拟。
- `SilentForecast`：复用原生 RNG 副本读取器，最多 8 条候选路径、各 3 次供牌；每个奖励只取一个候选，商店扣实际可负担价格。比较“现在拿这张牌后配套能增加多少价值”，最高加 9 分；无效副本、多人与不相容方向不用预测。未来牌不写入真实牌组和当前 Readiness。
- 路线沿用共用安全与经济规划，改用猎人当前战力/待升级价值；早期未有伤害引擎时保守对待精英。不为远处火堆虚构提前回血。
- 事件、先古、商店与奖励使用同一猎人加牌入口，移除/变牌扣除旧牌价值，升级/附魔独立比较；未知内容明确保留手动边界。
- 未到达的所有事件/先古不是完整搜索树。辉眼原生覆盖奖励、商店和部分已到达的随机供牌/变牌；其他事件仍按当下已知机制估计，不承诺后续必遇某牌。
- 多人只读建议，5 张多人专属猎人牌忽略。战斗仍交给独立求解器，助手不修改求解器策略。

## 原生验证入口

使用 `scripts/test-autopilot.ps1` 的 `silent-native`（奖励/营火/商店/事件/先古多选/潘多拉）、`silent-audit`（原生事件初始选项与先古遗物识别）、`silent-loop`（人工结算的循环生命周期，不是胜率样本）、`silent-training`（自然局）。所有验证使用隔离 APPDATA；自然局的 ProgressFile 只读取并复制进隔离目录。

`SilentIntegrationTests` 验证前瞻真实影响评分、价格/互斥约束、未来战力隔离、核心保护、事件成本、升级和药水/遗物特征等。具体执行结果见 `资料/猎人初版/全局接管验证.txt`。统计存在上传偏差，未控制模组/求解器；少量原生对局只证明流程，不足以估计胜率。

`silent-map-transition`、`ironclad-map-transition`、`defect-map-transition` 验证共用转场保护：助手提交选路后，旧地图仍显示时不得再次选路；进入战斗后只关闭属于该次自动选路的遗留地图。顶栏/快捷键由玩家打开的地图撤销这项所有权，不自动关闭；停止接管时也不关闭地图。该测试人为重现残留界面，不计自然局胜负。


## 0.6.52 校准

修复已有刀刃陷阱仍推荐第二张、陷阱升级后继续重复投资纯小刀品质升级的问题。刀数量升级不扣分。以匿名化实战快照回归锐利→杀灭、腐化→吹哨；新快照记录哑铃次数，烘焙手套兼顾力量与清理。主动弃牌/自身消耗不当作烧掉其他牌。

211 项核心检查通过；原生猎人附魔建议与接管一致、Girya 计数读取及升级相互作用通过，案例 artifacts/autopilot-tests/20261101-134041-4644b19b。此次没有进行新版自然胜率测试，也未上传 Steam/GitHub。

## 0.6.55 实战修正

金字塔按禁止回合末弃牌处理，与子弹时间双向协同；计算下注仅加保留的升级在金字塔下无额外收益。重复计算下注按牌组厚度与已有数量评估，有上限及抽牌位成本。两张产刀来源与终结技参与主线识别，辉眼与当前选牌共用冲突门控；末幕已有输出链时不投资纯粹的新输出体系，桥接和独立防守用途仍保留。详见资料/猎人初版/金字塔_计算下注_末幕辉眼_0.6.55.txt。
