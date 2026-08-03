namespace BetaSharp.Client.UI.Layout.Flexbox;

public partial class Flex
{
    internal class CachedMeasurement
    {
        internal float availableHeight;
        internal float availableWidth;
        internal float computedHeight = -1;
        internal float computedWidth = -1;
        internal MeasureMode heightMeasureMode = MeasureMode.Undefined;
        internal MeasureMode widthMeasureMode = MeasureMode.Undefined;

        internal void ResetToDefault()
        {
            availableHeight = 0;
            availableWidth = 0;
            widthMeasureMode = MeasureMode.Undefined;
            heightMeasureMode = MeasureMode.Undefined;
            computedWidth = -1;
            computedHeight = -1;
        }
    }

    internal class Layout
    {
        internal readonly float[] Border = new float[6];
        internal readonly CachedMeasurement cachedLayout = new();

        internal readonly CachedMeasurement[] cachedMeasurements = new CachedMeasurement[Constant.MaxCachedResultCount] { new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new(), new() };

        internal readonly float[] Dimensions = new float[2] { float.NaN, float.NaN };
        internal readonly float[] Margin = new float[6];
        internal readonly float[] measuredDimensions = new float[2] { float.NaN, float.NaN };
        internal readonly float[] Padding = new float[6];

        internal readonly float[] Position = new float[4];
        internal float computedFlexBasis = float.NaN;
        internal int computedFlexBasisGeneration;
        internal Direction Direction;

        // Instead of recomputing the entire layout every single time, we
        // cache some information to break early when nothing changed
        internal int generationCount;
        internal bool HadOverflow;
        internal Direction lastParentDirection = Direction.NeverUsed_1;
        internal int nextCachedMeasurementsIndex;

        internal void ResetToDefault()
        {
            for (int i = 0; i < Position.Length; i++)
            {
                Position[i] = 0;
            }

            for (int i = 0; i < Dimensions.Length; i++)
            {
                Dimensions[i] = float.NaN;
            }

            for (int i = 0; i < 6; i++)
            {
                Margin[i] = 0;
                Border[i] = 0;
                Padding[i] = 0;
            }

            Direction = Direction.Inherit;
            computedFlexBasisGeneration = 0;
            computedFlexBasis = float.NaN;
            HadOverflow = false;
            generationCount = 0;
            lastParentDirection = Direction.NeverUsed_1;
            nextCachedMeasurementsIndex = 0;

            foreach (CachedMeasurement cm in cachedMeasurements)
            {
                cm.ResetToDefault();
            }

            for (int i = 0; i < measuredDimensions.Length; i++)
            {
                measuredDimensions[i] = float.NaN;
            }

            cachedLayout.ResetToDefault();
        }
    }
}
