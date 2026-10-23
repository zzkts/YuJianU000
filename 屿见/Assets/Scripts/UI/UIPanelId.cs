namespace Yujian.UI
{
    /// <summary>
    /// 全局 UI 面板的稳定标识。
    ///
    /// 用枚举而不是字符串：拼错会在编译期被抓到，改名也能全局重命名。
    /// 新增面板一律追加到末尾并显式写数值。已有项的数值**不要改**——
    /// 场景里已经序列化的 panelId 会静默指向别的面板。
    /// </summary>
    public enum UIPanelId
    {
        None = 0,

        /// <summary>商店面板。</summary>
        Shop = 1,

        /// <summary>建筑列表面板。</summary>
        Building = 2,

        /// <summary>建造材料面板（由 BuildingMaterialPanel 自己管显隐）。</summary>
        Material = 3,

        /// <summary>底部导航栏。</summary>
        BottomNav = 4,

        /// <summary>金币 HUD。</summary>
        CoinHud = 5,

        /// <summary>通用提示条 / Toast。</summary>
        Message = 6,

        /// <summary>设置面板（预留）。</summary>
        Settings = 7,

        /// <summary>背包 / 库存面板（预留）。</summary>
        Inventory = 8,
    }
}
