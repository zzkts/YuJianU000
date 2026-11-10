using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Yujian.Building;
using Yujian.Core;
using Yujian.Mining;
using Yujian.Player;
using Yujian.UI;

namespace Yujian.Levels
{
    /// <summary>
    /// 关卡总控（策划案 2.3 阶段 1：解锁、进出、暂停）。
    ///
    /// 职责只有四件事：
    ///   1. 玩家等级变化时，刷新所有 <see cref="LevelEntrance"/> 的灰化 / 解锁；
    ///   2. 点击入口时判断等级：不足弹「当前等级不足」，够了走黑场转场进关；
    ///   3. 黑场转场 + 右上角退出按钮走同一个退出流程；
    ///   4. 离开关卡时暂停关卡内容（聚合体刷新、粒子、动画），回来时恢复。
    ///
    /// 不负责：伤害、金币、矿石抽取、相机机位——这些仍归各自的系统。
    /// 波次 / BOSS / 通关状态在阶段 2 接入，本类预留了 ActiveLevel 作为当前关卡。
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelManager : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("玩家等级，提供当前等级与等级变化事件")]
        [SerializeField] private PlayerLevel playerLevel;

        [Tooltip("区域控制器。进关卡切到挖矿区，出关卡切回观察模式")]
        [SerializeField] private GameAreaController areaController;

        [Tooltip("矿石生成器。离开关卡时暂停刷新，回来时恢复")]
        [SerializeField] private OreSpawner oreSpawner;

        [Tooltip("建筑网格。关卡污染区在这里登记为不可建，净化后解除封锁")]
        [SerializeField] private BuildingGrid buildingGrid;

        [Header("关卡")]
        [Tooltip("场景里的所有关卡入口。留空时自动查找（包含未激活）")]
        [SerializeField] private LevelEntrance[] entrances = new LevelEntrance[0];

        [Tooltip("从底部「挖矿」按钮直接进入时使用的关卡。留空则用第一个已解锁的入口")]
        [SerializeField] private LevelEntrance defaultEntrance;

        [Tooltip("关卡内容根。ActiveLevel 没有配置内容根时使用；再留空则自动找矿石")]
        [SerializeField] private Transform fallbackContentRoot;

        [Header("转场")]
        [Tooltip("进入关卡时黑场渐入 / 渐出的时长（秒）")]
        [SerializeField, Min(0f)] private float enterFadeDuration = 0.22f;

        [Tooltip("退出关卡时黑场渐入 / 渐出的时长（秒）")]
        [SerializeField, Min(0f)] private float exitFadeDuration = 0.22f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印关卡流程日志")]
        [SerializeField] private bool logLevelFlow = true;

        private bool levelRunning = true;
        private bool transitioning;
        private ParticleSystem[] pausedParticles;
        private bool[] particleWasPlaying;
        private Animator[] pausedAnimators;
        private float[] animatorSpeeds;

        // ---------------- 阶段 2：波次 / BOSS / 通关 ----------------

        /// <summary>每个入口各自的进度。退出关卡不清空，重新进入时接着打。</summary>
        private readonly Dictionary<LevelEntrance, LevelProgress> progressByEntrance =
            new Dictionary<LevelEntrance, LevelProgress>();

        /// <summary>当前追踪的共享矿石（OreSpawner 生成的那一块）。BOSS 也是它。</summary>
        private Ore trackedOre;

        private Vector3 trackedOreDefaultScale = Vector3.one;
        private bool trackedScaleCaptured;

        /// <summary>当前进度对应的入口。切换关卡时用来恢复体型等表现。</summary>
        private LevelEntrance flowLevel;

        /// <summary>BOSS 定义是否已经应用。离开关卡时清掉，重新进入会按满血重生成。</summary>
        private bool bossPresentationApplied;

        private float bossRegenAccumulator;
        private bool warnedMissingSpawner;

        /// <summary>BOSS 战剩余倒计时（秒）。0 表示没有在倒计时。</summary>
        private float bossCountdownRemaining;

        /// <summary>BOSS 被击败后自动退出关卡的协程。</summary>
        private Coroutine autoExitRoutine;

        /// <summary>击败 BOSS 后停留多久自动退出关卡（策划案 2.3.6：3 秒）。</summary>
        private const float ClearedAutoExitDelay = 3f;

        // ---------------- 阶段 3：占地封锁 / 视觉层 ----------------

        /// <summary>每个关卡入口对应的程序化占位视觉。</summary>
        private readonly Dictionary<LevelEntrance, LevelEnvironmentVisual> environmentVisuals =
            new Dictionary<LevelEntrance, LevelEnvironmentVisual>();

        /// <summary>网格线可视化。净化后要重画，让占地格重新显示出来。</summary>
        private BuildingGridVisualizer gridVisualizer;

        /// <summary>当前正在玩 / 最近玩过的关卡。</summary>
        public LevelEntrance ActiveLevel { get; private set; }

        /// <summary>是否正在播黑场转场。</summary>
        public bool IsTransitioning => transitioning;

        /// <summary>场景里的全部关卡入口。</summary>
        public IReadOnlyList<LevelEntrance> Entrances => entrances;

        /// <summary>当前关卡剩余波次（一个聚合体 = 一波）。BOSS 出现后为 0。</summary>
        public int RemainingWaves
        {
            get
            {
                if (ActiveLevel == null)
                {
                    return 0;
                }

                return Mathf.Max(0, ActiveLevel.WaveCount - GetProgress(ActiveLevel).ClearedAggregates);
            }
        }

        /// <summary>BOSS 倒计时是否正在进行（只在关卡内、BOSS 已出现且未击败时）。</summary>
        public bool IsBossCountdownActive
        {
            get
            {
                if (!levelRunning || ActiveLevel == null)
                {
                    return false;
                }

                LevelProgress progress = GetProgress(ActiveLevel);
                return progress.BossAppeared && !progress.BossDefeated && bossCountdownRemaining > 0f;
            }
        }

        /// <summary>BOSS 倒计时剩余秒数。</summary>
        public float BossCountdownRemaining => bossCountdownRemaining;

        /// <summary>当前关卡配置的 BOSS 倒计时总时长（秒）。</summary>
        public float BossCountdownDuration =>
            ActiveLevel != null ? ActiveLevel.BossCountdownSeconds : 0f;

        private void Awake()
        {
            ResolveDependencies();
        }

        private void Start()
        {
            ResolveDependencies();

            if (entrances == null || entrances.Length == 0)
            {
                entrances = FindObjectsByType<LevelEntrance>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
            }

            if (areaController == null)
            {
                Debug.LogError("[LevelManager] GameAreaController 未配置，关卡进出无法切换区域。" +
                               "请运行菜单「屿见/关卡/配置关卡入口（阶段 1）」。", this);
            }
            else
            {
                areaController.OnAreaChanged += HandleAreaChanged;
            }

            if (playerLevel != null)
            {
                playerLevel.OnLevelChanged += HandleLevelChanged;
            }
            else
            {
                Debug.LogWarning("[LevelManager] PlayerLevel 未配置，关卡解锁状态只在启动时算一次，" +
                                 "之后玩家升级不会解锁新关卡。", this);
            }

            ApplyLockStates(ResolveCurrentLevel());
            ActiveLevel = ResolveDefaultEntrance();

            // 开局在岛上：关卡内容先暂停。进入挖矿区时由区域事件恢复
            SetLevelRunning(areaController == null || areaController.CurrentArea == GameArea.Mining);

            EnsureTrackedOre();

            // 阶段 3：先把每个关卡的污染区登记到建筑网格，再生成占位视觉
            if (buildingGrid != null && !buildingGrid.IsConfigured)
            {
                buildingGrid.Rebuild();
            }

            RegisterLevelFootprints();
            EnsureEnvironmentVisuals();

            // 如果场景直接从挖矿区开局（现在默认是观察模式），把波次 / BOSS 状态接上
            if (areaController != null && areaController.CurrentArea == GameArea.Mining)
            {
                ApplyLevelFlow(ActiveLevel);
            }

            if (logLevelFlow)
            {
                Debug.Log($"[LevelManager] 就绪：入口 {entrances.Length} 个，当前等级 {ResolveCurrentLevel()}，" +
                          $"默认关卡 {(ActiveLevel != null ? ActiveLevel.name : "无")}。");
            }
        }

        private void OnDestroy()
        {
            if (areaController != null)
            {
                areaController.OnAreaChanged -= HandleAreaChanged;
            }

            if (playerLevel != null)
            {
                playerLevel.OnLevelChanged -= HandleLevelChanged;
            }

            CancelAutoExit();
            UnsubscribeTrackedOre();
            UnregisterLevelFootprints();
        }

        /// <summary>入口被点击。等级不足时提示，够了走黑场转场进关。</summary>
        public void TryEnter(LevelEntrance entrance)
        {
            if (entrance == null || transitioning)
            {
                return;
            }

            int playerLevelValue = ResolveCurrentLevel();

            if (!entrance.IsUnlocked(playerLevelValue))
            {
                ToastUI.Show("当前等级不足");

                if (logLevelFlow)
                {
                    Debug.Log($"[LevelManager] 点击关卡 {entrance.LevelNumber}（{entrance.name}）：" +
                              $"需要 {entrance.UnlockLevel} 级，当前 {playerLevelValue} 级，已提示「当前等级不足」。");
                }

                return;
            }

            if (areaController == null)
            {
                Debug.LogError("[LevelManager] 没有 GameAreaController，无法进入关卡。", this);
                entrance.ActivateMiningArea();
                return;
            }

            if (areaController.CurrentArea == GameArea.Mining && ActiveLevel == entrance && !transitioning)
            {
                return;
            }

            ActiveLevel = entrance;
            StartCoroutine(EnterRoutine(entrance));
        }

        /// <summary>退出关卡，返回岛屿观察模式。右上角按钮与「返回主岛」都会走这里。</summary>
        public void ExitLevel()
        {
            if (areaController == null || transitioning)
            {
                return;
            }

            if (areaController.CurrentArea != GameArea.Mining)
            {
                return;
            }

            StartCoroutine(ExitRoutine());
        }

        /// <summary>编辑器向导调用，一次接好全部依赖。</summary>
        public void ConfigureForSetup(PlayerLevel newPlayerLevel, GameAreaController newAreaController,
            OreSpawner newOreSpawner, LevelEntrance[] newEntrances, LevelEntrance newDefaultEntrance,
            Transform newFallbackContentRoot)
        {
            playerLevel = newPlayerLevel;
            areaController = newAreaController;
            oreSpawner = newOreSpawner;
            entrances = newEntrances ?? new LevelEntrance[0];
            defaultEntrance = newDefaultEntrance;
            fallbackContentRoot = newFallbackContentRoot;
        }

        private IEnumerator EnterRoutine(LevelEntrance entrance)
        {
            transitioning = true;

            if (logLevelFlow)
            {
                Debug.Log($"[LevelManager] 进入关卡 {entrance.LevelNumber}（{entrance.name}），黑场转场。");
            }

            yield return ScreenFadeUI.Instance.FadeToBlack(enterFadeDuration);

            entrance.ActivateMiningArea();

            // 等一帧让区域切换把面板 / 挖矿开关 / 关卡暂停状态全部应用完，再亮场
            yield return null;
            yield return ScreenFadeUI.Instance.FadeFromBlack(enterFadeDuration);

            transitioning = false;
        }

        private IEnumerator ExitRoutine()
        {
            transitioning = true;

            if (logLevelFlow)
            {
                Debug.Log("[LevelManager] 退出关卡，黑场转场，返回岛屿观察模式。");
            }

            yield return ScreenFadeUI.Instance.FadeToBlack(exitFadeDuration);

            areaController.EnterObservationMode();

            yield return null;
            yield return ScreenFadeUI.Instance.FadeFromBlack(exitFadeDuration);

            transitioning = false;
        }

        private void HandleLevelChanged(int newLevel)
        {
            ApplyLockStates(newLevel);

            if (logLevelFlow)
            {
                Debug.Log($"[LevelManager] 玩家升到 {newLevel} 级，已刷新所有关卡入口的解锁状态。");
            }
        }

        private void HandleAreaChanged(GameArea area)
        {
            if (area == GameArea.Mining)
            {
                if (ActiveLevel == null)
                {
                    ActiveLevel = ResolveDefaultEntrance();
                }

                SetLevelRunning(true);
                ApplyLevelFlow(ActiveLevel);
                return;
            }

            // 离开关卡：倒计时停表，击败 BOSS 后的自动退出也一并取消（手动退出优先）
            bossCountdownRemaining = 0f;
            CancelAutoExit();

            // 离开关卡：BOSS 只“暂停”，已出现状态与通关进度保留；
            // 下次进入时 bossPresentationApplied 为 false，会按满血重新生成（策划案 2.3.3）
            if (ActiveLevel != null)
            {
                LevelProgress progress = GetProgress(ActiveLevel);

                if (progress.BossAppeared && !progress.BossDefeated)
                {
                    bossPresentationApplied = false;
                    bossRegenAccumulator = 0f;

                    if (logLevelFlow)
                    {
                        Debug.Log("[LevelManager] 离开关卡时 BOSS 还在，下次进入按满血重新出现。");
                    }
                }
            }

            SetLevelRunning(false);
        }

        private void ApplyLockStates(int playerLevelValue)
        {
            if (entrances == null)
            {
                return;
            }

            for (int i = 0; i < entrances.Length; i++)
            {
                LevelEntrance entrance = entrances[i];

                if (entrance != null)
                {
                    entrance.ApplyLockState(playerLevelValue);
                }
            }
        }

        /// <summary>
        /// 当前玩家等级。优先按建筑数现查，避免 PlayerLevel.Start 还没跑完时读到 0；
        /// 查不到时退回 PlayerLevel.CurrentLevel，再不行按 1 级处理。
        /// </summary>
        private int ResolveCurrentLevel()
        {
            if (playerLevel == null)
            {
                return 1;
            }

            int level = playerLevel.GetLevelForBuildingCount(playerLevel.BuildingCount);

            if (level <= 0)
            {
                level = playerLevel.CurrentLevel;
            }

            return Mathf.Max(1, level);
        }

        private LevelEntrance ResolveDefaultEntrance()
        {
            if (defaultEntrance != null)
            {
                return defaultEntrance;
            }

            if (entrances == null || entrances.Length == 0)
            {
                return null;
            }

            int level = ResolveCurrentLevel();

            for (int i = 0; i < entrances.Length; i++)
            {
                if (entrances[i] != null && entrances[i].IsUnlocked(level))
                {
                    return entrances[i];
                }
            }

            return entrances[0];
        }

        // ---------------- 阶段 2：波次 / BOSS / 通关 ----------------

        /// <summary>BOSS 倒计时与回血。只在关卡运行中、BOSS 已出现且未被打死时生效。</summary>
        private void Update()
        {
            if (!levelRunning || ActiveLevel == null || trackedOre == null || !trackedOre.IsAlive)
            {
                return;
            }

            LevelProgress progress = GetProgress(ActiveLevel);

            if (!progress.BossAppeared || progress.BossDefeated)
            {
                return;
            }

            UpdateBossCountdown();
            UpdateBossRegen();
        }

        /// <summary>倒计时结束仍未击败 BOSS：回满血、把打散的垃圾重新聚合、倒计时重启（2.3.6）。</summary>
        private void UpdateBossCountdown()
        {
            if (bossCountdownRemaining <= 0f)
            {
                return;
            }

            bossCountdownRemaining = Mathf.Max(0f, bossCountdownRemaining - Time.deltaTime);

            if (bossCountdownRemaining > 0f)
            {
                return;
            }

            HandleBossCountdownExpired();
        }

        private void HandleBossCountdownExpired()
        {
            LevelProgress progress = GetProgress(ActiveLevel);

            if (progress == null || !progress.BossAppeared || progress.BossDefeated)
            {
                return;
            }

            if (trackedOre == null || !trackedOre.IsAlive)
            {
                return;
            }

            // 血量瞬间回满；外壳会重新聚合，等价于「损失掉的垃圾又聚回来了」
            trackedOre.RestoreFullHealth();
            bossRegenAccumulator = 0f;
            bossCountdownRemaining = ActiveLevel.BossCountdownSeconds;

            ToastUI.Show("时间到！BOSS 恢复了");

            if (logLevelFlow)
            {
                Debug.Log($"[LevelManager] 关卡 {ActiveLevel.LevelNumber}：BOSS 倒计时结束，" +
                          $"BOSS 回满血（{trackedOre.MaxHealth:0.##} HP），倒计时重启 " +
                          $"{ActiveLevel.BossCountdownSeconds:0.#} 秒。");
            }
        }

        private void UpdateBossRegen()
        {
            float regenPerSecond = ActiveLevel.BossHealthRegenPerSecond;

            if (regenPerSecond <= 0f)
            {
                return;
            }

            bossRegenAccumulator += regenPerSecond * Time.deltaTime;

            // 小步回血，避免每帧都触发一次血量变化事件
            if (bossRegenAccumulator < 0.25f)
            {
                return;
            }

            trackedOre.Heal(bossRegenAccumulator);
            bossRegenAccumulator = 0f;
        }

        /// <summary>
        /// 进入 / 回到关卡时把该关卡的波次、BOSS、通关状态接上。
        /// 可重复调用：同一状态下不会重置已经打掉的血量或进度。
        /// </summary>
        private void ApplyLevelFlow(LevelEntrance entrance)
        {
            if (entrance == null)
            {
                return;
            }

            EnsureTrackedOre();

            if (oreSpawner == null)
            {
                if (!warnedMissingSpawner)
                {
                    warnedMissingSpawner = true;
                    Debug.LogError("[LevelManager] 没有 OreSpawner，波次与 BOSS 无法推进。" +
                                   "请运行菜单「屿见/关卡/配置关卡入口（阶段 1）」。", this);
                }

                return;
            }

            if (flowLevel != entrance)
            {
                if (bossPresentationApplied)
                {
                    RestoreOreScale();
                }

                flowLevel = entrance;
                bossPresentationApplied = false;
                bossRegenAccumulator = 0f;
                bossCountdownRemaining = 0f;
            }

            oreSpawner.SetGroupOverride(entrance.AggregateGroups);

            LevelProgress progress = GetProgress(entrance);

            if (progress.BossDefeated)
            {
                oreSpawner.SetPaused(true);
                oreSpawner.StopSpawning();
                bossCountdownRemaining = 0f;
                RestoreOreScale();
                ToastUI.Show("该关卡已净化");

                if (logLevelFlow)
                {
                    Debug.Log($"[LevelManager] 关卡 {entrance.LevelNumber} 已通关，不再刷新聚合体。");
                }

                return;
            }

            if (progress.BossAppeared)
            {
                // 离开过关卡时这里会按满血重新生成 BOSS（策划案 2.3.3 的「重置」）
                if (!bossPresentationApplied)
                {
                    BeginBossPresentation(entrance);
                }

                return;
            }

            RestoreOreScale();
            SetBossDropMode(false);
            bossCountdownRemaining = 0f;

            progress.CurrentWave = Mathf.Max(1, progress.ClearedAggregates + 1);

            // 小怪阶段：确保生成器没有被上一关的暂停状态卡住
            if (oreSpawner.IsPaused)
            {
                oreSpawner.SetPaused(false);
            }

            if (logLevelFlow)
            {
                Debug.Log($"[LevelManager] 关卡 {entrance.LevelNumber} 小怪阶段：" +
                          $"已清除 {progress.ClearedAggregates}/{entrance.WaveCount} 波，" +
                          $"剩余 {Mathf.Max(0, entrance.WaveCount - progress.ClearedAggregates)} 波。");
            }
        }

        /// <summary>小怪清完后把共享矿石切换成 BOSS。</summary>
        private void BeginBossPresentation(LevelEntrance entrance)
        {
            LevelProgress progress = GetProgress(entrance);
            progress.BossAppeared = true;
            bossPresentationApplied = true;
            bossRegenAccumulator = 0f;

            oreSpawner.SetPaused(true);
            oreSpawner.StopSpawning();
            ApplyOreScale(entrance.BossScale);

            // BOSS 不走「每掉 20% 掉一档」的碎片结算：否则半血退出再重进可以反复刷奖励。
            // 停用掉落组件后，奖励由击杀时的常规流程一次性发放。
            SetBossDropMode(true);

            if (!oreSpawner.SpawnBoss(entrance.BossOreType, entrance.BossMaxHealth, entrance.BossReward))
            {
                bossPresentationApplied = false;
                return;
            }

            // 策划案 2.3.6：BOSS 刷新时开启倒计时
            bossCountdownRemaining = entrance.BossCountdownSeconds;
            ToastUI.Show("BOSS 出现！");

            if (logLevelFlow)
            {
                Debug.Log($"[LevelManager] 关卡 {entrance.LevelNumber}：{entrance.WaveCount} 波小怪已清除，" +
                          $"BOSS 出现（{entrance.BossMaxHealth:0.##} HP，每秒回血 {entrance.BossHealthRegenPerSecond:0.##}，" +
                          $"倒计时 {entrance.BossCountdownSeconds:0.#} 秒）。");
            }
        }

        /// <summary>聚合体被击碎。小怪阶段累计波次，BOSS 阶段直接通关。</summary>
        private void HandleOreDied(Ore deadOre)
        {
            if (deadOre != trackedOre || ActiveLevel == null || oreSpawner == null)
            {
                return;
            }

            LevelProgress progress = GetProgress(ActiveLevel);

            if (progress.BossDefeated)
            {
                return;
            }

            if (progress.BossAppeared)
            {
                progress.BossDefeated = true;
                HandleLevelCleared(ActiveLevel);
                return;
            }

            // 策划案 2.3.6：一个垃圾聚合体就是一个波次
            progress.ClearedAggregates++;
            progress.CurrentWave = progress.ClearedAggregates + 1;

            if (progress.ClearedAggregates < ActiveLevel.WaveCount)
            {
                if (logLevelFlow)
                {
                    Debug.Log($"[LevelManager] 第 {progress.ClearedAggregates}/{ActiveLevel.WaveCount} 波已清除，" +
                              $"剩余 {ActiveLevel.WaveCount - progress.ClearedAggregates} 波。");
                }

                return;
            }

            BeginBossPresentation(ActiveLevel);
        }

        /// <summary>BOSS 被击碎 = 通关（策划案 2.3.4）。保留进度，停止刷新。</summary>
        private void HandleLevelCleared(LevelEntrance entrance)
        {
            if (entrance == null)
            {
                return;
            }

            if (oreSpawner != null)
            {
                oreSpawner.SetPaused(true);
                oreSpawner.StopSpawning();
            }

            bossPresentationApplied = false;
            bossCountdownRemaining = 0f;
            bossRegenAccumulator = 0f;
            RestoreOreScale();

            // 阶段 3：占地解除封锁（污染核心格除外），视觉层换成大树 + 花草
            if (buildingGrid != null &&
                buildingGrid.SetLevelFootprintUnlocked(GetLevelOwnerId(entrance), true))
            {
                if (gridVisualizer != null)
                {
                    gridVisualizer.RebuildGridMesh();
                }
            }

            RefreshEnvironmentVisual(entrance);
            ToastUI.Show($"关卡净化完成！{ClearedAutoExitDelay:0} 秒后自动退出关卡");

            // 策划案 2.3.6：BOSS 被击败后倒计时暂停，玩家停留 3 秒未退出则自动退出
            CancelAutoExit();
            autoExitRoutine = StartCoroutine(AutoExitAfterCleared(entrance));

            if (logLevelFlow)
            {
                Debug.Log($"[LevelManager] 关卡 {entrance.LevelNumber}（{entrance.name}）已通关：" +
                          $"累计清除 {GetProgress(entrance).ClearedAggregates} 个聚合体，BOSS 已被清除。" +
                          $"{ClearedAutoExitDelay:0} 秒后自动退出关卡。");
            }
        }

        private IEnumerator AutoExitAfterCleared(LevelEntrance entrance)
        {
            yield return new WaitForSecondsRealtime(ClearedAutoExitDelay);
            autoExitRoutine = null;

            if (areaController == null || areaController.CurrentArea != GameArea.Mining)
            {
                yield break;
            }

            if (ActiveLevel != entrance || transitioning)
            {
                yield break;
            }

            if (logLevelFlow)
            {
                Debug.Log("[LevelManager] BOSS 已被击败并停留 3 秒，自动退出关卡。");
            }

            ExitLevel();
        }

        private void CancelAutoExit()
        {
            if (autoExitRoutine != null)
            {
                StopCoroutine(autoExitRoutine);
                autoExitRoutine = null;
            }
        }

        /// <summary>读取（必要时创建）某个关卡的进度。</summary>
        public LevelProgress GetProgress(LevelEntrance entrance)
        {
            if (entrance == null)
            {
                return null;
            }

            if (!progressByEntrance.TryGetValue(entrance, out LevelProgress progress))
            {
                progress = new LevelProgress();
                progressByEntrance.Add(entrance, progress);
            }

            return progress;
        }

        /// <summary>净化进度 0~1，供阶段 3 的视觉层使用。</summary>
        public float GetPurificationProgress(LevelEntrance entrance)
        {
            if (entrance == null)
            {
                return 0f;
            }

            return GetProgress(entrance).GetPurificationProgress(entrance.TotalAggregates);
        }

        /// <summary>该关卡是否已经通关（BOSS 已被清除）。</summary>
        public bool IsLevelCleared(LevelEntrance entrance)
        {
            return entrance != null && GetProgress(entrance).IsCleared;
        }

        // ---------------- 阶段 3：占地封锁 / 视觉层 ----------------

        /// <summary>关卡在建筑网格里的 ownerId，与 LevelEntrance 一一对应。</summary>
        private static int GetLevelOwnerId(LevelEntrance entrance)
        {
            return entrance != null ? entrance.GetInstanceID() : 0;
        }

        /// <summary>
        /// 把每个关卡的污染区登记到建筑网格：净化前整块不可建，净化后只保留核心格。
        /// 占地矩形以污染核心所在格为中心。
        /// </summary>
        private void RegisterLevelFootprints()
        {
            if (buildingGrid == null)
            {
                Debug.LogWarning("[LevelManager] 场景里没有 BuildingGrid，关卡污染区不会封锁建筑占地。", this);
                return;
            }

            if (entrances == null)
            {
                return;
            }

            for (int i = 0; i < entrances.Length; i++)
            {
                LevelEntrance entrance = entrances[i];

                if (entrance == null)
                {
                    continue;
                }

                if (!buildingGrid.WorldToCell(entrance.PollutionCore.position, out Vector2Int coreCell))
                {
                    Debug.LogWarning($"[LevelManager] 关卡 {entrance.LevelNumber}（{entrance.name}）" +
                                     "的污染核心不在建筑网格上，跳过占地封锁。", this);
                    continue;
                }

                Vector2Int size = entrance.FootprintCells;
                RectInt rect = new RectInt(
                    coreCell.x - size.x / 2,
                    coreCell.y - size.y / 2,
                    size.x,
                    size.y);

                bool unlocked = IsLevelCleared(entrance);
                buildingGrid.RegisterLevelFootprint(GetLevelOwnerId(entrance), rect, coreCell, unlocked);

                if (logLevelFlow)
                {
                    Debug.Log($"[LevelManager] 关卡 {entrance.LevelNumber} 占地 {size.x}×{size.y}，" +
                              $"格子 {rect}，核心格 {coreCell}，{(unlocked ? "已净化" : "未净化")}。");
                }
            }
        }

        private void UnregisterLevelFootprints()
        {
            if (buildingGrid == null || entrances == null)
            {
                return;
            }

            for (int i = 0; i < entrances.Length; i++)
            {
                if (entrances[i] != null)
                {
                    buildingGrid.UnregisterLevelFootprint(GetLevelOwnerId(entrances[i]));
                }
            }
        }

        /// <summary>为每个入口生成（或复用）程序化占位视觉。阶段 3 用，可整体删除。</summary>
        private void EnsureEnvironmentVisuals()
        {
            if (entrances == null)
            {
                return;
            }

            for (int i = 0; i < entrances.Length; i++)
            {
                LevelEntrance entrance = entrances[i];

                if (entrance == null)
                {
                    continue;
                }

                if (!environmentVisuals.TryGetValue(entrance, out LevelEnvironmentVisual visual) ||
                    visual == null)
                {
                    visual = entrance.GetComponentInChildren<LevelEnvironmentVisual>(true);

                    if (visual == null)
                    {
                        GameObject visualObject = new GameObject($"LevelEnvironment_Lv{entrance.LevelNumber}");
                        visualObject.transform.SetParent(entrance.transform.parent, false);
                        visualObject.transform.position = entrance.PollutionCore.position;
                        visual = visualObject.AddComponent<LevelEnvironmentVisual>();
                    }

                    environmentVisuals[entrance] = visual;
                }

                visual.Configure(entrance, this, buildingGrid);
                visual.Refresh();
            }
        }

        /// <summary>刷新单个关卡的污染 / 净化视觉。通关时调用。</summary>
        private void RefreshEnvironmentVisual(LevelEntrance entrance)
        {
            if (entrance == null)
            {
                return;
            }

            if (!environmentVisuals.TryGetValue(entrance, out LevelEnvironmentVisual visual) ||
                visual == null)
            {
                visual = entrance.GetComponentInChildren<LevelEnvironmentVisual>(true);
            }

            if (visual == null)
            {
                return;
            }

            environmentVisuals[entrance] = visual;
            visual.Configure(entrance, this, buildingGrid);
            visual.Refresh();
        }

        /// <summary>找到共享矿石并订阅它的死亡事件。BOSS 战也复用这一块矿石。</summary>
        private void EnsureTrackedOre()
        {
            Ore ore = oreSpawner != null ? oreSpawner.CurrentOre : null;

            if (ore == null)
            {
                ore = FindFirstObjectByType<Ore>(FindObjectsInactive.Include);
            }

            if (ore == trackedOre)
            {
                return;
            }

            UnsubscribeTrackedOre();
            trackedOre = ore;

            if (ore == null)
            {
                return;
            }

            trackedOreDefaultScale = ore.transform.localScale;
            trackedScaleCaptured = true;
            ore.OnDied += HandleOreDied;
        }

        private void UnsubscribeTrackedOre()
        {
            if (trackedOre != null)
            {
                trackedOre.OnDied -= HandleOreDied;
            }

            trackedOre = null;
            trackedScaleCaptured = false;
        }

        /// <summary>
        /// 切换「碎片分档掉落」模式。BOSS 战关闭，普通小怪开启。
        /// 关闭时 GarbageDropper.OnDisable 会把 SuppressDeathReward 复位，
        /// 因此 BOSS 被击碎时仍会按 Ore.Reward 一次性发放奖励。
        /// </summary>
        private void SetBossDropMode(bool bossMode)
        {
            if (trackedOre == null)
            {
                return;
            }

            GarbageDropper dropper = trackedOre.GetComponent<GarbageDropper>();

            if (dropper != null && dropper.enabled == bossMode)
            {
                dropper.enabled = !bossMode;
            }
        }

        private void ApplyOreScale(float multiplier)
        {
            if (trackedOre == null || !trackedScaleCaptured)
            {
                return;
            }

            trackedOre.transform.localScale = trackedOreDefaultScale * Mathf.Max(1f, multiplier);
        }

        private void RestoreOreScale()
        {
            if (trackedOre == null || !trackedScaleCaptured)
            {
                return;
            }

            trackedOre.transform.localScale = trackedOreDefaultScale;
        }

        private void ResolveDependencies()
        {
            if (playerLevel == null)
            {
                playerLevel = FindFirstObjectByType<PlayerLevel>(FindObjectsInactive.Include);
            }

            if (areaController == null)
            {
                areaController = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);
            }

            if (oreSpawner == null)
            {
                oreSpawner = FindFirstObjectByType<OreSpawner>(FindObjectsInactive.Include);
            }

            if (buildingGrid == null)
            {
                buildingGrid = FindFirstObjectByType<BuildingGrid>(FindObjectsInactive.Include);
            }

            if (gridVisualizer == null)
            {
                gridVisualizer = FindFirstObjectByType<BuildingGridVisualizer>(FindObjectsInactive.Include);
            }
        }

        // ---------------- 关卡暂停 ----------------

        /// <summary>
        /// 开关关卡内容。离开关卡时：
        ///   · 矿石生成器停止刷新；
        ///   · 内容根下的粒子暂停、动画速度归零。
        /// 建筑对关卡目标的伤害由 OrganMuseumAutoAttack 自己按区域挡掉。
        /// </summary>
        private void SetLevelRunning(bool running)
        {
            if (levelRunning == running)
            {
                return;
            }

            levelRunning = running;

            if (oreSpawner != null)
            {
                oreSpawner.SetPaused(!running);
            }

            if (running)
            {
                ResumePausedTargets();
            }
            else
            {
                PauseTargets();
            }

            if (logLevelFlow)
            {
                Debug.Log($"[LevelManager] 关卡内容{(running ? "恢复" : "暂停")}运行。");
            }
        }

        private void PauseTargets()
        {
            Transform root = ResolvePauseRoot();

            if (root == null)
            {
                if (logLevelFlow)
                {
                    Debug.Log("[LevelManager] 没有找到关卡内容根，跳过粒子 / 动画暂停。");
                }

                return;
            }

            pausedParticles = root.GetComponentsInChildren<ParticleSystem>(true);
            particleWasPlaying = new bool[pausedParticles.Length];

            for (int i = 0; i < pausedParticles.Length; i++)
            {
                ParticleSystem particles = pausedParticles[i];

                if (particles == null)
                {
                    continue;
                }

                particleWasPlaying[i] = particles.isPlaying;

                if (particleWasPlaying[i])
                {
                    particles.Pause(true);
                }
            }

            pausedAnimators = root.GetComponentsInChildren<Animator>(true);
            animatorSpeeds = new float[pausedAnimators.Length];

            for (int i = 0; i < pausedAnimators.Length; i++)
            {
                Animator animator = pausedAnimators[i];

                if (animator == null)
                {
                    continue;
                }

                animatorSpeeds[i] = animator.speed;
                animator.speed = 0f;
            }
        }

        private void ResumePausedTargets()
        {
            if (pausedParticles != null && particleWasPlaying != null)
            {
                for (int i = 0; i < pausedParticles.Length; i++)
                {
                    if (pausedParticles[i] != null && particleWasPlaying[i])
                    {
                        pausedParticles[i].Play(true);
                    }
                }
            }

            if (pausedAnimators != null && animatorSpeeds != null)
            {
                for (int i = 0; i < pausedAnimators.Length; i++)
                {
                    if (pausedAnimators[i] != null)
                    {
                        pausedAnimators[i].speed = animatorSpeeds[i];
                    }
                }
            }
        }

        private Transform ResolvePauseRoot()
        {
            if (ActiveLevel != null && ActiveLevel.ContentRoot != null)
            {
                return ActiveLevel.ContentRoot;
            }

            if (fallbackContentRoot != null)
            {
                return fallbackContentRoot;
            }

            Ore ore = FindFirstObjectByType<Ore>(FindObjectsInactive.Include);
            return ore != null ? ore.transform : null;
        }
    }
}
