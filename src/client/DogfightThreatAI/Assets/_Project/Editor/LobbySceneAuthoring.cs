using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DogfightThreatAI.Gameplay;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.Lobby;
using DogfightThreatAI.UI.Lobby;
using DogfightThreatAI.UI.Menu;
using DogfightThreatAI.UI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace DogfightThreatAI.Editor
{
    /// <summary>通过 Unity 序列化生成可继续编辑的大厅场景、Tilemap 和独立配置。</summary>
    public static class LobbySceneAuthoring
    {
        const string Settings = "Assets/_Project/Settings/Lobby";
        const string Atlas = "Assets/_Project/Art/Environment/Third_Party/Tilemap/tiles_packed.png";
        const int Columns = 64;
        const int Rows = 36;
        const float CellSize = .5f;
        static Scene _generatedScene;

        // 通过 Unity 构建大厅，并在结束或异常时恢复原活动场景。
        [MenuItem("Tools/Dogfight Threat AI/构建机库大厅")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }            // 批处理启动时可能只有一个临时空场景，先建立已保存的场景上下文。
            if (Application.isBatchMode && string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                EditorSceneManager.OpenScene(SceneNavigation.StartMenuScenePath, OpenSceneMode.Single);
            }
            var active = SceneManager.GetActiveScene();
            var handles = new HashSet<int>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                handles.Add(SceneManager.GetSceneAt(i).handle);
            }
            try
            {
                BuildContent();
            }
            finally
            {
                for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!handles.Contains(scene.handle) &&
                        (scene == _generatedScene || scene.path == SceneNavigation.StartMenuScenePath))
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
                if (active.IsValid() && active.isLoaded)
                {
                    SceneManager.SetActiveScene(active);
                }
            }
        }

        // 生成大厅配置、原生 Tilemap、停机位、HUD 和菜单入口。
        static void BuildContent()
        {
            if (EditorApplication.isPlaying)
            {
                return;
            }
            var previousScene = SceneManager.GetActiveScene();
            if (SceneManager.GetSceneByPath(SceneNavigation.LobbyScenePath).isLoaded)
            {
                throw new InvalidOperationException("请先关闭已打开的 LobbyScene，再执行重建，以保留场景中的手动编辑。");
            }
            Directory.CreateDirectory(Settings + "/Tiles");
            Directory.CreateDirectory(Path.GetDirectoryName(SceneNavigation.LobbyScenePath));
            AssetDatabase.Refresh();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(Atlas).OfType<Sprite>()
                .ToDictionary(sprite => sprite.name, sprite => sprite);
            if (!sprites.ContainsKey("tile_0050"))
            {
                throw new InvalidOperationException("Terrain atlas is not sliced.");
            }
            var aircraft = CreateAircraft();
            var data = LoadOrCreate<LobbyData>(Settings + "/Lobby.asset");
            var config = new SerializedObject(data);
            config.FindProperty("aircraft").objectReferenceValue = aircraft;
            config.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(data);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            _generatedScene = scene;
            SceneManager.SetActiveScene(scene);
            var camera = new GameObject("Lobby Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 9;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(43, 58, 42, 255);
            var fit = camera.gameObject.AddComponent<LobbyCamera>();
            Reference(fit, "data", data);

            var terrainMaterial = CreateTerrainMaterial();
            BuildTerrain(sprites, terrainMaterial);
            BuildPerimeter(sprites, terrainMaterial);
            BuildParking(data, sprites["tile_0050"], terrainMaterial);
            var hut = new GameObject("Utility hut", typeof(SpriteRenderer)).GetComponent<SpriteRenderer>();
            hut.sprite = sprites["tile_0072"];
            hut.transform.position = new Vector3(-12, 0, 0);
            hut.transform.localScale = Vector3.one * 4;
            hut.sortingOrder = -5;
            hut.color = new Color(.83f, .85f, .78f);
            hut.sharedMaterial = terrainMaterial;

            var spawns = new GameObject("Spawn points (configuration markers)");
            for (int i = 0; i < data.Capacity; i++)
            {
                var point = data.GetSpawnPoint(i);
                var marker = new GameObject("Spawn " + (i + 1).ToString("00"));
                marker.transform.SetParent(spawns.transform, false);
                marker.transform.position = new Vector3(point.X, point.Y, 0);
            }
            var expansion = new GameObject("Expansion anchors");
            var portal = new GameObject("North entrance (reserved)");
            portal.transform.SetParent(expansion.transform, false);
            portal.transform.position = new Vector3(0, 6.5f, 0);

            var visual = new GameObject("Local aircraft", typeof(SpriteRenderer), typeof(PlayerView));
            var renderer = visual.GetComponent<SpriteRenderer>();
            renderer.sprite = aircraft.BodySprite;
            renderer.sortingOrder = 10;
            visual.transform.localScale = Vector3.one * (data.AircraftWidth / aircraft.BodySprite.bounds.size.x);
            var spawn = data.GetSpawnPoint(0);
            visual.transform.position = new Vector3(spawn.X, spawn.Y, 0);
            var view = visual.GetComponent<PlayerView>();
            Reference(view, "spriteRenderer", renderer);

            var hud = new GameObject("Lobby HUD", typeof(RectTransform)).AddComponent<LobbyHudView>();
            hud.BuildLayout();
            hud.Present(1, data.Capacity);
            hud.PlaceLocalMarker(camera.WorldToViewportPoint(visual.transform.position));
            var controller = new GameObject("Lobby local driver").AddComponent<LobbyController>();
            Reference(controller, "data", data);
            Reference(controller, "aircraftView", view);
            Reference(controller, "hud", hud);
            Reference(controller, "lobbyCamera", camera);
            EditorSceneManager.SaveScene(scene, SceneNavigation.LobbyScenePath);

            // 保持现有 StartMenu / GameScene 的顺序，在最后追加大厅。
            var scenes = EditorBuildSettings.scenes.ToList();
            int index = scenes.FindIndex(entry => entry.path == SceneNavigation.LobbyScenePath);
            if (index < 0)
            {
                scenes.Add(new EditorBuildSettingsScene(SceneNavigation.LobbyScenePath, true));
            }
            else
            {
                scenes[index] = new EditorBuildSettingsScene(SceneNavigation.LobbyScenePath, true);
            }
            EditorBuildSettings.scenes = scenes.ToArray();

            var menuScene = SceneManager.GetSceneByPath(SceneNavigation.StartMenuScenePath);
            bool menuWasOpen = menuScene.isLoaded;
            if (!menuWasOpen)
            {
                menuScene = EditorSceneManager.OpenScene(
                SceneNavigation.StartMenuScenePath,
                OpenSceneMode.Additive);
            }
            var menu = menuScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MainMenuController>(true)).FirstOrDefault();
            if (menu == null)
            {
                throw new InvalidOperationException("StartMenu controller is missing.");
            }
            menu.BuildEditorLayout();
            EditorUtility.SetDirty(menu);
            EditorSceneManager.MarkSceneDirty(menuScene);
            EditorSceneManager.SaveScene(menuScene);
            AssetDatabase.SaveAssets();
            SavePreview(scene, camera, hud);
            if (!menuWasOpen)
            {
                EditorSceneManager.CloseScene(menuScene, true);
            }
            EditorSceneManager.CloseScene(scene, true);
            if (previousScene.IsValid() && previousScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousScene);
            }
            Debug.Log("LOBBY_BUILT: local movable lobby; 64x36 tiles, four spawn points; no network transport installed.");
        }

        // 临时切换渲染配置，从真实场景输出检查图后恢复现场。
        static void SavePreview(Scene scene, Camera camera, LobbyHudView hud)
        {
            // 从真实场景渲染设计检查图；临时隐藏其他场景的对象，渲染后恢复。
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return;
            }
            var hidden = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var other = SceneManager.GetSceneAt(i);
                if (other == scene || !other.isLoaded)
                {
                    continue;
                }
                foreach (var root in other.GetRootGameObjects())
                {
                    if (root.activeSelf)
                    {
                        hidden.Add(root);
                        root.SetActive(false);
                    }
                }
            }
            var canvas = hud.GetComponent<Canvas>();
            var image = new RenderTexture(1920, 1080, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = image;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                var aircraft = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PlayerView>()).First();
                hud.PlaceLocalMarker(camera.WorldToViewportPoint(aircraft.transform.position));
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = image;
                var pixels = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                pixels.Apply();
                string folder = Path.GetFullPath("Temp/LobbyPreview");
                Directory.CreateDirectory(folder);
                File.WriteAllBytes(Path.Combine(folder, "lobby-preview.png"), pixels.EncodeToPNG());
                Object.DestroyImmediate(pixels);
            }
            finally
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.worldCamera = null;
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(image);
                foreach (var root in hidden)
                {
                    if (root != null)
                    {
                        root.SetActive(true);
                    }
                }
            }
        }

        // 创建或更新独立的大厅机型，不修改战斗机型资产。
        static AircraftData CreateAircraft()
        {
            var aircraft = AssetDatabase.LoadAssetAtPath<AircraftData>(Settings + "/LobbyAircraft.asset");
            if (aircraft == null)
            {
                aircraft = ScriptableObject.CreateInstance<AircraftData>();
                AssetDatabase.CreateAsset(aircraft, Settings + "/LobbyAircraft.asset");
            }
            var values = new SerializedObject(aircraft);
            values.FindProperty("displayName").stringValue = "大厅飞机";
            values.FindProperty("bodySprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/_Project/Art/Characters/Third_Party_Ships/ship_0000.png");
            values.FindProperty("minSpeed").floatValue = 0;
            values.FindProperty("maxSpeed").floatValue = 6;
            values.FindProperty("acceleration").floatValue = 12;
            values.FindProperty("turnRateDegPerSec").floatValue = 240;
            values.FindProperty("drag").floatValue = 2;
            values.FindProperty("lateralGrip").floatValue = 12;
            values.FindProperty("driftScrub").floatValue = .15f;
            values.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(aircraft);
            return aircraft;
        }

        // 建立大厅专用的低饱和地形材质。
        static Material CreateTerrainMaterial()
        {
            string path = Settings + "/Terrain.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("DogfightThreatAI/LobbyTerrain");
            if (shader == null)
            {
                throw new InvalidOperationException("LobbyTerrain shader is missing.");
            }
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetFloat("_Saturation", .38f);
            material.SetFloat("_Brightness", .82f);
            EditorUtility.SetDirty(material);
            return material;
        }

        // 在地图边缘布置树木，并为北侧扩展入口留空。
        static void BuildPerimeter(Dictionary<string, Sprite> sprites, Material material)
        {
            var root = new GameObject("Perimeter trees");
            for (int y = 0; y < Rows; y += 2)
            {
                for (int x = 0; x < Columns; x += 2)
                {
                    bool edge = x < 4 || x >= Columns - 4 || y < 3 || y >= Rows - 3;
                    if (!edge || (x >= 29 && x <= 34 && y > Rows - 5))
                    {
                        continue;
                    }
                    int hash = (x * 37 + y * 71) % 101;
                    if (hash < 12)
                    {
                        continue;
                    }
                    var tree = new GameObject("Tree " + x + "-" + y, typeof(SpriteRenderer));
                    tree.transform.SetParent(root.transform, false);
                    tree.transform.position = new Vector3(-16 + (x + .6f) * CellSize, -9 + (y + .8f) * CellSize, 0);
                    tree.transform.localScale = Vector3.one * (2.1f + hash * .008f);
                    var renderer = tree.GetComponent<SpriteRenderer>();
                    renderer.sprite = sprites[hash % 2 == 0 ? "tile_0048" : "tile_0060"];
                    renderer.sharedMaterial = material;
                    renderer.sortingOrder = -20;
                    renderer.color = new Color(.82f, .91f, .84f);
                }
            }
        }

        // 把默认大厅布局保存为可继续编辑的原生 Tilemap。
        static void BuildTerrain(Dictionary<string, Sprite> sprites, Material material)
        {
            var root = new GameObject("Lobby map", typeof(Grid));
            root.GetComponent<Grid>().cellSize = new Vector3(CellSize, CellSize, 1);
            root.transform.position = new Vector3(-Columns * CellSize * .5f, -Rows * CellSize * .5f, 0);
            var ground = new GameObject("Ground (editable Tilemap)", typeof(Tilemap), typeof(TilemapRenderer));
            ground.transform.SetParent(root.transform, false);
            var terrainRenderer = ground.GetComponent<TilemapRenderer>();
            terrainRenderer.sortingOrder = -50;
            terrainRenderer.sharedMaterial = material;
            var map = ground.GetComponent<Tilemap>();
            map.color = new Color(.86f, .92f, .87f);
            var tiles = new Dictionary<int, Tile>();
            foreach (int id in new[] { 50, 110, 48, 60, 36, 56 })
            {
                var tile = LoadOrCreate<Tile>(Settings + "/Tiles/Tile_" + id.ToString("0000") + ".asset");
                tile.sprite = sprites["tile_" + id.ToString("0000")];
                tile.colliderType = Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
                tiles.Add(id, tile);
            }
            var cells = new TileBase[Columns * Rows];
            for (int y = 0; y < Rows; y++)
            {
                for (int x = 0; x < Columns; x++)
                {
                    int hash = (x * 73 + y * 137 + x * y * 17) % 101;
                    bool path = (x >= 6 && x <= 57 && (y == 8 || y == 9 || y == 28 || y == 29)) ||
                        (y >= 9 && y <= 28 && (x == 6 || x == 7 || x == 56 || x == 57)) ||
                        (x >= 31 && x <= 32 && y >= 28) || (y == 18 && x >= 3 && x <= 6);
                    int id = hash < 18 ? 110 : 50;
                    if (path)
                    {
                        id = 56;
                    }
                    cells[y * Columns + x] = tiles[id];
                }
            }
            map.SetTilesBlock(new BoundsInt(0, 0, 0, Columns, Rows, 1), cells);
        }

        // 按出生点配置创建停机位底色与角标。
        static void BuildParking(LobbyData data, Sprite groundSprite, Material terrainMaterial)
        {
            string path = Settings + "/Markings.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, path);
            }
            var root = new GameObject("Parking pads");
            for (int i = 0; i < data.Capacity; i++)
            {
                var spawn = data.GetSpawnPoint(i);
                var pad = new GameObject("Pad " + (i + 1).ToString("00"), typeof(SpriteRenderer));
                pad.transform.SetParent(root.transform, false);
                pad.transform.position = new Vector3(spawn.X, spawn.Y, 0);
                pad.transform.localScale = new Vector3(4.2f, 3.2f, 1);
                var renderer = pad.GetComponent<SpriteRenderer>();
                renderer.sprite = groundSprite;
                renderer.sharedMaterial = terrainMaterial;
                renderer.color = new Color(.58f, .61f, .52f);
                renderer.sortingOrder = -40;
                for (int corner = 0; corner < 4; corner++)
                {
                    float sx = corner % 2 == 0 ? -1 : 1;
                    float sy = corner / 2 == 0 ? -1 : 1;
                    var line = new GameObject("Corner " + corner).AddComponent<LineRenderer>();
                    line.transform.SetParent(root.transform, false);
                    line.sharedMaterial = material;
                    line.useWorldSpace = true;
                    line.positionCount = 3;
                    line.SetPositions(new[] { new Vector3(spawn.X + sx * .7f, spawn.Y + sy * .67f, 0),
                        new Vector3(spawn.X + sx * .95f, spawn.Y + sy * .67f, 0),
                        new Vector3(spawn.X + sx * .95f, spawn.Y + sy * .42f, 0) });
                    line.startWidth = line.endWidth = .045f;
                    line.startColor = line.endColor = new Color(.88f, .87f, .77f, .85f);
                    line.sortingOrder = -30;
                }
            }
        }

        // 复用已有内容资产，缺失时通过 Unity 创建并保存。
        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // 通过序列化字段写入场景和资产引用。
        static void Reference(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
