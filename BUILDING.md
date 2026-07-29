# Windows x64 构建与发布清单

本文只描述 EMKE AI Marker v2 的 .NET 10 Windows 生产路径。Python/Tkinter v1
位于 `legacy/python/`，仅作为行为参考，不参与 v2 构建、组包或发布。

## 1. 固定环境

必须使用：

- Windows x64；
- `global.json` 指定的 .NET SDK 10.0.100；
- PowerShell 7（`pwsh`）；
- Git；
- `packaging/exiftool.lock.json` 锁定的 Windows x64 ExifTool 13.59；
- `packaging/inno-setup.lock.json` 锁定的 Inno Setup 6.7.3。

仓库脚本不会擅自安装 .NET SDK、PowerShell 或其他缺失工具。若环境不完整，应停止
并说明缺少项，得到维护者确认后再安装。日常构建不需要 Visual Studio、Node.js、
Java 或其他运行时。

检查环境：

```powershell
dotnet --version
pwsh --version
git status --short --branch
```

`dotnet --version` 必须是 `10.0.100`。`global.json` 是 SDK 真源，不要通过
roll-forward 绕过锁定版本。

## 2. 锁定还原

在仓库根目录先执行：

```powershell
dotnet restore Emke.AiMarker.sln --locked-mode
```

所有 NuGet 项目都提交 `packages.lock.json`。不要删除锁文件、改用非 locked
restore，或在未评审的情况下更新依赖。

## 3. 获取并验证 ExifTool 13.59

锁定还原成功后执行：

```powershell
pwsh scripts\fetch-exiftool.ps1
```

发布工具会从锁定的 HTTPS 地址取得官方 Windows x64 压缩包，并验证精确字节长度、
SHA-256、ZIP 路径、必要 payload、逐文件 manifest 和实际版本。不要手工替换
`runtime\exiftool`，也不要绕过 `packaging\exiftool.lock.json`。

然后把已验证的可执行文件传给完整测试：

```powershell
$env:EMKE_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
& $env:EMKE_EXIFTOOL -ver
```

版本必须精确输出 `13.59`。Task 10 的集成测试会在未设置该变量时主动失败，不会
静默跳过。

## 4. 完整测试

必须在 fetch 之后、同一个 PowerShell 会话中运行：

```powershell
dotnet test Emke.AiMarker.sln -c Release --no-restore
```

该命令覆盖 Core、Infrastructure、App、Release 和真实 ExifTool Integration
项目。不要先运行一个缺少 `EMKE_EXIFTOOL` 的 solution test，也不要把 Integration
从 solution 中排除。

如只诊断一个项目，可以运行对应 `dotnet test <project> -c Release --no-restore`，
但发布前仍需回到完整 solution test。

## 5. 启动和构建证据

源码启动：

```powershell
dotnet run --project src\Emke.AiMarker.App\Emke.AiMarker.App.csproj -c Release --no-restore
```

仅编译：

```powershell
dotnet build Emke.AiMarker.sln -c Release --no-restore
```

`dotnet build` 证明源码可编译；`dotnet test` 证明相应自动化测试通过。它们都不单独
证明真实 Windows UI、拖放、DPI/高对比度、SmartScreen、签名或安装/发布接受度。

在非 Windows 主机加 `-p:EnableWindowsTargeting=true` 可以交叉编译 WPF 项目，
但不能运行 Windows UI 或 Windows 专用行为。交叉 publish 也不等于 Windows
暂存包自检通过。

## 6. 构建便携 ZIP 与按用户安装包

在满足前述条件的 Windows x64 PowerShell 7 环境中执行：

```powershell
pwsh scripts\build-release.ps1 `
  -InnoCompiler "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
```

`ISCC.exe` 必须来自官方 Inno Setup 6.7.3 安装程序；下载文件的长度、SHA-256
和 Authenticode 发布者必须与 `packaging/inno-setup.lock.json` 一致。Inno Setup
只用于维护者构建，最终用户不需要安装它。

脚本顺序固定为：

1. locked restore；
2. 获取并验证 ExifTool 13.59；
3. 设置 `EMKE_EXIFTOOL` 并运行完整 Release solution test；
4. self-contained `win-x64` publish；
5. 以白名单组装 stage；
6. 在 Windows 上执行 stage 内 headless self-test 和真实窗口 UI self-test；
7. 再次验证 stage，并从同一个 stage 生成确定性 ZIP；
8. 编译按当前用户安装的 Setup，静默安装到临时目录并逐文件比对 stage；
9. 对已安装程序执行 headless 与 UI self-test，再静默卸载；
10. 为 ZIP 和 Setup 重新生成一个双行 `SHA256SUMS.txt`。

成功输出应为：

```text
dist/
├─ emke-ai-marker-v2.0.1-windows-x64.zip
├─ emke-ai-marker-v2.0.1-windows-x64-setup.exe
└─ SHA256SUMS.txt
```

构建脚本会拒绝媒体、CSV、Python 源码、缓存、日志、`*_original`、链接/重解析点、
意外文档和发布文本中的本机绝对路径。`build/`、`dist/` 与本地 ExifTool runtime
都不得提交。

macOS 上可以验证 release tool 单元测试、部分交叉编译和交叉 publish；由于无法执行
Windows stage、安装和 UI self-test，不能据此声称 ZIP 或 Setup 已完成 Windows
真实机器验收。

## 7. 签名与 Windows 验收

当前 v2 为未签名内部预览。构建成功不等于：

- 二进制已代码签名；
- SmartScreen 不会警告；
- Windows 11 x64 真实机器 UI、拖放、长路径、中文路径和文件属性已接受；
- ZIP 已在干净 Windows 环境解压并完成端到端试用；
- Setup 的交互式安装、开始菜单、可选桌面快捷方式和控制面板卸载已人工接受；
- 已创建公开 GitHub Release。

这些是后续 Windows acceptance 与发布门禁，必须分别留下真实设备证据。

## 8. CI 与标签发布

CI 使用 `windows-2022`、锁定 SDK、locked restore，并在设置
`EMKE_EXIFTOOL` 后运行完整 solution test。所有 GitHub Actions 必须固定到完整
commit SHA。

`Build Windows release` 的 `workflow_dispatch` 只构建并上传工作流 artifact，
不会公开发布。只有已存在且与 `Directory.Build.props` 中版本精确匹配的 `v*` 标签
才进入 publish job，并使用 `gh release create --verify-tag`。

`v1.0.0` 是不可变的历史标签，绝不能移动、删除、复用或作为 v2 发布标签。
未经明确授权，不推送、不打标签、不创建 Release。

## 9. 发布前卫生检查

```powershell
git diff --check
git status --short
git diff --cached --stat
git diff --cached
```

只暂存当前任务范围。确认暂存区没有私人媒体、CSV、运行日志、runtime payload、
构建输出、环境变量文件、本机绝对路径或未授权生成物。
