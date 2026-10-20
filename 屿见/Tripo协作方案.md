# Tripo 3D 建模协作方案（讨论记录）

日期：2026-10-19
项目：《屿见》（Unity 6000.3.8f1 / URP 17.3.0）

---

## 一、结论

可以协作，但要分清边界：

- **不能**直接操作 Tripo 网页，也**不能**“看到”或主观判断生成出来的模型；
- **可以**把 Tripo 接进工作流：写 API 调用、批量生成、下载、导入 Unity、规范化模型；
- 最终美术筛选、风格判断仍需要人来做。

---

## 二、我能做的事

### 1. 生成侧

- 编写 Tripo API 脚本（Python / Node / C#）：
  - 文生 3D（text-to-model）
  - 图生 3D（image-to-model）
  - 多视图生成
  - 创建任务 → 轮询 `task_id` → 下载模型
- 设计提示词、参考图要求与参数（低模、面数、PBR、quad 拓扑等）。

### 2. Unity 导入侧

- 写 Editor 工具：批量导入模型 → 自动设置 Scale / Pivot / Layer / Collider → 生成 Prefab。
- 按《屿见》的网格规则自动计算 `Footprint`（单格 2 世界单位），并把模型底面对齐到地面。
- 生成或替换 URP 材质；必要时转为低模平面色风格。

### 3. 资产规范化

- 检查法线、平滑组、UV、材质槽、三角形预算；
- 做 LOD、合并材质、清理多余节点；
- 用 Tripo 的 convert 任务或 Blender 转 FBX / OBJ / GLB。

### 4. 自动化管线

- 一个菜单命令：输入参考图 / 提示词 → 调 Tripo → 下载 → 导入 → 生成 Prefab；
- 或者做成 Unity 外的本地批处理脚本，跑完再导入。

---

## 三、需要你提供

- **Tripo API Key**
  - 不要直接贴在聊天里；
  - 放环境变量或本地未跟踪文件，例如 `TRIPO_API_KEY`；
  - 不要提交进 Git。
- 想生成的资产清单与风格参考（低模？面数上限？是否要贴图？）；
- 是否接受消耗 Tripo 积分。

---

## 四、两条落地路线

| 路线 | 做法 | 适合 |
|---|---|---|
| **A. 半自动（推荐先走）** | 在 Tripo Studio 里生成并导出 FBX / GLB；我写一个 Unity「模型导入规范化」向导：自动对齐尺寸、轴心、碰撞体、图层、`Footprint`、Prefab | 风险低、风格可控，先把资产管线跑通 |
| **B. 全自动** | 调 Tripo API，在 Unity 里一键生成并导入；GLB 用 glTFast，FBX 用 Unity 原生导入 | 适合批量出草模，但要处理积分、超时、下载链接过期 |

### 推荐

对《屿见》目前这种**低模、方格建筑、URP** 的项目，建议 **先走 A**：

1. Tripo 生成的模型通常偏 PBR 高模感，直接进游戏会与低模海岛风格冲突；
2. 需要统一缩放、材质和面数；
3. 管线稳定后再考虑 B 的全自动批量生成。

---

## 五、与《屿见》的适配点

- 建筑网格单格 **2 世界单位**；模型导入后必须按实际尺寸算 `BuildingData.Footprint`。
- 模型轴心（Pivot）应位于底面中心，避免蓝图吸附后悬空或埋地。
- 建筑实例会被强制放到 `Building` 层；预制体需带 Collider，否则重叠检测查不到。
- URP 材质需要从 Tripo 的 PBR 材质转换，避免粉红/过亮。
- 低模目标参考：移动端道具 ≤ 1.5 万面，主建筑 ≤ 5 万面；具体以项目风格为准。
- 命名、目录、Prefab 结构应与现有 `Assets/Data/Buildings/`、`Assets/Prefabs/` 约定一致。

---

## 六、必须注意的坑

1. **下载链接过期**：Tripo 的模型下载 URL 大约 5 分钟后失效，任务成功后要立刻下载保存；超时也会扣积分。
2. **异步任务**：创建任务只拿到 `task_id`，必须轮询到 `status == success` 再下载，不能只看创建响应。
3. **格式陷阱**：`quad: true` 的输出可能标成 `.glb`，实际内容是 FBX；导入前确认格式。
4. **AI 模型需要后处理**：重拓扑 / UV / 烘焙 / 法线修正通常是必须的，不能直接当成品。
5. **我无法判断美观**：最终是否采用需要你或美术来判断。

---

## 七、下一步待定

需要你拍板：

- **先做 A：Unity 模型导入规范化向导**（推荐）；
- 还是 **先做 B：Tripo API 全自动生成 + 导入**。

如果选 A，请提供 2～3 个 Tripo 导出的 FBX / GLB 样例；
如果选 B，请先配置好 `TRIPO_API_KEY` 环境变量，并确认可接受的积分消耗。

---

## 八、参考资料

- [Tripo 开发者快速开始](https://developers.tripo3d.ai/ko/docs/quick-start)
- [如何使用 3D 生成 API](https://www.tripo3d.ai/zh/blog/how-to-use-a-3d-generation-api)
- [Tripo 官方 Python SDK](https://github.com/VAST-AI-Research/tripo-python-sdk)
- [Unity Asset Store: AI 3D Model Generator](https://assetstore.unity.com/packages/tools/generative-ai/ai-3d-model-generator-330156)
- [Tripo to Unity 6: normals / LOD 实践](https://www.strayspark.studio/blog/tripo-to-unity-6-game-ready-lods-smoothness-normals)
- [Tripo GLB 下载链接过期说明](https://github.com/sceneview/sceneview/pull/2675)
