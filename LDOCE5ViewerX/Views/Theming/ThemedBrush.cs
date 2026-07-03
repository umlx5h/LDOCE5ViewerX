using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace LDOCE5ViewerX.Views.Theming;

/// <summary>
/// Stores cached light and dark brushes for theme-aware view rendering.
/// </summary>
/// <param name="Light">Brush used by light theme rendering.</param>
/// <param name="Dark">Brush used by dark theme rendering.</param>
public readonly record struct ThemedBrush(IBrush Light, IBrush Dark);

/// <summary>
/// Creates and selects theme-aware brushes for custom view rendering.
/// </summary>
public static class ThemedBrushes
{
    /// <summary>
    /// Creates a themed brush from light and dark hexadecimal color strings.
    /// </summary>
    /// <param name="lightHex">Light theme color in hexadecimal notation.</param>
    /// <param name="darkHex">Dark theme color in hexadecimal notation.</param>
    /// <returns>The cached themed brush.</returns>
    public static ThemedBrush Create(string lightHex, string darkHex) =>
        new(CreateBrush(lightHex), CreateBrush(darkHex));

    /// <summary>
    /// Creates a themed brush from a light brush and a dark hexadecimal color string.
    /// </summary>
    /// <param name="light">Light theme brush.</param>
    /// <param name="darkHex">Dark theme color in hexadecimal notation.</param>
    /// <returns>The cached themed brush.</returns>
    public static ThemedBrush Create(IBrush light, string darkHex) =>
        new(light, CreateBrush(darkHex));

    /// <summary>
    /// Creates a themed brush from a light hexadecimal color string and a dark brush.
    /// </summary>
    /// <param name="lightHex">Light theme color in hexadecimal notation.</param>
    /// <param name="dark">Dark theme brush.</param>
    /// <returns>The cached themed brush.</returns>
    public static ThemedBrush Create(string lightHex, IBrush dark) =>
        new(CreateBrush(lightHex), dark);

    /// <summary>
    /// Creates a themed brush from light and dark brushes.
    /// </summary>
    /// <param name="light">Light theme brush.</param>
    /// <param name="dark">Dark theme brush.</param>
    /// <returns>The cached themed brush.</returns>
    public static ThemedBrush Create(IBrush light, IBrush dark) => new(light, dark);

    /// <summary>
    /// Creates a cached brush from a hexadecimal color string.
    /// </summary>
    /// <param name="hex">Color in hexadecimal notation.</param>
    /// <returns>The created brush.</returns>
    public static IBrush CreateBrush(string hex) => new SolidColorBrush(CreateColor(hex));

    /// <summary>
    /// Creates a color from a hexadecimal color string.
    /// </summary>
    /// <param name="hex">Color in hexadecimal notation.</param>
    /// <returns>The created color.</returns>
    public static Color CreateColor(string hex) => Color.Parse(hex);

    /// <summary>
    /// Selects the brush matching a styled element's actual theme.
    /// </summary>
    /// <param name="brush">The themed brush to select from.</param>
    /// <param name="element">Styled element whose actual theme is used.</param>
    /// <returns>The light or dark brush.</returns>
    public static IBrush Select(this ThemedBrush brush, StyledElement element) =>
        element.ActualThemeVariant == ThemeVariant.Dark ? brush.Dark : brush.Light;
}
