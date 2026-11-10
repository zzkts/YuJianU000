using UnityEngine;
using Yujian.Core;
using Yujian.Mining;

namespace Yujian.Levels
{
    /// <summary>
    /// 地图上的一个关卡入口（策划案 2.3.1「关卡入口 GameObject」）。
    ///
    /// 公共属性（都在 Inspector 上可调）：
    ///   · 解锁等级：玩家等级低于它时整棵子树灰化，点击只提示「当前等级不足」；
    ///   · 占用面积格：关卡在岛屿建筑网格上占用的格数（阶段 3 用于封锁可建区域）；
    ///   · 内容根 / 污染核心：关卡内容与点击点；
    ///   · 垃圾聚合体组、波次、BOSS 在阶段 2 接入。
    ///
    /// 本类只负责「这一个入口」的状态与表现：
    ///   · 灰化 / 还原（按原材质逐个克隆灰化材质，保留贴图与明暗）；
    ///   · 被点击时转交给 <see cref="LevelManager"/> 判断能不能进。
    /// 黑场转场、关卡暂停、等级订阅都在 LevelManager，避免每个入口各做一份。
    /// </summary>
    [DisallowMultipleComponent]
    public class LevelEntrance : MonoBehaviour
    {
        [Header("关卡属性")]
        [Tooltip("关卡号数。只用于日志 / 后续界面对应，不参与判定")]
        [SerializeField, Min(1)] private int levelNumber = 1;

        [Tooltip("解锁等级。玩家等级小于它时关卡灰化、不能进入")]
        [SerializeField, Min(1)] private int unlockLevel = 1;

        [Tooltip("关卡在岛屿建筑网格上占用的格数（阶段 3 使用）")]
        [SerializeField] private Vector2Int footprintCells = new Vector2Int(4, 4);

        [Header("波次与 BOSS（阶段 2）")]
        [Tooltip("总波次。按策划案 2.3.6，一个垃圾聚合体就是一个波次，" +
                 "所以这里也是本关要清除的聚合体总数，清完进入 BOSS 战")]
        [SerializeField, Min(1)] private int waveCount = 3;

        [Tooltip("本关专属的垃圾聚合体组。留空则使用全局矿石等级表里的组；" +
                 "填了以后只从这些组里抽，等级仍用于血量 / 奖励成长")]
        [SerializeField] private OreGroup[] aggregateGroups = new OreGroup[0];

        [Tooltip("BOSS 最大生命值（占位数值，Inspector 可调）")]
        [SerializeField, Min(1f)] private float bossMaxHealth = 40f;

        [Tooltip("BOSS 被清除时发放的垃圾总量（占位数值）")]
        [SerializeField, Min(0)] private int bossReward = 80;

        [Tooltip("BOSS 每秒回复的生命值。0 表示不回血；离开关卡时停止回血")]
        [SerializeField, Min(0f)] private float bossHealthRegenPerSecond = 0.5f;

        [Tooltip("BOSS 战倒计时（秒）。策划案 2.3.6 默认 30；倒计时结束前没击败 BOSS，" +
                 "BOSS 会瞬间回满血并重启倒计时。每个关卡可单独调")]
        [SerializeField, Min(1f)] private float bossCountdownSeconds = 30f;

        [Tooltip("BOSS 相对普通聚合体的体型倍数。阶段 2 用体型区分 BOSS，阶段 3 再换正式外观")]
        [SerializeField, Min(1f)] private float bossScale = 1.6f;

        [Tooltip("BOSS 使用的占位矿石种类。阶段 3 换正式 BOSS 外观后不再使用")]
        [SerializeField] private OreType bossOreType = OreType.HuaGangYan;

        [Header("场景引用")]
        [Tooltip("灰化作用的根物体。解锁前这一整棵子树里的 Renderer 都会换成灰色材质。" +
                 "留空使用自身")]
        [SerializeField] private Transform grayTarget;

        [Tooltip("关卡内容根（聚合体、BOSS、粒子等）。离开关卡时暂停它下面的粒子和动画。" +
                 "阶段 1 由向导填成矿石；阶段 2 / 3 再换成完整的关卡内容根")]
        [SerializeField] private Transform contentRoot;

        [Tooltip("污染核心。点击这里进入关卡；留空使用自身")]
        [SerializeField] private Transform pollutionCore;

        [Tooltip("进入挖矿区的旧入口组件。留空时自己切区域")]
        [SerializeField] private MiningEntrance miningEntrance;

        [Header("灰化材质")]
        [Tooltip("灰化材质模板。运行时会按原材质逐个克隆它；留空时用 Shader.Find 兜底")]
        [SerializeField] private Material lockedMaterial;

        [Tooltip("灰化 Shader 名。lockedMaterial 为空时按这个名字查找")]
        [SerializeField] private string lockedShaderName = "Yujian/Level Block";

        [Header("调试")]
        [Tooltip("是否在 Console 打印灰化 / 进入日志")]
        [SerializeField] private bool logLevelFlow = true;

        private Renderer[] grayRenderers;
        private Material[][] originalMaterials;
        private Material[][] grayMaterials;
        private bool locked;
        private bool runtimeMaterialCreated;
        private LevelManager manager;

        /// <summary>关卡号数。</summary>
        public int LevelNumber => levelNumber;

        /// <summary>解锁等级。</summary>
        public int UnlockLevel => unlockLevel;

        /// <summary>关卡占用格数（阶段 3）。</summary>
        public Vector2Int FootprintCells => footprintCells;

        /// <summary>进入 BOSS 战前要清除的波数（一个聚合体 = 一波）。</summary>
        public int WaveCount => Mathf.Max(1, waveCount);

        /// <summary>本关专属聚合体组；为空表示使用全局矿石等级表。</summary>
        public OreGroup[] AggregateGroups => aggregateGroups;

        /// <summary>清完全部小怪一共要打掉多少个聚合体（一个聚合体 = 一波）。</summary>
        public int TotalAggregates => WaveCount;

        /// <summary>BOSS 最大生命值。</summary>
        public float BossMaxHealth => Mathf.Max(1f, bossMaxHealth);

        /// <summary>BOSS 击碎奖励。</summary>
        public int BossReward => Mathf.Max(0, bossReward);

        /// <summary>BOSS 每秒回血量。</summary>
        public float BossHealthRegenPerSecond => Mathf.Max(0f, bossHealthRegenPerSecond);

        /// <summary>BOSS 战倒计时（秒）。旧场景没有序列化这个字段时按 30 兜底。</summary>
        public float BossCountdownSeconds => bossCountdownSeconds > 0f ? bossCountdownSeconds : 30f;

        /// <summary>BOSS 体型倍数。</summary>
        public float BossScale => Mathf.Max(1f, bossScale);

        /// <summary>BOSS 占位矿石种类。</summary>
        public OreType BossOreType => bossOreType;

        /// <summary>关卡内容根，离开关卡时暂停它。</summary>
        public Transform ContentRoot => contentRoot;

        /// <summary>污染核心（点击点）。</summary>
        public Transform PollutionCore => pollutionCore != null ? pollutionCore : transform;

        /// <summary>当前是否处于灰化（未解锁）状态。</summary>
        public bool IsLocked => locked;

        private void Awake()
        {
            if (grayTarget == null)
            {
                grayTarget = transform;
            }

            if (pollutionCore == null)
            {
                pollutionCore = transform;
            }

            if (miningEntrance == null)
            {
                miningEntrance = GetComponent<MiningEntrance>();
            }
        }

        private void OnDestroy()
        {
            DestroyGrayMaterials();

            if (runtimeMaterialCreated && lockedMaterial != null)
            {
                DestroyMaterialSafe(lockedMaterial);
            }
        }

        /// <summary>玩家等级是否够解锁本关。</summary>
        public bool IsUnlocked(int playerLevel)
        {
            return playerLevel >= unlockLevel;
        }

        /// <summary>入口被点击。锁定 / 转场判断交给 LevelManager 统一处理。</summary>
        public void TryEnter()
        {
            ResolveManager();

            if (manager == null)
            {
                // 没有关卡管理器时至少保持旧行为：直接进挖矿区，不让入口变成死物
                Debug.LogWarning("[LevelEntrance] 场景里没有 LevelManager，本次直接进入挖矿区，" +
                                 "跳过解锁判断与黑场转场。请运行菜单「屿见/关卡/配置关卡入口（阶段 1）」。", this);
                ActivateMiningArea();
                return;
            }

            manager.TryEnter(this);
        }

        /// <summary>真正切换到挖矿区。优先复用旧的 MiningEntrance，保证它原有的显隐逻辑不变。</summary>
        public void ActivateMiningArea()
        {
            if (miningEntrance != null)
            {
                miningEntrance.Activate();
                return;
            }

            GameAreaController areas = FindFirstObjectByType<GameAreaController>(FindObjectsInactive.Include);

            if (areas == null)
            {
                Debug.LogError("[LevelEntrance] 找不到 GameAreaController，无法进入关卡。", this);
                return;
            }

            areas.SetArea(GameArea.Mining);
        }

        /// <summary>按玩家等级刷新灰化状态。等级变化时由 LevelManager 调用。</summary>
        public void ApplyLockState(int playerLevel)
        {
            SetLocked(playerLevel < unlockLevel);
        }

        /// <summary>直接设置灰化状态（解锁时还原原材质）。</summary>
        public void SetLocked(bool value)
        {
            if (locked == value)
            {
                return;
            }

            locked = value;

            if (locked)
            {
                ApplyGray();
            }
            else
            {
                RestoreOriginalMaterials();
            }

            if (logLevelFlow)
            {
                Debug.Log($"[LevelEntrance] 关卡 {levelNumber}（{name}）{(locked ? "灰化：等级不足" : "解锁：还原材质")}。", this);
            }
        }

        /// <summary>编辑器向导调用，一次接好这一个入口的全部引用与阶段 2 占位数值。</summary>
        public void ConfigureForSetup(int newLevelNumber, int newUnlockLevel, Vector2Int newFootprintCells,
            Transform newGrayTarget, Transform newContentRoot, Transform newPollutionCore, Material newLockedMaterial,
            int newWaveCount = 3, OreGroup[] newAggregateGroups = null,
            float newBossMaxHealth = 40f, int newBossReward = 80, float newBossHealthRegenPerSecond = 0.5f,
            float newBossScale = 1.6f, OreType newBossOreType = OreType.HuaGangYan,
            float newBossCountdownSeconds = 30f)
        {
            levelNumber = Mathf.Max(1, newLevelNumber);
            unlockLevel = Mathf.Max(1, newUnlockLevel);
            footprintCells = new Vector2Int(
                Mathf.Max(1, newFootprintCells.x),
                Mathf.Max(1, newFootprintCells.y));
            waveCount = Mathf.Max(1, newWaveCount);
            aggregateGroups = newAggregateGroups ?? new OreGroup[0];
            bossMaxHealth = Mathf.Max(1f, newBossMaxHealth);
            bossReward = Mathf.Max(0, newBossReward);
            bossHealthRegenPerSecond = Mathf.Max(0f, newBossHealthRegenPerSecond);
            bossCountdownSeconds = Mathf.Max(1f, newBossCountdownSeconds);
            bossScale = Mathf.Max(1f, newBossScale);
            bossOreType = newBossOreType;
            grayTarget = newGrayTarget != null ? newGrayTarget : transform;
            contentRoot = newContentRoot;
            pollutionCore = newPollutionCore != null ? newPollutionCore : transform;
            lockedMaterial = newLockedMaterial;
            miningEntrance = GetComponent<MiningEntrance>();

            // 重新灰化时需要按新目标重新缓存渲染器
            ClearRendererCache();
        }

        /// <summary>
        /// 重跑向导时只补上缺失的灰化材质，不覆盖入口上已经调好的解锁等级 / 占地 / 内容根。
        /// </summary>
        public void EnsureLockedMaterialForSetup(Material material)
        {
            if (lockedMaterial == null)
            {
                lockedMaterial = material;
            }
        }

        private void ResolveManager()
        {
            if (manager == null)
            {
                manager = FindFirstObjectByType<LevelManager>(FindObjectsInactive.Include);
            }
        }

        // ---------------- 灰化 ----------------

        private void ApplyGray()
        {
            Material template = ResolveLockedMaterial();

            if (template == null)
            {
                return;
            }

            EnsureRendererCache();

            for (int i = 0; i < grayRenderers.Length; i++)
            {
                Renderer renderer = grayRenderers[i];

                if (renderer == null)
                {
                    continue;
                }

                Material[] originals = originalMaterials[i];

                if (originals == null || originals.Length == 0)
                {
                    continue;
                }

                Material[] clones = new Material[originals.Length];

                for (int j = 0; j < originals.Length; j++)
                {
                    clones[j] = CreateGrayClone(template, originals[j]);
                }

                renderer.sharedMaterials = clones;
                grayMaterials[i] = clones;
            }
        }

        private void RestoreOriginalMaterials()
        {
            if (grayRenderers == null || originalMaterials == null)
            {
                return;
            }

            for (int i = 0; i < grayRenderers.Length; i++)
            {
                Renderer renderer = grayRenderers[i];

                if (renderer != null && originalMaterials[i] != null)
                {
                    renderer.sharedMaterials = originalMaterials[i];
                }
            }

            DestroyGrayMaterials();
        }

        private void EnsureRendererCache()
        {
            if (grayRenderers != null)
            {
                return;
            }

            Transform target = grayTarget != null ? grayTarget : transform;
            grayRenderers = target.GetComponentsInChildren<Renderer>(true);
            originalMaterials = new Material[grayRenderers.Length][];
            grayMaterials = new Material[grayRenderers.Length][];

            for (int i = 0; i < grayRenderers.Length; i++)
            {
                Renderer renderer = grayRenderers[i];
                originalMaterials[i] = renderer != null ? renderer.sharedMaterials : null;
            }
        }

        private void ClearRendererCache()
        {
            // 正在灰化时先换回原材质，否则重新缓存后旧克隆材质会留在物体上
            if (locked)
            {
                RestoreOriginalMaterials();
                locked = false;
            }

            DestroyGrayMaterials();
            grayRenderers = null;
            originalMaterials = null;
            grayMaterials = null;
        }

        private Material ResolveLockedMaterial()
        {
            if (lockedMaterial != null)
            {
                return lockedMaterial;
            }

            Shader shader = Shader.Find(lockedShaderName);

            if (shader == null)
            {
                Debug.LogError($"[LevelEntrance] 找不到灰化 Shader「{lockedShaderName}」。" +
                               "关卡会保持原材质，未解锁提示仍然生效。" +
                               "请运行菜单「屿见/关卡/配置关卡入口（阶段 1）」生成材质。", this);
                return null;
            }

            lockedMaterial = new Material(shader)
            {
                name = "LevelBlock_Runtime",
            };
            runtimeMaterialCreated = true;
            return lockedMaterial;
        }

        /// <summary>
        /// 按原材质克隆一份灰化材质：保留贴图 / 基础色 / 贴图缩放，只把 Shader 换成灰度版本。
        /// 没有贴图或原材质不是 URP 材质时也能工作，退化成纯基础色的灰。
        /// </summary>
        private static Material CreateGrayClone(Material template, Material source)
        {
            Material clone = new Material(template);

            if (source == null)
            {
                return clone;
            }

            if (source.HasProperty("_BaseMap"))
            {
                Texture baseMap = source.GetTexture("_BaseMap");

                if (baseMap != null)
                {
                    clone.SetTexture("_BaseMap", baseMap);
                    clone.SetTextureScale("_BaseMap", source.GetTextureScale("_BaseMap"));
                    clone.SetTextureOffset("_BaseMap", source.GetTextureOffset("_BaseMap"));
                }
            }
            else if (source.HasProperty("_MainTex"))
            {
                Texture mainTex = source.GetTexture("_MainTex");

                if (mainTex != null)
                {
                    clone.SetTexture("_BaseMap", mainTex);
                    clone.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
                    clone.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
                }
            }

            if (source.HasProperty("_BaseColor"))
            {
                clone.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            }
            else if (source.HasProperty("_Color"))
            {
                clone.SetColor("_BaseColor", source.GetColor("_Color"));
            }

            return clone;
        }

        private void DestroyGrayMaterials()
        {
            if (grayMaterials == null)
            {
                return;
            }

            for (int i = 0; i < grayMaterials.Length; i++)
            {
                Material[] materials = grayMaterials[i];

                if (materials == null)
                {
                    continue;
                }

                for (int j = 0; j < materials.Length; j++)
                {
                    if (materials[j] != null)
                    {
                        DestroyMaterialSafe(materials[j]);
                    }
                }
            }

            grayMaterials = null;
        }

        /// <summary>
        /// 运行时用 Destroy，编辑器（向导 / 批处理验收）用 DestroyImmediate，
        /// 否则 Unity 会报「Destroy may not be called from edit mode」。
        /// </summary>
        private static void DestroyMaterialSafe(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }
    }
}
