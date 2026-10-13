# 构建与验证工作流

适用于 `codex/avalonia-aot`。从 preview.35 起，原生产物目录包含完整版本号，例如 `output/avalonia-aot-1.3.0-preview.35`。

## 先跑快速工程回归

在仓库根目录运行，无需 .NET、Rust、Maya、Blender 或游戏素材：

```powershell
./scripts/test-build-configuration.ps1
```

覆盖版本路径映射、无效或条件版本拒绝、Rust/restore/publish 失败传播、前置失败时保留旧构建，以及环境变量和当前目录的恢复。测试使用并清理独立临时目录。

## 构建和验证原生程序

需要 Windows x64、`global.json` 指定的 .NET SDK、Rust MSVC 工具链、Visual Studio C++ 构建工具及完整 Git 子模块。脚本优先使用仓库 `output/dotnet-sdk/dotnet.exe`，否则使用 PATH 中的 `dotnet`。

```powershell
git submodule update --init --recursive
./scripts/verify-avalonia-aot.ps1
```

脚本从仓库根执行 Rust 测试和构建、NuGet restore、Native AOT publish，然后运行服务、导出、更新器、窗口、无障碍和渲染检查。缺少真实 Hawk 素材时会明确标记跳过，不能据此声称真实素材验收通过。

`-BuildOnly` 保留 Rust 测试、restore 和 publish，跳过应用检查，不能替代完整验收。Rust 或 restore 失败时保留上一份 publish；开始 publish 后会清理同版本旧目录，publish 失败的目录不能用于打包。所有退出路径恢复四项环境变量及调用者目录。

## 找到当前产物

```powershell
. ./scripts/avalonia-build-configuration.ps1
$build = Get-AvaloniaBuildConfiguration
$exe = Join-Path $build.PublishDirectory 'AlchemyStars.Avalonia.exe'
./scripts/test-avalonia-aot-startup.ps1
& $exe
```

启动检查仍支持显式 `-PublishDirectory`。只在 `fork/AlchemyStars/BuildVersion.props` 更新 `Version`：Avalonia 和 Engine 导入该文件，Engine 公开常量由构建生成，界面、更新判断和脚本使用同一版本。WPF 与固定版本 RedFox 的配置独立。

## 本地打包

新增对应 `docs/releases/<版本>.zh-CN.md`，再运行：

```powershell
./scripts/build-avalonia-aot-release.ps1
```

缺少版本日志或程序实际版本与配置不同，打包在替换产物前失败。`-SkipVerification` 仅用于已通过完整检查的当前版本产物。此命令生成本地目录和 ZIP，上传 GitHub 由发布流程另行执行。

可用已编译的 `$exe` 额外检查打包会拒绝错配版本并保留旧目录：

```powershell
./scripts/test-build-configuration.ps1 -PublishedExecutable $exe
```

## 导出保护验证

完整验证的 `--self-test` 包含自动推断输出目录时的跨任务输入保护，以及 FBX 转换器部分写入后失败时的旧文件保留、新文件与临时文件清理。成功转换后才替换最终 FBX。这些测试使用生成的 CAST 和可控转换器故障，验证文件保护，不能代替 Blender/Maya 格式与动画验收。
