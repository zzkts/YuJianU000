using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 垃圾聚合体的碎片掉落。
    /// 每损失一定比例的最大生命值，就从外壳上摘下一组垃圾块，交给 <see cref="GarbageCollector"/> 排队收集。
    ///
    /// 只负责「什么时候掉、掉多少、掉几块」，不直接加钱、不处理飞行与 UI。
    /// </summary>
    public class GarbageDropper : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("垃圾聚合体本体。留空时自动取本物体上的 Ore")]
        [SerializeField] private Ore ore;

        [Tooltip("外壳表现。留空时自动在子物体里查找")]
        [SerializeField] private GarbageAggregateVisual visual;

        [Tooltip("收集队列。留空时自动在场景里查找")]
        [SerializeField] private GarbageCollector collector;

        [Header("掉落节奏")]
        [Tooltip("每损失多少比例的最大生命值掉落一次。0.2 = 每 20%")]
        [SerializeField, Range(0.01f, 1f)] private float dropHealthStep = 0.2f;

        [Tooltip("每次掉落增加多少垃圾。0 = 用聚合体总奖励 × 掉落比例自动算")]
        [SerializeField, Min(0)] private int garbagePerDrop = 0;

        [Tooltip("每次掉落摘下几块垃圾。0 = 外壳块数 × 掉落比例自动算")]
        [SerializeField, Min(0)] private int piecesPerDrop = 0;

        [Tooltip("是否把死亡奖励交给本系统分档发放。开启后 MiningInput / 建筑自动攻击不会再发一次")]
        [SerializeField] private bool takeOverDeathReward = true;

        [Header("调试")]
        [Tooltip("是否在 Console 打印每档掉落日志")]
        [SerializeField] private bool logDropFlow = true;

        private readonly List<float> thresholds = new List<float>();
        private readonly List<int> dropAmounts = new List<int>();
        private readonly List<GameObject> detachBuffer = new List<GameObject>();

        private int nextDropIndex;
        private bool armed;
        private bool subscribed;

        /// <summary>当前计划里的掉落档数（含死亡时的 0%）。</summary>
        public int DropStepCount => thresholds.Count;

        private void Awake()
        {
            if (ore == null)
            {
                ore = GetComponent<Ore>();
            }

            if (visual == null)
            {
                visual = GetComponentInChildren<GarbageAggregateVisual>(true);
            }

            if (collector == null)
            {
#if UNITY_2023_1_OR_NEWER
                collector = Object.FindFirstObjectByType<GarbageCollector>(FindObjectsInactive.Include);
#else
                collector = Object.FindFirstObjectByType<GarbageCollector>();
#endif
            }

            if (ore == null)
            {
                Debug.LogError("[GarbageDropper] 找不到 Ore，碎片掉落不会生效。", this);
            }
        }

        private void OnEnable()
        {
            Subscribe();

            if (takeOverDeathReward && ore != null && visual != null && collector != null)
            {
                ore.SuppressDeathReward = true;
            }
            else if (takeOverDeathReward && ore != null)
            {
                Debug.LogError("[GarbageDropper] 依赖不完整（Ore / 外壳 / 收集器），" +
                               "死亡奖励仍由原流程发放。请运行「屿见/清洁/配置垃圾聚合体」。", this);
            }
        }

        private void OnDisable()
        {
            Unsubscribe();

            if (ore != null)
            {
                ore.SuppressDeathReward = false;
            }
        }

        private void Start()
        {
            // OreSpawner 在 Start 里调用 ApplyDefinition 时会再重置一次；
            // 这里兜底处理「没有外部生成器」的场景。
            ResetForSpawn();
        }

        /// <summary>编辑器向导调用。</summary>
        public void ConfigureForSetup(Ore newOre, GarbageAggregateVisual newVisual,
            GarbageCollector newCollector)
        {
            ore = newOre;
            visual = newVisual;
            collector = newCollector;
        }

        private void Subscribe()
        {
            if (subscribed || ore == null)
            {
                return;
            }

            ore.OnHealthChanged += HandleHealthChanged;
            ore.OnDefinitionApplied += HandleDefinitionApplied;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || ore == null)
            {
                return;
            }

            ore.OnHealthChanged -= HandleHealthChanged;
            ore.OnDefinitionApplied -= HandleDefinitionApplied;
            subscribed = false;
        }

        private void HandleDefinitionApplied(Ore _)
        {
            ResetForSpawn();
        }

        /// <summary>
        /// 新一块聚合体出现：按当前最大生命值与奖励重新排档，并补回外壳。
        /// </summary>
        public void ResetForSpawn()
        {
            if (ore == null)
            {
                return;
            }

            BuildPlan();
            nextDropIndex = 0;
            armed = true;

            if (visual != null)
            {
                visual.Rebuild();
            }

            if (logDropFlow)
            {
                Debug.Log($"[GarbageDropper] 排档完成：每 {dropHealthStep * 100f:0.#}% 一档，" +
                          $"共 {thresholds.Count} 档，每档 " +
                          (dropAmounts.Count > 0 ? dropAmounts[0] : 0) +
                          " 垃圾（总奖励 " + ore.Reward + "）。", this);
            }
        }

        private void BuildPlan()
        {
            thresholds.Clear();
            dropAmounts.Clear();

            float step = Mathf.Clamp(dropHealthStep, 0.01f, 1f);
            float threshold = 1f - step;

            while (threshold > 0.0001f)
            {
                thresholds.Add(threshold);
                threshold -= step;
            }

            // 无论步长是否能整除，死亡瞬间都要有最后一档
            thresholds.Add(0f);

            int count = thresholds.Count;
            int total = Mathf.Max(0, ore != null ? ore.Reward : 0);
            int configuredPerDrop = Mathf.Max(0, garbagePerDrop);

            if (configuredPerDrop > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    dropAmounts.Add(configuredPerDrop);
                }

                return;
            }

            int baseAmount = count > 0 ? total / count : 0;
            int remainder = count > 0 ? total % count : 0;

            for (int i = 0; i < count; i++)
            {
                dropAmounts.Add(baseAmount + (i < remainder ? 1 : 0));
            }
        }

        private void HandleHealthChanged(float currentHealth, float maxHealth)
        {
            if (!armed || ore == null)
            {
                return;
            }

            if (maxHealth <= 0f)
            {
                return;
            }

            float fraction = Mathf.Clamp01(currentHealth / maxHealth);

            while (nextDropIndex < thresholds.Count && fraction <= thresholds[nextDropIndex] + 0.0001f)
            {
                TriggerDrop(nextDropIndex);
                nextDropIndex++;
            }
        }

        private void TriggerDrop(int index)
        {
            int amount = index >= 0 && index < dropAmounts.Count ? dropAmounts[index] : 0;
            int pieceCount = ResolvePiecesPerDrop();

            detachBuffer.Clear();

            if (visual != null)
            {
                visual.DetachPieces(pieceCount, detachBuffer);
            }

            if (collector != null)
            {
                collector.Enqueue(detachBuffer, amount);
            }
            else
            {
                Debug.LogError("[GarbageDropper] 收集器未配置，这一档垃圾无法发放。", this);
            }

            if (logDropFlow)
            {
                Debug.Log($"[GarbageDropper] 第 {index + 1} 档掉落：{detachBuffer.Count} 块，" +
                          $"垃圾 {amount}，剩余外壳 {(visual != null ? visual.RemainingPieceCount : 0)} 块。", this);
            }
        }

        private int ResolvePiecesPerDrop()
        {
            if (piecesPerDrop > 0)
            {
                return piecesPerDrop;
            }

            int shellCount = visual != null ? visual.ShellPieceCount : 0;
            return Mathf.Max(1, Mathf.RoundToInt(shellCount * Mathf.Clamp01(dropHealthStep)));
        }

        private void OnValidate()
        {
            dropHealthStep = Mathf.Clamp(dropHealthStep, 0.01f, 1f);
            garbagePerDrop = Mathf.Max(0, garbagePerDrop);
            piecesPerDrop = Mathf.Max(0, piecesPerDrop);
        }
    }
}
