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
