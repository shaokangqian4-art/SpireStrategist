# 验证、复盘与发布

## 验证层次

1. `scripts/build.ps1 -GameDir <游戏目录>`：构建 Core/Mod、核心回归、打包评分一致性、原生 Hook/RNG 检查和结构矩阵。失败不得发布。
2. 离线工具与最终 DLL 评分要一致。改变先验或评分后更新有证据的期望值，不能为过测试直接覆盖所有基线。
3. `scripts/install.ps1 -GameDir .local/lab/game` 安装到隔离测试游戏。`scripts/test-autopilot.ps1 -EventLab <模式> -RenderUi` 启动隔离存档、离屏窗口，不操作玩家真实存档。
4. 0.6.49 推荐/药水目录矩阵：`advice-0649`（战士）、`defect-advice-0649`（机器人）；英文加 `-Language eng`。原生药水奖励替换可跑 `potions-0643`、`defect-potions-0643`。这些是功能回归，不是胜率测试。
5. 检查 `artifacts/autopilot-tests/<case>/` 的结果、日志及 `ui-review` 截图。只检查文字存在不等于框真正画出来；还要检查可见目标、手动点击、切换窗口与标记开关。
6. 自然对局验证需记录版本、角色、进阶、种子、求解器设置、完整/中途/干预类型。固定种子不保证不同选择后的随机流相同。

## 复盘方法

按一局完整时间线关联 `session_start`、state、decision、event_decision、shop_preview、selection_advice、实际动作、feedback 和结果。先找触发错误的最早决定，再看后续缺口和死亡；不能只看最后牌组。记录是否实际出现过目标卡，不能把没遇到当成拒绝。

先比较评分分解、门槛、可用性和前瞻，再决定改机制或调参数。保留修复前后同快照结果，补反例：提高核心投资不能导致无配套无限抓重复核心；减少普通过渡不能再次造成第一幕无伤害。每条实战意见只应用一次。

## Steam 更新流程

- 模组版本同步 `Directory.Build.props`、`mod/SpireStrategist.json`、`Entry.cs`、`AdvisorPanel.cs`、`ClimbController.cs`。先完成构建和验证，再生成发布目录。
- 用 `发布准备/<版本>/Steam上传工作区` 独立暂存。只上传 `content/SpireStrategist.dll` 与同名 JSON，顶层为 `image.png`、`workshop.json`、`mod_id.txt`。不得复制整个开发目录、游戏 DLL、玩家存档、日志或本地配置。
- `workshop.json` 使用当前中英文说明，保留现有条目 3823884252。不要把 CombatSolver 误设为强制依赖，独立建议功能不要求安装它。不要覆盖他人在 Steam 页面维护的图片/视频或依赖设置。
- 当前工具为 ModUploader；在工具目录执行 `ModUploader.exe upload -w <工作区绝对路径> -i 3823884252`。工具目录须包含 Steam API 等原配文件，客户端须已登录。
- 预览图小于 1 MiB；项目曾因长文本遇到 InvalidParam，说明控制在 8000 UTF-8 字节内，更新备注控制在 1024 字节内。
- 保存 DLL SHA-256、manifest 版本、上传日志和发布校验记录。上传返回成功后验证现有公开条目的版本/改动说明；有内容审核时如实报告状态。
- Steam 页面的隐藏 HTML 错误模板不是实际删除通知。不能仅搜索到单词 removed 就判定发布失败。
- 用户说“上线 Steam”即授权更新现有条目；不自动等同于 GitHub 推送、重新打 ZIP 或发布宣传视频。
- 正式游戏本地安装用 `scripts/install.ps1`，有备份与哈希核对；玩家需完整重启才能加载新 DLL。

## 隐私与宣传约束

作者名穷仔；不公开私人联系方式。发布页与视频说明本地规则架构、求解器分工及支持范围。历史 7/14、8/10、12/19 等试玩结果对应各自旧版本与样本，不能用作当前版稳态胜率。宣传视频以基础介绍、开发报告和后续计划为主，作者自行配音。
