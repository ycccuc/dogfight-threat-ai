# 机炮、五段生命环与 AI 接入

日期：2026-10-25

从 `StartMenu` 点击开始游戏，进入河谷 `GameScene`。

- WASD 驾驶，J 或鼠标左键发射机炮；长按连射，每发间隔 0.45 秒。
- Shift 持续加速、Space 短冲，沿用原有飞行操作。
- 右下角生命环固定五段；外、内半径比例为 5:3，每次有效命中扣一段。中心显示机炮剪影，短线显示冷却恢复。
- 双方都为五格生命，使用同一个机炮资产；飞机接触不再造成伤害。每次有效命中扣一格，保护时间为 0.7 秒。
- Esc 打开暂停菜单，可继续、选择难度、重开、返回主菜单或退出。退出有确认页；对局结束后提供重开与返回。
- 难度选择保存到本机；新兵、标准、老兵、王牌均为固定难度，不再按生存时间自动提升。

## 代码与数据边界

| 层 | 文件/目录 | 职责 |
|---|---|---|
| Core 配置 | `Core/Config/WeaponSpec.cs`、`Core/AI/GunnerProfile.cs` | 不可变机炮、AI 配置，不依赖 Unity |
| Core 战斗 | `Core/Combat/ProjectileCombatSimulation.cs` | 弹丸、冷却、双方生命与保护时间；输入飞行快照和射击意图，输出状态与事件 |
| Core 算法 | `Core/AI/Pathfinding/GridPathfinder.cs` | BFS 与带最小堆的 A*，支持障碍与 A* 单格代价 |
| Core 决策 | `Core/AI/BehaviorTree/`、`Core/AI/GunnerPilot.cs` | 响应式 Selector / Sequence / Condition / Action；结束、拉开距离、瞄准开火、寻路接近 |
| Gameplay 配置 | `Gameplay/Config/WeaponData.cs`、`GunnerProfileData.cs` | ScriptableObject 转换成纯配置 |
| Gameplay 胶水 | `BattleCombatController`、`ChaseAiInputSource`、`BattleUiPresenter` | 读取输入和状态，推进模拟，转发显示值与菜单命令 |
| 表现 | `ProjectileView`、`AircraftDamageView`、`UI/Battle/` | 弹丸、受击、圆环与菜单，只显示结果 |

依赖为 `Gameplay → Core`、`Gameplay → UI`；`UI` 仍不引用 `Core` 或 `Gameplay`，用显示值和事件连接。未增加程序集或全局对局单例。飞行仍由现有 `PlayerController / FlightModel` 推进，战斗组件在其后读取上一/当前固定步快照。统一的完整 BattleSimulation 后续可继续整合，当前没有为简单射击重写整个飞行系统。

## 配置入口

- `Assets/_Project/Settings/Weapons/Cannon.asset`：武器稳定 ID、间隔、弹速、寿命、弹丸半径、机口偏移。
- `Assets/_Project/Settings/AI/Gunner1.asset` 至 `Gunner4.asset`：名称、说明、寻路算法、观察间隔、提前量、瞄准容差、开火反应、射击节奏、巡航、转向与脱离距离。
- `GameScene / BattleCombat`：引用机炮和难度资产数组，设置生命、保护时间、逻辑网格尺寸。
- 增加同类武器或难度：创建相应资产并登记到组件；改变机制才新增 Core 类型。当前菜单按四档布局。
- 编辑器菜单 `Tools → Dogfight Threat AI → Apply Cannon Combat Setup` 可重新接线；已有难度资产的调参不会被覆盖。

## AI 审查与本轮取舍

旧 `PursuitChargeInputSource` 已有观察、追击、移动瞄准、推进和冲撞恢复等实现；它是服务于撞击玩法的专用状态机，不是通用行为树。旧 `Grid2D` 提供坐标与邻居，未实现 BFS / A* 搜索。

本轮在 GameScene 接入独立的射击 Pilot，在历史回归测试中显式启用旧撞击模型便于队员对照（不新增发行场景）。新兵在远距离接近时调用 BFS，其余三档调用 A*；进入近距离交战后直接朝预测点转向，不强迫飞机对齐格中心。当前河谷建筑属于地面美术，不作为飞机空中障碍，因此实际地图使用开放网格。障碍绕行和带权路径由算法测试验证，不能把开放地图上的搜索描述为已完成复杂障碍规避或威胁热力图。

新难度提高观察频率、提前量、反应和机动能力，不改变双方弹速、伤害、武器射速或生命。它是一版可调的基础射击 AI，尚不声称已完成真人难度平衡验收。

## 关键实现限制

- 机炮是有飞行时间的直线弹丸。本轮需求覆盖了原架构约定 7.1 的 hitscan 初稿，新增了弹丸运行状态；没有实现导弹、锁定、装填或有限总弹药。
- 采用弹丸与目标的相对运动线段扫掠，防止高速穿透；保护期间命中的弹丸也会被消耗。
- 弹丸不绕世界边缘传送，到期回收；当前原型两机各属于一方。
- 菜单不会显示内部算法、观察参数或 AI 调试状态。算法证据在测试与文档中查看。

验证结果见 [本轮验证记录](../03-test/机炮与圆环HUD验证.md)。

界面沿用 Noto Sans SC / Bebas Neue；本轮扩充了原有中文字体子集，授权仍见字体目录内的 SIL OFL 文件。
