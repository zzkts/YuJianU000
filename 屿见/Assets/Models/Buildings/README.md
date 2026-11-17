<<<<<<< Updated upstream
# 屿见 Yujian · Meshy AI 低模资产包

《屿见·八卦楼》项目用的 6 个 AI 生成建筑/景观模型，导出日期 **2026-11-15**。
全部由 Meshy AI（图生 3D）生成，命名遵循《3D 资产规范》：`名称.fbx`，集合体按 `_01/_02` 序列编号。

![预览](preview_all.png)
=======
# 屿见 · 模型资产包（6 件）

按《3D 资产规范》命名完毕，可直接放进 Unity 工程 `屿见/Assets/Models/Buildings/` 下，也可直接上传 GitHub。
>>>>>>> Stashed changes

## 目录结构

```
<<<<<<< Updated upstream
yujian-models-20261115/
├── README.md
├── .gitattributes
├── preview_all.png
├── Haitian/
│   ├── Haitian.fbx
│   ├── preview.png
│   └── textures/{basecolor,normal,metallic,roughness}.png
├── Fengqing/
├── Shuzhuang_01/
├── Shuzhuang_02/
├── Shuzhuang_03/
└── Shuzhuang_04/
```

贴图统一使用 ASCII 规范名 `basecolor` / `normal` / `metallic` / `roughness`。
未输出 metallic/roughness 的模型，`textures/` 里只有 `basecolor.png`。

## 清单

| # | 文件 | 建筑 | 大小 | 三角面 | 归一化尺寸 宽×进深×高 (m) | 贴图 | 备注 |
|---|---|---|---|---|---|---|---|
| 1 | `Haitian.fbx` | 海天堂构 | 109.4 MB | 2,285,664 | 37.0 × 28.2 × 31.4 | 4 张 | 超 100 万面，仅展示/远景；原 Meshy_AI_Haitiantang_Optimized_Building_20261110 |
| 2 | `Fengqing.fbx` | 鼓浪屿风琴博物馆 | 55.6 MB | 1,051,970 | 30.0 × 25.9 × 21.4 | 4 张 | 红穹顶 + 橙屋面；原 Meshy_AI_dome_building_model_20261110 |
| 3 | `Shuzhuang_01.fbx` | 菽庄花园 · 石桥置石 | 2.1 MB | 10,139 | 17.2 × 24.0 × 9.0 | 1 张 | 桥与两块置石同网格，拆不开；原 Meshy_AI_lowpoly_stone_bridge_rocks_20261111 |
| 4 | `Shuzhuang_02.fbx` | 菽庄花园 · Qianbo 亭 | 16.1 MB | 101,264 | 11.9 × 12.0 × 9.8 | 4 张 | 屋顶生成失败成平板；原 Meshy_AI_Qianbo_Pavilion_LowPoly_20261111 |
| 5 | `Shuzhuang_03.fbx` | 菽庄花园 · Zhenlu 亭 | 19.3 MB | 184,692 | 11.5 × 11.6 × 12.0 | 4 张 | 本包最完整的亭子；原 Meshy_AI_Zhenlu_Pavilion_Low_Poly_20261111 |
| 6 | `Shuzhuang_04.fbx` | 菽庄花园 · Duyue 亭 | 18.1 MB | 128,170 | 12.0 × 8.2 × 6.8 | 4 张 | 矮胖带长凳的廊亭；原 Meshy_AI_Duyue_Pavilion_Low_Poly_20261111 |

合计约 **367 MB**，6 个 fbx。

## 命名对照（原名 → 规范名）

| 原名 slug | 规范名 | 说明 |
|---|---|---|
| haitiantang | `Haitian` | 海天堂构 |
| dome | `Fengqing` | 鼓浪屿风琴博物馆 |
| stone_bridge | `Shuzhuang_01` | 菽庄花园集合体第 1 件（石桥 + 置石） |
| qianbo | `Shuzhuang_02` | 菽庄花园集合体第 2 件（Qianbo 亭） |
| zhenlu | `Shuzhuang_03` | 菽庄花园集合体第 3 件（Zhenlu 亭） |
| duyue | `Shuzhuang_04` | 菽庄花园集合体第 4 件（Duyue 亭） |

## 导入须知

1. **单位**：Meshy 的 FBX 按厘米导出，Unity 导入后预制体根节点自带 ×100 缩放。
   缩放到真实尺寸要用 `transform.localScale *= k`，直接赋值会小 100 倍（本项目踩过这个坑）。
2. **朝向**：Y-up、正面朝 -Z，Unity 导入无需转轴。
3. **网格**：每个模型是单个 mesh、单材质槽，材质名 `Material.001`。
4. **材质**：Standard/Lit + basecolor，法线挂 `_BumpMap`；metallic 图纯黑，按不金属处理。
5. **面数**：Haitian 228 万面、Fengqing 105 万面，只能做远景/展示；Shuzhuang 四件可直接进场景。

## 推到 GitHub

`Haitian.fbx` 为 109.4 MB，**超过 GitHub 单文件 100 MB 硬限制**，必须走 Git LFS：

```bash
git lfs install
git add .
git commit -m "add yujian meshy low-poly model set"
git push
```

不想用 LFS，就把 `Haitian/` 单独挂到 Release 附件，仓库里只放其余 5 件。

> GitHub 免费 LFS 配额 1 GB 存储 / 1 GB 月流量，本包 367 MB 放得下。

## 来源与授权

- 生成工具：Meshy AI 图生 3D（image-to-3d + texture），生成日期 2026-11-10 ~ 2026-11-11。
- 用途：项目内部原型与展示；对外发布前确认账号套餐的商用条款。
- 项目内登记：`D:\\3D\\yujian\\ASSET_REGISTRY.md`。
=======
Haitian/          Haitian.fbx        textures/basecolor,normal,metallic,roughness.png  preview.png
Fengqing/         Fengqing.fbx       textures/(4 张)                                   preview.png
Shuzhuang_01/     Shuzhuang_01.fbx   textures/basecolor.png                            preview.png
Shuzhuang_02/     Shuzhuang_02.fbx   textures/(4 张)                                   preview.png
Shuzhuang_03/     Shuzhuang_03.fbx   textures/(4 张)                                   preview.png
Shuzhuang_04/     Shuzhuang_04.fbx   textures/(4 张)                                   preview.png
```

`_原始压缩包/` 里是 Meshy 下载的原 zip，仅作备份，**不要上传**。

## 原名 → 规范名

| 原文件 | 规范名 | 面数 | 说明 |
|---|---|---|---|
| Meshy_AI_Haitiantang_Optimized_Building | `Haitian` | 2,285,664 | 海天堂构；114MB，最重 |
| Meshy_AI_dome_building_model | `Fengqing` | 1,051,970 | 鼓浪屿风琴博物馆 |
| Meshy_AI_lowpoly_stone_bridge_rocks | `Shuzhuang_01` | 10,139 | 菽庄花园·石桥置石 |
| Meshy_AI_Qianbo_Pavilion_LowPoly | `Shuzhuang_02` | 101,264 | 亭；屋顶是平板，需重做 |
| Meshy_AI_Zhenlu_Pavilion_Low_Poly | `Shuzhuang_03` | 184,692 | 亭；形体最完整 |
| Meshy_AI_Duyue_Pavilion_Low_Poly | `Shuzhuang_04` | 128,170 | 廊亭；矮胖，带长凳 |

## 上传 GitHub 注意（重要）

- `Haitian.fbx` 是 **114MB**，超过 GitHub 单文件 100MB 上限，**必须走 Git LFS**，否则网页上传会直接失败（这很可能就是上次“看着是空文件夹”的原因之一）。
- 仓库根目录要有 `.gitattributes`：

```
*.fbx filter=lfs diff=lfs merge=lfs -text
*.png filter=lfs diff=lfs merge=lfs -text
```

- 网页端上传不支持 LFS 大文件，用 **GitHub Desktop**：拖进工作区 → Commit → Push。
- 若只是给同学看，直接把这份文件夹打包发给他更快，不必等仓库。

## Unity 导入要点

- Meshy 导出为 **Y-up**，导入 Unity **不要**再转 -90°；单位是 cm（导入后模型会在 ~190 单位量级，需要缩放到目标尺寸）。
- 每件都是**单网格 + 单材质**，材质名 `Material.001`；贴图要手动挂 basecolor 到 `_MainTex`、normal 到 `_BumpMap`。
- 目标最大边（建议）：`Haitian` 37m、`Fengqing` 30m、`Shuzhuang_01` 24m、`Shuzhuang_02` 12m、`Shuzhuang_03` 12m、`Shuzhuang_04` 12m。
>>>>>>> Stashed changes
