# RF199 Web/媒体连续前缀恢复

日期：2026-08-06

## 结论

`CF2.0/CrossFire/rez/RF199.REZ` 原停止位置 249,932,842 之后又严格恢复 29 个连续资源、3,511,193 字节：3 个 CWS、1 个 FLV、1 个 HTML 和 24 个 PNG。新的停止位置为 253,444,035，紧接一段不完整的 JavaScript/高位字节混合数据，因此不再向后搜索。

| 类型 | 数量 | 源字节 | 边界证据 |
|---|---:|---:|---|
| CWS | 3 | 2,002,406 | SWF 声明解压长度、Zlib EOF、完整标签流和最终 End 标签全部一致 |
| FLV | 1 | 1,060,140 | 122 个标签逐项验证大小和 PreviousTagSize；5 秒元数据与 4.958 秒末时间戳闭合；当前边界直接接 CWS |
| HTML | 1 | 2,945 | ASCII doctype、head/body 顺序和唯一 `</html>` 显式结束 |
| PNG | 24 | 445,702 | 每个 chunk 长度、CRC、IHDR/IDAT/IEND 结构完整 |
| 合计 | 29 | 3,511,193 | 从原停止位置连续覆盖，无间隙 |

三份 CWS 分别为：

- v8：33,528 压缩字节，声明 116,949 解压字节，2 帧、206 个标签；SHA-256 `a1632a449efbe4a99c9520fa49c6a4f3dfe71f3475aa066bb66e1a3587a2e484`。
- v17：924,905 压缩字节，声明 930,675 解压字节，218 帧、392 个标签；SHA-256 `0f8aa73753ea8d9b91856b2e101dada6d81c0d83f1482aaea7f6c45572eb82c1`。
- v17：1,043,973 压缩字节，声明 1,148,167 解压字节，31 帧、126 个标签；SHA-256 `6e3f7dc9b1b61d39c2567838bae61fe2326013e5044ac9c9ddbfa48031525506`。

## 安全边界

- CWS 解压设置声明长度上限；解压输出多一个字节、Zlib 未到 EOF、标签越界、End 标签带载荷或 End 后仍有字节均拒绝。
- FLV 只接受 v1、合法音视频标志、零首个 PreviousTagSize、类型 8/9/18、零 StreamID、非回退媒体时间戳和逐项 PreviousTagSize。边界只在当前位点直接出现已支持后继时成立，不搜索后继。
- HTML 必须从当前位点以 HTML doctype 开始，head/body 标签有序且唯一结束标签闭合。
- 原 REZ 只读；不执行 SWF ActionScript、HTML/JavaScript 或视频内容。

## 产物与验证

- 工具：`tools/recover_private_rez_framed_prefix.py`，tool version 11。
- 专项回归：22/22，新增覆盖 FWS/CWS、声明长度不一致、FLV 元数据闭合、PreviousTagSize 错误和 HTML 显式结束。
- 新未知后缀：437,781,181 字节，SHA-256 `03f33da9269c629bdc0b75f996e956b90af8f74d0ec9ac9b9d8b0589196777a4`。
- 统一 provenance：13/13 清单、184,214 个输出、25,483,621,864 字节、错误 0。

5 个精确停止后缀的合计保留量由 465,956,986 降为 462,445,793 字节。
