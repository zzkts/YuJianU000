using NUnit.Framework;
using UnityEngine.TestTools;
using Yujian.Save;

namespace Yujian.Tests
{
    /// <summary>
    /// 存档入口的解析 / 迁移 / 校验和白盒测试。
    /// TryParseSaveData 是私有的，这里按白盒方式直接调用它，覆盖：
    /// v1 无校验和迁移、v3 校验和、版本过新、空文件、截断文件。
    /// </summary>
    public class SaveSystemParseTests : WhiteBoxTestBase
    {
        private SaveSystem saveSystem;
        private JsonSaveSerializer serializer;

        [SetUp]
        public void CreateSystem()
        {
            saveSystem = AddComponent<SaveSystem>(NewGameObject("SaveSystem"));
            serializer = new JsonSaveSerializer();

            // EditMode 下 Awake 不一定执行；无论是否执行过，都显式注入同一套服务
            TestReflect.Set(saveSystem, "serializer", serializer);
        }

        private bool TryParse(string json, out SaveData data, out string error, out int fileVersion)
        {
            object[] arguments = { json, null, null, 0 };
            bool result = (bool)TestReflect.Invoke(saveSystem, "TryParseSaveData", arguments);

            data = (SaveData)arguments[1];
            error = (string)arguments[2];
            fileVersion = (int)arguments[3];
            return result;
        }

        private string Sign(SaveData data)
        {
            data.Normalize();
            data.checksum = string.Empty;
            string payload = serializer.Serialize(data);
            data.checksum = SaveIntegrity.Compute(payload);
            return serializer.Serialize(data);
        }

        [Test]
        public void ValidCurrentVersion_WithCorrectChecksum_Passes()
        {
            SaveData original = NewValidSaveData();
            original.currency = 12;
            original.materials.Add(new MaterialSave { materialId = "mat_brick", count = 2 });

            Assert.That(TryParse(Sign(original), out SaveData parsed, out string error, out int fileVersion),
                Is.True, error);

            Assert.That(parsed.currency, Is.EqualTo(12));
            Assert.That(parsed.materials.Count, Is.EqualTo(1));
            Assert.That(fileVersion, Is.EqualTo(SaveData.CurrentVersion));
        }

        [Test]
        public void TamperedCurrentVersion_FailsChecksum()
        {
            SaveData original = NewValidSaveData();
            original.currency = 12;

            string json = Sign(original);
            string tampered = json.Replace("\"currency\": 12", "\"currency\": 999");

            Assert.That(tampered, Is.Not.EqualTo(json), "测试前提：JSON 里应能找到 currency 字段");
            Assert.That(TryParse(tampered, out _, out string error, out _), Is.False);
            Assert.That(error, Does.Contain("校验和"));
        }

        [Test]
        public void CurrentVersionWithoutChecksum_Fails()
        {
            SaveData original = NewValidSaveData();
            original.checksum = string.Empty;

            Assert.That(TryParse(serializer.Serialize(original), out _, out string error, out _), Is.False);
            Assert.That(error, Does.Contain("checksum"));
        }

        [Test]
        public void Version1_NoChecksum_IsMigratedToCurrent()
        {
            SaveData legacy = new SaveData
            {
                version = 1,
                currency = 34,
                savedAtUtc = 0, // v1 没有这个字段，必须允许为 0
            };
            legacy.Normalize();

            Assert.That(TryParse(serializer.Serialize(legacy), out SaveData parsed, out string error, out int fileVersion),
                Is.True, error);

            Assert.That(fileVersion, Is.EqualTo(1));
            Assert.That(parsed.version, Is.EqualTo(SaveData.CurrentVersion));
            Assert.That(parsed.currency, Is.EqualTo(34));
        }

        [Test]
        public void Version0_EmptyObject_Fails()
        {
            Assert.That(TryParse("{}", out _, out string error, out _), Is.False);
            Assert.That(error, Does.Contain("version"));
        }

        [Test]
        public void FutureVersion_IsRejectedBeforeChecksum()
        {
            SaveData future = NewValidSaveData(SaveData.CurrentVersion + 1);

            Assert.That(TryParse(serializer.Serialize(future), out _, out string error, out _), Is.False);
            Assert.That(error, Does.Contain("高于"));
        }

        [Test]
        public void TruncatedJson_Fails()
        {
            // JsonSaveSerializer 对坏 JSON 会打一条 Error 日志，这是预期行为
            LogAssert.ignoreFailingMessages = true;

            Assert.That(TryParse("{\"version\": 3, \"currency\":", out _, out string error, out _), Is.False);
            Assert.That(error, Is.Not.Empty);
        }

        [Test]
        public void StructurallyInvalidSave_FailsBeforeChecksum()
        {
            LogAssert.ignoreFailingMessages = true;

            SaveData invalid = NewValidSaveData();
            invalid.currency = -5;
            invalid.checksum = "0000000000000000";

            Assert.That(TryParse(serializer.Serialize(invalid), out _, out string error, out _), Is.False);
            Assert.That(error, Does.Contain("结构校验"));
        }

        [Test]
        public void ValidStructuralButCorruptedChecksum_Fails()
        {
            SaveData original = NewValidSaveData();
            original.checksum = "deadbeefdeadbeef";

            Assert.That(TryParse(serializer.Serialize(original), out _, out string error, out _), Is.False);
            Assert.That(error, Does.Contain("校验和"));
        }
    }
}
