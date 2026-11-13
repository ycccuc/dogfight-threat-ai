using Mirror;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyReadyPanel : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private Button readyButton;
    [SerializeField] private Text buttonText;
    [SerializeField] private Text countText;

    private void Start()
    {
        if (panel == null || readyButton == null ||
            buttonText == null || countText == null)
        {
            Debug.LogError("请补齐准备面板的四个引用。", this);
            enabled = false;
            return;
        }

        readyButton.onClick.AddListener(ToggleReady);
    }

    private void Update()
    {
        LobbyNetworkPlayer localPlayer = GetLocalPlayer();

        // 只有本机玩家生成完成后，才显示准备面板。
        panel.SetActive(localPlayer != null);

        if (localPlayer == null)
        {
            return;
        }

        buttonText.text =
            localPlayer.ReadyToStart ? "取消准备" : "准备";

        var manager = NetworkManager.singleton as DogfightNetworkManager;
        SessionStateMessage state = manager != null ? manager.ClientSession : default;
        string role = state.IsRoomOwner ? "房主" : "队员";
        string message = $"{role} · 已准备：{state.ReadyCount} / {state.PlayerCount}";

        if (countText.text != message)
        {
            countText.text = message;
        }
    }

    private LobbyNetworkPlayer GetLocalPlayer()
    {
        if (!NetworkClient.isConnected ||
            NetworkClient.localPlayer == null)
        {
            return null;
        }

        return NetworkClient.localPlayer
            .GetComponent<LobbyNetworkPlayer>();
    }

    private void ToggleReady()
    {
        LobbyNetworkPlayer localPlayer = GetLocalPlayer();

        if (localPlayer == null)
        {
            return;
        }

        localPlayer.RequestToggleReady();
    }

    private void OnDestroy()
    {
        if (readyButton != null)
        {
            readyButton.onClick.RemoveListener(ToggleReady);
        }
    }
}
