# 项目维护指南

本文面向接手 EMKE AI Marker v2 的人类维护者与 AI 编程助手。开始工作前，必须完整
阅读本文、[README.md](README.md) 和 [CONTRIBUTING.md](CONTRIBUTING.md)；
涉及环境、构建、工作流或发布时，还必须阅读 [BUILDING.md](BUILDING.md)。随后只读
检查当前目录、分支、远端、工作树、最近提交及与需求直接相关的源码和测试。

如果需求仍处于交流或方案确认阶段，不要修改文件、安装依赖、构建、提交或发布。
只有范围明确并得到实施授权后，才进行与需求直接相关的改动。仓库脚本不擅自安装
缺失工具；若环境不完整，先报告缺少项并等待确认。

## 产品目标与边界

本项目是仅面向 Windows x64 的本地 .NET 10/WPF 桌面工具，用于在 JPG、JPEG、PNG
和 MP4 中写入并严格验证：

```text
XMP-dc:Subject
└─ rdf:Bag
   └─ rdf:li = contains-synthetic-performer
```

以下边界不可弱化：

- 工具不识别或判断媒体是否包含 AI 生成人物。
- 工具离线处理媒体，不上传、不连接亚马逊，也不发送遥测数据。
- 合规证据只能是精确 `XMP-dc:Subject` 与原始 XMP 中正式 namespace 下的
  `dc:subject/rdf:Bag/rdf:li`。
- `Microsoft:Category` 和 Windows 属性页中的“标记”不能替代上述 XMP 证据。
- “验证通过”只表示当前文件满足本工具字段与结构检查，不构成法律意见或平台最终
  审核保证。
- 不得把构建、单元测试、交叉编译或 CI 证据扩大为 Windows UI、真实包、自检、
  签名、SmartScreen、安装或公开发布证明。

## 生产真源与目录职责

- `src/Emke.AiMarker.Core/`：产品合同、发现、规划、处理和严格验证。
- `src/Emke.AiMarker.Infrastructure/`：ExifTool、文件事务、CSV 与 Windows 边界。
- `src/Emke.AiMarker.App/`：WPF 单窗应用、MVVM、服务和资源。
- `tools/Emke.AiMarker.Release/`：锁定 ExifTool 获取、stage 验证和确定性组包。
- `tests/Emke.AiMarker.*.Tests/`：v2 自动化测试。
- `tests/fixtures/controlled/`：唯一允许提交的受控四格式 fixture。
- `scripts/*.ps1`：v2 获取、测试、publish 与发布编排。
- `packaging/exiftool.lock.json`：ExifTool 13.59 Windows x64 锁定真源。
- `packaging/release-manifest.json`：v2 发布包结构合同。
- `release_template/`：v2 用户说明与空示例输出模板。
- `legacy/python/`：Python/Tkinter v1.0.0 行为参考；不参与 v2 构建、组包或交付。

根目录不得恢复 `src/ai_media_marker.py`、Python 构建脚本、`pyproject.toml`、
Python requirements 或旧启动器。不要直接维护 `build/`、`dist/`、runtime payload、
便携 EXE 或其他生成物；需要发布时从 C# 真源和统一 PowerShell 脚本重新生成。

## 不可破坏的实现规则

- 支持 `.jpg`、`.jpeg`、`.png`、`.mp4`，扩展名不区分大小写。
- 扫描必须递归、稳定排序，并拒绝符号链接、联接点和其他重解析点。
- Subject 使用区分大小写的完整字符串匹配。
- 写入前先读取 Subject；只有缺少目标值时才追加。
- 写入必须保留其他 Subject 关键词，并保持重复运行不会重复追加。
- `MarkCopies` 是默认安全副本模式：事务先创建并持有 owned temporary file，
  只对这个由事务证明所有权的临时副本调用
  `-overwrite_original_in_place -P`，严格验证并封存后才原子提交到最终输出。
  不得把 identity-preserving 写入改回普通替换，也不得在未证明所有权的路径上使用。
- `MarkOriginals` 是高级原件模式：每次运行必须重新确认，直接对源文件使用
  `-overwrite_original -P`，不创建备份。不得把高级选择持久化到下一次运行。
- `-P` 只要求 ExifTool 尽量保留修改时间；不得暗示所有文件系统时间、属性、容器
  字节或文件哈希完全不变。
- 写入后必须重新读取字段与原始 XMP，并确认正式 Dublin Core/RDF namespace 下的
  `rdf:Bag/rdf:li`；字段与结构不一致必须失败，不猜测、不修复。
- `VerifyOnly` 不能调用媒体写入逻辑；生成 CSV 运行记录是其唯一允许输出。
- 单个文件失败不能中断其他文件；安全停止只在当前文件完成后停止接收新文件。
- 常规产品运行不得宣称逐文件验证图片像素或视频媒体流哈希；受控集成测试证据不能
  泛化到私人媒体。

## 隐私与仓库卫生

不得提交真实商品媒体、私人媒体、CSV 运行记录、日志、`*_original`、本地 ExifTool
payload、`build/`、`dist/`、NuGet 输出、Python 缓存、环境变量文件或本机绝对路径。

唯一媒体例外是 `tests/fixtures/controlled/` 中由仓库生成器产生、manifest 锁定且
不含人物、产品、客户数据或外部素材的四个 fixture。错误报告和测试证据必须删除
敏感文件名、路径和元数据；不要上传未获授权的媒体。

发布工具的白名单、reparse guard、文本绝对路径扫描和许可证要求必须继续生效。

## 开发与验证

生产环境锁定 Windows x64、`global.json` 中 .NET SDK 10.0.100、PowerShell 7 与
ExifTool 13.59。正确的完整验证顺序：

```powershell
dotnet restore Emke.AiMarker.sln --locked-mode
pwsh scripts\fetch-exiftool.ps1
$env:EMKE_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
dotnet test Emke.AiMarker.sln -c Release --no-restore
```

涉及发布时再在真实 Windows x64 上运行：

```powershell
pwsh scripts\build-release.ps1 `
  -InnoCompiler "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
```

普通源码改动至少运行相关测试；涉及 ExifTool、事务、Integration、构建、打包、
安装器、运行时定位、许可证或发布卫生时，应使用锁定的 Inno Setup 6.7.3 并运行
相应完整门禁。macOS 上的
`EnableWindowsTargeting` 交叉构建不等于 Windows 运行验收。

## 受控 fixture

不得用私人媒体替代测试数据。只有确需重新生成受控 fixture 时，才在授权环境中使用
精确 FFmpeg 7.1.1 和锁定 ExifTool 13.59：

```powershell
$env:EMKE_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
pwsh tools\generate-controlled-fixtures.ps1
```

必须评审 `fixture-manifest.json`、字节长度、SHA-256 与生成命令；不得放宽 `.gitignore`
为任意媒体。

## 版本与发布

- v2 版本以 `Directory.Build.props` 为真源，并与 release manifest、自检、包名、
  测试和面向用户说明保持一致。
- 已发布标签不可移动、删除或复用；尤其 `v1.0.0` 永远不能用于 v2。
- `workflow_dispatch` 只构建 artifact，不公开发布；只有匹配版本的已存在 `v*` 标签
  才可进入 tag-only publish job。
- 未经明确授权，不提交、不推送、不打标签、不创建 Release、不签名，也不改写公开
  历史。
- 发布前只暂存当前需求文件，并完整检查 staged diff、测试、包内容、许可证、隐私
  与绝对路径。

## 新环境接手清单

1. 确认目录、分支、远端、工作树和最近提交。
2. 阅读本指南、README、贡献指南、BUILDING 及相关源码测试。
3. 确认需求是讨论还是已授权实施。
4. 只有确需运行时才检查锁定 SDK、PowerShell 与 ExifTool；不要擅自安装。
5. 先说明当前状态、改动范围、风险和可证明边界，再开始实施。
