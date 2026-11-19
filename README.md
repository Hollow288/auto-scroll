# 自动滚轮 Auto Scroll

Windows 自动滚动工具。按一个键开始或停止滚动，支持锁定聊天区域后在后台滚动，适合配合窗口录屏展示聊天记录。

使用 C#、Windows Forms 和 Windows 原生 API 编写，无第三方运行时依赖，使用 Windows 自带的 .NET Framework。

## 下载与使用

从 [Releases](https://github.com/Hollow288/auto-scroll/releases/latest) 下载 **AutoScrollWheel.exe**，双击运行，无需安装。

也可以[直接下载最新版 exe](https://github.com/Hollow288/auto-scroll/releases/latest/download/AutoScrollWheel.exe)。ZIP 包包含程序和使用说明。

1. 选择“固定区域（后台）”模式。
2. 将鼠标放到聊天记录等需要滚动的区域，按 **F8**，锁定该区域并开始滚动。
3. 锁定后可以将鼠标移开、切到其他软件。再按 **F8** 停止，**Esc** 也能立即停止。
4. 更换目标时，点击“重新选择区域”，然后将鼠标移到新区域并按启停键。

首次运行默认使用 F8。快捷键、方向和速度都可以修改；如果修改过，以窗口里显示的按键为准。可选按键为 F6–F11、Pause 和 ScrollLock。

“跟随鼠标”模式会模拟普通鼠标滚轮，滚动鼠标所在的位置。

## 窗口与后台滚动

- “收起到托盘”会保留当前滚动状态。点右上角 × 会停止滚动并收起到托盘，快捷键仍可使用。
- 双击托盘图标，或再次打开程序文件，可以找回窗口。完全退出时，右键托盘图标选择“退出”。
- 固定区域可以被其他窗口遮住，但不要最小化目标、调整尺寸或在目标软件里切换聊天。
- 移动整个目标窗口时会跟踪选定位置；目标关闭、隐藏、最小化或改变尺寸时会停止滚动。
- 后台模式向选中的窗口发送滚轮消息，不移动鼠标、不切换前台窗口，也不会自动改用普通滚轮。
- 部分软件会忽略后台滚轮消息；普通权限的工具也可能无法操作管理员权限的软件。正式录屏前，请先验证目标在后台是否仍能滚动。
- 滚动速度为每秒 1–20 格。每格对应一次标准滚轮刻度，实际距离由目标软件决定。

已在当前使用环境中验证企业微信可以后台滚动。录屏软件是否能在切换窗口后持续更新画面，需要另行试录确认。

设置保存在 `%LOCALAPPDATA%\AutoScrollWheel\settings.ini`。重新打开程序时始终为停止状态，目标区域需要重新选定。

## 从源码编译

在 Windows 的 PowerShell 中进入仓库目录，运行：

```powershell
.\build.ps1
```

生成的程序位于 `artifacts\AutoScrollWheel.exe`。脚本直接使用 .NET Framework 附带的 `csc.exe`，不需要安装 Visual Studio 或下载 NuGet 包。

如果当前 PowerShell 执行策略不允许运行脚本，可只对这一次进程运行使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

此命令不会更改系统执行策略。

运行自动回归检查并生成发布包：

```powershell
.\build.ps1 -RunTests -Package
```

后台滚动检查使用临时测试窗口，验证消息只到达指定区域、不移动鼠标、不切换前台窗口，以及目标移动、改变尺寸和关闭时的行为。测试不读取聊天记录、不修改个人设置。

## 目录

```text
src/AutoScrollWheel.cs          程序源码
src/app.manifest               Windows 程序清单与 DPI 设置
tests/BackgroundScrollTests.cs  后台滚动回归检查
docs/USAGE.txt                 随发布包提供的使用说明
build.ps1                      编译、测试、打包脚本
VERSION                        当前版本号
CHANGELOG.md                   版本说明
.github/workflows/             自动检查与发布
```

## 发布版本

Git 仓库保存源码、构建脚本和版本历史；编译后的 exe、ZIP 和 SHA-256 校验文件放在 GitHub Releases，不提交进源码历史。

发布新版本时，先同步修改 `VERSION`、源码中的 `AssemblyVersion` 和 `CHANGELOG.md`，提交后推送对应标签：

```powershell
git push origin main
git tag -a v1.2.0 -m "Release v1.2.0"
git push origin v1.2.0
```

以上为 v1.2.0 示例，后续版本请使用新的版本号。标签应与 `VERSION` 一致。

GitHub Actions 会自动编译、运行回归检查，然后创建 Release，并附上：

- `AutoScrollWheel.exe`
- `AutoScrollWheel-v版本号-win.zip`
- `SHA256SUMS.txt`
