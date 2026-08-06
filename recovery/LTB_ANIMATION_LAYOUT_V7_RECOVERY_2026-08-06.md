# LTB layout-v7 glTF 动画恢复

日期：2026-08-06

## 结论

layout-v7 已完整解析 321 个 CrossFire 复合 LTB 的骨骼后续区并严格到达 EOF，失败 0。98 个源文件实际含动画，共恢复 818 个 clip、29,212 个源关键帧、88,740 个 glTF TRS/weights 通道和 2,940 个 position morph targets；其余 223 个复合 LTB 的源动画数本来就是 0，不伪报为缺失。

8,721 个 LTB 仍为 8,721/8,721 转换成功，321 个文件的 glTF skin 和 1,844 个蒙皮网格继续保留。输出共含 32,608 个网格、23,507,864 顶点、20,749,724 三角形，GLB 总计 1,021,928,828 字节。

## 尾段格式与转换规则

- 严格解析 343 个 animation weight sets、544 个 child-model 记录、586 个 socket 和 818 条 animation binding，并验证数量、骨骼索引、有限浮点、字符串边界与 self binding 名称顺序。
- 818 个动画使用 compression type 0（331 个）或 type 2（487 个）；type 0 读取 float position/quaternion 或逐帧 vertex list，type 2 按 `int16 / 16` 位置和 `int16 / 32767` 四元数还原。
- LTB 位置/四元数直接输出为 glTF 节点局部 translation/rotation；`MNODE_ROTATIONONLY` 节点保留 bind offset，root animation binding translation 叠加到根节点。
- 6 个重复毫秒时间戳为满足 glTF 严格递增输入而保留同一时刻的最后源关键帧，原 string key 以 animation extras 留证。
- 20 个 vertex-animation 网格节点按 unduplicated vertex list 与 `DupMap` 扩展为完整位置帧，并输出 glTF position morph target；线性 one-hot weights 精确表达相邻源帧插值。
- 261 个异常/零法线经三角形几何恢复，其中 143 个退化邻域使用相邻法线或确定性 `(0,0,1)` 后备；752 个未引用异常顶点被有界移除。原 LTB 未改写。

这些语义交叉验证于 [CrossFire model loader](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/model/src/model_load.cpp)、[CrossFire vertex-animation renderer](https://github.com/liquiddeath13/crossfire_base/blob/fbc4fc238dbfd3b76ad417d45a3d62e072f9d0e4/runtime/render_a/src/sys/d3d/d3dmeshrendobj_vertanim.cpp) 和 [LithTech animation serializer](https://github.com/jsj2008/lithtech/blob/0eab18289bed72879eddb648d3311075b108cf46/tools/shared/model/model_save.cpp)。未执行来源程序或游戏客户端。

## 验证

- 当前 Python 工具链：86/86。
- 内建 GLB 门禁：8,721/8,721，输入/输出大小与 SHA-256 回读一致，失败 0。
- Khronos glTF Validator `2.0.0-dev.3.10`：321/321 个复合 GLB，0 errors、0 warnings；仅 1,864 条无材质 UV 与 15 条源退化三角形 info。
- 当前统一 provenance：13/13 清单、183,691 个输出、25,392,811,873 字节、错误 0。

## 边界

glTF 已含骨骼 TRS 动画、vertex morph 动画和 string-key extras；socket、animation weight-set 混合语义与外部 child-model 运行时绑定仍只做严格审计，未伪装成标准 glTF 行为。REZ 剩余范围已缩小为 3 个完全未知包和 4 个有精确停止证据的未知后缀。

- 转换器：`tools/convert_ltb_models.py`，tool version 8，输出 `private-rez-models/layout-v7/`。
- 回归：`tools/test_convert_ltb_models.py`，覆盖 animation tail、root binding、压缩通道、TRS、morph target/weights、skin 和法线规范化。
- 逐项清单：`recovery/private-rez-model-conversions.json`（可再生成，Git 忽略）。
