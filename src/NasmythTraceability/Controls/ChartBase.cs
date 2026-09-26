using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NasmythTraceability.Models;

namespace NasmythTraceability.Controls;

/// <summary>
/// Base for the lightweight, dependency-free charts. Owns an <see cref="ItemsSource"/> of
/// <see cref="SeriesPoint"/> and re-renders on data or size change via <see cref="OnRender"/>.
/// </summary>
public abstract class ChartBase : Control
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(ChartBase),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnItemsSourceChanged));

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText), typeof(string), typeof(ChartBase),
        new FrameworkPropertyMetadata("No data", FrameworkPropertyMetadataOptions.AffectsRender));

    private INotifyCollectionChanged? _observed;

    protected ChartBase()
    {
        SnapsToDevicePixels = true;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    protected double Dpi => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    protected static Brush SeriesBrush(int index)
    {
        var keys = new[] { "Series1Brush", "Series2Brush", "Series3Brush", "Series4Brush", "Series5Brush" };
        return Application.Current?.TryFindResource(keys[index % keys.Length]) as Brush
               ?? Brushes.SteelBlue;
    }

    protected Brush TextBrush =>
        Application.Current?.TryFindResource("AppTextSecondaryBrush") as Brush ?? Brushes.Gray;

    protected Brush GridBrush =>
        Application.Current?.TryFindResource("AppBorderBrush") as Brush ?? Brushes.DimGray;

    protected List<SeriesPoint> Points()
    {
        var list = new List<SeriesPoint>();
        if (ItemsSource is null)
            return list;
        foreach (var item in ItemsSource)
            if (item is SeriesPoint p)
                list.Add(p);
        return list;
    }

    protected FormattedText Text(string value, double size, Brush brush, FontWeight? weight = null)
        => new(value ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
            size, brush, Dpi);

    protected void DrawEmpty(DrawingContext dc)
    {
        var t = Text(EmptyText, 13, TextBrush);
        dc.DrawText(t, new Point((ActualWidth - t.Width) / 2, (ActualHeight - t.Height) / 2));
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var chart = (ChartBase)d;

        if (chart._observed is not null)
            chart._observed.CollectionChanged -= chart.OnCollectionChanged;

        chart._observed = e.NewValue as INotifyCollectionChanged;
        if (chart._observed is not null)
            chart._observed.CollectionChanged += chart.OnCollectionChanged;

        chart.InvalidateVisual();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
}
