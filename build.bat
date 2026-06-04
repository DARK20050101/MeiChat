@echo off
chcp 65001 >nul
echo ========================================
echo  芽衣 Claude AI 助手 - VPet 插件构建
echo ========================================
echo.

REM 检查 .NET SDK
dotnet --version >nul 2>&1
if %ERRORLEVEL% NEQ 0 (
    echo [错误] 未检测到 .NET SDK！
    echo 请从 https://dotnet.microsoft.com/download/dotnet/8.0 下载安装 .NET 8 SDK
    pause
    exit /b 1
)

echo [✓] .NET SDK 已检测
echo.

REM 构建插件（使用 NuGet 包，无需 VPET_PATH）
echo [1/2] 开始构建插件...
cd /d "%~dp0"

dotnet build VPet.Plugin.MeiChat\VPet.Plugin.MeiChat.csproj -c Release

if %ERRORLEVEL% NEQ 0 (
    echo [错误] 构建失败，请检查错误信息
    pause
    exit /b 1
)

echo [✓] 构建成功！
echo.

REM 复制到 VPet Mod 目录
echo [2/2] 安装到 VPet Mod 目录...

REM 尝试自动检测 VPet 安装位置
set "VPET_PATH="
if exist "C:\Program Files (x86)\Steam\steamapps\common\VPet\" set "VPET_PATH=C:\Program Files (x86)\Steam\steamapps\common\VPet"
if exist "C:\Program Files\Steam\steamapps\common\VPet\" set "VPET_PATH=C:\Program Files\Steam\steamapps\common\VPet"
if exist "D:\Program Files (x86)\Steam\steamapps\common\VPet\" set "VPET_PATH=D:\Program Files (x86)\Steam\steamapps\common\VPet"
if exist "D:\SteamLibrary\steamapps\common\VPet\" set "VPET_PATH=D:\SteamLibrary\steamapps\common\VPet"
if exist "E:\SteamLibrary\steamapps\common\VPet\" set "VPET_PATH=E:\SteamLibrary\steamapps\common\VPet"
if exist "F:\SteamLibrary\steamapps\common\VPet\" set "VPET_PATH=F:\SteamLibrary\steamapps\common\VPet"

if "%VPET_PATH%"=="" (
    echo [!] 未自动检测到 VPet 安装路径
    echo [!] 请手动复制以下 DLL 到 VPet 的 mod\MeiChat\plugin 目录:
    echo.
    echo     %~dp0VPet.Plugin.MeiChat\bin\Release\net8.0-windows\VPet.Plugin.MeiChat.dll
    echo.
    pause
    exit /b 0
)

set "MOD_DIR=%VPET_PATH%\mod\MeiChat"
set "PLUGIN_DIR=%MOD_DIR%\plugin"

if not exist "%MOD_DIR%" mkdir "%MOD_DIR%"
if not exist "%PLUGIN_DIR%" mkdir "%PLUGIN_DIR%"

REM 只复制插件 DLL 到 plugin 子目录（不要复制 Interface.dll 等系统 DLL）
copy /Y "%~dp0VPet.Plugin.MeiChat\bin\Release\net8.0-windows\VPet.Plugin.MeiChat.dll" "%PLUGIN_DIR%\" >nul

REM 创建 Mod 信息文件使 VPet 识别
set "INFO_LPS=%MOD_DIR%\info.lps"
if not exist "%INFO_LPS%" (
    echo vupmod#MeiChat:^|author#You:^|gamever#11000:^|ver#100:^|intro#MeiChat Claude AI Assistant:^| > "%INFO_LPS%"
)

echo [✓] 已安装到: %PLUGIN_DIR%
echo.
echo ========================================
echo  安装完成！
echo  请重启 VPet 以加载插件
echo ========================================
echo.
echo  启动后，可以在 VPet 的插件设置中找到 "MeiChat"
echo  配置 API Key 后即可开始使用！
echo.

pause
