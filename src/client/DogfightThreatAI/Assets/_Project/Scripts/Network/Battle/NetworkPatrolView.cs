using System;
using System.Collections.Generic;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.UI.Battle;
using Mirror;
using UnityEngine;

/// <summary>Client-side mission journal, patrol snapshots and collision effects.</summary>
internal sealed class NetworkPatrolView
{
    readonly PatrolMissionLayout layout;
    readonly PatrolMissionHud hud;
    readonly PatrolMissionEffects effects;
    readonly GameObject worldRoot;
    readonly SpriteRenderer[] enemies;
    readonly bool[] records;
    readonly float[] collisionUntil;
    readonly List<(double Time, Vector3[] Poses)> snapshots = new List<(double, Vector3[])>(16);
    static readonly string[] Bearings = { "北", "东北", "东", "东南", "南", "西南", "西", "西北" };
    float nextStatusUpdate, feedbackUntil;
    NetworkRoundPhase phase = (NetworkRoundPhase)byte.MaxValue;
    bool menuOpen;
    bool resultCanReturn;

    public bool BlocksInput => menuOpen;

    public NetworkPatrolView(Transform parent, PatrolMissionLayout layout, Action lobby, Action leave)
    {
        this.layout = layout;
        var root = new GameObject("Coop patrol HUD", typeof(RectTransform));
        root.transform.SetParent(parent, false);
        hud = root.AddComponent<PatrolMissionHud>();
        layout.BuildHud(hud, lobby, leave);
        hud.UseNetworkLabels();
        hud.ShowNetworkWaiting();
        // 飞机、物品与火花都用世界坐标渲染，和 HUD 的分辨率缩放分离。
        worldRoot = new GameObject("Coop patrol world");
        worldRoot.transform.SetParent(parent, false);
        effects = worldRoot.AddComponent<PatrolMissionEffects>();
        effects.Build(layout.PickupPoints());
        records = new bool[layout.Pickups.Length];
        int count = 0;
        foreach (var sector in layout.Sectors)
        {
            count += sector.enemies;
        }
        enemies = new SpriteRenderer[count];
        collisionUntil = new float[count];
        for (int i = 0; i < count; i++)
        {
            var aircraft = new GameObject($"Remote patrol {i + 1}");
            aircraft.transform.SetParent(worldRoot.transform, false);
            var sprite = aircraft.AddComponent<SpriteRenderer>();
            sprite.sprite = layout.EnemyAircraft.BodySprite;
            sprite.sortingOrder = 12;
            sprite.enabled = false;
            enemies[i] = sprite;
        }
    }

    public void ReceivePoses(double time, Vector3[] poses)
    {
        if (poses == null || poses.Length != enemies.Length ||
            (snapshots.Count > 0 && time <= snapshots[snapshots.Count - 1].Time))
        {
            return;
        }
        if (snapshots.Count == 16)
        {
            snapshots.RemoveAt(0);
        }
        snapshots.Add((time, poses));
    }

    public void Collision(Vector2 position, int enemyIndex, bool local)
    {
        effects.Impact(new Vec2(position.x, position.y));
        if (enemyIndex >= 0 && enemyIndex < collisionUntil.Length)
        {
            collisionUntil[enemyIndex] = Time.unscaledTime + PatrolMissionEffects.CollisionFeedbackSeconds;
        }
        if (local)
        {
            Feedback("机身碰撞  ·  机体 -1", true);
        }
    }

    public void Present(NetworkRoundPhase current, BattleNetworkPlayer local, IList<int> enemyHealth,
        IList<int> sectors, IList<int> pickups, bool won, float elapsed)
    {
        bool canReturn = NetworkManager.singleton is DogfightNetworkManager manager && manager.IsLocalRoomOwner;
        bool permissionChanged = canReturn != resultCanReturn;
        resultCanReturn = canReturn;
        bool phaseChanged = phase != current;
        phase = current;
        if (current == NetworkRoundPhase.Fighting && Application.isFocused)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (menuOpen)
                {
                    CloseMenu();
                }
                else
                {
                    menuOpen = true;
                    hud.ShowNetworkMenu(CloseMenu);
                }
            }
            if (!menuOpen && Input.GetKeyDown(KeyCode.Tab))
            {
                hud.ToggleChart();
            }
        }
        if (phaseChanged && current == NetworkRoundPhase.Fighting)
        {
            CloseMenu();
        }

        RenderPatrols(enemyHealth);
        effects.Present();
        if (feedbackUntil > 0f && Time.unscaledTime >= feedbackUntil)
        {
            feedbackUntil = 0f;
            hud.PresentFeedback("", false);
        }
        hud.PresentIntro(elapsed);
        if (local != null)
        {
            Vector3 position = local.RenderPosition;
            hud.PresentPosition(layout.ChartPosition(new Vec2(position.x, position.y)));
            hud.PresentBoost(local.BoostEnergy01);
        }
        if (!phaseChanged && !permissionChanged && Time.unscaledTime < nextStatusUpdate)
        {
            return;
        }
        nextStatusUpdate = Time.unscaledTime + .1f;

        int remaining = 0, cleared = 0;
        for (int i = 0; i < sectors.Count; i++)
        {
            remaining += sectors[i];
            if (sectors[i] == 0)
            {
                cleared++;
            }
            hud.PresentSector(i, sectors[i]);
        }
        int collected = 0;
        for (int i = 0; i < pickups.Count; i++)
        {
            if (pickups[i] > 0)
            {
                collected++;
            }
        }
        for (int i = 0; i < pickups.Count; i++)
        {
            int seat = pickups[i];
            string collector = seat == 0 ? null : local != null && local.Seat == seat ? "你拾取" : $"队友 {seat} 号拾取";
            if (seat > 0 && !records[i])
            {
                records[i] = true;
                effects.Collect(i);
                hud.ShowPickup($"{collector}  {layout.Pickups[i].name}  {collected}/{records.Length}");
            }
            hud.PresentCollection(i, layout.Pickups[i].name, seat > 0, collected, collector);
        }
        string alert = local == null ? "" : local.Health <= 0 ? "已击毁 · 等待队友完成任务" :
            local.PatrolContact == PatrolContact.Confirmed ? "敌机已发现你" :
            local.PatrolContact == PatrolContact.Lost ? "敌机正在搜索" :
            local.PatrolContact == PatrolContact.Suspicious ? "敌机起疑" : "";
        string radar = RemainingBearing(local, enemyHealth, remaining);
        hud.PresentStatus(remaining, enemies.Length, cleared, sectors.Count,
            local != null ? local.Health : 0, layout.PlayerHealth, local != null ? local.CannonReady01 : 1f,
            "队伍目标 · 清剿全部巡逻机", alert, radar);
        if ((phaseChanged || permissionChanged) && current == NetworkRoundPhase.Finished)
        {
            hud.ShowResult(won, enemies.Length - remaining, enemies.Length, elapsed, collected,
                records.Length, canReturn);
        }
    }

    string RemainingBearing(BattleNetworkPlayer local, IList<int> enemyHealth, int remaining)
    {
        if (remaining != 1 || local == null || snapshots.Count == 0)
        {
            return "";
        }
        for (int i = 0; i < enemyHealth.Count; i++)
        {
            if (enemyHealth[i] > 0)
            {
                Vector2 relative = (Vector2)(snapshots[snapshots.Count - 1].Poses[i] - local.RenderPosition);
                int direction = Mathf.RoundToInt(Mathf.Repeat(Mathf.Atan2(relative.x, relative.y) * Mathf.Rad2Deg, 360f) / 45f) % 8;
                return $"末架回波  {Bearings[direction]} · 约 {Mathf.Max(5, Mathf.RoundToInt(relative.magnitude / 5f) * 5)}";
            }
        }
        return "";
    }

    void RenderPatrols(IList<int> health)
    {
        if (snapshots.Count == 0)
        {
            return;
        }
        double now = NetworkTime.time;
        // Remote clients already use Mirror's buffered server timeline; host needs its own short buffer.
        double target = NetworkServer.active ? now - .1 : now;
        while (snapshots.Count > 1 && snapshots[1].Time <= target)
        {
            snapshots.RemoveAt(0);
        }
        var from = snapshots[0];
        var to = snapshots[Math.Min(1, snapshots.Count - 1)];
        float alpha = Mathf.Clamp01((float)((target - from.Time) / Math.Max(.001, to.Time - from.Time)));
        for (int i = 0; i < enemies.Length; i++)
        {
            var sprite = enemies[i];
            sprite.enabled = i < health.Count && health[i] > 0;
            if (!sprite.enabled)
            {
                continue;
            }
            Vector3 point = Vector3.Lerp(from.Poses[i], to.Poses[i], alpha);
            float angle = Mathf.LerpAngle(from.Poses[i].z, to.Poses[i].z, alpha);
            sprite.transform.SetPositionAndRotation(new Vector3(point.x, point.y, 0),
                Quaternion.Euler(0, 0, angle - layout.EnemyAircraft.VisualForwardOffsetDeg));
            sprite.color = PatrolMissionEffects.CollisionTint(Color.white, collisionUntil[i] - Time.unscaledTime);
        }
    }

    void Feedback(string message, bool damage)
    {
        hud.PresentFeedback(message, damage);
        feedbackUntil = Time.unscaledTime + 1.6f;
    }

    void CloseMenu()
    {
        menuOpen = false;
        hud.ShowFlight();
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(worldRoot);
        UnityEngine.Object.Destroy(hud.gameObject);
    }
}
