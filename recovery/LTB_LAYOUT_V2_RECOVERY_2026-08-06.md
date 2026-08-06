# LTB layout-v2 几何与骨骼元数据恢复

日期：2026-08-06

## 结论

8,721 个 LTB model 候选的严格 GLB 转换成功数从 8,067 提升至 8,552，失败从 654 降至 169。本轮解决 485 项：332 项属于新版布局与旧 GLB 的安全路径冲突，153 项属于 CrossFire 顶层 mesh + 多 submesh 复合布局。

## 输出迁移

- 新输出固定写入 `private-rez-models/layout-v2/`，不覆盖旧目录。
- 8,067 个内容完全相同的 GLB 通过硬链接复用，避免重复占用约 800 MB。
- 332 个与旧版不同的严格解析结果写入 layout-v2 独立文件，旧 GLB 原样保留。
- 所有 layout-v2 输出均重新验证文件大小、SHA-256 和 GLB 2.0 JSON/BIN 边界。

## 复合布局

公开的 CrossFire LTB 查看器源码显示，部分 LTB 的每个顶层 mesh 包含 submesh 表、材质段、可变权重字段、顶点/索引段和骨骼段。工具只在旧单 submesh 布局严格失败后启用该回退，并验证：

- 顶层 mesh/submesh 数量和所有字段边界；
- 类型对应的 32/36/40/44 字节顶点步长；
- 有限位置、法线、UV 和全部三角形索引范围；
- 权重表、尾段长度、骨骼名称、16-float 绑定矩阵；
- 父子计数总和及每个非根骨骼的父节点可解析性。

结果：153 个复合布局文件、924 个几何 submesh、750,782 个顶点、598,527 个三角形；153/153 均含严格骨骼元数据，共 7,169 条骨骼记录，单文件最多 77 条。骨骼元数据写入机器清单，但尚未生成 glTF skin；动画仍保持未完成状态。

结构规则交叉参考 [Cote-Duke LTB2X](https://www.spawnsite.net/forum/viewtopic.php?t=546) 及 [NewLTBViewerTool 的 CrossFire loader](https://github.com/giaynhap/NewLTBViewerTool/blob/master/NewSALL/LtbLoader.cpp)。未执行其中的 EXE 或客户端程序。

## 当前总计

- LTB 候选：8,721。
- 严格 GLB：8,552；失败：169。
- GLB 几何：31,667 meshes、22,773,183 顶点、19,988,052 三角形。
- GLB 输出：880,281,636 字节。
- 布局：旧单 submesh 8,399；CrossFire 复合布局 153。
- 统一 provenance：12/12 清单、148,660 个输出、21,404,864,079 字节、错误 0。

## 剩余 169 项

- 118：复合布局顶点出现非有限浮点；可能是尚未支持的压缩顶点编码，当前拒绝猜测。
- 43：复合布局三角形索引越界；可能有不同索引宽度/分段规则，当前拒绝输出错误几何。
- 4：复合 mesh type 6/20 未获得可靠步长定义。
- 2：骨骼名称越界，说明前序段仍有未识别字段。
- 1：复合 submesh 数量不可信。
- 1：顶层 mesh 数量不可信。

## 实现与证据

- 转换器：`tools/convert_ltb_models.py`，tool version 3。
- 测试：`tools/test_convert_ltb_models.py`，覆盖 layout-v2 旧文件保留与复合 mesh/骨骼元数据。
- 逐项清单：`recovery/private-rez-model-conversions.json`（可再生成，Git 忽略）。
- 统一账本：`recovery/special-format-conversion-ledger.json`。

下一步应先识别 118 项的顶点编码和 43 项的索引规则，再实现 glTF joints/weights/skins 和动画通道；不能把当前骨骼元数据审计误报为完整蒙皮动画转换。
