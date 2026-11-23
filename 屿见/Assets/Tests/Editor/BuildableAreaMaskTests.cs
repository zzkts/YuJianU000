using NUnit.Framework;
using UnityEngine;
using Yujian.Building;

namespace Yujian.Tests
{
    /// <summary>
    /// 手绘可建面积掩码：初始化、读写、越界、签名匹配、复制、计数。
    /// </summary>
    public class BuildableAreaMaskTests : WhiteBoxTestBase
    {
        private BuildableAreaMask CreateMask(Vector2Int counts = default, bool fill = false)
        {
            if (counts == default)
            {
                counts = new Vector2Int(4, 3);
            }

            BuildableAreaMask mask = Track(ScriptableObject.CreateInstance<BuildableAreaMask>());
            mask.Initialize(Vector3.zero, 2f, counts, fill);
            return mask;
        }

        [Test]
        public void Initialize_CreatesExpectedSize()
        {
            BuildableAreaMask mask = CreateMask(new Vector2Int(4, 3));
            mask.Fill(false);

            Assert.That(mask.IsValid, Is.True);
            Assert.That(mask.CellCounts, Is.EqualTo(new Vector2Int(4, 3)));
            Assert.That(mask.CellCount, Is.EqualTo(12));
            Assert.That(mask.CountBuildable(), Is.EqualTo(0));
        }

        [Test]
        public void SetGetCell_RoundTrips()
        {
            BuildableAreaMask mask = CreateMask();

            mask.SetCell(1, 2, true);
            Assert.That(mask.GetCell(1, 2), Is.True);
            Assert.That(mask.GetCell(new Vector2Int(1, 2)), Is.True);
            Assert.That(mask.GetCell(0, 0), Is.False);

            mask.SetCell(1, 2, false);
            Assert.That(mask.GetCell(1, 2), Is.False);
        }

        [Test]
        public void OutOfBounds_GetFalse_SetIgnored()
        {
            BuildableAreaMask mask = CreateMask();

            Assert.That(mask.GetCell(-1, 0), Is.False);
            Assert.That(mask.GetCell(4, 0), Is.False);
            Assert.That(mask.GetCell(0, -1), Is.False);
            Assert.That(mask.GetCell(0, 3), Is.False);

            mask.SetCell(-1, 0, true);
            mask.SetCell(4, 0, true);
            mask.SetCell(0, 3, true);

            Assert.That(mask.CountBuildable(), Is.EqualTo(0));
        }

        [Test]
        public void Fill_ChangesAllCells()
        {
            BuildableAreaMask mask = CreateMask();

            mask.Fill(true);
            Assert.That(mask.CountBuildable(), Is.EqualTo(mask.CellCount));

            mask.Fill(false);
            Assert.That(mask.CountBuildable(), Is.EqualTo(0));
        }

        [Test]
        public void IsCompatible_ChecksCenterSizeAndCounts()
        {
            BuildableAreaMask mask = CreateMask(new Vector2Int(4, 3));

            Assert.That(mask.IsCompatible(Vector3.zero, 2f, new Vector2Int(4, 3)), Is.True);
            Assert.That(mask.IsCompatible(new Vector3(0.1f, 0f, 0f), 2f, new Vector2Int(4, 3)), Is.False);
            Assert.That(mask.IsCompatible(Vector3.zero, 2.1f, new Vector2Int(4, 3)), Is.False);
            Assert.That(mask.IsCompatible(Vector3.zero, 2f, new Vector2Int(4, 4)), Is.False);
        }

        [Test]
        public void CopyTo_RejectsWrongLength()
        {
            BuildableAreaMask mask = CreateMask(new Vector2Int(4, 3), true);

            bool[] correct = new bool[12];
            Assert.That(mask.CopyTo(correct), Is.True);
            Assert.That(correct[0], Is.True);

            Assert.That(mask.CopyTo(new bool[11]), Is.False);
            Assert.That(mask.CopyTo(null), Is.False);
        }

        [Test]
        public void CopyFrom_CopiesMinLength()
        {
            BuildableAreaMask mask = CreateMask(new Vector2Int(2, 2));

            mask.CopyFrom(new[] { true, false, true, false });
            Assert.That(mask.GetCell(0, 0), Is.True);
            Assert.That(mask.GetCell(1, 0), Is.False);
            Assert.That(mask.GetCell(0, 1), Is.True);
            Assert.That(mask.GetCell(1, 1), Is.False);

            // 源更长时只取前 N 个，不越界
            mask.CopyFrom(new[] { true, true, true, true, true, true });
            Assert.That(mask.CountBuildable(), Is.EqualTo(4));
        }

        [Test]
        public void EnsureSize_RebuildsWhenCountsChange()
        {
            BuildableAreaMask mask = CreateMask(new Vector2Int(2, 2), true);

            TestReflect.Set(mask, "cellCounts", new Vector2Int(3, 3));
            mask.EnsureSize();

            Assert.That(mask.CellCount, Is.EqualTo(9));
            Assert.That(mask.IsValid, Is.True);
            Assert.That(mask.CountBuildable(), Is.EqualTo(0));
        }
    }
}
