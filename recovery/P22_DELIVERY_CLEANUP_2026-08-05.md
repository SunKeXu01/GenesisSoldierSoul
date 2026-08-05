# P22 交付清理与复现记录

日期：2026-08-05

## 安全清理

仅处理已确认由本机 Unity/Python/命令行生成、且未被恢复报告引用的临时产物：

- 仓库根目录早期日志：`audit-third-person.log`、`build-webgl.log`、`compile-third-person.log`。
- Unity 崩溃诊断：三份 `client-restored/mono_crash.*.json`。
- Python 字节码缓存：`tools/__pycache__/`。
- 统一交付门禁的忽略目录 `artifacts/`；机器可读摘要、主哈希和 77 文件清单已进入 `recovery/`。

上述文件未永久删除，统一移动至可恢复目录：
`/Users/Admin/.Trash/GenesisSoldierSoul-P22-cleanup-l4FN4e/`。

未删除任何来源不明的 APK、EXE/DLL、Unity 数据、REZ、Flash、Unreal 容器、压缩包、
原始模型/贴图/音频，也未批量删除 `recovery/` 验收日志。对 `recovery/*.log` 的 SHA-256
重复扫描没有发现可直接去重的同内容组，因此保留全部可追溯证据。

`.gitignore` 已新增根命令日志、Unity `mono_crash`、Python 缓存、`.venv` 和
`artifacts/` 规则，避免这些本机产物再次混入交付状态。

## 复现门禁

新增 `tools/verify_delivery.sh`，固定并检查 Node、pnpm、Python 和 Unity 版本。最终以
`--full` 从统一入口端到端执行通过：

- TypeScript 构建通过；
- 服务端 30/30；
- Python 工具 38/38；
- Unity EditMode 65/65；
- Unity PlayMode 6/6；
- Unity 综合门禁 9/9；
- 诊断 WebGL 构建成功，77 文件、360,379,681 字节。

机器可读摘要为 `delivery-verification-2026-08-05.json`，主文件哈希为
`delivery-webgl-build-2026-08-05.sha256`，完整清单为
`delivery-webgl-complete-tree-2026-08-05.sha256`。
