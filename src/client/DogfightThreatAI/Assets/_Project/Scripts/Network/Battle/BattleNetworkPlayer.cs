using Mirror;
using UnityEngine;
using UnityEngine.EventSystems;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Input;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.InputSource;
using DogfightThreatAI.Gameplay.View;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(SpriteRenderer))]
[DefaultExecutionOrder(100)]
public sealed class BattleNetworkPlayer : NetworkBehaviour
{
    [SerializeField] private AircraftData aircraft;
    [SerializeField] private Vector2 arenaHalfSize =
        new Vector2(200f, 150f);
    [SerializeField, Min(.01f)] private float hitRadius = .4f;

    [SyncVar] private int seat;
    [SyncVar(hook = nameof(OnHealthChanged))] private int health;
    [SyncVar] private VisiblePropulsionCue propulsionCue;
    [SyncVar] private byte speedTenths;
    [SyncVar] private byte boostPercent = 100;
    [SyncVar] private byte cannonPercent = 100;
    [SyncVar] private PatrolContact patrolContact;
    public int Seat => seat;
    public int Health => health;
    public bool ServerFlightReady => serverInitialized;
    public float BoostEnergy01 => boostPercent / 100f;
    public float CannonReady01 => cannonPercent / 100f;
    public PatrolContact PatrolContact => patrolContact;
    public Vector3 RenderPosition => visual.position;

    private FlightModel model;
    private FlightState state;
    private FlightState previousState;
    private Transform visual;
    private SpriteRenderer visualRenderer;
    private Color aircraftColor;
    private NetworkBattleMatch match;
    private WeaponInputSource weaponInput;
    private bool firePending;
    private readonly PlayerBoostModel boost = new PlayerBoostModel();
    private PlayerPropulsionView propulsion;
    private bool dashPending, holdPending, holdServer;
    private bool localDashPending, localHoldPending, holdBlocked;
    private float collisionUntil;
    [SerializeField, Min(0.01f)] private float clientVisualSmoothTime = 0.05f;
    private Vector3 clientVisualPosition;
    private Vector3 clientVisualVelocity;
    private Quaternion clientVisualRotation;
    private bool clientVisualInitialized;
    private FlightInput serverInput = new FlightInput(-1f, 0f);

    private float nextSendTime;
    private float lastInputTime;
    private bool serverInitialized;

    private void Awake()
    {
        SpriteRenderer source = GetComponent<SpriteRenderer>();
        if (aircraft != null)
        {
            source.sprite = aircraft.BodySprite;
        }

        // 根物体保留服务器真实位置，Art 只负责显示，不能参与网络同步。
        visual = new GameObject("Art").transform;
        visual.SetParent(transform, false);
        visual.gameObject.layer = gameObject.layer;
        visualRenderer = visual.gameObject.AddComponent<SpriteRenderer>();
        visualRenderer.sprite = source.sprite;
        visualRenderer.sharedMaterial = source.sharedMaterial;
        aircraftColor = source.color;
        visualRenderer.color = new Color(1f, .55f, .46f, aircraftColor.a);
        visualRenderer.flipX = source.flipX;
        visualRenderer.flipY = source.flipY;
        visualRenderer.sortingLayerID = source.sortingLayerID;
        visualRenderer.sortingOrder = source.sortingOrder;
        source.enabled = false;
    }

    internal void AssignSeat(NetworkBattleMatch battle, int assignedSeat)
    {
        match = battle;
        seat = assignedSeat;
        health = battle.MaxHealth;
    }

    internal void ApplyHealth(int value)
    {
        health = value;
    }

    internal void ApplyPatrolStatus(float cooldown, PatrolContact contact)
    {
        cannonPercent = (byte)Mathf.RoundToInt(100f * Mathf.Clamp01(1f - cooldown));
        patrolContact = contact;
    }

    internal void PresentCollision() =>
        collisionUntil = Time.unscaledTime + PatrolMissionEffects.CollisionFeedbackSeconds;

    internal AircraftCombatFrame ConsumeCombatFrame()
    {
        bool fire = firePending;
        firePending = false;
        return new AircraftCombatFrame(previousState, state, hitRadius, fire);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        if (match == null)
        {
            match = FindObjectOfType<NetworkBattleMatch>();
        }
        visualRenderer.enabled = health > 0;
        propulsion = gameObject.AddComponent<PlayerPropulsionView>();
        propulsion.Build(visualRenderer);
    }

    private void OnHealthChanged(int previous, int current)
    {
        visualRenderer.enabled = current > 0;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();

        if (aircraft == null ||
            arenaHalfSize.x <= 0f || arenaHalfSize.y <= 0f)
        {
            Debug.LogError("战斗飞机缺少机型配置或有效边界。", this);
            return;
        }

        Bounds2D bounds = Bounds2D.Centered(
            new Vec2(arenaHalfSize.x, arenaHalfSize.y));

        Vector3 position = transform.position;
        Vec2 spawn = new Vec2(position.x, position.y);

        if (!bounds.Contains(spawn))
        {
            Debug.LogError("战斗飞机出生点超出了战场边界。", this);
            return;
        }

        model = FlightModel.From(
            AircraftSpecAdapter.ToSpec(aircraft), bounds);

        float heading =
            (transform.eulerAngles.z + aircraft.VisualForwardOffsetDeg)
            * Mathf.Deg2Rad;

        state = FlightState.At(spawn, heading);
        previousState = state;
        lastInputTime = Time.unscaledTime;
        serverInitialized = true;
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        visualRenderer.color = aircraftColor;
        weaponInput = gameObject.AddComponent<WeaponInputSource>();
        weaponInput.Blocked = true;

        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("战斗场景中没有标记为 MainCamera 的相机。", this);
            return;
        }

        CameraFollow follow = mainCamera.GetComponent<CameraFollow>();

        if (follow == null)
        {
            Debug.LogError("战斗相机缺少 CameraFollow。", mainCamera);
            return;
        }

        follow.SetTarget(visual);
    }

    private void Update()
    {
        if (!isLocalPlayer || weaponInput == null)
        {
            return;
        }

        if (match == null)
        {
            match = FindObjectOfType<NetworkBattleMatch>();
        }
        bool controlsActive = health > 0 && match != null && !match.BlocksLocalInput &&
            match.Phase == NetworkRoundPhase.Fighting && Application.isFocused;
        weaponInput.Blocked = !controlsActive ||
            (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
        bool braking = Input.GetKey(KeyCode.S);
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        if (!controlsActive || braking)
        {
            localDashPending = localHoldPending = false;
            holdBlocked = shift;
        }
        else
        {
            holdBlocked &= shift;
            localDashPending |= Input.GetKeyDown(KeyCode.Space);
            localHoldPending |= !holdBlocked &&
                (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift));
        }
        if (Time.unscaledTime < nextSendTime)
        {
            return;
        }

        nextSendTime = Time.unscaledTime + 0.05f;

        float throttle = -1f;
        float steer = 0f;

        if (controlsActive)
        {
            // 战斗模式：松开 W/S 时为中性油门。
            throttle = Input.GetKey(KeyCode.S) ? -1f :
                Input.GetKey(KeyCode.W) ? 1f : 0f;

            steer = (Input.GetKey(KeyCode.A) ? 1f : 0f)
                - (Input.GetKey(KeyCode.D) ? 1f : 0f);
        }

        CmdSetInput(throttle, steer, weaponInput.Consume(),
            localDashPending, controlsActive && !braking && shift && !holdBlocked, localHoldPending);
        localDashPending = localHoldPending = false;
    }

    [Command]
    private void CmdSetInput(float throttle, float steer, bool fire, bool dash, bool hold, bool holdPressed)
    {
        if (health <= 0 || match == null || match.Phase != NetworkRoundPhase.Fighting ||
            float.IsNaN(throttle) || float.IsInfinity(throttle) ||
            float.IsNaN(steer) || float.IsInfinity(steer))
        {
            return;
        }

        serverInput = new FlightInput(
            Mathf.Clamp(throttle, -1f, 1f),
            Mathf.Clamp(steer, -1f, 1f));

        lastInputTime = Time.unscaledTime;
        firePending |= fire;
        if (throttle < 0f)
        {
            dashPending = holdPending = holdServer = false;
        }
        else
        {
            dashPending |= dash;
            holdPending |= holdPressed;
            holdServer = hold;
        }
    }

    private void FixedUpdate()
    {
        if (!isServer || !serverInitialized)
        {
            return;
        }

        previousState = state;
        if (health <= 0 || match == null || match.Phase != NetworkRoundPhase.Fighting)
        {
            firePending = dashPending = holdPending = holdServer = false;
            propulsionCue = VisiblePropulsionCue.None;
            return;
        }

        if (Time.unscaledTime - lastInputTime > 0.5f)
        {
            serverInput = new FlightInput(-1f, 0f);
            firePending = dashPending = holdPending = holdServer = false;
        }

        bool braking = serverInput.Throttle < 0f;
        boost.Step(dashPending, holdServer, Time.fixedDeltaTime,
            Vec2.Dot(state.Velocity, state.Forward), true, braking, holdPending);
        dashPending = holdPending = false;
        var flightInput = boost.RequestsForwardThrust && !braking
            ? new FlightInput(1f, serverInput.Steer) : serverInput;
        model.Step(ref state, flightInput, Time.fixedDeltaTime, boost.MotionCommand);
        propulsionCue = boost.IsOverheated ? VisiblePropulsionCue.Overheat : boost.IsDashing ? VisiblePropulsionCue.Dash :
            boost.IsSustaining ? VisiblePropulsionCue.Hold : VisiblePropulsionCue.None;
        speedTenths = (byte)Mathf.Clamp(Mathf.RoundToInt(state.Speed * 10f), 0, 255);
        boostPercent = (byte)Mathf.RoundToInt(boost.Energy01 * 100f);

        transform.SetPositionAndRotation(
            new Vector3(
                state.Position.X,
                state.Position.Y,
                transform.position.z),
            Quaternion.Euler(
                0f,
                0f,
                state.Heading * Mathf.Rad2Deg
                    - aircraft.VisualForwardOffsetDeg));
    }

    private void LateUpdate()
    {
        // 客户端只平滑 Art。用独立的世界坐标缓存，避免父物体更新时带动缓存。
        if (!isServer)
        {
            if (!isClient)
            {
                return;
            }

            Vector3 destination = transform.position;
            if (!clientVisualInitialized ||
                (destination - clientVisualPosition).sqrMagnitude > 100f)
            {
                clientVisualPosition = destination;
                clientVisualRotation = transform.rotation;
                clientVisualVelocity = Vector3.zero;
                clientVisualInitialized = true;
            }
            float smoothTime = Mathf.Max(0.01f, clientVisualSmoothTime);
            clientVisualPosition = Vector3.SmoothDamp(
                clientVisualPosition, destination, ref clientVisualVelocity,
                smoothTime, Mathf.Infinity, Time.deltaTime);
            float blend = 1f - Mathf.Exp(-Time.deltaTime / smoothTime);
            clientVisualRotation = Quaternion.Slerp(
                clientVisualRotation, transform.rotation, blend);
            visual.SetPositionAndRotation(clientVisualPosition, clientVisualRotation);
            PresentAircraft();
            return;
        }

        if (!serverInitialized)
        {
            return;
        }

        // Host 不经过远端快照插值，需要在两个服务器固定步之间平滑显示。
        float alpha = Time.fixedDeltaTime > 0f
            ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
            : 1f;
        Vec2 position = previousState.Position +
            (state.Position - previousState.Position) * alpha;
        float heading = Mathf.Lerp(previousState.Heading, state.Heading, alpha);
        visual.SetPositionAndRotation(
            new Vector3(position.X, position.Y, transform.position.z),
            Quaternion.Euler(0f, 0f,
                heading * Mathf.Rad2Deg - aircraft.VisualForwardOffsetDeg));
        PresentAircraft();
    }

    private void PresentAircraft()
    {
        if (propulsion == null)
        {
            return;
        }
        var color = isLocalPlayer || (match != null && match.IsCoop)
            ? aircraftColor : new Color(1f, .55f, .46f, aircraftColor.a);
        visualRenderer.color = PatrolMissionEffects.CollisionTint(color, collisionUntil - Time.unscaledTime);
        Vector3 forward = visual.rotation * (Quaternion.Euler(0f, 0f, aircraft.VisualForwardOffsetDeg) * Vector3.right);
        propulsion.Present(health > 0, propulsionCue, speedTenths * .1f, visual.position, forward);
    }
}
