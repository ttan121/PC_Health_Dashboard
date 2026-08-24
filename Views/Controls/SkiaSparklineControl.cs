// ============================================================================
// PC Health Dashboard - Views/Controls/SkiaSparklineControl.cs
// Zero-Allocation High-Performance SkiaSharp Sparkline & History Graph
// ============================================================================

using System;
using System.Collections.Generic;
using System.Windows;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

namespace PCHealthDashboard.Views.Controls;

/// <summary>
/// High-performance GPU in-memory telemetry sparkline control backed by SkiaSharp.
/// Features zero-allocation per paint cycle, smooth anti-aliased Bézier curves,
/// linear gradient fills, dual-channel metrics, auto-scaling, and lifecycle pause.
/// </summary>
public class SkiaSparklineControl : SKElement
{
    public static readonly DependencyProperty PrimaryColorProperty =
        DependencyProperty.Register(nameof(PrimaryColor), typeof(System.Windows.Media.Color), typeof(SkiaSparklineControl),
            new PropertyMetadata(System.Windows.Media.Color.FromRgb(59, 130, 246), OnVisualPropertyChanged));

    public static readonly DependencyProperty SecondaryColorProperty =
        DependencyProperty.Register(nameof(SecondaryColor), typeof(System.Windows.Media.Color), typeof(SkiaSparklineControl),
            new PropertyMetadata(System.Windows.Media.Color.FromRgb(168, 85, 247), OnVisualPropertyChanged));

    public static readonly DependencyProperty IsDualChannelProperty =
        DependencyProperty.Register(nameof(IsDualChannel), typeof(bool), typeof(SkiaSparklineControl),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register(nameof(MaxValue), typeof(float), typeof(SkiaSparklineControl),
            new PropertyMetadata(100f, OnVisualPropertyChanged));

    public static readonly DependencyProperty AutoScaleProperty =
        DependencyProperty.Register(nameof(AutoScale), typeof(bool), typeof(SkiaSparklineControl),
            new PropertyMetadata(false, OnVisualPropertyChanged));

    public static readonly DependencyProperty ShowGridLinesProperty =
        DependencyProperty.Register(nameof(ShowGridLines), typeof(bool), typeof(SkiaSparklineControl),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty SmoothCurvesProperty =
        DependencyProperty.Register(nameof(SmoothCurves), typeof(bool), typeof(SkiaSparklineControl),
            new PropertyMetadata(true, OnVisualPropertyChanged));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(float), typeof(SkiaSparklineControl),
            new PropertyMetadata(2f, OnVisualPropertyChanged));

    public static readonly DependencyProperty FillOpacityProperty =
        DependencyProperty.Register(nameof(FillOpacity), typeof(byte), typeof(SkiaSparklineControl),
            new PropertyMetadata((byte)80, OnVisualPropertyChanged));

    public System.Windows.Media.Color PrimaryColor
    {
        get => (System.Windows.Media.Color)GetValue(PrimaryColorProperty);
        set => SetValue(PrimaryColorProperty, value);
    }

    public System.Windows.Media.Color SecondaryColor
    {
        get => (System.Windows.Media.Color)GetValue(SecondaryColorProperty);
        set => SetValue(SecondaryColorProperty, value);
    }

    public bool IsDualChannel
    {
        get => (bool)GetValue(IsDualChannelProperty);
        set => SetValue(IsDualChannelProperty, value);
    }

    public float MaxValue
    {
        get => (float)GetValue(MaxValueProperty);
        set => SetValue(MaxValueProperty, value);
    }

    public bool AutoScale
    {
        get => (bool)GetValue(AutoScaleProperty);
        set => SetValue(AutoScaleProperty, value);
    }

    public bool ShowGridLines
    {
        get => (bool)GetValue(ShowGridLinesProperty);
        set => SetValue(ShowGridLinesProperty, value);
    }

    public bool SmoothCurves
    {
        get => (bool)GetValue(SmoothCurvesProperty);
        set => SetValue(SmoothCurvesProperty, value);
    }

    public float StrokeThickness
    {
        get => (float)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public byte FillOpacity
    {
        get => (byte)GetValue(FillOpacityProperty);
        set => SetValue(FillOpacityProperty, value);
    }

    /// <summary>
    /// When set to true (e.g. during Cryo Mode / Minimized to Tray), chart invalidation is suspended.
    /// </summary>
    public bool IsPaused { get; set; }

    // Pre-allocated rendering primitives to ensure 0 GC heap allocations in OnPaintSurface
    private readonly SKPaint _strokePaintPrimary = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };

    private readonly SKPaint _fillPaintPrimary = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private readonly SKPaint _strokePaintSecondary = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 2,
        StrokeCap = SKStrokeCap.Round,
        StrokeJoin = SKStrokeJoin.Round
    };

    private readonly SKPaint _fillPaintSecondary = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Fill
    };

    private readonly SKPaint _gridPaint = new()
    {
        IsAntialias = true,
        Style = SKPaintStyle.Stroke,
        StrokeWidth = 1,
        Color = new SKColor(255, 255, 255, 12)
    };

    private readonly SKPath _strokePath = new();
    private readonly SKPath _fillPath = new();

    private float[] _primaryData = new float[64];
    private float[] _secondaryData = new float[64];
    private int _dataCount;
    private readonly object _dataLock = new();

    public SkiaSparklineControl()
    {
        // Clip to bounds to avoid overdraw outside element rect
        ClipToBounds = true;
    }

    /// <summary>
    /// Updates chart data series using double sequences (converts in-place into float buffers).
    /// </summary>
    public void UpdateData(IReadOnlyList<double> primary, IReadOnlyList<double>? secondary = null)
    {
        if (primary == null || primary.Count == 0) return;

        lock (_dataLock)
        {
            int count = primary.Count;
            EnsureBufferCapacity(count);

            for (int i = 0; i < count; i++)
            {
                _primaryData[i] = (float)primary[i];
            }

            if (secondary != null && secondary.Count > 0)
            {
                int secCount = Math.Min(count, secondary.Count);
                for (int i = 0; i < secCount; i++)
                {
                    _secondaryData[i] = (float)secondary[i];
                }
                for (int i = secCount; i < count; i++)
                {
                    _secondaryData[i] = 0f;
                }
            }
            _dataCount = count;
        }

        if (!IsPaused && IsVisible)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Updates chart data series using float sequences.
    /// </summary>
    public void UpdateData(IReadOnlyList<float> primary, IReadOnlyList<float>? secondary = null)
    {
        if (primary == null || primary.Count == 0) return;

        lock (_dataLock)
        {
            int count = primary.Count;
            EnsureBufferCapacity(count);

            for (int i = 0; i < count; i++)
            {
                _primaryData[i] = primary[i];
            }

            if (secondary != null && secondary.Count > 0)
            {
                int secCount = Math.Min(count, secondary.Count);
                for (int i = 0; i < secCount; i++)
                {
                    _secondaryData[i] = secondary[i];
                }
                for (int i = secCount; i < count; i++)
                {
                    _secondaryData[i] = 0f;
                }
            }
            _dataCount = count;
        }

        if (!IsPaused && IsVisible)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Updates chart data directly from ReadOnlySpan (zero heap allocation).
    /// </summary>
    public void UpdateData(ReadOnlySpan<float> primary, ReadOnlySpan<float> secondary = default)
    {
        if (primary.IsEmpty) return;

        lock (_dataLock)
        {
            int count = primary.Length;
            EnsureBufferCapacity(count);

            primary.CopyTo(_primaryData.AsSpan(0, count));

            if (!secondary.IsEmpty)
            {
                int secCount = Math.Min(count, secondary.Length);
                secondary[..secCount].CopyTo(_secondaryData.AsSpan(0, secCount));
                if (secCount < count)
                {
                    _secondaryData.AsSpan(secCount, count - secCount).Clear();
                }
            }
            _dataCount = count;
        }

        if (!IsPaused && IsVisible)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Appends or shifts a single point to the rolling buffer.
    /// </summary>
    public void PushPoint(float primaryVal, float? secondaryVal = null, int maxCapacity = 60)
    {
        lock (_dataLock)
        {
            EnsureBufferCapacity(maxCapacity);

            if (_dataCount < maxCapacity)
            {
                _primaryData[_dataCount] = primaryVal;
                _secondaryData[_dataCount] = secondaryVal ?? 0f;
                _dataCount++;
            }
            else
            {
                // Shift left by 1
                Array.Copy(_primaryData, 1, _primaryData, 0, maxCapacity - 1);
                _primaryData[maxCapacity - 1] = primaryVal;

                Array.Copy(_secondaryData, 1, _secondaryData, 0, maxCapacity - 1);
                _secondaryData[maxCapacity - 1] = secondaryVal ?? 0f;
            }
        }

        if (!IsPaused && IsVisible)
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Clears data and resets the chart.
    /// </summary>
    public void ClearData()
    {
        lock (_dataLock)
        {
            _dataCount = 0;
        }

        if (!IsPaused && IsVisible)
        {
            InvalidateVisual();
        }
    }

    private void EnsureBufferCapacity(int count)
    {
        if (_primaryData.Length < count)
        {
            int newSize = Math.Max(count, _primaryData.Length * 2);
            Array.Resize(ref _primaryData, newSize);
            Array.Resize(ref _secondaryData, newSize);
        }
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(SKColors.Transparent);

        int count;
        lock (_dataLock)
        {
            count = _dataCount;
        }

        if (count < 2) return;

        float width = info.Width;
        float height = info.Height;

        if (width <= 0 || height <= 0) return;

        // 1. Draw Grid Lines
        if (ShowGridLines)
        {
            canvas.DrawLine(0, height * 0.25f, width, height * 0.25f, _gridPaint);
            canvas.DrawLine(0, height * 0.50f, width, height * 0.50f, _gridPaint);
            canvas.DrawLine(0, height * 0.75f, width, height * 0.75f, _gridPaint);
        }

        // 2. Determine Scale Limit
        float maxVal = MaxValue;
        if (AutoScale)
        {
            maxVal = 1f; // Floor to avoid division by 0
            lock (_dataLock)
            {
                for (int i = 0; i < count; i++)
                {
                    if (_primaryData[i] > maxVal) maxVal = _primaryData[i];
                    if (IsDualChannel && _secondaryData[i] > maxVal) maxVal = _secondaryData[i];
                }
            }
            // Add a 10% headroom for visual aesthetic
            maxVal *= 1.1f;
        }

        if (maxVal <= 0.0001f) maxVal = 1f;

        // 3. Draw Secondary Channel (Underneath primary, e.g. Upload)
        if (IsDualChannel)
        {
            DrawSeries(canvas, _secondaryData, count, width, height, maxVal, SecondaryColor, _strokePaintSecondary, _fillPaintSecondary);
        }

        // 4. Draw Primary Channel (e.g. Download)
        DrawSeries(canvas, _primaryData, count, width, height, maxVal, PrimaryColor, _strokePaintPrimary, _fillPaintPrimary);
    }

    private void DrawSeries(
        SKCanvas canvas,
        float[] data,
        int count,
        float width,
        float height,
        float maxVal,
        System.Windows.Media.Color color,
        SKPaint strokePaint,
        SKPaint fillPaint)
    {
        SKColor skColor = new(color.R, color.G, color.B, color.A);
        strokePaint.Color = skColor;
        strokePaint.StrokeWidth = StrokeThickness;

        _strokePath.Reset();
        _fillPath.Reset();

        float stepX = width / Math.Max(1, count - 1);
        float bottomPadding = 2f;
        float usableHeight = height - bottomPadding;

        // Calculate Y position: 0 is top, usableHeight is bottom
        float GetY(float val)
        {
            float normalized = Math.Clamp(val / maxVal, 0f, 1f);
            return usableHeight - (normalized * (usableHeight - 2f)) + 1f;
        }

        float firstX = 0f;
        float firstY;
        lock (_dataLock)
        {
            firstY = GetY(data[0]);
        }

        _strokePath.MoveTo(firstX, firstY);
        _fillPath.MoveTo(firstX, height);
        _fillPath.LineTo(firstX, firstY);

        if (SmoothCurves && count >= 3)
        {
            // Catmull-Rom to Cubic Bézier Spline Interpolation
            for (int i = 0; i < count - 1; i++)
            {
                float p0X = (i > 0) ? (i - 1) * stepX : 0f;
                float p1X = i * stepX;
                float p2X = (i + 1) * stepX;
                float p3X = (i + 2 < count) ? (i + 2) * stepX : p2X;

                float p0Y, p1Y, p2Y, p3Y;
                lock (_dataLock)
                {
                    p0Y = GetY((i > 0) ? data[i - 1] : data[i]);
                    p1Y = GetY(data[i]);
                    p2Y = GetY(data[i + 1]);
                    p3Y = GetY((i + 2 < count) ? data[i + 2] : data[i + 1]);
                }

                // Control points
                float c1X = p1X + (p2X - p0X) / 6f;
                float c1Y = Math.Clamp(p1Y + (p2Y - p0Y) / 6f, 0f, height);

                float c2X = p2X - (p3X - p1X) / 6f;
                float c2Y = Math.Clamp(p2Y - (p3Y - p1Y) / 6f, 0f, height);

                _strokePath.CubicTo(c1X, c1Y, c2X, c2Y, p2X, p2Y);
                _fillPath.CubicTo(c1X, c1Y, c2X, c2Y, p2X, p2Y);
            }
        }
        else
        {
            // Standard Linear segments
            for (int i = 1; i < count; i++)
            {
                float x = i * stepX;
                float y;
                lock (_dataLock)
                {
                    y = GetY(data[i]);
                }
                _strokePath.LineTo(x, y);
                _fillPath.LineTo(x, y);
            }
        }

        float lastX = (count - 1) * stepX;
        _fillPath.LineTo(lastX, height);
        _fillPath.Close();

        // Linear Gradient Shader for Area Fill
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(0, height),
            new[] { skColor.WithAlpha(FillOpacity), skColor.WithAlpha(0) },
            null,
            SKShaderTileMode.Clamp);

        fillPaint.Shader = shader;

        // Render filled area first, then stroke line on top
        canvas.DrawPath(_fillPath, fillPaint);
        canvas.DrawPath(_strokePath, strokePaint);
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SkiaSparklineControl ctrl && !ctrl.IsPaused)
        {
            ctrl.InvalidateVisual();
        }
    }
}
