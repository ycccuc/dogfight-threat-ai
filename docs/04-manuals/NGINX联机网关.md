# NGINX 联机网关

网关通过 Docker Desktop 的 Linux 引擎运行。默认只开放本机入口 `127.0.0.1:7777/UDP`，将客户端数据转发到 Windows 宿主机上的 Mirror 独立服 `7778/UDP`。游戏规则和房间权限由独立服处理。

## 启动

1. 在 Unity 打开 `LobbyScene`，选中 `LobbyNetworkRoot`。将独立服的 **Kcp Transport → Port** 临时设为 `7778`，勾选 **Dogfight Network Manager → Server Only In Editor**。点击 **Tools → Dogfight Threat AI → 本次从当前场景运行**，再按 Play。此时编辑器运行独立服，连接面板会隐藏。复测结束后退出 Play，取消勾选 **Server Only In Editor**，把端口还原为 `7777`，再保存场景。启动方式见 [Mirror 独立服与房间权限](Mirror独立服与房间权限.md)。
2. 打开 Docker Desktop。在项目根目录的 PowerShell 中运行以下命令。

   ```powershell
   docker compose -f src/server/GatewayServer/compose.yaml up -d
   docker compose -f src/server/GatewayServer/compose.yaml ps
   ```

3. 打开两个与服务端版本一致的普通客户端，保留客户端端口 `7777`，输入 `127.0.0.1` 并选择「连接服务器」。两人进入同一大厅后，准备、开始合作清剿、结算并返回大厅。

平时直接在编辑器使用「本机建房（测试）」时，先停止 NGINX 容器，释放本机的 `7777/UDP`。

在局域网联测时，可先将当前 PowerShell 的 `$env:GATEWAY_BIND_ADDRESS` 设为网关电脑的局域网 IPv4 地址，再执行 `up -d`。另一台电脑连接同一个地址，入口端口仍为 `7777/UDP`。若跨机连不上，检查 Windows 防火墙的入站范围是否允许该网段访问网关端口。

## 验证与停止

保持独立服运行，用 `stop` 暂停入口，确认新的客户端连接失败。再用 `up -d` 恢复网关，确认新的连接成功。日志会在 UDP 会话结束时记录来源、后端和收发字节数。

```powershell
docker compose -f src/server/GatewayServer/compose.yaml logs --tail 30
docker compose -f src/server/GatewayServer/compose.yaml stop
```

如果拉取镜像时 Docker Hub 返回 `EOF`，可试用 Docker Official Images 在 AWS 公共仓库的分发地址，在本机拉取后打上配置所用的标签。

```powershell
docker pull public.ecr.aws/docker/library/nginx:stable-alpine
docker tag public.ecr.aws/docker/library/nginx:stable-alpine nginx:stable-alpine
```

## 验证记录

2026-11-25 在 Windows、Docker Desktop Linux 引擎和 NGINX 1.30.8 上验证了配置语法和本机双向 UDP 转发。两个独立 UDP 客户端各发送三次请求，各自收到了对应回包。关闭网关后，监听中的后端没有收到新请求。重新启动网关后，两路转发再次通过。后端在 Docker 网络内按 IPv4 解析，避开测试中遇到的宿主机 IPv6 路由不可达。

11 月 25 日的双向转发测试使用 UDP 回包程序作为后端。2026-11-26 手动复测改用 Unity 独立战斗服和一个编辑器客户端。经网关连接成功，保持战斗服运行并停止网关后，新连接失败，恢复网关后重新连接成功。结果来自本机手动测试反馈。

2026-11-26 重启电脑后复测双客户端。网关启动后监听 `7777/UDP`，但当时 `7778/UDP` 没有独立服监听，两路连接超时。改用 Unity 编辑器以独立服模式监听 `7778/UDP`，两个普通客户端通过 `127.0.0.1:7777/UDP` 联机，手动测试反馈正常。

同机双客户端完成一局合作清剿。结算后房主点击「带队回联机大厅」，两名客户端返回同一房间。结果来自本机手动测试反馈。

跨电脑访问和正式部署下的后端入口隔离仍待验证。

[NGINX stream 转发说明](https://nginx.org/en/docs/stream/ngx_stream_proxy_module.html) · [Docker Desktop 主机网络说明](https://docs.docker.com/desktop/features/networking/) · [Docker 官方 NGINX 镜像](https://hub.docker.com/_/nginx)
