# RF199 Web/媒体连续前缀恢复

日期：2026-08-06

## 结论

`CF2.0/CrossFire/rez/RF199.REZ` 原停止位置 249,932,842 之后又严格恢复 1,186 个连续资源、37,597,385 字节：3 个 CWS、1 个 FLV、1 个 HTML、1 个 CP949 JavaScript/CSS bundle、1 个 UI layout、10 个 DTX 和 1,169 个 PNG。新的停止位置为 287,530,227，当前直接出现尚未支持的 `CFSprite` 二进制结构，因此不再向后搜索。

| 类型 | 数量 | 源字节 | 边界证据 |
|---|---:|---:|---|
| CWS | 3 | 2,002,406 | SWF 声明解压长度、Zlib EOF、完整标签流和最终 End 标签全部一致 |
| FLV | 1 | 1,060,140 | 122 个标签逐项验证大小和 PreviousTagSize；5 秒元数据与 4.958 秒末时间戳闭合；当前边界直接接 CWS |
| HTML | 1 | 2,945 | ASCII doctype、head/body 顺序和唯一 `</html>` 显式结束 |
| CP949 Web bundle | 1 | 18,617 | CP949 严格解码，JS/CSS 标记及三类定界符计数闭合，当前位点直接接 PNG |
| UI layout | 1 | 26,697 | 6 个 GROUP/DEFAULTGROUP 成对，94 个组件与 94 个 `-END` 成对，当前位点直接接 PNG |
| DTX | 10 | 220,776 | v−5 头、像素格式、尺寸和 mip 载荷闭合；另生成 10 个 PNG、9,115 字节 |
| PNG | 1,169 | 34,265,804 | 每个 chunk 长度、CRC、IHDR/IDAT/IEND 结构完整 |
| 合计 | 1,186 | 37,597,385 | 从原停止位置连续覆盖，无间隙 |

三份 CWS 分别为：

- v8：33,528 压缩字节，声明 116,949 解压字节，2 帧、206 个标签；SHA-256 `a1632a449efbe4a99c9520fa49c6a4f3dfe71f3475aa066bb66e1a3587a2e484`。
- v17：924,905 压缩字节，声明 930,675 解压字节，218 帧、392 个标签；SHA-256 `0f8aa73753ea8d9b91856b2e101dada6d81c0d83f1482aaea7f6c45572eb82c1`。
- v17：1,043,973 压缩字节，声明 1,148,167 解压字节，31 帧、126 个标签；SHA-256 `6e3f7dc9b1b61d39c2567838bae61fe2326013e5044ac9c9ddbfa48031525506`。

## 安全边界

- CWS 解压设置声明长度上限；解压输出多一个字节、Zlib 未到 EOF、标签越界、End 标签带载荷或 End 后仍有字节均拒绝。
- FLV 只接受 v1、合法音视频标志、零首个 PreviousTagSize、类型 8/9/18、零 StreamID、非回退媒体时间戳和逐项 PreviousTagSize。边界只在当前位点直接出现已支持后继时成立，不搜索后继。
- HTML 必须从当前位点以 HTML doctype 开始，head/body 标签有序且唯一结束标签闭合。
- CP949 bundle 必须严格解码，包含 JavaScript function 与 CSS body，`{}`、`()`、`[]` 数量闭合并以顶层 `}` 结束；只接受当前位置直接出现的二进制后继。
- UI layout 必须逐行满足 GROUP、DEFAULTGROUP、组件、属性与 `-END` 文法，组名和组件结束数量完全一致。
- 原 REZ 只读；不执行 SWF ActionScript、HTML/JavaScript 或视频内容。

## 产物与验证

- 工具：`tools/recover_private_rez_framed_prefix.py`，tool version 13。
- 专项回归：26/26，覆盖 FWS/CWS、FLV、HTML、CP949 bundle、UI layout 及其错误边界。
- 新未知后缀：403,694,989 字节，SHA-256 `0b76355ae67eb245367a565ef69f03afccd252dabde0f6c6f1ab9769281ae619`。
- 统一 provenance：13/13 清单、185,381 个输出、25,517,717,171 字节、错误 0。

5 个精确停止后缀的合计保留量由 465,956,986 降为 428,359,601 字节。
