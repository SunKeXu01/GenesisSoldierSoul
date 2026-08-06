# LTB layout-v5 glTF skin 恢复

> 历史阶段报告；当前 layout-v7 已恢复骨骼 TRS 与顶点 morph 动画，见 `LTB_ANIMATION_LAYOUT_V7_RECOVERY_2026-08-06.md`。

日期：2026-08-06

## 结论

layout-v5 已将 321 个 CrossFire 复合 LTB 的可证明骨骼绑定转为标准 glTF skin，共覆盖 1,844 个网格；8,721 个 LTB 仍为全部成功、失败 0。骨骼动画和顶点动画未与 skin 混为一谈，仍明确保留为待恢复范围。

## 绑定规则

- rigid render object：所有顶点以权重 1 绑定到已验证的 bone effector。
- skeletal direct：按 12-byte `BoneSetListItem` 中的顶点范围和 4 个骨骼槽，将顶点流中的 non-indexed blend weights 映射回全局骨骼。
- skeletal matrix palette：解析顶点的 4-byte 骨骼索引，并在启用 reindex 时通过重索引表映射回模型骨骼。
- D3D 只序列化前 1–3 个 blend weight，最后一个严格按 `1 - sum(explicit)` 恢复；所有权重必须有限、非负且总和可归一化，零权重槽的 joint 规范化为 0。
- LTB 存储全局 bind pose 矩阵；glTF 节点使用 `inverse(parentGlobal) * global`，`inverseBindMatrices` 使用每个全局 bind 矩阵的逆矩阵。

这些语义交叉验证于 [CrossFire skeletal renderer](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/render_a/src/sys/d3d/d3dmeshrendobj_skel.cpp)、[CrossFire model loader](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/model/src/model_load.cpp) 和 [LithTech Model Packer](https://github.com/jsj2008/lithtech/blob/0eab18289bed72879eddb648d3311075b108cf46/tools/Model_Packer/lta2ltb_d3d.cpp)。未执行其 EXE 或游戏客户端。

## 全量结果

- LTB：8,721/8,721；失败 0。
- GLB：32,608 meshes、23,507,870 顶点、20,749,724 三角形、950,977,288 字节。
- glTF skin：321 文件，14,896 joints，1,844 个网格带 `JOINTS_0/WEIGHTS_0`。
- 网格类型：1,123 rigid 和 721 skeletal 已绑定；20 vertex-animated 网格不伪装为 skin。
- 统一 provenance：12/12 清单、148,829 个输出、21,475,559,731 字节、错误 0。

## 验证与边界

工具验证骨骼索引范围、BoneSet 顶点/索引覆盖、空骨骼槽零权重、绑定矩阵可逆性、skin/joint/accessor 数量和 GLB JSON/BIN 边界，并在写入后复核大小与 SHA-256。真实复合样本另通过 Khronos glTF Validator 2.0.0-dev.3.10：0 errors、0 warnings，仅 2 条“未被材质引用的 UV” info。当前输出不含 skeletal animation channels 或 vertex morph animation；这两项必须从 LTB 尾段动画表继续解析。

- 转换器：`tools/convert_ltb_models.py`，tool version 6，输出 `private-rez-models/layout-v5/`。
- 回归：`tools/test_convert_ltb_models.py`，覆盖 direct BoneSet、matrix palette/重索引骨骼、inverse bind matrices 与 glTF skin attributes。
- 逐项清单：`recovery/private-rez-model-conversions.json`（可再生成，Git 忽略）。
