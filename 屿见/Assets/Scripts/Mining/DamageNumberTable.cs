using System;
using System.Collections.Generic;
using UnityEngine;

namespace Yujian.Mining
{
    /// <summary>
    /// 伤害数字配置表（策划案 2.1-3 的「数字类型配置表」）。
    /// 职责：按伤害来源提供颜色、字号、粗体；不生成、不播放伤害数字。
    /// 新增一种伤害来源时，在表里加一行即可，不需要改 <see cref="DamagePopup"/>。
    /// 资产放在 Assets/Data/Mining/DamageNumberTable.asset，由菜单
    /// 「屿见/配置伤害数字配置表（挖矿 2.1-3）」创建并接线。
    /// </summary>
    [CreateAssetMenu(menuName = "屿见/伤害数字配置表", fileName = "DamageNumberTable")]
    public class DamageNumberTable : ScriptableObject
    {
        /// <summary>一行配置，对应策划案表格里的一种数字类型。</summary>
        [Serializable]
        public class Style
        {
            [Tooltip("伤害来源。代码按该字段查表；每种来源只应出现一次")]
            [SerializeField] private DamageSource source = DamageSource.Player;

            [Tooltip("策划案里的数字类型名（Type_1 / Type_2），仅用于辨认，可随意修改")]
            [SerializeField] private string typeName = "Type_1";

            [Tooltip("数字颜色")]
            [SerializeField] private Color color = Color.white;

            [Tooltip("字号（uGUI Text 的 Font Size）")]
            [Min(1)]
            [SerializeField] private int fontSize = 16;

            [Tooltip("是否加粗")]
            [SerializeField] private bool bold;

            public DamageSource Source => source;

            public string TypeName => typeName;

            public Color Color => color;

            public int FontSize => fontSize;

            public bool Bold => bold;

            /// <summary>Unity 反序列化需要无参构造；Inspector 里新增行也走它。</summary>
            public Style()
            {
            }

            /// <summary>供代码在配置表缺行时构造兜底样式。</summary>
            public Style(DamageSource source, string typeName, Color color, int fontSize, bool bold)
            {
                this.source = source;
                this.typeName = typeName;
                this.color = color;
                this.fontSize = fontSize;
                this.bold = bold;
            }
        }

        [Tooltip("每种伤害来源一行。查不到时 DamagePopup 会报错并回退到内置默认值")]
        [SerializeField] private List<Style> styles = new List<Style>();

        /// <summary>表里所有行，供 Inspector / 外部只读查看。</summary>
        public IReadOnlyList<Style> Styles => styles;

        /// <summary>按来源查一行配置。</summary>
        public bool TryGet(DamageSource source, out Style style)
        {
            if (styles != null)
            {
                for (int i = 0; i < styles.Count; i++)
                {
                    if (styles[i] != null && styles[i].Source == source)
                    {
                        style = styles[i];
                        return true;
                    }
                }
            }

            style = null;
            return false;
        }
    }
}
