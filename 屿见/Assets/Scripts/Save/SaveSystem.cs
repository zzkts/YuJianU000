using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Yujian.Building;

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

        [Tooltip("失去焦点时自动保存。蓝图里已放入的材料会由 BlueprintSaveParticipant 退回库存")]
        [SerializeField] private bool saveOnFocusLost = true;

        [Header("自动保存")]
        [Tooltip("按固定间隔自动保存。关掉后仍可用右键菜单手动保存")]
        [SerializeField] private bool autoSaveEnabled = true;

        [Tooltip("自动保存间隔（秒）。使用不受 timeScale 影响的真实时间")]
        [SerializeField, Min(5f)] private float autoSaveIntervalSeconds = 60f;

        [Tooltip("建造完成或拆除建筑后立即保存，避免玩家在这两个关键节点丢失进度。" +
                 "读档时的批量清场会被内部标记屏蔽，不会触发")]
        [SerializeField] private bool autoSaveOnBuildingChanged = true;

        [Tooltip("建筑管理器。用于订阅建造 / 拆除事件；由菜单「屿见/配置存档系统」自动接线。留空时会尝试在场景里查找")]
        [SerializeField] private BuildingManager buildingManager;

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

        /// <summary>下一次定时自动保存的时间点（unscaledTime）。</summary>
        private float nextAutoSaveTime;

        /// <summary>建造 / 拆除触发的保存请求。统一延迟到 LateUpdate 执行，确保本帧的状态已结算完。</summary>
        private bool autoSaveRequested;
        private string pendingAutoSaveReason;

        /// <summary>读档过程中为 true。此时建筑清场会触发 OnBuildingRemoved，不能把它当成玩家拆除去保存。</summary>
        private bool isLoading;

        /// <summary>重置存档并重开场景的过程中为 true，屏蔽退出 / 失焦 / 自动保存。</summary>
        private bool isResetting;

        private BuildingManager subscribedBuildingManager;

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

        private void OnEnable()
        {
            // 重复实例不订阅，避免场景里误放两个 SaveSystem 时收到双份事件
            if (Instance == this)
            {
                SubscribeBuildingEvents();
            }
        }

        private void OnDisable()
        {
            UnsubscribeBuildingEvents();
        }

        private void Start()
        {
            if (Instance != this)
            {
                return;
            }

            // 场景里可能还没接线（旧场景），补一次查找；找不到只影响建造 / 拆除即时保存
            SubscribeBuildingEvents();
            nextAutoSaveTime = Time.unscaledTime + Mathf.Max(1f, autoSaveIntervalSeconds);

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

        private void Update()
        {
            if (Instance != this)
            {
                return;
            }

            HandleResetHotkey();

            if (!autoSaveEnabled || loadFailed || isLoading || isResetting)
            {
                return;
            }

            if (Time.unscaledTime < nextAutoSaveTime)
            {
                return;
            }

            nextAutoSaveTime = Time.unscaledTime + Mathf.Max(1f, autoSaveIntervalSeconds);

            if (logSaveFlow)
            {
                Debug.Log("[SaveSystem] 定时自动保存。", this);
            }

            Save();
        }

        private void LateUpdate()
        {
            if (Instance != this || !autoSaveRequested)
            {
                return;
            }

            autoSaveRequested = false;

            if (!autoSaveEnabled || !autoSaveOnBuildingChanged || loadFailed || isLoading || isResetting)
            {
                return;
            }

            if (logSaveFlow)
            {
                Debug.Log($"[SaveSystem] 自动保存（{pendingAutoSaveReason}）。", this);
            }

            Save();
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
                // 校验和覆盖除 checksum 本身以外的全部内容：
                // 先清空 checksum 序列化一次求值，再填回去序列化最终文本
                data.checksum = string.Empty;
                string payload = serializer.Serialize(data);
                data.checksum = SaveIntegrity.Compute(payload);
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

            // 读档开始前丢掉尚未执行的“建造 / 拆除”保存请求，
            // 否则 LateUpdate 会把刚读进来的状态再写一遍（还会多轮换一次备份）
            autoSaveRequested = false;

            SaveData data = null;
            SaveFileSource source = SaveFileSource.Primary;
            int fileVersion = 0;
            bool anyFileExists = false;
            List<string> failures = new List<string>();

            // 依次尝试主档 → .bak → .bak2。文件存在但内容损坏时也要继续往下试，
            // 否则两代备份形同虚设（这是本轮验证发现的缺口）。
            for (int i = 0; i < 3; i++)
            {
                SaveFileSource candidate = (SaveFileSource)i;
                string path = store.GetPath(candidate);
                string label = DescribeSource(candidate);

                if (!store.TryReadSource(candidate, out string json, out string readError))
                {
                    if (File.Exists(path))
                    {
                        anyFileExists = true;
                        failures.Add($"{label}（{path}）：文件不可读（{readError}）");
                    }

                    continue;
                }

                anyFileExists = true;

                if (!TryParseSaveData(json, out SaveData parsed, out string parseError, out int parsedVersion))
                {
                    failures.Add($"{label}（{path}）：{parseError}");

                    if (candidate != SaveFileSource.OlderBackup)
                    {
                        Debug.LogWarning($"[SaveSystem] {label}不可用：{parseError}。尝试更早的备份。", this);
                    }

                    continue;
                }

                data = parsed;
                source = candidate;
                fileVersion = parsedVersion;
                break;
            }

            if (data == null)
            {
                if (anyFileExists)
                {
                    Debug.LogError("[SaveSystem] 找到存档文件，但没有任何一代能通过校验，已拒绝加载：\n" +
                                   string.Join("\n", failures), this);
                }
                else
                {
                    Debug.Log("[SaveSystem] 没有存档可读，按新游戏启动。");
                }

                return false;
            }

            if (source != SaveFileSource.Primary)
            {
                Debug.LogWarning($"[SaveSystem] 已改用{DescribeSource(source)}加载：" +
                                 $"{store.GetPath(source)}", this);
            }

            if (fileVersion < data.version)
            {
                Debug.LogWarning($"[SaveSystem] 存档版本 {fileVersion} 低于当前 {data.version}：" +
                                 "v1 没有校验和，已完成结构校验，将按当前结构读取并迁移。" +
                                 "下一次保存会自动写成新版本。");
            }

            if (!ValidateParticipants())
            {
                return false;
            }

            // 建筑参与者在 Restore 里会先 ClearPlacedBuildings，从而触发 OnBuildingRemoved。
            // 这里的标记让 SaveSystem 把这批移除识别为“系统清场”，不产生自动保存请求。
            isLoading = true;

            try
            {
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
            }
            finally
            {
                isLoading = false;
            }

            IsLoaded = true;
            loadFailed = false;

            if (logSaveFlow)
            {
                string savedAt = data.savedAtUtc > 0
                    ? new DateTime(data.savedAtUtc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                    : "未知";

                string versionText = fileVersion == data.version
                    ? fileVersion.ToString()
                    : $"{fileVersion}（已迁移到 {data.version}）";

                Debug.Log($"[SaveSystem] 已读取{DescribeSource(source)}：{store.GetPath(source)}\n" +
                          $"版本 {versionText}，保存时间 {savedAt}，金币 {data.currency}，" +
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
            if (Instance != this || !saveOnQuit || isResetting)
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
            // loadFailed 时必须跳过：启动读档失败后切窗口不能把原档覆盖成默认状态
            if (Instance != this || hasFocus || !saveOnFocusLost || isResetting || loadFailed)
            {
                return;
            }

            Save();
        }

        /// <summary>
        /// 测试用：删除主存档与两代备份，然后重开当前场景，让游戏从「没有存档」重新开始。
        /// 绑定 F9（仅编辑器与 Development Build）。重开场景后本实例会销毁，新实例按新游戏启动。
        /// </summary>
        /// <returns>是否已成功删除存档并开始重开场景。</returns>
        public bool ResetSaveAndReload()
        {
            if (Instance != this)
            {
                Debug.LogWarning("[SaveSystem] 本实例不是当前存档系统，忽略重置请求。", this);
                return false;
            }

            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SaveSystem] 重置存档只能在 Play Mode 下使用。", this);
                return false;
            }

            if (isResetting)
            {
                return false;
            }

            Scene activeScene = SceneManager.GetActiveScene();

            if (activeScene.buildIndex < 0)
            {
                Debug.LogError("[SaveSystem] 当前场景不在 Build Settings 里，无法重开。", this);
                return false;
            }

            isResetting = true;
            autoSaveRequested = false;

            if (!DeleteSave())
            {
                Debug.LogError("[SaveSystem] 重置存档失败：存档文件没有删掉，已取消重开场景。", this);
                isResetting = false;
                return false;
            }

            if (logSaveFlow)
            {
                Debug.Log("[SaveSystem] 已重置存档，正在重开场景……", this);
            }

            try
            {
                SceneManager.LoadScene(activeScene.buildIndex);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[SaveSystem] 重开场景失败：{exception}", this);
                isResetting = false;
                return false;
            }
        }

        private void HandleResetHotkey()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Keyboard keyboard = Keyboard.current;

            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame)
            {
                ResetSaveAndReload();
            }
#endif
        }

        private void SubscribeBuildingEvents()
        {
            BuildingManager target = buildingManager;

            if (target == null)
            {
                target = FindFirstObjectByType<BuildingManager>();
            }

            if (target == subscribedBuildingManager)
            {
                return;
            }

            UnsubscribeBuildingEvents();
            subscribedBuildingManager = target;

            if (target == null)
            {
                if (logSaveFlow)
                {
                    Debug.LogWarning("[SaveSystem] 没有找到 BuildingManager，建造 / 拆除后不会立即保存" +
                                     "（定时保存仍然有效）。请运行菜单「屿见/配置存档系统」自动接线。", this);
                }

                return;
            }

            target.OnBlueprintClosed += HandleBlueprintClosedForAutoSave;
            target.OnBuildingRemoved += HandleBuildingRemovedForAutoSave;
        }

        private void UnsubscribeBuildingEvents()
        {
            if (subscribedBuildingManager == null)
            {
                return;
            }

            subscribedBuildingManager.OnBlueprintClosed -= HandleBlueprintClosedForAutoSave;
            subscribedBuildingManager.OnBuildingRemoved -= HandleBuildingRemovedForAutoSave;
            subscribedBuildingManager = null;
        }

        private void HandleBlueprintClosedForAutoSave(bool built)
        {
            if (built)
            {
                RequestAutoSave("建造完成");
            }
        }

        private void HandleBuildingRemovedForAutoSave(GameObject building)
        {
            // 读档清场也会走到这里，RequestAutoSave 内部会看 isLoading
            RequestAutoSave("拆除建筑");
        }

        private void RequestAutoSave(string reason)
        {
            if (!autoSaveEnabled || !autoSaveOnBuildingChanged ||
                loadFailed || isLoading || isResetting)
            {
                return;
            }

            autoSaveRequested = true;
            pendingAutoSaveReason = reason;
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

        /// <summary>
        /// 解析一份存档文本并做完整校验。不打印错误——调用方要把各代文件的失败原因汇总后统一汇报。
        /// </summary>
        private bool TryParseSaveData(
            string json, out SaveData data, out string error, out int fileVersion)
        {
            data = null;
            error = null;
            fileVersion = 0;

            try
            {
                data = serializer.Deserialize(json);
            }
            catch (Exception exception)
            {
                error = $"解析失败：{exception.Message}";
                return false;
            }

            if (data == null)
            {
                error = "解析结果为空。";
                return false;
            }

            data.Normalize();

            if (data.version > SaveData.CurrentVersion)
            {
                // 不覆盖、不猜测：新版本存档交给新版本游戏读取
                error = $"存档版本 {data.version} 高于当前支持的 {SaveData.CurrentVersion}，拒绝加载。";
                return false;
            }

            if (data.version <= 0)
            {
                error = "缺少 version 字段（可能是空文件、截断文件或非存档 JSON）。";
                return false;
            }

            // 结构校验先于校验和：能给出比「校验和不匹配」更具体的原因
            if (!SaveIntegrity.Validate(data, out string structureError))
            {
                error = $"结构校验失败：{structureError}";
                return false;
            }

            fileVersion = data.version;

            if (fileVersion < SaveData.CurrentVersion)
            {
                // v1 没有 checksum，这一代只做结构校验；下一次保存会写成当前版本
                data.version = SaveData.CurrentVersion;
                return true;
            }

            return VerifyChecksum(data, out error);
        }

        /// <summary>
        /// 校验 v2 存档的校验和。调用方已经确认 <see cref="SaveData.version"/> 是当前版本。
        /// </summary>
        private bool VerifyChecksum(SaveData data, out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(data.checksum))
            {
                error = "缺少 checksum 字段。";
                return false;
            }

            string stored = data.checksum;
            string payload;

            try
            {
                // 与写入时一致：清空 checksum 后对整个对象求规范 JSON
                data.checksum = string.Empty;
                payload = serializer.Serialize(data);
            }
            catch (Exception exception)
            {
                data.checksum = stored;
                error = $"计算校验和失败：{exception.Message}";
                return false;
            }

            data.checksum = stored;
            string expected = SaveIntegrity.Compute(payload);

            if (!string.Equals(expected, stored, StringComparison.OrdinalIgnoreCase))
            {
                error = $"校验和不匹配（期望 {expected}，文件内 {stored}），文件可能已损坏或被截断。";
                return false;
            }

            return true;
        }

        private static string DescribeSource(SaveFileSource source)
        {
            switch (source)
            {
                case SaveFileSource.Backup:
                    return "上一代备份（.bak）";
                case SaveFileSource.OlderBackup:
                    return "更早的备份（.bak2）";
                default:
                    return "主存档";
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

        [ContextMenu("调试：重置存档（删除并重开场景）")]
        private void DebugResetSave()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[SaveSystem] 重置存档只能在 Play Mode 下使用，" +
                                 "否则场景会以编辑模式重载。", this);
                return;
            }

            ResetSaveAndReload();
        }
    }
}
