# 构建与开发

常规构建步骤见 [README](../README.md)。所有命令从仓库目录执行，输出放到独立构建根目录。

## 目录

```text
build.py                  统一命令行入口
scripts/                  工程生成、补丁、资源转换和打包
src/runtime/              自有 C# / Objective-C++ 兼容层
src/launch/strings.json   启动页与设置本地化文本
tests/                    小型资源夹具和原生路径层测试
docs/                     构建说明
```

## 程序与资源边界

`resources` 保留序列化对象 ID、引用及非纹理数据，将不适合 iOS 的桌面纹理转为 ASTC 或 RGBA。
转换结果可继续运行；没有改变输入的文件复用上次结果。

`prepare` 从转换结果直接准备 `HollowKnightResources-v1`。`StreamingAssets/` 映射为 iOS 的 `Data/Raw/`。
程序内只保留启动文件、脚本注册和与当前代码匹配的 IL2CPP 元数据。其余游戏数据进入外置包。
兼容标识由游戏/引擎版本、转换策略与文件清单生成。

原生启动器在 Unity 初始化前检查清单和文件大小，创建 Application Support 下的软链接覆盖目录。
游戏资源路由到 Documents，代码元数据路由到当前签名后的应用。
更新后会刷新程序路径，原始资源文件无需复制。

过场视频直接使用解析后的资源文件路径。

## 原生层快速迭代

只改 `.mm` / `.h` 或打包设置时：

```sh
python3 build.py relink --work "$nativeOutput" --version 0.2.5 --build-number 10
```

这会同步原生源文件、调用 Xcode 并重打 IPA，复用原有游戏代码库。
修改启动页的素材提取逻辑后，需要重新生成对应素材。

## 修改托管兼容代码或 C++ 补丁

```sh
python3 build.py refresh-runtime --work "$nativeOutput" --input-system "$inputSystem" --jobs 4
python3 build.py native --work "$nativeOutput" --jobs 4
```

刷新会保留上一次 C++ 生成目录作为本地备份，重编译兼容程序集，重新生成代码和配套元数据，再由 `native` 编译。
刷新后需要运行 `native`，使代码库和元数据保持匹配；完成前 `relink` 不可用。
游戏 IL 输入保持原样，版本相关的兼容修改集中在生成 C++ 的补丁脚本中。

`build.json` 保存本机输入路径，仅位于构建输出目录，不应提交或作为公开日志直接上传。
若移动构建目录，更新这些配置或在新路径重新准备。

## 测试

跨平台的资源边界测试：

```sh
python3 -m unittest discover -s tests -p 'test_resource_plan.py'
```

手柄回归测试（需要 C++ 编译器和 .NET SDK 8 或更新版本）：

```sh
python3 -m unittest discover -s tests -p 'test_controller_input.py'
```

在 Mac 上运行 Foundation 路径与导入测试：

```sh
python3 tests/test_split_resources.py
```

覆盖缺失/不匹配/部分资源、有效路由、重复与越界路径、拒绝资源软链接、程序更新后的路径刷新。
测试使用临时资源夹具。

命令行入口检查：

```sh
python3 build.py --help
python3 build.py prepare --help
python3 build.py native --help
python3 build.py resource-zip --help
```

资源转换后会校验对象和纹理，确认引用与非纹理数据保持一致。
