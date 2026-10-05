using UnityEditor;
using UnityEngine;

namespace Dogfight.EditorTools
{
    /// <summary>
    /// 验证「手动步进物理」在本机 Unity 2022.3 上真的可用。
    ///
    /// 为什么值得单独做一个探针：
    ///   整条「批量对战 / 无头快进」链路都压在 Physics2D.simulationMode = Script
    ///   加 Physics2D.Simulate(step) 这个组合上。它的行为最好实测确认，而不是照文档假设 ——
    ///   如果这台机器上不可用，就把 MatchLoop 的 Manual Physics Step 关掉，退回 Unity 自动物理
    ///   （代价是批量对战只能按真实帧率跑，慢，但仍可用）。
    ///
    /// 用法：进入播放模式 → 菜单 Dogfight → 验证 → 手动步进物理。
    /// 通过标准：一个初速 1 单位/秒、无阻尼无重力的刚体，被手动推 60 步 1/60 秒后
    ///           应该正好移动约 1 个单位。如果几乎没动，说明自动物理仍在接管（或未生效）。
    /// </summary>
    public static class PhysicsStepProbe
    {
        [MenuItem("Dogfight/验证/手动步进物理（需先进入播放模式）", priority = 40)]
        public static void Run()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[物理探针] 请先进入播放模式再运行本验证 —— 编辑模式下物理世界不推进。");
                return;
            }

            var probeObject = new GameObject("~PhysicsStepProbe");
            var body = probeObject.AddComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.drag = 0f;
            body.angularDrag = 0f;
            body.velocity = new Vector2(1f, 0f);

            SimulationMode2D originalMode = Physics2D.simulationMode;
            Vector2 startPosition = body.position;

            const float step = 1f / 60f;
            const int stepCount = 60;
            const float expectedDistance = 1f; // v * t = 1 单位/秒 * 1 秒

            float actualDistance;
            SimulationMode2D modeAfterAssign;

            try
            {
                Physics2D.simulationMode = SimulationMode2D.Script;
                modeAfterAssign = Physics2D.simulationMode;

                for (int i = 0; i < stepCount; i++)
                {
                    Physics2D.Simulate(step);
                }

                actualDistance = body.position.x - startPosition.x;
            }
            finally
            {
                Physics2D.simulationMode = originalMode;
                Object.Destroy(probeObject);
            }

            bool apiAccepted = modeAfterAssign == SimulationMode2D.Script;
            bool moved = actualDistance > expectedDistance * 0.9f;
            bool overshoot = actualDistance > expectedDistance * 1.1f;

            string report =
                "[物理探针] 模拟模式赋值" + (apiAccepted ? "成功" : "失败（仍是 " + modeAfterAssign + "）") +
                " · 期望位移 " + expectedDistance.ToString("0.###") +
                " · 实际位移 " + actualDistance.ToString("0.###");

            if (apiAccepted && moved && !overshoot)
            {
                Debug.Log(report + "\n结论：**手动步进物理可用** → MatchLoop 保持 Manual Physics Step 勾选，批量对战可以快进跑。");
            }
            else if (moved && overshoot)
            {
                Debug.LogWarning(report + "\n结论：位移超过预期 —— 说明自动物理也在跑（双重步进）。请把 MatchLoop 的 Manual Physics Step 关掉，或检查是否有别处在调用 Simulate。");
            }
            else
            {
                Debug.LogError(report + "\n结论：**手动步进物理不可用**。退路：把 MatchLoop 的 Manual Physics Step 取消勾选，回到 Unity 自动物理；批量对战将只能按真实帧率推进。");
            }
        }
    }
}
