using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 伤害数字。
    /// 职责：在矿石上方显示一次伤害数值，向上飘动并淡出，播放结束后自毁。
    /// 不是 UI 框架的一部分，只服务于挖矿反馈。
    /// 使用内置 TextMesh 而非 TextMeshPro，是为了不依赖 TMP Essentials 的导入步骤。
    /// </summary>
    public class DamagePopup : MonoBehaviour
    {
        [Header("文本")]
        [Tooltip("显示用的 TextMesh。留空则在 Awake 时自动创建一个")]
        [SerializeField] private TextMesh textMesh;

        [Tooltip("文字大小，数值不合适时优先调这一项")]
        [SerializeField] private float characterSize = 0.5f;

        [Tooltip("文字颜色")]
        [SerializeField] private Color textColor = new Color(1f, 0.85f, 0.3f, 1f);

        [Tooltip("是否始终面向相机")]
        [SerializeField] private bool faceCamera = true;

        [Header("动画")]
        [Tooltip("显示时长（秒）")]
        [SerializeField] private float lifetime = 0.8f;

        [Tooltip("向上飘动的距离")]
        [SerializeField] private float riseDistance = 0.8f;

        private float elapsed;
        private Vector3 startPosition;
        private bool playing;

        private void Awake()
        {
            EnsureTextMesh();
        }

        /// <summary>
        /// 播放一次伤害数字。由 Ore 在生成本对象后立即调用。
        /// </summary>
        /// <param name="damage">要显示的伤害数值。</param>
        public void Play(float damage)
        {
            if (textMesh == null)
            {
                return;
            }

            textMesh.text = damage.ToString("0.##");
            textMesh.color = textColor;

            startPosition = transform.position;
            elapsed = 0f;
            playing = true;
        }

        private void Update()
        {
            if (!playing)
            {
                return;
            }

            elapsed += Time.deltaTime;
            float progress = lifetime > 0f ? Mathf.Clamp01(elapsed / lifetime) : 1f;

            transform.position = startPosition + Vector3.up * (riseDistance * progress);

            if (textMesh != null)
            {
                Color faded = textColor;
                faded.a = textColor.a * (1f - progress);
                textMesh.color = faded;
            }

            if (progress >= 1f)
            {
                playing = false;
                Destroy(gameObject);
            }
        }

        private void LateUpdate()
        {
            if (!playing || !faceCamera)
            {
                return;
            }

            Camera viewCamera = Camera.main;
            if (viewCamera != null)
            {
                transform.rotation = viewCamera.transform.rotation;
            }
        }

        /// <summary>
        /// 保证有一个可用的 TextMesh：没有就建一个，没有字体就取 Unity 内置字体。
        /// 这样 DamagePopup 预制体只需要一个挂本脚本的空物体。
        /// </summary>
        private void EnsureTextMesh()
        {
            if (textMesh == null)
            {
                textMesh = GetComponentInChildren<TextMesh>();
            }

            if (textMesh == null)
            {
                GameObject textObject = new GameObject("Text");
                textObject.transform.SetParent(transform, false);
                textMesh = textObject.AddComponent<TextMesh>();
            }

            MeshRenderer meshRenderer = textMesh.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                meshRenderer = textMesh.gameObject.AddComponent<MeshRenderer>();
            }

            if (textMesh.font == null)
            {
                Font builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (builtinFont == null)
                {
                    builtinFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }

                if (builtinFont != null)
                {
                    textMesh.font = builtinFont;
                    meshRenderer.sharedMaterial = builtinFont.material;
                }
                else
                {
                    Debug.LogError("[DamagePopup] 找不到 Unity 内置字体，伤害数字将无法显示。请手动为该 TextMesh 指定 Font。", this);
                }
            }

            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.characterSize = characterSize;
            textMesh.color = textColor;
        }
    }
}
