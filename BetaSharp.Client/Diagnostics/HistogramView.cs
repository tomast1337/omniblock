using System.Numerics;
using BetaSharp.Network;
using Hexa.NET.ImGui;

namespace BetaSharp.Client.Diagnostics;

/// <summary>
///     Draws a <see cref="PacketArrivalHistogram" /> as bars, one per bucket.
///     <para>
///         The shape is the point, and it is the thing a row of percentiles cannot show. A tight
///         cluster around the tick interval and a long thin tail are the same p50 and a very
///         different connection; so are a bimodal distribution and a wide unimodal one with the same
///         p95. Reading five numbers and imagining the curve is what made this window hard to use.
///     </para>
///     <para>
///         Bars are evenly spaced rather than positioned by value, because the buckets themselves are
///         logarithmic — spacing them linearly would compress everything below 100 ms into the first
///         eighth of the width, which is exactly the region every decision is made in.
///     </para>
/// </summary>
internal static class HistogramView
{
    /// <summary>Bars past p95 are drawn in the warning colour, so the tail is visible without reading the legend.</summary>
    private static readonly Vector4 s_barColour = new(0.2f, 0.7f, 1.0f, 1.0f);
    private static readonly Vector4 s_tailColour = new(1.0f, 0.6f, 0.2f, 1.0f);
    private static readonly Vector4 s_emptyColour = new(1.0f, 1.0f, 1.0f, 0.08f);

    /// <summary>
    ///     Draws the distribution and a one-line legend beneath it.
    /// </summary>
    /// <param name="label">Shown above the bars, with the sample count.</param>
    /// <param name="unit">Appended to every duration in the legend.</param>
    public static void Draw(string label, PacketArrivalHistogram? histogram, float height = 46f, string unit = "ms")
    {
        if (histogram is null || histogram.Count == 0)
        {
            ImGuiTextSafe.Text($"{label}: no samples yet");
            return;
        }

        long[] counts = histogram.Snapshot();
        IReadOnlyList<double> bounds = PacketArrivalHistogram.UpperBounds;

        double p50 = histogram.PercentileMs(50);
        double p95 = histogram.PercentileMs(95);

        ImGuiTextSafe.Text($"{label}  ({histogram.Count:N0} samples)");

        long peak = 0;
        foreach (long count in counts)
        {
            peak = Math.Max(peak, count);
        }

        ImDrawListPtr drawList = ImGui.GetWindowDrawList();
        Vector2 origin = ImGui.GetCursorScreenPos();
        float width = ImGui.GetContentRegionAvail().X;

        if (width < 10f)
        {
            width = 100f;
        }

        ImGui.Dummy(new Vector2(width, height));

        drawList.AddRectFilled(
            origin, origin + new Vector2(width, height), ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.4f)));

        float barWidth = width / counts.Length;
        uint bar = ImGui.GetColorU32(s_barColour);
        uint tail = ImGui.GetColorU32(s_tailColour);
        uint empty = ImGui.GetColorU32(s_emptyColour);

        for (int i = 0; i < counts.Length; i++)
        {
            float x = origin.X + (i * barWidth);
            float bottom = origin.Y + height;

            // An empty bucket still gets a sliver, so the axis reads as an axis and a gap in the
            // middle of the distribution is distinguishable from the end of it.
            float barHeight = peak == 0 ? 1f : Math.Max(1f, counts[i] / (float)peak * height);

            drawList.AddRectFilled(
                new Vector2(x + 1f, bottom - barHeight),
                new Vector2(x + barWidth - 1f, bottom),
                counts[i] == 0 ? empty : (bounds[i] > p95 ? tail : bar));
        }

        ImGuiTextSafe.Text(
            $"  p50 <= {Format(p50, unit)}   p95 <= {Format(p95, unit)}   max {histogram.MaxMs:F0} {unit}"
            + $"   mean {histogram.MeanMs:F1} {unit}");
    }

    private static string Format(double value, string unit) =>
        double.IsPositiveInfinity(value) ? $"over {PacketArrivalHistogram.UpperBounds[^2]:F0} {unit}" : $"{value:F0} {unit}";
}
