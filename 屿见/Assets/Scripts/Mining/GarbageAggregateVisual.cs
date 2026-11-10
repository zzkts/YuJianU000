using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 垃圾聚合体的外壳表现。
    /// 用「垃圾」预制体在外层生成一圈白色垃圾块，中间放「垃圾精灵」紫色团块。
    /// 预制体还是 Unity 自带 Cube 时，会由 GarbageShapeMeshes 在运行时换成
    /// 低多边形的不规则形状；换美术预制体后不再覆盖。
    /// 每次重新生成外壳都会重新随机排布，不会每次刷新都长得一样。
    /// 掉血时由 GarbageDropper 调用 <see cref="DetachPieces"/> 把外壳块摘下来，
    /// 外壳越少，中间的垃圾精灵就越明显。
    ///
    /// 只负责外观与外壳块管理，不碰血量、伤害、奖励与收集。
    /// </summary>
    public class GarbageAggregateVisual : MonoBehaviour
    {
        [Header("预制体")]
        [Tooltip("每块垃圾的预制体（占位为白色 Cube）。留空时外壳不会生成")]
        [SerializeField] private GameObject garbagePiecePrefab;

        [Tooltip("垃圾精灵预制体（占位为紫色 Cube）。留空时中间的精灵不会生成")]
        [SerializeField] private GameObject garbageSpritePrefab;

        [Header("占位外形")]
        [Tooltip("预制体还是 Unity 自带 Cube 时，运行时换成程序生成的不规则垃圾块 / 团块；" +
                 "换美术预制体后自动跳过，不会覆盖")]
        [SerializeField] private bool replacePlaceholderCubes = true;

        [Header("外壳")]
        [Tooltip("外壳由多少块垃圾拼成")]
        [SerializeField, Min(1)] private int shellPieceCount = 28;

        [Tooltip("外壳半径。外壳块会落在这个半径附近")]
        [SerializeField, Min(0.01f)] private float shellRadius = 0.66f;

        [Tooltip("外壳在竖直方向的压扁比例。俯视视角下 0.7~0.9 更像一堆垃圾")]
        [SerializeField, Range(0.1f, 2f)] private float shellVerticalScale = 0.8f;

        [Tooltip("外壳整体相对聚合体原点的偏移")]
        [SerializeField] private Vector3 shellCenter = new Vector3(0f, 0.2f, 0f);

        [Tooltip("每块垃圾的随机尺寸范围（本地缩放）")]
        [SerializeField] private Vector2 pieceSizeRange = new Vector2(0.18f, 0.27f);

        [Tooltip("外壳块与理想球面的随机偏移，制造不规则感")]
        [SerializeField, Min(0f)] private float shellJitter = 0.04f;

        [Tooltip("是否给每块垃圾随机旋转")]
        [SerializeField] private bool randomizeRotation = true;

        [Tooltip("勾选后固定使用下面的种子复现同一套外壳；关闭时每次重新生成都随机")]
        [SerializeField] private bool useFixedSeed = false;

        [Tooltip("固定随机种子。仅在「使用固定种子」勾选时生效；为 0 时仍然每次随机")]
        [SerializeField] private int randomSeed = 20261107;

        // 旧场景保存的是初版外观参数；下面这版迁移只在这几个值仍是初版默认时生效一次。
        [SerializeField, HideInInspector] private int tuningVersion;

        [Header("垃圾精灵")]
        [Tooltip("外壳完整时垃圾精灵的缩放")]
        [SerializeField, Min(0.01f)] private float spriteHiddenScale = 0.24f;

        [Tooltip("外壳掉光时垃圾精灵的缩放，用来逐渐显现")]
        [SerializeField, Min(0.01f)] private float spriteRevealedScale = 0.40f;

        [Header("掉落")]
        [Tooltip("垃圾块脱离外壳时向外弹开的距离")]
        [SerializeField, Min(0f)] private float detachPopDistance = 0.15f;

        [Tooltip("垃圾块脱离外壳时向上弹开的高度")]
        [SerializeField, Min(0f)] private float detachPopUp = 0.08f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印外壳生成与摘下日志")]
        [SerializeField] private bool logBuildFlow = false;

        private readonly List<GameObject> shellPieces = new List<GameObject>();
        private GameObject spriteInstance;
        private bool built;
        private bool warnedMissingPrefabs;
        private int initialPieceCount;

        /// <summary>配置的外壳块数。</summary>
        public int ShellPieceCount => shellPieceCount;

        /// <summary>当前还留在外壳上的垃圾块数。</summary>
        public int RemainingPieceCount => shellPieces.Count;

        /// <summary>当前的垃圾精灵实例，没有生成时为 null。</summary>
        public GameObject SpriteInstance => spriteInstance;

        private void Awake()
        {
            ApplyTuningMigration();
            EnsureBuilt();
        }

        /// <summary>编辑器向导调用，把预制体接进来。重复调用只会更新引用。</summary>
        public void ConfigureForSetup(GameObject piecePrefab, GameObject spritePrefab)
        {
            garbagePiecePrefab = piecePrefab;
            garbageSpritePrefab = spritePrefab;
        }

        /// <summary>
        /// 清掉当前外壳并重新生成。刷新 / 下一次生成时调用。
        /// 外壳本来就完整时直接复用，避免每次刷新都重建一遍。
        /// </summary>
        public void Rebuild()
        {
            EnsureBuilt();

            if (shellPieces.Count == initialPieceCount && spriteInstance != null)
            {
                UpdateSpriteScale();
                return;
            }

            ClearBuiltObjects();
            built = false;
            EnsureBuilt();
        }

        /// <summary>生成外壳与垃圾精灵。重复调用不会重复生成。</summary>
        public void EnsureBuilt()
        {
            if (built)
            {
                return;
            }

            built = true;

            if (garbagePiecePrefab == null || garbageSpritePrefab == null)
            {
                if (!warnedMissingPrefabs)
                {
                    warnedMissingPrefabs = true;
                    Debug.LogError("[GarbageAggregateVisual] 垃圾 / 垃圾精灵预制体未配置，外壳不会显示。" +
                                   "请运行菜单「屿见/清洁/配置垃圾聚合体」。", this);
                }

                return;
            }

            BuildSprite();
            BuildShell();
            UpdateSpriteScale();

            if (logBuildFlow)
            {
                Debug.Log($"[GarbageAggregateVisual] 已生成外壳 {shellPieces.Count} 块、垃圾精灵 1 个。", this);
            }
        }

        /// <summary>
        /// 从外壳上摘下指定数量的垃圾块，并把它们从聚合体上分离。
        /// 返回实际摘下的数量；剩余不足时有多少摘多少。
        /// </summary>
        /// <param name="count">想摘下的块数。</param>
        /// <param name="detached">摘下的块会追加到这个列表。</param>
        public int DetachPieces(int count, List<GameObject> detached)
        {
            if (detached == null)
            {
                Debug.LogError("[GarbageAggregateVisual] DetachPieces 的 detached 列表为空。", this);
                return 0;
            }

            EnsureBuilt();

            int wanted = Mathf.Clamp(count, 0, shellPieces.Count);
            int detachedCount = 0;

            for (int i = 0; i < wanted; i++)
            {
                int index = Random.Range(0, shellPieces.Count);
                GameObject piece = shellPieces[index];
                shellPieces.RemoveAt(index);

                if (piece == null)
                {
                    continue;
                }

                // 保留世界位置，脱离外壳后不再受受击缩放 / 区域显隐影响
                piece.transform.SetParent(null, true);

                Vector3 outward = piece.transform.position - transform.position;
                outward.y = 0f;

                if (outward.sqrMagnitude < 0.0001f)
                {
                    outward = Vector3.forward;
                }

                piece.transform.position += outward.normalized * detachPopDistance
                                            + Vector3.up * detachPopUp;

                detached.Add(piece);
                detachedCount++;
            }

            UpdateSpriteScale();

            if (logBuildFlow && detachedCount > 0)
            {
                Debug.Log($"[GarbageAggregateVisual] 摘下 {detachedCount} 块垃圾，" +
                          $"外壳剩余 {shellPieces.Count} 块。", this);
            }

            return detachedCount;
        }

        /// <summary>
        /// 暴击点在外壳上的落点：记录挂在哪块垃圾、块上的本地表面点，
        /// 暴击点每帧用 Anchor.TransformPoint(LocalPoint) 跟着垃圾一起动。
        /// </summary>
        public struct CritSurface
        {
            public Transform Anchor;
            public Vector3 LocalPoint;
            public Vector3 WorldPosition;
            public Vector3 WorldNormal;
        }

        /// <summary>
        /// 暴击点系统用：在外壳上找一块朝向相机的垃圾，返回其表面落点。
        /// 外壳掉光 / 没有相机时返回 false。
        /// </summary>
        public bool TryGetCritPointSurface(Camera camera, out CritSurface surface)
        {
            surface = default;

            if (camera == null || shellPieces.Count == 0)
            {
                return false;
            }

            Vector3 cameraPosition = camera.transform.position;
            Vector3 center = transform.TransformPoint(shellCenter);
            Vector3 toCamera = cameraPosition - center;

            if (toCamera.sqrMagnitude < 0.0001f)
            {
                toCamera = Vector3.forward;
            }

            toCamera.Normalize();

            int count = shellPieces.Count;
            int start = Random.Range(0, count);

            // 先找朝向相机一侧的块，避免把点放到背面被挡住的垃圾上。
            for (int i = 0; i < count; i++)
            {
                GameObject piece = shellPieces[(start + i) % count];

                if (piece == null)
                {
                    continue;
                }

                Vector3 fromCenter = piece.transform.position - center;

                if (fromCenter.sqrMagnitude < 0.0001f)
                {
                    fromCenter = toCamera;
                }

                if (Vector3.Dot(fromCenter.normalized, toCamera) < 0.2f)
                {
                    continue;
                }

                if (TryGetPieceSurface(piece, cameraPosition, out surface))
                {
                    return true;
                }
            }

            // 相机几乎正对侧面时，仍然从所有块里兜底，保证不会落到空气里。
            for (int i = 0; i < count; i++)
            {
                GameObject piece = shellPieces[i];

                if (piece != null && TryGetPieceSurface(piece, cameraPosition, out surface))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 取一块垃圾朝向相机的那个面：用渲染器本地包围盒算出表面点，
        /// 这样以后把 Cube 换成别的形状也仍然贴着表面。
        /// </summary>
        private static bool TryGetPieceSurface(GameObject piece, Vector3 cameraPosition,
            out CritSurface surface)
        {
            surface = default;

            if (piece == null)
            {
                return false;
            }

            Renderer renderer = piece.GetComponentInChildren<Renderer>();

            if (renderer == null)
            {
                return false;
            }

            Transform shape = renderer.transform;
            Vector3 toCamera = cameraPosition - shape.position;

            if (toCamera.sqrMagnitude < 0.0001f)
            {
                toCamera = Vector3.up;
            }

            Vector3 localDirection = shape.InverseTransformDirection(toCamera.normalized);
            Vector3 abs = new Vector3(
                Mathf.Abs(localDirection.x),
                Mathf.Abs(localDirection.y),
                Mathf.Abs(localDirection.z));
            Vector3 localNormal;

            if (abs.x >= abs.y && abs.x >= abs.z)
            {
                localNormal = new Vector3(Mathf.Sign(localDirection.x), 0f, 0f);
            }
            else if (abs.y >= abs.z)
            {
                localNormal = new Vector3(0f, Mathf.Sign(localDirection.y), 0f);
            }
            else
            {
                localNormal = new Vector3(0f, 0f, Mathf.Sign(localDirection.z));
            }

            Bounds localBounds = renderer.localBounds;
            Vector3 localPoint = localBounds.center + Vector3.Scale(localNormal, localBounds.extents);

            surface.Anchor = shape;
            surface.LocalPoint = localPoint;
            surface.WorldPosition = shape.TransformPoint(localPoint);
            surface.WorldNormal = shape.TransformDirection(localNormal).normalized;
            return true;
        }

        private void BuildSprite()
        {
            spriteInstance = Instantiate(garbageSpritePrefab, transform);
            spriteInstance.name = "垃圾精灵";
            spriteInstance.transform.localPosition = shellCenter;
            spriteInstance.transform.localRotation = Quaternion.identity;
            spriteInstance.transform.localScale = Vector3.one * Mathf.Max(0.01f, spriteHiddenScale);
            DisableColliders(spriteInstance);

            if (replacePlaceholderCubes)
            {
                GarbageShapeMeshes.TryApplyBlob(spriteInstance);
            }
        }

        private void BuildShell()
        {
            int count = Mathf.Max(1, shellPieceCount);
            initialPieceCount = count;

            Random.State previousState = Random.state;

            // 默认每次刷新 / 重新生成都换一套排布，避免每次都长得一模一样；
            // 调试时可以勾选「使用固定种子」复现同一套。
            int seed = useFixedSeed && randomSeed != 0
                ? randomSeed
                : System.Guid.NewGuid().GetHashCode();
            Random.InitState(seed);

            // 斐波那契球面：块数不多时也能均匀包住中心。
            // 每次生成再随机旋转整层螺旋、随机竖直起点，避免每次都是同一套点位。
            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));
            float angleOffset = Random.Range(0f, Mathf.PI * 2f);
            float verticalOffset = Random.Range(0.2f, 0.8f);

            for (int i = 0; i < count; i++)
            {
                float y = 1f - (i + verticalOffset) * 2f / count;
                float radius = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float angle = i * goldenAngle + angleOffset;

                Vector3 direction = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
                Vector3 position = shellCenter + new Vector3(
                    direction.x * shellRadius,
                    direction.y * shellRadius * shellVerticalScale,
                    direction.z * shellRadius);

                if (shellJitter > 0f)
                {
                    position += new Vector3(
                        Random.Range(-shellJitter, shellJitter),
                        Random.Range(-shellJitter, shellJitter),
                        Random.Range(-shellJitter, shellJitter));
                }

                GameObject piece = Instantiate(garbagePiecePrefab, transform);
                piece.name = $"垃圾_{i + 1}";
                piece.transform.localPosition = position;
                piece.transform.localRotation = randomizeRotation ? Random.rotation : Quaternion.identity;
                piece.transform.localScale = Vector3.one * Random.Range(pieceSizeRange.x, pieceSizeRange.y);
                DisableColliders(piece);

                if (replacePlaceholderCubes)
                {
                    GarbageShapeMeshes.TryApplyChunk(piece, i);
                }

                shellPieces.Add(piece);
            }

            Random.state = previousState;
        }

        private void UpdateSpriteScale()
        {
            if (spriteInstance == null)
            {
                return;
            }

            float detachedRatio = initialPieceCount <= 0
                ? 0f
                : Mathf.Clamp01(1f - (float)shellPieces.Count / initialPieceCount);
            float scale = Mathf.Lerp(
                spriteHiddenScale, spriteRevealedScale, Mathf.SmoothStep(0f, 1f, detachedRatio));
            spriteInstance.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
        }

        private void ClearBuiltObjects()
        {
            for (int i = 0; i < shellPieces.Count; i++)
            {
                DestroyRuntimeObject(shellPieces[i]);
            }

            shellPieces.Clear();

            if (spriteInstance != null)
            {
                DestroyRuntimeObject(spriteInstance);
                spriteInstance = null;
            }

            initialPieceCount = 0;
        }

        private static void DisableColliders(GameObject target)
        {
            Collider[] colliders = target.GetComponentsInChildren<Collider>(true);

            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        private static void DestroyRuntimeObject(GameObject target)
        {
            if (target == null)
            {
                return;
            }

            // 先隐藏，避免 Destroy 延迟到帧末时和新生成的外壳重叠一帧
            target.SetActive(false);

            if (Application.isPlaying)
            {
                Object.Destroy(target);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }

        private const int CurrentTuningVersion = 1;

        /// <summary>
        /// 场景里保存的还是初版外观参数（紫色精灵偏大、外壳偏松）时，升级到当前这版。
        /// 只有六个值都仍是初版默认值才迁移，避免覆盖手动调过的外观。
        /// </summary>
        private void ApplyTuningMigration()
        {
            if (tuningVersion >= CurrentTuningVersion)
            {
                return;
            }

            tuningVersion = CurrentTuningVersion;

            bool legacyDefaults =
                shellPieceCount == 20 &&
                Mathf.Approximately(shellRadius, 0.75f) &&
                Mathf.Approximately(pieceSizeRange.x, 0.16f) &&
                Mathf.Approximately(pieceSizeRange.y, 0.24f) &&
                Mathf.Approximately(spriteHiddenScale, 0.35f) &&
                Mathf.Approximately(spriteRevealedScale, 0.55f);

            if (!legacyDefaults)
            {
                return;
            }

            shellPieceCount = 28;
            shellRadius = 0.66f;
            pieceSizeRange = new Vector2(0.18f, 0.27f);
            spriteHiddenScale = 0.24f;
            spriteRevealedScale = 0.40f;
        }

        private void OnValidate()
        {
            ApplyTuningMigration();
            shellPieceCount = Mathf.Max(1, shellPieceCount);
            shellRadius = Mathf.Max(0.01f, shellRadius);
            shellVerticalScale = Mathf.Clamp(shellVerticalScale, 0.1f, 2f);
            shellJitter = Mathf.Max(0f, shellJitter);

            if (pieceSizeRange.x < 0.01f)
            {
                pieceSizeRange.x = 0.01f;
            }

            if (pieceSizeRange.y < pieceSizeRange.x)
            {
                pieceSizeRange.y = pieceSizeRange.x;
            }

            spriteHiddenScale = Mathf.Max(0.01f, spriteHiddenScale);
            spriteRevealedScale = Mathf.Max(spriteHiddenScale, spriteRevealedScale);
        }
    }
}
