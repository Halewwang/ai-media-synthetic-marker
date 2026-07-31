# 参与贡献

感谢参与改进 EMKE AI Marker v2。

## 开始之前

- 当前生产目标仅为 Windows x64，工具链由 `global.json` 锁定到 .NET SDK 10.0.100。
- v2 生产真源位于 `src/Emke.AiMarker.*`、`tools/Emke.AiMarker.Release` 和
  `scripts/*.ps1`；`legacy/python/` 仅是 v1 行为参考。
- 不得提交私人媒体、真实商品媒体、CSV 运行记录、日志、`*_original`、本地
  ExifTool、runtime payload、`build/`、`dist/`、缓存或本机绝对路径。
- 不要擅自安装缺失工具、更新锁文件或替换 ExifTool；先说明环境缺口并等待确认。

## 允许的受控媒体

仓库只允许 `tests/fixtures/controlled/` 下已列入 `fixture-manifest.json` 的 JPG、
JPEG、PNG 和 MP4 fixture。它们必须由仓库脚本生成，不含人物、商品、客户数据、
外部素材或私人元数据。

只有确需再生时才使用：

- 精确 FFmpeg 7.1.1；
- `packaging/exiftool.lock.json` 锁定的精确 ExifTool 13.59。

```powershell
dotnet restore Emke.AiMarker.sln --locked-mode
pwsh scripts\fetch-exiftool.ps1
$env:EMKE_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
pwsh tools\generate-controlled-fixtures.ps1
```

生成器会拒绝其他版本。提交前必须复核 manifest 中的生成命令、字节长度和 SHA-256，
并确认没有放宽 `.gitignore` 为任意媒体。

## 本地检查

在 Windows x64 PowerShell 7 中：

```powershell
dotnet restore Emke.AiMarker.sln --locked-mode
pwsh scripts\fetch-exiftool.ps1
$env:EMKE_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
dotnet test Emke.AiMarker.sln -c Release --no-restore
```

涉及构建、发布工具、许可证或工作流时，还应在 Windows 上运行：

```powershell
pwsh scripts\build-release.ps1 `
  -InnoCompiler "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
```

安装器构建使用 `packaging/inno-setup.lock.json` 锁定的官方 Inno Setup 6.7.3。
macOS 交叉编译可作为补充编译证据，但不能替代 Windows UI、stage self-test、
ZIP、Setup 安装/卸载、签名或真实机器验收。

## 提交问题或变更

请提供：

- Windows 版本和 x64 环境；
- EMKE AI Marker、.NET SDK 与 ExifTool 版本；
- 模式（安全副本、只读验证或高级原件）；
- 已脱敏的完整错误与可复现步骤；
- 实际运行过的测试命令及其证据边界。

分享前删除文件名、路径、CSV 和元数据中的敏感信息。不要上传未获授权的媒体。
