using DogfightThreatAI.Core.Lobby;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.InputSource;
using DogfightThreatAI.UI.Lobby;
using DogfightThreatAI.UI.Navigation;
using UnityEngine;

namespace DogfightThreatAI.Gameplay.Lobby
{
    /// <summary>本地预览驱动：输入 → Core 模拟 → 视图/HUD。接入网络后由网络驱动提交输入和快照。</summary>
    [RequireComponent(typeof(LobbyInputSource))]
    public sealed class LobbyController : MonoBehaviour
    {
        [SerializeField] LobbyData data;
        [SerializeField] PlayerView aircraftView;
        [SerializeField] LobbyHudView hud;
        [SerializeField] Camera lobbyCamera;
        LobbyInputSource _input;
        LobbySimulation _simulation;
        SceneNavigation _navigation;

        public LobbySimulation Simulation => _simulation;
        public LobbyData Data => data;

        // 检查大厅引用，创建本地模拟，并绑定退出请求。
        void Awake()
        {
            // 配置或视图引用缺失时停止初始化，避免创建不完整的本地大厅。
            if (data == null || !data.IsValid || aircraftView == null || hud == null || lobbyCamera == null)
            {
                Debug.LogError("大厅配置或表现引用缺失，请重新构建 LobbyScene。", this);
                enabled = false;
                return;
            }
            Time.timeScale = 1f;
            _input = GetComponent<LobbyInputSource>();
            _navigation = gameObject.AddComponent<SceneNavigation>();
            _simulation = new LobbySimulation(AircraftSpecAdapter.ToSpec(data.Aircraft), data.MovementBounds,
                data.GetSpawnPoint(0), data.Aircraft.VisualForwardOffsetDeg * Mathf.Deg2Rad);
            aircraftView.SetVisualForward(data.Aircraft.VisualForwardOffsetDeg);
            hud.Present(1, data.Capacity);
            hud.ExitRequested += Exit;
        }

        // 将大厅输入交给纯 C# 模拟，推进一个固定步。
        void FixedUpdate()
        {
            if (_simulation != null)
            {
                _simulation.Tick(_input.ReadInput(Time.fixedDeltaTime), Time.fixedDeltaTime);
            }
        }

        // 把模拟快照交给飞机视图，并更新本机玩家标记。
        void LateUpdate()
        {
            if (_simulation == null)
            {
                return;
            }
            float alpha = Time.fixedDeltaTime > 0f
                            ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;
            aircraftView.Render(_simulation.Current, _simulation.Previous, alpha, data.Aircraft.BodySprite);
            hud.PlaceLocalMarker(lobbyCamera.WorldToViewportPoint(aircraftView.RenderPosition));
        }

        // 响应 HUD 退出请求，返回主菜单。
        void Exit()
        {
            _navigation.ReturnToMainMenu();
        }

        // 解除退出订阅，避免场景卸载后保留回调。
        void OnDestroy()
        {
            if (hud != null)
            {
                hud.ExitRequested -= Exit;
            }
        }
    }
}
