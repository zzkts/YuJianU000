using System.Collections.Generic;
using UnityEngine;
using Yujian.Core;
using Yujian.Core.Events;

namespace Yujian.Mining
{
    /// <summary>
    /// 暴击点系统（D3=A：触发式暴击点，删除随机暴击）。
    ///
    /// 规则（2026-10-29 拍板）：
    ///   · 每次**玩家普通攻击**有 spawnChancePercent（默认 8%）概率生成一个暴击点；
    ///   · 暴击点生成在穿过矿石中心的水平面上，按矿石碰撞体大小摆在外圈；
    ///   · 同时存在数量上限与停留时长由 <see cref="MiningLightTool"/> 的等级决定
    ///     （未拥有 1 个 / 2.5 秒，火把 3 个 / 4 秒，手电 5 个 / 6 秒）；
    ///   · 点中后立即消失并造成「普通伤害 × 暴击倍数」，点掉后下一次普通攻击可以立即补位；
    ///   · 建筑自动攻击不参与生成与判定。
    ///
    /// 本类只管生成、上限、清场；伤害由 DamageSystem，发币由 MiningInput。
    /// </summary>
    [DisallowMultipleComponent]
    public class CritPointSystem : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("场上唯一一块矿石。留空则运行时自动查找")]
        [SerializeField] private Ore ore;

        [Tooltip("照明工具。留空时视为未拥有：上限 1、停留 2.5 秒")]
        [SerializeField] private MiningLightTool lightTool;

        [Header("生成")]
        [Tooltip("每次玩家普通攻击生成暴击点的概率（百分比）。D3=A 建议 8%")]
        [SerializeField, Range(0f, 100f)] private float spawnChancePercent = 8f;

        [Tooltip("暴击点摆在矿石碰撞体外圈多少世界单位处")]
        [SerializeField, Min(0f)] private float spawnMargin = 0.35f;

        [Tooltip("暴击点直径（世界单位）")]
        [SerializeField, Min(0.05f)] private float pointDiameter = 0.42f;

        [Tooltip("暴击点颜色")]
        [SerializeField] private Color pointColor = new Color(1f, 0.72f, 0.15f, 1f);

        [Header("调试")]
        [SerializeField] private bool logSpawnFlow = true;

        private readonly List<CritPoint> activePoints = new List<CritPoint>();
        private Material pointMaterial;
        private bool warnedMissingOre;

        /// <summary>当前存活的暴击点数量。</summary>
        public int AliveCount
        {
            get
            {
                PruneConsumed();
                return activePoints.Count;
            }
        }

        /// <summary>当前生效的上限。</summary>
        public int Capacity => lightTool != null ? Mathf.Max(1, lightTool.CritPointCapacity) : 1;

        /// <summary>当前生效的停留时长（秒）。</summary>
        public float Lifetime => lightTool != null ? Mathf.Max(0.1f, lightTool.CritPointLifetime) : 2.5f;

        /// <summary>生成概率（百分比）。</summary>
        public float SpawnChancePercent => spawnChancePercent;

        private void Awake()
        {
            ResolveDependencies();
            BuildMaterial();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<OreHit>(HandleOreHit);
            EventBus.Subscribe<AreaChanged>(HandleAreaChanged);
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<OreHit>(HandleOreHit);
            EventBus.Unsubscribe<AreaChanged>(HandleAreaChanged);
        }

        private void OnDestroy()
        {
            if (pointMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(pointMaterial);
            }
            else
            {
                DestroyImmediate(pointMaterial);
            }
        }

        /// <summary>
        /// 玩家完成一次普通攻击后调用。掷骰决定是否生成暴击点。
        /// 只在矿石仍存活时生成；达到上限时不生成（避免顶掉玩家正在点的目标）。
        /// </summary>
        public void NotifyPlayerAttack(Ore target)
        {
            if (target == null || !target.IsAlive)
            {
                return;
            }

            PruneConsumed();

            if (activePoints.Count >= Capacity)
            {
                return;
            }

            if (Random.value * 100f >= spawnChancePercent)
            {
                return;
            }

            SpawnAt(target);
        }

        /// <summary>由 MiningInput 在点中暴击点后调用，立即移除。</summary>
        public void Consume(CritPoint point)
        {
            if (point == null)
            {
                return;
            }

            activePoints.Remove(point);
            point.Consume();
        }

        /// <summary>清空所有暴击点（矿石死亡 / 离开矿区 / 读档）。</summary>
        public void ClearAll()
        {
            for (int i = 0; i < activePoints.Count; i++)
            {
                if (activePoints[i] != null)
                {
                    activePoints[i].Consume();
                }
            }

            activePoints.Clear();
        }

        /// <summary>生成一个暴击点。公开出来方便批处理验证不依赖随机。</summary>
        public CritPoint SpawnAt(Ore target)
        {
            if (target == null)
            {
                return null;
            }

            PruneConsumed();

            if (activePoints.Count >= Capacity)
            {
                return null;
            }

            Vector3 position = ResolveSpawnPosition(target);
            GameObject pointObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pointObject.name = "CritPoint";
            pointObject.layer = target.gameObject.layer;
            pointObject.transform.position = position;
            pointObject.transform.localScale = Vector3.one * pointDiameter;

            Renderer renderer = pointObject.GetComponent<Renderer>();

            if (renderer != null)
            {
                renderer.sharedMaterial = pointMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            Collider collider = pointObject.GetComponent<Collider>();

            if (collider != null)
            {
                // MiningInput 的射线用 QueryTriggerInteraction.Ignore，这里必须是实体碰撞体
                collider.isTrigger = false;
            }

            CritPoint point = pointObject.AddComponent<CritPoint>();
            point.Initialize(target, Lifetime);
            activePoints.Add(point);

            if (logSpawnFlow)
            {
                Debug.Log($"[CritPointSystem] 生成暴击点（{activePoints.Count}/{Capacity}），" +
                          $"停留 {Lifetime:0.#} 秒，位置 {position}。", this);
            }

            return point;
        }

        private Vector3 ResolveSpawnPosition(Ore target)
        {
            Collider targetCollider = target.GetComponentInChildren<Collider>();
            Bounds bounds = targetCollider != null
                ? targetCollider.bounds
                : new Bounds(target.transform.position, Vector3.one);

            // 穿过矿石中心的水平面：y 取碰撞体中心，只在 XZ 上偏移到外圈
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z) + spawnMargin;
            float angle = Random.value * Mathf.PI * 2f;

            return bounds.center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        private void PruneConsumed()
        {
            for (int i = activePoints.Count - 1; i >= 0; i--)
            {
                if (activePoints[i] == null || activePoints[i].IsConsumed)
                {
                    activePoints.RemoveAt(i);
                }
            }
        }

        private void HandleOreHit(OreHit evt)
        {
            if (evt.Destroyed)
            {
                ClearAll();
            }
        }

        private void HandleAreaChanged(AreaChanged evt)
        {
            if (evt.Area != GameArea.Mining)
            {
                ClearAll();
            }
        }

        private void ResolveDependencies()
        {
            if (ore == null)
            {
                ore = FindFirstObjectByType<Ore>(FindObjectsInactive.Include);
            }

            if (lightTool == null)
            {
                lightTool = FindFirstObjectByType<MiningLightTool>(FindObjectsInactive.Include);
            }

            if (ore == null && !warnedMissingOre)
            {
                warnedMissingOre = true;
                Debug.LogWarning("[CritPointSystem] 场景里找不到 Ore，暴击点无法定位。" +
                                 "请运行菜单「屿见/挖矿/配置暴击点系统」。", this);
            }
        }

        private void BuildMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                Debug.LogWarning("[CritPointSystem] 找不到可用着色器，暴击点会使用默认材质。", this);
                return;
            }

            pointMaterial = new Material(shader) { name = "CritPoint_Runtime" };

            if (pointMaterial.HasProperty("_BaseColor"))
            {
                pointMaterial.SetColor("_BaseColor", pointColor);
            }

            if (pointMaterial.HasProperty("_Color"))
            {
                pointMaterial.SetColor("_Color", pointColor);
            }
        }

        /// <summary>编辑器向导调用，接好矿石与照明工具。</summary>
        public void ConfigureForSetup(Ore targetOre, MiningLightTool tool)
        {
            ore = targetOre;
            lightTool = tool;
        }
    }
}
