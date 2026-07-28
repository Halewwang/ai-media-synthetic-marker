# EMKE AI Marker v2（Windows）

EMKE AI Marker v2 是面向 Windows x64 的本地桌面工具，用于为已经由人工确认
需要披露的 JPG、JPEG、PNG 和 MP4 媒体写入并严格验证：

```text
XMP-dc:Subject
└─ rdf:Bag
   └─ rdf:li = contains-synthetic-performer
```

应用完全在本机离线处理媒体，不上传文件、不连接亚马逊，也不发送遥测数据。
它不会识别或判断媒体是否包含 AI 生成人物；选择哪些媒体使用此标记始终由使用者
决定。

> 当前状态：v2.0.0 是未签名内部预览。Windows 发布流水线已在 `windows-2022`
> 上完成锁定工具链、完整 solution test、stage 自检和便携 ZIP 构建，并通过
> [GitHub Releases](https://github.com/Halewwang/ai-media-synthetic-marker/releases)
> 提供下载。这些证据不等于 Windows 11 x64 真实机器 UI、拖放、SmartScreen 或
> 安装验收；具体边界见 [BUILDING.md](BUILDING.md)。

## 使用方式

应用采用一个窗口完成选择、处理和复查：

1. 点击“添加文件”或“添加文件夹”，也可以把媒体文件或文件夹拖放到窗口。
2. 确认列表中只有已经人工判断需要披露的媒体。
3. 默认点击“开始标记”创建安全副本；也可以先点击“只读验证”。
4. 任务完成后查看逐文件结果、输出位置和 CSV 运行记录。

支持扩展名为 `.jpg`、`.jpeg`、`.png`、`.mp4`，不区分大小写。文件夹会递归扫描
并稳定排序；符号链接、联接点和其他重解析点不会被跟随。

### 默认：安全副本

默认的 `MarkCopies` 模式保留原件，在原输入旁的 `EMKE 已标记` 目录中创建输出。
应用先在受控的 owned temporary file 上复制和处理，再经过严格回读后原子提交。
这个内部 identity-preserving 写入使用 ExifTool
`-overwrite_original_in_place -P`；它是安全副本事务的一部分，不表示整个文件
字节或所有文件系统时间、属性保持不变。

### 高级原件模式

设置中的“高级原件模式”会直接修改原始媒体。每次运行都会再次要求确认，并使用
ExifTool `-overwrite_original -P`；不创建备份或 `*_original` 文件。写入元数据会
改变文件容器和整个文件校验值。只在已经另行备份并理解风险时使用。

### 只读验证

“只读验证”不会调用媒体写入逻辑，也不会修改媒体。它仍会在本机应用数据目录生成
CSV 运行记录；这是该模式唯一允许的输出。CSV 可能包含相对文件名和元数据证据，
分享前必须脱敏。

## 严格验证边界

“验证通过”必须同时满足：

- `XMP-dc:Subject` 中存在区分大小写、完整匹配的
  `contains-synthetic-performer`；
- 原始 XMP 在正式 Dublin Core 和 RDF namespace 下存在
  `dc:subject/rdf:Bag/rdf:li`；
- 对应 `rdf:li` 的值与目标字符串完全一致。

已有其他 Subject 关键词会被保留；重复运行不会重复追加目标值。字段值与原始 XMP
结构不一致时会报告失败，不会猜测或自动修复。`Microsoft:Category` 和 Windows
属性页中的“标记”不能替代上述 XMP 证据，MP4 在资源管理器中显示为空也不能单独
证明失败。

单个文件失败不会中断其他文件。常规运行不会逐文件证明图片像素或视频媒体流哈希
未变；受控集成测试中的 `ImageDataHash` 证据只覆盖仓库内四个受控 fixture。

## Windows x64 开发

固定工具链：

- Windows x64；
- `global.json` 锁定的 .NET SDK 10.0.100；
- PowerShell 7；
- `packaging/exiftool.lock.json` 锁定的 ExifTool 13.59。

在 Windows PowerShell 7 中：

```powershell
dotnet restore Emke.AiMarker.sln --locked-mode
pwsh scripts\fetch-exiftool.ps1
$env:EMKE_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
dotnet test Emke.AiMarker.sln -c Release --no-restore
dotnet run --project src\Emke.AiMarker.App\Emke.AiMarker.App.csproj -c Release --no-restore
```

完整环境准备、跨平台证据边界、发布包构建与标签规则见
[BUILDING.md](BUILDING.md)。v2 的生产真源是 `src/Emke.AiMarker.*` 和
`tools/Emke.AiMarker.Release`；Python/Tkinter v1 仅保存在
`legacy/python/` 作为一个大版本周期的行为参考，不参与 v2 构建或发布包。

## 隐私、风险和范围

- 不得把真实商品媒体、私人媒体、CSV 记录、日志、本地运行时或构建产物提交到仓库。
- 平台上传、转码或发布过程可能移除 XMP；本工具不验证平台处理后的文件。
- 当前 Windows 程序未签名，SmartScreen 可能显示未知发布者并要求额外确认。
- “验证通过”只说明当前文件满足本工具的精确字段与结构检查，不构成法律意见，也
  不保证任何平台最终审核结果。
- 本项目与 Amazon 无隶属、合作、官方认可或保证关系。

原创源码采用 [MIT License](LICENSE)。生产包所含 .NET 10 runtime 与 ExifTool
13.59 适用各自许可，见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

参与开发前请阅读 [CONTRIBUTING.md](CONTRIBUTING.md)。
