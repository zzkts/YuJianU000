using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Yujian.Save
{
    /// <summary>本次读到的存档来自哪一代文件。</summary>
    public enum SaveFileSource
    {
        Primary = 0,
        Backup = 1,
        OlderBackup = 2,
    }

    /// <summary>
    /// 存档文件读写。只负责“把一段文本安全地放到磁盘 / 从磁盘拿回来”，
    /// 不解析 JSON、不知道任何游戏系统。
    ///
    /// 写入顺序：
    ///   1. 先写 save.json.tmp；
    ///   2. 把上一代 .bak 轮换成 .bak2（保留两代备份）；
    ///   3. 用 File.Replace 原子替换 save.json，并把旧文件留成 save.json.bak；
    ///   4. Replace 不可用时退化为“复制到 .bak → 删除主文件 → 移动临时文件”。
    /// 读取顺序：save.json → save.json.bak → save.json.bak2。
    ///
    /// 两代备份的意义：如果某次保存写进了「语法合法但内容错误」的数据，
    /// 第一次坏档后 .bak 还是好的；下一次保存会把坏档挤到 .bak，
    /// 但 .bak2 仍保留再上一代的好档。
    ///
    /// PC 单机默认路径：Application.persistentDataPath（%USERPROFILE%\AppData\LocalLow\...）。
    /// </summary>
    public sealed class SaveFileStore
    {
        private const string SaveFileName = "save.json";
        private const string TempFileName = "save.json.tmp";
        private const string BackupFileName = "save.json.bak";
        private const string OlderBackupFileName = "save.json.bak2";

        /// <summary>存档所在目录。</summary>
        public string Directory { get; }

        /// <summary>主存档路径。</summary>
        public string SavePath => Path.Combine(Directory, SaveFileName);

        /// <summary>上一代备份路径。</summary>
        public string BackupPath => Path.Combine(Directory, BackupFileName);

        /// <summary>上上代备份路径。</summary>
        public string OlderBackupPath => Path.Combine(Directory, OlderBackupFileName);

        /// <summary>主存档或任意一代备份是否存在。</summary>
        public bool HasAnySave =>
            File.Exists(SavePath) || File.Exists(BackupPath) || File.Exists(OlderBackupPath);

        public SaveFileStore(string directory = null)
        {
            Directory = string.IsNullOrWhiteSpace(directory)
                ? Application.persistentDataPath
                : directory;
        }

        /// <summary>取某一代存档文件的路径。</summary>
        public string GetPath(SaveFileSource source)
        {
            switch (source)
            {
                case SaveFileSource.Primary:
                    return SavePath;
                case SaveFileSource.Backup:
                    return BackupPath;
                case SaveFileSource.OlderBackup:
                    return OlderBackupPath;
                default:
                    return SavePath;
            }
        }

        /// <summary>
        /// 读取指定的一代存档文本。是否回退由 SaveSystem 决定——
        /// 它需要先解析内容，才知道这份文件是「不存在」还是「存在但损坏」。
        /// </summary>
        public bool TryReadSource(SaveFileSource source, out string json, out string error)
        {
            return TryReadFile(GetPath(source), out json, out error);
        }

        /// <summary>
        /// 原子写入存档文本，并保留上一份为 .bak。
        /// </summary>
        public bool TryWrite(string json, out string error)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "存档文本为空，未写入。";
                return false;
            }

            string tempPath = Path.Combine(Directory, TempFileName);

            try
            {
                System.IO.Directory.CreateDirectory(Directory);
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));

                RotateBackups();

                if (File.Exists(SavePath))
                {
                    try
                    {
                        // 同目录内替换是原子的；旧文件自动成为 .bak
                        File.Replace(tempPath, SavePath, BackupPath, true);
                    }
                    catch (Exception replaceException)
                    {
                        // 某些文件系统/权限下 Replace 不可用，退化为复制覆盖。
                        // 这不是原子写，但至少保证旧存档还有一份 .bak
                        Debug.LogWarning($"[SaveFileStore] File.Replace 失败（{replaceException.Message}），" +
                                         "改用复制方式写入。");
                        File.Copy(SavePath, BackupPath, true);
                        File.Delete(SavePath);
                        File.Move(tempPath, SavePath);
                    }
                }
                else
                {
                    File.Move(tempPath, SavePath);
                }

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;

                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // 清理临时文件失败不覆盖真正的错误
                }

                return false;
            }
        }

        /// <summary>
        /// 把当前 .bak 轮换成 .bak2，为本次写入腾出 .bak。
        /// 轮换失败只警告、不阻止保存：最坏情况是少保留一代备份。
        /// </summary>
        private void RotateBackups()
        {
            if (!File.Exists(BackupPath))
            {
                return;
            }

            try
            {
                File.Copy(BackupPath, OlderBackupPath, true);
                File.Delete(BackupPath);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SaveFileStore] 轮换备份失败（{exception.Message}），" +
                                 "本次保存仍会继续，但更早的备份可能被覆盖。");
            }
        }

        /// <summary>删除主存档、两代备份与临时文件。用于“新游戏” / 重置存档。</summary>
        public bool TryDelete(out string error)
        {
            try
            {
                DeleteIfExists(SavePath);
                DeleteIfExists(BackupPath);
                DeleteIfExists(OlderBackupPath);
                DeleteIfExists(Path.Combine(Directory, TempFileName));
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static bool TryReadFile(string path, out string json, out string error)
        {
            json = null;

            if (!File.Exists(path))
            {
                error = "文件不存在";
                return false;
            }

            try
            {
                json = File.ReadAllText(path, Encoding.UTF8);

                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "文件为空";
                    return false;
                }

                error = null;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                return false;
            }
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
