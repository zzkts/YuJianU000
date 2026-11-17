# 屿见 Yujian · Meshy AI 低模资产包

《屿见·八卦楼》项目用的 6 个 AI 生成建筑/景观模型，导出日期 2026-11-15。全部由 Meshy AI（图生 3D）生成，命名为《3D 资产规范》：`名称.fbx`，集合体按 `_01/_02` 序列编号。

## 目录结构

## 清单

| 规范名 | 对应建筑 | 面数 | 说明 |
|---|---|---|---|
| `Haitian` | 海天堂构 | 2,285,664 | 最重，109MB |
| `Fengqing` | 鼓浪屿风琴博物馆 | 1,051,970 | |
| `Shuzhuang_01` | 菽庄花园·石桥置石 | 10,139 | 低模平涂 |
| `Shuzhuang_02` | 菽庄花园·Qianbo 亭 | 101,264 | 屋顶为平板，待重做 |
| `Shuzhuang_03` | 菽庄花园·Zhenlu 亭 | 184,692 | 形体最完整 |
| `Shuzhuang_04` | 菽庄花园·Duyue 亭 | 128,170 | 廊亭，矮胖带长凳 |

## Unity 导入要点

- Meshy 导出为 **Y-up**，导入 Unity 不需要额外转 -90°；单位是 cm，导入后需按目标尺寸缩放
- 每件为**单网格 + 单材质**（`Material.001`），贴图需手动挂：basecolor → `_MainTex`，normal → `_BumpMap`
- 建议目标最大边：`Haitian` 37m、`Fengqing` 30m、`Shuzhuang_01` 24m、其余亭子 12m

## 存储

`.fbx` / `.png` 走 **Git LFS**（见仓库根 `.gitattributes`）。克隆后若文件只有 100 多字节，说明没装 LFS，`git lfs install && git lfs pull` 即可。