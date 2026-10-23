namespace Yujian.Audio
{
    /// <summary>
    /// 全部音频的稳定标识。代码里只引用这个枚举，不直接引用 AudioClip——
    /// 换素材、调音量、改冷却都只需要改 AudioCatalog 资产，不用改代码。
    ///
    /// **只追加，不要改已有项的数值，也不要改名字。**
    /// 数值会进日志与后续可能的存档 / 配置；改名会让配置表里的条目失联。
    /// </summary>
    public enum AudioId
    {
        None = 0,

        // ---------------- 界面 ----------------
        UiClick = 10,
        UiOpen = 11,
        UiClose = 12,

        // ---------------- 金币 / 库存 ----------------
        CoinGain = 20,
        CoinSpend = 21,
        ItemGain = 22,

        // ---------------- 挖矿 ----------------
        OreHit = 30,
        OreBreak = 31,

        // ---------------- 建造 ----------------
        BlueprintAnchor = 40,
        BlueprintCancel = 41,
        MaterialPlace = 42,
        MaterialUndone = 43,
        BuildComplete = 44,
        BuildingRemoved = 45,

        // ---------------- 商店 / 成长 ----------------
        PurchaseSuccess = 50,
        PurchaseFailed = 51,
        LevelUp = 52,

        // ---------------- 提示 ----------------
        MessageSuccess = 60,
        MessageFail = 61,

        // ---------------- 音乐 ----------------
        MusicMining = 100,
        MusicBuilding = 101,
        MusicShop = 102,
    }
}
