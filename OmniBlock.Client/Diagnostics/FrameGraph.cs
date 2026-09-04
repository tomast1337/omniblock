using System.Numerics;
using Hexa.NET.ImGui;

namespace OmniBlock.Client.Diagnostics;

public sealed class FrameGraph(string label, int capacity = 100)
{
    private readonly float[] _values = new float[capacity];
    private bool _autoScale = true;

    private float _customMax;
    private int _index;
    private bool _initializedMax;

    public void Push(float value)
    {
        _values[_index] = value;
        _index = (_index + 1) % capacity;
    }

    public void Draw(float height = 50.0f, float? manualMax = null)
    {
        if (!_initializedMax)
        {
            if (manualMax.HasValue)
            {
                _customMax = manualMax.Value;
                _autoScale = false;
            }
            else
            {
                _customMax = 100.0f;
                _autoScale = true;
            }

            _initializedMax = true;
        }

        if (!string.IsNullOrEmpty(label))
        {
            ImGuiTextSafe.Text(label);
            ImGui.SameLine();
            ImGui.Checkbox($"Auto##auto_{label}", ref _autoScale);
            if (!_autoScale)
            {
                ImGui.SameLine();
                ImGui.SetNextItemWidth(150);
                ImGui.DragFloat($"##max_{label}", ref _customMax, _customMax * 0.05f + 1.0f, 0.1f, float.MaxValue, "Max: %.1f");
            }
        }

        var drawList = ImGui.GetWindowDrawList();
        var p = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;

        if (width < 10) width = 100;

        ImGui.Dummy(new Vector2(width, height));

        drawList.AddRectFilled(p, p + new Vector2(width, height), ImGui.GetColorU32(new Vector4(0, 0, 0, 0.4f)));

        var maxValue = _autoScale ? GetMax() : _customMax;
        if (maxValue <= 0) maxValue = 1.0f;

        var scaleY = height / maxValue;
        var barWidth = width / capacity;
        var color = ImGui.GetColorU32(new Vector4(0.2f, 0.7f, 1.0f, 1.0f));

        for (var i = 0; i < capacity; i++)
        {
            var bufferIndex = (_index + i) % capacity;
            var val = _values[bufferIndex];
            var h = val * scaleY;

            if (h > 0)
            {
                if (h > height) h = height;

                var x = p.X + i * barWidth;
                var yBase = p.Y + height;

                drawList.AddRectFilled(
                    new Vector2(x, yBase - h),
                    new Vector2(x + barWidth, yBase),
                    color);
            }
        }
    }

    private float GetMax()
    {
        float max = 0;
        for (var i = 0; i < capacity; i++)
        {
            if (_values[i] > max)
                max = _values[i];
        }

        return max;
    }
}
