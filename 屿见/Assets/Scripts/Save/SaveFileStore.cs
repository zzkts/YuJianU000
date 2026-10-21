using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace Yujian.Save
{
    /// <summary>
    /// 存档文件读写。只负责“把一段文本安全地放到磁盘 / 从磁盘拿回来”，
    /// 不解析 JSON、不知道任何游戏系统。
    ///
    /// 写入顺序：
    ///   1. 先写 save.json.tmp；
    ///   2. 用 File.Replace 原子替换 save.json，并把旧文件留成 save.json.bak；
    ///   3. Replace 不可用时退化为“复制到 .bak → 覆盖 save.json”。
    /// 读取顺序：先读 save.json，失败再读 save.json.bak。
    ///
    /// PC 单机默认路径：Application.persistentDataPath（%USERPROFILE%\AppData\LocalLow\...）。
    /// </summary>
    public sealed class SaveFileStore
    {
        private const string SaveFileName = "save.json";
        private const string TempFileName = "save.json.tmp";
        private const string BackupFileName = "save.json.bak";

        /// <summary>存档所在目录。</summary>
        public string Directory { get; }

        /// <summary>主存档路径。</summary>
        public string SavePath => Path.Combine(Directory, SaveFileName);

        /// <summary>备份路径。</summary>
        public string BackupPath => Path.Combine(Directory, BackupFileName);

        /// <summary>主存档或备份是否存在。</summary>
        public bool HasAnySave => File.Exists(SavePath) || File.Exists(BackupPath);

        public SaveFileStore(string directory = null)
        {
            Directory = string.IsNullOrWhiteSpace(directory)
                ? Application.persistentDataPath
                : directory;
        }

        /// <summary>
        /// 读取存档文本。主存档失败时自动尝试备份。
        /// </summary>
        /// <param name="json">读到的文本。</param>
        /// <param name="fromBackup">是否来自备份。</param>
        /// <param name="error">两个文件都失败时的具体原因。</param>
        public bool TryRead(out string json, out bool fromBackup, out string error)
        {
            if (TryReadFile(SavePath, out json, out string primaryError))
            {
                fromBackup = false;
                error = null;
                return true;
            }

            if (TryReadFile(BackupPath, out json, out string backupError))
            {
                fromBackup = true;
                error = null;
                Debug.LogWarning($"[SaveFileStore] 主存档不可用（{primaryError}），已改用备份：{BackupPath}");
                return true;
            }

            fromBackup = false;
            error = $"主存档：{primaryError}；备份：{backupError}";
            return false;
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

        /// <summary>删除主存档、备份与临时文件。用于“新游戏”。</summary>
        public bool TryDelete(out string error)
        {
            try
            {
                DeleteIfExists(SavePath);
                DeleteIfExists(BackupPath);
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
