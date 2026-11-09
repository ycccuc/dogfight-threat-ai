using Mirror;
using UnityEngine;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Lobby;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.InputSource;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(LobbyInputSource))]
public sealed class LobbyNetworkPlayer : NetworkBehaviour
{
    [SerializeField] private LobbyData data;

    [SyncVar]
    private bool readyToStart;

    public bool ReadyToStart => readyToStart;

    private LobbyInputSource inputSource;
    private LobbySimulation simulation;

    private FlightInput serverInput = new FlightInput(-1f, 0f);
    private float nextSendTime;
    private float lastInputTime;

    private void Awake()
    {
        inputSource = GetComponent<LobbyInputSource>();
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (data == null || !data.IsValid)
        {
            Debug.LogError("联网大厅飞机缺少有效的 Lobby 配置。", this);
            return;
        }

        // 使用 Network Manager 分配的出生位置。
        Vector3 position = transform.position;
        float heading =
            (transform.eulerAngles.z + data.Aircraft.VisualForwardOffsetDeg)
            * Mathf.Deg2Rad;

        simulation = new LobbySimulation(
            AircraftSpecAdapter.ToSpec(data.Aircraft),
            data.MovementBounds,
            new Vec2(position.x, position.y),
            heading);

        lastInputTime = Time.unscaledTime;
    }

    private void Update()
    {
        // 每台电脑只读取属于自己的飞机的输入。
        if (!isLocalPlayer)
        {
            return;
        }

        // 每秒发送约 20 次操作。
        if (Time.unscaledTime < nextSendTime)
        {
            return;
        }

        nextSendTime = Time.unscaledTime + 0.05f;

        FlightInput input = inputSource.ReadInput(Time.deltaTime);
        CmdSetInput(input.Throttle, input.Steer);
    }

    [Command]
    private void CmdSetInput(float throttle, float steer)
    {
        // 网络传来的数据必须在服务器检查。
        if (float.IsNaN(throttle) || float.IsInfinity(throttle) ||
            float.IsNaN(steer) || float.IsInfinity(steer))
        {
            return;
        }

        serverInput = new FlightInput(
            Mathf.Clamp(throttle, -1f, 1f),
            Mathf.Clamp(steer, -1f, 1f));

        lastInputTime = Time.unscaledTime;
    }

    private void FixedUpdate()
    {
        // 只有服务器推进飞行计算。
        if (!isServer || simulation == null)
        {
            return;
        }

        // 长时间没收到输入时刹车，避免持续沿用旧操作。
        if (Time.unscaledTime - lastInputTime > 0.5f)
        {
            serverInput = new FlightInput(-1f, 0f);
        }

        simulation.Tick(serverInput, Time.fixedDeltaTime);

        // 把模拟结果写到物体上，交给 Network Transform 同步。
        var state = simulation.Current;

        transform.SetPositionAndRotation(
            new Vector3(
                state.Position.X,
                state.Position.Y,
                transform.position.z),
            Quaternion.Euler(
                0f,
                0f,
                state.Heading * Mathf.Rad2Deg
                    - data.Aircraft.VisualForwardOffsetDeg));
    }

    public void RequestToggleReady()
    {
        if (!isLocalPlayer)
        {
            return;
        }

        CmdToggleReady();
    }

    [Command]
    private void CmdToggleReady()
    {
        readyToStart = !readyToStart;
    }
}