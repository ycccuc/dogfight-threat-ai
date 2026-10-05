using System.Text;
using Dogfight.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dogfight.EditorTools
{
    /// <summary>
    /// 用代码搭建 / 刷新 `Battle` 场景。
    ///
    /// 为什么场景要用代码搭，而不是在编辑器里手拖：
    ///   `.unity` 是塞满 GUID 的 YAML —— 人肉 review 基本读不懂，两个人同时改也没法合。
    ///   用代码搭之后：场景结构能被 review、能重复执行、换素材后能一键重建，
    ///   而且"新同学 clone 下来该看到什么"变成了可执行的代码而不是口口相传。
    ///
    /// 幂等：按名字查找，存在就更新、不存在才创建；**不会删除你手动加的东西**。
    /// 用法：菜单 `Dogfight → 搭建或刷新 Battle 场景`。
    /// </summary>
    public static class BattleSceneBuilder
    {
        const string ScenePath = "Assets/_Project/Scenes/Battle.unity";

        /// <summary>与 docs/02-design/美术规范.md 一致：720 ÷ 2 ÷ 32 = 11.25。</summary>
        const float CameraOrthographicSize = 11.25f;

        const float PlayerLinearDrag = 0.8f;
        const float PlaneColliderRadius = 0.4f;

        [MenuItem("Dogfight/搭建或刷新 Battle 场景", priority = 10)]
        public static void BuildOrRefresh()
        {
            // 有未保存改动就先问，避免把别人的工作冲掉
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[场景搭建] 已取消（当前场景有未保存改动）。");
                return;
            }

            // 依赖项：排序层必须先存在，否则下面设 sortingLayerName 会静默失败
            DogfightProjectSetup.EnsureSortingLayers();
            DogfightProjectSetup.EnsureTopDownGravity();

            bool isNew = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null;
            Scene scene = isNew
                ? EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single)
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var report = new StringBuilder();
            report.AppendLine("[场景搭建] " + (isNew ? "新建" : "刷新") + " " + ScenePath);

            BuildCamera(scene, report);

            GameObject loopObject = FindOrCreate(scene, "MatchLoop", typeof(MatchLoop));
            if (isNew) report.AppendLine("  + MatchLoop");

            GameObject player = FindOrCreate(scene, "Plane_Player",
                typeof(SpriteRenderer), typeof(Rigidbody2D), typeof(CircleCollider2D), typeof(PlaneAgent));
            ConfigurePlane(player, agentId: 0, team: 0, report);

            EnsureParallaxLayer(scene, "BG_Background", "Background", 0.15f, report);
            EnsureParallaxLayer(scene, "BG_Far", "Far", 0.35f, report);
            EnsureParallaxLayer(scene, "BG_Mid", "Mid", 0.6f, report);

            // 把玩家机接进对局循环（这一步让 MatchLoop 能判定结束、能施加边界回推）
            var loop = loopObject.GetComponent<MatchLoop>();
            loop.SetPlanes(new[] { player.GetComponent<PlaneAgent>() });

            // 相机跟随的目标由 CameraFollow 自己在 Awake 里找，这里不用连
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();

            report.AppendLine("  ✓ 已保存。按 Play 即可试飞。");
            Debug.Log(report.ToString());
        }

        static void BuildCamera(Scene scene, StringBuilder report)
        {
            GameObject cameraObject = FindInScene(scene, "Main Camera") ?? FindInScene(scene, "Camera");
            if (cameraObject == null)
            {
                cameraObject = new GameObject("Main Camera");
                report.AppendLine("  + Main Camera");
            }

            var camera = cameraObject.GetComponent<Camera>();
            if (camera == null) camera = cameraObject.AddComponent<Camera>();

            camera.orthographic = true;
            camera.orthographicSize = CameraOrthographicSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.05f, 0.07f, 0.12f, 1f);
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            cameraObject.tag = "MainCamera";

            if (cameraObject.GetComponent<AudioListener>() == null &&
                Object.FindObjectOfType<AudioListener>() == null)
            {
                cameraObject.AddComponent<AudioListener>();
            }

            if (cameraObject.GetComponent<CameraFollow>() == null)
            {
                cameraObject.AddComponent<CameraFollow>();
                report.AppendLine("  + CameraFollow（挂在相机上）");
            }
        }

        static void ConfigurePlane(GameObject plane, int agentId, int team, StringBuilder report)
        {
            var body = plane.GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.drag = PlayerLinearDrag;
            body.angularDrag = 0f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var collider = plane.GetComponent<CircleCollider2D>();
            collider.radius = PlaneColliderRadius;

            var renderer = plane.GetComponent<SpriteRenderer>();
            renderer.sortingLayerName = "Units";
            renderer.sortingOrder = team == 0 ? 0 : 10;

            var agent = plane.GetComponent<PlaneAgent>();
            // 飞行参数留空 → PlaneAgent 会用 FlightSpec.Default（与暑期原型等价的数值）。
            // 想调参就 Create → Dogfight → 飞行参数 建一个资产再拖上去。
            agent.ConfigureForSetup(agentId, team, playerControlled: team == 0, profile: null);

            if (renderer.sprite == null)
            {
                report.AppendLine("  ! Plane_Player 还没有贴图：把 ship_0000.png 拖到它的 SpriteRenderer.sprite 上");
            }
        }

        static void EnsureParallaxLayer(Scene scene, string name, string sortingLayer, float factor, StringBuilder report)
        {
            bool existed = FindInScene(scene, name) != null;
            GameObject layerObject = FindOrCreate(scene, name, typeof(SpriteRenderer), typeof(ParallaxLayer));

            var renderer = layerObject.GetComponent<SpriteRenderer>();
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = 0;

            layerObject.GetComponent<ParallaxLayer>().Configure(factor, sortingLayer, 0, wrapX: true);

            if (!existed) report.AppendLine("  + " + name + "（层 " + sortingLayer + "，视差系数 " + factor + "）");
            if (renderer.sprite == null)
            {
                report.AppendLine("  ! " + name + " 还没有贴图（背景素材见 docs/素材台账.md 待补清单）");
            }
        }

        // ───────────────────────── 查找 / 创建 ─────────────────────────

        static GameObject FindInScene(Scene scene, string name)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (roots[i].name == name) return roots[i];

                Transform child = roots[i].transform.Find(name);
                if (child != null) return child.gameObject;
            }
            return null;
        }

        static GameObject FindOrCreate(Scene scene, string name, params System.Type[] components)
        {
            GameObject target = FindInScene(scene, name);
            if (target == null) target = new GameObject(name);

            for (int i = 0; i < components.Length; i++)
            {
                if (target.GetComponent(components[i]) == null) target.AddComponent(components[i]);
            }
            return target;
        }
    }
}
