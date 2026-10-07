using UnityEngine;
using Yujian.Player;

namespace Yujian.Shop
{
    /// <summary>
    /// 商店。职责：唯一的购买入口——校验参数 → 读价 → 扣钱 → 入库存。
    /// 只做"钱换原料"这一件事，不持有 UI，不知道按钮长什么样。
    /// UI 必须调用 TryBuy()，不得自己碰 PlayerCurrency 或 MaterialInventory。
    /// </summary>
    public class Shop : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("玩家货币，扣钱在这里发生")]
        [SerializeField] private PlayerCurrency playerCurrency;

        [Tooltip("玩家原料库存，买到的东西进这里")]
        [SerializeField] private MaterialInventory inventory;

        [Header("调试")]
        [Tooltip("是否在 Console 打印成功/失败的购买日志")]
        [SerializeField] private bool logPurchaseFlow = true;

        /// <summary>玩家货币。只读暴露给 UI 显示用，不提供任何写入口。</summary>
        public PlayerCurrency Currency => playerCurrency;

        /// <summary>玩家原料库存。只读暴露给 UI 显示用，不提供任何写入口。</summary>
        public MaterialInventory Inventory => inventory;

        private void Awake()
        {
            if (playerCurrency == null)
            {
                Debug.LogError("[Shop] PlayerCurrency 未配置，所有购买都会失败。", this);
            }

            if (inventory == null)
            {
                Debug.LogError("[Shop] MaterialInventory 未配置，买到的原料无处存放。", this);
            }
        }

        /// <summary>
        /// 尝试购买指定颜色、指定数量的原料。
        /// 货币不足时返回 false，此时既不扣钱也不增加原料（原子性：要么全成，要么全不动）。
        /// </summary>
        /// <param name="material">要购买的原料，不可为 null。</param>
        /// <param name="color">颜色，必须是该原料允许的颜色。</param>
        /// <param name="amount">购买数量，必须为正数。</param>
        /// <returns>是否购买成功。</returns>
        public bool TryBuy(MaterialData material, MaterialColor color, int amount = 1)
        {
            if (material == null)
            {
                Debug.LogError("[Shop] TryBuy 收到空的 MaterialData，购买取消。", this);
                return false;
            }

            if (amount <= 0)
            {
                Debug.LogWarning($"[Shop] TryBuy 收到非正数数量({amount})，购买取消。", this);
                return false;
            }

            if (!material.SupportsColor(color))
            {
                Debug.LogWarning($"[Shop] 原料「{material.MaterialName}」不支持颜色 {color}，购买取消。" +
                                 "请在 MaterialData 的允许颜色列表里补上，或改掉按钮绑定的颜色。", this);
                return false;
            }

            if (playerCurrency == null || inventory == null)
            {
                // Awake 已经打印过明确错误，这里不再重复刷屏
                return false;
            }

            int totalCost = material.Price * amount;

            // 价格为 0 的免费原料直接发货；TrySpendMoney 会拒绝 0，不能拿它当扣钱入口
            if (totalCost > 0 && !playerCurrency.TrySpendMoney(totalCost))
            {
                if (logPurchaseFlow)
                {
                    Debug.Log($"[Shop] 购买失败：{color.ToChineseName()}{material.MaterialName} ×{amount} " +
                              $"需要 {totalCost} 金币，当前只有 {playerCurrency.CurrentMoney}。金币与库存均未变动。");
                }

                return false;
            }

            inventory.AddMaterial(material, color, amount);

            if (logPurchaseFlow)
            {
                Debug.Log($"[Shop] 购买成功：{color.ToChineseName()}{material.MaterialName} ×{amount}，" +
                          $"花费 {totalCost} 金币，剩余 {playerCurrency.CurrentMoney} 金币。");
            }

            return true;
        }
    }
}
