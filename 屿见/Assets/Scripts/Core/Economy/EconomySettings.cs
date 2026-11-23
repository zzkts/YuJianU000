using UnityEngine;

namespace Yujian.Core
{
    /// <summary>
    /// 垃圾 / 金币两种资源的全局经济配置（2026-11-19 用户拍板严格拆分）。
    ///
    ///   · 采矿、建筑击碎矿石只产「垃圾」；回收站按 <see cref="GarbagePerCoin"/> 把垃圾换成「金币」；
    ///   · 商店 / 工具 / 照明 / 建筑升级花「金币」；
    ///   · 旧的经济里只有一种货币，价格也是按它标定的。拆分时用本资产记录「是否已经做过一次
    ///     价格 ÷ 转换比」的迁移标记，避免向导每跑一次就把价格再除一遍。
    ///
    /// 只是数据：不扣钱、不加钱、不做 UI。
    /// </summary>
    [CreateAssetMenu(fileName = "EconomySettings", menuName = "屿见/经济设置", order = 5)]
    public class EconomySettings : ScriptableObject
    {
        [Header("转换")]
        [Tooltip("多少垃圾换 1 金币。回收站与旧价格迁移共用这个值")]
        [SerializeField, Min(1)] private int garbagePerCoin = 5;

        [Header("开局")]
        [Tooltip("新游戏开局的初始金币。2026-11-19 前的调试值 9,999,999 应改为 0")]
        [SerializeField, Min(0)] private int startingCoins = 0;

        [Header("迁移标记")]
        [Tooltip("旧价格是否已经按转换比 ÷ 过一次（回收站配置向导写入，不要手改）")]
        [SerializeField] private bool legacyPricesConverted = false;

        /// <summary>多少垃圾换 1 金币。</summary>
        public int GarbagePerCoin => Mathf.Max(1, garbagePerCoin);

        /// <summary>新游戏的初始金币。</summary>
        public int StartingCoins => Mathf.Max(0, startingCoins);

        /// <summary>旧价格是否已经完成迁移。</summary>
        public bool LegacyPricesConverted => legacyPricesConverted;

        /// <summary>向导调用：标记旧价格已按转换比除过一次。</summary>
        public void MarkLegacyPricesConverted()
        {
            legacyPricesConverted = true;
        }

        /// <summary>把旧经济里的价格换算成金币，至少 1 金币。</summary>
        public int ToCoinPrice(int legacyPrice)
        {
            if (legacyPrice <= 0)
            {
                return 0;
            }

            return Mathf.Max(1, Mathf.CeilToInt(legacyPrice / (float)GarbagePerCoin));
        }

        private void OnValidate()
        {
            garbagePerCoin = Mathf.Max(1, garbagePerCoin);
            startingCoins = Mathf.Max(0, startingCoins);
        }
    }
}
