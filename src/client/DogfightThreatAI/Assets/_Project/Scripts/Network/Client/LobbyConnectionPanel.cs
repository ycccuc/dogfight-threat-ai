using Mirror;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyConnectionPanel : MonoBehaviour
{
    [SerializeField] private GameObject panel;
    [SerializeField] private InputField addressInput;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;

    private void Start()
    {
        if (panel == null || addressInput == null ||
            hostButton == null || joinButton == null)
        {
            Debug.LogError("请补齐连接面板的四个引用。", this);
            enabled = false;
            return;
        }

        hostButton.onClick.AddListener(CreateLobby);
        joinButton.onClick.AddListener(JoinLobby);
    }

    private void Update()
    {
        // 连接成功后隐藏面板，连接过程中仍然显示。
        panel.SetActive(!NetworkClient.isConnected);

        bool canStart =
            NetworkManager.singleton != null &&
            !NetworkServer.active &&
            !NetworkClient.active &&
            !NetworkClient.isConnecting;

        hostButton.interactable = canStart;
        joinButton.interactable = canStart;
        addressInput.interactable = canStart;
    }

    private void CreateLobby()
    {
        NetworkManager manager = NetworkManager.singleton;

        if (manager == null ||
            NetworkServer.active || NetworkClient.active)
        {
            return;
        }

        manager.StartHost();
    }

    private void JoinLobby()
    {
        NetworkManager manager = NetworkManager.singleton;

        if (manager == null ||
            NetworkServer.active || NetworkClient.active)
        {
            return;
        }

        string address = addressInput.text.Trim();

        if (string.IsNullOrWhiteSpace(address))
        {
            address = "localhost";
            addressInput.text = address;
        }

        manager.networkAddress = address;
        manager.StartClient();
    }

    private void OnDestroy()
    {
        if (hostButton != null)
        {
            hostButton.onClick.RemoveListener(CreateLobby);
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveListener(JoinLobby);
        }
    }
}