# 🌸 芽衣 Claude AI 助手 - VPet 插件

**崩坏三雷电芽衣主题的 Claude AI 桌面助手**，基于 [VPet（虚拟桌宠模拟器）](https://store.steampowered.com/app/1920960/_/) 插件系统开发。

将强大的 Claude AI（对话、代码生成、文件管理）与可爱的桌面宠物合二为一！

---

## ✨ 功能

| 功能 | 说明 |
|------|------|
| 💬 **AI 对话** | 与 Claude AI 实时对话（流式响应） |
| 📁 **文件管理** | 浏览、创建、编辑、删除工作目录中的文件 |
| 💻 **代码生成** | 让 Claude 帮你写代码、优化代码 |
| ⚙️ **灵活配置** | API Key、模型选择、温度参数、工作目录全可调 |

---

## 📦 项目结构

```
mei-claude-pet/
├── VPet.Plugin.MeiChat/           # 🎯 主插件项目 (C# WPF)
│   ├── Main.cs                     # 插件入口 (继承 MainPlugin)
│   ├── ClaudeClient.cs             # Claude API 客户端
│   ├── Models/
│   │   ├── ChatMessage.cs          # 消息模型
│   │   └── AppConfig.cs            # 配置模型
│   └── Views/
│       ├── ChatWindow.xaml/.cs      # 聊天窗口
│       ├── FileBrowser.xaml/.cs     # 文件管理器
│       ├── SettingsWindow.xaml/.cs  # 设置窗口
│       └── MessageTemplateSelector.cs
├── MeiPetMod/                      # 🎨 芽衣角色 Mod（预留扩展）
│   ├── info.lps
│   └── pet/
│       └── mei.lps
├── build.bat                       # 🔨 一键构建脚本
└── README.md                       # 📖 本文件
```

---

## 🚀 快速开始

### 前置条件

1. **VPet**（虚拟桌宠模拟器）- 从 [Steam](https://store.steampowered.com/app/1920960/_/) 安装（免费）
2. **.NET 8 SDK** - 从 [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0) 下载安装
3. **Anthropic API Key** - 从 [console.anthropic.com](https://console.anthropic.com/) 获取

### 构建安装

**一键构建：**
```bash
# 双击 build.bat 即可自动完成构建 + 安装
# 脚本会自动检测 VPet 安装路径
.\build.bat
```

**手动构建：**
```bash
# 设置 VPet 路径（如果脚本没自动检测到）
set VPET_PATH="C:\Program Files (x86)\Steam\steamapps\common\VPet"

# 构建
dotnet build VPet.Plugin.MeiChat\VPet.Plugin.MeiChat.csproj -c Release -p:VPET_PATH="%VPET_PATH%"

# 复制到 Mod 目录
copy VPet.Plugin.MeiChat\bin\Release\net8.0-windows\VPet.Plugin.MeiChat.dll "%VPET_PATH%\mod\MeiChat\"
```

### 使用

1. 启动 **VPet**（需重启以加载新插件）
2. 右键点击桌宠 → **系统** → **设置**
3. 在 **Mod/插件管理** 中找到 **MeiChat**，点击设置按钮 ⚙️
4. 填入你的 **Anthropic API Key**
5. 选择 **Claude 模型**（推荐 Sonnet 4.6）
6. 设置 **工作目录**（Claude 可操作的文件目录）
7. 点击 **保存设置**
8. 点击右下角的 **打开对话** 💬 开始使用！

---

## 🛠 开发指南

### 插件 API 说明

本插件基于 VPet 的 `MainPlugin` 抽象类：

```csharp
public class Main : MainPlugin
{
    public override string PluginName => "MeiChat";
    
    public Main(IMainWindow mainwin) : base(mainwin) { }
    
    public override void LoadPlugin()   // 插件加载时调用
    public override void GameLoaded()   // 游戏加载完毕
    public override void LoadDIY()      // 添加自定义按钮
    public override void Setting()      // 打开设置
    public override void Save()         // 保存数据
    public override void EndGame()      // 插件卸载
}
```

### 为插件添加自定义功能

在 `Main.cs` 中可以添加更多方法，并通过 `MW.Dispatcher.Invoke()` 在 UI 线程执行操作。

---

## 🎨 扩展：添加芽衣角色皮肤

### 准备素材

需要准备 SD 比例（2.5头身）的雷电芽衣 PNG 序列帧：

1. **工具推荐：**
   - [NovelAI](https://novelai.net/) / [Midjourney](https://www.midjourney.com/) 生成 Q 版角色图
   - Aseprite / Photoshop 裁切成序列帧
   - 参考 VPet 原版 `pet/vup/` 目录下的精灵图命名格式

2. **精灵图命名规则：**
   ```
   {套装字母}_{帧编号}_{帧间隔ms}.png
   例如: A_000_125.png  (A套装，第0帧，125ms切换)
   ```

3. **目录结构：**
   ```
   MeiPetMod/
   ├── info.lps
   ├── pet/
   │   ├── mei.lps           # 角色定义
   │   └── mei/
   │       ├── Default/
   │       │   ├── Nomal/1/   # 待机动画帧
   │       │   ├── Happy/1/   # 开心动画帧
   │       │   └── Ill/1/     # 生病动画帧
   │       ├── BDay/          # 生日动画
   │       └── ...
   ├── icon.png              # 模组图标
   └── text/mei.lps          # 对话文本
   ```

4. **安装：**
   ```bash
   # 将 MeiPetMod 目录复制到 VPet 的 mod 目录
   xcopy /E MeiPetMod "%VPET_PATH%\mod\MeiPetMod\"
   ```

---

## 📝 常见问题

**Q: 构建时找不到 VPet 接口 DLL？**
A: 确保已安装 VPet，然后设置环境变量 `VPET_PATH` 指向 VPet 安装目录。

**Q: API 连接测试失败？**
A: 检查 API Key 是否正确，以及网络是否能访问 `api.anthropic.com`。

**Q: 如何更换 Claude 模型？**
A: 在设置窗口中选择不同的模型：
- **Haiku 4.5** - 最快响应，适合简单对话
- **Sonnet 4.6** - 速度与质量平衡（推荐）
- **Opus 4.8** - 最强能力，适合复杂任务

**Q: 如何卸载插件？**
A: 删除 VPet 安装目录下的 `mod/MeiChat` 文件夹。

---

## 📄 许可

MIT License - 可自由使用、修改、分发。

---

## 🙏 鸣谢

- [VPet - 虚拟桌宠模拟器](https://github.com/LorisYounger/VPet) - 开源的桌宠框架
- [Anthropic Claude API](https://www.anthropic.com/) - AI 能力支持
- 崩坏3 / miHoYo - 雷电芽衣角色
