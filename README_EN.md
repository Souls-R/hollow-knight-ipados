# Hollow Knight iPadOS

[简体中文](README.md) · English

Unofficial native iPadOS build tools for the original **Hollow Knight**.

Starting with your own purchased Steam macOS game, the tools convert managed assemblies into C++ with Unity IL2CPP and compile ARM64 code for Unity's iOS runtime and Metal renderer. An IPA and a separate resource folder make program updates easier.

This repository contains compatibility code and build tools only. It does not include game assemblies, game assets, Unity binaries, or signed applications. Supply your own game and toolchain.

## Quick start

Build on a Mac with Python 3.12, Unity tools, and Xcode.

```text
python3 build.py resources      Convert original resources
python3 build.py prepare        Generate the iOS project and external resource folder
python3 build.py native         Compile the unsigned program IPA
python3 build.py resource-zip   Archive resources for Files import
```

## Features

- iOS gamepad, external keyboard, and mouse input adapters.
- DualSense / DualShock touchpad-button support.
- Independent PNG skin packs and in-game switching; see [skin support](docs/SKINS.md).
- Controller vibration bridge with original game settings.
- Original display-menu resolution/frame-rate bridge and iPad aspect adaptation.
- General Animator binding repair.
- Shared cinematic-video path adaptation using original external MP4 files.
- Startup notice, icon and app names; startup UI supports Simplified Chinese, Traditional Chinese and English.
- Optional diagnostic logs, Metal performance HUD and startup details.

Use an external keyboard or controller for gameplay.

## Notice

**This project is free. Please purchase and support the original game.** Do not sell or pay for IPA packages, paid groups, or testing access. Game content belongs to its respective owners. This project is not affiliated with Team Cherry.

Repository compatibility code is available under [MIT](LICENSE). See [third-party notices](THIRD_PARTY_NOTICES.md). That license does not cover the original game or separately obtained toolchain.

## Dependencies

| Component | Supported/tested input |
| --- | --- |
| Hollow Knight | Purchased Steam macOS version 1.5.12620 |
| Unity iOS Build Support | 6000.0.61f1, matching the game's engine |
| Unity host tools | Tested with 6000.0.50f1 Roslyn, .NET and Mono BCL, supplied separately |
| Unity Input System | Official 1.14.2 package, iOS layout source read at build time |
| Python | 3.12; dependencies in requirements.txt |
| Ruby xcodeproj | 1.27.0 |
| Xcode | Tested with 26.6 and iOS SDK 26.5 |

The game and engine versions must match.

## Build your IPA

Run these commands in a Mac terminal.

### Obtain the game

Sign into Steam with an account that owns the game:

```sh
open 'steam://open/console'
```

In the Steam console, download the macOS depot:

```text
download_depot 367520 367522
```

This downloads the depot's current contents. The builder requires the version above; if the depot has changed, select a matching manifest available to your account. Keep the complete original `hollow_knight.app`. Conversion writes separate outputs.

### Install tools

Install the required Unity tools and 6000.0.61f1 iOS Build Support through Unity Hub. Open Xcode once to finish component setup. In this repository:

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

### Convert resources and prepare

Continue in the same terminal:

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

The iOS support directory must contain `Trampoline/`, `il2cpp/`, `Variations/` and `Data/`. Host tools must contain `NetCoreRuntime/`, `DotNetSdkRoslyn/` and `MonoBleedingEdge/`. Adjust paths when using extracted installer packages.

Resource conversion resumes completed files when rerun. `prepare` requires fresh native and resource-pack output directories. The default version is **0.2.4 / build 8**; change it with `--version` and `--build-number`.

### Compile and install

```sh
python3 build.py native --work "$nativeOutput" --jobs 4
```

Output: `$nativeOutput/output/HollowKnight-0.2.4-split-unsigned.ipa`. Logs: `$nativeOutput/logs/`. Subsequent builds reuse compilation caches.

Sign and install using your own sideloading tool.

### Import resources

`prepare` creates `$packOutput/HollowKnightResources-v1`, approximately 6.59 GiB unpacked. Copy that folder directly or create a ZIP of approximately 1.46 GiB:

```sh
python3 build.py resource-zip --resources "$packOutput" --output "$buildRoot/HollowKnightResources-v1.zip"
```

1. Install and launch the app once.
2. Import the ZIP or folder into the app's Documents area. You can tap the ZIP in iPad Files to extract it.
3. Place `HollowKnightResources-v1` directly in On My iPad → Hollow Knight, without double nesting.
4. Close the app from the app switcher and reopen it.

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

Startup validates the manifest and file sizes, then builds program/resource routes. Compatible program updates reuse the imported resources.

### Diagnostics

In iPadOS Settings → Apps → Hollow Knight, configure the notice, diagnostic logs, Metal HUD and startup details. Fully close and reopen the app to apply changes. Diagnostics and the HUD are off by default.

For feedback, enable diagnostics, restart, reproduce, and export `native.log` and `native-launch.log` from Documents. The next launch rewrites them.

## Saves and updates

The original game's saves are directly in Documents: `user1.dat` through `user4.dat`, with shared settings in `shared.dat`. Fully close the app before backing up or replacing them.

Update in place with the same signing account and bundle identifier to retain saves and resources. Back up first before changing either. **Do not uninstall before updating: uninstalling deletes the app's data.**

## Tested scope

| Item | Scope |
| --- | --- |
| Baseline | 0.2.3 / build 7 experimental version |
| Game | Steam macOS 1.5.12620 / Unity 6000.0.61f1 |
| Deployment target | iOS / iPadOS 16.0 |
| Device | 11-inch iPad Pro (2021, M1), iPadOS 26.6.2 |
| Tested | Startup, menus, new game, opening videos, initial area, native aspect and frame-rate switching |
| Controller | Xbox, with Menu pause and View inventory |

## Changes

### 0.2.4

- Add independent Custom Knight PNG packs and a pause-menu skin selector with gamepad, keyboard and touch controls.

### 0.2.3

- Port the DS touchpad-button fix from Silksong, restoring input to the original game bindings.

### 0.2.2

- Fix opening-video file access with external resources.
- Add optional video preparation/frame and settings load/apply diagnostics.

### 0.2.0

- Add iPad aspect adaptation, original display-menu bridge and Xbox device-style selection.
- Add original-game startup artwork, icons, localized names and system diagnostic settings.

### 0.1.1

- Separate program IPA and external resources for compatible program updates.

## Feedback

Use the issue template with device, OS, app version, reproduction steps and relevant logs. Do not upload purchased game content, account details or signing material.
