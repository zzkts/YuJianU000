using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Yujian.Building;
using Yujian.Island;

namespace Yujian.Tests
{
    /// <summary>
    /// 建筑网格：坐标换算、吸附、可建判定优先级、占用登记/释放、关卡污染区封锁。
    /// 使用 IslandDefinition 的矩形回退判定（没有 IslandSettings 时会打一条警告，不影响结果）。
    /// </summary>
    public class BuildingGridTests : WhiteBoxTestBase
    {
        private IslandDefinition island;
        private BuildingGrid grid;

        [SetUp]
        public void CreateGrid()
        {
            // BuildingGrid.Awake 在依赖配置前执行会报“没有海岛”的错误，这是构造顺序问题
            LogAssert.ignoreFailingMessages = true;

            island = NewGameObject("Island").AddComponent<IslandDefinition>();
            island.Configure(Vector3.zero, new Vector2(10f, 10f), -1f);

            grid = NewGameObject("Grid").AddComponent<BuildingGrid>();
            grid.Configure(Vector3.zero, 2f, new Vector2Int(10, 10), island, null);
        }

        [Test]
        public void Configure_SetsOriginAndBounds()
        {
            Assert.That(grid.IsConfigured, Is.True);
            Assert.That(grid.CellSize, Is.EqualTo(2f));
            Assert.That(grid.CellCounts, Is.EqualTo(new Vector2Int(10, 10)));
            Assert.That(grid.WorldMin, Is.EqualTo(new Vector3(-10f, 0f, -10f)));
            Assert.That(grid.WorldMax, Is.EqualTo(new Vector3(10f, 0f, 10f)));
            Assert.That(grid.GridPlaneY, Is.EqualTo(0f));
        }

        [Test]
        public void WorldToCell_ConvertsWithFloor()
        {
            Assert.That(grid.WorldToCell(new Vector3(0f, 0f, 0f), out Vector2Int cell), Is.True);
            Assert.That(cell, Is.EqualTo(new Vector2Int(5, 5)));

            // 网格外也返回格子坐标（可能越界），由调用方判断
            Assert.That(grid.WorldToCell(new Vector3(100f, 0f, 0f), out Vector2Int outside), Is.True);
            Assert.That(outside.x, Is.EqualTo(55));
        }

        [Test]
        public void CellCornersAndCenter_MatchConvention()
        {
            Vector2Int cell = new Vector2Int(5, 5);

            Assert.That(grid.CellMinCorner(cell), Is.EqualTo(new Vector3(0f, 0f, 0f)));
            Assert.That(grid.CellCenter(cell), Is.EqualTo(new Vector3(1f, 0f, 1f)));
            Assert.That(grid.CellRectCenter(new RectInt(5, 5, 2, 2)),
                Is.EqualTo(new Vector3(2f, 0f, 2f)));
        }

        [Test]
        public void CellsToWorldFootprint_ScalesAndClamps()
        {
            Assert.That(grid.CellsToWorldFootprint(new Vector2Int(2, 3)),
                Is.EqualTo(new Vector2(4f, 6f)));
            Assert.That(grid.CellsToWorldFootprint(new Vector2Int(-1, 2)),
                Is.EqualTo(new Vector2(0f, 4f)));
        }

        [Test]
        public void FootprintCells_RoundsUpWithEpsilonGuard()
        {
            Assert.That(grid.FootprintCells(new Vector2(4f, 4f)), Is.EqualTo(new Vector2Int(2, 2)));
            Assert.That(grid.FootprintCells(new Vector2(4.0000001f, 4f)), Is.EqualTo(new Vector2Int(2, 2)));
            Assert.That(grid.FootprintCells(new Vector2(5f, 5f)), Is.EqualTo(new Vector2Int(3, 3)));
            Assert.That(grid.FootprintCells(new Vector2(0f, 3f)), Is.EqualTo(Vector2Int.zero));
            Assert.That(grid.FootprintCells(new Vector2(-2f, -2f)), Is.EqualTo(Vector2Int.zero));
        }

        [Test]
        public void TryGetSnappedPose_EvenFootprintSnapsToGridLineIntersection()
        {
            Assert.That(grid.TryGetSnappedPose(new Vector3(1.4f, 0f, 1.4f), new Vector2(4f, 4f),
                out Vector3 center, out RectInt rect), Is.True);

            Assert.That(rect, Is.EqualTo(new RectInt(5, 5, 2, 2)));
            Assert.That(center, Is.EqualTo(new Vector3(2f, 0f, 2f)));
        }

        [Test]
        public void TryGetSnappedPose_OddFootprintCentersOnCell()
        {
            Assert.That(grid.TryGetSnappedPose(new Vector3(1.4f, 0f, 1.4f), new Vector2(6f, 6f),
                out Vector3 center, out RectInt rect), Is.True);

            Assert.That(rect, Is.EqualTo(new RectInt(4, 4, 3, 3)));
            Assert.That(center, Is.EqualTo(new Vector3(1f, 0f, 1f)));
        }

        [Test]
        public void TryGetSnappedPose_OutsideGrid_ReturnsFalse()
        {
            Assert.That(grid.TryGetSnappedPose(new Vector3(100f, 0f, 0f), new Vector2(4f, 4f),
                out _, out _), Is.False);
        }

        [Test]
        public void CanPlace_EmptyBuildableRect_Succeeds()
        {
            Assert.That(grid.CanPlace(new RectInt(5, 5, 2, 2), out GridPlacementFailure failure), Is.True);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.None));
        }

        [Test]
        public void CanPlace_InvalidFootprint_ReportedFirst()
        {
            Assert.That(grid.CanPlace(new RectInt(5, 5, 0, 2), out GridPlacementFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.InvalidFootprint));
        }

        [Test]
        public void CanPlace_OutOfBounds_Reported()
        {
            Assert.That(grid.CanPlace(new RectInt(-1, 0, 2, 2), out GridPlacementFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.OutOfBounds));

            Assert.That(grid.CanPlace(new RectInt(9, 9, 2, 2), out failure), Is.False);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.OutOfBounds));
        }

        [Test]
        public void CanPlace_NotBuildable_OutranksOccupied()
        {
            IslandDefinition smallIsland = NewGameObject("SmallIsland").AddComponent<IslandDefinition>();
            smallIsland.Configure(Vector3.zero, new Vector2(6f, 6f), -1f);
            grid.Configure(Vector3.zero, 2f, new Vector2Int(10, 10), smallIsland, null);

            // 直接点亮占用表：角格同时“被占用”且“不在台地上”，
            // 策划案 3.2 要求先报面积问题
            bool[] occupied = TestReflect.Get<bool[]>(grid, "occupied");
            occupied[0] = true;

            Assert.That(grid.CanPlace(new RectInt(0, 0, 1, 1), out GridPlacementFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.NotBuildable));
        }

        [Test]
        public void CanPlace_NotConfigured_Reported()
        {
            BuildingGrid fresh = NewGameObject("FreshGrid").AddComponent<BuildingGrid>();
            TestReflect.Set(fresh, "configured", false);

            Assert.That(fresh.CanPlace(new RectInt(0, 0, 1, 1), out GridPlacementFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.NotConfigured));
            Assert.That(fresh.WorldToCell(Vector3.zero, out _), Is.False);
        }

        [Test]
        public void Occupy_Release_TracksOccupancy()
        {
            RectInt rect = new RectInt(5, 5, 2, 2);

            Assert.That(grid.Occupy(rect, 1), Is.True);
            Assert.That(grid.OccupiedCellCount, Is.EqualTo(4));
            Assert.That(grid.IsCellOccupied(new Vector2Int(5, 5)), Is.True);
            Assert.That(grid.IsCellOccupied(new Vector2Int(4, 4)), Is.False);

            Assert.That(grid.CanPlace(rect, out GridPlacementFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.Occupied));

            Assert.That(grid.Release(1), Is.True);
            Assert.That(grid.OccupiedCellCount, Is.EqualTo(0));
            Assert.That(grid.Release(1), Is.False);
        }

        [Test]
        public void Occupy_OverlappingDifferentOwner_Fails()
        {
            Assert.That(grid.Occupy(new RectInt(5, 5, 2, 2), 1), Is.True);

            Assert.That(grid.Occupy(new RectInt(6, 6, 2, 2), 2), Is.False);
            Assert.That(grid.OccupiedCellCount, Is.EqualTo(4));
        }

        [Test]
        public void Occupy_SameOwnerAgain_ReleasesOldRectFirst()
        {
            Assert.That(grid.Occupy(new RectInt(5, 5, 1, 1), 1), Is.True);
            Assert.That(grid.Occupy(new RectInt(7, 7, 1, 1), 1), Is.True);

            Assert.That(grid.OccupiedCellCount, Is.EqualTo(1));
            Assert.That(grid.IsCellOccupied(new Vector2Int(5, 5)), Is.False);
            Assert.That(grid.IsCellOccupied(new Vector2Int(7, 7)), Is.True);
        }

        [Test]
        public void CanPlaceIgnoringOwner_OnlyIgnoresSelf()
        {
            RectInt rect = new RectInt(5, 5, 2, 2);
            grid.Occupy(rect, 1);

            Assert.That(grid.CanPlaceIgnoringOwner(rect, 1, out GridPlacementFailure failure), Is.True);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.None));

            Assert.That(grid.CanPlaceIgnoringOwner(rect, 2, out failure), Is.False);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.Occupied));
        }

        [Test]
        public void LevelFootprint_LocksWholeRectUntilUnlocked()
        {
            Assert.That(grid.RegisterLevelFootprint(42, new RectInt(4, 4, 2, 2),
                new Vector2Int(4, 4), false), Is.True);

            Assert.That(grid.CanPlace(new RectInt(4, 4, 1, 1), out GridPlacementFailure failure), Is.False);
            Assert.That(failure, Is.EqualTo(GridPlacementFailure.LevelLocked));
            Assert.That(grid.IsCellLevelBlocked(new Vector2Int(5, 5)), Is.True);
            Assert.That(grid.IsCellBuildable(new Vector2Int(5, 5)), Is.False);

            Assert.That(grid.SetLevelFootprintUnlocked(42, true), Is.True);

            // 已净化：只保留污染核心那一格
            Assert.That(grid.IsCellLevelBlocked(new Vector2Int(4, 4)), Is.True);
            Assert.That(grid.IsCellLevelBlocked(new Vector2Int(5, 5)), Is.False);
            Assert.That(grid.CanPlace(new RectInt(5, 4, 1, 1), out failure), Is.True);

            Assert.That(grid.UnregisterLevelFootprint(42), Is.True);
            Assert.That(grid.CanPlace(new RectInt(4, 4, 1, 1), out failure), Is.True);
        }

        [Test]
        public void LevelFootprint_InvalidRect_Rejected()
        {
            Assert.That(grid.RegisterLevelFootprint(1, new RectInt(0, 0, 0, 2),
                Vector2Int.zero, false), Is.False);
            Assert.That(grid.HasLevelFootprint(1), Is.False);
        }

        [Test]
        public void LevelFootprint_TryGetAndUnregisterUnknown()
        {
            grid.RegisterLevelFootprint(7, new RectInt(2, 2, 2, 2), new Vector2Int(2, 2), true);

            Assert.That(grid.TryGetLevelFootprint(7, out RectInt rect, out Vector2Int core, out bool unlocked),
                Is.True);
            Assert.That(rect, Is.EqualTo(new RectInt(2, 2, 2, 2)));
            Assert.That(core, Is.EqualTo(new Vector2Int(2, 2)));
            Assert.That(unlocked, Is.True);

            Assert.That(grid.SetLevelFootprintUnlocked(999, true), Is.False);
            Assert.That(grid.UnregisterLevelFootprint(999), Is.False);
            Assert.That(grid.TryGetLevelFootprint(999, out _, out _, out _), Is.False);
        }

        [Test]
        public void ClearOccupancy_ReleasesEverything()
        {
            grid.Occupy(new RectInt(5, 5, 2, 2), 1);
            grid.Occupy(new RectInt(0, 0, 2, 2), 2);

            grid.ClearOccupancy();

            Assert.That(grid.OccupiedCellCount, Is.EqualTo(0));
        }

        [Test]
        public void FailureNames_AreUserFacing()
        {
            Assert.That(GridPlacementFailure.Occupied.ToChineseName(), Is.EqualTo("格子已被占用"));
            Assert.That(GridPlacementFailure.LevelLocked.ToChineseName(), Is.EqualTo("关卡污染区未净化"));
        }
    }
}
