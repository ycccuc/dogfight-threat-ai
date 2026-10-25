# AI原型交接

更新日期：2026-10-25。分支来源为`Chaos1207/dogfight-threat-ai:feat/chase-ai-prototype`，评审目标为`ycccuc/dogfight-threat-ai:main`。当前交付为 Unity GameScene 中的单敌机空战原型；Battle 沿用为玩法名称，实际场景路径为 `Assets/_Project/Scenes/GameScene/GameScene.unity`。完整玩法见[Battle手册](Battle追击演示.md)，算法与资源规则见[AI原型说明](../02-design/AI原型说明.md)。

当前账号没有上游仓库的直接推送权限，采用标准 Fork 流程：将同名功能分支推送到 Fork，更新现有[上游 PR #1](https://github.com/ycccuc/dogfight-threat-ai/pull/1)。本轮集成上游 `main` 的开始界面、河谷农田地图和场景改名，继续在同一 PR 评审；main 的合并由项目负责人决定。

## 当前实现

敌机持续追踪，在距离不超过8且双方完整可见时开始0.8秒移动预警。前0.5秒跟踪调整，最后0.3秒固定未来发射点和方向，机身按已选弧线入轨后以45前速直线冲撞；锁轨后不因玩家变向改轴。命中或扑空后完整恢复，再次追击。玩家受击保护期间敌机可以准备下一轮完整预警，伤害仍由保护时钟独立限制。

| 级别 | 自动进入时间 | 常速 / 普通转向°每秒 | 追赶上限 | 恢复秒 | 决策重点 |
|---|---:|---|---:|---:|---|
| Lv1 | 0秒 | 16 / 420 | 24 | 1.40 | 追踪、观察年龄补偿与有界转弯预测 |
| Lv2 | 20秒 | 17 / 450 | 28 | 0.65 | 根据已观察速度与接近趋势截击 |
| Lv3 | 40秒 | 18 / 480 | 32 | 0.60 | 比较直追、左切、右切并有限保持侧别 |
| Lv4 | 60秒 | 18 / 520 | 36 | 0.20 | 更快观察、可见躲避/推进线索与短恢复 |

全级可用已观察曲率作恢复重入，圆心与侧别在恢复入口冻结；没有历史条件时普通飞离，目标距离覆盖本轮完整行程加余量，避免场内越点折返。移动预警使用中心距2的预测留量，真实碰撞半径之和仍为1。

- 玩家5HP，每次有效真实接触扣1HP，5→4→3→2→1→0；任意阶段接触都可能受伤，受击保护0.7秒。高速接触采用两机相对轨迹扫掠，Art短抖不移动根碰撞体。
- Shift长推与Space短冲共享60能量。长推在前速9以上以7.5单位/秒²升到18，每秒耗40，没有独立CD；短冲预付25，以21.6前速执行0.18秒，起手CD1.2秒，按住不连发。
- 正常停推0.7秒后每秒回20；获准消费耗尽才进入0.9秒过热，禁推进、禁回能并有限减速。S刹车优先；死亡/失焦取消能力和待消费边沿，不退款。推进不增加无敌。

追踪预测使用可见历史与分档观察延迟，首次可见样本用于初始化；8单位预警门槛使用本步可见距离。推进表现另经过可见性、时间戳和延迟检查。AI不读取玩家键码、私有能量或未来操作。当前难度由规则、观察、路线和性能共同构成；没有掩体、小队、在线学习、玩家武器或服务器权威接线。

## 启动与复现

1. 使用Unity Hub安装并选择**2022.3.62f3c1**，打开仓库中的`src/client/DogfightThreatAI`。工程版本记录在[ProjectVersion.txt](../../src/client/DogfightThreatAI/ProjectSettings/ProjectVersion.txt)。
2. 打开 `Assets/_Project/Scenes/StartMenu/StartMenu.unity`，或执行 `Tools → Dogfight Threat AI → 打开完整游戏入口`。点击 Play 后按任意键进入菜单，再点“开始游戏”进入 GameScene；右上角“返回主菜单”可回到 StartMenu。常规 Play 由 `GameEntrySetup` 从 StartMenu 启动。
3. 点击Game窗口，用英文输入法操作：W加速、S刹车、A/D转向、Shift长推、Space短冲、R重开。F1～F4固定对应难度，F5恢复自动；进行中的动作结束后才应用切级，不重置能量或冷却。
4. 对照HUD的`HIT -1 HULL`、能量/短冲CD、`AIMING`、`LOCKED - DODGE`与`RECOVERING`。金线转橙表示轨道已承诺，可尝试横切或短冲；勿把预警阶段理解为免碰撞。

单独复验 AI 时执行 `Tools → Dogfight Threat AI → Open Battle and Play`，临时直入 `Assets/_Project/Scenes/GameScene/GameScene.unity`，退出 Play 后恢复 StartMenu 入口；仅打开 GameScene 后点击普通 Play 仍先进入菜单。已有出生点时使用 Open 菜单即可；Setup 菜单会维护场景/prefab，需退出 Play 后使用。Build Settings 顺序为 StartMenu（0）、GameScene（1），导航脚本实际加载这两个名称。完整运行、菜单和地图说明见[获取与运行](获取与运行.md)、[场景切换接入](场景切换接入.md)和[河谷农田地图](河谷农田地图.md)。

河谷地图保持 400×300 物理战场，相机世界半尺寸保持 (201.2, 151.2)。为覆盖镜头边缘增加的 2 单位地图外沿只作背景渲染，不改变飞行边界、碰撞体积或接触伤害；调试网格保留但 Play 中隐藏，星点背景移除。

默认物理固定步为0.02秒。Core批量对照、Unity真实场景和系统键盘输入各自验证不同边界，不能互相替代。本轮测试数量与结果以[场景改名与上游集成验证](../03-test/场景改名与上游集成验证.md)为准；[原交接版本验证摘要](../03-test/evidence/chase-ai-prototype/handoff-validation.md)保留为集成前基线，不从旧开发记录推断此次集成已经通过。

## 跨模块变更与评审范围

以下改动跨越[分工初稿](../00-project/分工.md)中AI、架构/模拟和UI/内容边界，请岗位A、B、E结合PR统一评审：

| 变更 | 接线与兼容性 | 评审重点 |
|---|---|---|
| `FlightInput`与`FlightMotionCommand` | `IPlayerInputSource`仍输出油门/转向；可选`IFlightMotionSource`另提供普通惯性、有限加速或定速命令，由`PlayerController`交给唯一运动写入者`FlightModel`，旧Step/倍率入口保留 | 新动作命令的速度语义、计费时长、退出与转向上限；旧输入源能否继续运行 |
| `PlayerBoostModel`与键盘桥接 | 资源模型为纯Core；Update缓存Space/Shift边沿，固定步消费一次，死亡/失焦清理 | 付费动作、冷却、耗尽和切换边界；没有直接改飞机状态 |
| 真实体积接触 | `AircraftHitbox`读取启用碰撞体世界半径；`BattleCombatController`在两机固定步后调用`ContactCombatModel.StepContact` | 高速扫掠、保护到期、同一战斗模型持续持有HP/时钟，不能由物理回调再扣一次血 |
| 公开推进线索 | `AircraftSnapshot`增加可见性、推进表现与时间戳，`IVisiblePropulsionCueSource`只公开Hold/Dash/Overheat | 信息经过可见性和观察延迟，不能暴露私有资源或输入 |
| 只读状态与表现 | `PlayerController.State/PreviousState`提供值快照；`PlayerView`保留未抖动`RenderPosition`供相机跟随 | AI不读Art Transform瞄准，机身抖动不污染碰撞或镜头 |
| 场景、prefab与HUD | 敌机、BattleCombat、体积、预警/尾焰与难度/资源HUD共同接入GameScene，保留上游菜单、河谷地图和返回入口 | 序列化引用及.meta完整，场景不保存运行期位置，表现不另做判定 |
| 场景改名与入口 | Menu / Battle 改为 StartMenu / GameScene，保留原场景.meta GUID；常规Play走菜单，AI工具直入GameScene | 运行时加载名、编辑器工具和测试路径一致，返回后不残留旧对局 |

当前键盘玩家的普通MotionCommand也固定提供9/18/240，未自动继承AircraftData的MaxSpeed/Acceleration/TurnRate；只修改这三项资产字段不能调整当前键盘机型。该原型限制与后续机型配置映射需岗位A一并评审，详见[Player移动说明](../02-design/Player移动说明.md)。

共享运动细节见[Player移动说明](../02-design/Player移动说明.md)。Core继续不依赖Unity；联机输入协议和服务器权威尚未接入，后续需评审新能力如何复现与校验，不能默认旧输入协议已经覆盖MotionCommand。

## 与分工初稿的差异

保留初稿作为历史计划，本次不把以下变化描述成已经完成团队契约确认：

| 初稿约定 | 当前原型 | 需要确认的事项 |
|---|---|---|
| 三档AI难度 | 四档，自动时间0/20/40/60秒，并支持固定试玩 | 最终产品档位、命名和成长节奏 |
| AI只能输出FlightInput | FlightInput加可选MotionCommand，AI仍不直接修改状态 | 共享动作接口与模拟/回放/网络适配方式 |
| 差异来自决策，不靠伤害或速度加成 | 各档玩家均为5HP、接触伤害均为1HP，但敌机常速、转向、追赶和恢复随级别改变 | 性能差异是否纳入最终设计；不能把当前版本宣称为纯决策对比实验 |

## 项目设置：后台运行

[ProjectSettings.asset](../../src/client/DogfightThreatAI/ProjectSettings/ProjectSettings.asset)的`runInBackground`从0改为1。这是项目级变更，使应用失焦后仍允许继续运行，不限于敌机模块；需在PR中单列并由集成方评审。键盘失焦清理仍会取消推进和未消费边沿。需要停止战斗时应先暂停，避免仅切换窗口而继续推进对局。

## 验证与证据携带

集成前交接版本的独立静态审查记录为：未发现Critical/Important问题；资源meta缺失、重复GUID、未解析GUID和fileID均为0；该次审查扫描的205个候选提交文件未检出密钥模式或构建缓存。该结果只覆盖当时的静态审查范围，不作为本轮静态检查结果，也不能替代真实编译、Unity测试或试玩。

从仓库根目录执行纯Core测试：

```powershell
dotnet test tools/DogfightThreatAI.Core.Tests/DogfightThreatAI.Core.Tests.csproj
```

Unity测试通过`Window → General → Test Runner`分别运行EditMode与PlayMode。本轮复跑状态和新证据入口以[场景改名与上游集成验证](../03-test/场景改名与上游集成验证.md)为准；[handoff-validation.md](../03-test/evidence/chase-ai-prototype/handoff-validation.md)记录集成前版本的命令、结果和可携带文件，不充当本轮测试结果。批量矩阵工具说明见[tools文档](../../tools/README.md)，历史控制台AiDemo仍用旧攻击入口。

[全级强化开发记录](../03-test/AI全级强化验证.md)与更早记录保留各阶段口径，其中本机临时目录、日志和截图路径不会随仓库传递。接收方应使用本次随仓库携带的证据与复现方法；一伤版本不能沿用早期多伤候选的生存倍率，脚本键盘成绩也不能写成人工手打。

当前一伤版没有有效的原生键盘试玩成绩，不能声称人工试玩通过。历史矩阵与截图不等于本轮复跑；提交验证摘要应分别列明本轮实际重跑的测试、携带的历史数据及尚未覆盖的边界。

## 变更记录

- 2026-10-25：记录同一 PR 的上游集成范围，更新 StartMenu / GameScene、双向导航与 AI 直入流程，并区分本轮证据与集成前基线。
- 2026-10-24：整理当前四级一伤原型、共享变更、初稿差异、后台运行设置与可携带提交验证入口。
