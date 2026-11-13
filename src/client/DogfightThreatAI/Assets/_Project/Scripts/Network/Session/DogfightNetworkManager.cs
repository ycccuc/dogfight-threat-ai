using System.Collections.Generic;
using DogfightThreatAI.Core.Session;
using DogfightThreatAI.UI.Navigation;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class DogfightNetworkManager : NetworkManager
{
    [Header("对局配置")]
    [SerializeField] private GameObject battlePlayerPrefab;
    [SerializeField] private string battleSceneName = "NetworkBattle";
    [SerializeField] private string coopSceneName = "NetworkPatrol";
    [SerializeField, Min(2)] private int minimumPlayers = 2;
    [Header("独立服务器调试")]
    [SerializeField] private bool serverOnlyInEditor;

    private bool matchStarting;
    // 连接只在入场时映射到席位。对局规则不保存 Mirror 连接。
    private readonly List<int> battleConnections = new List<int>();
    private NetworkBattleMatch activeBattle;
    private readonly RoomSession room = new RoomSession();
    private readonly Dictionary<int, double> nextRequestTimes = new Dictionary<int, double>();
    private bool serverStopping;
    public SessionStateMessage ClientSession { get; private set; }
    public bool IsLocalRoomOwner => NetworkClient.isConnected && ClientSession.IsRoomOwner;
    public int BattleSeatCount => battleConnections.Count;
    private bool InBattle => SceneManager.GetActiveScene().name == battleSceneName ||
        SceneManager.GetActiveScene().name == coopSceneName;

    public override void Awake()
    {
        // 返回大厅时保留跨场景的管理器，销毁大厅场景带来的副本。
        if (singleton != null && singleton != this)
        {
            Destroy(gameObject);
            return;
        }
        base.Awake();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        matchStarting = false;
        activeBattle = null;
        battleConnections.Clear();
        room.Clear();
        nextRequestTimes.Clear();
        serverStopping = false;
        NetworkServer.RegisterHandler<SessionRequestMessage>(OnSessionRequest, true);
    }

    public override void Start()
    {
        if (DedicatedServerBootstrap.Requested || (Application.isEditor && serverOnlyInEditor))
        {
            if (!NetworkServer.active && !NetworkClient.active)
            {
                StartServer();
            }
            return;
        }
        base.Start();
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        ClientSession = default;
        NetworkClient.RegisterHandler<SessionStateMessage>(OnSessionState, true);
    }

    public override void OnStopClient()
    {
        ClientSession = default;
        NetworkClient.UnregisterHandler<SessionStateMessage>();
        base.OnStopClient();
    }

    public override void OnStopServer()
    {
        serverStopping = true;
        room.Clear();
        battleConnections.Clear();
        nextRequestTimes.Clear();
        NetworkServer.UnregisterHandler<SessionRequestMessage>();
        base.OnStopServer();
    }

    public override void OnStopHost()
    {
        serverStopping = true;
        base.OnStopHost();
    }

    private void OnSessionState(SessionStateMessage state)
    {
        ClientSession = state;
    }

    public override void OnServerConnect(NetworkConnectionToClient connection)
    {
        if (matchStarting || InBattle)
        {
            connection.Disconnect();
            return;
        }
        base.OnServerConnect(connection);
        PublishSession();
    }

    public override void OnServerReady(NetworkConnectionToClient connection)
    {
        base.OnServerReady(connection);
        PublishSession();
    }

    public bool CanStartMatch()
    {
        return NetworkClient.isConnected && ClientSession.CanStartMatch;
    }

    private bool CanStartMatch(NetworkConnectionToClient requester)
    {
        if (!NetworkServer.active || requester == null || !requester.isAuthenticated ||
            !room.CanStart(requester.connectionId, minimumPlayers) ||
            matchStarting || NetworkServer.isLoadingScene ||
            SceneManager.GetActiveScene().name != SceneNavigation.LobbyScene)
        {
            return false;
        }

        int count = 0;

        // 使用服务器连接记录，不能相信客户端 UI 显示的人数。
        foreach (NetworkConnectionToClient connection
                 in NetworkServer.connections.Values)
        {
            if (connection == null ||
                !connection.isAuthenticated ||
                !connection.isReady ||
                connection.identity == null)
            {
                return false;
            }

            LobbyNetworkPlayer player =
                connection.identity.GetComponent<LobbyNetworkPlayer>();

            if (player == null || !player.ReadyToStart)
            {
                return false;
            }

            count++;
        }

        return count >= minimumPlayers;
    }

    public void StartMatch()
    {
        RequestSessionAction(SessionAction.StartVersus);
    }

    public void StartCoop()
    {
        RequestSessionAction(SessionAction.StartCoop);
    }

    // Host 和远程客户端走相同的消息路径，权限始终在服务器重新检查。
    private void RequestSessionAction(SessionAction action)
    {
        if (NetworkClient.isConnected)
        {
            NetworkClient.Send(new SessionRequestMessage { Action = action });
        }
    }

    private void OnSessionRequest(NetworkConnectionToClient sender, SessionRequestMessage message)
    {
        if (serverStopping || !sender.isAuthenticated || !sender.isReady ||
            sender.identity == null || !room.IsOwner(sender.connectionId))
        {
            return;
        }
        double now = NetworkTime.time;
        if (nextRequestTimes.TryGetValue(sender.connectionId, out double next) && now < next)
        {
            return;
        }
        nextRequestTimes[sender.connectionId] = now + .25;
        switch (message.Action)
        {
            case SessionAction.StartVersus:
                BeginBattle(sender, battleSceneName);
                break;
            case SessionAction.StartCoop:
                BeginBattle(sender, coopSceneName);
                break;
            case SessionAction.ReturnToLobby:
                if (!NetworkServer.isLoadingScene && activeBattle != null &&
                    activeBattle.Phase == NetworkRoundPhase.Finished)
                {
                    ServerChangeScene(SceneNavigation.LobbyScenePath);
                }
                break;
        }
    }

    private void BeginBattle(NetworkConnectionToClient requester, string scene)
    {
        // 点击时再次检查，防止期间有人取消准备或离开。
        if (!CanStartMatch(requester))
        {
            Debug.LogWarning("无法开始：需要房主发起，人数足够且所有玩家已准备。");
            return;
        }

        if (battlePlayerPrefab == null ||
            battlePlayerPrefab.GetComponent<NetworkIdentity>() == null ||
            battlePlayerPrefab.GetComponent<BattleNetworkPlayer>() == null)
        {
            Debug.LogError("请配置有效的 BattleNetworkPlayer 预制体。");
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogError($"请在 Build Settings 中加入并勾选 {scene}。");
            return;
        }

        battleConnections.Clear();
        foreach (var connection in NetworkServer.connections.Values)
        {
            battleConnections.Add(connection.connectionId);
        }
        battleConnections.Sort();
        matchStarting = true;
        PublishSession();

        // Mirror 通知所有客户端加载同一个场景。
        ServerChangeScene(scene);
    }

    public void RegisterBattle(NetworkBattleMatch battle)
    {
        activeBattle = battle;
    }

    public bool IsBattleSeatConnected(int seat)
    {
        return seat >= 1 && seat <= battleConnections.Count &&
            room.Contains(battleConnections[seat - 1]) &&
            NetworkServer.connections.ContainsKey(battleConnections[seat - 1]);
    }

    public override void OnServerDisconnect(NetworkConnectionToClient connection)
    {
        if (activeBattle != null)
        {
            activeBattle.Forfeit(battleConnections.IndexOf(connection.connectionId) + 1);
        }
        room.Leave(connection.connectionId);
        nextRequestTimes.Remove(connection.connectionId);
        base.OnServerDisconnect(connection);
        PublishSession();
        // 空房不关闭战斗服，回到大厅等待下一批玩家。
        if (!serverStopping && NetworkServer.active && room.Count == 0 &&
            InBattle && !NetworkServer.isLoadingScene)
        {
            ServerChangeScene(SceneNavigation.LobbyScenePath);
        }
    }

    public override void OnServerSceneChanged(string sceneName)
    {
        if (SceneManager.GetActiveScene().name == SceneNavigation.LobbyScene)
        {
            activeBattle = null;
            battleConnections.Clear();
            matchStarting = false;
            room.ResetReady();
        }
        base.OnServerSceneChanged(sceneName);
        PublishSession();
        // 也覆盖所有玩家在切场景期间离开的情况。
        if (!serverStopping && room.Count == 0 && InBattle)
        {
            ServerChangeScene(SceneNavigation.LobbyScenePath);
        }
    }

    public void ReturnToLobby(NetworkBattleMatch battle)
    {
        if (battle == null || battle.Phase != NetworkRoundPhase.Finished)
        {
            return;
        }
        RequestSessionAction(SessionAction.ReturnToLobby);
    }

    public void SetLobbyReady(NetworkConnectionToClient connection, LobbyNetworkPlayer player, bool ready)
    {
        if (!NetworkServer.active || matchStarting || NetworkServer.isLoadingScene || InBattle ||
            connection == null || !connection.isAuthenticated || !connection.isReady ||
            connection.identity == null || connection.identity.GetComponent<LobbyNetworkPlayer>() != player ||
            !room.SetReady(connection.connectionId, ready))
        {
            return;
        }
        player.SetReadyOnServer(ready);
        PublishSession();
    }

    private void PublishSession()
    {
        if (!NetworkServer.active || serverStopping)
        {
            return;
        }
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection == null || !connection.isAuthenticated || !room.Contains(connection.connectionId))
            {
                continue;
            }
            connection.Send(new SessionStateMessage
            {
                IsRoomOwner = room.IsOwner(connection.connectionId),
                CanStartMatch = CanStartMatch(connection),
                PlayerCount = room.Count,
                ReadyCount = room.ReadyCount
            });
        }
    }

    public void LeaveSession()
    {
        if (NetworkServer.active && NetworkClient.active)
        {
            StopHost();
        }
        else if (NetworkServer.active)
        {
            StopServer();
        }
        else if (NetworkClient.active)
        {
            StopClient();
        }
    }

    public override void OnServerAddPlayer(NetworkConnectionToClient connection)
    {
        if (connection.identity != null)
        {
            Debug.LogError("这个连接已经有玩家对象。");
            return;
        }

        bool inBattle = InBattle;

        int seat = inBattle ? battleConnections.IndexOf(connection.connectionId) + 1 : 0;
        if (inBattle && (seat == 0 || activeBattle == null))
        {
            Debug.LogError("无法生成战斗玩家：该连接没有席位，或战斗控制器未初始化。", this);
            connection.Disconnect();
            return;
        }

        GameObject prefab = inBattle ? battlePlayerPrefab : playerPrefab;

        if (prefab == null)
        {
            Debug.LogError("当前场景缺少玩家预制体配置。");
            return;
        }

        Transform spawn = GetStartPosition();

        if (spawn == null)
        {
            Debug.LogError("当前场景没有可用的 Network Start Position。");
            return;
        }

        GameObject player = Instantiate(
            prefab, spawn.position, spawn.rotation);

        player.name = $"{prefab.name} [connId={connection.connectionId}]";

        BattleNetworkPlayer battlePlayer = null;
        if (inBattle)
        {
            battlePlayer = player.GetComponent<BattleNetworkPlayer>();
            battlePlayer.AssignSeat(activeBattle, seat);
        }

        if (!NetworkServer.AddPlayerForConnection(connection, player))
        {
            Destroy(player);
            return;
        }
        if (battlePlayer != null)
        {
            activeBattle.RegisterPlayer(battlePlayer);
        }
        else
        {
            // 只让成功生成玩家的已认证连接成为成员。顺序同时决定房主移交。
            room.Join(connection.connectionId);
            room.SetReady(connection.connectionId, false);
        }
        PublishSession();
    }
}
