using Mirror;
using UnityEngine;
using UnityEngine.UI;

public sealed class LobbyStartButton : MonoBehaviour
{
    [SerializeField] private Button startButton;
    private Button coopButton;

    private void Start()
    {
        if (startButton == null)
        {
            Debug.LogError("请绑定开始比赛按钮。", this);
            enabled = false;
            return;
        }

        coopButton = Instantiate(startButton, startButton.transform.parent);
        coopButton.name = "StartCoopButton";
        var versusRect = (RectTransform)startButton.transform;
        var coopRect = (RectTransform)coopButton.transform;
        Vector2 center = versusRect.anchoredPosition;
        versusRect.anchoredPosition = center + Vector2.left * 79f;
        coopRect.anchoredPosition = center + Vector2.right * 79f;
        versusRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 146f);
        coopRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 146f);
        startButton.GetComponentInChildren<Text>().text = "自由对战";
        coopButton.GetComponentInChildren<Text>().text = "合作清剿";
        startButton.onClick.AddListener(StartMatch);
        coopButton.onClick.AddListener(StartCoop);
    }

    private void Update()
    {
        DogfightNetworkManager manager =
            NetworkManager.singleton as DogfightNetworkManager;

        if (startButton == null || coopButton == null)
        {
            return;
        }
        bool isOwner = manager != null && manager.IsLocalRoomOwner;

        startButton.gameObject.SetActive(isOwner);

        startButton.interactable =
            isOwner && manager.CanStartMatch();
        coopButton.gameObject.SetActive(isOwner);
        coopButton.interactable = startButton.interactable;
    }

    private void StartCoop()
    {
        if (NetworkManager.singleton is DogfightNetworkManager manager)
        {
            manager.StartCoop();
        }
    }

    private void StartMatch()
    {
        DogfightNetworkManager manager =
            NetworkManager.singleton as DogfightNetworkManager;

        if (manager == null)
        {
            Debug.LogError("没有找到 DogfightNetworkManager。");
            return;
        }

        manager.StartMatch();
    }

    private void OnDestroy()
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(StartMatch);
        }
        if (coopButton != null)
        {
            Destroy(coopButton.gameObject);
        }
    }
}
