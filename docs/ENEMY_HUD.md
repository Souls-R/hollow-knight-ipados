# 怪物血条与伤害数字

原生实现普通怪物头顶血条、屏幕下方 Boss 血条，以及命中后上浮的伤害数字。首次受到有效伤害后显示血条；怪物死亡、离场或切换场景后清理。功能只观察游戏实际扣血，不修改伤害、无敌判定或生命值。

## 开关

暂停游戏后可触摸右侧开关：

| 功能 | 手柄 | 键盘 |
| --- | --- | --- |
| 怪物/Boss 血条 | RB / R1 | F7 |
| 伤害数字 | R3 | F8 |

开关立即生效并保存。与玩家皮肤分开设置。

## 默认美术

构建时可从自己获取的 [Enemy Health Bar with Damage Number](https://www.nexusmods.com/hollowknight/mods/123) ZIP 提取默认图片：

```sh
python3 build.py enemy-hud-assets --mod '/path/to/EnemyHPBar.zip' --work "$nativeOutput"
python3 build.py native --work "$nativeOutput" --jobs 4
```

工具只读解析 DLL 内的图片资源，不运行插件。提取结果只在本地工程的 `NativeEnemyHud` 中，随个人 IPA 打包；不会进入原游戏资源包或源码仓库。未提供美术时使用简单血条和文字数字。

## 自定义

在游戏文稿中创建 `HollowKnightMods/EnemyHud`，可以放入 `bg.png`、`mg.png`、`ol.png`、`bossbg.png`、`bossfg.png`、`bossol.png` 覆盖默认血条。`digits.png` 为按从上到下、从左到右排列的 0–9 数字图集，每格 64 × 64。

同目录可放 `settings.json`，例如：

```json
{
  "bossHPThreshold": 150,
  "enemyScale": 1.0,
  "bossScale": 1.0,
  "damageScale": 1.0,
  "verticalOffset": 1.5
}
```

当前以最大生命值阈值区分普通怪物与 Boss。缩放范围为 0.5–3.0；文件设置和图片在重启后读取。开关在游戏内即时调整。

## English

Native enemy/boss HP bars and floating damage numbers observe actual HP subtraction, including normal hits, spells and extra damage. Pause, then use **RB / R1 / F7** for bars and **R3 / F8** for damage numbers, or touch the switches.

Optional art and settings overrides live in `Documents/HollowKnightMods/EnemyHud/`. Defaults can be extracted locally from your own mod ZIP with `build.py enemy-hud-assets`. The desktop BepInEx DLL is not loaded by the iOS app. Mod artwork retains its original ownership and is not covered by this repository's MIT license.
