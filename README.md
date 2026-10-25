# Dogfight Threat AI

一个用于验证飞行手感与敌机行为的 2D 俯视空战原型。

旧 AI 原型的启动、共享接口变更和待评审差异见[交接说明](docs/04-manuals/AI原型交接.md)。场景改名与上游集成的复跑状态见[集成验证](docs/03-test/场景改名与上游集成验证.md)；[原交接版本验证摘要](docs/03-test/evidence/chase-ai-prototype/handoff-validation.md)保留为集成前基线。

## 当前可玩内容

- 带惯性、转向与漂移的飞机操控，保留 Shift 加速、Space 短冲及过热/回能。
- 原生开始界面与河谷地图，支持 **StartMenu → 开始游戏 → GameScene → 返回主菜单**。
- 玩家和敌机共用机炮：一颗颗直线弹丸，每发间隔 0.45 秒；飞机接触不再扣血。
- 双方五格生命；每次有效命中扣一格，保护时间 0.7 秒。右下角以五段圆环显示生命，中心显示机炮与冷却。
- Esc 打开同一美术风格的暂停、难度与退出菜单；击毁敌机或自己被击毁后可重开。
- 四档固定 AI 难度通过资产配置；行为树组织拉开距离、接近与瞄准开火，新兵使用 BFS 接近，其余档使用 A*。
- 当前地形只作背景，不参与空中障碍判定；威胁热力图、导弹和联机仍为后续内容。

## 运行游戏

使用项目约定的 Unity **2022.3.62f3c1** 打开 `src/client/DogfightThreatAI`。本轮实际验证环境为本机团结 **2022.3.62t16 / 1.10.4**。

打开 `Assets/_Project/Scenes/StartMenu/StartMenu.unity`，点击 Play 后按任意键显示菜单，再点“开始游戏”进入 `GameScene`。Build Settings 保持 StartMenu（0）、GameScene（1）。

点击 Game 窗口，用英文输入法操作：**W/S 推进与刹车、A/D 转向、J 或鼠标左键射击、Shift 加速、Space 短冲、R 重开、Esc 暂停**。暂停菜单中可选择新兵/标准/老兵/王牌，选择保存在本机；当前版本不会按时间自动成长。

使用与配置入口见[机炮与圆环 HUD](docs/04-manuals/机炮与圆环HUD.md)，测试与画面见[本轮验证](docs/03-test/机炮与圆环HUD验证.md)。开始界面和地图维护分别见[菜单说明](docs/04-manuals/开始界面.md)、[地图说明](docs/04-manuals/河谷农田地图.md)。

旧撞击 AI 代码和回归测试保留，历史测试通过 `LegacyRamFixture` 显式选择旧机制。旧交接、成长与冲撞文档作为历史基线，不代表当前机炮玩法。

## 仓库结构

    src/client/DogfightThreatAI   Unity 客户端
    src/server/GatewayServer      网关服务器（规划中）
    src/server/LogicServer        逻辑服务器（规划中）
    src/shared/Protocol           前后端共享协议（规划中）
    docs                          项目文档
    art-source                    原始美术素材
    tools                         不依赖 Unity 的 Core 测试与历史 AI 演示

## 独立测试

需要 .NET 8 SDK。在仓库根目录执行：

```powershell
dotnet test tools/DogfightThreatAI.Core.Tests/DogfightThreatAI.Core.Tests.csproj
```

输入只产生动作意图，玩家与敌机推进模型管理资源和计时，`FlightMotionCommand` 明确区分普通惯性、有限加速度和定速覆盖，由 `FlightModel` 统一写入实际飞行状态。AI 读取只读世界快照与带延迟的可见推进线索；机炮模拟按飞行快照统一结算弹丸、冷却与血量，视图只显示结果。实现边界见 [AI 原型说明](docs/02-design/AI原型说明.md)。

`tools/DogfightThreatAI.AiDemo` 暂时仍运行旧 `AttackRunInputSource` 的控制台演示，其四级规则不等于当前 Battle，也不能代替场景试玩。命令与区别见 [tools 说明](tools/README.md)。

## 变更记录

- 2026-10-25：加入双方机炮、五段圆环 HUD、暂停/难度/退出菜单与 BFS、A*、行为树基础框架；本机团结 449 项 EditMode、68 项 PlayMode 通过。

- 2026-10-25：接入上游开始界面、河谷农田地图及双向场景导航；现行场景路径改为 StartMenu / GameScene，保留四级 AI、玩家推进与真实接触战斗。
- 2026-10-24：补充AI原型交接、固定Unity版本与可携带验证入口。
