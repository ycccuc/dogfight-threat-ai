using Mirror;
using UnityEngine;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.UI.Lobby;
using DogfightThreatAI.UI.Navigation;
using UnityEngine.SceneManagement;

public sealed class LobbyNetworkHud : MonoBehaviour
{
    [SerializeField] private LobbyHudView hud;
    [SerializeField] private Camera lobbyCamera;
    [SerializeField] private LobbyData data;

    private bool exiting;

    private void Start()
    {
        if (hud == null || lobbyCamera == null || data == null)
        {
            Debug.LogError("请补齐 LobbyNetworkHud 的三个引用。", this);
            enabled = false;
            return;
        }

        hud.Present(0, data.Capacity);
        HideLocalMarker();

        hud.ExitRequested += ExitLobby;
    }

    private void LateUpdate()
    {
        int playerCount = 0;

        if (NetworkClient.active)
        {
            // 只统计已经同步到本机的大厅玩家飞机。
            foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
            {
                if (identity != null &&
                    identity.GetComponent<LobbyNetworkPlayer>() != null)
                {
                    playerCount++;
                }
            }
        }

        hud.Present(playerCount, data.Capacity);

        NetworkIdentity localPlayer = NetworkClient.localPlayer;

        if (localPlayer == null ||
            localPlayer.GetComponent<LobbyNetworkPlayer>() == null)
        {
            HideLocalMarker();
            return;
        }

        Vector3 viewportPosition =
            lobbyCamera.WorldToViewportPoint(localPlayer.transform.position);

        hud.PlaceLocalMarker(viewportPosition);
    }

    private void HideLocalMarker()
    {
        // 现有 HUD 会在 z <= 0 时隐藏标记。
        hud.PlaceLocalMarker(new Vector3(0f, 0f, -1f));
    }

    private void ExitLobby()
    {
        if (exiting)
        {
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(
                SceneNavigation.StartMenuScene))
        {
            Debug.LogError("请在 Build Settings 中勾选 StartMenu 场景。");
            return;
        }

        NetworkManager manager = NetworkManager.singleton;

        if (manager == null)
        {
            Debug.LogError("没有找到大厅 Network Manager。");
            return;
        }

        // 正在联网时，由 Mirror 在断开后加载 Offline Scene。
        if (NetworkServer.active || NetworkClient.active)
        {
            if (string.IsNullOrWhiteSpace(manager.offlineScene))
            {
                Debug.LogError("请把 Network Manager 的 Offline Scene 设为 StartMenu。");
                return;
            }

            exiting = true;
            if (manager is DogfightNetworkManager session)
            {
                session.LeaveSession();
            }

            return;
        }

        // 尚未点击 Host / Client：直接退出，并清理常驻的管理物体。
        exiting = true;
        Destroy(manager.gameObject);
        SceneManager.LoadSceneAsync(SceneNavigation.StartMenuScene);
    }

    private void OnDestroy()
    {
        if (hud != null)
        {
            hud.ExitRequested -= ExitLobby;
        }
    }
}
