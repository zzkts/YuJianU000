using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using Yujian.Save;

namespace Yujian.Tests
{
    /// <summary>存档文件读写：原子写入、两代备份轮换、三代回退读取的数据层测试。</summary>
    public class SaveFileStoreTests : WhiteBoxTestBase
    {
        private string directory;
        private SaveFileStore store;

        [SetUp]
        public void CreateStore()
        {
            directory = Path.Combine(Path.GetTempPath(), "yujian-whitebox-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(directory);
            store = new SaveFileStore(directory);
        }

        [TearDown]
        public void DeleteStoreDirectory()
        {
            if (System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.Delete(directory, true);
            }
        }

        [Test]
        public void NewStore_HasNoSave()
        {
            Assert.That(store.HasAnySave, Is.False);
            Assert.That(File.Exists(store.SavePath), Is.False);
        }

        [Test]
        public void TryWrite_EmptyText_Rejected()
        {
            Assert.That(store.TryWrite(null, out string error), Is.False);
            Assert.That(error, Does.Contain("为空"));

            Assert.That(store.TryWrite("   ", out error), Is.False);
            Assert.That(store.HasAnySave, Is.False);
        }

        [Test]
        public void TryWrite_ThenRead_ReturnsSameText()
        {
            Assert.That(store.TryWrite("第一份存档", out string error), Is.True, error);

            Assert.That(store.TryReadSource(SaveFileSource.Primary, out string json, out error), Is.True, error);
            Assert.That(json, Is.EqualTo("第一份存档"));
        }

        [Test]
        public void TryWrite_WritesUtf8WithoutBom()
        {
            store.TryWrite("屿见", out _);

            byte[] bytes = File.ReadAllBytes(store.SavePath);

            Assert.That(bytes.Length, Is.GreaterThan(3));
            Assert.That(bytes[0], Is.Not.EqualTo(0xEF), "不应写入 UTF-8 BOM");
        }

        [Test]
        public void ThreeWrites_RotateTwoGenerations()
        {
            Assert.That(store.TryWrite("A", out _), Is.True);
            Assert.That(store.TryWrite("B", out _), Is.True);
            Assert.That(store.TryWrite("C", out _), Is.True);

            Assert.That(File.ReadAllText(store.SavePath), Is.EqualTo("C"));
            Assert.That(File.ReadAllText(store.BackupPath), Is.EqualTo("B"));
            Assert.That(File.ReadAllText(store.OlderBackupPath), Is.EqualTo("A"));
        }

        [Test]
        public void OlderBackup_RotatesInsteadOfBeingOverwritten()
        {
            store.TryWrite("A", out _);
            store.TryWrite("B", out _);
            store.TryWrite("C", out _);
            store.TryWrite("D", out _);

            Assert.That(File.ReadAllText(store.SavePath), Is.EqualTo("D"));
            Assert.That(File.ReadAllText(store.BackupPath), Is.EqualTo("C"));
            Assert.That(File.ReadAllText(store.OlderBackupPath), Is.EqualTo("B"));
        }

        [Test]
        public void TryReadSource_MissingFile_ReturnsFalse()
        {
            Assert.That(store.TryReadSource(SaveFileSource.Backup, out string json, out string error), Is.False);
            Assert.That(json, Is.Null);
            Assert.That(error, Does.Contain("不存在"));
        }

        [Test]
        public void TryReadSource_EmptyFile_ReturnsFalse()
        {
            File.WriteAllText(store.SavePath, string.Empty, new UTF8Encoding(false));

            Assert.That(store.TryReadSource(SaveFileSource.Primary, out _, out string error), Is.False);
            Assert.That(error, Does.Contain("为空"));
        }

        [Test]
        public void HasAnySave_TrueWhenOnlyOlderBackupExists()
        {
            File.WriteAllText(store.OlderBackupPath, "old", new UTF8Encoding(false));

            Assert.That(store.HasAnySave, Is.True);
        }

        [Test]
        public void TryDelete_RemovesPrimaryAndBothBackups()
        {
            store.TryWrite("A", out _);
            store.TryWrite("B", out _);
            store.TryWrite("C", out _);

            Assert.That(store.TryDelete(out string error), Is.True, error);

            Assert.That(File.Exists(store.SavePath), Is.False);
            Assert.That(File.Exists(store.BackupPath), Is.False);
            Assert.That(File.Exists(store.OlderBackupPath), Is.False);
            Assert.That(store.HasAnySave, Is.False);
        }

        [Test]
        public void GetPath_MapsAllSources()
        {
            Assert.That(store.GetPath(SaveFileSource.Primary), Is.EqualTo(store.SavePath));
            Assert.That(store.GetPath(SaveFileSource.Backup), Is.EqualTo(store.BackupPath));
            Assert.That(store.GetPath(SaveFileSource.OlderBackup), Is.EqualTo(store.OlderBackupPath));
        }

        [Test]
        public void TryWrite_CreatesDirectoryWhenMissing()
        {
            string nested = Path.Combine(directory, "a", "b");
            SaveFileStore nestedStore = new SaveFileStore(nested);

            Assert.That(nestedStore.TryWrite("x", out string error), Is.True, error);
            Assert.That(File.Exists(nestedStore.SavePath), Is.True);
        }
    }
}
