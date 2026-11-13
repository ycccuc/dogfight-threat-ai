using System.Collections.Generic;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.UI.Battle;
using Mirror;
using UnityEngine;

public enum NetworkRoundPhase : byte
{
    Waiting,
    Fighting,
    Finished
}

/// <summary>把 Mirror 的玩家席位连接到一场纯 C# 对局，并同步结果和弹丸表现。</summary>
[RequireComponent(typeof(NetworkIdentity))]
[DefaultExecutionOrder(150)]
public sealed class NetworkBattleMatch : NetworkBehaviour
{
    [SerializeField] private WeaponData weapon;
    [SerializeField] private PatrolMissionLayout patrolLayout;
    [SerializeField, Min(1)] private int maxHealth = 5;
    [SerializeField, Min(0f)] private float invulnerabilitySeconds = .7f;
    [SerializeField, Min(1f)] private float roundSeconds = 180f;
    [SerializeField, Min(1f)] private float loadingSeconds = 25f;

    [SyncVar] private NetworkRoundPhase phase;
    [SyncVar] private int aliveCount;
    [SyncVar] private int winner;
    [SyncVar] private double deadline;
    [SyncVar] private int patrolPlayers;
    [SyncVar] private double patrolStartedAt;
    [SyncVar] private float patrolDuration;
    [SyncVar] private bool patrolWon;
    private readonly SyncList<int> patrolHealth = new SyncList<int>();
    private readonly SyncList<int> patrolSectors = new SyncList<int>();
    private readonly SyncList<int> patrolRecords = new SyncList<int>();

    private AirCombatRound round;
    private BattleNetworkPlayer[] players;
    private AircraftCombatFrame[] frames;
    private NetworkBattleHud hud;
    private ProjectileView projectileView;
    private PatrolMission patrol;
    private NetworkPatrolView patrolView;
    private Vector3[] patrolPoses;
    private double nextPatrolSnapshot;
    private readonly List<VisualShot> visualShots = new List<VisualShot>();
    private readonly List<ProjectileState> renderShots = new List<ProjectileState>();

    public NetworkRoundPhase Phase => phase;
    public bool IsCoop => patrolLayout != null;
    public int MaxHealth => IsCoop ? patrolLayout.PlayerHealth : maxHealth;
    public bool BlocksLocalInput => IsCoop ? patrolView != null && patrolView.BlocksInput : hud != null && hud.BlocksInput;

    private struct VisualShot
    {
        public ProjectileState State;
        public double SpawnTime;
    }

    public override void OnStartServer()
    {
        var manager = NetworkManager.singleton as DogfightNetworkManager;
        if (manager == null || manager.BattleSeatCount < 2 ||
            (IsCoop ? !patrolLayout.IsConfigured : weapon == null))
        {
            Debug.LogError("联机战斗缺少有效席位或机炮配置。", this);
            return;
        }

        int seats = manager.BattleSeatCount;
        if (IsCoop)
        {
            patrol = patrolLayout.CreateMission(seats);
            round = patrol.Combat;
            patrolPlayers = seats;
            patrolPoses = new Vector3[patrol.EnemyCount];
            for (int i = 0; i < patrol.EnemyCount; i++) patrolHealth.Add(patrol.EnemyHealth(i));
            for (int i = 0; i < patrol.SectorCount; i++) patrolSectors.Add(patrol.SectorRemaining(i));
            for (int i = 0; i < patrolLayout.Pickups.Length; i++) patrolRecords.Add(0);
        }
        else
        {
            round = new AirCombatRound(weapon.ToSpec(), seats, maxHealth, invulnerabilitySeconds);
        }
        players = new BattleNetworkPlayer[seats];
        frames = new AircraftCombatFrame[seats];
        deadline = NetworkTime.time + loadingSeconds;
        manager.RegisterBattle(this);
        for (int seat = 1; seat <= seats; seat++)
        {
            if (!manager.IsBattleSeatConnected(seat))
            {
                round.Forfeit(seat);
            }
        }
        UpdateAliveCount();
    }

    public override void OnStartClient()
    {
        var cannon = IsCoop ? patrolLayout.Weapon : weapon;
        if (cannon == null)
        {
            return;
        }

        var manager = NetworkManager.singleton;
        var spec = cannon.ToSpec();
        projectileView = gameObject.AddComponent<ProjectileView>();
        projectileView.Build((IsCoop ? patrolPlayers + patrolHealth.Count : manager.maxConnections) *
            (Mathf.CeilToInt(spec.Lifetime / spec.ShotInterval) + 2));

        if (IsCoop)
        {
            patrolView = new NetworkPatrolView(transform, patrolLayout, ReturnToLobby, LeaveSession);
            return;
        }
        var root = new GameObject("Network battle HUD", typeof(RectTransform));
        hud = root.AddComponent<NetworkBattleHud>();
        hud.Build(LeaveSession, ReturnToLobby);
    }

    public override void OnStopClient()
    {
        visualShots.Clear();
        renderShots.Clear();
        patrolView?.Dispose();
        patrolView = null;
        if (hud != null)
        {
            Destroy(hud.gameObject);
        }
        if (projectileView != null)
        {
            projectileView.Present(renderShots, 1f);
        }
    }

    [Server]
    public void RegisterPlayer(BattleNetworkPlayer player)
    {
        int seat = player.Seat;
        if (round == null || seat < 1 || seat > players.Length)
        {
            return;
        }
        players[seat - 1] = player;
        player.ApplyHealth(round.Health(seat));
    }

    [Server]
    public void Forfeit(int seat)
    {
        if (round == null || phase == NetworkRoundPhase.Finished || seat < 1 || seat > players.Length)
        {
            return;
        }

        round.Forfeit(seat);
        players[seat - 1] = null;
        UpdateAliveCount();
    }

    [ServerCallback]
    private void FixedUpdate()
    {
        if (round == null || phase == NetworkRoundPhase.Finished)
        {
            return;
        }

        double now = NetworkTime.time;
        if (phase == NetworkRoundPhase.Waiting)
        {
            int loaded = 0;
            for (int i = 0; i < players.Length; i++)
            {
                if (round.Health(i + 1) <= 0)
                {
                    continue;
                }
                if (players[i] != null && players[i].ServerFlightReady)
                {
                    loaded++;
                }
                else if (now >= deadline)
                {
                    round.Forfeit(i + 1);
                    if (players[i] != null)
                    {
                        players[i].ApplyHealth(0);
                    }
                }
            }

            UpdateAliveCount();
            if (loaded < aliveCount)
            {
                return;
            }
            if (loaded < (IsCoop ? 1 : 2))
            {
                FinishRound();
                return;
            }
            phase = NetworkRoundPhase.Fighting;
            deadline = now + roundSeconds;
            patrolStartedAt = now;
        }

        if ((!IsCoop && (aliveCount <= 1 || now >= deadline)) || (IsCoop && aliveCount == 0))
        {
            FinishRound();
            return;
        }

        for (int i = 0; i < players.Length; i++)
        {
            var player = players[i];
            frames[i] = player != null ? player.ConsumeCombatFrame() : default;
        }
        if (IsCoop)
        {
            patrol.TickTeam(Time.fixedDeltaTime, frames);
            PublishPatrol(now);
        }
        else
        {
            round.Tick(Time.fixedDeltaTime, frames);
        }

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
            {
                players[i].ApplyHealth(round.Health(i + 1));
            }
        }

        UpdateAliveCount();
        if (IsCoop ? patrol.Outcome != MissionOutcome.Active : aliveCount <= 1)
        {
            FinishRound();
            return;
        }

        foreach (int id in round.Removed)
        {
            RpcRemoveShot(id);
        }
        foreach (var shot in round.Fired)
        {
            RpcLaunchShot(shot.Id, shot.OwnerId,
                new Vector2(shot.Position.X, shot.Position.Y),
                new Vector2(shot.Velocity.X, shot.Velocity.Y), shot.Remaining, now);
        }
    }

    private void FinishRound()
    {
        if (IsCoop)
        {
            patrolWon = patrol.Outcome == MissionOutcome.Victory;
            patrolDuration = patrol.Elapsed;
        }
        else
        {
            winner = round.WinnerByHealth();
        }
        phase = NetworkRoundPhase.Finished;
    }

    private void UpdateAliveCount() => aliveCount = IsCoop ? patrol.PlayersRemaining : round.AliveCount;

    private void PublishPatrol(double now)
    {
        for (int i = 0; i < patrol.EnemyCount; i++)
        {
            int health = patrol.EnemyHealth(i);
            if (patrolHealth[i] != health) patrolHealth[i] = health;
        }
        for (int i = 0; i < patrol.SectorCount; i++)
        {
            int remaining = patrol.SectorRemaining(i);
            if (patrolSectors[i] != remaining) patrolSectors[i] = remaining;
        }
        for (int i = 0; i < patrolRecords.Count; i++)
        {
            int collector = patrol.CollectedBy(i);
            if (patrolRecords[i] != collector)
            {
                patrolRecords[i] = collector;
            }
        }
        for (int i = 0; i < players.Length; i++)
        {
            var player = players[i];
            if (player == null) continue;
            var contact = PatrolContact.None;
            for (int enemy = 0; enemy < patrol.EnemyCount; enemy++)
            {
                if (patrolHealth[enemy] <= 0) continue;
                var seen = patrol.EnemyContactFor(enemy, i + 1);
                if (seen == PatrolContact.Confirmed)
                {
                    contact = seen;
                    break;
                }
                if (seen > contact) contact = seen;
            }
            player.ApplyPatrolStatus(round.Cooldown01(i + 1), contact);
        }
        foreach (var hit in round.Collisions)
        {
            int playerSeat = hit.FirstSeat;
            var player = players[playerSeat - 1];
            RpcPatrolCollision(player != null ? player.netId : 0, hit.SecondSeat - patrolPlayers - 1,
                new Vector2(hit.Position.X, hit.Position.Y));
        }
        if (now < nextPatrolSnapshot)
        {
            return;
        }
        nextPatrolSnapshot = now + .05;
        for (int i = 0; i < patrolPoses.Length; i++)
        {
            var frame = patrol.EnemyFrame(i).Current;
            patrolPoses[i] = new Vector3(frame.Position.X, frame.Position.Y, frame.Heading * Mathf.Rad2Deg);
        }
        RpcPatrolPoses(now, patrolPoses);
    }

    [ClientRpc(channel = Channels.Unreliable)]
    private void RpcPatrolPoses(double now, Vector3[] poses) => patrolView?.ReceivePoses(now, poses);

    [ClientRpc]
    private void RpcPatrolCollision(uint playerId, int enemyIndex, Vector2 point)
    {
        bool local = NetworkClient.localPlayer != null && NetworkClient.localPlayer.netId == playerId;
        if (NetworkClient.spawned.TryGetValue(playerId, out var identity) &&
            identity.TryGetComponent<BattleNetworkPlayer>(out var player))
        {
            player.PresentCollision();
            // Render the local burst near the displayed aircraft when interpolation lags behind impact.
            Vector2 rendered = player.RenderPosition;
            if (local && (rendered - point).sqrMagnitude > 1f)
            {
                point = rendered;
            }
        }
        patrolView?.Collision(point, enemyIndex, local);
    }

    [ClientRpc]
    private void RpcLaunchShot(int id, int owner, Vector2 position, Vector2 velocity, float lifetime, double time)
    {
        visualShots.Add(new VisualShot
        {
            SpawnTime = time,
            State = new ProjectileState
            {
                Id = id,
                OwnerId = owner,
                Position = new Vec2(position.x, position.y),
                Velocity = new Vec2(velocity.x, velocity.y),
                Remaining = lifetime
            }
        });
    }

    [ClientRpc]
    private void RpcRemoveShot(int id)
    {
        for (int i = 0; i < visualShots.Count; i++)
        {
            if (visualShots[i].State.Id == id)
            {
                visualShots.RemoveAt(i);
                return;
            }
        }
    }

    [ClientCallback]
    private void LateUpdate()
    {
        if (hud == null && patrolView == null)
        {
            return;
        }
        BattleNetworkPlayer local = null;
        if (NetworkClient.localPlayer != null)
        {
            NetworkClient.localPlayer.TryGetComponent(out local);
        }
        if (IsCoop)
        {
            float elapsed = phase == NetworkRoundPhase.Finished ? patrolDuration :
                phase == NetworkRoundPhase.Waiting ? 0f : Mathf.Max(0f, (float)(NetworkTime.time - patrolStartedAt));
            patrolView.Present(phase, local, patrolHealth, patrolSectors, patrolRecords, patrolWon, elapsed);
        }
        else
        {
            int seconds = Mathf.Max(0, Mathf.CeilToInt((float)(deadline - NetworkTime.time)));
            hud.Present((int)phase, local != null ? local.Health : -1, maxHealth,
                aliveCount, seconds, winner, local != null ? local.Seat : 0,
                NetworkManager.singleton is DogfightNetworkManager manager && manager.IsLocalRoomOwner);
        }

        renderShots.Clear();
        if (phase == NetworkRoundPhase.Finished)
        {
            visualShots.Clear();
        }
        else
        {
            double now = NetworkTime.time;
            for (int i = visualShots.Count - 1; i >= 0; i--)
            {
                var shot = visualShots[i];
                float age = (float)(now - shot.SpawnTime);
                if (age >= shot.State.Remaining)
                {
                    visualShots.RemoveAt(i);
                }
                else if (age >= 0f)
                {
                    var state = shot.State;
                    state.Position += state.Velocity * age;
                    state.PreviousPosition = state.Position;
                    bool friendly = IsCoop ? state.OwnerId <= patrolPlayers : local != null && state.OwnerId == local.Seat;
                    state.OwnerId = friendly ? 1 : 2;
                    renderShots.Add(state);
                }
            }
        }
        projectileView.Present(renderShots, 1f);
    }

    private void LeaveSession()
    {
        if (NetworkManager.singleton is DogfightNetworkManager manager)
        {
            manager.LeaveSession();
        }
    }

    private void ReturnToLobby()
    {
        if (NetworkManager.singleton is DogfightNetworkManager manager)
        {
            manager.ReturnToLobby(this);
        }
    }
}
