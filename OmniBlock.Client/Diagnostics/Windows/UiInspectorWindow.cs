using System.Numerics;
using System.Text;
using Hexa.NET.ImGui;
using OmniBlock.Client.UI;
using OmniBlock.Client.UI.Controls.Core;

namespace OmniBlock.Client.Diagnostics.Windows;

internal sealed class UIInspectorWindow(DebugWindowContext ctx) : DebugWindow
{
    private UIElement? _hoveredElement;
    private UIScreen? _lastScreen;
    private UIElement? _selectedElement;
    public override string Title => "UI Inspector";
    public override DebugDock DefaultDock => DebugDock.Right;

    protected override void OnDraw()
    {
        _hoveredElement = null;

        var screen = ctx.CurrentScreen;

        if (screen != _lastScreen)
        {
            _selectedElement = null;
            _lastScreen = screen;
        }

        ImGui.SeparatorText("Current Screen");

        if (screen == null)
        {
            ImGuiTextSafe.TextDisabled("none");
        }
        else
        {
            ImGuiTextSafe.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), screen.GetType().Name);
            ImGui.Spacing();

            ImGui.PushID("screen");
            DrawElementNode(screen.Root, 0);
            ImGui.PopID();
        }

        ImGui.Spacing();
        ImGui.SeparatorText("HUD");

        var hud = ctx.HUD;
        if (hud?.Root is { } hudRoot)
        {
            ImGui.PushID("hud");
            DrawElementNode(hudRoot, 0);
            ImGui.PopID();
        }
        else
        {
            ImGuiTextSafe.TextDisabled("no HUD");
        }

        if (_selectedElement != null)
        {
            ImGui.Spacing();
            ImGui.SeparatorText("Selected");
            if (ImGui.SmallButton("Clear selection"))
            {
                _selectedElement = null;
            }
            else
            {
                DrawProperties(_selectedElement);
            }
        }

        DrawOverlays();
    }

    private void DrawElementNode(UIElement element, int depth)
    {
        var nodeFlags = ImGuiTreeNodeFlags.SpanAvailWidth;
        if (depth == 0)
            nodeFlags |= ImGuiTreeNodeFlags.DefaultOpen;

        var dim = !element.Visible || !element.Enabled;
        var isSelected = element == _selectedElement;

        var colorPushCount = 0;
        if (isSelected)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.35f, 0.75f, 1f, 1f));
            colorPushCount++;
        }
        else if (dim)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            colorPushCount++;
        }

        var open = ImGui.TreeNodeEx("##n", nodeFlags);

        if (ImGui.IsItemHovered())
        {
            _hoveredElement = element;
        }

        if (ImGui.IsItemClicked())
        {
            _selectedElement = isSelected ? null : element;
        }

        ImGui.SameLine();
        ImGuiTextSafe.Text(BuildLabel(element));

        if (ImGui.IsItemHovered())
        {
            _hoveredElement = element;
        }

        if (colorPushCount > 0)
        {
            ImGui.PopStyleColor(colorPushCount);
        }

        if (open)
        {
            var propsOpen = ImGui.TreeNodeEx("##p", ImGuiTreeNodeFlags.SpanAvailWidth);
            ImGui.SameLine();
            ImGuiTextSafe.TextDisabled("Properties");

            if (propsOpen)
            {
                DrawProperties(element);
                ImGui.TreePop();
            }

            for (var i = 0; i < element.Children.Count; i++)
            {
                ImGui.PushID(i);
                DrawElementNode(element.Children[i], depth + 1);
                ImGui.PopID();
            }

            if (element is ScrollView sv)
            {
                ImGui.PushID("content");
                DrawElementNode(sv.ContentContainer, depth + 1);
                ImGui.PopID();
            }

            ImGui.TreePop();
        }
    }

    private unsafe void DrawOverlays()
    {
        var scaleFactor = GetScaleFactor();
        var vpOffset = ctx.DebugViewportScreenPos;
        ImDrawList* drawList = ImGui.GetForegroundDrawList();

        if (_hoveredElement != null && _hoveredElement != _selectedElement)
        {
            DrawElementHighlight(drawList, _hoveredElement, scaleFactor, vpOffset, false);
        }

        if (_selectedElement != null)
        {
            DrawElementHighlight(drawList, _selectedElement, scaleFactor, vpOffset, true);
        }
    }

    private static unsafe void DrawElementHighlight(ImDrawList* drawList, UIElement el, int scaleFactor, Vector2 vpOffset, bool isSelected)
    {
        var x = el.ScreenX * scaleFactor + vpOffset.X;
        var y = el.ScreenY * scaleFactor + vpOffset.Y;
        var w = el.ComputedWidth * scaleFactor;
        var h = el.ComputedHeight * scaleFactor;

        if (w <= 0 || h <= 0) return;

        var min = new Vector2(x, y);
        var max = new Vector2(x + w, y + h);

        var fillColor = isSelected
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.60f, 1.0f, 0.30f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.60f, 1.0f, 0.14f));

        var borderColor = isSelected
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.55f, 1.0f, 1.0f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.55f, 1.0f, 0.65f));

        var borderThickness = isSelected ? 2f : 1f;

        drawList->AddRectFilled(min, max, fillColor);
        drawList->AddRect(min, max, borderColor, 0f, 0, borderThickness);

        var typeName = el.GetType().Name;
        var sizeStr = $"  {el.ComputedWidth:F0}×{el.ComputedHeight:F0}";

        var typeSize = ImGui.CalcTextSize(typeName);
        var sizeTextSize = ImGui.CalcTextSize(sizeStr);
        var padX = 5f;
        var padY = 2f;
        var chipW = typeSize.X + sizeTextSize.X + padX * 2;
        var chipH = typeSize.Y + padY * 2;

        var chipX = x;
        var chipY = y - chipH - 1f;
        if (chipY < 0) chipY = y + 1f;

        var chipMin = new Vector2(chipX, chipY);
        var chipMax = new Vector2(chipX + chipW, chipY + chipH);

        var chipBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0.08f, 0.08f, 0.08f, 0.90f));
        var typeColor = isSelected
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.75f, 1.0f, 1.0f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.80f, 1.0f, 1.0f));
        var dimColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.70f, 0.70f, 0.70f, 1.0f));

        drawList->AddRectFilled(chipMin, chipMax, chipBg, 3f);
        drawList->AddText(new Vector2(chipX + padX, chipY + padY), typeColor, typeName);
        drawList->AddText(new Vector2(chipX + padX + typeSize.X, chipY + padY), dimColor, sizeStr);
    }

    private int GetScaleFactor()
    {
        var uiCtx = ctx.UIContext;
        var inputSize = uiCtx.InputDisplaySize;
        var res = new ScaledResolution(uiCtx.Options, inputSize.X, inputSize.Y);
        return res.ScaleFactor;
    }

    private static void DrawProperties(UIElement el)
    {
        foreach (var prop in el.GetInspectorProperties())
        {
            ImGuiTextSafe.Text(prop);
        }
    }

    private static string BuildLabel(UIElement el)
    {
        var typeName = el.GetType().Name;
        var sizeStr = el.ComputedWidth > 0 || el.ComputedHeight > 0
            ? $"  {el.ComputedWidth:F0}×{el.ComputedHeight:F0}"
            : string.Empty;

        var sb = new StringBuilder();
        if (!el.Visible) sb.Append(" [hidden]");
        if (!el.Enabled) sb.Append(" [disabled]");
        if (el.IsHovered) sb.Append(" [hovered]");
        if (el.IsFocused) sb.Append(" [focused]");
        if (!el.IsHitTestVisible) sb.Append(" [no-hit]");

        var flags = sb.Length > 0 ? "  " + sb.ToString().TrimStart() : string.Empty;
        return $"{typeName}{sizeStr}{flags}";
    }
}
