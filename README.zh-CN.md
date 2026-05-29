# SpeakerRouter

[English](README.md)

SpeakerRouter 是一个轻量 Windows 11 托盘工具，会根据当前正在发声的窗口所在屏幕，自动切换 Windows 系统默认声音输出设备。

## 功能

- 检测 Windows Core Audio 中正在发声的会话，并将 Chrome/Edge 等浏览器子进程映射回可见窗口。
- 支持给每块屏幕绑定一个播放设备，并按窗口所在屏幕自动切换系统默认输出。
- 使用 WinForms、Win32 和 Windows Core Audio API，无第三方依赖包。
- 常驻系统托盘，可选开机启动。
- 配置保存到 `%AppData%\SpeakerRouter\settings.json`。

## 使用

1. 运行 `SpeakerRouter.exe`。
2. 左键点击托盘图标，或右键打开主界面。
3. 为每块屏幕选择绑定的播放设备并保存。
4. 启用自动检测并切换。
