# Hollow Knight iPadOS

简体中文 · [English](README_EN.md)

《Hollow Knight》（空洞骑士一代）非官方 iPadOS 原生构建工具。

从你自己购买的 Steam macOS 版游戏开始，将托管程序集通过 Unity IL2CPP 转换为 C++，编译为 ARM64，并使用 Unity 的 iOS 原生运行时和 Metal 渲染。采用 IPA + 外置资源包，方便覆盖更新和迭代。

仓库仅包含适配代码和构建工具，不包含游戏程序集、资源、Unity 运行时或已签名程序。请自行准备正版游戏和工具链。

## 开始构建

需要 Mac、Python 3.12、Unity 工具和 Xcode。

```text
python3 build.py resources      转换游戏资源
python3 build.py prepare        生成 iOS 工程和分体资源目录
python3 build.py native         编译未签名 IPA
python3 build.py resource-zip   打包资源 ZIP
```

## 功能

- iOS 手柄、外接键盘和鼠标输入适配。
- DualSense / DualShock 触摸板按键适配。
- 手柄震动输出桥接，保留原游戏的震动设置。
- 原版设置菜单的分辨率和帧率桥接、iPad 画幅适配。
- 通用 Animator 绑定修复。
- 通用过场视频读取适配，直接使用外置资源中的原始 MP4。
- 启动声明、图标和应用名称；启动页支持简中、繁中、英文。
- 可选诊断日志、Metal 性能面板和启动详情。

游戏内请使用外接键盘或手柄。

## 声明

**本项目完全免费，请支持正版并购买原游戏。**

请勿倒卖或购买 IPA 包、付费群或测试资格。Hollow Knight、角色、美术、音乐和游戏数据属于各自权利人；本项目与 Team Cherry 无隶属关系。

本仓库的自有适配代码采用 [MIT](LICENSE)，来源说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。该许可不覆盖原游戏和独立获取的工具链。

## 依赖与来源

| 项目 | 版本与获取方式 |
| --- | --- |
| Hollow Knight | 自购 Steam macOS 版，测试版本 1.5.12620 |
| Unity iOS Build Support | 必须匹配游戏：6000.0.61f1 |
| Unity 主机工具 | 已验证 6000.0.50f1 的 Roslyn、.NET、Mono BCL；路径单独指定 |
| Unity Input System | 1.14.2 官方包，构建时读取其 iOS 布局源码 |
| Python | 3.12，依赖见 requirements.txt |
| Ruby xcodeproj | 1.27.0 |
| Xcode | 已验证 Xcode 26.6；iOS SDK 26.5 |

游戏/引擎版本必须匹配。

## 构建自己的 IPA

以下命令在 Mac 终端运行。

### 获取原版游戏

在 Steam 登录已购买游戏的账号，打开控制台：

```sh
open 'steam://open/console'
```

在 Steam 控制台下载 macOS depot：

```text
download_depot 367520 367522
```

此命令获取 depot 当前内容；工具要求上表所列版本，若 depot 已更新，请使用与你拥有的匹配版本对应的 manifest。保留完整的 `hollow_knight.app`，不要覆盖原始游戏。资源转换会写入独立输出目录。

### 准备工具

通过 Unity Hub 安装所需工具和 6000.0.61f1 的 iOS Build Support。安装并打开 Xcode 完成组件安装。

在仓库目录执行：

```sh
python3.12 -m venv .venv
source .venv/bin/activate
python3 -m pip install -r requirements.txt
gem install xcodeproj -v 1.27.0 --no-document --user-install

buildRoot="$HOME/HollowKnightBuild"
mkdir -p "$buildRoot/input-system"
curl -fL 'https://download.packages.unity.com/com.unity.inputsystem/-/com.unity.inputsystem-1.14.2.tgz' -o "$buildRoot/input-system.tgz"
tar -xzf "$buildRoot/input-system.tgz" -C "$buildRoot/input-system"
```

### 转换资源并生成工程

继续在同一终端执行：

```sh
gameData="$HOME/Library/Application Support/Steam/steamapps/content/app_367520/depot_367522/hollow_knight.app/Contents/Resources/Data"
hostTools='/Applications/Unity/Hub/Editor/6000.0.50f1/Unity.app/Contents'
iosSupport='/Applications/Unity/Hub/Editor/6000.0.61f1/Unity.app/Contents/PlaybackEngines/iOSSupport'
inputSystem="$buildRoot/input-system/package"
resourceOutput="$buildRoot/resources"
packOutput="$buildRoot/resource-pack"
nativeOutput="$buildRoot/native"

python3 build.py resources --game-data "$gameData" --output "$resourceOutput" --jobs 2
python3 build.py prepare --game-data "$gameData" --host-tools "$hostTools" --ios-support "$iosSupport" --input-system "$inputSystem" --resources "$resourceOutput" --pack-output "$packOutput" --output "$nativeOutput" --bundle-id 'org.example.hollowknight.personal'
```

`iosSupport` 应包含 `Trampoline/`、`il2cpp/`、`Variations/` 和 `Data/`；`hostTools` 应包含 `NetCoreRuntime/`、`DotNetSdkRoslyn/` 和 `MonoBleedingEdge/`。如由安装包提取工具链，请把变量指向对应目录。

资源转换支持继续运行同一命令，复用已完成的文件。`prepare` 要求尚不存在的原生工程和资源包输出目录；重新生成时选新目录。默认版本为 **0.2.5 / build 9**，可用 `--version`、`--build-number` 修改。

### 编译与签名安装

```sh
python3 build.py native --work "$nativeOutput" --jobs 4
```

IPA 位于 `$nativeOutput/output/HollowKnight-0.2.5-split-unsigned.ipa`，构建日志在 `$nativeOutput/logs/`。再次执行会复用编译缓存。

使用自己的侧载工具签名安装。

### 导入资源

`prepare` 生成 `$packOutput/HollowKnightResources-v1`，展开约 6.59 GiB。可直接复制文件夹，也可以打包为约 1.46 GiB 的 ZIP：

```sh
python3 build.py resource-zip --resources "$packOutput" --output "$buildRoot/HollowKnightResources-v1.zip"
```

1. 安装 IPA 并打开一次应用。
2. 将 ZIP 或已解压的文件夹导入应用 Documents；ZIP 可在 iPad「文件」中点按解压。
3. 将资源文件夹放到「我的 iPad → 空洞骑士」，不要套两层同名目录。
4. 从后台彻底关闭并重新打开应用。

```text
Documents/
  HollowKnightResources-v1/
    pack.json
    resource-index.json
    Data/
      globalgamemanagers
      level0
      Raw/Intro.mp4
      ...
```

启动前校验资源清单和文件大小，并建立程序/资源加载路径。资源兼容时，更新 IPA 无需重新导入资源。

### 性能与诊断

在 iPadOS「设置 → App → 空洞骑士」中调整声明、诊断日志、Metal 性能面板和启动详情；从后台关闭并重开应用后生效。日志与性能面板默认关闭。

需要反馈时开启诊断、重启并复现，再导出 Documents 中的 `native.log` 和 `native-launch.log`。下次启动会重写日志。

## 存档与程序更新

一代存档直接位于应用 Documents：`user1.dat` 到 `user4.dat` 为存档栏，`shared.dat` 为共享设置。备份或替换前彻底关闭游戏。

使用相同签名账号和应用标识覆盖安装，保留原有资源和存档。更换账号或标识前先备份。**不要为更新先卸载应用，卸载会删除应用数据。**

## 支持与测试范围

| 项目 | 当前范围 |
| --- | --- |
| 基线版本 | 0.2.5 / build 9 测试版 |
| 原游戏 | Steam macOS 1.5.12620 / Unity 6000.0.61f1 |
| 最低部署目标 | iOS / iPadOS 15.0 |
| iOS 15 实测 | 已通过用户实机测试 |
| 实测设备 | iPad Pro 11 英寸（2021，M1），iPadOS 26.6.2 |
| 已实测 | 启动、主菜单、新游戏、开场视频、初始关卡、原生画幅、帧率切换 |
| 手柄 | Xbox，支持 Menu 暂停和 View 背包 |

## 更新说明

### 0.2.5

- 同步丝之歌 0.8.3 的手柄身份识别修复，统一 Switch、通用 iOS 手柄与 DualSense 的控件和原游戏绑定。
- 首次创建 PS5 配置时继承已有 PS4 改键；已有 PS5 配置保持不变。

### 0.2.4

- 最低部署目标降至 iOS / iPadOS 15.0，已通过用户实机测试。

### 0.2.3

- 同步丝之歌的 DS 手柄触摸板按键修复，补回原游戏绑定所需的输入。

### 0.2.2

- 修复分体资源模式下开场视频的本地文件读取。
- 补充视频准备、出帧与设置加载/应用的可选诊断。

### 0.2.0

- 加入 iPad 画幅、原版显示菜单桥接和 Xbox 风格选择。
- 同步原版素材启动页、应用图标、名称和系统诊断选项。

### 0.1.1

- 支持程序 IPA 与外置资源包分离，资源可跨兼容程序更新复用。

## 反馈

提供设备、系统、应用版本、复现步骤和必要日志。使用仓库的问题反馈模板，不上传正版游戏内容、账号或签名资料。
