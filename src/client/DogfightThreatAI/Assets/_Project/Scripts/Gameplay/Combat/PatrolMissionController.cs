using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.InputSource;
using DogfightThreatAI.UI.Battle;
using DogfightThreatAI.UI.Navigation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace DogfightThreatAI.Gameplay.Combat
{
    /// <summary>把 Unity 飞行、关卡配置和本地 HUD 接到纯 C# 任务。</summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PatrolMissionController : MonoBehaviour
    {
        enum Screen
        {
            Briefing,
            Flight,
            Pause,
            Result,
            Loading
        }

        [SerializeField] PlayerController player;
        [SerializeField] PatrolMissionLayout layout;

        PatrolMission mission;
        WeaponInputSource weaponInput;
        PlayerView playerView;
        AircraftDamageView playerDamage;
        AircraftHitbox playerHitbox;
        KeyboardInputSource keyboard;
        PlayerPropulsionView propulsion;
        PatrolMissionHud hud;
        ProjectileView projectiles;
        PatrolMissionEffects effects;
        PlayerView[] enemyViews;
        AircraftDamageView[] enemyDamage;
        bool[] collisionFade;
        bool[] shownCollection;
        int[] enemyHealthCache;
        int[] sectorRemaining;
        float nextHudUpdate;
        float feedbackUntil;
        float allowFireAt;
        float lastEnemySince = -1f;
        int lastPlayerHealth;
        Screen screen;

        public PatrolMission Mission => mission;
        public bool IsFlying => screen == Screen.Flight;

        void Awake()
        {
            if (player == null || !player.IsInitialized || !player.ArenaBounds.HasValue ||
                layout == null || !layout.IsConfigured)
            {
                Debug.LogError("河谷清剿场景缺少机型、边界、武器或巡逻区配置。", this);
                enabled = false;
                return;
            }

            mission = layout.CreateMission();
            sectorRemaining = new int[layout.Sectors.Length];
            shownCollection = new bool[layout.Pickups.Length];
            for (int i = 0; i < sectorRemaining.Length; i++)
            {
                sectorRemaining[i] = mission.SectorRemaining(i);
            }
            collisionFade = new bool[mission.EnemyCount + 1];
            lastPlayerHealth = layout.PlayerHealth;
            weaponInput = player.gameObject.AddComponent<WeaponInputSource>();
            weaponInput.Blocked = true;
            playerHitbox = player.GetComponent<AircraftHitbox>();
            playerView = player.GetComponentInChildren<PlayerView>();
            keyboard = player.GetComponent<KeyboardInputSource>();
            propulsion = player.gameObject.AddComponent<PlayerPropulsionView>();
            propulsion.Build(player.GetComponentInChildren<SpriteRenderer>());
            playerDamage = player.gameObject.AddComponent<AircraftDamageView>();
            projectiles = gameObject.AddComponent<ProjectileView>();
            projectiles.Build(mission.Combat.ProjectileCapacity);
            effects = gameObject.AddComponent<PatrolMissionEffects>();
            effects.Build(layout.PickupPoints());
            CreatePatrolViews();

            var root = new GameObject("Patrol mission HUD", typeof(RectTransform));
            root.transform.SetParent(transform, false);
            hud = root.AddComponent<PatrolMissionHud>();
            layout.BuildHud(hud, Retry, ReturnToMenu);
            for (int i = 0; i < sectorRemaining.Length; i++)
            {
                hud.PresentSector(i, sectorRemaining[i]);
            }
            screen = Screen.Briefing;
            Time.timeScale = 0f;
            hud.ShowBriefing(mission.EnemyCount, Deploy);
        }

        void CreatePatrolViews()
        {
            enemyViews = new PlayerView[mission.EnemyCount];
            enemyDamage = new AircraftDamageView[mission.EnemyCount];
            enemyHealthCache = new int[mission.EnemyCount];
            for (int i = 0; i < enemyViews.Length; i++)
            {
                var aircraft = new GameObject($"Patrol aircraft {i + 1}");
                aircraft.transform.SetParent(transform, false);
                var sprite = aircraft.AddComponent<SpriteRenderer>();
                sprite.sprite = layout.EnemyAircraft.BodySprite;
                sprite.sortingOrder = 12;
                var view = aircraft.AddComponent<PlayerView>();
                view.SetVisualForward(layout.EnemyAircraft.VisualForwardOffsetDeg);
                var frame = mission.EnemyFrame(i);
                view.Render(frame.Current, frame.Previous, 1f, layout.EnemyAircraft.BodySprite);
                enemyViews[i] = view;
                enemyDamage[i] = aircraft.AddComponent<AircraftDamageView>();
                enemyHealthCache[i] = layout.EnemyHealth;
            }
        }

        public void Deploy()
        {
            if (screen != Screen.Briefing && screen != Screen.Pause)
            {
                return;
            }
            screen = Screen.Flight;
            allowFireAt = Time.unscaledTime + .22f;
            Time.timeScale = 1f;
            hud.ShowFlight();
            RefreshHud();
        }

        public void Pause()
        {
            if (screen != Screen.Flight)
            {
                return;
            }
            screen = Screen.Pause;
            weaponInput.Blocked = true;
            Time.timeScale = 0f;
            RefreshHud();
            hud.ShowPause(Deploy);
        }

        public void Retry() => Load(gameObject.scene.path);
        public void ReturnToMenu() => Load(SceneNavigation.StartMenuScenePath);

        void Load(string path)
        {
            if (screen == Screen.Loading)
            {
                return;
            }
            screen = Screen.Loading;
            player.enabled = false;
            weaponInput.Blocked = true;
            Time.timeScale = 1f;
            SceneManager.LoadSceneAsync(path, LoadSceneMode.Single);
        }

        void FixedUpdate()
        {
            if (!IsFlying)
            {
                return;
            }
            float radius = playerHitbox != null ? playerHitbox.WorldRadius : .42f;
            bool fire = Time.unscaledTime >= allowFireAt && weaponInput.Consume();
            mission.Tick(Time.fixedDeltaTime, new AircraftCombatFrame(
                player.PreviousState, player.State, radius, fire));
            ApplyDamageEffects();
            PresentPickups();

            if (mission.Outcome != MissionOutcome.Active)
            {
                screen = Screen.Result;
                player.enabled = false;
                weaponInput.Blocked = true;
                Time.timeScale = 0f;
                hud.ShowResult(mission.Outcome == MissionOutcome.Victory,
                    mission.EnemyCount - mission.EnemiesRemaining, mission.EnemyCount, mission.Elapsed,
                    mission.CollectedCount, layout.Pickups.Length);
            }
        }

        void ApplyDamageEffects()
        {
            int hp = mission.Combat.Health(PatrolMission.PlayerSeat);
            if (hp < lastPlayerHealth)
            {
                collisionFade[0] = false;
                playerDamage.PlayHit();
                if (hp == 0 && playerView != null)
                {
                    playerView.SetVisible(false);
                }
            }
            for (int i = 0; i < enemyViews.Length; i++)
            {
                int enemyHp = mission.EnemyHealth(i);
                if (enemyHp < enemyHealthCache[i])
                {
                    collisionFade[i + 1] = false;
                    var view = enemyViews[i];
                    if (enemyHp == 0)
                    {
                        view.SetVisible(false);
                    }
                    else
                    {
                        enemyDamage[i].PlayHit();
                    }
                    Feedback(enemyHp == 0 ? "敌机击毁" : "命中敌机", false);
                }
                enemyHealthCache[i] = enemyHp;
            }
            if (hp < lastPlayerHealth)
            {
                Feedback("机体受损", true);
            }
            foreach (var collision in mission.Combat.Collisions)
            {
                collisionFade[collision.FirstSeat - 1] = true;
                collisionFade[collision.SecondSeat - 1] = true;
                effects.Impact(collision.Position);
                Feedback("机身碰撞  ·  机体受损", true);
            }
            lastPlayerHealth = hp;
        }

        void PresentPickups()
        {
            for (int i = 0; i < shownCollection.Length; i++)
            {
                if (!shownCollection[i] && mission.IsCollected(i))
                {
                    shownCollection[i] = true;
                    effects.Collect(i);
                    hud.PresentCollection(i, layout.Pickups[i].name, true, mission.CollectedCount);
                    hud.ShowPickup($"已拾取  {layout.Pickups[i].name}  {mission.CollectedCount}/{layout.Pickups.Length}");
                }
            }
        }

        void Feedback(string message, bool damage)
        {
            hud.PresentFeedback(message, damage);
            feedbackUntil = Time.unscaledTime + 1.1f;
        }

        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                if (screen == Screen.Pause)
                {
                    Deploy();
                }
                else
                {
                    Pause();
                }
            }
            if (!IsFlying)
            {
                return;
            }
            if (UnityEngine.Input.GetKeyDown(KeyCode.Tab))
            {
                hud.ToggleChart();
            }
            weaponInput.Blocked = Time.unscaledTime < allowFireAt ||
                (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject());
            if (feedbackUntil > 0f && Time.unscaledTime >= feedbackUntil)
            {
                feedbackUntil = 0f;
                hud.PresentFeedback("", false);
            }
            if (Time.unscaledTime >= nextHudUpdate)
            {
                nextHudUpdate = Time.unscaledTime + .12f;
                RefreshHud();
            }
            hud.PresentPosition(layout.ChartPosition(player.State.Position));
            hud.PresentIntro(mission.Elapsed);
            RenderPatrols();
        }

        void RenderPatrols()
        {
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            for (int i = 0; i < enemyViews.Length; i++)
            {
                if (enemyHealthCache[i] > 0)
                {
                    var frame = mission.EnemyFrame(i);
                    enemyViews[i].Render(frame.Current, frame.Previous, alpha, layout.EnemyAircraft.BodySprite);
                    enemyDamage[i].Present(true, mission.Combat.Invulnerability(i + 2), collisionFade[i + 1]);
                }
            }
        }

        void LateUpdate()
        {
            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime);
            playerDamage.Present(lastPlayerHealth > 0, mission.Combat.Invulnerability(PatrolMission.PlayerSeat),
                collisionFade[0]);
            projectiles.Present(mission.Combat.Projectiles, alpha);
            effects.Present();
            if (keyboard != null)
            {
                hud.PresentBoost(keyboard.BoostEnergy01);
                var heading = player.State.Forward;
                propulsion.Present(lastPlayerHealth > 0, keyboard.VisiblePropulsionCue, player.State.Speed,
                    playerView != null ? playerView.RenderPosition : player.transform.position,
                    new Vector3(heading.X, heading.Y));
            }
        }

        void RefreshHud()
        {
            int cleared = 0;
            int nearestSector = -1;
            float nearest = float.PositiveInfinity;
            Vec2 position = player.State.Position;
            for (int i = 0; i < layout.Sectors.Length; i++)
            {
                int left = mission.SectorRemaining(i);
                if (left != sectorRemaining[i])
                {
                    sectorRemaining[i] = left;
                    hud.PresentSector(i, left);
                }
                if (left == 0)
                {
                    cleared++;
                    continue;
                }

                float distance = (SectorCenter(i) - position).SqrMagnitude;
                if (distance < nearest)
                {
                    nearest = distance;
                    nearestSector = i;
                }
            }

            bool confirmed = false, suspicious = false, searching = false;
            int lastEnemy = -1;
            for (int i = 0; i < enemyHealthCache.Length; i++)
            {
                if (enemyHealthCache[i] <= 0)
                {
                    continue;
                }
                lastEnemy = i;
                var contact = mission.EnemyContact(i);
                confirmed |= contact == PatrolContact.Confirmed;
                suspicious |= contact == PatrolContact.Suspicious;
                searching |= contact == PatrolContact.Lost;
            }

            string guidance = nearestSector >= 0
                ? $"最近目标  {layout.Sectors[nearestSector].name} · {Bearing(SectorCenter(nearestSector) - position)}"
                : "敌方空域已清除";
            string radar = "";
            if (mission.EnemiesRemaining == 1 && lastEnemy >= 0)
            {
                if (lastEnemySince < 0f)
                {
                    lastEnemySince = mission.Elapsed;
                }
                if (mission.Elapsed - lastEnemySince >= 8f)
                {
                    var relative = mission.EnemyFrame(lastEnemy).Current.Position - position;
                    radar = $"末架回波  {Bearing(relative)} · 约 {Mathf.Max(5, Mathf.RoundToInt(relative.Magnitude / 5f) * 5)}";
                }
            }

            string alert = confirmed ? "敌机已发现你" : searching ? "敌机正在搜索" :
                suspicious ? "敌机起疑" : "";
            hud.PresentStatus(mission.EnemiesRemaining, mission.EnemyCount, cleared, layout.Sectors.Length,
                lastPlayerHealth, layout.PlayerHealth, 1f - mission.Combat.Cooldown01(PatrolMission.PlayerSeat), guidance, alert, radar);
        }

        Vec2 SectorCenter(int index) => new Vec2(layout.Sectors[index].center.x, layout.Sectors[index].center.y);

        static readonly string[] Directions = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };

        static string Bearing(Vec2 relative)
        {
            if (relative.SqrMagnitude < 12f)
            {
                return "附近";
            }
            float degrees = Mathf.Atan2(relative.X, relative.Y) * Mathf.Rad2Deg;
            return Directions[Mathf.RoundToInt(Mathf.Repeat(degrees, 360f) / 45f) % Directions.Length];
        }

        void OnDestroy()
        {
            if (mission != null && screen != Screen.Loading)
            {
                Time.timeScale = 1f;
            }
        }
    }
}
