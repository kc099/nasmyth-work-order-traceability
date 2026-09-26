using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NasmythTraceability.Converters;

/// <summary>"OK" / "NG" / "DUP" / "ERR" / status text -> themed brush.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = (value?.ToString() ?? "").Trim().ToUpperInvariant();
        var resource = key switch
        {
            "OK" or "ACCEPTED" or "COMPLETED" or "ONLINE" or "INPROGRESS" => "AppOkBrush",
            "NG" or "REJECTED" or "ERR" or "ERROR" or "OFFLINE" => "AppNgBrush",
            "DUP" or "DUPLICATE" or "WARN" or "WARNING" => "AppWarnBrush",
            _ => "AppTextSecondaryBrush",
        };
        return Application.Current.TryFindResource(resource) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>bool -> one of two themed brushes (param "TrueKey|FalseKey").</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter?.ToString() ?? "AppOkBrush|AppNgBrush").Split('|');
        var key = value is true ? parts[0] : parts[^1];
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

/// <summary>Non-empty collection / non-null / non-empty string -> Visible, else Collapsed. Invert with param "Invert".</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasContent = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };

        if (string.Equals(parameter?.ToString(), "Invert", StringComparison.OrdinalIgnoreCase))
            hasContent = !hasContent;

        return hasContent ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// Two-way enum &lt;-&gt; bool for radio-button navigation.
/// ConverterParameter is the enum member name this radio represents.
/// </summary>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is not null && targetType.IsEnum)
            return Enum.Parse(targetType, parameter.ToString()!, ignoreCase: true);
        return Binding.DoNothing;
    }
}

/// <summary>Fraction (0..1) * bound reference width. Param = "MaxWidthPixels".</summary>
public sealed class FractionToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = value is double d ? d : 0;
        var max = double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var m)
            ? m : 200;
        return Math.Max(0, Math.Min(1, fraction)) * max;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
