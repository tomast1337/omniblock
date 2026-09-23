namespace OmniBlock.Client.UI.Layout.Flexbox;

public partial class Node
{
    internal readonly List<Node> Children = new();
    internal readonly Flex.Layout nodeLayout = new();

    internal readonly Value[] resolvedDimensions = new Value[2] { Flex.ValueUndefined, Flex.ValueUndefined };

    private Layout? _layout;
    internal BaselineFunc? baselineFunc;
    internal Config config = Constant.configDefaults;
    public object? Context;
    internal bool hasNewLayout = true;
    internal int lineIndex;

    internal MeasureFunc? measureFunc;

    internal Node? NextChild;
    public Style nodeStyle = new();
    internal NodeType NodeType = NodeType.Default;

    internal Node? Parent;
    internal PrintFunc? printFunc;

    public Node()
    {
    }

    public Node(Style style) => nodeStyle = style;

    public Layout layout
    {
        get
        {
            if (_layout == null)
            {
                _layout = new Layout(this);
            }

            return (Layout)_layout;
        }
    }

    public int ChildrenCount => Children.Count;

    public Node? firstChild => Children.Count > 0 ? Children.First() : null;
    public Node? lastChild => Children.Count > 0 ? Children.Last() : null;

    public bool IsDirty { get; internal set; }


    public void CalculateLayout(float parentWidth, float parentHeight, Direction parentDirection)
    {
        _layout = null;
        Flex.CalculateLayout(this, parentWidth, parentHeight, parentDirection);
    }

    public void MarkAsDirty() => Flex.nodeMarkDirtyInternal(this);


    #region Layout

    internal float LayoutGetX()
    {
        var x = nodeLayout.Position[(int)Edge.Left];
        if (Parent != null)
        {
            x += Parent.LayoutGetX();
        }

        return x;
    }

    internal float LayoutGetY()
    {
        var y = nodeLayout.Position[(int)Edge.Top];
        if (Parent != null)
        {
            y += Parent.LayoutGetY();
        }

        return y;
    }

    // LayoutGetLeft gets left
    internal float LayoutGetLeft() => nodeLayout.Position[(int)Edge.Left];

    // LayoutGetTop gets top
    internal float LayoutGetTop() => nodeLayout.Position[(int)Edge.Top];

    // LayoutGetRight gets right
    internal float LayoutGetRight() => nodeLayout.Position[(int)Edge.Right];

    // LayoutGetBottom gets bottom
    internal float LayoutGetBottom() => nodeLayout.Position[(int)Edge.Bottom];

    // LayoutGetWidth gets width
    internal float LayoutGetWidth() => nodeLayout.Dimensions[(int)Dimension.Width];

    // LayoutGetHeight gets height
    internal float LayoutGetHeight() => nodeLayout.Dimensions[(int)Dimension.Height];

    // LayoutGetMargin gets margin
    internal float LayoutGetMargin(Edge edge)
    {
        Flex.assertWithNode(this, edge < Edge.End, "Cannot get layout properties of multi-edge shorthands");
        if (edge == Edge.Left)
        {
            if (nodeLayout.Direction == Direction.RTL)
            {
                return nodeLayout.Margin[(int)Edge.End];
            }

            return nodeLayout.Margin[(int)Edge.Start];
        }

        if (edge == Edge.Right)
        {
            if (nodeLayout.Direction == Direction.RTL)
            {
                return nodeLayout.Margin[(int)Edge.Start];
            }

            return nodeLayout.Margin[(int)Edge.End];
        }

        return nodeLayout.Margin[(int)edge];
    }

    // LayoutGetBorder gets border
    internal float LayoutGetBorder(Edge edge)
    {
        Flex.assertWithNode(this, edge < Edge.End,
            "Cannot get layout properties of multi-edge shorthands");
        if (edge == Edge.Left)
        {
            if (nodeLayout.Direction == Direction.RTL)
            {
                return nodeLayout.Border[(int)Edge.End];
            }

            return nodeLayout.Border[(int)Edge.Start];
        }

        if (edge == Edge.Right)
        {
            if (nodeLayout.Direction == Direction.RTL)
            {
                return nodeLayout.Border[(int)Edge.Start];
            }

            return nodeLayout.Border[(int)Edge.End];
        }

        return nodeLayout.Border[(int)edge];
    }

    // LayoutGetPadding gets padding
    internal float LayoutGetPadding(Edge edge)
    {
        Flex.assertWithNode(this, edge < Edge.End,
            "Cannot get layout properties of multi-edge shorthands");
        if (edge == Edge.Left)
        {
            if (nodeLayout.Direction == Direction.RTL)
            {
                return nodeLayout.Padding[(int)Edge.End];
            }

            return nodeLayout.Padding[(int)Edge.Start];
        }

        if (edge == Edge.Right)
        {
            if (nodeLayout.Direction == Direction.RTL)
            {
                return nodeLayout.Padding[(int)Edge.Start];
            }

            return nodeLayout.Padding[(int)Edge.End];
        }

        return nodeLayout.Padding[(int)edge];
    }

    internal Direction LayoutGetDirection() => nodeLayout.Direction;

    internal bool LayoutGetHadOverflow() => nodeLayout.HadOverflow;

    #endregion

    #region other props

    public void SetMeasureFunc(MeasureFunc? measureFunc) => Flex.SetMeasureFunc(this, measureFunc);

    public MeasureFunc? GetMeasureFunc() => measureFunc;

    public void SetBaselineFunc(BaselineFunc? baselineFunc) => this.baselineFunc = baselineFunc;

    public BaselineFunc? GetBaselineFunc() => baselineFunc;

    public void SetPrintFunc(PrintFunc? printFunc) => this.printFunc = printFunc;

    public PrintFunc? GetPrintFunc() => printFunc;

    #endregion

    #region tree

    public Node? GetChild(int idx) => Flex.GetChild(this, idx);
    public void AddChild(Node child) => Flex.InsertChild(this, child, ChildrenCount);

    public void InsertChild(Node child, int idx) => Flex.InsertChild(this, child, idx);
    public void RemoveChild(Node child) => Flex.RemoveChild(this, child);

    #endregion
}
