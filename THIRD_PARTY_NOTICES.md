# 来源与第三方组件 / Sources and third-party components

本仓库的输入、动画、震动、纹理转换和启动页适配基于
[Souls-R/silksong-ipados](https://github.com/Souls-R/silksong-ipados) 的 MIT 许可代码，
并针对《Hollow Knight》一代独立适配。原许可及版权声明保留在 [LICENSE](LICENSE)。
参考版本：`e596b1ad6bda14113208faf82aa31c8d560b0a26`。
DS 手柄触摸板适配来自后续版本 `106220a7195c56b7aa388284121f28a3e359cbb7`。

Input, animation, haptics, texture-conversion and launcher compatibility code was
adapted from the MIT-licensed project above. The original notice is retained.

以下组件由使用者独立获取，不随仓库分发：

| 组件 / Component | 用途 / Use |
| --- | --- |
| Hollow Knight / Team Cherry | 用户自购游戏及原始资源 / Purchased game and original assets |
| Unity Editor、IL2CPP、iOS Build Support | 主机编译工具和目标运行时 / Host tools and target runtime |
| Unity Input System 1.14.2 | 官方包中的 iOS 手柄布局源码 / Official iOS layout source |
| Apple Xcode / SDKs | 原生编译、链接 / Native compilation and linking |

Python 依赖由 `requirements.txt` 指定，通过包管理器安装；其许可按各自项目执行。
仓库不包含下载后的第三方包、原游戏程序集、反编译输出、图片、字体、音乐、
视频或原游戏图标。启动图标、美术和字体只在本地构建时从使用者的原游戏提取。

Python dependencies are installed separately. Their respective licenses apply.
Artwork, fonts and icons are extracted locally from the user's purchased game.

## Enemy Health Bar with Damage Number

怪物血条功能参考用户独立获取的 HollowBoys
[Enemy Health Bar with Damage Number](https://www.nexusmods.com/hollowknight/mods/123) 的功能和图片命名。
本仓库独立实现原生生命值观察、界面与开关，不分发或加载该桌面 DLL 及其 BepInEx/Harmony 依赖。
可选默认美术从用户自己的 Mod ZIP 只读提取到本地构建目录；美术属于原权利人，不包含在源码仓库中，也不适用本仓库 MIT 许可。

## Custom Knight

皮肤 PNG 命名、角色/特效与过场替身的定位方式参考
[HollowKnight.CustomKnight](https://github.com/PrashantMohta/HollowKnight.CustomKnight)。
本项目独立实现本地皮肤加载、输入隔离与切换界面，不依赖其 Modding API。
独立获取的皮肤美术由各自作者持有，作者和原始来源保留在皮肤的 `metadata.json`。

Custom Knight reference license:

```text
MIT License

Copyright (c) 2024 CustomKnight

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
