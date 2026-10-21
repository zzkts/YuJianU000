using System;
using UnityEngine;

namespace Yujian.Save
{
    /// <summary>
    /// 存档系统入口。
    ///
    /// 职责：
    ///   1. 按固定顺序调用各 <see cref="SaveParticipant"/> 的 Capture / Restore；
    ///   2. 通过 <see cref="SaveFileStore"/> 原子写入、备份读取；
    ///   3. 处理版本检查、启动读档、退出保存与手动保存。
    ///
    /// 不知道金币、材料、建筑、矿石各自怎么存——那些全在参与者里。
    ///
    /// 时序：默认脚本的 Start 先跑（PlayerCurrency 初始化、OreSpawner 先生成一块矿石等），
    /// 本类用 [DefaultExecutionOrder(1000)] 在所有默认 Start 之后读档，
    /// 用存档值覆盖它们刚建立的初始状态。第 2、3 步的建筑与矿石恢复也依赖这个顺序。
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SaveSystem : MonoBehaviour
    {
        [Header("参与者")]
        [Tooltip("按数组顺序依次执行 Capture / Restore。顺序很重要：先基础数据，再依赖它的系统。" +
                 "由菜单「屿见/配置存档系统」自动接线")]
        [SerializeField] private SaveParticipant[] participants = new SaveParticipant[0];

        [Header("时机")]
        [Tooltip("启动时自动读档。没有存档时按新游戏启动")]
        [SerializeField] private bool loadOnStart = true;

        [Tooltip("退出游戏时自动保存。编辑器里停止 Play Mode 也会触发")]
        [SerializeField] private bool saveOnQuit = true;

        [Tooltip("失去焦点时自动保存。第 1 步默认关闭：蓝图里已放入的材料还没有退款逻辑，" +
                 "此时保存会把它们留在存档之外。第 3 步处理蓝图退款后再打开")]
        [SerializeField] private bool saveOnFocusLost = false;

        [Header("调试")]
        [Tooltip("是否在 Console 打印保存 / 读取日志")]
        [SerializeField] private bool logSaveFlow = true;

        /// <summary>当前实例。供后续 UI 的“保存 / 读取”按钮访问。</summary>
        public static SaveSystem Instance { get; private set; }

        /// <summary>是否已经成功读过一次存档。</summary>
        public bool IsLoaded { get; private set; }

        /// <summary>磁盘上是否存在主存档或备份。</summary>
        public bool HasSave => store != null && store.HasAnySave;

        /// <summary>当前存档路径，供日志与调试显示。</summary>
        public string SavePath => store != null ? store.SavePath : string.Empty;

        /// <summary>保存成功后触发。</summary>
        public event Action OnSaved;

        /// <summary>读档成功后触发。</summary>
        public event Action OnLoaded;

        private SaveFileStore store;
        private ISaveSerializer serializer;
        private bool warnedAboutMissingParticipants;

        /// <summary>
        /// 启动时存在存档但读取失败。此时退出自动保存必须跳过，
        /// 否则会把一个“版本过新 / 已损坏”的原存档直接覆盖成当前默认状态。
        /// </summary>
        private bool loadFailed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[SaveSystem] 场景中存在多个 SaveSystem，已保留最先初始化的实例，" +
                                 "本实例不会读写存档。", this);
                return;
            }

            Instance = this;
            store = new SaveFileStore();
            serializer = new JsonSaveSerializer();
        }

        private void Start()
        {
            if (Instance != this)
            {
                return;
            }

            if (!loadOnStart)
            {
                if (logSaveFlow)
                {
                    Debug.Log("[SaveSystem] 已关闭启动读档，按当前场景状态开始。");
                }

                return;
            }

            if (!HasSave)
            {
                if (logSaveFlow)
                {
                    Debug.Log("[SaveSystem] 没有找到存档，按新游戏启动。");
                }

                return;
            }

            if (!Load())
            {
                loadFailed = true;
            }
        }

        /// <summary>
        /// 立即保存。失败时返回 false，并把具体原因写进 Console，不静默吞掉。
        /// </summary>
        public bool Save()
        {
            if (Instance != this)
            {
                Debug.LogWarning("[SaveSystem] 本实例不是当前存档系统，忽略保存请求。", this);
                return false;
            }

            EnsureServices();

            if (!ValidateParticipants())
            {
                return false;
            }

            SaveData data = new SaveData
            {
                version = SaveData.CurrentVersion,
                savedAtUtc = DateTime.UtcNow.Ticks,
            };

            for (int i = 0; i < participants.Length; i++)
            {
                SaveParticipant participant = participants[i];

                try
                {
                    participant.Capture(data);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[SaveSystem] {participant.GetType().Name} 在 Capture 时抛出异常，" +
                                   $"本次保存已取消：{exception}", participant);
                    return false;
                }
            }

            data.Normalize();

            string json;

            try
            {
                json = serializer.Serialize(data);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SaveSystem] 序列化存档失败：{exception}", this);
                return false;
            }

            if (!store.TryWrite(json, out string error))
            {
                Debug.LogError($"[SaveSystem] 写入存档失败：{error}\n路径：{store.SavePath}", this);
                return false;
            }

            if (logSaveFlow)
            {
                Debug.Log($"[SaveSystem] 已保存：{store.SavePath}\n" +
                          $"版本 {data.version}，金币 {data.currency}，材料 {data.materials.Count} 格，" +
                          $"建筑 {data.buildings.Count} 座。");
            }

            OnSaved?.Invoke();
            return true;
        }

        /// <summary>
        /// 立即读档。没有存档、版本过新、解析失败或任一参与者恢复失败时返回 false。
        /// 失败时不会清空当前游戏状态（参与者按顺序恢复，可能已经恢复了一部分，Console 会说明）。
        /// </summary>
        public bool Load()
        {
            if (Instance != this)
            {
                Debug.LogWarning("[SaveSystem] 本实例不是当前存档系统，忽略读取请求。", this);
                return false;
            }

            EnsureServices();

            if (!store.TryRead(out string json, out bool fromBackup, out string readError))
            {
                if (HasSave)
                {
                    Debug.LogError($"[SaveSystem] 找到存档文件但读取失败：{readError}", this);
                }
                else
                {
                    Debug.Log("[SaveSystem] 没有存档可读，按新游戏启动。");
                }

                return false;
            }

            SaveData data;

            try
            {
                data = serializer.Deserialize(json);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SaveSystem] 解析存档失败：{exception}", this);
                return false;
            }

            if (data == null)
            {
                return false;
            }

            if (data.version > SaveData.CurrentVersion)
            {
                // 不覆盖、不猜测：新版本存档交给新版本游戏读取
                Debug.LogError($"[SaveSystem] 存档版本 {data.version} 高于当前支持的 " +
                               $"{SaveData.CurrentVersion}，已拒绝加载。请升级游戏或删除该存档。", this);
                return false;
            }

            if (data.version < SaveData.CurrentVersion)
            {
                Debug.LogWarning($"[SaveSystem] 存档版本 {data.version} 低于当前 {SaveData.CurrentVersion}，" +
                                 "当前还没有迁移步骤，按现有结构尝试读取。");
                data.version = SaveData.CurrentVersion;
            }

            if (!ValidateParticipants())
            {
                return false;
            }

            for (int i = 0; i < participants.Length; i++)
            {
                SaveParticipant participant = participants[i];

                try
                {
                    participant.Restore(data);
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[SaveSystem] {participant.GetType().Name} 在 Restore 时抛出异常，" +
                                   $"读档中止：{exception}", participant);
                    return false;
                }
            }

            IsLoaded = true;
            loadFailed = false;

            if (logSaveFlow)
            {
                string savedAt = data.savedAtUtc > 0
                    ? new DateTime(data.savedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    : "未知";

                Debug.Log($"[SaveSystem] 已读取{(fromBackup ? "备份" : "主")}存档：{store.SavePath}\n" +
                          $"版本 {data.version}，保存时间 {savedAt}，金币 {data.currency}，" +
                          $"材料 {data.materials.Count} 格，建筑 {data.buildings.Count} 座。");
            }

            OnLoaded?.Invoke();
            return true;
        }

        /// <summary>删除主存档与备份。不会重置当前场景状态；“新游戏”应由上层重启场景。</summary>
        public bool DeleteSave()
        {
            EnsureServices();

            if (!store.TryDelete(out string error))
            {
                Debug.LogError($"[SaveSystem] 删除存档失败：{error}", this);
                return false;
            }

            IsLoaded = false;

            if (logSaveFlow)
            {
                Debug.Log($"[SaveSystem] 已删除存档：{store.SavePath}");
            }

            return true;
        }

        private void OnApplicationQuit()
        {
            if (Instance != this || !saveOnQuit)
            {
                return;
            }

            if (loadFailed)
            {
                Debug.LogError("[SaveSystem] 启动时读取存档失败，已跳过退出自动保存，" +
                               "避免把原存档覆盖成当前默认状态。\n" +
                               $"原存档：{SavePath}", this);
                return;
            }

            Save();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (Instance != this || hasFocus || !saveOnFocusLost)
            {
                return;
            }

            Save();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void EnsureServices()
        {
            if (store == null)
            {
                store = new SaveFileStore();
            }

            if (serializer == null)
            {
                serializer = new JsonSaveSerializer();
            }
        }

        /// <summary>检查参与者列表。空列表或空引用时明确报错，避免存出一个空文件。</summary>
        private bool ValidateParticipants()
        {
            if (participants == null || participants.Length == 0)
            {
                if (!warnedAboutMissingParticipants)
                {
                    warnedAboutMissingParticipants = true;
                    Debug.LogError("[SaveSystem] 没有任何 SaveParticipant，存档不会包含任何数据。" +
                                   "请运行菜单「屿见/配置存档系统」。", this);
                }

                return false;
            }

            bool valid = true;

            for (int i = 0; i < participants.Length; i++)
            {
                if (participants[i] == null)
                {
                    Debug.LogError($"[SaveSystem] participants 第 {i} 项是空引用，存档不会执行。" +
                                   "请重新运行菜单「屿见/配置存档系统」。", this);
                    valid = false;
                }
            }

            return valid;
        }

        [ContextMenu("调试：保存")]
        private void DebugSave()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SaveSystem] 调试保存只能在 Play Mode 下使用，" +
                                 "否则会把编辑模式下的默认数值写进存档。", this);
                return;
            }

            Save();
        }

        [ContextMenu("调试：读取")]
        private void DebugLoad()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SaveSystem] 调试读取只能在 Play Mode 下使用。", this);
                return;
            }

            Load();
        }

        [ContextMenu("调试：删除存档")]
        private void DebugDeleteSave()
        {
            DeleteSave();
        }
    }
}
