using NUnit.Framework;
using UnityEngine;
using Yujian.Building.Effects;

namespace Yujian.Tests
{
    /// <summary>
    /// 建筑伤害数字车道：槽位申请 / 释放 / 复用，以及槽位 → 显示偏移的排布。
    /// 纯计算，不需要场景。
    /// </summary>
    public class DamageNumberLayoutTests : WhiteBoxTestBase
    {
        private const float Tolerance = 1e-4f;

        [SetUp]
        public void ResetSlots()
        {
            BuildingDamageNumberSlots.Clear();
        }

        [TearDown]
        public void ClearSlots()
        {
            BuildingDamageNumberSlots.Clear();
        }

        // ---------- 槽位注册表 ----------

        [Test]
        public void Acquire_AssignsSequentialSlots()
        {
            Assert.That(BuildingDamageNumberSlots.Acquire(101), Is.EqualTo(0));
            Assert.That(BuildingDamageNumberSlots.Acquire(102), Is.EqualTo(1));
            Assert.That(BuildingDamageNumberSlots.Acquire(103), Is.EqualTo(2));
        }

        [Test]
        public void Acquire_SameOwner_ReturnsSameSlot()
        {
            int first = BuildingDamageNumberSlots.Acquire(101);
            int second = BuildingDamageNumberSlots.Acquire(101);

            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void Release_AllowsSlotToBeReused()
        {
            BuildingDamageNumberSlots.Acquire(101);
            int released = BuildingDamageNumberSlots.Acquire(102);
            BuildingDamageNumberSlots.Acquire(103);

            BuildingDamageNumberSlots.Release(102);

            Assert.That(BuildingDamageNumberSlots.Acquire(104), Is.EqualTo(released));
        }

        [Test]
        public void Release_UnknownOwner_DoesNothing()
        {
            BuildingDamageNumberSlots.Acquire(101);

            Assert.DoesNotThrow(() => BuildingDamageNumberSlots.Release(999));
            Assert.That(BuildingDamageNumberSlots.Acquire(102), Is.EqualTo(1));
        }

        [Test]
        public void Clear_ResetsSlots()
        {
            BuildingDamageNumberSlots.Acquire(101);
            BuildingDamageNumberSlots.Acquire(102);

            BuildingDamageNumberSlots.Clear();

            Assert.That(BuildingDamageNumberSlots.Acquire(101), Is.EqualTo(0));
            Assert.That(BuildingDamageNumberSlots.TryGetSlot(102, out _), Is.False);
        }

        [Test]
        public void TryGetSlot_ReportsAcquiredSlot()
        {
            int slot = BuildingDamageNumberSlots.Acquire(101);

            Assert.That(BuildingDamageNumberSlots.TryGetSlot(101, out int found), Is.True);
            Assert.That(found, Is.EqualTo(slot));
            Assert.That(BuildingDamageNumberSlots.TryGetSlot(102, out _), Is.False);
        }

        // ---------- 槽位 → 偏移 ----------

        [Test]
        public void GetOffset_ZeroOrNegativeSpacing_DisablesLanes()
        {
            Assert.That(BuildingDamageNumberSlots.GetOffset(3, 9, 0f), Is.EqualTo(Vector3.zero));
            Assert.That(BuildingDamageNumberSlots.GetOffset(3, 9, -1f), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void GetOffset_FirstSlot_StaysCentered()
        {
            Assert.That(BuildingDamageNumberSlots.GetOffset(0, 9, 0.9f), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void GetOffset_AlternatesLeftAndRight()
        {
            Vector3 left = BuildingDamageNumberSlots.GetOffset(1, 9, 0.9f);
            Vector3 right = BuildingDamageNumberSlots.GetOffset(2, 9, 0.9f);

            Assert.That(left.x, Is.EqualTo(-0.9f).Within(Tolerance));
            Assert.That(right.x, Is.EqualTo(0.9f).Within(Tolerance));
            Assert.That(left.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(right.y, Is.EqualTo(0f).Within(Tolerance));
        }

        [Test]
        public void GetOffset_OuterPairsMoveFartherAndHigher()
        {
            Vector3 secondLeft = BuildingDamageNumberSlots.GetOffset(3, 9, 0.9f);
            Vector3 secondRight = BuildingDamageNumberSlots.GetOffset(4, 9, 0.9f);

            Assert.That(secondLeft.x, Is.EqualTo(-1.44f).Within(Tolerance));
            Assert.That(secondRight.x, Is.EqualTo(1.44f).Within(Tolerance));
            Assert.That(secondLeft.y, Is.EqualTo(0.315f).Within(Tolerance));
            Assert.That(secondRight.y, Is.EqualTo(0.315f).Within(Tolerance));
        }

        [Test]
        public void GetOffset_WrapsAtLaneCount()
        {
            Assert.That(BuildingDamageNumberSlots.GetOffset(3, 3, 0.9f),
                Is.EqualTo(BuildingDamageNumberSlots.GetOffset(0, 3, 0.9f)));
            Assert.That(BuildingDamageNumberSlots.GetOffset(4, 3, 0.9f),
                Is.EqualTo(BuildingDamageNumberSlots.GetOffset(1, 3, 0.9f)));
        }

        [Test]
        public void GetOffset_NegativeSlotOrLaneCount_FallsBackToCenter()
        {
            Assert.That(BuildingDamageNumberSlots.GetOffset(-3, 9, 0.9f), Is.EqualTo(Vector3.zero));
            Assert.That(BuildingDamageNumberSlots.GetOffset(0, 0, 0.9f), Is.EqualTo(Vector3.zero));
            Assert.That(BuildingDamageNumberSlots.GetOffset(5, -2, 0.9f), Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void GetOffset_SingleLaneCount_KeepsEveryoneCentered()
        {
            Assert.That(BuildingDamageNumberSlots.GetOffset(0, 1, 0.9f), Is.EqualTo(Vector3.zero));
            Assert.That(BuildingDamageNumberSlots.GetOffset(7, 1, 0.9f), Is.EqualTo(Vector3.zero));
        }
    }
}
