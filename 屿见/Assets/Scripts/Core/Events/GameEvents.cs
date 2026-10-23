using UnityEngine;
using Yujian.Building;
using Yujian.Shop;

namespace Yujian.Core.Events
{
    /// <summary>
    /// 跨模块事件的载荷定义。
    ///
    /// 全部是 readonly struct：订阅者只能读，不能改发布者的状态。
    /// 新增事件时**追加新类型**，不要改已有字段的顺序/含义（事件也会进日志与后续存档回放）。
    /// </summary>
    public static class GameEvents
    {
    }

    /// <summary>金币变化。Delta 为本次增量，可用于区分获得 / 消耗。</summary>
    public readonly struct MoneyChanged
    {
        public readonly int Amount;
        public readonly int Delta;

        public MoneyChanged(int amount, int delta)
        {
            Amount = amount;
            Delta = delta;
        }
    }

    /// <summary>库存变化。Delta 为本次增减数量。</summary>
    public readonly struct InventoryChanged
    {
        public readonly MaterialData Material;
        public readonly MaterialColor Color;
        public readonly int Delta;

        public InventoryChanged(MaterialData material, MaterialColor color, int delta)
        {
            Material = material;
            Color = color;
            Delta = delta;
        }
    }

    /// <summary>玩家等级变化。</summary>
    public readonly struct LevelChanged
    {
        public readonly int Level;

        public LevelChanged(int level)
        {
            Level = level;
        }
    }

    /// <summary>区域切换。</summary>
    public readonly struct AreaChanged
    {
        public readonly GameArea Area;

        public AreaChanged(GameArea area)
        {
            Area = area;
        }
    }

    /// <summary>选中建筑、蓝图出现。</summary>
    public readonly struct BlueprintOpened
    {
        public readonly BuildingData Data;

        public BlueprintOpened(BuildingData data)
        {
            Data = data;
        }
    }

    /// <summary>蓝图落位（冻结到格点，尚未建成）。</summary>
    public readonly struct BlueprintAnchored
    {
        public readonly Vector3 Position;
        public readonly RectInt Rect;

        public BlueprintAnchored(Vector3 position, RectInt rect)
        {
            Position = position;
            Rect = rect;
        }
    }

    /// <summary>蓝图关闭。Built=true 表示已建成，false 表示取消 / 撤回。</summary>
    public readonly struct BlueprintClosed
    {
        public readonly bool Built;

        public BlueprintClosed(bool built)
        {
            Built = built;
        }
    }

    /// <summary>一座建筑建成。</summary>
    public readonly struct BuildingPlaced
    {
        public readonly BuildingData Data;
        public readonly Vector3 Position;
        public readonly RectInt Rect;

        public BuildingPlaced(BuildingData data, Vector3 position, RectInt rect)
        {
            Data = data;
            Position = position;
            Rect = rect;
        }
    }

    /// <summary>一座建筑被移除。读档清场不会发布。</summary>
    public readonly struct BuildingRemoved
    {
        public readonly BuildingData Data;
        public readonly Vector3 Position;

        public BuildingRemoved(BuildingData data, Vector3 position)
        {
            Data = data;
            Position = position;
        }
    }

    /// <summary>一块材料放入蓝图。</summary>
    public readonly struct MaterialPlaced
    {
        public readonly MaterialData Material;
        public readonly MaterialColor Color;

        public MaterialPlaced(MaterialData material, MaterialColor color)
        {
            Material = material;
            Color = color;
        }
    }

    /// <summary>一块材料从蓝图撤回。</summary>
    public readonly struct MaterialUndone
    {
        public readonly MaterialData Material;
        public readonly MaterialColor Color;

        public MaterialUndone(MaterialData material, MaterialColor color)
        {
            Material = material;
            Color = color;
        }
    }

    /// <summary>蓝图内材料 / 进度变化，UI 收到后重新读状态。</summary>
    public readonly struct ConstructionChanged
    {
    }

    /// <summary>给玩家看的提示行。</summary>
    public readonly struct MessageShown
    {
        public readonly string Message;
        public readonly bool Success;

        public MessageShown(string message, bool success)
        {
            Message = message;
            Success = success;
        }
    }

    /// <summary>购买成功。</summary>
    public readonly struct PurchaseSucceeded
    {
        public readonly MaterialData Material;
        public readonly MaterialColor Color;
        public readonly int Amount;
        public readonly int TotalCost;

        public PurchaseSucceeded(MaterialData material, MaterialColor color, int amount, int totalCost)
        {
            Material = material;
            Color = color;
            Amount = amount;
            TotalCost = totalCost;
        }
    }

    /// <summary>购买失败。Reason 可以直接显示给玩家。</summary>
    public readonly struct PurchaseFailed
    {
        public readonly MaterialData Material;
        public readonly MaterialColor Color;
        public readonly int Amount;
        public readonly string Reason;

        public PurchaseFailed(MaterialData material, MaterialColor color, int amount, string reason)
        {
            Material = material;
            Color = color;
            Amount = amount;
            Reason = reason;
        }
    }

    /// <summary>矿石受击。Destroyed=true 表示这一击把矿石打碎。</summary>
    public readonly struct OreHit
    {
        public readonly Vector3 Position;
        public readonly bool Destroyed;

        public OreHit(Vector3 position, bool destroyed)
        {
            Position = position;
            Destroyed = destroyed;
        }
    }

    /// <summary>存档写入成功。</summary>
    public readonly struct SaveCompleted
    {
    }

    /// <summary>读档成功。</summary>
    public readonly struct LoadCompleted
    {
    }
}
