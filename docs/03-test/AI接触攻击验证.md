# AI 接触攻击验证

> 本文为旧版连续追踪与平滑增强的历史记录。当前四级成长、锁向预警与公平反制已替换该行为，见 [AI成长与反制验证](AI成长与反制验证.md)。

日期：2026-10-24。当前 Battle 默认玩法为接触攻击循环；[AI 间距验证](AI间距验证.md) 保留为旧版本记录。

## 已确认结果

| 验证 | 状态 | 证据 |
|---|---|---|
| .NET / NUnit 全套 | 126 / 126 通过，0 失败、0 跳过 | `contact-core-full.trx` |
| Unity EditMode | 126 / 126 通过，0 失败、0 跳过 | `contact-editmode-final.xml` |
| Unity Battle PlayMode | 8 / 8 通过，0 失败、0 跳过 | `contact-playmode-final.xml` |
| 独立随机闭环 | 5056 场完成五次实际接触至 HP0 | `E:\codex-temp\chase-stall-debug\FINAL-ATTACK-REPORT.md` |
| 真实编辑器画面 | 已进入 Play，确认血量 HUD、攻击结果和死亡/重开界面可见 | `contact-live-final.png` |

独立 .NET 工程直接链接同一份 Core 与 EditMode 测试源；126 项包括原有 87 项、攻击循环 21 项与接触战斗 18 项。实际 Battle 的反复命中、飞离、无敌与重开由 PlayMode 测试验证，编辑器画面检查单独记录。

本机证据目录：`E:\codex-temp\dogfight-ai-validation\`。`contact-core-full.trx` 的计数为 total=126、executed=126、passed=126、failed=0、notExecuted=0。

## 问题复现与目标

玩家原点静止、敌机位于 `(2.2, 0)`、机头朝向玩家且速度为零时，旧间距算法持续刹车，500 个固定步后仍停在 2.2 单位处。该行为满足旧版“保持距离”目标，却没有完成用户要求的攻击。

最小攻击回归先失败：`contact-attack-red.trx` 记录期望接触距离不超过 1，实际仍为 2.20000005。新版本的验收目标是敌机追上接触，玩家受伤后进入无敌帧，敌机飞离绕回，再次攻击，直到玩家血量归零。

独立子代理在最高难度发现第二个真实问题：刷新逃离航点同时重置阶段起点，使部分轨迹永远处于 BreakAway。seed 658872 在战斗时钟 60 秒起步时，旧实现仅命中一次后永久飞离；对应红测为 `attack-max-difficulty-red.trx`。修复将阶段时钟与航点刷新时钟分开，同一轨迹修后在 19.48 秒完成五击。回归同时覆盖时钟 0/30/60/120 秒。

另经独立审查修复大但有限的位置差溢出及非法战斗时钟；都有先失败后通过的回归证据（`attack-extreme-red.trx`、`attack-clock-red.trx`）。

## 当前规则

- 玩家初始 5 HP，每次有效碰撞扣 1 HP；无敌时间 2 秒，持续重叠不会每帧扣血。
- `ContactCombatModel` 是唯一伤害入口；Unity 在两机固定步后读取前后状态，以相对轨迹扫掠检测接触，避免高速穿越漏判或物理回调重复扣血。
- `AttackRunInputSource` 只输出 `FlightInput`。四个阶段为追击 `Pursuit`、飞离 `BreakAway`、重新占位 `Reengage` 与无目标 `NoTarget`。
- 玩家停车时敌机继续攻击；命中后必须真实飞离，再追玩家当前状态。机动点受战场边界限制。
- 敌机使用独立机型资产，最大速度 12、转速 300°/秒；AI 在 60 秒内平滑增强油门、转向响应与拦截提前量，到达上限后保持。输入由原有飞行模型执行，没有直接改位置或瞬移。
- HP0 后玩家停止操控，敌机停止攻击；按 R 重载 Battle，重置血量、时钟、命中计数与出生状态。

## 回归覆盖

Core 攻击循环测试覆盖旧停车距离的接触回归、静止与重合出生、战场两处墙角与边线、真实玩家加速转弯后刹车、飞离后再次命中及死亡、60 秒强度上限、重复读取不加快时钟、不修改快照、目标失效后再捕获、相同输入重放，以及非法时钟与极端有限坐标的合法输入。

接触战斗测试覆盖单次扣血、高速扫掠、双方移动时的相对接触、近失与相切、持续重叠、无敌结束前后的分段判定、HP 下限和死亡冻结、难度进度、无效时间与状态、极端坐标和配置检查。

Battle PlayMode 8 项已全部通过，直接加载保存的 Battle 场景，检查场景引用与独立机型、静止玩家连续受击直到死亡、无敌期间不连扣、命中后飞离、玩家移动转弯后停车继续受击、受击后离开仍被重新追上、难度增长、目标禁用或销毁、重开与首步插值。

随机压力验证对战斗起点 0/30/60/120 秒分别运行 1264 场：400 静止、400 随机飞行十秒后停车、64 真实墙角（边界半尺寸 200/150）、400 近距离或重叠出生，共 5056 场与 25280 次真实伤害。全部完成五击，最慢模拟时长 57.94 秒，敌机最高实测速度 11.976049；自主阶段距离、无敌、真实接触、输入合法性、速度上限和重复读取一致性检查通过。

自主飞离至少达到 3 单位后进入重新占位；无敌结束且距离至少 2.2 才重新追击。若玩家主动追回正在飞离的敌机，无敌结束后的真实碰撞仍会扣血。压力测试记录了一次这样的合法回撞，敌机始终处于 BreakAway，不能把它算作提前主动重攻。

## 复跑

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
dotnet test tools/DogfightThreatAI.Core.Tests/DogfightThreatAI.Core.Tests.csproj --no-restore
```

Unity 中先运行 `Tools → Dogfight Threat AI → Setup Battle Chase Demo` 升级场景与 prefab，再由 Test Runner 分别运行 EditMode 与 PlayMode。玩法和接线见 [Battle 追击演示](../04-manuals/Battle追击演示.md)。

`tools/DogfightThreatAI.AiDemo` 默认运行接触攻击演示。最终复跑：静止玩家在 0.72/3.42/6.10/8.78/11.46 秒受击，573 步重放一致；飞行转弯五秒后刹车场景在 5.38/8.06/10.74/13.40/16.06 秒受击，803 步重放一致。演示同时验证每次攻击的无敌间隔、飞离与重新占位。

## 参考材料边界

已核对 [B 站《最后生还者》人类敌人 AI 的 GDC 双语转载](https://www.bilibili.com/video/BV11JTMe6EtN/) 中 Skills/Behaviors、Combat coordinator/roles 与 Flanking 等关键画面，以及 [Naughty Dog 官方讲座说明](https://www.naughtydog.com/blog/2014_naughty_dog_gdc_talks_and_the_last_of_us_accolades)。本机没有独立字幕轨，抽查了视频关键帧，没有逐字分析完整 48 分钟。本项目明确区分决策阶段、具体机动和伤害结算；碰撞循环与渐进曲线来自当前游戏需求。
