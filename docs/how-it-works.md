# 实现原理

部分 MelonLoader IL2CPP 环境由 Il2CppInterop 生成的代理类型位于 `Il2CppTMPro` 命名空间，而 XUnity.AutoTranslator 5.6.1 只查找 `TMPro.*`。因此 XUnity 的 TMP Hook 会静默跳过、后备字体加载也失败。桥接模组针对这类环境补齐两条链路：文本翻译与后备字体。

## 文本翻译链路

桥接模组在主线程完成以下工作：

1. 等待 XUnity.AutoTranslator 初始化完成。
2. 从生成代理的 `NativeMethodInfoPtr_*` 字段取得原生方法地址。
3. 通过 `MelonLoader.NativeUtils.NativeHook<T>` 接入 `TMP_Text.text` 与两个具体 TMP 类型的 `OnEnable`。
4. 从组件读取当前文本，通过 `AutoTranslator.Default.TryTranslate` 查询 XUnity 已建立的同步缓存。
5. 缓存命中时先确保后备字体可用，再写回译文；同一组件再次收到原文时恢复已缓存译文。
6. 初始化和场景加载后执行一次限频全量补扫，处理 Hook 安装前已经创建的组件。

写回期间使用按实例指针隔离的重入保护。组件缓存同时记录 `InstanceID`，避免 Il2Cpp 对象销毁后复用指针造成错译文。组件扫描和 XUnity 对象访问都限制在初始化时记录的主线程。

## 后备字体链路

XUnity 的后备字体加载依赖 `UnityTypes.TMP_FontAsset`，在 `Il2CppTMPro` 环境下该类型解析为 null，因此 XUnity 无法加载字体。桥接模组自行完成：

1. 读取 XUnity 配置中 `[Behaviour]` 下的 `FallbackFontTextMeshPro`（未配置时使用 `AutoTranslator\arialuni_sdf_u2022` 作为默认位置）。
2. 通过 `UnityEngine.AssetBundle::LoadFromFile_Internal` 等 icall 直接加载字体 AssetBundle，并加载其中的全部资产保持生命周期。
3. 仅当字体自身的 atlas 纹理、材质或 shader 引用缺失时进行修复；随后调用 `ReadFontAssetDefinition()` 重建运行时查找表。
4. 将后备字体注册到 `TMP_Settings.fallbackFontAssets`（全局回退），并注册到组件当前字体的 `fallbackFontAssetTable`（逐字体回退）。

TMP 在渲染时按字形查找：当前字体缺字形（中文）时原生回退到后备字体，拉丁字符保持游戏原字体渲染。桥接模组不直接替换组件的 `font` 或 `fontSharedMaterial`——在 IL2CPP 代理下直接替换会把 mesh 按旧字体生成、材质按新字体渲染，导致整块文本空白。

本模组不解析 `AutoTranslator/Translation`，不选择在线翻译服务，也不修改 XUnity 配置。缓存未命中时直接保留原文。
