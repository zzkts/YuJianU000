using UnityEngine;
using UnityEngine.UI;
using Yujian.Player;

namespace Yujian.UI
{
    /// <summary>
    /// 阶段 6：常驻在屏幕上的金币显示（形如「金币：100」）。
    ///
    /// **硬性要求：靠事件刷新，不许用 Update() 每帧去读。**
    /// 所以本类订阅 <see cref="PlayerCurrency.OnMoneyChanged"/>，钱一变才写一次文字。
    /// 本类里**没有 Update 方法**——这是刻意的，不是漏写。
    ///
    /// 与商店面板里那行「金币：N」（<c>ShopUI.moneyText</c>）是两份独立显示：
    /// 商店那行只在商店区看得见，本类这一份是常驻的。
    /// 两份都订阅同一个事件，所以不会出现「一个涨了一个没涨」。
    /// </summary>
    public class CoinHudUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("玩家货币。挂 Player 上")]
        [SerializeField] private PlayerCurrency currency;

        [Header("显示")]
        [Tooltip("显示金币的文本。**必须**是旧版 UnityEngine.UI.Text，不用 TextMeshPro（项目规则 10）")]
        [SerializeField] private Text coinText;

        [Tooltip("文本格式。{0} 是金币数，例如「金币：{0}」显示成「金币：100」。" +
                 "格式串里必须留着 {0}，否则金币数字不会出现")]
        [SerializeField] private string format = "金币：{0}";

        [Header("调试")]
        [Tooltip("是否在 Console 打印每次金币变化")]
        [SerializeField] private bool logMoneyChanges = false;

        private bool warnedAboutFormat;

        private void OnEnable()
        {
            if (currency == null)
            {
                Debug.LogError("[CoinHudUI] PlayerCurrency 未配置，金币显示不会更新。" +
                               "请重新运行菜单「屿见/配置 MVP 整合（阶段 6）」。", this);
                return;
            }

            if (coinText == null)
            {
                Debug.LogError("[CoinHudUI] 金币文本未配置，金币不会显示出来。", this);
                return;
            }

            currency.OnMoneyChanged += HandleMoneyChanged;

            // 订阅只保证「之后」的变化；先把当前值补上，否则开局显示的是场景里那句占位文字
            Refresh(currency.CurrentMoney);
        }

        private void OnDisable()
        {
            if (currency != null)
            {
                currency.OnMoneyChanged -= HandleMoneyChanged;
            }
        }

        private void HandleMoneyChanged(int money)
        {
            Refresh(money);

            if (logMoneyChanges)
            {
                Debug.Log($"[CoinHudUI] 金币变化：{money}");
            }
        }

        private void Refresh(int money)
        {
            if (coinText == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(format))
            {
                // 空格式串会让 string.Format 直接抛异常，这里退化成默认写法
                WarnAboutFormat("格式串是空的");
                coinText.text = "金币：" + money;
                return;
            }

            try
            {
                coinText.text = string.Format(format, money);
            }
            catch (System.FormatException)
            {
                WarnAboutFormat($"格式串「{format}」不合法");
                coinText.text = "金币：" + money;
            }
        }

        private void WarnAboutFormat(string reason)
        {
            if (warnedAboutFormat)
            {
                return;
            }

            warnedAboutFormat = true;
            Debug.LogError($"[CoinHudUI] {reason}，已退化成「金币：数字」。请把字段「Format」修好。", this);
        }
    }
}
