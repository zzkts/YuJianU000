using UnityEngine;

namespace Yujian.Core
{
    /// <summary>
    /// 数字缩写（2026-11-26）。
    ///
    /// 为什么单独拎出来：伤害数字以前直接 ToString()，新曲线下 40 级伤害已经到 4 万多，
    /// 再往上会到百万、十亿，屏幕上会糊一串长数字。HUD 里早就有这套 k/m/b 缩写，
    /// 现在把它变成全工程唯一的实现，HUD 和伤害数字共用一套规则，不会出现
    /// 「界面写 1.2m、飘字写 1234567」的不一致。
    ///
    /// 规则：
    ///   · &lt; 1000 → 保留小数（2.2、18.5），伤害早期是有小数的；
    ///   · ≥ 1000 → k / m / b / t，再往上按 aa / bb / cc 递增；
    ///   · 缩放后 ≥ 100 不保留小数（431k），≥ 10 保留一位（43.1k），否则两位（4.32k）。
    /// </summary>
    public static class NumberFormat
    {
        private static readonly string[] CommonUnits = { string.Empty, "k", "m", "b", "t" };

        /// <summary>整数缩写。金币、垃圾、价格都用这个。</summary>
        public static string Compact(long value)
        {
            if (value < 0)
            {
                return "-" + Compact(-value);
            }

            if (value < 1000)
            {
                return value.ToString();
            }

            return CompactScaled(value);
        }

        /// <summary>浮点缩写。伤害数字用这个：小数值保留小数，大数值走单位。</summary>
        public static string Compact(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "0";
            }

            if (value < 0f)
            {
                return "-" + Compact(-value);
            }

            if (value < 1000f)
            {
                // 伤害早期是 2.2 / 18.5 这种，直接去掉小数会让玩家以为数值没变
                return value.ToString("0.##");
            }

            return CompactScaled(value);
        }

        private static string CompactScaled(double value)
        {
            double scaled = value;
            int unit = 0;

            while (scaled >= 1000d && unit < CommonUnits.Length - 1)
            {
                scaled /= 1000d;
                unit++;
            }

            string suffix;

            if (scaled >= 1000d)
            {
                int extra = 0;

                while (scaled >= 1000d)
                {
                    scaled /= 1000d;
                    extra++;
                }

                suffix = SuffixFor(extra);
            }
            else
            {
                suffix = CommonUnits[unit];
            }

            string number = scaled >= 100d ? scaled.ToString("0")
                : scaled >= 10d ? scaled.ToString("0.#")
                : scaled.ToString("0.##");

            return number + suffix;
        }

        private static string SuffixFor(int index)
        {
            // 1 → aa，2 → bb，3 → cc …… 26 → zz，27 → aaa
            int safe = Mathf.Max(1, index) - 1;
            int letter = safe % 26;
            int repeat = safe / 26 + 2;
            char c = (char)('a' + letter);
            return new string(c, repeat);
        }
    }
}
