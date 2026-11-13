using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DogfightThreatAI.UI.Navigation;
using kcp2k;
using Mirror;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>两个真实 UDP 端点连接 server-only Mirror，验证没有 Host 本地连接的控制流程。</summary>
public sealed class DedicatedSessionFlowTests
{
    private readonly List<RemotePeer> peers = new List<RemotePeer>();
    private DogfightNetworkManager manager;

    [UnityTest]
    public IEnumerator RemoteOwnerStartsCoopTransfersOwnershipAndReturnsToLobby()
    {
        yield return SceneManager.LoadSceneAsync(SceneNavigation.LobbyScenePath);
        manager = UnityEngine.Object.FindObjectOfType<DogfightNetworkManager>();
        Assert.That(manager, Is.Not.Null);
        // 使用临时端口，避免占用用户的 7777 服务。
        var transport = (KcpTransport)manager.transport;
        using (var socket = new System.Net.Sockets.UdpClient(0))
        {
            transport.Port = (ushort)((System.Net.IPEndPoint)socket.Client.LocalEndPoint).Port;
        }
        manager.StartServer();
        Assert.That(NetworkServer.localConnection, Is.Null);
        Assert.That(NetworkClient.active, Is.False);

        var owner = Connect(transport.Port);
        yield return WaitFor(() => owner.State.PlayerCount == 1);
        Assert.That(owner.State.IsRoomOwner, Is.True);
        var member = Connect(transport.Port);
        yield return WaitFor(() => member.State.PlayerCount == 2 && owner.State.PlayerCount == 2);
        Assert.That(member.State.IsRoomOwner, Is.False);

        foreach (var connection in NetworkServer.connections.Values)
        {
            var player = connection.identity.GetComponent<LobbyNetworkPlayer>();
            manager.SetLobbyReady(connection, player, true);
        }
        yield return WaitFor(() => owner.State.CanStartMatch);
        member.Send(new SessionRequestMessage { Action = SessionAction.StartCoop });
        yield return Pump(.4f);
        Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(SceneNavigation.LobbyScene));

        owner.Send(new SessionRequestMessage { Action = SessionAction.StartCoop });
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "NetworkPatrol" &&
            UnityEngine.Object.FindObjectOfType<NetworkBattleMatch>() != null &&
            UnityEngine.Object.FindObjectOfType<NetworkBattleMatch>().Phase == NetworkRoundPhase.Fighting);
        Assert.That(NetworkServer.localConnection, Is.Null);
        Assert.That(NetworkServer.spawned.Count, Is.GreaterThanOrEqualTo(3));

        owner.Disconnect();
        yield return WaitFor(() => member.State.IsRoomOwner && member.State.PlayerCount == 1);
        var battle = UnityEngine.Object.FindObjectOfType<NetworkBattleMatch>();
        // 结束战斗的测试前置条件，不通过客户端修改血量或胜负。
        typeof(NetworkBattleMatch).GetMethod("FinishRound", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(battle, null);
        member.Send(new SessionRequestMessage { Action = SessionAction.ReturnToLobby });
        yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneNavigation.LobbyScene &&
            !NetworkServer.isLoadingScene && NetworkServer.connections.Count == 1 &&
            member.State.ReadyCount == 0 && !member.State.CanStartMatch);
        Assert.That(member.State.IsRoomOwner, Is.True);
        member.Disconnect();
        yield return WaitFor(() => NetworkServer.connections.Count == 0);
        Assert.That(NetworkServer.active, Is.True);

        var replacement = Connect(transport.Port);
        yield return WaitFor(() => replacement.State.IsRoomOwner && replacement.State.PlayerCount == 1);
        Assert.That(replacement.State.ReadyCount, Is.Zero);

        // 新的一批成员还能开自由对战，所有成员在战斗内离开后服务继续等待。
        var teammate = Connect(transport.Port);
        yield return WaitFor(() => replacement.State.PlayerCount == 2);
        foreach (var connection in NetworkServer.connections.Values)
        {
            manager.SetLobbyReady(connection, connection.identity.GetComponent<LobbyNetworkPlayer>(), true);
        }
        yield return WaitFor(() => replacement.State.CanStartMatch);
        replacement.Send(new SessionRequestMessage { Action = SessionAction.StartVersus });
        yield return WaitFor(() => SceneManager.GetActiveScene().name == "NetworkBattle" &&
            UnityEngine.Object.FindObjectOfType<NetworkBattleMatch>() != null &&
            UnityEngine.Object.FindObjectOfType<NetworkBattleMatch>().Phase == NetworkRoundPhase.Fighting);
        replacement.Disconnect();
        teammate.Disconnect();
        yield return WaitFor(() => SceneManager.GetActiveScene().name == SceneNavigation.LobbyScene &&
            !NetworkServer.isLoadingScene && NetworkServer.connections.Count == 0);
        Assert.That(NetworkServer.active, Is.True);
    }

    private RemotePeer Connect(ushort port)
    {
        var peer = new RemotePeer(port);
        peers.Add(peer);
        return peer;
    }

    private IEnumerator WaitFor(Func<bool> condition)
    {
        float deadline = Time.realtimeSinceStartup + 20f;
        while (!condition())
        {
            Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "UDP 联机流程超时");
            TickPeers();
            yield return null;
        }
    }

    private IEnumerator Pump(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until)
        {
            TickPeers();
            yield return null;
        }
    }

    private void TickPeers()
    {
        foreach (var peer in peers)
        {
            peer.Tick();
        }
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        foreach (var peer in peers)
        {
            peer.Disconnect();
        }
        peers.Clear();
        if (manager != null && NetworkServer.active)
        {
            manager.StopServer();
        }
        yield return null;
    }

    private sealed class RemotePeer
    {
        private readonly KcpClient client;
        private readonly Unbatcher incoming = new Unbatcher();
        private bool spawnPending;
        private bool stopped;
        private string joinedScene;
        public SessionStateMessage State { get; private set; }

        public RemotePeer(ushort port)
        {
            client = new KcpClient(() => spawnPending = true, Receive, () => { },
                (code, message) => Assert.Fail($"KCP {code}: {message}"), new KcpConfig());
            client.Connect("127.0.0.1", port);
        }

        public void Send<T>(T message) where T : struct, NetworkMessage
        {
            using (var writer = NetworkWriterPool.Get())
            using (var batch = NetworkWriterPool.Get())
            {
                NetworkMessages.Pack(message, writer);
                var batcher = new Batcher(1200);
                batcher.AddMessage(writer.ToArraySegment(), NetworkTime.time);
                while (batcher.GetBatch(batch))
                {
                    client.Send(batch.ToArraySegment(), KcpChannel.Reliable);
                    batch.Reset();
                }
            }
        }

        private void Receive(ArraySegment<byte> data, KcpChannel channel)
        {
            Assert.That(incoming.AddBatch(data), Is.True);
            while (incoming.GetNextMessage(out var message, out _))
            {
                using (var reader = NetworkReaderPool.Get(message))
                {
                    Assert.That(NetworkMessages.UnpackId(reader, out ushort id), Is.True);
                    if (id == NetworkMessages.GetId<SessionStateMessage>())
                    {
                        State = reader.Read<SessionStateMessage>();
                    }
                    else if (id == NetworkMessages.GetId<SceneMessage>())
                    {
                        var scene = reader.Read<SceneMessage>();
                        // 首次连接也会收到当前场景通知，不能重复 AddPlayer。
                        string target = System.IO.Path.GetFileNameWithoutExtension(scene.sceneName);
                        if (target != joinedScene)
                        {
                            spawnPending = true;
                        }
                    }
                }
            }
        }

        public void Tick()
        {
            if (stopped)
            {
                return;
            }
            client.TickIncoming();
            if (spawnPending && client.connected && !NetworkServer.isLoadingScene)
            {
                spawnPending = false;
                joinedScene = SceneManager.GetActiveScene().name;
                Send(new ReadyMessage());
                Send(new AddPlayerMessage());
            }
            client.TickOutgoing();
        }

        public void Disconnect()
        {
            if (!stopped)
            {
                client.Disconnect();
                incoming.Clear();
                stopped = true;
            }
        }
    }
}
