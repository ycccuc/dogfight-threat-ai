# Mirror 独立服与房间权限验证

日期：2026-11-13。引擎：Unity 2022.3.62f3c1。验证范围为独立服会话与房间权限，不含独立构建和网关链路。

## 已执行

| 检查 | 结果 |
|---|---|
| Unity 编译与 Mirror 消息代码生成 | 通过 |
| Unity EditMode，DogfightThreatAI.Tests.EditMode | 477 通过，0 失败 |
| Unity PlayMode，DedicatedSessionFlowTests | 1 通过，0 失败 |
| git diff --check | 通过 |

EditMode 包含新增的 5 项 RoomSession 测试：远程编号成员开局、非房主拒绝、按加入顺序移交、准备门槛与取消准备、返回清空准备、空房重新接入等规则。

PlayMode 测试加载真实 LobbyScene，调用 StartServer，使用两个独立 KcpClient UDP 端点与真实 KcpTransport 通信。测试明确断言 NetworkServer.localConnection 为空、NetworkClient 未启动。通过 Mirror 实际消息序列化、批处理与服务端处理器验证：

1. 首名远程成员取得房主，第二名收到队员状态。
2. 服务器设置两名成员准备，房主收到可开局状态。
3. 队员直接发送开局控制消息仍无法切场景。
4. 房主发送合作清剿开局，服务器进入 NetworkPatrol 并推进到 Fighting。
5. 原房主断开后，另一成员取得房主权限。
6. 服务器在测试中结束对局，新房主的返回请求实际切回大厅，准备状态清空。
7. 全员退出后服务器仍监听，新成员可重新进入。
8. 新一批成员可开始自由对战，战斗中全员退出后服务器自动返回大厅。

## 测试范围

UDP 集成测试使用两个协议端点。准备状态在服务器设置，结算通过服务器结束方法触发；未模拟完整客户端渲染或全部敌机击毁过程。覆盖 server-only 会话权限、网络请求、房主移交和场景流程。

待验证：双机完整客户端 UI、公网、延迟与丢包、插值表现、Dedicated Server 构建和 NGINX 转发。飞行、伤害和决策逻辑的 EditMode 回归已通过。

`dotnet test` 因 Windows 应用控制策略无法加载测试程序集。上述 EditMode 结果来自 Unity Test Runner。

## 本机证据与复跑

本机 XML 与日志位于 tools/.network-validation，已加入 gitignore，不作为源码提交。测试异常退出时，先确认前一个后台测试进程已经结束，再运行下一项，避免同一工程被两个 Unity 占用。

Unity Test Runner 中选择 EditMode 的 RoomSessionTests 或 PlayMode 的 DedicatedSessionFlowTests 可复跑。运行完整 EditMode 时可筛选 DogfightThreatAI.Tests.EditMode。

运行方式、权限和当前单房间边界见 [独立服与房间权限](../04-manuals/Mirror独立服与房间权限.md)。
