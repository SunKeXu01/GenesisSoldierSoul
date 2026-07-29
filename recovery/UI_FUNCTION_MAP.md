# 原界面功能映射

该表只登记已在缓存中找到“原界面类 + 原配置数据”的入口。没有找到完整资源的入口不伪造实现。

| 大厅入口 | 原始 SWF / 导出类 | 原始数据 | 当前结论 |
|---|---|---|---|
| 任务 | `taskUI` / `TaskShootingView`、`TaskItem` | `tasks.xml`、`pvrtask.xml`、`pvrdailytask.xml` | 可恢复 |
| 活动 | `actUI` / `ActivityView`、`ActivityItem` | `act.xml`、`activeLevel`、`activeadd` | 可恢复 |
| 靶场 | `ArsenalUI` / `ArsenalView`、`ArsenalPokedexView` | `rangeRules.xml`、`reward.xml` | 可恢复 |
| 军团 | `teamUI` / 17 个军团界面类 | `team`、`teamLevel`、`teamGift` | 可恢复 |
| 排名 | `mainUI` / `RankLeftPanel`、`RankLeftPanelListItem` | `ladderLevel`、`ladderRules`、`ladderSeason` | 可恢复 |
| 个人 | `PlayerInfoUI` / `PlayerInfoView` | `level`、`title`、`achi` | 可恢复 |
| 仓库 | `storeUI` / `WarehouseView2` | `props.xml` 中 498 把战斗武器及其参数 | 可恢复 |
| 商城 | `storeUI` / `ShopNewView` | `props.xml`、`hotProps`、`limitProps` | 可恢复（暂不接收费） |
| 自由模式 | `mainUI` / `FreeChannelItem`，`hallUI` / 房间与建房界面 | `createRoom.xml`、`quickSearch`、`roomConfig.xml` | 可恢复 |
| 天梯 | `mainUI` / `LadderTip`，`BattleResultUI` / 天梯结算 | 全套 `ladder*` 配置 | 可恢复 |
| 生化 | `evilUI` / 生化频道和房间，`local` / 生化 HUD | `roomConfig.xml` 生化地图、`survive_reward` | 可恢复 |
| 邮件 | 尚未定位到完整邮件面板导出类 | 未确认 | 暂不冒充可用 |

当前旧 `Scene1` 里的“任务、活动、靶场、军团、邮件、排名、个人、天梯”等按钮大多只绑定了点击音效。新入口将按本表连接原资源，不再沿用这些空绑定。
