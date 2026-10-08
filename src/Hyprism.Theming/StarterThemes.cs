namespace Hyprism.Theming;

/// <summary>Built-in palettes. Values come from each project's published terminal scheme.</summary>
public static class StarterThemes
{
    public static readonly Palette CatppuccinMocha = Palette.FromHex("Catppuccin Mocha", "#1E1E2E", "#CDD6F4", "#CBA6F7",
        "#45475A #F38BA8 #A6E3A1 #F9E2AF #89B4FA #F5C2E7 #94E2D5 #BAC2DE #585B70 #F38BA8 #A6E3A1 #F9E2AF #89B4FA #F5C2E7 #94E2D5 #A6ADC8");

    public static readonly Palette CatppuccinLatte = Palette.FromHex("Catppuccin Latte", "#EFF1F5", "#4C4F69", "#8839EF",
        "#5C5F77 #D20F39 #40A02B #DF8E1D #1E66F5 #EA76CB #179299 #ACB0BE #6C6F85 #D20F39 #40A02B #DF8E1D #1E66F5 #EA76CB #179299 #BCC0CC", dark: false);

    public static readonly Palette TokyoNight = Palette.FromHex("Tokyo Night", "#1A1B26", "#C0CAF5", "#7AA2F7",
        "#15161E #F7768E #9ECE6A #E0AF68 #7AA2F7 #BB9AF7 #7DCFFF #A9B1D6 #414868 #F7768E #9ECE6A #E0AF68 #7AA2F7 #BB9AF7 #7DCFFF #C0CAF5");

    public static readonly Palette Nord = Palette.FromHex("Nord", "#2E3440", "#D8DEE9", "#88C0D0",
        "#3B4252 #BF616A #A3BE8C #EBCB8B #81A1C1 #B48EAD #88C0D0 #E5E9F0 #4C566A #BF616A #A3BE8C #EBCB8B #81A1C1 #B48EAD #8FBCBB #ECEFF4");

    public static readonly Palette Gruvbox = Palette.FromHex("Gruvbox", "#282828", "#EBDBB2", "#FE8019",
        "#282828 #CC241D #98971A #D79921 #458588 #B16286 #689D6A #A89984 #928374 #FB4934 #B8BB26 #FABD2F #83A598 #D3869B #8EC07C #EBDBB2");

    public static readonly Palette RosePine = Palette.FromHex("Rosé Pine", "#191724", "#E0DEF4", "#C4A7E7",
        "#26233A #EB6F92 #31748F #F6C177 #9CCFD8 #C4A7E7 #EBBCBA #E0DEF4 #6E6A86 #EB6F92 #31748F #F6C177 #9CCFD8 #C4A7E7 #EBBCBA #E0DEF4");

    public static readonly Palette RosePineDawn = Palette.FromHex("Rosé Pine Dawn", "#FAF4ED", "#575279", "#907AA9",
        "#F2E9E1 #B4637A #286983 #EA9D34 #56949F #907AA9 #D7827E #575279 #9893A5 #B4637A #286983 #EA9D34 #56949F #907AA9 #D7827E #575279", dark: false);

    public static IReadOnlyList<Palette> All { get; } = [CatppuccinMocha, TokyoNight, Nord, Gruvbox, RosePine, CatppuccinLatte, RosePineDawn];
}
