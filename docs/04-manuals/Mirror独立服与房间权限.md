# Mirror 独立服与房间权限

Mirror 独立服负责大厅、自由对战和合作清剿的会话管理。房主权限独立于 Host 本地连接，支持房主移交和空房复用。

## 运行规则

- 每个逻辑服进程承载一个大厅和一场对局，不支持一个进程中并发多个独立房间。
- 第一名成功生成大厅玩家的成员成为房主，房主离开后按加入顺序移交。
- 房主是本次会话内的权限，不是服务器管理员。连接编号不是账号，不提供跨重连身份恢复。
- Mirror 默认认证阶段通过不等于账号认证。当前未实现账号、凭证校验、自动匹配或 Nakama 集成。
- 所有人必须准备且达到最少人数，房主才能请求开局。服务器每次重新检查，客户端按钮只显示服务器下发的状态。
- 战斗中拒绝新成员。已在场成员在服务器切换场景后重新生成玩家。
- 结算后的返回大厅请求由房主发起。房主掉线后，新的房主拥有返回权限。
- 独立服所有成员离开后继续监听。空战斗场景会回到大厅，准备状态清空。
- Host 进程退出仍会终止其内嵌的服务器。暂不支持 Host 迁移。

## 修改内容

- 将成员、准备状态和房主规则拆入纯 C# RoomSession。
- 开局与返回大厅改为网络请求，由服务器检查权限。
- 大厅和结算界面读取服务器下发的房主状态。
- 增加独立服启动入口和 Network 程序集。

## 代码职责

| 位置 | 职责 |
|---|---|
| Core/Session/RoomSession | 成员、准备状态、房主顺序与开局基本规则，不引用 Unity 或 Mirror |
| Network/Session/DogfightNetworkManager | 连接生命周期、服务器权限复核、席位、场景切换 |
| Network/Session/SessionMessages | 低频房间请求与服务器下发的界面状态 |
| Network/Session/DedicatedServerBootstrap | 独立服进入大厅与启动入口 |
| Network/Lobby | 大厅输入、准备和按钮表现 |
| Network/Battle | 战斗输入与状态同步，复用现有 Core 模拟 |

Network 已单独使用 DogfightThreatAI.Network 程序集，依赖 Core、Gameplay、UI 和 Mirror。依赖不反向进入 Core。

## 通信流程

SessionRequestMessage 只携带操作类型，发送者身份由 Mirror 连接确定。服务器检查认证阶段、场景加载、玩家对象和房主权限，并限制请求频率。

开局还会检查大厅场景、切场景状态、人数、每个连接的准备状态以及战斗配置。返回大厅仅在活动对局已经结束时允许。未知操作不会触发场景变化。

服务器逐连接下发 SessionStateMessage，包括该接收者的房主标记、能否开局、人数与准备数。房主移交在战斗与结算阶段也更新 UI。大厅准备继续通过玩家的有权限 Command 发起，再由会话管理器验证。

飞行输入、NetworkTransform 和战斗插值沿用既有实现。

## 启动与验证

### 在编辑器测试 server-only

打开 LobbyScene，选择 LobbyNetworkRoot，在 DogfightNetworkManager 中勾选 Server Only In Editor，然后 Play。此时调用 StartServer，不调用 StartHost，没有本地玩家飞机。取消勾选即可恢复普通交互启动。

普通客户端点击“连接服务器”。“本机建房（测试）”保留原 Host 启动方式，只用于开发调试。独立服的房主由进入顺序决定，不需要在客户端启动服务器。

客户端和服务端必须使用同一协议版本。旧客户端包缺少新增消息的序列化与处理器，需要重新构建。

### 构建后启动

Unity Dedicated Server 构建会根据 UNITY_SERVER 自动加载大厅并调用 StartServer。普通桌面构建也可以显式使用以下参数作为服务端运行：

```text
DogfightThreatAI.exe -batchmode -nographics -dogfightServer -logFile D:\GameBuilds\Unity\dogfight-threat-ai\server.log
```

端口沿用 KCP Transport 配置，默认 7777/UDP。仅指定 -batchmode 不会自动开服，需使用 -dogfightServer 或 Dedicated Server 构建。

### 手动验收

1. 独立服启动，两个新版普通客户端连接，服务器没有 localConnection。
2. 两人准备，队员无法开局，房主可分别发起自由对战与合作清剿。
3. 战斗加载、飞行、射击、伤害和结果由服务器推进。
4. 结算后房主可带队返回，重新进入大厅后每个人都未准备。
5. 原房主在大厅、战斗或结算阶段离开，剩余玩家的权限正常更新。
6. 最后一名玩家在战斗中离开，服务器回大厅并继续监听。再进入的玩家成为新房主。

## 后续接入 NGINX

NGINX stream 可将 UDP 流量转发到固定逻辑服。客户端地址改为网关入口，转发到战斗服的 KCP UDP 端口。需要验证双向数据、UDP 会话超时及源端点映射。NGINX stream 转发不自动提供账号认证、房间分配或对局恢复。

同一房间的连接需转发到同一战斗服。多进程房间需要维护房间与后端实例的映射，网关链路待实测。

参考文档：

- https://mirror-networking.gitbook.io/docs/manual/components/network-manager
- https://mirror-networking.gitbook.io/docs/manual/guides/communications/remote-actions
- https://mirror-networking.gitbook.io/docs/manual/guides/communications/networkmanager-callbacks
- https://docs.nginx.com/nginx/admin-guide/load-balancer/tcp-udp-load-balancer/
