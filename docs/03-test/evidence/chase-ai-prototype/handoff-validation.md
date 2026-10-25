# Chase AI 原型交付复跑

2026-10-24。本记录是整理分支、提交及创建PR前的**新复跑**，使用当前单次1伤版本；没有沿用之前423/423/55的结果。测试期间生产源码、场景、资产及项目配置保持不变。

| 集合 | 通过/总数 | 失败/跳过 | 测试历时 | 本轮报告 |
|---|---:|---:|---:|---|
| .NET Core | 423/423 | 0/0 | 24.56秒 | [完整TRX XML](core-handoff.trx.xml) |
| Unity EditMode | 423/423 | 0/0 | 36.37秒 | [NUnit XML](unity-edit-handoff.xml) |
| Unity PlayMode | 55/55 | 0/0 | 30.36秒 | [NUnit XML](unity-play-handoff.xml) |

Core时间取TRX的start/finish，Unity时间取NUnit套件duration；不含独立工程首次导入和缓存复制。Core/EditMode是同一逻辑集在两个宿主执行，不能相加作为846个独立用例。工具版本为.NET SDK 8.0.422、Unity 2022.3.62f3c1。

## 输入与隔离

复制当前工程的Assets、Packages、ProjectSettings到独立工程，使用自己的Library。仅复制已有PackageCache依赖缓存，没有链接或共享活跃Library的写入；现有Battle GUI未关闭、未控制。EditMode与PlayMode串行运行，两次测试批处理均正常退出。

启动前和全部测试结束后，逐文件核对原工作区与隔离工程：**598个输入全部一致，包含92个C#文件，其中49个为生产脚本**。完整相对路径及SHA256见[input-hashes.json](input-hashes.json)，清单文件本身的SHA256、规则与计数见[summary.json](summary.json)。这份清单包含当前工作区中的新增文件，交付时也必须将对应源码、meta、场景、配置及tools工程纳入版本控制；不依赖未提交的测试副本。

清单中的`sha256`对应本轮实际测试时的原始文件字节；`sha256LfNormalized`对应Git规范化后的文件字节，文本仅将CRLF转为LF，Git属性声明二进制或包含NUL的文件保持原样。已从提交`040b5fec5780bdbed753a705cf15165e5a185c1f`的单次`git archive`逐项核对全部598个Git blob，全部与规范化测试输入一致；其中16个文件只存在换行差异。接收方从Git检出后可对照后者，原始测试证据仍由前者保留。清单及本目录新文本显式使用UTF-8/LF；`inputManifestSha256`对清单落盘实际字节计算，不使用另一个隐含串行化格式。

验证规则包括各档真实接触均扣1HP、5HP经五次有效命中击毁、0.7秒受击保护、完整0.8秒预警、末0.3秒固定世界发射轨道及45冲撞速度。场景回归覆盖真实体积伤害接线、切级不重建战斗模型、生命/保护保留、重开、可见性、运动与呈现。方向/计时专用用例的碰撞隔离不作为近距安全证据；真实接触用例保留体积和正常伤害。

## 复现命令

从仓库根目录执行。`$evidence`是自选的新结果目录，`$isolatedProject`是复制后的独立Unity工程，`$UnityEditor`指向对应版本Unity.exe；这些变量避免绑定本机路径。

独立工程的准备方式如下，目标必须是新的目录。没有已有PackageCache时，由对应版本Unity根据随仓库的依赖清单正常恢复。

```powershell
$sourceProject = Join-Path (Get-Location) 'src/client/DogfightThreatAI'
$isolatedProject = Join-Path $evidence 'unity-isolated'
if (Test-Path -LiteralPath $isolatedProject) { throw '请选择新的隔离工程目录' }
New-Item -ItemType Directory -Path $isolatedProject -Force | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
  Copy-Item -LiteralPath (Join-Path $sourceProject $folder) -Destination $isolatedProject -Recurse
}
$cachedPackages = Join-Path $sourceProject 'Library/PackageCache'
if (Test-Path -LiteralPath $cachedPackages) {
  $cloneLibrary = Join-Path $isolatedProject 'Library'
  New-Item -ItemType Directory -Path $cloneLibrary -Force | Out-Null
  Copy-Item -LiteralPath $cachedPackages -Destination $cloneLibrary -Recurse
}
```

```powershell
$env:MSBuildEnableWorkloadResolver = 'false'
$env:ALLUSERSPROFILE = [Environment]::GetFolderPath('CommonApplicationData')
dotnet test tools/DogfightThreatAI.Core.Tests/DogfightThreatAI.Core.Tests.csproj `
  -p:NuGetAudit=false --logger 'trx;LogFileName=core-handoff.trx' `
  --results-directory $evidence --artifacts-path (Join-Path $evidence 'artifacts-core')
```

本轮Unity使用下列参数串行执行；不加`-quit`，由测试运行器结束进程。

```powershell
foreach ($platform in @('EditMode', 'PlayMode')) {
  $name = if ($platform -eq 'EditMode') { 'unity-edit-handoff' } else { 'unity-play-handoff' }
  $testArgs = @('-batchmode', '-nographics', '-projectPath', $isolatedProject,
    '-runTests', '-testPlatform', $platform,
    '-testResults', (Join-Path $evidence ($name + '.xml')),
    '-logFile', (Join-Path $evidence ($name + '.log')))
  Start-Process -FilePath $UnityEditor -ArgumentList $testArgs -WindowStyle Hidden -Wait
}
```

首次独立启动因宿主缺少标准ALLUSERSPROFILE环境变量，UPM在测试开始前解析失败。仅给测试子进程补齐系统CommonApplicationData路径后，重新启动并完整执行上述测试；未改生产源码或依赖清单。初次环境失败未生成测试结果，不计入代码用例失败。环境日志和含本机路径的完整日志保留在仓库外。

主交付流程另核对AiDemo与BalanceProbe两工具构建，均0警告、0错误；BalanceProbe先恢复项目依赖再构建。本轮没有因此重新运行完整压力矩阵。

```powershell
dotnet build tools/DogfightThreatAI.AiDemo/DogfightThreatAI.AiDemo.csproj --no-restore -p:NuGetAudit=false --nologo
dotnet restore tools/DogfightThreatAI.BalanceProbe/DogfightThreatAI.BalanceProbe.csproj --ignore-failed-sources -p:NuGetAudit=false --nologo
dotnet build tools/DogfightThreatAI.BalanceProbe/DogfightThreatAI.BalanceProbe.csproj --no-restore -p:NuGetAudit=false --nologo
```

## 证据边界

- 本轮复跑的是上述三套回归；[历史一伤384局压力与3456首击轨迹](../../AI全级强化验证.md)是独立实验，未在本轮重新生成。Core中的前三次真实接触节奏回归不等于完整5HP生存时间。
- 当前一伤版未达到所有级别、所有操作下统一五倍难度；历史数据中的删失、步长差异和缺失反制组合继续保留，不能由本次全绿回归泛化为整体平衡完成。
- 之前唯一原生键盘绕圈尝试因额外按键作废，没有有效的四档原生试玩成绩。本轮没有人工手打或系统键盘试玩，不能将自动化结果写成人类手感评估。
- PlayMode验证真实Unity接线与可计算表现；本次使用无图形设备批处理，不代替前台视觉审美检查、独立构建性能测试或所有墙角/输入抖动下的可躲保证。
- 报告仅清理机器路径、主机名和用户名，保留测试名称、结果、时长、计数与诊断；原始报告SHA256和脱敏副本SHA256均在summary中。`core-handoff.trx.xml`内容仍为TRX格式，只用附加`.xml`避开仓库的`*.trx`忽略规则。
