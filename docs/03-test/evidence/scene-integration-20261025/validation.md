# 场景改名与上游集成验证

本轮从已解决合并冲突的当前工作区创建全新隔离副本，依次执行 Core、Unity EditMode、Unity PlayMode 全量测试。结果均通过，未筛选或跳过用例。

| 测试集 | 通过 / 总数 | 失败 | 跳过 | 时长（秒） |
| --- | ---: | ---: | ---: | ---: |
| Core / .NET | 423 / 423 | 0 | 0 | 20.31 |
| Unity EditMode | 426 / 426 | 0 | 0 | 33.35 |
| Unity PlayMode | 61 / 61 | 0 | 0 | 45.89 |

Core 与 Unity EditMode 共享核心逻辑用例，不能将三行相加解释为互不重复的测试数。地图资产及流式绘制测试需要 Unity 宿主，仅在 EditMode 执行。

菜单集成测试通过真实按钮完成 `StartMenu → GameScene → StartMenu → GameScene`。首局先结算真实接触伤害至 4HP，再消耗玩家推进能量至 35；返回时卸载旧地图、双飞机与战斗实例，并恢复时间倍率。重进保留同一河谷农田地图资产及有效地块，每局仅一个战斗权威、一个敌机 AI 和两架飞机，玩家恢复 5HP、玩家与敌机能量恢复 60，旧受伤计数、保护时间和冲刺冷却均清零。EditMode 还验证四个实际相机边角均被有限且无碰撞的边缘地块覆盖。

环境为 .NET SDK 8.0.422 与 Unity 2022.3.62f3c1。Assets、Packages、ProjectSettings 与 Core 工程文件完整复制后冻结；Library 独立写入，仅复制旧 PackageCache 作为依赖缓存。未控制原工程 GUI。子进程补齐标准 ALLUSERSPROFILE 环境变量，未重定义用户主目录。原始测试结果与日志保留在临时验证目录，本目录 XML 已移除本机路径、用户名和主机名。

可携带测试结果见 [Core TRX](core.trx.xml)、[Unity EditMode](unity-edit.xml)、[Unity PlayMode](unity-play.xml)，机器可读计数、时长与文件 SHA256 见 [summary.json](summary.json)。[input-hashes.json](input-hashes.json) 对每个测试输入记录原始字节 `sha256`、Git LF 规范化字节 `sha256LfNormalized` 和 `binary`；文本仅转换 CRLF → LF，Git 二进制属性或含 NUL 的输入保持原样。所有 SHA256 均对实际落盘字节计算，清单自身哈希也由 summary 记录。

冻结输入已逐项同时核对工作区、隔离副本和已暂存源码快照。summary 的 `sourceSnapshot` 记录本机冻结 index tree 与三个源码子树 ID。index tree 不是要求接收方检出的提交；最终提交会增加本目录证据，接收方应以源码子树 ID 及清单中的规范化文件哈希核对收到的仓库。

另见独立的[静态场景审计](static-scene-audit.json)，覆盖场景本地引用、GUID/meta、Build Settings 与程序集定义。

复现时先按清单确认代码和资源，再在独立副本运行以下命令；`Unity` 代表同版本编辑器可执行文件，`validation` 代表自定结果目录：

```text
dotnet test tools/DogfightThreatAI.Core.Tests/DogfightThreatAI.Core.Tests.csproj --artifacts-path validation/artifacts-core --results-directory validation --logger "trx;LogFileName=core.trx"
Unity -batchmode -nographics -projectPath src/client/DogfightThreatAI -runTests -testPlatform EditMode -testResults validation/unity-edit.xml -logFile validation/unity-edit.log
Unity -batchmode -nographics -projectPath src/client/DogfightThreatAI -runTests -testPlatform PlayMode -testResults validation/unity-play.xml -logFile validation/unity-play.log
```

本轮不包含人工键盘试玩、历史平衡矩阵或反应轨迹重跑。自动化测试覆盖结论限于上述真实执行结果。
