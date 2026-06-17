# 划词翻译 MAUI

这是一个轻量版 Pot 类工具，目标是保留“选中文本后快速翻译”的核心路径，并把平台相关能力隔离起来，方便后续补 macOS 版本。

## 当前功能

- Windows 桌面 MAUI 应用，macOS/Mac Catalyst 架构已预留
- 可配置全局快捷键，默认 `Ctrl+Alt+T`
- 通过平台适配器捕获当前前台程序中的选中文本
- 鼠标附近弹出翻译结果窗口，先显示加载状态，结果返回后原地更新，支持复制、固定和失焦关闭
- 托盘常驻，关闭窗口时可隐藏到托盘
- 可选开机启动
- 历史记录默认开启，可设置保留条数、删除单条或清空全部
- OpenAI API Key 存入系统安全凭据，不写入普通 JSON 设置文件

## 翻译引擎

- Google 翻译：默认引擎，使用网页接口，不需要 API Key；该接口是实验性方案。
- Bing 词典：使用必应词典网页结果，不需要 API Key；适合单词或短语，支持词性/释义/例句分区展示、美音/英音发音按钮和打开原网页查看完整内容。
- OpenAI：使用 OpenAI 官方 Chat Completions API，endpoint 固定，用户配置 API Key 和模型名。

OpenAI 引擎支持自定义 Prompt：

- 自定义 Prompt 为空时，程序会把 UI 中的源语言和目标语言一起发送给模型。
- 自定义 Prompt 非空时，程序只发送自定义 Prompt 和原文；UI 中的源语言和目标语言不会再参与 OpenAI 请求。
- Google 翻译仍会使用 UI 中的语言设置；Bing 词典使用必应网页自身的词典结果。

## 设计分层

- `Models`：设置、翻译结果、词典结果、历史记录模型
- `Services/Translation`：Google、Bing 词典网页、OpenAI provider 和 provider registry
- `Services/Platform`：热键、选中文本、托盘、启动项、安全凭据、浮窗位置接口
- `Platforms/Windows/Services`：Windows 真实适配器
- `Platforms/MacCatalyst/Services`：macOS 占位适配器，后续补 Keychain、菜单栏、快捷键和选中文本捕获

共享层不应引用 Win32、WinUI 或 Windows-only 命名空间；平台相关代码只放在 `Platforms/*` 下。

## 运行

```powershell
dotnet run -f net10.0-windows10.0.19041.0
```

启动后开启监听，在其他程序里选中文本，按配置的快捷键即可捕获并翻译。

## 当前边界

- 当前可运行版本以 Windows 为主。
- macOS 目前只预留架构；全局快捷键、菜单栏和跨应用选中文本捕获尚未实现。
- 选中文本捕获仍会短暂使用剪贴板，并尽量恢复原有文本内容；复杂富文本剪贴板内容暂不完整保留。
- Google 网页接口可能受网络或服务策略影响；需要更稳定时可切换到 OpenAI。
- Bing 词典依赖必应词典网页结构；如果页面结构变化，浮窗摘要解析可能需要同步调整，仍可通过“打开网页”查看原页面。
