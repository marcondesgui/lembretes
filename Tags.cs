using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace Lembretes;

/// <summary>Tag colorida de projeto. Cinza = pessoal.</summary>
public enum TagColor
{
    None,
    Orange,
    Green,
    Yellow,
    Gray
}

public static class Tags
{
    public static readonly TagColor[] All = { TagColor.Orange, TagColor.Green, TagColor.Yellow, TagColor.Gray };

    private static readonly Dictionary<TagColor, string> Defaults = new()
    {
        [TagColor.Orange] = "Laranja",
        [TagColor.Green] = "Verde",
        [TagColor.Yellow] = "Amarelo",
        [TagColor.Gray] = "Pessoal",
    };

    private static readonly Dictionary<TagColor, Brush> Brushes = new()
    {
        [TagColor.None] = Frozen("#00000000"),
        [TagColor.Orange] = Frozen("#F2883A"),
        [TagColor.Green] = Frozen("#4CC38A"),
        [TagColor.Yellow] = Frozen("#E9D54F"),
        [TagColor.Gray] = Frozen("#9AA0AA"),
    };

    /// <summary>Nomes personalizados (vêm do config.json).</summary>
    private static Dictionary<string, string> _names = new();

    public static void Load(Dictionary<string, string>? names) => _names = names ?? new();

    public static string NameOf(TagColor t)
    {
        if (t == TagColor.None) return "";
        return _names.TryGetValue(t.ToString(), out var n) && !string.IsNullOrWhiteSpace(n) ? n : Defaults[t];
    }

    public static string DefaultName(TagColor t) => t == TagColor.None ? "" : Defaults[t];

    public static Brush BrushOf(TagColor t) => Brushes[t];

    private static Brush Frozen(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
