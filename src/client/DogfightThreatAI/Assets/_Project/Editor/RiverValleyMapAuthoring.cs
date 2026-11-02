using System;
using System.Collections.Generic;
using System.IO;
using DogfightThreatAI.Gameplay;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DogfightThreatAI.Editor
{
    /// <summary>把选定的河谷农田构图扩展为地图资产，并通过 Unity 序列化接入 Battle。</summary>
    public static class RiverValleyMapAuthoring
    {
        const string ScenePath = "Assets/_Project/Scenes/GameScene/GameScene.unity";
        const string AtlasPath = "Assets/_Project/Art/Environment/Third_Party/Tilemap/tiles_packed.png";
        const string MapFolder = "Assets/_Project/Settings/Maps";
        const int Width = 800, Height = 600;

        [MenuItem("Tools/Dogfight Threat AI/构建河谷农田地图")]
        public static void Build()
        {
            if (EditorApplication.isPlaying) return;
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(MapFolder);
            AssetDatabase.Refresh();
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = 32;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.wrapMode = TextureWrapMode.Clamp;
            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();
            var existingSlices = provider.GetSpriteRects();
            var slices = new SpriteRect[120];
            var nameIdPairs = new List<SpriteNameFileIdPair>();
            for (int i = 0; i < slices.Length; i++)
            {
                string spriteName = "tile_" + i.ToString("0000");
                var existing = Array.Find(existingSlices, slice => slice.name == spriteName);
                slices[i] = new SpriteRect
                {
                    name = spriteName,
                    rect = new Rect(i % 12 * 16, (9 - i / 12) * 16, 16, 16),
                    alignment = SpriteAlignment.Center,
                    spriteID = existing != null ? existing.spriteID : GUID.Generate(),
                    pivot = new Vector2(.5f, .5f)
                };
            }
            foreach (var slice in slices)
            {
                nameIdPairs.Add(new SpriteNameFileIdPair(slice.name, slice.spriteID));
            }
            provider.SetSpriteRects(slices);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>().SetNameFileIdPairs(nameIdPairs);
            provider.Apply();
            importer.SaveAndReimport();
            var palette = new Sprite[120];
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(AtlasPath))
                if (obj is Sprite sprite && int.TryParse(sprite.name.Substring(5), out int index))
                    palette[index] = sprite;
            for (int i = 0; i < palette.Length; i++)
                if (palette[i] == null) throw new InvalidOperationException("Missing terrain tile " + i);

            byte[] layout = new byte[Width * Height];
            for (int y = 0; y < Height; y++)
                for (int x = 0; x < Width; x++)
                    layout[y * Width + x] = (byte)TileAt(x - (Width / 2 - 18), y - (Height / 2 - 10));
            string layoutPath = MapFolder + "/RiverValleyTiles.bytes";
            File.WriteAllBytes(layoutPath, layout);
            AssetDatabase.ImportAsset(layoutPath);
            string dataPath = MapFolder + "/RiverValley.asset";
            var map = AssetDatabase.LoadAssetAtPath<BattleMapData>(dataPath);
            if (map == null)
            {
                map = ScriptableObject.CreateInstance<BattleMapData>();
                AssetDatabase.CreateAsset(map, dataPath);
            }
            var data = new SerializedObject(map);
            data.FindProperty("displayName").stringValue = "河谷农田";
            data.FindProperty("width").intValue = Width;
            data.FindProperty("height").intValue = Height;
            data.FindProperty("tileWorldSize").floatValue = .5f;
            data.FindProperty("layout").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TextAsset>(layoutPath);
            var entries = data.FindProperty("palette");
            entries.arraySize = palette.Length;
            for (int i = 0; i < palette.Length; i++) entries.GetArrayElementAtIndex(i).objectReferenceValue = palette[i];
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(map);

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var starfield in UnityEngine.Object.FindObjectsOfType<DebugStarfield>())
                UnityEngine.Object.DestroyImmediate(starfield.gameObject);
            var view = UnityEngine.Object.FindObjectOfType<BattleMapView>();
            if (view == null) view = new GameObject("River Valley Map").AddComponent<BattleMapView>();
            var viewData = new SerializedObject(view);
            viewData.FindProperty("map").objectReferenceValue = map;
            viewData.FindProperty("targetCamera").objectReferenceValue = Camera.main;
            viewData.ApplyModifiedPropertiesWithoutUndo();
            view.Rebuild();
            var debugGrid = UnityEngine.Object.FindObjectOfType<GridDebugView>();
            if (debugGrid != null)
            {
                var grid = new SerializedObject(debugGrid);
                grid.FindProperty("drawInPlayMode").boolValue = false;
                grid.ApplyModifiedPropertiesWithoutUndo();
            }
            Camera.main.backgroundColor = new Color32(223, 246, 245, 255);
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            EditorUtility.SetDirty(view);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("RIVER_VALLEY_BUILT: 400 x 300 world, 800 x 600 tiles, 32 PPU, Battle scene saved.");
        }

        // Local coordinates preserve option 02's original 36 x 20 composition around spawn.
        static bool Land(int x, int y)
        {
            double distance = Math.Max(0, Math.Abs(y - 10) - 20);
            double broadBend = Math.Min(22, distance * .035) * Math.Sin(y * .045);
            return Math.Abs(x - (18 + 2.6 * Math.Sin(y * .32) + broadBend)) > 3.2;
        }

        static bool Full(int x, int y)
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (!Land(x + dx, y + dy)) return false;
            return true;
        }

        static int Mod(int value, int period) => (value % period + period) % period;
        static double Hash(int x, int y, int seed)
        {
            double value = Math.Sin(x * 127.1 + y * 311.7 + seed * 19.13) * 43758.5453;
            return value - Math.Floor(value);
        }

        static int TileAt(int x, int y)
        {
            if (!Land(x, y))
                return !Land(x - 1, y) && !Land(x + 1, y) && !Land(x, y - 1) &&
                    !Land(x, y + 1) && Hash(x, y, 2) < .018 ? 65 : 42;
            bool n = Land(x, y - 1), e = Land(x + 1, y), s = Land(x, y + 1), w = Land(x - 1, y);
            if (!n && !w) return 37;
            if (!n && !e) return 39;
            if (!s && !w) return 61;
            if (!s && !e) return 63;
            if (!n) return 38;
            if (!s) return 62;
            if (!w) return 49;
            if (!e) return 51;
            if (!Land(x + 1, y + 1)) return 40;
            if (!Land(x - 1, y + 1)) return 41;
            if (!Land(x + 1, y - 1)) return 52;
            if (!Land(x - 1, y - 1)) return 53;
            if (!Full(x, y)) return 50;

            int px = Mod(x, 44), py = Mod(y, 20);
            if (px == 6 && py == 4 || px == 28 && py == 7) return 72;
            if (px == 6 && py == 14 || px == 28 && py == 15) return 84;
            if (px == 6 && py == 5 || px == 28 && py == 8) return 96;
            if (px == 8 && py == 13 || px == 30 && py == 16) return 108;
            if (px >= 9 && px <= 12 && py >= 3 && py <= 6 ||
                px >= 24 && px <= 26 && py >= 12 && py <= 15) return 109;
            if (px == 7 && py == 10) return 86;
            if (px == 7 || px == 29) return 85;
            if (px >= 2 && px < 14 && py == 10) return 98;
            if (Hash(x, y, 6) < .17) return Hash(x, y, 9) < .35 ? 60 : 48;
            return Hash(x, y, 11) < .025 ? 110 : 50;
        }
    }
}
