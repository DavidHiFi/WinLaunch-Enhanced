using WpfColor = System.Windows.Media.Color;

namespace WinLaunch.Enhanced.Theme;

// Role table mirroring the FreeDictate/T3 theme collection so every house app
// speaks the same visual language. Roles: bg window, card tiles and panels,
// field inputs, hover/press states, text, sub secondary text, accent primary
// actions and focus, on_accent text on accent, accent_hover, warn destructive
// text, dark native-dark-mode flag. CardBorder is always one step above card.
public sealed class HouseTheme
{
    public string Name;
    public string Bg, Card, Field, Hover, Press, Text, Sub;
    public string Accent, OnAccent, AccentHover, Warn;
    public bool Dark;
    public string CardBorder => Hover;
}

public static class Themes
{
    public static readonly Dictionary<string, HouseTheme> All = new()
    {
        ["T3 Dark"] = new HouseTheme { Name = "T3 Dark",
            Bg = "#09090B", Card = "#18181B", Field = "#27272A", Hover = "#3F3F46",
            Press = "#52525B", Text = "#FAFAFA", Sub = "#A1A1AA", Accent = "#FAFAFA",
            OnAccent = "#18181B", AccentHover = "#D4D4D8", Warn = "#F38BA8", Dark = true },
        ["T3 Light"] = new HouseTheme { Name = "T3 Light",
            Bg = "#FCFCFD", Card = "#F4F4F5", Field = "#E4E4E7", Hover = "#D4D4D8",
            Press = "#A1A1AA", Text = "#18181B", Sub = "#52525B", Accent = "#18181B",
            OnAccent = "#FAFAFA", AccentHover = "#3F3F46", Warn = "#D20F39", Dark = false },
        ["Catppuccin Mocha"] = new HouseTheme { Name = "Catppuccin Mocha",
            Bg = "#1E1E2E", Card = "#313244", Field = "#45475A", Hover = "#45475A",
            Press = "#585B70", Text = "#CDD6F4", Sub = "#BAC2DE", Accent = "#89B4FA",
            OnAccent = "#1E1E2E", AccentHover = "#B4BEFE", Warn = "#F38BA8", Dark = true },
        ["Catppuccin Latte"] = new HouseTheme { Name = "Catppuccin Latte",
            Bg = "#EFF1F5", Card = "#E6E9EF", Field = "#CCD0DA", Hover = "#BCC0CC",
            Press = "#ACB0BE", Text = "#4C4F69", Sub = "#5C5F77", Accent = "#1E66F5",
            OnAccent = "#FFFFFF", AccentHover = "#7287FD", Warn = "#D20F39", Dark = false },
        ["Tokyo Night"] = new HouseTheme { Name = "Tokyo Night",
            Bg = "#1A1B26", Card = "#24283B", Field = "#343A55", Hover = "#414868",
            Press = "#565F89", Text = "#C0CAF5", Sub = "#A9B1D6", Accent = "#7AA2F7",
            OnAccent = "#1A1B26", AccentHover = "#89DDFF", Warn = "#F7768E", Dark = true },
        ["Nord"] = new HouseTheme { Name = "Nord",
            Bg = "#2E3440", Card = "#3B4252", Field = "#434C5E", Hover = "#4C566A",
            Press = "#616E88", Text = "#ECEFF4", Sub = "#D8DEE9", Accent = "#88C0D0",
            OnAccent = "#2E3440", AccentHover = "#8FBCBB", Warn = "#EBCB8B", Dark = true },
        ["Rose Pine"] = new HouseTheme { Name = "Rose Pine",
            Bg = "#191724", Card = "#1F1D2E", Field = "#26233A", Hover = "#403D52",
            Press = "#524F67", Text = "#E0DEF4", Sub = "#908CAA", Accent = "#C4A7E7",
            OnAccent = "#191724", AccentHover = "#EBBCBA", Warn = "#EB6F92", Dark = true },
    };

    public const string Default = "Catppuccin Mocha";

    // Migrates the 1.0 theme.mode values ("Dark"/"Light") to theme names.
    public static string Resolve(string stored)
    {
        if (stored == null) return Default;
        stored = stored.Trim();
        if (All.ContainsKey(stored)) return stored;
        if (stored == "Dark") return Default;
        if (stored == "Light") return "Catppuccin Latte";
        return Default;
    }

    public static WpfColor C(string hex) =>
        (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(hex);
}
