using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 挂在矿石上，标明这块矿是什么种类。
    /// 只负责“是什么矿”和“长什么样”，不碰血量、奖励、掉落——那些仍由 Ore 自己管。
    /// </summary>
    public class OreTypeTag : MonoBehaviour
    {
        [SerializeField] private OreType type = OreType.QingShi;

        [Tooltip("没有美术资源时，进入 Play 后把模型染成对应颜色")]
        [SerializeField] private bool tintByType = true;

        public OreType Type { get { return type; } }

        /// <summary>供编辑器向导批量配置用。</summary>
        public void Configure(OreType newType, bool tint)
        {
            type = newType;
            tintByType = tint;
        }

        private void Awake()
        {
            if (!tintByType) return;
            ApplyTint();
        }

        [ContextMenu("应用种类颜色")]
        private void ApplyTint()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            Color color = OreTypeInfo.Tint(type);

            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                Material material = renderers[i].material;
                material.color = color;
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            }
        }
    }
}