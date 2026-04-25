using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AudioClient.GUI.Views;

public class BoolToBrushConverter : IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    private static readonly Dictionary<string, IBrush> _brushes = new()
    {
        ["Green"] = new SolidColorBrush(Color.Parse("#43b581")),
        ["GreenDim"] = new SolidColorBrush(Color.Parse("#3043b581")),
        ["TextMuted"] = new SolidColorBrush(Color.Parse("#72767d")),
        ["TextPrimary"] = new SolidColorBrush(Color.Parse("#dcddde")),
        ["Accent"] = new SolidColorBrush(Color.Parse("#7289da")),
        ["Red"] = new SolidColorBrush(Color.Parse("#f04747")),
        ["Transparent"] = Brushes.Transparent,
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool boolValue = value is bool b && b;
        string param = parameter as string ?? "Green:TextMuted";
        var parts = param.Split(':');
        string key = boolValue ? parts[0] : (parts.Length > 1 ? parts[1] : "TextPrimary");
        return _brushes.GetValueOrDefault(key, Brushes.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class StringNotEmptyConverter : IValueConverter
{
    public static readonly StringNotEmptyConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class NotZeroConverter : IValueConverter
{
    public static readonly NotZeroConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i > 0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

// Returns Accent brush when value string matches ConverterParameter, else BgDark
public class StringMatchBrushConverter : IValueConverter
{
    public static readonly StringMatchBrushConverter Instance = new();

    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#7289da"));
    private static readonly IBrush BgDark = new SolidColorBrush(Color.Parse("#202225"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal)
            ? Accent : BgDark;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToStringConverter : IValueConverter
{
    public static readonly BoolToStringConverter SignInButtonLabel = new("Verify TOTP", "Sign In");

    private readonly string _trueText;
    private readonly string _falseText;

    public BoolToStringConverter(string trueText, string falseText)
    {
        _trueText = trueText;
        _falseText = falseText;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? _trueText : _falseText;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class BoolToGeometryConverter : IValueConverter
{
    public static readonly BoolToGeometryConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string param)
            return null;

        var parts = param.Split(':', 2);
        if (parts.Length != 2)
            return null;

        var key = value is bool b && b ? parts[0] : parts[1];
        return Application.Current?.Resources.TryGetResource(key, null, out var resource) == true ? resource : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class VoiceModeBrushConverter : IValueConverter
{
    public static readonly VoiceModeBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => (value as string) switch
        {
            "Whisper" => GetBrush("BrushPurple"),
            "Normal" => GetBrush("BrushGreen"),
            "Shout" => GetBrush("BrushYellow"),
            "Broadcast" => GetBrush("BrushCyan"),
            _ => GetBrush("BrushTextMuted")
        };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static object? GetBrush(string key)
        => Application.Current?.Resources.TryGetResource(key, null, out var resource) == true ? resource : null;
}
