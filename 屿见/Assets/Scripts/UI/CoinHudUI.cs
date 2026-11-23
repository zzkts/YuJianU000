using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building.Recycling;
using Yujian.Player;

namespace Yujian.UI
{
    /// <summary>
    /// 常驻在屏幕上的**金币**显示（形如「金币：100」）。
    ///
    /// 2026-11-19 经济拆分后：垃圾是采矿产出，金币只由回收站产出。
    /// 本类只显示金币，垃圾数量由清洁界面（关卡内）与 RecyclingHudUI（岛上）显示。
    ///
    /// **硬性要求：靠事件刷新，不许用 Update() 每帧去读。**
    /// 所以本类订阅 <see cref="PlayerCurrency.OnMoneyChanged"/>，钱一变才写一次文字；
    /// 另外订阅 <see cref="RecyclingSystem.CoinsProduced"/>，
    /// 每完成一批转换时让金币数字旋转放大再缩回（策划案「右上角金币数值旋转放大后旋转缩小」）。
    /// </summary>
    public class CoinHudUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("玩家金币。挂 Player 上")]
        [SerializeField] private PlayerCurrency currency;

        [Tooltip("回收站系统。留空时自动查找；找不到则只刷新数字、不播金币弹跳")]
        [SerializeField] private RecyclingSystem recycling;

        [Header("显示")]
        [Tooltip("显示金币的文本。**必须**是旧版 UnityEngine.UI.Text，不用 TextMeshPro（项目规则 10）")]
        [SerializeField] private Text coinText;

        [Tooltip("文本格式。{0} 是金币数，例如「金币：{0}」显示成「金币：100」。" +
                 "格式串里必须留着 {0}，否则金币数字不会出现")]
        [SerializeField] private string format = "金币：{0}";

        [Header("回收站到账反馈")]
        [Tooltip("每完成一批转换时，金币数字放大的倍数")]
        [SerializeField, Min(1f)] private float punchScale = 1.3f;

        [Tooltip("每完成一批转换时，金币数字旋转的角度（度）")]
        [SerializeField] private float punchAngle = 14f;

        [Tooltip("一次弹跳的总时长（秒）")]
        [SerializeField, Min(0.05f)] private float punchDuration = 0.4f;

        [Header("调试")]
        [Tooltip("是否在 Console 打印每次金币变化")]
        [SerializeField] private bool logMoneyChanges = false;

        private bool warnedAboutFormat;
        private Coroutine punchRoutine;
        private Vector3 baseScale = Vector3.one;
        private Quaternion baseRotation = Quaternion.identity;

        private void OnEnable()
        {
            if (currency == null)
            {
                Debug.LogError("[CoinHudUI] PlayerCurrency 未配置，金币显示不会更新。" +
                               "请重新运行菜单「屿见/整合/配置 MVP 整合（阶段 6）」。", this);
                return;
            }

            if (coinText == null)
            {
                Debug.LogError("[CoinHudUI] 金币文本未配置，金币不会显示出来。", this);
                return;
            }

            if (recycling == null)
            {
                recycling = FindFirstObjectByType<RecyclingSystem>(FindObjectsInactive.Include);
            }

            baseScale = coinText.rectTransform.localScale;
            baseRotation = coinText.rectTransform.localRotation;

            currency.OnMoneyChanged += HandleMoneyChanged;

            if (recycling != null)
            {
                recycling.CoinsProduced += HandleCoinsProduced;
            }

            // 订阅只保证「之后」的变化；先把当前值补上，否则开局显示的是场景里那句占位文字
            Refresh(currency.CurrentMoney);
        }

        private void OnDisable()
        {
            if (currency != null)
            {
                currency.OnMoneyChanged -= HandleMoneyChanged;
            }

            if (recycling != null)
            {
                recycling.CoinsProduced -= HandleCoinsProduced;
            }

            if (punchRoutine != null)
            {
                StopCoroutine(punchRoutine);
                punchRoutine = null;
            }

            ResetPunchTransform();
        }

        private void HandleMoneyChanged(int money)
        {
            Refresh(money);

            if (logMoneyChanges)
            {
                Debug.Log($"[CoinHudUI] 金币变化：{money}");
            }
        }

        private void HandleCoinsProduced(int _)
        {
            if (coinText == null)
            {
                return;
            }

            if (punchRoutine != null)
            {
                StopCoroutine(punchRoutine);
            }

            punchRoutine = StartCoroutine(PunchRoutine());
        }

        private IEnumerator PunchRoutine()
        {
            float duration = Mathf.Max(0.05f, punchDuration);
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);

                // 0 → 1 → 0：先放大旋转，再转回来缩小
                float phase = t < 0.4f ? t / 0.4f : 1f - (t - 0.4f) / 0.6f;
                float eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phase));

                if (coinText != null)
                {
                    RectTransform rect = coinText.rectTransform;
                    rect.localScale = baseScale * Mathf.Lerp(1f, Mathf.Max(1f, punchScale), eased);
                    rect.localRotation = baseRotation * Quaternion.Euler(0f, 0f, punchAngle * eased);
                }

                yield return null;
            }

            ResetPunchTransform();
            punchRoutine = null;
        }

        private void ResetPunchTransform()
        {
            if (coinText == null)
            {
                return;
            }

            coinText.rectTransform.localScale = baseScale;
            coinText.rectTransform.localRotation = baseRotation;
        }

        private void Refresh(int money)
        {
            if (coinText == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(format))
            {
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
