using NUnit.Framework;
using Yujian.Save;

namespace Yujian.Tests
{
    /// <summary>
    /// 冒烟测试：只确认三件事——
    /// 1. Assets/Tests/Editor 里的代码能编译进 Assembly-CSharp-Editor；
    /// 2. 能直接引用游戏代码（不用 asmdef）；
    /// 3. EditMode Test Runner 能在批处理里发现并运行它。
    /// </summary>
    public class WhiteBoxSmokeTests
    {
        [Test]
        public void GameAssembly_IsAccessible()
        {
            Assert.That(SaveData.CurrentVersion, Is.EqualTo(3));
        }

        [Test]
        public void SaveIntegrity_IsDeterministic()
        {
            string first = SaveIntegrity.Compute("屿见");
            string second = SaveIntegrity.Compute("屿见");

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first, Has.Length.EqualTo(16));
        }
    }
}
