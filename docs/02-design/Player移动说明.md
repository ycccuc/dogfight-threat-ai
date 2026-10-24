# Player 移动：架构与落地说明

适用版本：Unity 2022.3.62f3c1

最后更新：2026-10-24

相关文档：[架构约定.md](架构约定.md)、[AI原型交接](../04-manuals/AI原型交接.md)、[提交验证摘要](../03-test/evidence/chase-ai-prototype/handoff-validation.md)

## 一、代码位置与职责

Player 移动涉及的文件如下。

| 层 | 文件 | 职责 | Unity 依赖 |
|---|---|---|---|
| 配置资产 | `Gameplay/Config/AircraftData.cs` | 飞机数值与美术引用，ScriptableObject | 允许 |
| 纯配置 | `Core/Config/AircraftSpec.cs` | 模拟层使用的数值结构 | 不允许 |
| 配置转换 | `Gameplay/Config/AircraftSpecAdapter.cs` | 将 AircraftData 转为 AircraftSpec | 允许 |
| 模拟 | `Core/Flight/FlightModel.cs` | 飞行积分 | 不允许 |
| 状态 | `Core/Flight/FlightState.cs` | 位置、速度、机头朝向 | 不允许 |
| 输入意图 | `Core/Input/FlightInput.cs` | 油门与转向 | 不允许 |
| 输入接口 | `Core/Input/IPlayerInputSource.cs` | 输入源抽象 | 不允许 |
| 动作命令 | `Core/Input/FlightMotionCommand.cs`、`IFlightMotionSource.cs` | 普通惯性、有限加速或定速动作意图 | 不允许 |
| 推进资源 | `Core/Flight/PlayerBoostModel.cs` | Shift长推、Space短冲、能量与冷却、耗尽惩罚 | 不允许 |
| 输入实现 | `Gameplay/Input/KeyboardInputSource.cs` | 键盘边沿缓存、固定步消费、可见推进线索 | 允许 |
| 胶水 | `Gameplay/Flight/PlayerController.cs` | 输入、模拟、物理与视图的连接 | 允许 |
| 视图 | `Gameplay/View/PlayerView.cs` | 位置插值、机头旋转、换图 | 允许 |
| 相机 | `Gameplay/View/CameraFollow.cs` | 跟随、前瞻、世界边界夹紧 | 允许 |
| 接触结算 | `Core/Combat/ContactCombatModel.cs`、`Gameplay/Combat/AircraftHitbox.cs`、`Gameplay/Combat/BattleCombatController.cs` | 真实体积扫掠、血量与受击保护、Unity接线 | Core不允许，Gameplay允许 |

数据流如下：

    KeyboardInputSource
            ↓
    PlayerController
            ├─→ FlightInput + IFlightMotionSource.MotionCommand
            ├─→ FlightModel.Step(ref FlightState, FlightInput, dt, MotionCommand)
            ├─→ Rigidbody2D.MovePosition(state.Position.ToUnity())
            └─→ PlayerView.Render(state, previous, alpha, sprite)

`FlightModel`以`AircraftSpec`构造，消费`FlightInput`及可选`FlightMotionCommand`，统一推进`FlightState`。`PlayerController`先调用`ReadInput`，再读取本步动作命令；普通旧输入源仍走原`Step`入口，旧速度倍率接口继续兼容。AI和玩家能力都不直接改写位置或速度。Core不引用Unity，也不访问`transform`、`Input`或`Time`。

## 二、当前配置

### 2.1 飞机资产

当前使用的资产为 `Assets/_Project/Settings/Aircraft/ship_0001.asset`。

| 字段 | 当前值 |
|---|---|
| Display Name | 未命名机型 |
| Body Sprite | `ship_0001`（32×32） |
| Min Speed | 2 |
| Max Speed | 9 |
| Acceleration | 18 |
| Turn Rate Deg Per Sec | 240 |
| Drag | 0.1 |
| Lateral Grip | 8 |
| Drift Scrub | 0.35 |
| Visual Forward Offset Deg | 90 |

以上是资产字段值。`FlightModel`在`Awake`读取并保存基础配置，修改资产后需要停止Play再运行。但当前键盘输入始终发布`PlayerBoostModel.MotionCommand`，普通阶段也使用无参`Ordinary()`的9/18/240默认值，因此只改资产中的MaxSpeed、Acceleration、TurnRate不会改变当前键盘玩家的这三项运动限制。MinSpeed、Drag、LateralGrip、DriftScrub等仍由模型的基础配置提供。

Shift/Space和敌机各阶段也使用各自的动作命令。要让不同键盘机型继承这三项资产参数，后续需调整命令构造或输入源接线；当前原型未自动完成该映射，需架构岗位评审。实际推进参数见[Battle手册](../04-manuals/Battle追击演示.md)。

新增一架飞机时，复制现有 asset，修改数值与贴图，然后在 `PlayerController` 的 Ship 字段中引用。后续引入 `AircraftCatalog` 后，改为在 catalog 中登记。

### 2.2 Prefab 结构

`Assets/_Project/Prefabs/Aircraft/Player_Ship01.prefab` 的当前结构：

    Player_Ship01（根）
    ├─ Rigidbody2D
    ├─ CircleCollider2D（Radius 0.5）
    ├─ AircraftHitbox
    ├─ KeyboardInputSource
    └─ PlayerController
       └─ Art（子物体）
          ├─ SpriteRenderer
          └─ PlayerView

`PlayerView` 必须挂在子物体 `Art` 上。它每帧写入 `transform.position` 和 `transform.rotation`；若与 `Rigidbody2D` 位于同一物体，会与 `MovePosition` 争夺同一个 Transform。

Rigidbody2D 的主要设置：

| 字段 | 值 |
|---|---|
| Body Type | Kinematic |
| Interpolate | Interpolate |
| Collision Detection | Continuous |
| Constraints | Freeze Rotation Z |

### 2.3 场景配置

`Assets/_Project/Scenes/Battle/Battle.unity` 当前包含：

| 物体 | 配置 |
|---|---|
| Main Camera | Orthographic Size 5，挂 CameraFollow |
| CameraFollow | Target为玩家Art子物体，读取未受击抖动的RenderPosition；Smooth Time 0.03，Look Ahead 0，World Half Size (201.2, 151.2) |
| Player_Ship01 | 场景实例的 Arena Half Size 为 (200, 150) |
| DebugStarfield | 提供视差星点参照 |
| Grid Debug View | World Size (400, 300)，Cell Size 4 |
| Enemy_Chaser01 | 敌机输入桥接、真实体积、移动预警与推进表现 |
| BattleCombat | 两机固定步之后统一结算接触、5HP与0.7秒保护，提供HUD和重开 |

Build Settings 当前顺序为：Menu（0）、Battle（1）。

## 三、运行与验证

1. 打开 `Battle.unity`。
2. 点击 Play。
3. 使用W/S控制油门、A/D转向，Shift长推、Space短冲、R重开；F1～F4固定敌机难度，F5自动。
4. 进入`Window → General → Test Runner`，分别运行EditMode与PlayMode。测试数量、提交版本的执行结果和证据见[可携带验证摘要](../03-test/evidence/chase-ai-prototype/handoff-validation.md)，不沿用早期16项移动测试的数量。

预期行为：

- 不按键时飞机保持静止。
- 按 W 加速，松开后保持惯性滑行。
- 按 S 减速至停止。
- 按 A / D 转向，转向后存在横向甩尾并逐渐收敛。
- 相机跟随飞机，飞至 ±200 / ±150 时被边界挡住。
- Shift长推与Space短冲消耗同一个60能量槽，刹车/松手后有限退出；耗尽触发0.9秒过热，推进不提供无敌。
- 两机实际启用体积接触时每次扣1HP，受击保护0.7秒；Art受击抖动不改变模拟位置和碰撞体积。

批量运行测试时，可使用：

    Unity.exe -batchmode -nographics -projectPath <工程路径> ^
      -runTests -testPlatform EditMode ^
      -testResults <结果文件> -logFile <日志文件>

该命令不要加 `-quit`。当前工程在带 `-quit` 时会在测试执行前提前退出。

## 四、飞行模型

下面描述`Ordinary`兼容路径，未使用额外推进动作时每个固定步执行：

1. 将油门与转向限制在 [-1, 1]。
2. 按 `steer × turnRate × dt` 更新机头朝向。
3. 将速度分解为沿机头的前进分量和垂直机头的横向分量。
4. 根据油门确定目标前进速度：
   - 油门大于 0：加速至 `throttle × maxSpeed`，且不低于 `minSpeed`。
   - 油门小于 0：目标为 0，即刹车。
   - 油门等于 0：保持当前前进速度。
5. 按侧滑比例降低目标前进速度，系数为 `driftScrub`。
6. 以 `acceleration` 为上限，让前进速度逼近目标值。
7. 横向速度按 `e^(-lateralGrip × dt)` 衰减。
8. 按 `1 / (1 + drag × dt)` 施加整体阻尼。
9. 合成速度并更新位置。
10. 若位于战场边界外，夹紧位置并移除朝外的速度分量。

油门为 0 时保持当前速度，是这套模型的关键行为。早期版本使用 `(throttle + 1) / 2 × maxSpeed`，导致松开按键后仍以半速前进。当前实现已改为保持速度，不应再改回旧写法。

横向分量独立衰减，而不是让整个速度向量追随机头。这样转向时会产生甩尾，同时横向速度会自行收敛。

`FlightMotionCommand`另提供`FiniteAcceleration`与`SpeedOverride`。前者用于线性长推、减速退出和过热，明确加速、减速及转向上限；后者用于已付费短冲、敌机移动瞄准和锁向冲撞。命令中的速度按阻尼后的前向目标计，普通惯性仍保留自然阻尼。`WithActiveSeconds`只授权本步内已支付的时长，剩余时段走有限退出，避免能量不足整步时赠送推力。

当前Shift长推在巡航9以上以7.5单位/秒²加速到18，每秒花40能量；Space预付25，以21.6目标前速短冲0.18秒，起手CD1.2秒。两者共享60能量，正常停推0.7秒后每秒回20，耗尽进入0.9秒禁推进/禁回能阶段并有限减速。完整交接、松键锁和冷却规则见[AI与加速说明](AI原型说明.md)。

## 五、参数与手感

### 5.1 单位

像素素材的 Pixels Per Unit 为 32，因此 1 个世界单位约等于 1 个机身长度。最大速度 9 表示每秒飞过约 9 个机身长度。

相机 Orthographic Size 为 5 时，屏幕竖向可见 10 个世界单位，约 10 个机身长度。

### 5.2 当前参数的影响

下表解释参数用于基础Step或相应动作命令时的作用。当前键盘玩家的MaxSpeed/Acceleration/TurnRate来自动作命令默认值，不能只改同名资产字段验收；阻力、侧向抓地和漂移削速仍来自资产。

| 参数 | 当前值 | 调整后的效果 |
|---|---|---|
| Max Speed | 9 | 增大后速度更快，瞄准难度上升 |
| Acceleration | 18 | 从静止到满速约 0.5 秒；减小后加速更迟缓 |
| Turn Rate | 240°/s | 掉头 180° 约 0.75 秒；减小后转向更慢 |
| Drag | 0.1 | 松油门后速度约每 7 秒减半；增大后滑行距离缩短 |
| Lateral Grip | 8 | 增大后横向速度收敛更快，甩尾减弱 |
| Drift Scrub | 0.35 | 增大后侧滑时掉速更明显 |

Drag 的半衰期可由 `ln(2) / drag` 估算。当前值 0.1 对应约 6.9 秒。

### 5.3 调参检查

调参时可以用以下三项检查：

1. 滑行：满速后松开 W，飞机应继续滑行数秒，再明显减速。
2. 转向：按住 D，机头旋转 180° 应需要约 0.7 至 0.8 秒，而不是瞬间完成。
3. 对比：先确认输入源是否提供MotionCommand。测试基础资产参数时，使用不提供该命令的输入源或Core基础Step；当前键盘路径应比较命令实际提供的TurnRate，不能只把资产改为0/1000后据此判断模拟失效。

以下参数组合可作为不同机型的起点：

| 预设 | Max Speed | Acceleration | Turn Rate | Drag | Lateral Grip | Drift Scrub |
|---|---|---|---|---|---|---|
| 街机（当前） | 9 | 18 | 240 | 0.1 | 8 | 0.35 |
| 重型机 | 7 | 10 | 150 | 0.2 | 4 | 0.5 |
| 轻型机 | 12 | 26 | 320 | 0.08 | 12 | 0.2 |

具体数值仍需通过实际试玩确定。上表仅作为调参起点；应用于当前键盘玩家前，需让动作命令采用相应配置。

## 六、相机与地图

当前采用跟随相机与大地图组合：

- 地图范围 400×300，边界半尺寸为 (200, 150)。
- 相机通过 `CameraFollow` 跟随玩家，`Smooth Time` 为 0.03。
- 相机目标仍为玩家的`Art`子物体，但有`PlayerView`时读取未加受击抖动的`RenderPosition`，保持插值跟随且不把抖动传给镜头。
- `Look Ahead` 当前为 0，飞机保持在屏幕中心附近。
- `DebugStarfield` 提供视差参照，避免飞机看起来钉在屏幕上。

如果改为固定镜头，可移除 `CameraFollow`，将相机固定在 (0, 0, -10)，并把战场尺寸缩小到一屏范围。当前代码同时支持两种方式，`CameraFollow` 和 `Bounds2D` 都是可选项。

## 七、已知问题与后续调整

1. Legacy Input 可能被禁用。工程安装了 Input System 包。若 `Project Settings → Player → Active Input Handling` 只勾选 `Input System Package (New)`，`KeyboardInputSource` 中的 `Input.GetKey` 会运行时报错。应改为 `Both`，修改后需要重启编辑器。
2. 边界逻辑目前仍在 `FlightModel` 内。后续会移入独立的 `ArenaRule`，并由 `ArenaConfig` 提供参数。
3. `PlayerView` 必须位于 `Art` 子物体。若出现画面抖动，应先检查这一点。
4. 当前为单机结构。后续引入 `BattleSimulation` 与 `Bootstrap` 后，`PlayerController` 会拆分为负责接线的 `AircraftAgent` 和负责推进模拟的 `BattleSimulation`。`FlightModel` 本身不需要修改。
5. 联机阶段计划将 `FlightModel` 原样用于逻辑服务器。任何对 Unity 的依赖都会破坏这一计划，因此 Core 程序集保持 `noEngineReferences: true`。

## 八、相关文档

- [架构约定.md](架构约定.md)：分层、目录、提交规范。
- [AI原型交接](../04-manuals/AI原型交接.md)：共享动作、碰撞与可见线索契约的评审范围。
- [提交验证摘要](../03-test/evidence/chase-ai-prototype/handoff-validation.md)：可携带证据与执行结果。
- 立项书：《基于实时威胁评估与动态决策的飞机对战游戏》。

## 变更记录

- 2026-10-24：补充MotionCommand、推进资源、真实体积接触、稳定相机跟随及提交验证入口。
