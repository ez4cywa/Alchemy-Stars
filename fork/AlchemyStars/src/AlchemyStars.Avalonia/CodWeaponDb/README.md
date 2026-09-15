# CODWeaponDb — 随包数据说明

本目录是 [CODWeaponDB](https://github.com/ez4cywa/Alchemy-Stars) 之外独立项目 **COD Weapon Console Codename Database v0.12.0** 的发布数据快照，供「COD 武器库」页面离线查询。

## 文件

| 文件 | 用途 |
| --- | --- |
| `weapons.jsonl` | 逐行一条的武器记录：游戏、武器名、控制台代号、类别、来源链接。 |
| `blueprints.json` | 按 `游戏:武器名` 归组的蓝图记录：名称、蓝图代号、稀有度、获取方式、Wiki 图片链接、来源链接。 |
| `qa.json` | 上游刷新时的覆盖统计与待复核问题数。 |
| `refresh-manifest.json` | 固定的上游 GitHub commit、时间戳、摘要哈希，用于溯源。 |
| `snapshot-diff.json` | 本次快照相对上一次发布的新增 / 变更 / 移除明细。 |

应用只读取前四个文件；`snapshot-diff.json` 随包保留，仅供人工核对数据变化。选择其它 CODWeaponDB `dist` 目录后，同一套解析逻辑会替换这里的快照（设置 → COD 武器库 → 导入数据目录）。

## 数据来源与许可

- 武器名与控制台代号：`SadSlothXL/COD-Weapon-codenames` 的 GitHub 表格，固定到 `refresh-manifest.json` 中记录的 commit。该第三方仓库**未声明许可证**，数据仅在本地随包提供。
- 蓝图资料与图片链接：Call of Duty Wiki（Fandom），CC BY-SA 3.0。
- 本项目不包含 Wiki 的图片文件本身。武器图标由应用在运行时按需从 `callofduty.fandom.com` 获取，只缓存在本机，不写入任何导出文件。

完整说明见随包 `Docs/COD-WEAPON-DB.zh-CN.md`。
