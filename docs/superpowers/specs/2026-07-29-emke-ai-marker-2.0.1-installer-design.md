# EMKE AI Marker 2.0.1 Windows 安装器设计

## 目标

修复 EMKE AI Marker v2.0.0 在普通 WPF 启动时因只读
`ProgressPercent` 属性被默认 TwoWay 绑定而崩溃的问题，并交付版本为 2.0.1 的
Windows x64 便携 ZIP 与当前用户 Inno Setup 安装器。

本次交付不移动或复用既有 `v2.0.0` 标签，不创建或发布新 GitHub Release，也不进行
代码签名。最终产物仍属于未签名内部预览。

## 已确认的产品范围

- 版本提升为 `2.0.1`，程序集版本与文件版本为 `2.0.1.0`。
- 继续支持 `.jpg`、`.jpeg`、`.png` 与 `.mp4`，不改变媒体处理合同。
- 保留现有自包含 Windows x64 便携 ZIP。
- 新增 Inno Setup 单文件安装器，安装范围固定为当前用户。
- 安装器不请求管理员权限。
- 默认创建开始菜单快捷方式。
- 桌面快捷方式作为未默认勾选的可选任务。
- 提供标准卸载项。
- 不注册文件关联，不添加开机启动，不增加网络或遥测行为。
- ZIP 与安装器使用同一个经过发布校验的 stage 作为唯一 payload 真源。

## 启动缺陷修复

`MainWindow.xaml` 中 `ProgressBar.Value` 当前使用未声明模式的
`{Binding ProgressPercent}`。WPF `RangeBase.ValueProperty` 默认
`BindsTwoWayByDefault`，而 `MainWindowViewModel.ProgressPercent` 只有 getter，
因此窗口首次布局时抛出 `InvalidOperationException`。

最小生产修复是在该绑定上显式声明 `Mode=OneWay`。不为
`ProgressPercent` 增加无意义的 setter，也不改变进度计算方式。

## UI 启动自检

原有 `--self-test --report <absolute-path>` 保留为 headless 自检，继续检查：

- 应用版本；
- .NET 运行时主版本；
- ExifTool manifest 与版本；
- 品牌资源。

新增独立的 UI 启动自检入口。该入口使用正常应用组合根创建真实
`MainWindow`、设置真实 `MainWindowViewModel`、显示窗口并让 WPF 至少完成一次
布局与数据绑定。窗口成功进入已呈现状态后，自检原子写入报告并以退出码 0 关闭；
在窗口构造、资源解析、绑定、首次布局或报告写入期间发生异常时，写入脱敏失败报告并
返回非零退出码。

UI 自检不选择媒体、不执行媒体写入、不打开文件选择器，也不生成 CSV。它只验证应用
能够从交付 payload 完成真实 WPF 启动。原有 headless 自检与新增 UI 自检必须都通过。

## 版本一致性

`Directory.Build.props` 是版本真源，更新为：

```xml
<Version>2.0.1</Version>
<AssemblyVersion>2.0.1.0</AssemblyVersion>
<FileVersion>2.0.1.0</FileVersion>
```

发布清单、自检报告、stage 校验器、ZIP 根目录、ZIP 文件名、安装器文件名、测试、
README、BUILDING、用户说明、Windows 验收文档与 GitHub Actions 发布路径同步更新为
2.0.1。既有历史说明中明确指向已发布 v2.0.0 的事实保持历史语义，不把旧发布记录
改写为新发布已完成。

## 安装器设计

安装器使用锁定的 Inno Setup 6.7.3 `ISCC.exe` 构建。仓库发布脚本不会下载或安装
Inno Setup，而是接受编译器路径并验证文件版本精确为 6.7.3。

安装器核心设置：

```text
AppName=EMKE AI Marker
AppVersion=2.0.1
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\EMKE AI Marker
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
```

安装器使用稳定且后续版本继续复用的 `AppId`，以支持覆盖升级和统一卸载项。安装内容
递归复制自 `build/stage/emke-ai-marker-v2.0.1-windows-x64`，不从 publish、
源码目录或本机运行时目录直接取文件。

安装器生成：

```text
dist/emke-ai-marker-v2.0.1-windows-x64-setup.exe
```

便携包生成：

```text
dist/emke-ai-marker-v2.0.1-windows-x64.zip
```

`dist/SHA256SUMS.txt` 使用 UTF-8 无 BOM 与 LF，按文件名稳定排序，包含 ZIP 与 Setup
两行小写 SHA-256。只有两个产物均通过最终验证后才原子替换该文件。

## 构建数据流

发布流程按以下固定顺序执行：

1. 使用 .NET SDK 10.0.100 执行 locked restore。
2. 获取并验证 ExifTool 13.59。
3. 设置 `EMKE_EXIFTOOL`，运行完整 Release solution tests。
4. 执行 self-contained `win-x64` publish。
5. 通过现有白名单、许可证、隐私、绝对路径与重解析点检查组装 stage。
6. 在 stage 中执行 headless 自检。
7. 在 stage 中执行真实 WPF UI 启动自检。
8. 从已验证 stage 生成确定性便携 ZIP。
9. 使用精确 Inno Setup 6.7.3 从同一 stage 生成 Setup。
10. 将 Setup 静默安装到临时当前用户目录，禁用快捷方式创建。
11. 检查安装文件集合、版本、ExifTool，并运行已安装应用的 headless 与 UI 自检。
12. 静默卸载，确认临时安装目录和测试快捷方式没有残留。
13. 再次校验 ZIP 与 Setup，原子写入双产物 `SHA256SUMS.txt`。

## 安装与卸载验收

安装验收使用 Inno Setup 的静默参数，并通过 `/DIR=` 指向构建拥有的临时目录。
验收不写入系统级目录，不创建文件关联或开机启动项。测试安装必须验证：

- 安装器退出码为 0；
- 安装后的相对路径集合满足发布清单；
- 应用文件版本为 `2.0.1.0`；
- ExifTool 输出精确为 `13.59`；
- headless 自检报告精确通过；
- UI 启动自检报告精确通过；
- 卸载器存在且能够静默执行；
- 卸载后临时安装目录不存在；
- 构建拥有的测试快捷方式不存在；
- 不存在测试遗留进程。

普通交互安装保留开始菜单快捷方式，并提供可选桌面快捷方式。构建验收通过
`/NOICONS` 避免污染实际用户桌面和开始菜单。

## 失败与清理语义

任何 restore、测试、publish、stage 校验、自检、Inno 编译、静默安装、已安装应用
自检、静默卸载或最终校验失败，都视为发布失败。

发布失败时：

- 删除本次构建拥有的候选 Setup、候选校验文件和临时安装目录；
- 不删除仓库外目录或非本次构建拥有的文件；
- 不留下新的可交付 ZIP、Setup 或部分 `SHA256SUMS.txt`；
- 保留脱敏的控制台错误作为诊断证据；
- 不覆盖更早的公开 Release、标签或用户安装。

所有删除继续使用精确父目录、精确名称、普通目录和重解析点防护。

## 自动化测试

实现使用红—绿测试驱动流程。

首先增加会在现有 v2.0.0 代码上失败的回归测试：

- `ProgressPercent` 的 `ProgressBar.Value` 绑定必须显式为 OneWay；
- UI 自检必须实际显示主窗口并成功写入报告；
- UI 绑定或首次布局异常必须令 UI 自检失败；
- 版本真源及生产发布位置必须一致为 2.0.1；
- 安装脚本必须固定当前用户模式、安装目录、快捷方式和禁止项；
- Setup 必须只消费已验证 stage；
- `SHA256SUMS.txt` 必须包含且只包含 ZIP 与 Setup；
- 安装与卸载必须通过临时当前用户目录验收；
- Setup 和 ZIP 均不得包含媒体、CSV、日志、缓存、私人路径、链接或意外文件。

最小生产修复通过后，运行：

```powershell
dotnet restore Emke.AiMarker.sln --locked-mode
pwsh scripts\fetch-exiftool.ps1
$env:EMKE_EXIFTOOL = (Resolve-Path .\runtime\exiftool\exiftool.exe)
dotnet test Emke.AiMarker.sln -c Release --no-restore
pwsh scripts\build-release.ps1 `
  -InnoCompiler "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
```

最后从 Setup 进行一次全新临时安装、应用双自检和卸载验收，并重新核对产物 SHA-256。

## 可证明边界

通过上述门禁可以证明：

- 对应源码在 Windows x64 与锁定工具链上构建；
- ZIP 和 Setup 来自同一已验证 payload；
- 发布应用能够完成真实 WPF 主窗口启动；
- 当前用户安装、运行与卸载路径可用；
- ExifTool 与版本合同一致；
- 产物哈希与包内容经过验证。

这不证明：

- 应用或安装器已经代码签名；
- SmartScreen 不会警告；
- Windows 11 上 100%、150%、200% DPI、拖放、长路径和所有人工流程已验收；
- 平台上传后 XMP 一定保留；
- 标记结果构成法律意见或平台最终审核保证。
