using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Yujian.Building;
using Yujian.Core.Events;

namespace Yujian.UI
{
    /// <summary>
    /// 特殊建筑「场上最多一座」的入口表现（策划案 4.1 追问结论）。
    ///
    /// 职责只有两件：
    ///   · 订阅建筑增删事件，把已建成的特殊建筑按钮置灰；
    ///   · 光标停在置灰按钮上时，在面板提示文字里说明原因。
    ///
    /// 是否可建的唯一判定在 <see cref="BuildingUI.IsBuildingAvailable"/>，
    /// 这里只负责表现，避免按钮状态与点击校验两套规则。
    /// </summary>
    public class SpecialBuildingAvailabilityUI : MonoBehaviour
    {
        [Header("依赖")]
        [Tooltip("建筑入口。按钮下标与它的 Available Buildings 一一对应")]
        [SerializeField] private BuildingUI buildingUI;

        [Tooltip("与 Available Buildings 同序的按钮列表")]
        [SerializeField] private Button[] buildingButtons = new Button[0];

        [Header("提示")]
        [Tooltip("面板上的提示文字。留空则只在 Console 中提示")]
        [SerializeField] private Text hintText;

        [Tooltip("按钮文字变成灰色时的颜色")]
        [SerializeField] private Color disabledTextColor = new Color(0.55f, 0.55f, 0.55f);

        [Tooltip("提示文字相对光标的偏移（默认放在光标右下）")]
        [SerializeField] private Vector2 hintCursorOffset = new Vector2(18f, -18f);

        [Header("调试")]
        [Tooltip("是否在 Console 打印按钮可用状态变化")]
        [SerializeField] private bool logChanges = true;

        private Color[] originalTextColors = new Color[0];
        private Text[] buttonLabels = new Text[0];
        private bool[] lastAvailable = new bool[0];
        private bool initialized;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<BuildingPlaced>(HandleBuildingChanged);
            EventBus.Subscribe<BuildingRemoved>(HandleBuildingChanged);
            EventBus.Subscribe<LoadCompleted>(HandleLoadCompleted);

            Refresh();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<BuildingPlaced>(HandleBuildingChanged);
            EventBus.Unsubscribe<BuildingRemoved>(HandleBuildingChanged);
            EventBus.Unsubscribe<LoadCompleted>(HandleLoadCompleted);

            HideHint();
        }

        /// <summary>按当前场上建筑重新计算按钮置灰状态。可被外部兜底调用。</summary>
        public void Refresh()
        {
            if (!initialized)
            {
                Initialize();
            }

            if (buildingUI == null)
            {
                return;
            }

            int count = buildingButtons != null ? buildingButtons.Length : 0;

            for (int i = 0; i < count; i++)
            {
                Button button = buildingButtons[i];

                if (button == null)
                {
                    continue;
                }

                bool available = buildingUI.IsBuildingAvailable(i, out string reason);
                button.interactable = available;

                if (buttonLabels[i] != null)
                {
                    buttonLabels[i].color = available ? originalTextColors[i] : disabledTextColor;
                }

                if (lastAvailable[i] != available)
                {
                    lastAvailable[i] = available;

                    if (logChanges)
                    {
                        string name = button.name;
                        Debug.Log(available
                            ? $"[SpecialBuildingAvailabilityUI] 按钮「{name}」恢复可建造。"
                            : $"[SpecialBuildingAvailabilityUI] 按钮「{name}」置灰：{reason}", this);
                    }
                }
            }
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            int count = buildingButtons != null ? buildingButtons.Length : 0;
            ArrayUtilityResize(count);

            for (int i = 0; i < count; i++)
            {
                Button button = buildingButtons[i];

                if (button == null)
                {
                    continue;
                }

                buttonLabels[i] = button.GetComponentInChildren<Text>(true);
                originalTextColors[i] = buttonLabels[i] != null ? buttonLabels[i].color : Color.white;
                lastAvailable[i] = true; // 场景里按钮默认可点；首次 Refresh 只因「已建成」而置灰

                ButtonHintRelay relay = button.GetComponent<ButtonHintRelay>();

                if (relay == null)
                {
                    relay = button.gameObject.AddComponent<ButtonHintRelay>();
                }

                relay.Owner = this;
                relay.Index = i;
            }

            HideHint();
        }

        private void ArrayUtilityResize(int count)
        {
            originalTextColors = new Color[count];
            buttonLabels = new Text[count];
            lastAvailable = new bool[count];
        }

        private void HandleBuildingChanged(BuildingPlaced evt)
        {
            Refresh();
        }

        private void HandleBuildingChanged(BuildingRemoved evt)
        {
            Refresh();
        }

        private void HandleLoadCompleted(LoadCompleted evt)
        {
            Refresh();
        }

        private void HandleHover(int index, bool entered, Vector2 screenPosition)
        {
            if (!entered)
            {
                HideHint();
                return;
            }

            if (buildingUI == null || hintText == null)
            {
                return;
            }

            if (buildingUI.IsBuildingAvailable(index, out string reason))
            {
                HideHint();
                return;
            }

            hintText.text = reason;
            hintText.gameObject.SetActive(true);
            PositionHint(screenPosition);
        }

        private void PositionHint(Vector2 screenPosition)
        {
            RectTransform hintRect = hintText.rectTransform;
            RectTransform parentRect = hintRect.parent as RectTransform;

            if (parentRect == null)
            {
                return;
            }

            Camera camera = hintText.canvas != null && hintText.canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? hintText.canvas.worldCamera
                : null;

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parentRect, screenPosition, camera, out Vector2 localPoint))
            {
                return;
            }

            hintRect.anchorMin = new Vector2(0.5f, 0.5f);
            hintRect.anchorMax = new Vector2(0.5f, 0.5f);
            hintRect.pivot = new Vector2(0f, 1f);
            hintRect.anchoredPosition = localPoint + hintCursorOffset;
        }

        private void HideHint()
        {
            if (hintText != null && hintText.gameObject.activeSelf)
            {
                hintText.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 挂在每个建筑按钮上，把指针悬停转发回本组件。
        /// Button 被置灰后仍会收到指针事件（Graphic 仍是 raycast target），所以提示照常可用。
        /// </summary>
        private sealed class ButtonHintRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public SpecialBuildingAvailabilityUI Owner;
            public int Index;

            public void OnPointerEnter(PointerEventData eventData)
            {
                Owner?.HandleHover(Index, true, eventData.position);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                Owner?.HandleHover(Index, false, eventData.position);
            }
        }
    }
}
