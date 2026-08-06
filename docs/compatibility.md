# 兼容性说明

当前已验证的静态兼容基线：

- Sprocket `0.2.53.2`
- Unity `2022.3.62f2`
- MelonLoader `0.7.3` CoreCLR/net6 IL2CPP
- XUnity.AutoTranslator `5.6.1` MelonMod IL2CPP

桥接依赖生成代理中的原生方法信息字段、`UnityEngine.AssetBundle` 内部 icall，以及 XUnity 的 `AutoTranslator.Default.TryTranslate` 和初始化状态 API。以下变化都可能需要重新构建或修改桥接：

- 游戏更新并重新生成 `Unity.TextMeshPro.dll` 代理。
- MelonLoader 或 Il2CppInterop 改变 `NativeHook<T>` 或代理字段布局。
- XUnity 改变 `AssetBundle` 内部调用签名或后备字体格式。
- XUnity 改变初始化事件或同步缓存 API。
- 目标游戏改用不同的 TMP 组件或自行覆盖文本刷新流程。

该桥接针对 `TMPro.*` 与 `Il2CppTMPro.*` 的代理命名空间不匹配，不依赖 Sprocket 特有 API。其他游戏只要具有相同代理形态和兼容的运行时 API，也可能适用；未经实际构建和游戏内验证的组合不视为已支持环境。

离线测试覆盖文本过滤、缓存恢复决策和重入保护。Clean 构建成功只证明当前本地依赖可以编译，不证明所有游戏场景中的文本都已正确显示。
