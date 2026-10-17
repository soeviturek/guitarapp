# Guitar Practice

Windows 原生吉他练习应用，使用 .NET 8、WPF、WASAPI 和 NAudio 构建。当前版本只面向 Windows，不包含 macOS 构建或兼容层。

## 已实现

- 同时导入原曲、人声、鼓、贝斯等多条音轨，并从同一时间轴同步播放
- 每条音轨独立音量和静音，总输出也可单独调节
- 点击或拖动时间轴、播放位置滑杆进行定位
- 使用 `[`、`]` 设置循环起止点，或按住 `Shift` 在时间轴拖选循环区间
- 与音频时间轴同步的 40–240 BPM 木块式短促节拍器，带第一拍重音和独立音量
- 选择 Windows 录音输入，勾选“待命”后随播放录制吉他
- 暂停、停止或完成一次循环时自动保存录音，并把录音作为新音轨加入
- 自动保存并恢复上次打开的音轨、混音、节拍器、循环区间和录音设备
- 支持拖放导入；具体可解码格式取决于 Windows Media Foundation，常用的 WAV、MP3、AIFF、WMA、M4A、AAC、FLAC 已列入文件选择器

录音保存在：

```text
文档\GuitarPractice\Recordings
```

会话状态保存在：

```text
%LOCALAPPDATA%\GuitarPractice\session.json
```

## 运行

需要 Windows 10 或更高版本，以及 .NET 8 SDK：

```powershell
dotnet run --project .\src\GuitarPractice.App\GuitarPractice.App.csproj
```

## 生成可分发的 Windows x64 版本

```powershell
.\scripts\publish-win-x64.ps1
```

输出位于 `artifacts\win-x64`。这是自包含版本，目标电脑不需要另外安装 .NET。
发布时应分发整个目录，不要只复制 EXE；发布脚本会把 .NET、WPF 和 NAudio
所需的许可证及第三方声明一并放入该目录。

## GitHub 自动发布

公共仓库：[soeviturek/guitarapp](https://github.com/soeviturek/guitarapp)。

推送 `v主版本.次版本.修订版本` 格式的 tag（例如 `v0.1.0`）会触发
`.github\workflows\release.yml`，运行测试、构建 Windows x64 自包含 EXE，
并创建对应的 [GitHub Release](https://github.com/soeviturek/guitarapp/releases)：

```powershell
git push origin main
git tag v0.1.0
git push origin v0.1.0
```

下载 Release 中的 `GuitarPractice-0.1.0-win-x64.zip`，解压后运行
`GuitarPractice.App.exe`，不需要安装 .NET。ZIP 同时包含所需许可证和第三方声明。
tag 中的版本号会写入 EXE；`v0.1.0-beta.1` 这类 tag 会创建预发布版本。
重新运行同一个 tag 的工作流会更新该 Release 的 ZIP。

也可以在 GitHub 的 **Actions → Build Windows release → Run workflow** 手动构建。
选择 `main` 时只生成 Actions artifact，不创建 Release；构建产物保留 30 天，
Release 中的附件不受此保留期限制。工作流使用 GitHub 自动提供的 `GITHUB_TOKEN`，
不需要额外配置 secret。

## 商业发布

当前运行时依赖 .NET 8、WPF 和 NAudio 2.2.1，三者均采用允许商业使用、
修改和再分发的 MIT 许可证，不要求公开本应用源码。节拍器声音由程序实时生成，
没有使用第三方音效采样。

正式销售前仍需为应用准备自己的最终用户许可协议、隐私说明、品牌素材、
代码签名和安装包，并确保没有把无授权的歌曲、图片或其他素材打包进产品。
用户自行导入的音乐不随应用分发。

## 操作

| 操作 | 方法 |
|---|---|
| 播放 / 暂停 | `Space` |
| 设置循环起点 | `[` |
| 设置循环终点 | `]` |
| 选择循环区间 | `Shift` + 时间轴拖动 |
| 录音待命 | `R` |
| 任意定位 | 点击或拖动时间轴 |

录音期间会暂时锁定时间轴定位，避免录音与伴奏失去对齐。循环录音按单次 take 处理：到达循环终点后保存当前 take，伴奏继续循环，新录音会出现在音轨列表中。应用不会实时监听吉他输入，以避免扬声器回授；监听可由声卡的 direct monitor 功能完成。

## 项目结构

- `src\GuitarPractice.Core`：平台无关的时间轴、循环和节拍信号规则
- `src\GuitarPractice.App`：Windows WPF 界面、WASAPI 播放/录音、多轨混音与会话保存
- `tests\GuitarPractice.Core.Tests`：循环边界、时长变化、节拍和时间显示测试
- `tests\GuitarPractice.App.Tests`：实际 WAV 重采样、声道映射、音轨偏移、重定位和波形测试
