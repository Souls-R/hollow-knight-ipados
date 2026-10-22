# 游戏内皮肤

暂停游戏后，点右上角“皮肤”，或按手柄 **LB / L1**、键盘 **F6** 打开选择器。方向键选择，**A / × / 回车**应用，**B / ○ / Esc**返回。选择“原版”立即还原；选择结果会在下次启动时保留。

## 安装与移除

在“文件”App 中解压皮肤包，把 `HollowKnightSkins` 放在游戏的文稿根目录，与 `HollowKnightResources-v1` 并列：

```text
Documents/
├── HollowKnightResources-v1/
└── HollowKnightSkins/
    ├── grimm-knight/
    │   ├── Knight.png
    │   ├── Sprint.png
    │   └── metadata.json
    └── another-skin/
        └── Knight.png
```

每套皮肤是独立文件夹。可随时添加或删除，在选择器里点“刷新列表”；选中的皮肤被移除后，刷新或重启会回退原版。多个 ZIP 解压后，将各皮肤子文件夹合并到同一个 `HollowKnightSkins` 下。

皮肤不需要重新签名、重建 IPA 或替换大资源包。原游戏文件不会被改写。只加载当前选中的皮肤；切换时释放上一套自定义贴图。文件缺失、解码失败或尺寸不匹配时保留当前皮肤。

旧版 HUD 皮肤可能缺少当前游戏的血量、魂槽或神居指示帧。加载时优先从旧皮肤自己的血量图标重建新版待机帧，保留其形状、颜色和透明度；无法重建的回复过渡帧及缺失的必要指示使用原版。此处理只修改内存中的自定义贴图，不改皮肤 PNG 或游戏资源。

皮肤只有提供 `HUD.png` 才会替换 HUD；只有角色或冲刺图片的皮肤会继续使用原版玩家界面。怪物血条是独立功能，见 [怪物血条](ENEMY_HUD.md)。

## 支持的图片

采用 Custom Knight PNG 命名，文件名不区分大小写。每套至少有 `Knight.png`，其他图片可省略，缺省部分使用原版。支持原尺寸和等比例 2 倍尺寸。

| 图片 | 原尺寸 |
| --- | --- |
| Knight | 4096 × 4096 |
| Sprint、Weaver、Liquid | 512 × 1024 |
| HUD、VS、Wraiths、VoidSpells、Shade、Grimm、Hornet | 2048 × 2048 |
| Unn、Hatchling、Fluke、Shield、Baldur、Cloak、Wings、Webbed、DreamArrival、Dreamnail | 1024 × 1024 |
| Geo、Compass | 256 × 512 |
| OrbFull | 130 × 125 |
| Birthplace、Quirrel | 1024 × 2048 |
| Shriek | 512 × 512 |

`Sprint` 对应原版的冲刺/梦之门图集；`Cloak`、`Wings` 等对应获取能力或过场中的角色替身。它们不会修改游戏动画或动作逻辑。Custom Knight 的 DLL、配置脚本、Swapper、额外护符图标及其他未列图片不在此格式内。

`Liquid` 对应魂槽填充图集。使用 `OrbFull` 时暂时隐藏原版的脉冲叠层，卸下后恢复。

## 整理作者原包

可以直接放 PNG 文件夹，也可以把作者 ZIP 整理为统一的独立皮肤 ZIP：

```sh
python3 build.py skin-zip --source '/path/to/author-skin.zip' --output '/path/to/my-skin.zip' --id 'my-skin' --name 'My Skin' --author 'Artist' --url 'https://example.org/original-skin'
```

打包器检查 PNG 尺寸及解码，统一大小写，拒绝重复图片和越界路径，并在 `metadata.json` 中保留作者、来源及未包含的图片列表。

皮肤可在 [HKSkins](https://hkskins.art/) 查找。请保留作者与原始来源；皮肤美术不适用本仓库的 MIT 许可，也不包含在源码仓库中。

## English

Extract packs into `Documents/HollowKnightSkins/<skin-id>/`, beside the existing game resource folder. Each skin needs `Knight.png`; optional sheets are listed above. Pause, then tap **Skins** or press **LB / L1 / F6**. Use the D-pad and **A / Cross / Enter** to apply; **B / Circle / Esc** closes the selector.

Add or remove individual folders and refresh the list. Removing the selected skin falls back to Original on refresh/relaunch. Packs are independent of the IPA and resource pack. Keep the artists' attribution and original source links.
