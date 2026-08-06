using Il2CppTMPro;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using MelonLoader;
using MelonLoader.NativeUtils;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using XUnity.AutoTranslator.Plugin.Core;
using XUnity.Il2CppTMProBridge.Core;
using XuaAutoTranslator = global::XUnity.AutoTranslator.Plugin.Core.AutoTranslator;

[assembly: MelonInfo(typeof(XUnity.Il2CppTMProBridge.BridgeMod), "XUnity Il2CppTMPro Bridge", "0.1.0", "furryAxw")]

namespace XUnity.Il2CppTMProBridge;

public sealed class BridgeMod : MelonMod
{
    private const double MinimumScanIntervalSeconds = 2;
    private const string TextSetterMethodInfoField = "NativeMethodInfoPtr_set_text_Public_Virtual_New_set_Void_String_0";
    private const string OnEnableMethodInfoField = "NativeMethodInfoPtr_OnEnable_Protected_Virtual_Void_0";
    private static BridgeMod? Instance;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TextSetterDelegate(IntPtr instance, IntPtr value, IntPtr methodInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void OnEnableDelegate(IntPtr instance, IntPtr methodInfo);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LoadFromFileInternalDelegate(IntPtr path, uint crc, ulong offset);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr LoadAssetWithSubAssetsInternalDelegate(IntPtr instance, IntPtr name, IntPtr type);

    private static readonly TextSetterDelegate TextSetterDetourDelegate = TextSetterDetour;
    private static readonly OnEnableDelegate TextMeshProOnEnableDetourDelegate = TextMeshProOnEnableDetour;
    private static readonly OnEnableDelegate TextMeshProUguiOnEnableDetourDelegate = TextMeshProUguiOnEnableDetour;
    private static readonly LoadFromFileInternalDelegate LoadFromFileInternal = IL2CPP.ResolveICall<LoadFromFileInternalDelegate>("UnityEngine.AssetBundle::LoadFromFile_Internal(System.String,System.UInt32,System.UInt64)");
    private static readonly LoadAssetWithSubAssetsInternalDelegate LoadAssetWithSubAssetsInternal = IL2CPP.ResolveICall<LoadAssetWithSubAssetsInternalDelegate>("UnityEngine.AssetBundle::LoadAssetWithSubAssets_Internal");

    private readonly Dictionary<nint, CacheEntry> _cache = new();
    private readonly Dictionary<string, DateTime> _nextErrorLog = new();
    private readonly List<UnityEngine.Object> _keptAssets = new();
    private readonly ReentrancyGuard<nint> _writeGuard = new();
    private readonly List<KeyValuePair<string, string>> _substitutions = new();
    private MelonPreferences_Entry<bool>? _verboseLogging;
    private NativeHook<TextSetterDelegate>? _textSetterHook;
    private NativeHook<OnEnableDelegate>? _textMeshProOnEnableHook;
    private NativeHook<OnEnableDelegate>? _textMeshProUguiOnEnableHook;
    private int _mainThreadId;
    private bool _translatorReady;
    private bool _scanPending;
    private UnityEngine.AssetBundle? _fontBundle;
    private Il2CppTMPro.TMP_FontAsset? _fontAsset;
    private DateTime _scanNotBeforeUtc;
    private DateTime _lastScanUtc = DateTime.MinValue;

    public override void OnInitializeMelon()
    {
        Instance = this;
        _mainThreadId = Environment.CurrentManagedThreadId;
        var preferences = MelonPreferences.CreateCategory("XUnityIl2CppTMProBridge", "XUnity Il2CppTMPro Bridge");
        _verboseLogging = preferences.CreateEntry("VerboseLogging", false, "Verbose logging");
        LoadSubstitutions();

        LoadFallbackFont();

        LoggerInstance.Msg($"XUnity {typeof(XuaAutoTranslator).Assembly.GetName().Version} detected.");
        LoggerInstance.Msg($"{typeof(TMP_Text).FullName} detected.");

        _textSetterHook = CreateNativeHook("TMP_Text.set_text", typeof(TMP_Text), TextSetterMethodInfoField, TextSetterDetourDelegate);
        _textMeshProOnEnableHook = CreateNativeHook("TextMeshPro.OnEnable", typeof(TextMeshPro), OnEnableMethodInfoField, TextMeshProOnEnableDetourDelegate);
        _textMeshProUguiOnEnableHook = CreateNativeHook("TextMeshProUGUI.OnEnable", typeof(TextMeshProUGUI), OnEnableMethodInfoField, TextMeshProUguiOnEnableDetourDelegate);
        AttachNativeHook("TMP_Text.set_text", _textSetterHook);
        AttachNativeHook("TextMeshPro.OnEnable", _textMeshProOnEnableHook);
        AttachNativeHook("TextMeshProUGUI.OnEnable", _textMeshProUguiOnEnableHook);

        AutoTranslatorState.PluginInitializationCompleted += OnTranslatorInitialized;
        if (AutoTranslatorState.PluginInitialized)
        {
            OnTranslatorInitialized();
        }
        else
        {
            LoggerInstance.Msg("Waiting for XUnity.AutoTranslator initialization.");
        }
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        ScheduleScan(TimeSpan.FromSeconds(1));
    }

    public override void OnUpdate()
    {
        if (!_translatorReady || !_scanPending || DateTime.UtcNow < _scanNotBeforeUtc)
        {
            return;
        }

        _scanPending = false;
        ScanAllText("scheduled");
    }

    public override void OnDeinitializeMelon()
    {
        AutoTranslatorState.PluginInitializationCompleted -= OnTranslatorInitialized;
        DetachNativeHook("TMP_Text.set_text", _textSetterHook);
        DetachNativeHook("TextMeshPro.OnEnable", _textMeshProOnEnableHook);
        DetachNativeHook("TextMeshProUGUI.OnEnable", _textMeshProUguiOnEnableHook);
        _textSetterHook = null;
        _textMeshProOnEnableHook = null;
        _textMeshProUguiOnEnableHook = null;
        _writeGuard.Dispose();
        Instance = null;
    }

    private void OnTranslatorInitialized()
    {
        _translatorReady = true;
        LoggerInstance.Msg("XUnity.AutoTranslator initialization completed.");
        ScheduleScan(TimeSpan.FromMilliseconds(500));
    }

    private void ScheduleScan(TimeSpan delay)
    {
        _scanPending = true;
        var requested = DateTime.UtcNow + delay;
        var throttled = _lastScanUtc + TimeSpan.FromSeconds(MinimumScanIntervalSeconds);
        _scanNotBeforeUtc = requested > throttled ? requested : throttled;
    }

    private void ScanAllText(string reason)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
        {
            ScheduleScan(TimeSpan.Zero);
            return;
        }

        var inspected = 0;
        var hits = 0;
        var applied = 0;
        try
        {
            var components = UnityEngine.Object.FindObjectsOfType<TMP_Text>(true);
            foreach (var component in components)
            {
                inspected++;
                var result = Process(component);
                if (result is ProcessResult.CacheHit or ProcessResult.Applied)
                {
                    hits++;
                }

                if (result == ProcessResult.Applied)
                {
                    applied++;
                }
            }

            _lastScanUtc = DateTime.UtcNow;
            LoggerInstance.Msg($"{reason} scan: inspected={inspected}, hits={hits}, applied={applied}.");
        }
        catch (Exception exception)
        {
            LogExceptionLimited("scan", exception);
        }
    }

    private ProcessResult Process(TMP_Text? component)
    {
        if (!_translatorReady || component is null || Environment.CurrentManagedThreadId != _mainThreadId)
        {
            return ProcessResult.None;
        }

        var pointer = component.Pointer;
        if (pointer == IntPtr.Zero)
        {
            return ProcessResult.None;
        }

        if (!_writeGuard.TryEnter(pointer, out var lease))
        {
            return ProcessResult.None;
        }

        using (lease)
        {
            try
            {
                var current = component.text;
                var instanceId = component.GetInstanceID();
                CacheEntry? cached = null;
                if (_cache.TryGetValue(pointer, out var existing) && existing.InstanceId == instanceId)
                {
                    cached = existing;
                }
                else
                {
                    _cache.Remove(pointer);
                }

                var snapshot = cached is null
                    ? (TranslationSnapshot?)null
                    : new TranslationSnapshot(cached.Original, cached.Translation);
                switch (TranslationStateMachine.Decide(current, snapshot))
                {
                    case TranslationAction.Skip:
                        return ProcessResult.None;
                    case TranslationAction.RestoreCachedTranslation:
                        var restoredFontChanged = ApplyFallbackFont(component);
                        component.text = cached!.Translation;
                        if (restoredFontChanged)
                        {
                            component.ForceMeshUpdate();
                        }

                        Verbose("Restored translation for instance " + instanceId + ".");
                        return ProcessResult.Applied;
                }

                if (!XuaAutoTranslator.Default.TryTranslate(current, out var translation)
                    || string.IsNullOrWhiteSpace(translation))
                {
                    return ProcessResult.None;
                }

                translation = RestoreTemplateVariables(current, translation);

                _cache[pointer] = new CacheEntry(instanceId, current, translation);
                var fontChanged = ApplyFallbackFont(component);
                if (string.Equals(current, translation, StringComparison.Ordinal))
                {
                    if (fontChanged)
                    {
                        component.ForceMeshUpdate();
                    }

                    return ProcessResult.CacheHit;
                }

                component.text = translation;
                if (fontChanged)
                {
                    component.ForceMeshUpdate();
                }

                return ProcessResult.Applied;
            }
            catch (Exception exception)
            {
                LogExceptionLimited("component", exception);
                return ProcessResult.None;
            }
        }
    }

    private void LoadFallbackFont()
    {
        try
        {
            var gameRoot = new DirectoryInfo(UnityEngine.Application.dataPath).Parent!.FullName;
            var configuredPath = ReadFallbackFontPath(Path.Combine(gameRoot, "AutoTranslator", "Config.ini"));
            var fontPath = Path.Combine(gameRoot, configuredPath ?? Path.Combine("AutoTranslator", "arialuni_sdf_u2022"));
            if (!File.Exists(fontPath))
            {
                LoggerInstance.Warning($"Fallback TMP font not found: {fontPath}");
                return;
            }

            var bundlePtr = LoadFromFileInternal(IL2CPP.ManagedStringToIl2Cpp(fontPath), 0u, 0UL);
            if (bundlePtr == IntPtr.Zero)
            {
                LoggerInstance.Warning($"Could not load fallback TMP font bundle: {fontPath}");
                return;
            }

            // Keep the AssetBundle proxy alive for as long as its font is in use. This
            // matches XUnity's proxy loader and prevents bundle-owned dependencies from
            // losing their native owner while the font asset is still referenced.
            _fontBundle = new UnityEngine.AssetBundle(bundlePtr);

            var arrayPtr = LoadAssetWithSubAssetsInternal(
                bundlePtr,
                IL2CPP.ManagedStringToIl2Cpp(string.Empty),
                Il2CppType.Of<TMP_FontAsset>().Pointer);
            if (arrayPtr == IntPtr.Zero)
            {
                LoggerInstance.Warning($"Fallback TMP font bundle contained no assets: {fontPath}");
                return;
            }

            var assets = new Il2CppReferenceArray<TMP_FontAsset>(arrayPtr);
            if (assets.Length == 0 || assets[0] is null)
            {
                LoggerInstance.Warning($"Fallback TMP font asset is empty; the AssetBundle version may be incompatible, replace the font bundle: {fontPath}");
                return;
            }

            _fontAsset = assets[0];

            // Load every asset in the bundle so the font's dependencies (atlas texture,
            // material, shaders) are instantiated and stay alive. Direct icall loading does
            // not run Unity's dependency-loading pipeline, so the font object can otherwise
            // reference assets that were never loaded, which renders as missing glyph boxes.
            try
            {
                var allPtr = LoadAssetWithSubAssetsInternal(
                    bundlePtr,
                    IL2CPP.ManagedStringToIl2Cpp(string.Empty),
                    Il2CppType.Of<UnityEngine.Object>().Pointer);
                if (allPtr != IntPtr.Zero)
                {
                    var allAssets = new Il2CppReferenceArray<UnityEngine.Object>(allPtr);
                    foreach (var kept in allAssets)
                    {
                        if (kept is not null)
                        {
                            _keptAssets.Add(kept);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                LogExceptionLimited("font-deps", exception);
            }

            PrepareFallbackFont(bundlePtr);
            UnityEngine.Object.DontDestroyOnLoad(_fontAsset);
            LoggerInstance.Msg("Loaded fallback TMP font: " + fontPath);

            RegisterGlobalFallback();
        }
        catch (Exception exception)
        {
            LogExceptionLimited("font", exception);
        }
    }

    private void PrepareFallbackFont(IntPtr bundlePtr)
    {
        if (_fontAsset is null)
        {
            return;
        }

        UnityEngine.Texture2D? atlas = null;
        try
        {
            var atlases = _fontAsset.atlasTextures;
            if (atlases is not null && atlases.Length > 0 && IsAlive(atlases[0]))
            {
                atlas = atlases[0];
            }
            else if (IsAlive(_fontAsset.atlasTexture))
            {
                atlas = _fontAsset.atlasTexture;
            }

            // Only repair the atlas when the font asset's own atlas is missing. TMP
            // computes glyph UVs from the font's serialized atlas size, so re-assigning
            // atlasTextures/atlasWidth/atlasHeight on an already-valid font corrupts
            // every glyph's UV and renders blank text.
            if (!IsAlive(atlas))
            {
                atlas = LoadFirstBundleAsset<UnityEngine.Texture2D>(bundlePtr);
                if (IsAlive(atlas))
                {
                    var atlasArray = new Il2CppReferenceArray<UnityEngine.Texture2D>(1L);
                    atlasArray[0] = atlas!;
                    _fontAsset.atlasTextures = atlasArray!;
                    _fontAsset.m_AtlasTexture = atlas!;
                    _fontAsset.atlasWidth = atlas!.width;
                    _fontAsset.atlasHeight = atlas.height;
                }
            }
            var material = _fontAsset.material;
            if (!IsAlive(material))
            {
                material = LoadFirstBundleAsset<UnityEngine.Material>(bundlePtr);
                if (IsAlive(material))
                {
                    _fontAsset.material = material;
                }
            }

            if (!IsAlive(material) && IsAlive(atlas))
            {
                var shader = FindTmpShader();
                if (IsAlive(shader))
                {
                    material = new UnityEngine.Material(shader!);
                    _fontAsset.material = material;
                    _keptAssets.Add(material);
                }
            }
            else if (IsAlive(material) && !IsAlive(material!.shader))
            {
                var shader = FindTmpShader();
                if (IsAlive(shader))
                {
                    material.shader = shader;
                }
            }

            if (IsAlive(material) && IsAlive(atlas))
            {
                // Do not overwrite the bundle material's own properties (Distance Field
                // shader values are serialized correctly in the AssetBundle). Only make
                // sure the main texture points at a live atlas if it is missing.
                if (!IsAlive(material!.mainTexture))
                {
                    material.mainTexture = atlas;
                }
            }
            else if (!IsAlive(material))
            {
                LoggerInstance.Warning("Fallback TMP font has no usable material.");
            }

            // Asset bundles only serialize the tables. Rebuild TMP's runtime lookup
            // dictionaries after repairing legacy/current atlas fields.
            _fontAsset.ReadFontAssetDefinition();

        }
        catch (Exception exception)
        {
            LogExceptionLimited("font-prepare", exception);
        }
    }

    private T? LoadFirstBundleAsset<T>(IntPtr bundlePtr) where T : UnityEngine.Object
    {
        var arrayPtr = LoadAssetWithSubAssetsInternal(
            bundlePtr,
            IL2CPP.ManagedStringToIl2Cpp(string.Empty),
            Il2CppType.Of<T>().Pointer);
        if (arrayPtr == IntPtr.Zero)
        {
            return null;
        }

        var assets = new Il2CppReferenceArray<T>(arrayPtr);
        foreach (var asset in assets)
        {
            if (IsAlive(asset))
            {
                _keptAssets.Add(asset);
                return asset;
            }
        }

        return null;
    }

    private UnityEngine.Shader? FindTmpShader()
    {
        var shader = UnityEngine.Shader.Find("TextMeshPro/Distance Field");
        if (!IsAlive(shader))
        {
            shader = UnityEngine.Shader.Find("TextMeshPro/Mobile/Distance Field");
        }

        if (IsAlive(shader))
        {
            _keptAssets.Add(shader);
            return shader;
        }

        LoggerInstance.Warning("Could not resolve a TextMesh Pro distance-field shader.");
        return null;
    }

    private static bool IsAlive(UnityEngine.Object? value)
    {
        return value is not null && value.Pointer != IntPtr.Zero;
    }

    private static string? ReadFallbackFontPath(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return null;
        }

        var section = string.Empty;
        foreach (var rawLine in File.ReadLines(configPath, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if (!string.Equals(section, "Behaviour", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || !string.Equals(line[..separator].Trim(), "FallbackFontTextMeshPro", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(separator + 1)..].Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    private void LoadSubstitutions()
    {
        try
        {
            var gameRoot = new DirectoryInfo(UnityEngine.Application.dataPath).Parent!.FullName;
            var translationRoot = Path.Combine(gameRoot, "AutoTranslator", "Translation");
            if (!Directory.Exists(translationRoot))
            {
                return;
            }

            foreach (var langDir in Directory.GetDirectories(translationRoot))
            {
                var textDir = Path.Combine(langDir, "Text");
                var file = Path.Combine(textDir, "_Substitutions.txt");
                if (!File.Exists(file))
                {
                    continue;
                }

                foreach (var rawLine in File.ReadLines(file, Encoding.UTF8))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var key = line[..separator].Trim();
                    var value = line[(separator + 1)..].Trim();
                    _substitutions.Add(new KeyValuePair<string, string>(key, value));
                }
            }

            if (_substitutions.Count > 0)
            {
                LoggerInstance.Msg($"Loaded {_substitutions.Count} template substitutions.");
            }
        }
        catch (Exception exception)
        {
            LogExceptionLimited("substitutions", exception);
        }
    }

    private string RestoreTemplateVariables(string original, string translation)
    {
        try
        {
            var arguments = TemplatizeByReplacementsAndNumbers(original);
            if (arguments is null || arguments.Count == 0)
            {
                return translation;
            }

            foreach (var kvp in arguments)
            {
                translation = translation.Replace(kvp.Key, kvp.Value);
            }
        }
        catch (Exception exception)
        {
            LogExceptionLimited("template-restore", exception);
        }

        return translation;
    }

    private Dictionary<string, string>? TemplatizeByReplacementsAndNumbers(string text)
    {
        var offset = 0;
        string template = text;
        Dictionary<string, string>? arguments = null;

        if (_substitutions.Count > 0)
        {
            var byReplacements = TemplatizeByReplacements(template, _substitutions, ref offset);
            if (byReplacements is not null)
            {
                arguments = byReplacements;
                template = byReplacements["__template__"];
                arguments.Remove("__template__");
            }
        }

        var byNumbers = TemplatizeByNumbers(template, offset);
        if (byNumbers is not null)
        {
            if (arguments is null)
            {
                arguments = new Dictionary<string, string>();
            }

            foreach (var kvp in byNumbers)
            {
                arguments[kvp.Key] = kvp.Value;
            }
        }

        return arguments;
    }

    private static Dictionary<string, string>? TemplatizeByReplacements(string text, List<KeyValuePair<string, string>> replacements, ref int offset)
    {
        var arguments = new Dictionary<string, string>();
        var arg = (char)('A' + offset);
        foreach (var kvp in replacements)
        {
            var original = kvp.Key;
            var replacement = kvp.Value;
            if (string.IsNullOrEmpty(original))
            {
                continue;
            }

            if (string.IsNullOrEmpty(replacement))
            {
                int idx;
                while ((idx = text.IndexOf(original, StringComparison.InvariantCulture)) != -1)
                {
                    text = text.Remove(idx, original.Length);
                }
            }
            else
            {
                string? key = null;
                int idx;
                while ((idx = text.IndexOf(original, StringComparison.InvariantCulture)) != -1)
                {
                    if (key is null)
                    {
                        key = "{{" + arg + "}}";
                        arguments[key] = replacement;
                        arg++;
                    }

                    text = text.Remove(idx, original.Length).Insert(idx, key);
                }
            }
        }

        if (arguments.Count == 0)
        {
            return null;
        }

        offset = arg - 'A';
        arguments["__template__"] = text;
        return arguments;
    }

    private static Dictionary<string, string>? TemplatizeByNumbers(string text, int offset)
    {
        var arguments = new Dictionary<string, string>();
        var arg = (char)('A' + offset);
        var isHandling = false;
        var carg = new StringBuilder();
        var sidx = -1;
        var lidx = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (isHandling)
            {
                if (IsTemplateNumber(c))
                {
                    lidx = i;
                }

                if (IsNumberOrDotOrControl(c))
                {
                    carg.Append(c);
                }
                else
                {
                    var diff = i - lidx - 1;
                    carg.Remove(carg.Length - diff, diff);
                    var variable = carg.ToString();
                    var argName = "{{" + arg + "}}";
                    arguments[argName] = variable;
                    arg++;
                    carg.Clear();
                    isHandling = false;
                    text = text.Remove(sidx, lidx - sidx + 1).Insert(sidx, argName);
                    i += argName.Length - variable.Length;
                }
            }
            else if (IsTemplateNumber(c))
            {
                isHandling = true;
                carg.Clear();
                carg.Append(c);
                sidx = i;
                lidx = i;
            }
        }

        if (carg.Length > 0)
        {
            var diff = text.Length - lidx - 1;
            carg.Remove(carg.Length - diff, diff);
            var variable = carg.ToString();
            var argName = "{{" + arg + "}}";
            arguments[argName] = variable;
            text = text.Remove(sidx, text.Length - sidx - diff).Insert(sidx, argName);
        }

        return arguments.Count > 0 ? arguments : null;
    }

    private static bool IsTemplateNumber(char c)
    {
        return (c >= '0' && c <= '9') || (c >= '０' && c <= '９');
    }

    private static bool IsNumberOrDotOrControl(char c)
    {
        return (c >= '*' && c <= ':') || (c >= '０' && c <= '９');
    }

    private void RegisterGlobalFallback()
    {
        if (_fontAsset is null)
        {
            return;
        }

        try
        {
            var settings = Il2CppTMPro.TMP_Settings.instance;
            if (!IsAlive(settings))
            {
                LogWarningLimited("font-settings", "TMP_Settings.instance is null; skipping global fallback.");
                return;
            }

            var list = settings.m_fallbackFontAssets;
            if (list is null)
            {
                list = new Il2CppSystem.Collections.Generic.List<Il2CppTMPro.TMP_FontAsset>();
                settings.m_fallbackFontAssets = list;
            }

            var contains = false;
            for (var i = 0; i < list.Count; i++)
            {
                var candidate = list[i];
                if (candidate is not null && candidate.Pointer == _fontAsset.Pointer)
                {
                    contains = true;
                    break;
                }
            }

            if (!contains)
            {
                list.Add(_fontAsset);
                LoggerInstance.Msg("Global TMP fallback registered via TMP_Settings.");
            }
            else
            {
                LoggerInstance.Msg("Global TMP fallback already present.");
            }
        }
        catch (Exception exception)
        {
            LogExceptionLimited("font-settings", exception);
        }
    }

    private bool ApplyFallbackFont(TMP_Text component)
    {
        if (_fontAsset is null)
        {
            return false;
        }

        // Do NOT replace component.font or fontSharedMaterial directly: on IL2CPP
        // proxy components the TMP font setter can silently fail while the material
        // swap still lands, leaving the mesh generated from the old font and rendered
        // with the new atlas -- which renders completely blank. Instead, register our
        // CJK font as a TMP fallback on the component's own font so TMP resolves
        // missing glyphs (Chinese) natively and Latin text keeps the original font.
        var changed = false;
        try
        {
            var componentFont = component.font;
            if (!IsAlive(componentFont) || componentFont.Pointer == _fontAsset.Pointer)
            {
                return false;
            }

            var fallbacks = componentFont.fallbackFontAssetTable;
            if (fallbacks is null)
            {
                fallbacks = new Il2CppSystem.Collections.Generic.List<Il2CppTMPro.TMP_FontAsset>();
            }

            var contains = false;
            for (var i = 0; i < fallbacks.Count; i++)
            {
                var candidate = fallbacks[i];
                if (candidate is not null && candidate.Pointer == _fontAsset.Pointer)
                {
                    contains = true;
                    break;
                }
            }

            if (!contains)
            {
                fallbacks.Add(_fontAsset);
                componentFont.fallbackFontAssetTable = fallbacks;
                changed = true;
            }
        }
        catch (Exception exception)
        {
            LogExceptionLimited("font-fallback", exception);
        }

        return changed;
    }

    private NativeHook<T>? CreateNativeHook<T>(string label, Type declaringType, string methodInfoFieldName, T detour)
        where T : Delegate
    {
        try
        {
            var field = declaringType.GetField(methodInfoFieldName, BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingFieldException(declaringType.FullName, methodInfoFieldName);
            if (field.GetValue(null) is not IntPtr methodInfo || methodInfo == IntPtr.Zero)
            {
                throw new InvalidOperationException($"{declaringType.FullName}.{methodInfoFieldName} is null.");
            }

            var target = Marshal.ReadIntPtr(methodInfo);
            if (target == IntPtr.Zero)
            {
                throw new InvalidOperationException($"{declaringType.FullName}.{methodInfoFieldName} has no method pointer.");
            }

            return new NativeHook<T>(target, Marshal.GetFunctionPointerForDelegate(detour));
        }
        catch (Exception exception)
        {
            LoggerInstance.Warning($"Could not prepare {label} hook: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    private void AttachNativeHook<T>(string label, NativeHook<T>? hook) where T : Delegate
    {
        if (hook is null)
        {
            return;
        }

        try
        {
            hook.Attach();
            LoggerInstance.Msg($"Hooked {label}.");
        }
        catch (Exception exception)
        {
            LoggerInstance.Warning($"Could not hook {label}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void DetachNativeHook<T>(string label, NativeHook<T>? hook) where T : Delegate
    {
        if (hook?.IsHooked != true)
        {
            return;
        }

        try
        {
            hook.Detach();
        }
        catch (Exception exception)
        {
            LoggerInstance.Warning($"Could not detach {label}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void LogExceptionLimited(string category, Exception exception)
    {
        var now = DateTime.UtcNow;
        if (_nextErrorLog.TryGetValue(category, out var next) && now < next)
        {
            return;
        }

        _nextErrorLog[category] = now + TimeSpan.FromSeconds(30);
        LoggerInstance.Warning($"{category} error: {exception.GetType().Name}: {exception.Message}");
    }

    private void LogWarningLimited(string category, string message)
    {
        var now = DateTime.UtcNow;
        if (_nextErrorLog.TryGetValue(category, out var next) && now < next)
        {
            return;
        }

        _nextErrorLog[category] = now + TimeSpan.FromSeconds(30);
        LoggerInstance.Warning(message);
    }

    private void Verbose(string message)
    {
        if (_verboseLogging?.Value == true)
        {
            LoggerInstance.Msg(message);
        }
    }

    private static void TextSetterDetour(IntPtr instance, IntPtr value, IntPtr methodInfo)
    {
        var bridge = Instance;
        var hook = bridge?._textSetterHook;
        if (hook is null)
        {
            return;
        }

        hook.Trampoline(instance, value, methodInfo);
        bridge!.ProcessNativeInstance(instance);
    }

    private static void TextMeshProOnEnableDetour(IntPtr instance, IntPtr methodInfo)
    {
        var bridge = Instance;
        var hook = bridge?._textMeshProOnEnableHook;
        if (hook is null)
        {
            return;
        }

        hook.Trampoline(instance, methodInfo);
        bridge!.ProcessNativeInstance(instance);
    }

    private static void TextMeshProUguiOnEnableDetour(IntPtr instance, IntPtr methodInfo)
    {
        var bridge = Instance;
        var hook = bridge?._textMeshProUguiOnEnableHook;
        if (hook is null)
        {
            return;
        }

        hook.Trampoline(instance, methodInfo);
        bridge!.ProcessNativeInstance(instance);
    }

    private void ProcessNativeInstance(IntPtr pointer)
    {
        try
        {
            Process(new TMP_Text(pointer));
        }
        catch (Exception exception)
        {
            LogExceptionLimited("native-hook", exception);
        }
    }

    private sealed record CacheEntry(int InstanceId, string Original, string Translation);

    private enum ProcessResult
    {
        None,
        CacheHit,
        Applied
    }
}
