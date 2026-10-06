using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using StudyReportEvaluator.App.Workspace;

namespace StudyReportEvaluator.App.Display;

/// <summary>The OS high-contrast colours (Windows system colours) used while high contrast is on.</summary>
public sealed record SystemPalette(
    Color Window,
    Color WindowText,
    Color Highlight,
    Color HighlightText,
    Color ButtonFace,
    Color ButtonText,
    Color GrayText,
    Color HotLight)
{
    public bool IsDark => Luminance(Window) < Luminance(WindowText);

    private static double Luminance(Color color) => (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
}

/// <summary>OS display preferences that Avalonia 12.1.1 does not expose (system colours, text size, animations).</summary>
public interface IOsDisplaySource
{
    SystemPalette? GetSystemPalette();

    /// <summary>The OS text size factor (1 = 100%).</summary>
    double GetTextScaleFactor();

    /// <summary>False when the OS asks applications to turn animations off.</summary>
    bool AreAnimationsEnabled();
}

/// <summary>The settings to apply to one window, from Avalonia's platform colour values and the OS source.</summary>
public sealed record OsDisplayState(
    PlatformThemeVariant ThemeVariant,
    ColorContrastPreference ContrastPreference,
    SystemPalette? Palette,
    double TextScaleFactor,
    bool AnimationsEnabled)
{
    public bool IsHighContrast => ContrastPreference == ColorContrastPreference.High && Palette is not null;
}

/// <summary>
/// NFR-UX-010: follows the OS light/dark mode (the application requests the Default theme variant),
/// Windows high contrast (system colours for text, borders, focus and the Fluent palette), the Windows
/// text size (content zoom that also drives the panel reflow) and the "animation effects" switch.
/// There is no in-app theme switch.
/// </summary>
public static class OsDisplaySettings
{
    public const string HighContrastVariantKey = "StudyReportHighContrast";

    internal static readonly string[] SurfaceKeys =
    [
        "AppCanvasBrush", "AppSurfaceBrush", "AppSurfaceMutedBrush", "AppAccentSoftBrush", "AppCompletedSoftBrush",
        "WarningBackgroundBrush", "TechnicalBackgroundBrush", "AppHeroBrush", "CancelBackgroundBrush",
    ];

    internal static readonly string[] TextKeys =
    [
        "AppTextBrush", "AppMutedTextBrush", "AppBorderBrush", "AppCompletedBrush", "WarningBorderBrush",
        "WarningTextBrush", "TechnicalBorderBrush", "TechnicalTextBrush", "AppHeroMutedBrush", "CancelTextBrush",
        "CancelBorderBrush",
    ];

    internal static readonly string[] HighlightKeys = ["AppAccentBrush", "AppAccentStrongBrush", "FocusRingBrush"];

    private static readonly ThemeVariant HighContrastDark = new(HighContrastVariantKey + "Dark", ThemeVariant.Dark);
    private static readonly ThemeVariant HighContrastLight = new(HighContrastVariantKey + "Light", ThemeVariant.Light);
    private static readonly Styles ReducedMotionStyle = CreateReducedMotionStyle();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Window, List<object>> AppliedKeys = [];

    /// <summary>Applies the current OS state to the window and follows later colour changes.</summary>
    public static void Attach(Window window, IOsDisplaySource? source = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        IOsDisplaySource os = source ?? (OperatingSystem.IsWindows() ? new WindowsDisplaySource() : NullDisplaySource.Instance);
        IPlatformSettings? platform = Application.Current?.PlatformSettings;
        void Update() => Apply(window, Read(platform, os));
        void HandleChanged(object? sender, PlatformColorValues e) => Avalonia.Threading.Dispatcher.UIThread.Post(Update);
        Update();
        if (platform is { } settings)
        {
            settings.ColorValuesChanged += HandleChanged;
            window.Closed += (_, _) => settings.ColorValuesChanged -= HandleChanged;
        }
    }

    public static OsDisplayState Read(IPlatformSettings? settings, IOsDisplaySource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        PlatformColorValues values = settings?.GetColorValues() ?? new PlatformColorValues();
        return new OsDisplayState(
            values.ThemeVariant,
            values.ContrastPreference,
            values.ContrastPreference == ColorContrastPreference.High ? source.GetSystemPalette() : null,
            source.GetTextScaleFactor(),
            source.AreAnimationsEnabled());
    }

    public static void Apply(Window window, OsDisplayState state)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(state);
        ApplyContrast(window, state);
        ApplyMotion(window, state.AnimationsEnabled);
        ApplyTextScale(window, state.TextScaleFactor);
    }

    private static void ApplyContrast(Window window, OsDisplayState state)
    {
        foreach (object key in AppliedKeys.GetOrCreateValue(window))
        {
            window.Resources.Remove(key);
        }

        AppliedKeys.GetOrCreateValue(window).Clear();

        if (!state.IsHighContrast || state.Palette is not { } palette)
        {
            window.ClearValue(ThemeVariantScope.RequestedThemeVariantProperty);
            return;
        }

        List<object> applied = AppliedKeys.GetOrCreateValue(window);
        void Set(string key, Color color)
        {
            window.Resources[key] = new SolidColorBrush(color);
            applied.Add(key);
        }

        foreach (string key in SurfaceKeys)
        {
            Set(key, palette.Window);
        }

        foreach (string key in TextKeys)
        {
            Set(key, palette.WindowText);
        }

        foreach (string key in HighlightKeys)
        {
            Set(key, palette.Highlight);
        }

        Set("AppOnAccentBrush", palette.HighlightText);
        ThemeVariant variant = palette.IsDark ? HighContrastDark : HighContrastLight;
        ApplyFluentBrushes(window, palette, palette.IsDark ? ThemeVariant.Dark : ThemeVariant.Light, applied);
        window.RequestedThemeVariant = variant;
    }

    // Every solid Fluent brush (text, borders, backgrounds, selection, focus) is replaced with the OS
    // system colour that plays the same role, so Fluent controls follow the high-contrast theme too.
    private static void ApplyFluentBrushes(Window window, SystemPalette palette, ThemeVariant baseVariant, List<object> applied)
    {
        if (Application.Current is not { } application
            || application.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().FirstOrDefault() is not { } fluent)
        {
            return;
        }

        HashSet<object> keys = [];
        CollectKeys(fluent.Resources, keys);
        bool dark = baseVariant == ThemeVariant.Dark;
        foreach (object key in keys)
        {
            if (window.Resources.ContainsKey(key)
                || !application.TryGetResource(key, baseVariant, out object? value)
                || value is not ISolidColorBrush { Color.A: > 0 } brush)
            {
                continue;
            }

            Color color = brush.Color;
            int chroma = Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));
            double luminance = (0.2126 * color.R) + (0.7152 * color.G) + (0.0722 * color.B);
            bool backgroundLike = dark ? luminance < 128d : luminance >= 128d;
            Color mapped = chroma > 60 ? palette.Highlight : backgroundLike ? palette.Window : palette.WindowText;
            window.Resources[key] = new SolidColorBrush(mapped);
            applied.Add(key);
        }
    }

    private static void CollectKeys(IResourceDictionary dictionary, HashSet<object> keys)
    {
        foreach (KeyValuePair<object, object?> entry in dictionary)
        {
            keys.Add(entry.Key);
        }

        foreach (IResourceProvider merged in dictionary.MergedDictionaries)
        {
            if (merged is IResourceDictionary child)
            {
                CollectKeys(child, keys);
            }
        }

        foreach (IThemeVariantProvider theme in dictionary.ThemeDictionaries.Values)
        {
            if (theme is IResourceDictionary child)
            {
                CollectKeys(child, keys);
            }
        }
    }

    private static Styles CreateReducedMotionStyle()
    {
        Style controls = new(selector => selector.Is<Control>());
        controls.Setters.Add(new Setter(Animatable.TransitionsProperty, null));
        Style templateParts = new(selector => selector.Is<TemplatedControl>().Template().Is<Control>());
        templateParts.Setters.Add(new Setter(Animatable.TransitionsProperty, null));
        return [controls, templateParts];
    }

    private static void ApplyMotion(Window window, bool animationsEnabled)
    {
        bool applied = window.Styles.Contains(ReducedMotionStyle);
        if (!animationsEnabled && !applied)
        {
            window.Styles.Add(ReducedMotionStyle);
        }
        else if (animationsEnabled && applied)
        {
            window.Styles.Remove(ReducedMotionStyle);
        }
    }

    private static void ApplyTextScale(Window window, double factor)
    {
        double scale = double.IsFinite(factor) ? Math.Clamp(factor, 1d, 2.25d) : 1d;
        PanelWorkspace.SetContentScale(window, scale);
        if (scale > 1d && window.Content is Control content and not LayoutTransformControl)
        {
            // Everything grows with the text, so the panels reflow as at a higher display scale.
            window.Content = null;
            window.Content = new LayoutTransformControl { Child = content, LayoutTransform = new ScaleTransform(scale, scale) };
        }
        else if (window.Content is LayoutTransformControl zoom)
        {
            zoom.LayoutTransform = new ScaleTransform(scale, scale);
        }
    }

    private sealed class NullDisplaySource : IOsDisplaySource
    {
        public static NullDisplaySource Instance { get; } = new();

        public SystemPalette? GetSystemPalette() => null;

        public double GetTextScaleFactor() => 1d;

        public bool AreAnimationsEnabled() => true;
    }
}

/// <summary>Windows system colours, "Text size" and "Animation effects" (read only; nothing is changed).</summary>
internal sealed class WindowsDisplaySource : IOsDisplaySource
{
    private const int ColorWindow = 5;
    private const int ColorWindowText = 8;
    private const int ColorHighlight = 13;
    private const int ColorHighlightText = 14;
    private const int ColorButtonFace = 15;
    private const int ColorGrayText = 17;
    private const int ColorButtonText = 18;
    private const int ColorHotLight = 26;
    private const uint SpiGetClientAreaAnimation = 0x1042;

    public SystemPalette? GetSystemPalette()
    {
        try
        {
            return new SystemPalette(
                Read(ColorWindow), Read(ColorWindowText), Read(ColorHighlight), Read(ColorHighlightText),
                Read(ColorButtonFace), Read(ColorButtonText), Read(ColorGrayText), Read(ColorHotLight));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public double GetTextScaleFactor()
    {
        if (!OperatingSystem.IsWindows())
        {
            return 1d;
        }

        try
        {
            using Microsoft.Win32.RegistryKey? key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Accessibility");
            return key?.GetValue("TextScaleFactor") is int percent && percent is >= 100 and <= 225 ? percent / 100d : 1d;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return 1d;
        }
    }

    public bool AreAnimationsEnabled()
    {
        try
        {
            return !SystemParametersInfo(SpiGetClientAreaAnimation, 0, out int enabled, 0) || enabled != 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            return true;
        }
    }

    private static Color Read(int index)
    {
        uint value = GetSysColor(index);
        return Color.FromRgb((byte)(value & 0xFF), (byte)((value >> 8) & 0xFF), (byte)((value >> 16) & 0xFF));
    }

    [DllImport("user32.dll")]
    private static extern uint GetSysColor(int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, out int pvParam, uint fWinIni);
}
