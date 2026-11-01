# LockpickStandalone（万能钥匙 BE 版 9）

SPT 4.1 客户端开锁模组：自带工具的点击锁定小游戏 + 完美区 + 连击评级 + 可存档的开锁技能。

## 现状（BE 版 9）

- 件：`LockpickStandalone.dll`（判代差一律看 md5，不看字节数；BE 版 9 尚未出包，字节数以 Release 构建输出为准）
- 玩法：底部扫描轨道上的荧光绿游标往返，鼠标左键点击锁定白色区间；中心金色段算完美锁定。
- 难度：门等级 1~5，由 `keys.json` 的钥匙价分级（231 把），可被 `doors.json` 的 doorOverrides 覆盖。
- 技能：自注入「开锁」技能槽位（SE 在位时让位），等级曲线 **Lv1~70**，每 10 级一档台阶；
  Lv51 起进入精英段（可经 F12 关闭「精英工具免磨损」）。
- 面板：局外技能面板「开锁」行下方显示一排 6 条等级加成图标，悬停出中文说明（可经 F12「面板显示等级加成」关闭）。
- 文案：中英双轨，随游戏界面语言切换（`Interaction\LpText.cs`）。

## 关键文件

| 文件 | 作用 |
|---|---|
| `Interaction\LpText.cs` | 面向玩家的全部文案（中/英）；**新文案一律加在这里**，不要在别处写中文字面量 |
| `LockpickEnv.cs` | 与游戏世界交互；技能宽容度 `Forgiveness()` 与台阶 `Tier()` 在这里 |
| `Data\LockpickLevel.cs` | 等级上限的**唯一来源**（`MaxLevel` / `CapRaised` / `ProgressCap`），全工程只认它 |
| `Game\DeltaLockGame.cs` | 小游戏判定与每帧驱动 |
| `Ui\DeltaLockUIBuilder.cs` | 面板构建与刷新（含代码生成的圆环/刻度/弧线 Sprite） |
| `LpConfig.cs` | F12 配置项；分节 `0.总开关`~`7.兼容` |
| `Patches\SkillLevelCapPatch.cs` | 放开等级天花板：Current 钳位 / SummaryLevel / 两处写档，**只对 Lockpicking 生效**，其余 43 技能原版钳位照旧 |
| `Patches\SkillBonusBuffPatch.cs` | 往 `EFT.Skill` 构造器挂 6 条加成 Buff；走独立 Harmony 实例并钉 `Priority.Last`，与 SkillsExtended 的同名构造器前缀定序取胜 |
| `Patches\SkillBuffTextPatch.cs` | 局外面板加成图标悬停 + 技能说明列表的中文文案；只替换本模组自建的那 6 条，其余 43 技能文案零风险 |
| `Compat\SeCompat.cs` | 与 SkillsExtended 共存：接管门交互注入，并对外暴露 `TakeoverEffective` 闸门 |
| `Patches\` 其余 | 门交互注入、技能疲劳豁免、技能槽位注入、门禁卡门去重 |

> BepInEx 日志保持中文（便于与既有排查记录逐字对账），**不要**把日志也接进 `LpText`。

## 构建

```
"C:\Program Files\dotnet\dotnet.exe" build LockpickStandalone41.csproj -c Release
```

- 目标框架 netstandard2.1；csproj 名是 `LockpickStandalone41.csproj`。
- `[BepInPlugin]` 版本串必须是纯 `x.y.z`（带后缀会被 BepInEx 整个跳过），内测序号只挂显示名
  —— 当前显示名「万能钥匙 BE 版 9」、版本串 `1.0.0`，两者不要混着改。
- 出包脚本见 `_archive\开锁BE7打包_20261004\tools\`（build + 独立 verify 两个脚本，均以非零退出码报错）。

本仓库源码以 **AGPL-3.0-or-later** 发布，素材与文档以 **CC BY-NC-SA 4.0** 发布（详见 LICENSE 与 ASSETS-LICENSE）。

## 开源协议
- 源代码：AGPL-3.0-or-later。衍生作品必须以同一协议发布，并公开完整对应源码。本项目在服务器上运行时，向通过该服务器与之交互的所有用户提供完整对应源码（AGPL §13）。
- 素材与文档（图片/图标/模型/译文/说明）：CC BY-NC-SA 4.0。须署名、禁商用、衍生作品同协议发布。
- 源码获取渠道：见本仓库 Releases 与随包附带的 source/<mod>-src.zip。
