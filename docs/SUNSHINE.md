# Sunshine / Moonlight 串流与重新连接

屏幕管家管理 Windows 显示输出，Sunshine 决定捕获哪块屏幕。关闭 Sunshine 正在捕获的屏幕会中断当前画面；若希望下次串流时自动启用，需要配置 Sunshine 的显示准备行为。

## 平时关闭，下次串流自动恢复

以下处理针对已排查的 **Foundation Sunshine v2026.717.124516.杂鱼 + Zako 虚拟显示器**。其他分支、版本和客户端覆盖设置可能有不同表现。

该版本在虚拟显示模式下，会把 `ensure_active` 映射为内部的 `no_operation`，假定创建出的虚拟显示器已经启用。但 Windows 可能记住屏幕管家保存的停用状态，导致下一次创建 Zako 时仍为未启用，串流在捕获前失败。

可以在 Sunshine 的音频 / 视频设置中采用：

| 设置 | 值 |
| --- | --- |
| 输出显示器 | Zako 虚拟显示器选项 |
| 串流时显示器组合状态 | 副屏串流 |
| 对应配置键 | `display_device_prep = ensure_secondary` |
| Moonlight 自定义屏幕模式（如有） | 跟随服务端 |

这个模式会将 Zako 加入当前启用的桌面，保留现有实体主屏和已经开启的实体屏幕。原本关闭的其他实体屏幕不会被加入启用集合。自动激活发生在 Sunshine 的串流准备阶段，因此不要求屏幕管家常驻。

如果直接编辑 `config/sunshine.conf`，先备份文件，在没有串流会话时修改并重载 Sunshine。不要把另一台电脑的完整配置文件覆盖到自己的安装目录。

**仓库中的程序和便携包不会自动应用上述 Sunshine 设置。** 已验证特定环境中的配置被服务正常载入；实际 Moonlight 重新连接仍需在各自环境中验证。

源码依据：

- [统一选项映射到 VDD 模式](https://github.com/AlkaidLab/foundation-sunshine/blob/3cd3b698bb2e69ff5c18274341a9150c8a9f37fb/src/display_device/parsed_config.cpp#L608)
- [启用 VDD 并保留活动实体屏幕的实现](https://github.com/AlkaidLab/foundation-sunshine/blob/3cd3b698bb2e69ff5c18274341a9150c8a9f37fb/src/display_device/vdd_utils.cpp#L1104)

## 串流保护和恢复倒计时

屏幕管家的“串流保护”是手动指定的关闭限制，适合不希望误关捕获输出时使用。指定保护后，“只开此屏”、快捷键等操作也必须保留受保护屏幕。

如果希望关闭这块屏幕，先取消或更改保护对象。切换后仍需在 30 秒内确认，否则恢复进程会尝试回到更改前的可用配置。

## 故障排查

| 现象 | 检查方法 |
| --- | --- |
| 列表中没有虚拟屏幕 | 确认虚拟驱动已安装，并由 Sunshine 创建了显示设备；工具本身不创建 VDD |
| 串流前提示设备未激活 | 检查本页的显示准备模式，以及 Moonlight 是否覆盖服务端设置 |
| 开始串流后其他屏幕被关闭 | 检查 Sunshine 是否启用了“仅启用指定显示器串流” |
| 热键没有反应 | 检查快捷键占用及客户端键盘转发，改用面板或托盘测试 |
| 工具刚关闭屏幕又被开启 | Sunshine 或其他显示管理软件可能重新应用了配置；工具会显示实际状态，不会持续抢占 |
| 恢复失败 | 查看 `%LOCALAPPDATA%\ScreenPilot\Recovery\` 内对应操作的 `error.txt` |

不要把“无操作”“验证显示器”作为通用的自动恢复配置；它们未必会启用已经关闭的捕获屏幕。不同版本选项名称也可能不同，请结合该版本的文档和日志判断。
