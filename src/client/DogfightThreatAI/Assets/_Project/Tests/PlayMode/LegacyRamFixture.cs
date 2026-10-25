using System.Reflection;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Gameplay.Combat;
using DogfightThreatAI.Gameplay.InputSource;
using UnityEngine;

namespace DogfightThreatAI.Tests.PlayMode
{
    /// <summary>
    /// 历史撞击回归显式选择旧机制；正常 GameScene 始终使用机炮。
    /// 测试复用现有地图/飞机接线，不为历史玩法新增发行场景。
    /// </summary>
    internal static class LegacyRamFixture
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static void Activate()
        {
            var battle = Object.FindObjectOfType<BattleCombatController>();
            var presenter = battle.GetComponent<BattleUiPresenter>();
            if (presenter != null) Object.DestroyImmediate(presenter);
            var shots = battle.GetComponent<ProjectileView>();
            if (shots != null) Object.DestroyImmediate(shots);
            for (int i = battle.transform.childCount - 1; i >= 0; i--)
            {
                var child = battle.transform.GetChild(i).gameObject;
                if (child.name == "Battle HUD" || child.name.StartsWith("Cannon round")) Object.DestroyImmediate(child);
            }
            typeof(BattleCombatController).GetField("useProjectileCombat", Private).SetValue(battle, false);
            typeof(BattleCombatController).GetField("_simulation", Private).SetValue(battle, null);
            typeof(BattleCombatController).GetField("_model", Private).SetValue(battle,
                new ContactCombatModel(5, 1, .7f, Mathf.Max(.01f, battle.ContactRadius)));
            var ai = battle.Enemy.GetComponent<ChaseAiInputSource>();
            typeof(ChaseAiInputSource).GetField("_gunner", Private).SetValue(ai, null);
            typeof(ChaseAiInputSource).GetField("_source", Private).SetValue(ai, null);
            ai.SetDifficulty(0);
            var cue = battle.Enemy.GetComponent<AttackCueView>();
            if (cue != null) cue.enabled = true;
        }
    }
}
