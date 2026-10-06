using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
namespace NoMoreBacknoise;
public static class Localization {
    public static readonly Dictionary<string,string> Languages = new() {{"en","English"},{"zh-CN","简体中文"},{"es","Español"},{"hi","हिन्दी"},{"ar","العربية"},{"pt-BR","Português (Brasil)"},{"fr","Français"},{"ru","Русский"},{"ja","日本語"}};
    private static Dictionary<string,Dictionary<string,string>>? _strings;
    private static Dictionary<string,string> _current = new();
    public static string Current { get; private set; } = "en";
    public static string T(string key) => _current.GetValueOrDefault(key, _strings?.GetValueOrDefault("en")?.GetValueOrDefault(key, key) ?? key);
    public static void Load(string locale) {
        _strings ??= JsonSerializer.Deserialize<Dictionary<string,Dictionary<string,string>>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Localization","strings.json")))!;
        Current = Languages.ContainsKey(locale) ? locale : "en"; _current = _strings[Current];
        foreach (var (key,value) in _strings["en"]) Application.Current.Resources["t."+key] = _current.GetValueOrDefault(key,value);
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(Current); CultureInfo.CurrentUICulture = CultureInfo.CurrentCulture;
    }
}
