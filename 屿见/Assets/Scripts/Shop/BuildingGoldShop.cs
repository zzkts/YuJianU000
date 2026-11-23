using UnityEngine;
using Yujian.Building;
using Yujian.Player;

namespace Yujian.Shop
{
    /// <summary>
    /// 隔离商店系统：建筑金币购买。
    ///
    /// 打开 <see cref="useGoldPurchase"/> 后，建筑不再消耗原料：
    ///   · 蓝图落位后直接点 √ 建造；
    ///   · 建造时按 <see cref="BuildingShopCatalog"/> 查价、扣金币；
    ///   · 扣费失败就不建，建造失败会原额退回。
    ///
    /// 关掉开关（或把本组件从场景里移除）就完全回到原来的「拖原料建造」流程，
    /// 所以这是一版可随时回退的隔离实现，不改动 BuildingData 的材料配置。
    ///
    /// 职责边界：只做「查价 / 能不能付 / 付 / 退」。
    /// 不生成建筑（BuildingConstruction 负责）、不画 UI、不碰材料库存。
    /// </summary>
    public class BuildingGoldShop : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("玩家金币。扣款与退款都走它")]
        [SerializeField] private PlayerCurrency currency;

        [Tooltip("建筑金币价目表。留空时所有建筑都会被判为「未配置金币价格」")]
        [SerializeField] private BuildingShopCatalog catalog;

        [Header("模式开关")]
        [Tooltip("勾上 = 建筑直接花金币建造（隔离商店模式）；" +
                 "取消 = 回到原有的「拖原料建造」流程。随时可切回")]
        [SerializeField] private bool useGoldPurchase = true;

        [Header("调试")]
        [Tooltip("是否在 Console 打印扣款 / 退款日志")]
        [SerializeField] private bool logPurchaseFlow = true;

        private bool warnedMissingCurrency;
        private bool warnedMissingCatalog;

        /// <summary>当前是否处于金币建造模式。关掉后 BuildingConstruction 会走原料流程。</summary>
        public bool IsEnabled => useGoldPurchase;

        /// <summary>玩家金币组件，可能为空。</summary>
        public PlayerCurrency Currency => currency;

        /// <summary>当前金币余额；没有配置货币时返回 0。</summary>
        public int CurrentMoney => currency != null ? currency.CurrentMoney : 0;

        private void Awake()
        {
            if (!useGoldPurchase)
            {
                return;
            }

            // 场景里漏接时退回全场景唯一的 PlayerCurrency，与拆除 / 商店的自动接线口径一致
            if (currency == null)
            {
                currency = FindFirstObjectByType<PlayerCurrency>(FindObjectsInactive.Include);
            }

            if (currency == null)
            {
                Debug.LogError("[BuildingGoldShop] 金币建造模式已开启，但 Player Currency 未配置，" +
                               "所有建筑都会购买失败。", this);
                warnedMissingCurrency = true;
            }

            if (catalog == null)
            {
                Debug.LogError("[BuildingGoldShop] 金币建造模式已开启，但价目表未配置，" +
                               "所有建筑都会被判为「未配置金币价格」。", this);
                warnedMissingCatalog = true;
            }
        }

        /// <summary>
        /// 查建筑的金币价。价目表或建筑缺失时返回 false。
        /// 关闭金币模式时也返回 false——调用方应先看 <see cref="IsEnabled"/>。
        /// </summary>
        public bool TryGetPrice(BuildingData building, out int price)
        {
            price = 0;

            if (!useGoldPurchase || building == null)
            {
                return false;
            }

            if (catalog == null)
            {
                if (!warnedMissingCatalog)
                {
                    warnedMissingCatalog = true;
                    Debug.LogError("[BuildingGoldShop] 价目表未配置，无法查询建筑金币价。", this);
                }

                return false;
            }

            return catalog.TryGetPrice(building, out price);
        }

        /// <summary>当前余额是否够付 price。price <= 0 视为免费，恒为 true。</summary>
        public bool CanAfford(int price)
        {
            if (price <= 0)
            {
                return true;
            }

            if (currency == null)
            {
                if (!warnedMissingCurrency)
                {
                    warnedMissingCurrency = true;
                    Debug.LogError("[BuildingGoldShop] Player Currency 未配置，无法判断余额。", this);
                }

                return false;
            }

            return currency.CurrentMoney >= price;
        }

        /// <summary>
        /// 扣金币。price <= 0 直接返回 true（免费建筑）。
        /// 余额不足或依赖缺失时不扣任何金币。
        /// </summary>
        public bool TrySpend(int price)
        {
            if (price <= 0)
            {
                return true;
            }

            if (currency == null || !currency.TrySpendMoney(price))
            {
                return false;
            }

            if (logPurchaseFlow)
            {
                Debug.Log($"[BuildingGoldShop] 扣除 {price} 金币，剩余 {currency.CurrentMoney}。");
            }

            return true;
        }

        /// <summary>原额退回金币（建造失败时使用）。price <= 0 忽略。</summary>
        public void Refund(int price)
        {
            if (price <= 0)
            {
                return;
            }

            if (currency == null)
            {
                Debug.LogError($"[BuildingGoldShop] 需要退还 {price} 金币，但 Player Currency 未配置，退还失败。", this);
                return;
            }

            currency.AddMoney(price);

            if (logPurchaseFlow)
            {
                Debug.Log($"[BuildingGoldShop] 退还 {price} 金币，当前 {currency.CurrentMoney}。");
            }
        }
    }
}
