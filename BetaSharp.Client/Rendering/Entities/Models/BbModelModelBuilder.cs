namespace BetaSharp.Client.Rendering.Entities.Models;

public static class BbModelModelBuilder
{
    public static BbModelBuiltModel Build(BbModelDocument document, float inflationOffset = 0f)
    {
        Dictionary<string, BbModelElement> elementsByUuid = document.Elements.ToDictionary(e => e.Uuid, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, BbModelGroup> groupsByUuid = document.Groups.ToDictionary(g => g.Uuid, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ModelPart> parts = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, BbModelPartGeometry> geometry = new(StringComparer.OrdinalIgnoreCase);
        List<string> renderOrder = new();

        foreach (BbModelOutlinerEntry entry in document.Outliner)
        {
            if (!groupsByUuid.TryGetValue(entry.Uuid, out BbModelGroup? group) || !group.Export)
                continue;

            string? cubeUuid = entry.Children.FirstOrDefault();
            if (cubeUuid is null || !elementsByUuid.TryGetValue(cubeUuid, out BbModelElement? element) || !element.Export)
                continue;

            if (!string.Equals(element.Type, "cube", StringComparison.OrdinalIgnoreCase))
                continue;

            BbModelPartGeometry partGeometry = ConvertElement(group, element, inflationOffset);
            ModelPart part = CreateModelPart(partGeometry);
            string boneName = group.Name;

            parts[boneName] = part;
            geometry[boneName] = partGeometry;
            renderOrder.Add(boneName);
        }

        if (parts.Count == 0)
        {
            throw new InvalidDataException("Bbmodel produced no exportable parts from outliner.");
        }

        return new BbModelBuiltModel
        {
            Parts = parts,
            RenderOrder = renderOrder,
            Geometry = geometry
        };
    }

    internal static BbModelPartGeometry ConvertElement(BbModelGroup group, BbModelElement element, float inflationOffset)
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

        return new BbModelPartGeometry( group.Name, pivotX, pivotY, pivotZ, boxX, boxY, boxZ, sizeX, sizeY, sizeZ, uvU, uvV, inflate, element.MirrorUv);
    }

    private static ModelPart CreateModelPart(BbModelPartGeometry geometry)
    {
        ModelPart part = new(geometry.UvU, geometry.UvV)
        {
            Mirror = geometry.Mirror
        };
        part.AddBox(geometry.BoxX, geometry.BoxY, geometry.BoxZ, geometry.SizeX, geometry.SizeY, geometry.SizeZ, geometry.Inflate);
        part.SetRotationPoint(geometry.PivotX, geometry.PivotY, geometry.PivotZ);
        return part;
    }
}
