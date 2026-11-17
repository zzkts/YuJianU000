# 屿见 · 模型资产包（6 件）

按《3D 资产规范》命名完毕，可直接放进 Unity 工程 `屿见/Assets/Models/Buildings/` 下，也可直接上传 GitHub。

## 目录结构

```
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
