# ATF 标准媒体转换

## 结果

- 源 ATF：538；成功 538；失败 0。
- 格式：346 个 legacy `COMPRESSED`，192 个 legacy `COMPRESSED_ALPHA`。
- 标准输出：538 个 DXT1/DXT5 DDS 和 538 个 RGBA PNG，合计 271,008,801 字节。
- mip：源头声明 5,117 层，其中 1,001 层有实际数据并全部写入 DDS；空层不伪造图像。

## 重建方法

该批 version-0 ATF 不包含可直接拷贝的完整 DXT 流。`COMPRESSED` 每层由 8 个记录组成，`COMPRESSED_ALPHA` 每层由 10 个记录组成；桌面端 DXT 的查找表与端点被分别存放在省略长度字段的 LZMA 流和 JPEG-XR 图像中。

`tools/convert_atf_textures.py` 对每个长度、边界、维度、编码前缀、LZMA 输出大小与填充字节做 fail-closed 检查，然后重建 DXT1/DXT5 block，写入 DDS，再由 Pillow 重新打开 DDS 并转为 PNG。源 `.atf` 只读，不删除、不改写。JPEG-XR 解码依赖固定为 `imagecodecs==2026.6.26`。

## 验证

- 合成回归测试：DXT1、DXT5 alpha、声明长度错误和不连续 mip 共 4 项。
- Python 工具链：78/78 通过。
- 独立重新打开 1,076/1,076 个 DDS/PNG，维度、RGBA 模式、字节数和 SHA-256 全部一致。
- 统一 provenance：13/13 清单、149,905 个输出、21,817,520,072 字节、错误 0。

逐文件输入/输出哈希、尺寸、格式和 mip 计数见 `recovery/atf-texture-conversions.json`。大型可再生 DDS/PNG 位于已忽略的 `recovery/special-formats/flash-atf-converted/`，不上传 GitHub。
