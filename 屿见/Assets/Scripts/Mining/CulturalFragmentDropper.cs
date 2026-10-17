using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Yujian.Mining
{
    /// <summary>
    /// 挂在矿石上：矿石被击碎时按概率掉一枚“文化碎片”。
    /// 碎片就是鼓浪屿冷知识，后面的图鉴可以直接读这里的数据。
    /// 由 Ore.Die() 调用 Roll()，也可以在其他挖矿代码里手动调用。
    /// </summary>
    public class CulturalFragmentDropper : MonoBehaviour
    {
        [Header("掉落概率")]
        [Range(0f, 1f)]
        [SerializeField] private float dropChance = 0.08f;

        [Tooltip("花岗岩这种稀有矿击碎必掉一片，避免玩家一直刷不到彩蛋")]
        [SerializeField] private bool rareOreAlwaysDrop = true;

        [Header("提示")]
        [SerializeField] private Color popupColor = new Color(1f, 0.84f, 0.35f);
        [SerializeField] private float popupHeight = 1.6f;

        /// <summary>本次游戏累计拿到的碎片数量，HUD 和结算界面可以直接读。</summary>
        public static int TotalCollected { get; private set; }

        /// <summary>已经解锁的碎片条数，图鉴按这个数字点亮。</summary>
        public static int UnlockedCount { get { return unlocked.Count; } }

        /// <summary>总共准备了多少条冷知识。</summary>
        public static int FragmentCount { get { return fragments.Length; } }

        /// <summary>拿到碎片时触发，参数是累计数量。</summary>
        public static event UnityAction<int> OnCollected;

        /// <summary>解锁一条新知识时触发，参数是碎片下标和标题。</summary>
        public static event UnityAction<int, string> OnUnlocked;

        public static string LastUnlockedTitle { get; private set; }
        public static string LastUnlockedText { get; private set; }

        private static readonly List<int> unlocked = new List<int>();

        // 鼓浪屿冷知识。文案同学可以直接改这里，不影响其他代码。
        private static readonly Fragment[] fragments =
        {
            new Fragment("八卦楼的红屋顶",
                "八卦楼的红色圆顶有八个棱面，老厦门人看着像八卦，这个名字就叫开了。它1907年建成，最早是林鹤寿的私家别墅。"),

            new Fragment("菽庄花园的藏海",
                "菽庄花园1913年建成，园主林尔嘉用自己字“叔臧”的谐音给它命名。进门先看不到海，绕过月洞门才豁然见海，这就是有名的藏海手法。"),

            new Fragment("钢琴博物馆",
                "菽庄花园里的听涛轩是钢琴博物馆，2000年开馆，收藏了胡友义先生捐赠的古钢琴，很多都是全世界只剩个位数的型号。"),

            new Fragment("亚洲最大的管风琴",
                "鼓浪屿有一台1917年制造的卡萨翁700号管风琴，是亚洲最大的管风琴之一，每天有固定演奏场次，声音大到能震得胸口发麻。"),

            new Fragment("万国建筑博览",
                "岛上保存着上千栋老别墅，哥特式、罗马式、南洋骑楼挤在一条街上。先后有十几个国家在岛上设过领事馆，所以被称为万国建筑博览。"),

            new Fragment("没有汽车的小岛",
                "鼓浪屿岛上基本看不到汽车，运货靠板车和电瓶车。上岛只能坐船，所以连快递都要先坐轮渡。"),

            new Fragment("琴岛的由来",
                "鼓浪屿被叫作钢琴之岛。鼎盛时期，岛上几百户人家里就有上百台钢琴，傍晚沿着小巷走，常能听见此起彼伏的练琴声。"),

            new Fragment("防空洞",
                "岛上还留着抗战时期挖的防空洞，有些入口就藏在老别墅的院子里。它们不是景点，却是这座岛最沉默的一段记忆。"),

            new Fragment("掌中木偶戏",
                "厦门布袋木偶戏是国家级非物质文化遗产。艺人一只手伸进木偶里，就能同时演出生气、害羞、打架这些细节。"),

            new Fragment("日光岩的名字",
                "日光岩是岛上最高的地方，原名晃岩。传说郑成功觉得晃字不够气派，把它拆成了日光两个字。"),
        };

        /// <summary>按概率判定是否掉碎片。矿石击碎时调用一次。</summary>
        public void Roll()
        {
            bool rare = rareOreAlwaysDrop && IsRareOre();
            if (!rare && Random.value > dropChance) return;
            Drop();
        }

        /// <summary>无视概率，直接掉一片。新手引导和测试时用。</summary>
        [ContextMenu("测试：立刻掉一片")]
        public void Drop()
        {
            int index = PickIndex();
            Fragment fragment = fragments[index];

            TotalCollected++;
            LastUnlockedTitle = fragment.title;
            LastUnlockedText = fragment.text;

            bool isNew = !unlocked.Contains(index);
            if (isNew) unlocked.Add(index);

            FragmentPopup.Spawn(
                transform.position + Vector3.up * popupHeight,
                "文化碎片 +1\n「" + fragment.title + "」",
                popupColor);

            Debug.Log("[屿见] 文化碎片 +1：" + fragment.title + " —— " + fragment.text);

            if (OnCollected != null) OnCollected(TotalCollected);
            if (isNew && OnUnlocked != null) OnUnlocked(index, fragment.title);
        }

        /// <summary>清空统计。做“重新开始”时调用。</summary>
        public static void ResetProgress()
        {
            TotalCollected = 0;
            unlocked.Clear();
            LastUnlockedTitle = null;
            LastUnlockedText = null;
        }

        private bool IsRareOre()
        {
            OreTypeTag tag = GetComponent<OreTypeTag>();
            return tag != null && OreTypeInfo.IsRare(tag.Type);
        }

        private static int PickIndex()
        {
            // 优先给还没解锁的，收集感更强；全解锁后随机重复。
            if (unlocked.Count >= fragments.Length) return Random.Range(0, fragments.Length);

            for (int attempt = 0; attempt < 64; attempt++)
            {
                int i = Random.Range(0, fragments.Length);
                if (!unlocked.Contains(i)) return i;
            }

            for (int i = 0; i < fragments.Length; i++)
            {
                if (!unlocked.Contains(i)) return i;
            }

            return 0;
        }

        private class Fragment
        {
            public readonly string title;
            public readonly string text;

            public Fragment(string title, string text)
            {
                this.title = title;
                this.text = text;
            }
        }
    }
}