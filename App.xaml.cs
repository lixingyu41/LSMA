using LSMA.Models;
using LSMA.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LSMA;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
        Services = new AppServices();
    }

    public static new App Current => (App)Application.Current;

    public AppServices Services { get; }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        await Services.InitializeAppearanceAsync();
        _window = new MainWindow();
        Services.Platform.AttachWindow(_window);
        ApplyAppearance(Services.Settings.Current.Theme, Services.Settings.Current.Palette);
        _window.Activate();
    }

    public void ApplyAppearance(AppTheme theme, AppPalette palette)
    {
        ApplyPalette(palette);
        _window?.ApplyAppearance(theme, palette);
    }

    private void ApplyPalette(AppPalette palette)
    {
        var definition = PaletteDefinition.Get(palette);
        var colors = Resources.MergedDictionaries.First(dictionary => dictionary.ContainsKey("AccentBrush"));
        SetBrush(colors, "AccentBrush", definition.Accent);
        SetBrush(colors, "SuccessBrush", definition.Success);
        SetBrush(colors, "WarningBrush", definition.Warning);
        SetBrush(colors, "DangerBrush", definition.Danger);
        Resources["SystemAccentColor"] = definition.Accent;
        SetBrush(Resources, "AccentFillColorDefaultBrush", definition.Accent);
        SetBrush(Resources, "AccentFillColorSecondaryBrush", definition.Accent);
        SetBrush(Resources, "AccentFillColorTertiaryBrush", definition.Accent);
        SetBrush(Resources, "AccentFillColorDisabledBrush", definition.Accent);

        ApplyScheme((ResourceDictionary)colors.ThemeDictionaries["Dark"], definition.Dark, definition.Accent);
        ApplyScheme((ResourceDictionary)colors.ThemeDictionaries["Default"], definition.Dark, definition.Accent);
        ApplyScheme((ResourceDictionary)colors.ThemeDictionaries["Light"], definition.Light, definition.Accent);
    }

    public AppTheme GetSystemTheme()
    {
        var background = new Windows.UI.ViewManagement.UISettings()
            .GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        var luminance = (0.2126 * background.R) + (0.7152 * background.G) + (0.0722 * background.B);
        return luminance < 128 ? AppTheme.Dark : AppTheme.Light;
    }

    public AppTheme GetDisplayedTheme()
    {
        return _window?.GetDisplayedTheme() ?? GetSystemTheme();
    }

    private static void ApplyScheme(ResourceDictionary dictionary, ThemeScheme scheme, Color accent)
    {
        SetBrush(dictionary, "AppBackgroundBrush", scheme.Background);
        SetBrush(dictionary, "TitleBarBackgroundBrush", scheme.TitleBar);
        SetBrush(dictionary, "NavigationViewDefaultPaneBackground", scheme.Pane);
        SetBrush(dictionary, "NavigationViewExpandedPaneBackground", scheme.Pane);
        SetBrush(dictionary, "NavigationViewContentBackground", scheme.Background);
        SetBrush(dictionary, "NavigationViewContentGridBorderBrush", scheme.Border);
        SetBrush(dictionary, "NavigationViewItemBackgroundSelected", scheme.AccentSoft);
        SetBrush(dictionary, "NavigationViewItemBackgroundSelectedPointerOver", scheme.AccentSoft);
        SetBrush(dictionary, "NavigationViewItemBackgroundSelectedPressed", scheme.AccentSoft);
        SetBrush(dictionary, "NavigationViewItemForegroundSelected", accent);
        SetBrush(dictionary, "NavigationViewItemForegroundSelectedPointerOver", accent);
        SetBrush(dictionary, "NavigationViewItemForegroundSelectedPressed", accent);
        SetBrush(dictionary, "CardBackgroundBrush", scheme.Card);
        SetBrush(dictionary, "CardBorderBrush", scheme.Border);
        SetBrush(dictionary, "PrimaryTextBrush", scheme.PrimaryText);
        SetBrush(dictionary, "SecondaryTextBrush", scheme.SecondaryText);
        SetBrush(dictionary, "MutedTextBrush", scheme.MutedText);
        SetBrush(dictionary, "AccentSoftBrush", scheme.AccentSoft);
    }

    private static void SetBrush(ResourceDictionary dictionary, string key, Color color)
    {
        if (dictionary[key] is not SolidColorBrush brush)
        {
            throw new InvalidOperationException($"Palette resource '{key}' must be an existing SolidColorBrush.");
        }

        brush.Color = color;
    }

    private sealed record ThemeScheme(
        Color Background,
        Color TitleBar,
        Color Pane,
        Color Card,
        Color Border,
        Color PrimaryText,
        Color SecondaryText,
        Color MutedText,
        Color AccentSoft);

    private sealed record PaletteDefinition(
        Color Accent,
        Color Success,
        Color Warning,
        Color Danger,
        ThemeScheme Dark,
        ThemeScheme Light)
    {
        public static PaletteDefinition Get(AppPalette palette) => palette switch
        {
            AppPalette.Junimo => new PaletteDefinition(
                Hex("#78C850"), Hex("#65B845"), Hex("#E5B64A"), Hex("#E05B57"),
                new ThemeScheme(Hex("#0E1511"), Hex("#121B16"), Hex("#121B16"), Hex("#17211B"), Hex("#29372D"), Hex("#F0F5F1"), Hex("#ABB8AE"), Hex("#718176"), Hex("#223622")),
                new ThemeScheme(Hex("#F1F6EE"), Hex("#E8F1E3"), Hex("#E8F1E3"), Hex("#FFFFFF"), Hex("#D7E5CF"), Hex("#202A23"), Hex("#58665A"), Hex("#7A897C"), Hex("#E1F0D9"))),
            AppPalette.Moonlight => new PaletteDefinition(
                Hex("#69B4E0"), Hex("#53B889"), Hex("#E5B64A"), Hex("#EA6670"),
                new ThemeScheme(Hex("#0D131A"), Hex("#111A24"), Hex("#111A24"), Hex("#16212C"), Hex("#283849"), Hex("#F1F5FA"), Hex("#A6B5C5"), Hex("#708296"), Hex("#192F42")),
                new ThemeScheme(Hex("#F0F5F8"), Hex("#E6EEF4"), Hex("#E6EEF4"), Hex("#FFFFFF"), Hex("#D4E0EA"), Hex("#202832"), Hex("#586774"), Hex("#778997"), Hex("#DDECF5"))),
            AppPalette.Cranberry => new PaletteDefinition(
                Hex("#DF7086"), Hex("#58B67A"), Hex("#E9B949"), Hex("#EF5961"),
                new ThemeScheme(Hex("#171114"), Hex("#1C1418"), Hex("#1C1418"), Hex("#241A1F"), Hex("#3B2931"), Hex("#F7F1F3"), Hex("#C3A8B0"), Hex("#8F737D"), Hex("#41202A")),
                new ThemeScheme(Hex("#FAF2F4"), Hex("#F4E6EA"), Hex("#F4E6EA"), Hex("#FFFFFF"), Hex("#EAD5DB"), Hex("#302327"), Hex("#725860"), Hex("#937580"), Hex("#F5DCE2"))),
            _ => new PaletteDefinition(
                Hex("#F2C94C"), Hex("#4CAF50"), Hex("#F2C94C"), Hex("#EF5350"),
                new ThemeScheme(Hex("#101014"), Hex("#14151A"), Hex("#14151A"), Hex("#1A1B20"), Hex("#2A2C33"), Hex("#F4F4F5"), Hex("#A9ABB3"), Hex("#747782"), Hex("#3A321A")),
                new ThemeScheme(Hex("#F6F3EA"), Hex("#F0EBDE"), Hex("#F0EBDE"), Hex("#FFFFFF"), Hex("#E5DFD1"), Hex("#252525"), Hex("#626262"), Hex("#79736B"), Hex("#FBF1D2")))
        };

        private static Color Hex(string value)
        {
            var bytes = Convert.FromHexString(value[1..]);
            return Color.FromArgb(255, bytes[0], bytes[1], bytes[2]);
        }
    }
}
