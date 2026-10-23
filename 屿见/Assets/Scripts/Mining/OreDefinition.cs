using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 一种矿石的静态数据：名字、种类、血量、奖励和颜色。
    /// 策划只改这个资产，不需要改代码。
    /// </summary>
    [CreateAssetMenu(fileName = "Ore_", menuName = "屿见/挖矿/矿石定义", order = 10)]
    public class OreDefinition : ScriptableObject
    {
        [Header("显示")]
        [SerializeField] private string displayName = "青石";
        [SerializeField] private OreType type = OreType.QingShi;

        [Header("数值")]
        [SerializeField, Min(1f)] private float maxHealth = 10f;
        [SerializeField, Min(0)] private int reward = 10;

        [Header("外观")]
        [Tooltip("是否用颜色区分矿种。关掉后保留模型原本的材质颜色")]
        [SerializeField] private bool useTint = true;

        [Tooltip("勾选后不使用 OreType 的默认颜色，改用下面的自定义颜色")]
        [SerializeField] private bool useCustomTint;

        [SerializeField] private Color customTint = Color.white;

        public string DisplayName =>
            string.IsNullOrWhiteSpace(displayName) ? OreTypeInfo.DisplayName(type) : displayName;

        public OreType Type => type;

        public float MaxHealth => Mathf.Max(1f, maxHealth);

        public int Reward => Mathf.Max(0, reward);

        public bool UseTint => useTint;

        public Color Tint => useCustomTint ? customTint : OreTypeInfo.Tint(type);

        /// <summary>编辑器向导创建默认资产时使用。运行时不要调用。</summary>
        public void ConfigureForSetup(string newDisplayName, OreType newType,
            float newMaxHealth, int newReward, bool tint, bool customTintEnabled, Color customColor)
        {
            displayName = newDisplayName;
            type = newType;
            maxHealth = Mathf.Max(1f, newMaxHealth);
            reward = Mathf.Max(0, newReward);
            useTint = tint;
            useCustomTint = customTintEnabled;
            customTint = customColor;
        }

        private void OnValidate()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            reward = Mathf.Max(0, reward);

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = OreTypeInfo.DisplayName(type);
            }
        }
    }
}
