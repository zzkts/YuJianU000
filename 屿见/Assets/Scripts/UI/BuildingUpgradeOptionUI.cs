using System;
using UnityEngine;
using UnityEngine.UI;
using Yujian.Building;

namespace Yujian.UI
{
    /// <summary>
    /// 强化节点里的一条选项按钮。
    ///
    /// 挂在场景里的「选项模板」上，由 <see cref="BuildingUpgradeNodeUI"/> 运行时克隆。
    /// 名称 / 描述 / 按钮本身都是 Inspector 引用：
    ///   · 想改字体、颜色、间距 → 直接改场景里的模板；
    ///   · 想加图标 / 边框 → 往模板里加物体即可，本组件不会覆盖模板结构。
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildingUpgradeOptionUI : MonoBehaviour
    {
        [Header("引用")]
        [Tooltip("这一行的按钮。留空会在运行时尝试从本物体上取")]
        [SerializeField] private Button button;

        [Tooltip("强化名称文本")]
        [SerializeField] private Text nameText;

        [Tooltip("强化描述文本")]
        [SerializeField] private Text descriptionText;

        [Header("名称格式")]
        [Tooltip("{0} = 强化名称，{1} = 「全局」/「个体」")]
        [SerializeField] private string nameFormat = "{0}　<color=#8ee6a0>{1}</color>";

        /// <summary>
        /// 这一行的按钮。
        /// 引用必须属于本克隆体；若序列化引用还指在模板上（克隆时没重映射），
        /// 直接取本物体上的 Button，避免点克隆体没反应。
        /// </summary>
        public Button Button
        {
            get
            {
                if (button == null || !button.transform.IsChildOf(transform))
                {
                    button = GetComponent<Button>();
                }

                return button;
            }
        }

        /// <summary>编辑器向导调用，把模板里的引用接好。</summary>
        public void ConfigureForSetup(Button newButton, Text newNameText, Text newDescriptionText)
        {
            button = newButton;
            nameText = newNameText;
            descriptionText = newDescriptionText;
        }

        /// <summary>填入一条配置并挂上点击回调。重复绑定会先清掉旧点击。</summary>
        public void Bind(BuildingEnhancementOption option, Action onClick)
        {
            if (option == null)
            {
                return;
            }

            Text targetName = ResolveText(nameText, "名称", 0);
            Text targetDescription = ResolveText(descriptionText, "描述", 1);

            if (targetName != null)
            {
                targetName.text = string.Format(nameFormat, option.DisplayName, option.ScopeName);
            }

            if (targetDescription != null)
            {
                targetDescription.text = option.GetFullDescription();
            }

            Button target = Button;

            if (target == null)
            {
                Debug.LogError("[BuildingUpgradeOptionUI] 选项模板没有 Button，点了不会有反应。", this);
                return;
            }

            target.onClick.RemoveAllListeners();
            target.onClick.AddListener(() => onClick?.Invoke());
        }

        /// <summary>
        /// 取属于本克隆体的文本。
        /// 正常情况序列化引用会随 Instantiate 重映射；这里再兜一层，
        /// 万一引用还指在模板上，就按“名称 / 描述”或子物体顺序找回自己的文本，
        /// 避免所有克隆体都显示模板的占位文字。
        /// </summary>
        private Text ResolveText(Text assigned, string childName, int fallbackIndex)
        {
            if (assigned != null && assigned.transform.IsChildOf(transform))
            {
                return assigned;
            }

            Transform child = transform.Find(childName);

            if (child != null)
            {
                Text found = child.GetComponent<Text>();

                if (found != null)
                {
                    return found;
                }
            }

            Text[] texts = GetComponentsInChildren<Text>(true);
            return fallbackIndex >= 0 && fallbackIndex < texts.Length ? texts[fallbackIndex] : null;
        }

        private void Awake()
        {
            // 模板本身保持隐藏；克隆体由 BuildingUpgradeNodeUI 打开
            if (button == null)
            {
                button = GetComponent<Button>();
            }
        }
    }
}
