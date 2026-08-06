# 故障排查

## 模组无法加载

确认以下文件存在：

```text
Mods\XUnity.Il2CppTMProBridge.dll
Mods\XUnity.AutoTranslator.Plugin.MelonMod.dll
UserLibs\XUnity.AutoTranslator.Plugin.Core.dll
```

然后检查 `MelonLoader/Latest.log` 中是否同时出现 XUnity 初始化和桥接模组日志。

## 日志显示正在等待 XUnity

桥接必须在 XUnity 初始化完成后才会查询翻译缓存。检查 XUnity 自身的错误日志、版本和配置，不要通过复制第二套 XUnity DLL 解决。

## Hook 警告

`Could not prepare` 或 `Could not hook` 通常表示游戏生成代理、MelonLoader 或 Il2CppInterop 版本与当前兼容基线不同。保留完整 `MelonLoader/Latest.log`，并记录游戏、MelonLoader 和 XUnity 版本。

## 只有部分文本被翻译

确认对应原文已经存在于 `AutoTranslator/Translation/<language>/Text`，并检查启动日志中的 `hits` 和 `applied` 数量。桥接只读取 XUnity 已建立的缓存；没有缓存结果时不会自行调用在线翻译服务。

可在 `UserData/MelonPreferences.cfg` 中启用：

```ini
[XUnityIl2CppTMProBridge]
VerboseLogging = true
```

详细日志用于短期排错，正常游玩时建议关闭。

## 译文显示方框或字体加载失败

桥接读取 XUnity 配置中 `[Behaviour]` 下的 `FallbackFontTextMeshPro`；未配置时尝试默认位置 `AutoTranslator\arialuni_sdf_u2022`。确认该值指向有效的 TMP 字体 AssetBundle。

正常启动日志应包含：

```text
[XUnity Il2CppTMPro Bridge] Loaded fallback TMP font: <path>
[XUnity Il2CppTMPro Bridge] Global TMP fallback registered via TMP_Settings.
```

- 没有 `Loaded fallback TMP font`：文件不存在或 AssetBundle 加载失败，检查路径与文件完整性。
- 有加载日志但仍显示方框：后备字体不含目标字形，需要替换包含 CJK 字形的字体文件。
- 文本整块空白：组件渲染被破坏。桥接不会替换组件字体/材质，若出现请保留完整日志并报告。
