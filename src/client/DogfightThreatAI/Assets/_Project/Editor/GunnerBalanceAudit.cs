using System;
using System.Globalization;
using System.IO;
using System.Text;
using DogfightThreatAI.Core.AI;
using DogfightThreatAI.Core.Combat;
using DogfightThreatAI.Core.Flight;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using DogfightThreatAI.Gameplay.Config;
using UnityEditor;
using UnityEngine;

namespace DogfightThreatAI.EditorTools
{
    /// <summary>固定步、固定航线的射击基线。只评估基础反应，不替代真人试玩。</summary>
    public static class GunnerBalanceAudit
    {
        [MenuItem("Tools/Dogfight Threat AI/Audit Gunner Difficulties")]
        public static void Run()
        {
            var cannon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/_Project/Settings/Weapons/Cannon.asset").ToSpec();
            var bounds = Bounds2D.Centered(new Vec2(200, 150)); var grid = new Grid2D(bounds, 4);
            var csv = new StringBuilder("level,route,shots,hits,searches,min_distance\n");
            for (int level = 1; level <= 4; level++)
            for (int route = 0; route < 3; route++)
            {
                var data = AssetDatabase.LoadAssetAtPath<GunnerProfileData>("Assets/_Project/Settings/AI/Gunner" + level + ".asset");
                var pilot = new GunnerPilot(grid, cannon, data.ToSpec());
                var model = new FlightModel(2, 12, 18, 240, 0, 6, .35f, bounds);
                var simulation = new ProjectileCombatSimulation(cannon, 1000, .7f);
                var self = FlightState.At(new Vec2(25, 0), MathF.PI);
                var player = FlightState.At(new Vec2(-60, 0));
                int shots = 0; float minimum = float.MaxValue;
                for (int step = 0; step < 3000; step++)
                {
                    float t = step * .02f;
                    var previousPlayer = player; var previousSelf = self;
                    player.Heading = route == 0 ? 0 : route == 1 ? t * .75f : MathF.Sin(t * .55f) * 1.4f;
                    player.Velocity = player.Forward * 9;
                    player.Position = bounds.Clamp(player.Position + player.Velocity * .02f);
                    player.Velocity = (player.Position - previousPlayer.Position) / .02f;
                    var input = pilot.Tick(self, player, true, .02f);
                    model.Step(ref self, input, .02f, pilot.MotionCommand);
                    simulation.Tick(.02f, previousPlayer, player, previousSelf, self, .5f, .5f, false, pilot.WantsFire);
                    for (int i = 0; i < simulation.Events.Count; i++) if (simulation.Events[i].Kind == WeaponEventKind.Fired) shots++;
                    minimum = MathF.Min(minimum, Vec2.Distance(player.Position, self.Position));
                }
                csv.Append(level).Append(',').Append(route).Append(',').Append(shots).Append(',')
                    .Append(simulation.Status.HitSequence).Append(',').Append(pilot.SearchCount).Append(',')
                    .Append(minimum.ToString("F2", CultureInfo.InvariantCulture)).Append('\n');
            }
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../out"));
            Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "gunner-balance.csv"), csv.ToString());
            // 本机团结为新 meta 写入编码 GUID；导出 Unity 可携带的原始标识用于交付校验。
            var identifiers = new StringBuilder();
            foreach (string path in AssetDatabase.GetAllAssetPaths())
                if (path.StartsWith("Assets/_Project/", StringComparison.Ordinal))
                    identifiers.Append(path).Append('\t').Append(AssetDatabase.AssetPathToGUID(path)).Append('\n');
            File.WriteAllText(Path.Combine(directory, "asset-guids.tsv"), identifiers.ToString());
            Debug.Log("GUNNER_BALANCE_AUDIT_OK\n" + csv);
        }
    }
}
