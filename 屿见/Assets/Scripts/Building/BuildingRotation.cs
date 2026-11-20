using UnityEngine;

namespace Yujian.Building
{
    /// <summary>
    /// 建筑朝向的共用换算（策划案 3.3 第 4 条「建筑旋转功能」）。
    ///
    /// 朝向只记录「转了几个 90°」（0~3），不存欧拉角：
    ///   · 存档里就是一个 int，旧存档缺字段时默认 0（不旋转）；
    ///   · 判断是否交换宽高只看奇偶，不会出现 359.99° 这类浮点误差；
    ///   · 判定「能不能放下」「占哪几格」和外观旋转都用同一套换算，不会各算各的。
    ///
    /// 约定：QuarterTurns 每 +1 表示绕世界 Y 轴顺时针转 90°（俯视视角）。
    /// </summary>
    public static class BuildingRotation
    {
        /// <summary>一圈分成几个 90°。归一化时用。</summary>
        public const int QuarterTurnCount = 4;

        /// <summary>把任意整数转成 0~3。负数也能正确回绕（-1 → 3）。</summary>
        public static int Normalize(int quarterTurns)
        {
            int value = quarterTurns % QuarterTurnCount;
            return value < 0 ? value + QuarterTurnCount : value;
        }

        /// <summary>
        /// 旋转后的占地格数：转 90° / 270° 时宽高互换。
        /// 4×2 的建筑转一次就是 2×4，占格判定和存档都要跟着一起变。
        /// </summary>
        public static Vector2Int RotateCells(Vector2Int cells, int quarterTurns)
        {
            return Normalize(quarterTurns) % 2 == 1
                ? new Vector2Int(cells.y, cells.x)
                : cells;
        }

        /// <summary>朝向 → 欧拉角（只动 Y 轴）。</summary>
        public static Vector3 ToEuler(int quarterTurns)
        {
            return new Vector3(0f, Normalize(quarterTurns) * 90f, 0f);
        }

        /// <summary>朝向 → 旋转量，直接赋给 transform.rotation。</summary>
        public static Quaternion ToQuaternion(int quarterTurns)
        {
            return Quaternion.Euler(ToEuler(quarterTurns));
        }
    }
}
