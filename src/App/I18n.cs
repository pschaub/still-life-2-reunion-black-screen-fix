using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;

namespace StillLife2Patcher;

static class I18n
{
    static readonly Dictionary<string, Dictionary<string, string>> All = Load();
    public static readonly (string Code, string Name)[] Languages = [("en", "English"), ("de", "Deutsch"), ("fr", "Français"), ("es", "Español"), ("it", "Italiano"), ("pt", "Português"), ("pl", "Polski"), ("ru", "Русский"), ("zh-Hans", "简体中文"), ("ja", "日本語"), ("ko", "한국어")];
    public static string Language { get; set; } = DetectLanguage(CultureInfo.CurrentUICulture);
    public static string DetectLanguage(CultureInfo culture)
    {
        if (culture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) || culture.Name.Equals("zh-SG", StringComparison.OrdinalIgnoreCase) || culture.Name.Contains("Hans", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        return Languages.Any(x => x.Code == culture.TwoLetterISOLanguageName) ? culture.TwoLetterISOLanguageName : "en";
    }
    static Dictionary<string, Dictionary<string, string>> Load()
    {
        var a = typeof(I18n).Assembly; using var s = a.GetManifestResourceStream(a.GetManifestResourceNames().Single(x => x.EndsWith("strings.json")))!;
        return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(s)!;
    }
    public static string T(string key, params object[] args)
    {
        string value = All.TryGetValue(Language, out var lang) && lang.TryGetValue(key, out var v) ? v : All["en"][key];
        return args.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, args);
    }
    public static string Validate()
    {
        foreach (var (culture, expected) in new[] { ("de-DE", "de"), ("ja-JP", "ja"), ("ko-KR", "ko"), ("pt-BR", "pt"), ("zh-CN", "zh-Hans"), ("zh-TW", "en"), ("tr-TR", "en"), ("en-GB", "en") })
            if (DetectLanguage(CultureInfo.GetCultureInfo(culture)) != expected) throw new InvalidDataException("Language detection failed: " + culture);
        foreach (var (code, _) in Languages)
        {
            if (!All.TryGetValue(code, out var values)) throw new InvalidDataException("Missing language " + code);
            if (!All["en"].Keys.Order().SequenceEqual(values.Keys.Order())) throw new InvalidDataException("Missing translation keys: " + code);
            foreach (var (key, text) in All["en"])
            {
                string Fields(string s) => string.Join(",", Regex.Matches(s, @"\{\d+\}").Select(x => x.Value).Order());
                if (string.IsNullOrWhiteSpace(values[key]) || Fields(text) != Fields(values[key])) throw new InvalidDataException("Invalid translation: " + code + "/" + key);
            }
        }
        return $"{Languages.Length} languages; {All["en"].Count} keys per language; placeholders and system-language/fallback selection valid.";
    }
}
