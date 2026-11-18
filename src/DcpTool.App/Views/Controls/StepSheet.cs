using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DcpTool.App.Views.Controls;

/// <summary>Which of the two bottom areas is the open step.</summary>
public enum OpenStep
{
    None,
    Add,
    Export,
}

/// <summary>
/// The ground behind the two areas at the bottom of the window. The open step is a tab of the white sheet above:
/// the sheet and its tab carry one grey outline, the tab rounds only at the bottom and a small curve joins it to the
/// sheet on the inner side. Drawn as one path, so there are no seams (board "Styles to copy", note for Avalonia).
/// </summary>
public sealed class StepSheet : Control
{
    private const double TabRadius = 22;
    private const double JoinRadius = 12;

    public static readonly StyledProperty<OpenStep> OpenStepProperty =
        AvaloniaProperty.Register<StepSheet, OpenStep>(nameof(OpenStep));

    /// <summary>Space between the bottom of the tab and the bottom of the control.</summary>
    public static readonly StyledProperty<double> BottomGapProperty =
        AvaloniaProperty.Register<StepSheet, double>(nameof(BottomGap), 12);

    public static readonly StyledProperty<IBrush?> GroundProperty =
        AvaloniaProperty.Register<StepSheet, IBrush?>(nameof(Ground));

    public static readonly StyledProperty<IBrush?> SheetProperty =
        AvaloniaProperty.Register<StepSheet, IBrush?>(nameof(Sheet));

    public static readonly StyledProperty<IBrush?> LineProperty =
        AvaloniaProperty.Register<StepSheet, IBrush?>(nameof(Line));

    static StepSheet()
    {
        AffectsRender<StepSheet>(OpenStepProperty, BottomGapProperty, GroundProperty, SheetProperty, LineProperty);
    }

    public OpenStep OpenStep
    {
        get => GetValue(OpenStepProperty);
        set => SetValue(OpenStepProperty, value);
    }

    public double BottomGap
    {
        get => GetValue(BottomGapProperty);
        set => SetValue(BottomGapProperty, value);
    }

    public IBrush? Ground
    {
        get => GetValue(GroundProperty);
        set => SetValue(GroundProperty, value);
    }

    public IBrush? Sheet
    {
        get => GetValue(SheetProperty);
        set => SetValue(SheetProperty, value);
    }

    public IBrush? Line
    {
        get => GetValue(LineProperty);
        set => SetValue(LineProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        context.FillRectangle(Ground ?? Brushes.LightGray, new Rect(0, 0, w, h));
        var pen = new Pen(Line ?? Brushes.Gray, 1);
        var half = Math.Round(w / 2);
        var bottom = h - BottomGap - 0.5;

        if (OpenStep == OpenStep.None)
        {
            context.DrawLine(pen, new Point(0, 0.5), new Point(w, 0.5));
            return;
        }

        var (outline, fill) = OpenStep == OpenStep.Add
            ? (LeftTab(half, bottom, w, closed: false), LeftTab(half, bottom, w, closed: true))
            : (RightTab(half, bottom, w, closed: false), RightTab(half, bottom, w, closed: true));
        context.DrawGeometry(Sheet ?? Brushes.White, null, fill);
        context.DrawGeometry(null, pen, outline);
    }

    /// <summary>The Add area open: the tab spans the left half, flush with the left edge.</summary>
    private static StreamGeometry LeftTab(double half, double bottom, double width, bool closed)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        var inner = half - 0.5;
        if (closed)
        {
            g.BeginFigure(new Point(0, 0), true);
            g.LineTo(new Point(0, bottom - TabRadius));
        }
        else
        {
            g.BeginFigure(new Point(0, bottom - TabRadius), false);
        }

        g.ArcTo(new Point(TabRadius, bottom), new Size(TabRadius, TabRadius), 0, false, SweepDirection.CounterClockwise);
        g.LineTo(new Point(inner - TabRadius, bottom));
        g.ArcTo(new Point(inner, bottom - TabRadius), new Size(TabRadius, TabRadius), 0, false, SweepDirection.CounterClockwise);
        g.LineTo(new Point(inner, JoinRadius));
        g.ArcTo(new Point(inner + JoinRadius, 0.5), new Size(JoinRadius, JoinRadius), 0, false, SweepDirection.Clockwise);
        if (closed)
        {
            g.LineTo(new Point(inner + JoinRadius, 0));
            g.EndFigure(true);
        }
        else
        {
            g.LineTo(new Point(width, 0.5));
            g.EndFigure(false);
        }

        return geometry;
    }

    /// <summary>The Export area open: the tab spans the right half, flush with the right edge.</summary>
    private static StreamGeometry RightTab(double half, double bottom, double width, bool closed)
    {
        var geometry = new StreamGeometry();
        using var g = geometry.Open();
        var inner = half + 0.5;
        if (closed)
        {
            g.BeginFigure(new Point(inner - JoinRadius, 0), true);
        }
        else
        {
            g.BeginFigure(new Point(0, 0.5), false);
            g.LineTo(new Point(inner - JoinRadius, 0.5));
        }

        g.ArcTo(new Point(inner, JoinRadius), new Size(JoinRadius, JoinRadius), 0, false, SweepDirection.Clockwise);
        g.LineTo(new Point(inner, bottom - TabRadius));
        g.ArcTo(new Point(inner + TabRadius, bottom), new Size(TabRadius, TabRadius), 0, false, SweepDirection.CounterClockwise);
        g.LineTo(new Point(width - TabRadius, bottom));
        g.ArcTo(new Point(width, bottom - TabRadius), new Size(TabRadius, TabRadius), 0, false, SweepDirection.CounterClockwise);
        if (closed)
        {
            g.LineTo(new Point(width, 0));
            g.EndFigure(true);
        }
        else
        {
            g.EndFigure(false);
        }

        return geometry;
    }
}
