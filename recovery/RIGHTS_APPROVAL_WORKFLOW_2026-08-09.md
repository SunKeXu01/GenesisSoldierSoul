# 正式资源权利审批工作流（2026-08-09）

## 当前结论

7 个运行时资源组的技术依赖闭包均为 0 缺失，但没有任何一组拥有经批准的权利记录，因此正式构建继续 fail-closed。`rights-review-request.json` 是待外部审核材料，不是许可或批准记录。

## 审核材料

`recovery/rights-review-request.json` 为每个运行时资源组保存：

- 完整文件路径、逐文件字节数和 SHA-256；
- 组级资产清单 SHA-256 与总字节数；
- 来源说明、发布安全后备方案和待审状态。

当前 7 组共绑定 2,382 个文件。任何文件内容或成员变化都会改变组级清单哈希，使旧批准记录自动失效。
当前请求包 SHA-256 为 `70d25d0b563afe7d7a3eb7df3c40d8e8bb693707a1c82f44f53fd2e5c79e8c99`；重复生成已验证字节稳定。

## 可接受的批准记录

只有取得真实授权或确认使用具备相应发布权利的替代资产后，才可新增批准记录。记录必须位于仓库内、加入 `resource-closure-policy.json` 的 `approved_rights_records`，并由对应资源组的 `rights_record` 引用。审计器要求：

1. `record_type` 为 `formal_resource_rights_approval`，`disposition` 为 `approved_for_formal_release`；
2. `resource_group` 和 `asset_manifest_sha256` 与当前审计完全一致；
3. `rights_basis` 为所有者授权、开源许可、原创、公有领域或替代资产之一；
4. `scope` 至少包含 `formal_release` 和 `redistribution`；
5. grantor、grantee、许可标识、审批人及带时区的审批时间非空；
6. 至少一份仓库内证据文件存在且 SHA-256 匹配。

仅把路径写进允许列表、创建空 JSON、使用待审请求或引用不存在/哈希不符的证据，都只能得到 D 级。

## 复核命令

```bash
python3 tools/audit_resource_closures.py --check-formal
```

在全部 7 组记录验证通过前，该命令应以状态码 2 拒绝正式构建。记录齐备后还必须重跑锁定的完整交付门禁、七地图同构建浏览器回归及双浏览器联网回归。

当前防绕过与完整工具回归为 Python 125/125；锁定 `verify_delivery.sh --quick` 同时通过服务端 30/30、Unreal UObject 518/518 和 .NET 0 警告/0 错误。

## 不得代填的内容

grantor、授权范围、许可标识、审批人和证据不能由恢复工具推断或生成。维护者需要提供真实授权材料，或明确决定将相应资源组替换为清单中列出的发布安全后备实现。
