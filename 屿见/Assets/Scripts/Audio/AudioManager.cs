using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Core;
using Yujian.Core.Events;

namespace Yujian.Audio
{
    /// <summary>
    /// 全局音频播放器。
    ///
    /// 职责：
    ///   · 按 <see cref="AudioId"/> 播放一次性音效（对象池 + 每 ID 冷却）；
    ///   · 播放 / 切换背景音乐（单条，带淡入淡出）；
    ///   · 维护主音量 / 音乐 / 音效 / 界面四档音量，存进 PlayerPrefs；
    ///   · 订阅 EventBus，把玩法事件自动翻译成音效。
    ///
    /// 刻意**不**做的事：不持有业务状态、不判断能不能买、不关心当前在哪个区域之外的事情。
    /// 玩法系统只知道自己发生了什么，音频怎么表现由本类 + AudioCatalog 决定。
    ///
    /// 用法：代码里 <c>AudioManager.Play(AudioId.UiClick)</c> 即可，不需要在业务脚本里拖引用。
    /// 场景里没有本组件时是空操作（只警告一次）。
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        private const string MasterVolumeKey = "yujian.audio.master";
        private const string MusicVolumeKey = "yujian.audio.music";
        private const string SfxVolumeKey = "yujian.audio.sfx";
        private const string UiVolumeKey = "yujian.audio.ui";

        private static bool warnedAboutMissingManager;

        /// <summary>当前实例。音频不跨场景保留——新场景应自带一个 AudioManager（向导会创建）。</summary>
        public static AudioManager Instance { get; private set; }

        [Header("配置")]
        [Tooltip("音频配置表。留空时所有播放都是空操作。" +
                 "资产由菜单「屿见/通用模块/配置通用模块（事件·音频·UI）」创建在 Assets/Data/Audio")]
        [SerializeField] private AudioCatalog catalog;

        [Header("对象池")]
        [Tooltip("同时可播放的一次性音效数量。8~12 足够；超出时复用最早的那个")]
        [SerializeField, Min(1)] private int sourceCount = 10;

        [Header("音乐")]
        [Tooltip("背景音乐淡入淡出秒数。0 表示直接切换")]
        [SerializeField, Min(0f)] private float musicFadeSeconds = 0.6f;

        [Tooltip("区域切换时自动切背景音乐。配置表里没有对应音乐时不会报错，静默跳过")]
        [SerializeField] private bool switchMusicByArea = true;

        [Header("调试")]
        [Tooltip("是否打印播放日志（会非常吵，只在排查音频问题时打开）")]
        [SerializeField] private bool logAudioFlow = false;

        private readonly List<AudioSource> pool = new List<AudioSource>();
        private readonly Dictionary<AudioId, float> lastPlayedTime = new Dictionary<AudioId, float>();

        private AudioSource musicSource;
        private Coroutine musicRoutine;
        private AudioId currentMusic = AudioId.None;
        private float musicFade;

        private float masterVolume = 1f;
        private float musicVolume = 1f;
        private float sfxVolume = 1f;
        private float uiVolume = 1f;

        private int nextSourceIndex;
        private bool warnedAboutMissingCatalog;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[AudioManager] 场景中存在多个 AudioManager，已保留最先初始化的实例，" +
                                 "本实例不会播放任何声音。", this);
                enabled = false;
                return;
            }

            Instance = this;

            LoadVolumes();
            BuildPool();
            BuildMusicSource();

            if (catalog == null)
            {
                Debug.LogWarning("[AudioManager] 未配置 AudioCatalog，所有音效播放都会被跳过。" +
                                 "请运行菜单「屿见/通用模块/配置通用模块（事件·音频·UI）」，或把 Assets/Data/Audio/AudioCatalog.asset " +
                                 "拖到该字段。", this);
            }
        }

        private void OnEnable()
        {
            EventBus.Subscribe<MoneyChanged>(HandleMoneyChanged);
            EventBus.Subscribe<InventoryChanged>(HandleInventoryChanged);
            EventBus.Subscribe<OreHit>(HandleOreHit);
            EventBus.Subscribe<BlueprintAnchored>(HandleBlueprintAnchored);
            EventBus.Subscribe<BlueprintClosed>(HandleBlueprintClosed);
            EventBus.Subscribe<BuildingPlaced>(HandleBuildingPlaced);
            EventBus.Subscribe<BuildingRemoved>(HandleBuildingRemoved);
            EventBus.Subscribe<MaterialPlaced>(HandleMaterialPlaced);
            EventBus.Subscribe<MaterialUndone>(HandleMaterialUndone);
            EventBus.Subscribe<LevelChanged>(HandleLevelChanged);
            EventBus.Subscribe<PurchaseSucceeded>(HandlePurchaseSucceeded);
            EventBus.Subscribe<PurchaseFailed>(HandlePurchaseFailed);
            EventBus.Subscribe<AreaChanged>(HandleAreaChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<MoneyChanged>(HandleMoneyChanged);
            EventBus.Unsubscribe<InventoryChanged>(HandleInventoryChanged);
            EventBus.Unsubscribe<OreHit>(HandleOreHit);
            EventBus.Unsubscribe<BlueprintAnchored>(HandleBlueprintAnchored);
            EventBus.Unsubscribe<BlueprintClosed>(HandleBlueprintClosed);
            EventBus.Unsubscribe<BuildingPlaced>(HandleBuildingPlaced);
            EventBus.Unsubscribe<BuildingRemoved>(HandleBuildingRemoved);
            EventBus.Unsubscribe<MaterialPlaced>(HandleMaterialPlaced);
            EventBus.Unsubscribe<MaterialUndone>(HandleMaterialUndone);
            EventBus.Unsubscribe<LevelChanged>(HandleLevelChanged);
            EventBus.Unsubscribe<PurchaseSucceeded>(HandlePurchaseSucceeded);
            EventBus.Unsubscribe<PurchaseFailed>(HandlePurchaseFailed);
            EventBus.Unsubscribe<AreaChanged>(HandleAreaChanged);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        // ---------------- 静态播放入口 ----------------

        /// <summary>播放一个 2D 音效（界面音、提示音）。</summary>
        public static void Play(AudioId id)
        {
            PlayInternal(id, Vector3.zero, false);
        }

        /// <summary>在世界坐标播放一个音效。条目不是 3D 时坐标会被忽略。</summary>
        public static void Play(AudioId id, Vector3 worldPosition)
        {
            PlayInternal(id, worldPosition, true);
        }

        /// <summary>播放 / 切换背景音乐。传 <see cref="AudioId.None"/> 等于淡出停止。</summary>
        public static void PlayMusic(AudioId id)
        {
            AudioManager manager = Instance;

            if (manager == null)
            {
                WarnMissingManager();
                return;
            }

            manager.SwitchMusic(id);
        }

        private static void PlayInternal(AudioId id, Vector3 position, bool hasPosition)
        {
            AudioManager manager = Instance;

            if (manager == null)
            {
                WarnMissingManager();
                return;
            }

            manager.PlayClip(id, position, hasPosition);
        }

        private static void WarnMissingManager()
        {
            if (warnedAboutMissingManager)
            {
                return;
            }

            warnedAboutMissingManager = true;

            if (Application.isPlaying)
            {
                Debug.LogWarning("[AudioManager] 场景里没有 AudioManager，播放请求被忽略。" +
                                 "请运行菜单「屿见/通用模块/配置通用模块（事件·音频·UI）」。");
            }
        }

        // ---------------- 播放实现 ----------------

        private void PlayClip(AudioId id, Vector3 position, bool hasPosition)
        {
            if (id == AudioId.None || catalog == null)
            {
                ReportMissingCatalog();
                return;
            }

            if (!catalog.TryGet(id, out AudioCatalog.Entry entry))
            {
                // 条目没配 / 素材还没做：这是配置阶段的正常状态，不刷错误
                return;
            }

            if (entry.CooldownSeconds > 0f &&
                lastPlayedTime.TryGetValue(id, out float last) &&
                Time.unscaledTime - last < entry.CooldownSeconds)
            {
                return;
            }

            AudioSource source = GetSource();

            if (source == null)
            {
                return;
            }

            lastPlayedTime[id] = Time.unscaledTime;

            source.Stop();
            source.clip = entry.Clip;
            source.loop = false;
            source.pitch = entry.RollPitch();
            source.spatialBlend = entry.Spatial ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = entry.MinDistance;
            source.maxDistance = entry.MaxDistance;

            if (entry.Spatial && hasPosition)
            {
                source.transform.position = position;
            }

            source.volume = masterVolume * ChannelVolume(entry.Channel) * entry.Volume;
            source.Play();

            if (logAudioFlow)
            {
                Debug.Log($"[AudioManager] 播放 {id}（{entry.Channel}），音量 {source.volume:0.00}", this);
            }
        }

        private AudioSource GetSource()
        {
            // 优先找空闲的；都忙就轮转复用
            for (int i = 0; i < pool.Count; i++)
            {
                int index = (nextSourceIndex + i) % pool.Count;
                AudioSource candidate = pool[index];

                if (candidate != null && !candidate.isPlaying)
                {
                    nextSourceIndex = (index + 1) % pool.Count;
                    return candidate;
                }
            }

            if (pool.Count == 0)
            {
                return null;
            }

            AudioSource reused = pool[nextSourceIndex];
            nextSourceIndex = (nextSourceIndex + 1) % pool.Count;
            return reused;
        }

        private void BuildPool()
        {
            pool.Clear();

            for (int i = 0; i < sourceCount; i++)
            {
                GameObject sourceObject = new GameObject($"SfxSource_{i}");
                sourceObject.transform.SetParent(transform, false);

                AudioSource source = sourceObject.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
                pool.Add(source);
            }
        }

        private void BuildMusicSource()
        {
            GameObject musicObject = new GameObject("MusicSource");
            musicObject.transform.SetParent(transform, false);

            musicSource = musicObject.AddComponent<AudioSource>();
            musicSource.playOnAwake = false;
            musicSource.loop = true;
            musicSource.spatialBlend = 0f;
        }

        // ---------------- 音乐 ----------------

        private void SwitchMusic(AudioId id)
        {
            if (id == currentMusic && (musicSource == null || musicSource.isPlaying))
            {
                return;
            }

            if (musicRoutine != null)
            {
                StopCoroutine(musicRoutine);
                musicRoutine = null;
            }

            if (id != AudioId.None && (catalog == null || !catalog.TryGet(id, out _)))
            {
                // 没配音乐就保持现状，不把正在播的曲子停掉
                return;
            }

            currentMusic = id;
            musicRoutine = StartCoroutine(SwitchMusicRoutine(id));
        }

        private IEnumerator SwitchMusicRoutine(AudioId id)
        {
            float fade = Mathf.Max(0.0001f, musicFadeSeconds);

            // 淡出当前
            while (musicFade > 0f)
            {
                musicFade = Mathf.MoveTowards(musicFade, 0f, Time.unscaledDeltaTime / fade);
                ApplyMusicVolume();
                yield return null;
            }

            musicSource.Stop();

            if (id == AudioId.None || catalog == null || !catalog.TryGet(id, out AudioCatalog.Entry entry))
            {
                musicRoutine = null;
                yield break;
            }

            musicSource.clip = entry.Clip;
            musicSource.loop = true;
            musicSource.Play();

            // 淡入新的
            while (musicFade < 1f)
            {
                musicFade = Mathf.MoveTowards(musicFade, 1f, Time.unscaledDeltaTime / fade);
                ApplyMusicVolume();
                yield return null;
            }

            musicRoutine = null;
        }

        private void ApplyMusicVolume()
        {
            if (musicSource != null)
            {
                musicSource.volume = masterVolume * musicVolume * musicFade;
            }
        }

        // ---------------- 音量 ----------------

        /// <summary>读取某组音量（0~1）。</summary>
        public float GetVolume(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Master: return masterVolume;
                case AudioChannel.Music: return musicVolume;
                case AudioChannel.Ui: return uiVolume;
                default: return sfxVolume;
            }
        }

        /// <summary>设置某组音量（0~1），自动存进 PlayerPrefs。音乐音量会立刻生效。</summary>
        public void SetVolume(AudioChannel channel, float value)
        {
            float clamped = Mathf.Clamp01(value);

            switch (channel)
            {
                case AudioChannel.Master:
                    masterVolume = clamped;
                    PlayerPrefs.SetFloat(MasterVolumeKey, clamped);
                    break;
                case AudioChannel.Music:
                    musicVolume = clamped;
                    PlayerPrefs.SetFloat(MusicVolumeKey, clamped);
                    break;
                case AudioChannel.Ui:
                    uiVolume = clamped;
                    PlayerPrefs.SetFloat(UiVolumeKey, clamped);
                    break;
                default:
                    sfxVolume = clamped;
                    PlayerPrefs.SetFloat(SfxVolumeKey, clamped);
                    break;
            }

            ApplyMusicVolume();
        }

        private float ChannelVolume(AudioChannel channel)
        {
            return GetVolume(channel);
        }

        private void LoadVolumes()
        {
            masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, 1f);
            musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 1f);
            sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 1f);
            uiVolume = PlayerPrefs.GetFloat(UiVolumeKey, 1f);
        }

        private void ReportMissingCatalog()
        {
            if (catalog != null || warnedAboutMissingCatalog)
            {
                return;
            }

            warnedAboutMissingCatalog = true;
            Debug.LogWarning("[AudioManager] 没有音频配置表，播放请求全部跳过。", this);
        }

        // ---------------- EventBus → 音效 ----------------

        private void HandleMoneyChanged(MoneyChanged changed)
        {
            if (changed.Delta > 0)
            {
                Play(AudioId.CoinGain);
            }
            else if (changed.Delta < 0)
            {
                Play(AudioId.CoinSpend);
            }
        }

        private void HandleInventoryChanged(InventoryChanged changed)
        {
            if (changed.Delta > 0)
            {
                Play(AudioId.ItemGain);
            }
        }

        private void HandleOreHit(OreHit hit)
        {
            Play(hit.Destroyed ? AudioId.OreBreak : AudioId.OreHit, hit.Position);
        }

        private void HandleBlueprintAnchored(BlueprintAnchored anchored)
        {
            Play(AudioId.BlueprintAnchor, anchored.Position);
        }

        private void HandleBlueprintClosed(BlueprintClosed closed)
        {
            // 建成时由 BuildingPlaced 播放完成音，这里只处理取消
            if (!closed.Built)
            {
                Play(AudioId.BlueprintCancel);
            }
        }

        private void HandleBuildingPlaced(BuildingPlaced placed)
        {
            Play(AudioId.BuildComplete, placed.Position);
        }

        private void HandleBuildingRemoved(BuildingRemoved removed)
        {
            Play(AudioId.BuildingRemoved, removed.Position);
        }

        private void HandleMaterialPlaced(MaterialPlaced placed)
        {
            Play(AudioId.MaterialPlace);
        }

        private void HandleMaterialUndone(MaterialUndone undone)
        {
            Play(AudioId.MaterialUndone);
        }

        private void HandleLevelChanged(LevelChanged changed)
        {
            Play(AudioId.LevelUp);
        }

        private void HandlePurchaseSucceeded(PurchaseSucceeded succeeded)
        {
            Play(AudioId.PurchaseSuccess);
        }

        private void HandlePurchaseFailed(PurchaseFailed failed)
        {
            Play(AudioId.PurchaseFailed);
        }

        private void HandleAreaChanged(AreaChanged changed)
        {
            if (!switchMusicByArea)
            {
                return;
            }

            switch (changed.Area)
            {
                case GameArea.Mining:
                    PlayMusic(AudioId.MusicMining);
                    break;
                case GameArea.Building:
                    PlayMusic(AudioId.MusicBuilding);
                    break;
                case GameArea.Shop:
                    PlayMusic(AudioId.MusicShop);
                    break;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayModeEnter()
        {
            Instance = null;
            warnedAboutMissingManager = false;
        }
    }
}
