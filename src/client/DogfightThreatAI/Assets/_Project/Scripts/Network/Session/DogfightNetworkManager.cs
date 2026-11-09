using System.Collections.Generic;
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

    private bool matchStarting;
    // 连接只在入场时映射到席位。对局规则不保存 Mirror 连接。
    private readonly List<int> battleConnections = new List<int>();
    private NetworkBattleMatch activeBattle;
    private NetworkConnectionToClient roomOwner;
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
        roomOwner = null;
    }

    // 直连模式将 Host 设为房主。将来可在认证成功后分配同一角色。
    public override void OnServerConnect(NetworkConnectionToClient connection)
    {
        if (matchStarting || InBattle)
        {
            connection.Disconnect();
            return;
        }
        if (connection == NetworkServer.localConnection)
        {
            roomOwner = connection;
        }
        base.OnServerConnect(connection);
    }

    public bool CanStartMatch() => CanStartMatch(NetworkServer.localConnection);

    private bool CanStartMatch(NetworkConnectionToClient requester)
    {
        if (!NetworkServer.active || requester == null || requester != roomOwner ||
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

    public void StartMatch() => BeginBattle(battleSceneName);
    public void StartCoop() => BeginBattle(coopSceneName);

    private void BeginBattle(string scene)
    {
        // 点击时再次检查，防止期间有人取消准备或离开。
        if (!CanStartMatch())
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

        // Mirror 通知所有客户端加载同一个场景。
        ServerChangeScene(scene);
    }

    public void RegisterBattle(NetworkBattleMatch battle)
    {
        activeBattle = battle;
    }

    public bool IsBattleSeatConnected(int seat) =>
        NetworkServer.connections.ContainsKey(battleConnections[seat - 1]);

    public override void OnServerDisconnect(NetworkConnectionToClient connection)
    {
        if (activeBattle != null)
        {
            activeBattle.Forfeit(battleConnections.IndexOf(connection.connectionId) + 1);
        }
        if (connection == roomOwner)
        {
            roomOwner = null;
        }
        base.OnServerDisconnect(connection);
    }

    public override void OnServerSceneChanged(string sceneName)
    {
        if (SceneManager.GetActiveScene().name == SceneNavigation.LobbyScene)
        {
            activeBattle = null;
            battleConnections.Clear();
            matchStarting = false;
        }
        base.OnServerSceneChanged(sceneName);
    }

    public void ReturnToLobby(NetworkBattleMatch battle)
    {
        if (!NetworkServer.active || NetworkServer.localConnection == null ||
            NetworkServer.localConnection != roomOwner || NetworkServer.isLoadingScene ||
            battle == null || battle != activeBattle || battle.Phase != NetworkRoundPhase.Finished)
        {
            return;
        }
        ServerChangeScene(SceneNavigation.LobbyScenePath);
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
    }
}
