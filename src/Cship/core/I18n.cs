using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Cship.Core;

/// <summary>
/// 文案（步骤 1.6）：加载 dependencies\resources\i18n\{lang}.json。
/// 缺失键回退英文，再回退键名本身；Reload()/SetLanguage() 支持热切换。
/// </summary>
public static class I18n
{
    static readonly object Gate = new();
    static Dictionary<string, string> _current = new();
    static Dictionary<string, string> _english = new();

    public static string CurrentLanguage { get; private set; } = "zh-CN";

    /// <summary>语言切换完成（UI 订阅后整体刷新文案）。</summary>
    public static event Action? LanguageChanged;

    public static void Init(string language)
    {
        _english = LoadFile("en-US");
        Apply(language);
    }

    /// <summary>按当前语言重新加载（外置 JSON 可编辑，改完即热生效）。</summary>
    public static void Reload()
    {
        Apply(CurrentLanguage);
        LanguageChanged?.Invoke();
    }

    public static void SetLanguage(string language)
    {
        Apply(language);
        LanguageChanged?.Invoke();
    }

    /// <summary>取文案：当前语言 → 英文 → 键名本身。</summary>
    public static string Tr(string key)
    {
        lock (Gate)
        {
            if (_current.TryGetValue(key, out var v)) return v;
            if (_english.TryGetValue(key, out var en)) return en;
        }
        return key;
    }

    static void Apply(string language)
    {
        lock (Gate)
        {
            CurrentLanguage = language;
            _current = string.Equals(language, "en-US", StringComparison.OrdinalIgnoreCase)
                ? new Dictionary<string, string>(_english)
                : LoadFile(language);
        }
    }

    static Dictionary<string, string> LoadFile(string lang)
    {
        var path = Path.Combine(Paths.I18nDir(), lang + ".json");
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var dict = new Dictionary<string, string>();
            foreach (var p in doc.RootElement.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.String)
                    dict[p.Name] = p.Value.GetString() ?? string.Empty;
            return dict;
        }
        catch (Exception ex)
        {
            Logger.Warn($"i18n 加载失败 {lang}：{ex.Message}");
            return new Dictionary<string, string>();
        }
    }
}
