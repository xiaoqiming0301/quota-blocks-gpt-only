# Quota Blocks（仅保留 GPT 的个人修改版）for Windows

Quota Blocks（仅保留 GPT 的个人修改版）是一个 Windows 任务栏组件，只显示本机 ChatGPT / Codex 的剩余额度。点击任务栏中的图标和色块即可打开详细面板，查看 5 小时、周额度及对应重置时间。

![详细面板与任务栏](docs/gpt-version-panel.png)

## 系统要求

- Windows 10 或 Windows 11；
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)；
- 已安装并登录的 Codex / ChatGPT 桌面应用。

## 安装

在本目录运行：

```powershell
.\scripts\install.ps1
```

安装脚本会编译并安装程序到 `%LOCALAPPDATA%\Programs\GPTVersion`，然后注册开机启动并打开程序。再次运行脚本会直接更新。

仅构建而不安装：

```powershell
dotnet build QuotaBlocksWin.csproj -c Release --nologo
```

检查本地额度读取路径：

```powershell
.\bin\Release\net8.0-windows\GPTVersion.exe --probe
```

## 使用

- 点击任务栏组件可打开详细面板；
- “切换为 English / Switch to Chinese”切换显示语言；
- “开机自动启动”控制 Windows 登录后启动；
- “打开 Codex 额度页面”在浏览器中打开对应页面；
- “退出”关闭程序。

组件每两分钟自动刷新，无需手动刷新。执行 `GPTVersion.exe --probe` 时不会显示界面，适用于诊断本地额度读取。

## 数据与隐私

程序通过本机 Codex / ChatGPT 应用的 `codex.exe app-server` 获取本地额度信息。它不向第三方服务上传账户凭据或额度数据。详见根目录的 [隐私说明](../PRIVACY.md)。

## 归属

本 Windows 个人修改版本基于 [NathanCheng685/quota-blocks](https://github.com/NathanCheng685/quota-blocks)，并继续采用 [MIT 许可证](../LICENSE)。
