using NUnit.Framework;
using Yujian.Levels;

namespace Yujian.Tests
{
    /// <summary>关卡进度：小怪阶段占净化进度 80%，BOSS 清除后 100%。</summary>
    public class LevelProgressTests : WhiteBoxTestBase
    {
        private LevelProgress progress;

        [SetUp]
        public void CreateProgress()
        {
            progress = new LevelProgress();
        }

        [Test]
        public void NewProgress_StartsAtWaveOneAndNotCleared()
        {
            Assert.That(progress.CurrentWave, Is.EqualTo(1));
            Assert.That(progress.ClearedAggregates, Is.EqualTo(0));
            Assert.That(progress.BossAppeared, Is.False);
            Assert.That(progress.BossDefeated, Is.False);
            Assert.That(progress.IsCleared, Is.False);
        }

        [Test]
        public void Purification_ZeroCleared_IsZero()
        {
            Assert.That(progress.GetPurificationProgress(10), Is.EqualTo(0f));
        }

        [Test]
        public void Purification_HalfCleared_IsFortyPercent()
        {
            progress.ClearedAggregates = 5;

            Assert.That(progress.GetPurificationProgress(10), Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void Purification_AllAggregatesCleared_CapsAtEightyPercent()
        {
            progress.ClearedAggregates = 10;

            Assert.That(progress.GetPurificationProgress(10), Is.EqualTo(0.8f).Within(1e-5f));
        }

        [Test]
        public void Purification_OverCleared_StillCapsAtEightyPercent()
        {
            progress.ClearedAggregates = 25;

            Assert.That(progress.GetPurificationProgress(10), Is.EqualTo(0.8f).Within(1e-5f));
        }

        [Test]
        public void Purification_ZeroOrNegativeTotal_IsZero()
        {
            progress.ClearedAggregates = 3;

            Assert.That(progress.GetPurificationProgress(0), Is.EqualTo(0f));
            Assert.That(progress.GetPurificationProgress(-5), Is.EqualTo(0f));
        }

        [Test]
        public void Purification_BossDefeated_IsOne()
        {
            progress.ClearedAggregates = 2;
            progress.BossDefeated = true;

            Assert.That(progress.IsCleared, Is.True);
            Assert.That(progress.GetPurificationProgress(10), Is.EqualTo(1f));
            Assert.That(progress.GetPurificationProgress(0), Is.EqualTo(1f));
        }

        [Test]
        public void BossAppearedAlone_DoesNotClearLevel()
        {
            progress.BossAppeared = true;

            Assert.That(progress.IsCleared, Is.False);
        }
    }
}
