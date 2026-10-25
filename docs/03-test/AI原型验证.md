# AI 原型验证记录

此文件保留初始追击版本和首轮 Battle 接线证据；当前提前减速与保持距离版本见 [AI 间距验证](AI间距验证.md)。

日期：2026-10-24（Asia/Shanghai）。基于远程 `main` 提交 `7e29a12`，本地分支 `feat/chase-ai-prototype`。

## 结果

| 验证入口 | 结果 |
|---|---|
| .NET 8 / NUnit，同一套 EditMode 源文件 | 58 通过，0 失败，0 跳过 |
| Unity 2022.3.62f3c1 / EditMode | 58 通过，0 失败，0 跳过 |
| Core 独立编译为 netstandard2.1 | 成功，无 Unity 引用，警告视为错误 |
| 控制台追击演示 | 两个场景均通过，退出码 0 |
| 新增 Unity 资产元数据 | 全部新 .cs 和 Core/AI 目录具备 .meta |
| 包清单 | manifest.json / packages-lock.json 未变 |

测试细分：AI 42 项、原有 FlightModel 9 项、原有 Grid2D 7 项。AI 测试中的参数化用例分别计数；覆盖输入有限且在范围内、玩家筛选、稳定平局、最短转向、到达刹车、目标移动/移除、世界不被修改、非法参数/数据及精确重放。

## 测试先行证据

先新增测试，缺少 AI/快照类型时编译失败。补充契约及仅返回 Neutral 的最小骨架后，58 项中 34 项失败、24 项通过：追击、目标筛选、刹车、转向及参数验证断言能检出未实现行为。完成算法后，同一套测试全部通过。

## 真实飞行演示

20 ms 固定步长，每个场景 600 步，再完整重放 600 步。生产 AI 只返回 FlightInput，演示宿主调用生产 FlightModel.Step，未直接移动飞机。

| 场景 | 初始距离 | 最近距离 | 12 秒末距离 | 重放 |
|---|---:|---:|---:|---|
| 正前方玩家 (30,0) | 30.000 | 0.060 | 2.805 | 600/600 步输入、位置、速度、朝向完全一致 |
| 上方玩家 (0,30) | 30.000 | 0.070 | 2.360 | 600/600 步完全一致 |

最近距离而非最终停车距离是此追击原型的验收指标。飞行惯性会导致到达后越过目标、重新追击；当前不是提前减速的停车控制器，也没有武器、威胁决策或寻路。威胁网格本阶段交付字段与更新频率草案。

## 复跑命令

仓库根目录，需要 .NET 8 SDK：

```powershell
dotnet test tools/DogfightThreatAI.Core.Tests/DogfightThreatAI.Core.Tests.csproj --logger "trx;LogFileName=core-tests.trx" --results-directory TestResults
dotnet run --project tools/DogfightThreatAI.AiDemo/DogfightThreatAI.AiDemo.csproj
```

Unity 命令（替换编辑器路径，工程路径从仓库根解析）：

```powershell
$project = (Resolve-Path 'src/client/DogfightThreatAI').Path
$results = Join-Path (Get-Location).Path 'TestResults'
New-Item -ItemType Directory -Path $results -Force | Out-Null
& 'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Unity.exe' -batchmode -nographics -projectPath $project -runTests -testPlatform EditMode -testResults "$results/unity-editmode.xml" -logFile "$results/unity-editmode.log"
```

不要加 `-quit`。执行时不要在另一个编辑器实例中打开同一工程。

本次自动化环境缺少 Windows 的 ALLUSERSPROFILE，初次 UPM 解析报 `path argument ... undefined`。仅为测试进程设置 `$env:ALLUSERSPROFILE='C:\ProgramData'` 后正常；.NET 入口同时设置 `$env:MSBuildEnableWorkloadResolver='false'`。未修改系统环境或 Unity 包配置，普通桌面终端通常无需此补全。

## 本机原始证据

输出位于 `E:\codex-temp\dogfight-ai-validation\`：

- `chase-ai-red.trx`：最小骨架时的 34 项失败。
- `chase-ai-green.trx`：.NET 58 项通过。
- `unity-editmode.xml`：Unity 58 项通过，测试执行时间约 0.097 秒（不含导入/编译）。
- `unity-editmode.log`：正式 Unity 执行日志，无 C# 编译错误/警告。

独立审查未发现首期范围内的阻断缺陷。共享世界契约仍应在团队接入 BattleSimulation 时由岗位 A 评审。

## Battle 场景接入验证

同日将 Unity 输入适配器与红色敌机 prefab 接入实际 Battle：新增 5 项 PlayMode 测试全部通过；完整 EditMode 回归仍为 58/58。覆盖保存后的玩家引用、开局可见与颜色区别、真实 FixedUpdate 接近静止玩家、玩家移动转弯后的持续追击、目标禁用/销毁后刹车，以及首次插值起点。接线前的同组测试因 AI 敌机数量为 0 而全部失败。

结果为 `battle-playmode.xml`、`battle-editmode.xml`，日志为同名 `.log`，均在上述本机证据目录。新增资产 meta 齐全；Test Runner 临时设置的 `runInBackground` 已恢复为项目原值。最后在可见 Unity 游戏视图中进入 Play 并确认两架飞机均已渲染。操作和接线详见 [Battle 追击演示](../04-manuals/Battle追击演示.md)。
