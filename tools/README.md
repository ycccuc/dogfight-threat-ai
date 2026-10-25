# 不依赖 Unity 的 Core 测试与历史 AI 演示

需要 .NET 8 SDK。在仓库根目录执行：

```powershell
dotnet test tools/DogfightThreatAI.Core.Tests/DogfightThreatAI.Core.Tests.csproj
```

首次运行需要恢复固定版本的 NUnit、NUnit3TestAdapter 和 Microsoft.NET.Test.Sdk。本机验证可沿用已恢复依赖，并明确关闭 NuGet 审计与工作负载解析：

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:ALLUSERSPROFILE = 'C:\ProgramData'
dotnet test tools/DogfightThreatAI.Core.Tests/DogfightThreatAI.Core.Tests.csproj --no-restore -p:NuGetAudit=false
```

- `DogfightThreatAI.Core` 直接编译 Unity 工程中的 `Scripts/Core/**/*.cs`，不复制生产逻辑，不引用 Unity。
- `DogfightThreatAI.Core.Tests` 验证飞行、AI、玩家/敌机推进资源和接触战斗；历史算法及其测试仍保留。
- 相机可见性、键盘、HUD 与渲染需另在 Unity 验证，见 [Battle 手册](../docs/04-manuals/Battle追击演示.md)。场景改名后的本轮结果见 [集成验证](../docs/03-test/场景改名与上游集成验证.md)；[全级强化验证](../docs/03-test/AI全级强化验证.md)、[锁轨与四级压迫验证](../docs/03-test/AI敌机锁轨与四级压迫验证.md)、[追赶推进与资源惩罚验证](../docs/03-test/AI追赶推进与资源惩罚验证.md) 保留各阶段的参数与结果。

## 当前 Battle：四级策略与有限追赶

Battle 是本文沿用的玩法名称，实际 Unity 场景已更名为 `GameScene`，路径为 `Assets/_Project/Scenes/GameScene/GameScene.unity`。常规 Play 先进入 `StartMenu`；单场景 AI 复验使用 `Tools → Dogfight Threat AI → Open Battle and Play` 临时直入 GameScene，退出 Play 后恢复 StartMenu 入口。完整导航与地图入口见 [获取与运行](../docs/04-manuals/获取与运行.md)。

默认输入源是 `PursuitChargeInputSource`，难度参数为 `PursuitDifficultyProfile`，观察历史为 `PursuitObservationMemory`。追赶资源由 `PursuitBoostModel` 管理，追赶路线选择在 `PursuitSteeringPlanner` 中完成；`MovingAttackPlanner` 规划移动预警及固定发射轴，`PursuitReentryPath` 负责全级对已观察稳定转弯目标的恢复切入。

| 级别 | 自动进入 | 常速 / 普通转向 | 追赶极速 / 最长时间 / 起手 CD | 策略 | 观察延迟/取样间隔 | 恢复 |
|---|---|---|---|---|---|---|
| Lv1 | 0秒 | 16 / 420°每秒 | 24 / 0.45秒 / 4.2秒 | 追踪，共享观察年龄补偿与转弯预测 | 0.06 / 0.08秒 | 1.40秒 |
| Lv2 | 20秒 | 17 / 450°每秒 | 28 / 0.50秒 / 3.5秒 | 根据闭合趋势作有界速度截击 | 0.04 / 0.06秒 | 0.65秒 |
| Lv3 | 40秒 | 18 / 480°每秒 | 32 / 0.60秒 / 2.8秒 | 直追与左右内切三候选，有限保持侧别 | 0.04 / 0.06秒 | 0.60秒 |
| Lv4 | 60秒 | 18 / 520°每秒 | 36 / 0.65秒 / 2.2秒 | 更快观察、可见躲避侧别与推进线索 | 0.02 / 0.04秒 | 0.20秒 |

时间来自战斗模拟时钟。正在执行的追赶推进及其退出、Windup、Charge、Recover 冻结当前档位，下一动作边界才应用自动成长或手动请求；能量和冷却保留。F1～F4 固定试玩，F5 自动，HUD 同样可选。基础机型资产的上限不会覆盖这些动作命令中的实际常速与转向。

追赶与攻击分开调度：每架敌机有 60 能量，一次追赶预付 30，起手 CD 按档位计；停推 0.8 秒后每秒回 15。仅 Pursuit 可启动，方向、距离和制动预测用于选择有限时长；使用有限加速度 40、退出减速度 30，推进和退出尾段的真实转向上限为 90°每秒。恢复、瞄准和冲撞中不起追赶，取消不退款、不返还 CD；敌机不套用玩家空槽过热。Charge 不另设能量费用。

距离不超过 8 且双方完整可见才开始完整 0.8 秒移动预警，期限不反复重置。前 0.5 秒继续跟踪规划，金线显示真实机头方向；最后 0.3 秒由 `MovingAttackPlanner` 提前承诺最终 Heading、固定的未来发射位置和入轨转向 bank。机身保持有限转向，沿已选弧线转入公布的轴，不因玩家后续动作改变承诺；末段提示的世界位置与方向固定。满 0.8 秒后以阻尼后实际前速 45 沿该轴直冲最多 0.9 秒，转向为零。若从追赶或退出中突然具备攻击条件，立即取消追赶；入口实际前速夹在本档常速与追赶上限之间，快照为整段 Windup 定速，并恢复本档普通转向，不先减速等待。

Recover 首步回到本档常速，完整执行表中的恢复时间。全级在已观察曲率置信度大于0.6、转率绝对值大于0.5弧度/秒且目标速度大于1时，使用`PursuitReentryPath`：冻结估计圆心与侧别，以本档16/17/18/18常速作连续外圈切入，通过切向引导与径向纠偏避免追过固定点再回头；否则普通飞离。默认飞离点在入口按`max(原4或12, CruiseSpeed * RecoverySeconds + 2)`延伸，再按原规则限制在场内，避免场内飞过目标点后提前折返。短恢复内只作有限方向调整，不要求绕完一圈。它不追读晚反向，也不以到点或最小分离距离阻塞下一轮；重复接触不重设期限或路线。玩家仍处于受击保护时可开始下一轮完整0.8秒预警，保护期内碰撞不会扣血。静止目标的窄屏切入利用画内角落保留转弯空间，攻击可见性和8单位起手条件保持一致。

观察样本有时间戳，策略只使用按级别延迟后的样本；全级补偿观察年龄，并在置信度足够时使用有界曲率预测、转向侧别与恢复重入。曲率历史按约0.48秒累计，最多128个间隔，0.02固定步仍用24个间隔；不可见目标不刷新样本。Lv3/Lv4的Pursuit路线评价最多三条、每条16步、1.2秒视野，每0.1秒重评；侧别保持有限，合理反向时可改道。移动预警的预测评分对中心距小于2的路径施加高惩罚，实际接触半径仍由碰撞体积决定，不将该留量当作判伤半径或任意操作安全保证。`IVisiblePropulsionCueSource`仅公开Hold/Dash/Overheat等可见表现，世界适配经过可见性门槛后，把线索和时间戳交给观察记忆；AI不读取玩家键码、Shift/Space状态、私有能量或未来输入。

`AttackCueView` 使用细长渐隐金线，承诺后转暖橙并配合机身小收束光环，不显示大末端箭头。追赶是蓝白分层尾焰；Charge 只保留暖白后尾流，没有前向冲撞路径、终点贴片或红色危险区标记。

原作参考来自《最后生还者》的目标记忆、分层、有限侧切与可读攻防节奏，这里适配为单敌机飞行路线和压力资源管理。当前没有掩体、小队或在线学习系统，也不声称复制原作 AI。

## 玩家长推、短冲与耗尽

`KeyboardInputSource` 在 Update 缓存 Space/Shift 边沿，固定步消费一次。玩家资源模型不读取 Unity 或键码，不写 `FlightState`。

- **Shift 长推**：无初始 Dash；实际前速 9 以上以 7.5 单位/秒²到上限 18，9 以下沿用普通起步加速度。每秒花 40，满 60 可供 1.5 秒，推进和正常尾段转向 180°每秒。
- **Space 短冲**：按下预付 25，以阻尼后的目标前速 21.6 执行 0.18 秒；起手 CD 1.2 秒。松开不截断已付短冲，持续按住不重复，CD 内按下不排队、不扣能。
- **动作交接**：Dash 抢占 Hold，不叠扣长推。结束时 Shift 仍按住且至少有 25 才恢复 Hold；已合法进入的 Hold 可降到 25 以下直到零。被拒 Space 不打断 Hold，缺能导致 Hold 起手/恢复失败后须松 Shift 再按。
- **退出与回充**：退出按 30 单位/秒²回到普通 9；高于 18 的 Dash 尾速转 Hold 先有限减速，不夹速。正常停推 0.7 秒后每秒回 20；Dash 及其尾段转向 240°每秒。
- **耗尽**：只有获准的正能量消费降到零才进入0.9秒过热，禁推进、无回能，有限减速靠近7.2，上限转向仍240°每秒。之后立即每秒回20；达到25并满足松键要求即可重新长推，Shift没有独立CD，Space还须满足自身1.2秒起手CD。从过热开始最早2.15秒可再次推进。恰好25的Dash先完成0.18秒再过热，从扣零起最早2.33秒。空槽乱按或S都不延长/清除过热债。
- **取消与重开**：S/下箭头优先刹车；死亡/失焦清边沿、取消当前能力、不退款，持键须先松。R 重开恢复 60 能量和零计时，但仍按住的能力键须先松；普通 WASD 保留。推进不增加无敌，0.7 秒受击保护仍由接触战斗模型管理。

主入口为：

```csharp
boost.Step(dashPressed, holdHeld, fixedDt, currentForwardSpeed, canBoost, braking, holdPressed);
```

`holdPressed` 保存两个固定步之间的极短 Shift 点按；旧 `Step(held, fixedDt, canBoost, braking)` 重载转为长推语义。HUD 可读 `Energy`、`IsOverheated`、`OverheatRemaining`、`DashCooldownRemaining`、`CanDash` 和现有动作状态。

`IFlightMotionSource.MotionCommand` 把普通惯性、有限加速与定速覆盖交给唯一运动写入者 `FlightModel.Step(ref state, input, fixedDt, command)`。命令中的速度统一为阻尼后的前向目标/上限，普通惯性路径仍保留自然阻尼；有限加速补偿前向阻尼而保留侧滑。可支付时间小于一个固定步时，`WithActiveSeconds` 只授权相应部分，剩余时间走有限退出，不能白送半步推力。历史速度倍率和巡航请求重载保留给旧算法与回归。

## 当前碰撞结算

玩家5HP；两机根物体各有半径0.5的`CircleCollider2D`。当前四级配置的`CollisionDamage`均为1，任意阶段的有效真实接触扣1HP，五次有效真实命中将5→4→3→2→1→0。受击无敌0.7秒、Art抖动0.18秒；持续接触不会每帧扣，但保护结束仍接触可以再次扣血。

```csharp
combat.StepContact(previousPlayer, player, previousEnemy, enemy, fixedDt, combinedRadius, ai.CollisionDamage);
```

`combinedRadius`取两机实际启用体积的世界半径之和。连续相对扫掠与无敌到期时段求交，处理高速穿越；每步读取当前冻结动作的伤害，不重建血量或保护时钟，伤害不受攻击编号限制。旧六参`StepContact`仍使用构造伤害；旧五参`Step`只推进时钟，六参`Step`供历史攻击窗口算法使用。当前Battle用七参`StepContact`结算。

## 四级压力与反制对照

```powershell
dotnet run --project tools/DogfightThreatAI.BalanceProbe/DogfightThreatAI.BalanceProbe.csproj -- TestResults/balance-matrix.json
```

该入口直接使用当前Core，执行四级×12初态×8策略，共384局，正常5HP并使用当前冻结动作的碰撞伤害，每局最长60游戏秒。策略包括静止、直飞、左右固定绕圈、冷却好就短冲、持续长推至耗尽、读预警换向加短冲、保留能量管理推进。输出为逐局JSON与终端分组统计；未出现预警的局在`firstWarning`记-1，终端以60代入表示观察期结束仍未发生，不能解释为恰好60秒发生。

这是无遮挡、无边界的确定性仿真；可见性用跟随玩家的矩形近似，不能代替Unity的真实相机、输入、渲染与原生键盘试玩。输出目录会自动创建。本轮复跑状态见[集成验证](../docs/03-test/场景改名与上游集成验证.md)，集成前生产参数实测及限制见[全级强化验证](../docs/03-test/AI全级强化验证.md)，更早版本数据见[锁轨与四级压迫验证](../docs/03-test/AI敌机锁轨与四级压迫验证.md)。

## 历史控制台演示

```powershell
dotnet run --project tools/DogfightThreatAI.AiDemo/DogfightThreatAI.AiDemo.csproj
```

**此入口仍是旧 `AttackRunInputSource`，不是当前 `PursuitChargeInputSource` 的等价演示。** 旧四级规则包含起手锁向、旧预警/准备点、安全准入和分离约束，不能套用当前 Battle。

此前结果见 [四级成长与双模式推进](../docs/03-test/AI四级成长与双模式推进验证.md)、[绕后与玩家冲刺](../docs/03-test/AI绕后瞄准与玩家冲刺验证.md)、[移动瞄准与成长修复](../docs/03-test/AI移动瞄准与成长修复验证.md)、[成长与反制](../docs/03-test/AI成长与反制验证.md)。

保存当前纯 Core 报告：

```powershell
dotnet test tools/DogfightThreatAI.Core.Tests/DogfightThreatAI.Core.Tests.csproj --logger "trx;LogFileName=core-tests.trx" --results-directory TestResults
```
