using System.Linq;
using System.Reflection;
using DogfightThreatAI.Gameplay.Config;
using DogfightThreatAI.Gameplay.View;
using DogfightThreatAI.Gameplay.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class BattleMapTests
    {
        const string MapPath = "Assets/_Project/Settings/Maps/RiverValley.asset";

        [Test]
        public void RiverValley_AllCellsHaveValidSprites_AndMatchExistingArena()
        {
            var map = AssetDatabase.LoadAssetAtPath<BattleMapData>(MapPath);
            Assert.That(map, Is.Not.Null);
            Assert.That(map.IsValid, Is.True);
            Assert.That(map.WorldSize, Is.EqualTo(new Vector2(400, 300)));
            var bytes = map.Layout.bytes;
            foreach (int index in bytes.Distinct())
            {
                Assert.That(index, Is.LessThan(map.Palette.Length));
                Assert.That(map.Palette[index], Is.Not.Null, "Missing tile " + index);
                Assert.That(map.Palette[index].pixelsPerUnit, Is.EqualTo(32));
                Assert.That(map.Palette[index].texture.filterMode, Is.EqualTo(FilterMode.Point));
            }
            Assert.That(bytes, Does.Contain((byte)42), "River");
            Assert.That(bytes, Does.Contain((byte)109), "Farmland");
            Assert.That(bytes, Does.Contain((byte)72), "Village");
        }

        [Test]
        public void MapStreaming_CoversCameraAtSpawnChunkCrossingsAndWorldCorners()
        {
            var go = new GameObject("Map streaming test");
            var cameraObject = new GameObject("Map test camera", typeof(Camera));
            try
            {
                var cam = cameraObject.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 5;
                cam.aspect = 16f / 9f;
                var view = go.AddComponent<BattleMapView>();
                var fields = new SerializedObject(view);
                fields.FindProperty("map").objectReferenceValue = AssetDatabase.LoadAssetAtPath<BattleMapData>(MapPath);
                fields.FindProperty("targetCamera").objectReferenceValue = cam;
                fields.ApplyModifiedPropertiesWithoutUndo();
                view.Rebuild();
                var positions = new[] { Vector2.zero, new Vector2(8, 12), new Vector2(16, 16),
                    new Vector2(-191, -145), new Vector2(191, -145),
                    new Vector2(-191, 145), new Vector2(191, 145), Vector2.zero };
                foreach (var position in positions)
                {
                    cameraObject.transform.position = new Vector3(position.x, position.y, -10);
                    view.RefreshVisibleTiles();
                    var chunks = view.GetComponentsInChildren<Tilemap>();
                    Assert.That(view.LoadedChunkCount, Is.InRange(1, 9));
                    Assert.That(chunks.Length, Is.EqualTo(view.LoadedChunkCount));
                    for (float y = -4.9f; y <= 4.9f; y += .48f)
                        for (float x = -8.8f; x <= 8.8f; x += .48f)
                        {
                            var world = new Vector3(position.x + x, position.y + y);
                            Assert.That(chunks.Any(chunk => chunk.HasTile(chunk.WorldToCell(world))), Is.True,
                                "Uncovered map at " + world);
                        }
                    Assert.That(go.GetComponentsInChildren<TilemapCollider2D>(), Is.Empty);
                    Assert.That(chunks.All(c => c.GetComponent<TilemapRenderer>().sortingOrder < 0), Is.True);
                }
                view.enabled = false;
                Assert.That(go.transform.childCount, Is.Zero, "Generated chunks must be cleaned up");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void MapStreaming_CoversExpandedCameraCornersWithFiniteNonCollidingEdgePadding()
        {
            var go = new GameObject("Expanded camera map test");
            var cameraObject = new GameObject("Expanded map camera", typeof(Camera), typeof(CameraFollow));
            try
            {
                var map = AssetDatabase.LoadAssetAtPath<BattleMapData>(MapPath);
                var originalLayout = map.Layout.bytes;
                var cam = cameraObject.GetComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 5;
                cam.aspect = 16f / 9f;
                var follow = cameraObject.GetComponent<CameraFollow>();
                follow.SetWorldHalfSize(new Vector2(201.2f, 151.2f));
                var clamp = typeof(CameraFollow).GetMethod("ClampInsideWorld", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(clamp, Is.Not.Null);

                var view = go.AddComponent<BattleMapView>();
                var fields = new SerializedObject(view);
                fields.FindProperty("map").objectReferenceValue = map;
                fields.FindProperty("targetCamera").objectReferenceValue = cam;
                fields.ApplyModifiedPropertiesWithoutUndo();
                view.Rebuild();

                foreach (int sideY in new[] { -1, 1 })
                    foreach (int sideX in new[] { -1, 1 })
                    {
                        // Use the real follow clamp at the physical arena corner, then sample the full viewport.
                        cameraObject.transform.position = (Vector3)clamp.Invoke(follow,
                            new object[] { new Vector3(sideX * 200f, sideY * 150f, -10f) });
                        view.RefreshVisibleTiles();
                        var chunks = view.GetComponentsInChildren<Tilemap>();
                        Assert.That(view.LoadedChunkCount, Is.InRange(1, 9));
                        Assert.That(chunks.Length, Is.EqualTo(view.LoadedChunkCount));
                        for (int y = 0; y <= 24; y++)
                            for (int x = 0; x <= 40; x++)
                            {
                                var world = cam.ViewportToWorldPoint(new Vector3(x / 40f, y / 24f, 10f));
                                Assert.That(chunks.Any(chunk => chunk.HasTile(chunk.WorldToCell(world))), Is.True,
                                    "Expanded camera exposes an uncovered map edge at " + world);
                            }

                        var edge = new Vector3(sideX * 199.75f, sideY * 149.75f);
                        var padding = new Vector3(sideX * 201.75f, sideY * 151.75f);
                        var edgeTile = chunks.Select(chunk => chunk.GetTile<Tile>(chunk.WorldToCell(edge))).First(tile => tile != null);
                        var paddingTile = chunks.Select(chunk => chunk.GetTile<Tile>(chunk.WorldToCell(padding))).FirstOrDefault(tile => tile != null);
                        Assert.That(paddingTile, Is.Not.Null, "All four padding cells must be drawn.");
                        Assert.That(paddingTile.sprite, Is.EqualTo(edgeTile.sprite), "Padding must repeat the nearest edge terrain.");
                        foreach (var outside in new[] {
                            new Vector3(sideX * 202.01f, sideY * 151.75f),
                            new Vector3(sideX * 201.75f, sideY * 152.01f) })
                            Assert.That(chunks.Any(chunk => chunk.HasTile(chunk.WorldToCell(outside))), Is.False,
                                "Visual padding must stop after four cells at " + outside);
                        Assert.That(go.GetComponentsInChildren<Collider2D>(), Is.Empty);
                        Assert.That(chunks.SelectMany(chunk => chunk.GetUsedTilesCount() == 0
                            ? new TileBase[0] : chunk.GetTilesBlock(chunk.cellBounds)).OfType<Tile>()
                            .All(tile => tile.colliderType == Tile.ColliderType.None), Is.True);
                    }

                Assert.That(map.WorldSize, Is.EqualTo(new Vector2(400, 300)));
                Assert.That(map.Layout.bytes, Is.EqualTo(originalLayout), "Visual padding must not change the source map.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
