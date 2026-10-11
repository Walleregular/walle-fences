using System.Globalization;
using System.Text.Json;

namespace WalleFences.Core;

public static class L
{
    public static readonly string[] Codes = ["uk", "en", "ru"];
    public static readonly string[] Names = ["Українська", "English", "Русский"];
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = LoadCatalogs();
    public static string Language { get; private set; } = "ru";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Language == "en" ? "en-US" : Language == "uk" ? "uk-UA" : "ru-RU");
    public static string Normalize(string? code) => code != null && Codes.Contains(code, StringComparer.Ordinal) ? code : "ru";
    public static void SetLanguage(string? code) { Language = Normalize(code); }
    public static string Text(string key) => Catalogs[Language].TryGetValue(key, out var value) ? value : Catalogs["ru"].GetValueOrDefault(key, key);
    public static string Format(string key, params object?[] values) => string.Format(Culture, Text(key), values);
    public static IReadOnlyDictionary<string, string> Catalog(string code) => Catalogs[Normalize(code)];
    private static Dictionary<string, Dictionary<string, string>> LoadCatalogs()
    {
        var catalogs = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var code in Codes)
        {
            using var stream = typeof(L).Assembly.GetManifestResourceStream($"WalleFences.Core.Resources.Strings.{code}.json")
                ?? throw new InvalidOperationException($"Missing language resource: {code}");
            catalogs.Add(code, JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? throw new InvalidDataException($"Invalid language resource: {code}"));
        }
        return catalogs;
    }
}
