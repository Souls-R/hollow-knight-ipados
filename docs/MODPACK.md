# 内置 Mod 分支

本分支把用户提供的 Any Radiance 1、Debug 汉化版、QoL、GodSeekerPlus 和现有原生怪物血条一起编译为 iOS ARM64。完整游戏资源打包在 IPA 中，安装后无需另外导入游戏资源。不会附带测试皮肤。

## 使用

使用 iLoader 签名安装完整 IPA。原有存档和设置保存在应用文稿目录；覆盖安装时请保持相同应用标识。

- Any Radiance 1：使用提供版本的原始攻击和战斗逻辑。
- Debug：保留原版面板、快捷键、存档状态和 132 个命令。暂停后点 **Debug**，或按 **L3 / F12**，可通过触摸或手柄选择任意命令。方向键选择，A 执行，B 返回。
- QoL、寻神者+：在游戏的 Mod 设置菜单内配置。保留提供版本中的全部模块，包括默认关闭的可选项。
- 怪物/Boss 血条、伤害数字：暂停菜单中切换，或分别使用 **RB / R3**。

Mod 的设置文件和 Debug 存档状态写入应用文稿目录。外接键盘使用原有快捷键；Debug 也提供触摸命令列表。

## 原生构建方式

这些文件使用 Hollow Knight Modding API，并非 BepInEx。游戏和 Mod 的托管逻辑一起经 IL2CPP 提前编译。运行时不扫描桌面 DLL，也不生成机器码或修改可执行内存。

`On` Hook 使用保留原方法的强类型调用链，支持重复调用 `orig`、延迟协程、嵌套调用和异常恢复。IL 修改在构建时转换为可切换的原生方法变体；运行时的注册、卸载和配置开关选择对应变体。反射字段访问、子对象引用和克隆通过 iOS 支持的反射操作实现。保留 API 原有的主菜单刷新流程，预加载使用完整场景模式。

保留所提供版本的 16 个 QoL 模块、51 个寻神者+模块及 132 个 Debug 命令。数量与编译检查只证明内容保留及可编译，实际游戏效果仍需设备测试。

## 准备本地输入

需要 .NET SDK 10 和匹配 Unity 版本的完整原版游戏。游戏、Mod、依赖库和生成程序集都放在仓库外。工作目录布局：

```text
mod-work/
  api-reference/
    Vanilla/       原版 macOS Managed 文件
    OutputFinal/   对应 Unity 6 的 Modding API 离线处理结果
  inputs/
    AnyRadiance1.dll
    DebugMod.dll
    QoL/QoL.dll
    GodSeekerPlus.dll
  dependencies/
    Satchel.dll
    Osmi.dll
    Vasi.dll
```

已验证的 API 输入来自 [hk-modding/api](https://github.com/hk-modding/api) 的 Unity 6 适配提交 `468f579`；依赖为 Satchel 0.9.4、Osmi 0.2.2、Vasi v2。构建 API 时将 `CopyDir` 设为空，避免覆盖原版游戏。

```sh
python3 build.py modpack-stage --work /path/to/mod-work \
  --dotnet /path/to/dotnet --csc /path/to/sdk/Roslyn/bincore/csc.dll

# 使用独立的、已 prepare 的 iOS 工程，保留旧版本构建。
python3 build.py modpack-install --stage /path/to/mod-work/native-stage \
  --work /path/to/native-modpack
python3 build.py refresh-runtime --work /path/to/native-modpack
python3 build.py native --work /path/to/native-modpack
python3 build.py bundle-ipa --work /path/to/native-modpack \
  --output /path/to/HollowKnight-modpack-unsigned.ipa
```

更新 Mod 输入后使用新的 Mod 工作目录。转换后的代码与元数据必须成对重新编译；不可只替换旧 IPA 的元数据。现有游戏资源目录直接复用，不重复转换或解压。

仓库只提供自有适配和构建代码，不分发上述 DLL、原游戏资源或第三方反编译源码。独立获取的组件仍适用各自许可。
