# XUnity Il2CppTMPro Bridge

适用于 MelonLoader IL2CPP 游戏的 XUnity.AutoTranslator 桥接模组。它让 XUnity 能够处理由 Il2CppInterop 生成到 `Il2CppTMPro` 命名空间的 TextMeshPro 文本。

XUnity.AutoTranslator 5.6.1 默认查找 `TMPro.*` 类型；部分 MelonLoader/Il2CppInterop 环境生成的代理类型则位于 `Il2CppTMPro.*`。在这种环境中，翻译文件虽然已经加载，部分 TMP 界面仍不会显示译文。本模组通过 XUnity 的公开缓存 API 应用已有翻译，不修改 XUnity，也不自行读取汉化文件。

## 功能

- 接入 `TMP_Text.text`、`TextMeshPro.OnEnable` 和 `TextMeshProUGUI.OnEnable` 的原生 IL2CPP 方法。
- 在 XUnity 初始化完成及场景加载后执行限频补扫，覆盖 Hook 前已经存在的文本。
- 复用 `AutoTranslator.Default.TryTranslate`，缓存未命中时不会主动调用在线翻译接口。
- 防止重复写回和递归调用；单个 Hook 不可用时会记录警告并继续加载其余功能。
- 自行加载 XUnity 配置的 TMP 后备字体（XUnity 无法解析 `Il2CppTMPro.TMP_FontAsset`），注册为 `TMP_Settings` 全局回退与逐字体回退，中文字形由 TMP 原生解析，不直接替换组件字体或材质。
- 可通过 `UserData/MelonPreferences.cfg` 中的 `XUnityIl2CppTMProBridge`/`VerboseLogging` 开启逐组件日志。

## 已验证环境

- Sprocket `0.2.53.2`
- Unity `2022.3.62f2`
- MelonLoader `0.7.3` CoreCLR/net6 IL2CPP
- XUnity.AutoTranslator `5.6.1` MelonMod IL2CPP

以上是当前实际验证基线，不代表问题只存在于 Sprocket。其他使用 `Il2CppTMPro` 代理类型的 MelonLoader IL2CPP 游戏也可能适用，但游戏、MelonLoader、Il2CppInterop 或 XUnity 版本变化后需要重新验证。详细边界见 [兼容性说明](docs/compatibility.md)。

## 安装

1. 安装并配置 XUnity.AutoTranslator。
2. 从 [Releases](https://github.com/furryaxw/XUnity-Il2CppTMPro-Bridge/releases) 下载 `XUnity.Il2CppTMProBridge.dll`。
3. 将 DLL 放入游戏根目录的 `Mods` 文件夹。
4. 启动游戏，在 `MelonLoader/Latest.log` 中确认 XUnity、Il2CppTMPro 类型和 Hook 日志。

本仓库和 Release 均不包含 XUnity、MelonLoader、游戏程序集或汉化文本。卸载时只需删除 `Mods/XUnity.Il2CppTMProBridge.dll`。

## 构建与测试

项目目标框架为 .NET 6，并引用本地游戏目录中的 MelonLoader、IL2CPP 代理程序集和 XUnity Core。构建前通过 `SPROCKET_GAME_ROOT` 环境变量或 `SprocketGameRoot` MSBuild 属性提供游戏根目录。

```powershell
dotnet test .\XUnity.Il2CppTMProBridge.slnx --configuration Release
dotnet build .\src\XUnity.Il2CppTMProBridge\XUnity.Il2CppTMProBridge.csproj `
  --configuration Release `
  -p:SkipModDeploy=true
```

省略 `-p:SkipModDeploy=true` 时，构建会将桥接 DLL 复制到所选游戏目录的 `Mods`。第三方和游戏程序集均设置为非复制引用，不会进入输出或 Release。

## 验证

预期启动日志包括：

```text
[XUnity Il2CppTMPro Bridge] XUnity 5.6.1.0 detected.
[XUnity Il2CppTMPro Bridge] Il2CppTMPro.TMP_Text detected.
[XUnity Il2CppTMPro Bridge] Hooked TMP_Text.set_text.
[XUnity Il2CppTMPro Bridge] Loaded fallback TMP font: <path>
[XUnity Il2CppTMPro Bridge] Global TMP fallback registered via TMP_Settings.
[XUnity Il2CppTMPro Bridge] scheduled scan: inspected=N, hits=N, applied=N.
```

构建和离线测试只能确认静态兼容性，不能替代目标游戏内界面的显示验收。工作原理及排错步骤见 [实现原理](docs/how-it-works.md) 和 [故障排查](docs/troubleshooting.md)。

## English

This MelonLoader IL2CPP mod bridges XUnity.AutoTranslator to TextMeshPro proxies generated in the `Il2CppTMPro` namespace. The namespace mismatch is not specific to Sprocket; Sprocket is the current verified environment. The bridge uses XUnity's synchronous translation cache and loads the configured fallback TMP font when XUnity cannot resolve `Il2CppTMPro.TMP_FontAsset`, registering it as a global `TMP_Settings` fallback plus a per-font fallback so CJK glyphs resolve natively without touching component fonts. It does not bundle or modify XUnity, MelonLoader, game assemblies, or translation files.

## License

[MIT](LICENSE.txt)
