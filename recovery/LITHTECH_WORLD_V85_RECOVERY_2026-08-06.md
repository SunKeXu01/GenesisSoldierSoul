# LithTech world v85 资源边界恢复

日期：2026-08-06

## 结论

`RF164.REZ` 和 `RF266.REZ` 的数据区已不再归类为未知块。两个数据区各自由一个完整的 LithTech Jupiter world v85 组成；严格解析器分别在 17,537,044 和 41,241,093 字节处结束，与 REZ `root_offset` 精确相等，两包保留尾字节都为 0。

合计恢复 2 个 world、58,778,137 个源字节，并保留原始字节不做转换性改写。

| 来源 | 字节 | RenderBlock | 子 WorldModel | 材质区段 | 顶点 | 三角形 | 尾字节 |
|---|---:|---:|---:|---:|---:|---:|---:|
| `RF164.REZ` | 17,537,044 | 123 | 48 | 1,182 | 125,310 | 59,896 | 0 |
| `RF266.REZ` | 41,241,093 | 398 | 117 | 2,870 | 322,125 | 165,317 | 0 |
| 合计 | 58,778,137 | 521 | 165 | 4,052 | 447,435 | 225,213 | 0 |

## 边界证明

解析始终从 REZ v1 固定数据区起点 `168` 开始，不搜索后续魔数。边界来自 world 自身的可验证结构：

1. 版本必须为 85，object、blind object、light grid、collision、particle blocker 和 render 六个偏移必须单调且全部在 REZ 数据区内；
2. 从 render 偏移严格读取递归 `CD3D_RenderWorld`，所有 RenderBlock、纹理名、材质区段、顶点、三角形、sky portal、occluder 和 light group 都必须满足有界计数；
3. 材质区段的三角形总数必须等于 RenderBlock 三角形数，每个顶点索引必须落在当前顶点数内，子块索引必须落在当前 RenderBlock 数内；
4. 递归 render world 结束后继续解析客户端 light group 及样本网格，最终读取位置必须是资源结束。

字段语义交叉验证于 CrossFire 公开源码的 [`ReadWorldHeader`](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/world/src/world_shared_bsp.cpp)、[`CWorldClientBSP::LoadRenderData`](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/client/src/world_client_bsp.cpp)、[`CD3D_RenderWorld::Load`](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/render_a/src/sys/d3d/d3d_renderworld.cpp) 和 [`CD3D_RenderBlock::Load`](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/render_a/src/sys/d3d/d3d_renderblock.cpp)。未编译或运行该源码及游戏客户端。

## 产物与验证

- 工具：`tools/recover_private_rez_framed_prefix.py`，tool version 7。
- 回归：`tools/test_recover_private_rez_framed_prefix.py`，15/15；覆盖完整 world、非法子块索引、连续恢复和未知尾段保留。
- 机器清单：`recovery/private-rez-framed-prefix-recovery.json`（Git 忽略，可再生成）。
- 隔离输出：`recovery/special-formats/private-rez-png-prefix/RF164__a2d4cf8b147b/lithtech_world-00000.dat` 和 `RF266__0ea0339fe541/lithtech_world-00000.dat`（Git 忽略）。
- SHA-256：`RF164` 输出 `fad42d24075bac77209837189e7da72149d06b6d5950c8669f73871aae87ffa1`；`RF266` 输出 `146636012e0b20178ea6eb2836bd7b3fb339959634cf470d3b0ac437bb879ac6`。
- RB001 连续前缀中的 `NANO_RoofGhost.DAT` 又以相同解析器恢复 12,589,936 字节、59 个 RenderBlock、4 个子 WorldModel、939 个材质区段、88,343 个顶点和 40,285 个三角形，并同时通过同源 loose 字节相等验证。
- 全量 Python 回归 91/91；快速交付门禁通过。统一 provenance 复核 13/13 清单、184,185 个输出、25,480,110,671 字节，错误 0。

REZ 已无从数据区起点完全未知的包；剩余范围为 RF019/RF199 的 4 个和 RB001 的 1 个已精确记录停止位置的未知后缀。
