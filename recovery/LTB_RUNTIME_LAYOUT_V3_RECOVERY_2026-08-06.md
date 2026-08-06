# LTB runtime layout-v3 全量几何恢复

> 历史阶段报告；当前 layout-v5 已恢复 joints/weights/glTF skin，见 `LTB_SKIN_LAYOUT_V5_RECOVERY_2026-08-06.md`。

日期：2026-08-06

## 结论

8,721 个 LTB model 候选已全部通过严格解析、GLB 结构验证和 SHA-256 读回；成功数从 8,552 提升到 8,721，失败从 169 降为 0。本轮修正了对 CrossFire LithTech 运行时 render object 字段的旧命名/含义误读，没有猜测索引宽度或生造法线。

## 可证明的布局规则

- render object type 4/5/6/7 分别是 rigid、skeletal、vertex animated 和 null。
- skeletal 对象支持 direct bone sets 与 matrix palette，后者可含重索引骨骼表和 indexed blend 顶点流。
- vertex animated 对象含未去重顶点数、动画节点、骨骼 effector 和顶点重复映射。
- 文件头命令串后是 global radius、OBB 计数与 68-byte OBB 记录，之后才是 ModelPiece 计数。
- 每个 render object 都验证声明尺寸与实际消耗字节完全相等；顶点位置/UV、索引、骨骼范围和层级均做有界验证。

规则交叉参考 [CrossFire render object 定义](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/shared/src/renderobject.h)、[CrossFire skeletal renderer](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/render_a/src/sys/d3d/d3dmeshrendobj_skel.cpp) 与 [LithTech Model Packer](https://github.com/jsj2008/lithtech/blob/0eab18289bed72879eddb648d3311075b108cf46/tools/Model_Packer/lta2ltb_d3d.cpp)。只阅读公开源码，未执行其 EXE 或游戏客户端。

## 有限值处理

原数据中 746 个非有限法线顶点全部没有被三角形索引引用，工具只删除这些不可见顶点并稳定重映射索引。若非有限法线被引用，必须能从相邻非退化三角面推导出有限单位法线，否则失败；实际全量中需推导的被引用顶点为 0。

## 全量结果

- LTB：8,721/8,721；失败 0。
- GLB：32,608 meshes、23,507,870 顶点、20,749,724 三角形、909,248,808 字节。
- 布局：8,400 个 legacy single-submesh；321 个 CrossFire composite。
- 复合骨骼元数据：321 文件、14,896 条骨骼；matrix palette submesh 79，vertex animation submesh 20，OBB 7。
- 统一 provenance：12/12 清单、148,829 个输出、21,433,831,251 字节、错误 0。

## 范围边界

GLB 只声明已验证的几何、法线和 UV；骨骼层级/绑定矩阵仅保存在机器清单中。joints/weights、glTF skin 和动画通道仍未转换，必须继续保留原 LTB。

- 转换器：`tools/convert_ltb_models.py`，tool version 4，输出 `private-rez-models/layout-v3/`。
- 回归：`tools/test_convert_ltb_models.py`，覆盖 rigid、matrix palette/重索引骨骼、vertex animation、OBB、有界法线推导和旧输出保留。
- 逐项机器清单：`recovery/private-rez-model-conversions.json`（可再生成，Git 忽略）。
