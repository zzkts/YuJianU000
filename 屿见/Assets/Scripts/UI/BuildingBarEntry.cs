using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yujian.Building;

namespace Yujian.UI
{
    /// <summary>
    /// 建筑栏位里的一个建筑按钮（策划案 3.7）。
    ///
    /// 只负责「把自己画成当前状态」：可建 / 选中 / 未选中 / 不可建 / 原料刚好够。
    /// 能不能建由 BuildingUI.Evaluate 判定，本类一个字都不判，只读结论。
    ///
    /// 按钮本身的 ColorTint 被关掉了（transition = None），
    /// 否则 Button 会在每次状态切换时把背景色改回去，和我们手写的配色打架。
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingBarEntry : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float UnselectedAlpha = 0.78f;

        private static readonly Color AvailableBackground = new Color(0.17f, 0.19f, 0.23f, 0.96f);
        private static readonly Color SelectedBackground = new Color(0.25f, 0.29f, 0.36f, 1f);
        private static readonly Color DisabledBackground = new Color(0.11f, 0.11f, 0.13f, 0.92f);

        private static readonly Color NameColor = new Color(0.96f, 0.96f, 0.96f, 1f);
        private static readonly Color DisabledNameColor = new Color(0.55f, 0.55f, 0.55f, 1f);

        private static readonly Color ReadyStatusColor = new Color(0.72f, 0.92f, 0.72f, 1f);
        private static readonly Color JustEnoughStatusColor = new Color(1f, 0.87f, 0.45f, 1f);
        private static readonly Color DisabledStatusColor = new Color(0.62f, 0.62f, 0.62f, 1f);

        private static readonly Color SelectedGlowColor = new Color(1f, 0.84f, 0.40f, 0.60f);
        private static readonly Color JustEnoughGlowColor = new Color(1f, 0.90f, 0.55f, 0.85f);

        private Image background;
        private Outline glow;
        private CanvasGroup canvasGroup;
        private Text nameText;
        private Text statusText;
        private Button button;

        private bool isSelected;
        private bool isAvailable;
        private bool isHovered;

        /// <summary>由 BuildingBarUI 在创建条目时调用，把引用塞进来。</summary>
        public void Bind(Image entryBackground, Outline entryGlow, CanvasGroup group,
                         Text entryNameText, Text entryStatusText, Button entryButton)
        {
            background = entryBackground;
            glow = entryGlow;
            canvasGroup = group;
            nameText = entryNameText;
            statusText = entryStatusText;
            button = entryButton;
        }

        /// <summary>按判定结果刷新外观。</summary>
        public void Apply(BuildingData data, BuildingAvailabilityInfo info, bool selected)
        {
            isSelected = selected;
            isAvailable = info.IsAvailable;

            if (nameText != null)
            {
                nameText.text = data != null ? data.BuildingName : "未命名建筑";
                nameText.color = isAvailable ? NameColor : DisabledNameColor;
            }

            if (statusText != null)
            {
                if (isAvailable)
                {
                    statusText.text = info.JustEnoughMaterials ? "原料刚好够" : "可建造";
                    statusText.color = info.JustEnoughMaterials ? JustEnoughStatusColor : ReadyStatusColor;
                }
                else
                {
                    statusText.text = string.IsNullOrEmpty(info.Reason) ? "暂不可建" : info.Reason;
                    statusText.color = DisabledStatusColor;
                }
            }

            if (background != null)
            {
                background.color = !isAvailable
                    ? DisabledBackground
                    : (isSelected ? SelectedBackground : AvailableBackground);
            }

            if (button != null)
            {
                button.interactable = isAvailable;
            }

            if (glow != null)
            {
                glow.enabled = isAvailable && (isSelected || info.JustEnoughMaterials);
                glow.effectColor = isSelected ? SelectedGlowColor : JustEnoughGlowColor;
            }

            ApplyAlpha();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
            ApplyAlpha();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
            ApplyAlpha();
        }

        private void ApplyAlpha()
        {
            if (canvasGroup == null)
            {
                return;
            }

            bool full = isSelected || !isAvailable || isHovered;
            canvasGroup.alpha = full ? 1f : UnselectedAlpha;
        }
    }
}