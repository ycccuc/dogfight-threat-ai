using System;
using System.Collections.Generic;
using DogfightThreatAI.Core.AI.Pathfinding;
using DogfightThreatAI.Core.Math;
using DogfightThreatAI.Core.World;
using NUnit.Framework;

namespace DogfightThreatAI.Tests.EditMode
{
    public sealed class GridPathfinderTests
    {
        readonly Grid2D _grid = Grid2D.Centered(new Vec2(5, 3), 1);
        [Test] public void BfsFindsShortestUnweightedRouteWhileAStarAvoidsExpensiveCells()
        {
            var search = new GridPathfinder(_grid); var path = new List<int>();
            int start = _grid.ToIndex(0, 1), goal = _grid.ToIndex(4, 1);
            Assert.That(search.BreadthFirst(start, goal, null, path), Is.True); Assert.That(path.Count, Is.EqualTo(5));
            var costs = new float[_grid.CellCount]; for (int i = 0; i < costs.Length; i++) costs[i] = 1;
            costs[_grid.ToIndex(2, 1)] = 20;
            Assert.That(search.AStar(start, goal, null, costs, path), Is.True);
            Assert.That(path.Count, Is.EqualTo(7)); CollectionAssert.DoesNotContain(path, _grid.ToIndex(2, 1));
        }
        [Test] public void BothSearchesRespectWallsAndClearStalePathWhenUnreachable()
        {
            var search = new GridPathfinder(_grid); var path = new List<int>(); var blocked = new bool[_grid.CellCount];
            for (int y = 0; y < _grid.Height; y++) blocked[_grid.ToIndex(2, y)] = true;
            path.Add(7);
            Assert.That(search.BreadthFirst(0, 4, blocked, path), Is.False); Assert.That(path, Is.Empty);
            Assert.That(search.AStar(0, 4, blocked, null, path), Is.False); Assert.That(path, Is.Empty);
        }
        [Test] public void AStarMatchesBfsShortestPathsAcrossSeededObstacleMaps()
        {
            var grid = Grid2D.Centered(new Vec2(15, 15), 1); var search = new GridPathfinder(grid);
            var bfs = new List<int>(); var astar = new List<int>(); var blocked = new bool[grid.CellCount];
            var random = new Random(82);
            for (int iteration = 0; iteration < 60; iteration++)
            {
                for (int i = 0; i < blocked.Length; i++) blocked[i] = random.NextDouble() < .26;
                blocked[0] = blocked[blocked.Length - 1] = false;
                bool reachable = search.BreadthFirst(0, blocked.Length - 1, blocked, bfs);
                Assert.That(search.AStar(0, blocked.Length - 1, blocked, null, astar), Is.EqualTo(reachable));
                Assert.That(astar.Count, Is.EqualTo(bfs.Count));
                for (int i = 1; i < astar.Count; i++)
                {
                    grid.FromIndex(astar[i - 1], out int ax, out int ay); grid.FromIndex(astar[i], out int bx, out int by);
                    Assert.That(System.Math.Abs(ax - bx) + System.Math.Abs(ay - by), Is.EqualTo(1));
                    Assert.That(blocked[astar[i]], Is.False);
                }
            }
        }
        [Test] public void StartEqualsGoalReturnsSingleCellAndInvalidCostsAreRejected()
        {
            var search = new GridPathfinder(_grid); var path = new List<int>();
            Assert.That(search.BreadthFirst(1, 1, null, path), Is.True); Assert.That(path, Is.EqualTo(new[] { 1 }));
            Assert.Throws<ArgumentOutOfRangeException>(() => search.AStar(0, 1, null, new float[_grid.CellCount], path));
        }
    }
}
