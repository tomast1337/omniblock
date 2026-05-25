namespace BetaSharp.Client.Rendering.Entities.Bbmodel;

public static class BbmodelModelBuilder
{
    public static BbmodelBuiltModel Build(BbmodelDocument document, float inflationOffset = 0f)
    {
        var elementsByUuid = document.Elements.ToDictionary(e => e.Uuid, StringComparer.OrdinalIgnoreCase);
        var groupsByUuid = document.Groups.ToDictionary(g => g.Uuid, StringComparer.OrdinalIgnoreCase);

        var parts = new Dictionary<string, Models.ModelPart>(StringComparer.OrdinalIgnoreCase);
        var geometry = new Dictionary<string, BbmodelPartGeometry>(StringComparer.OrdinalIgnoreCase);
        var renderOrder = new List<string>();

        foreach (BbmodelOutlinerEntry entry in document.Outliner)
        {
            if (!groupsByUuid.TryGetValue(entry.Uuid, out BbmodelGroup? group) || !group.Export)
            {
                continue;
            }

            string? cubeUuid = entry.Children.FirstOrDefault();
            if (cubeUuid is null || !elementsByUuid.TryGetValue(cubeUuid, out BbmodelElement? element) || !element.Export)
            {
                continue;
            }

            if (!string.Equals(element.Type, "cube", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            BbmodelPartGeometry partGeometry = ConvertElement(group, element, inflationOffset);
            Models.ModelPart part = CreateModelPart(partGeometry);
            string boneName = group.Name;

            parts[boneName] = part;
            geometry[boneName] = partGeometry;
            renderOrder.Add(boneName);
        }

        if (parts.Count == 0)
        {
            throw new InvalidDataException("Bbmodel produced no exportable parts from outliner.");
        }

        return new BbmodelBuiltModel
        {
            Parts = parts,
            RenderOrder = renderOrder,
            Geometry = geometry,
        };
    }

    internal static BbmodelPartGeometry ConvertElement(BbmodelGroup group, BbmodelElement element, float inflationOffset)
    {
        if (group.Origin.Length < 3 || element.From.Length < 3 || element.To.Length < 3)
        {
            throw new InvalidDataException($"Bone '{group.Name}' has invalid origin or cube bounds.");
        }

        float ox = group.Origin[0];
        float oy = group.Origin[1];
        float oz = group.Origin[2];

        float dx = element.To[0] - element.From[0];
        float dy = element.To[1] - element.From[1];
        float dz = element.To[2] - element.From[2];

        int sizeX = (int)MathF.Round(dx);
        int sizeY = (int)MathF.Round(dy);
        int sizeZ = (int)MathF.Round(dz);

        float boxX = ox - element.From[0] - dx;
        float boxY = oy - element.From[1] - dy;
        float boxZ = element.From[2] - oz;

        float pivotX = -ox;
        float pivotY = 24f - oy;
        float pivotZ = oz;

        int uvU = element.UvOffset.Length > 0 ? element.UvOffset[0] : 0;
        int uvV = element.UvOffset.Length > 1 ? element.UvOffset[1] : 0;
        float inflate = element.Inflate + inflationOffset;

        return new BbmodelPartGeometry(
            group.Name,
            pivotX,
            pivotY,
            pivotZ,
            boxX,
            boxY,
            boxZ,
            sizeX,
            sizeY,
            sizeZ,
            uvU,
            uvV,
            inflate,
            element.MirrorUv);
    }

    private static Models.ModelPart CreateModelPart(BbmodelPartGeometry geometry)
    {
        var part = new Models.ModelPart(geometry.UvU, geometry.UvV);
        part.mirror = geometry.Mirror;
        part.addBox(
            geometry.BoxX,
            geometry.BoxY,
            geometry.BoxZ,
            geometry.SizeX,
            geometry.SizeY,
            geometry.SizeZ,
            geometry.Inflate);
        part.setRotationPoint(geometry.PivotX, geometry.PivotY, geometry.PivotZ);
        return part;
    }
}
