namespace OmniBlock.Client.Rendering.Entities.Models;

public static class BbModelModelBuilder
{
    public static BbModelBuiltModel Build(BbModelDocument document, float inflationOffset = 0f)
    {
        var elementsByUuid = document.Elements.ToDictionary(e => e.Uuid, StringComparer.OrdinalIgnoreCase);
        var groupsByUuid = document.Groups.ToDictionary(g => g.Uuid, StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ModelPart> parts = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, BbModelPartGeometry> geometry = new(StringComparer.OrdinalIgnoreCase);
        List<string> renderOrder = new();

        foreach (var entry in document.Outliner)
        {
            if (!groupsByUuid.TryGetValue(entry.Uuid, out var group) || !group.Export)
                continue;

            var cubeUuid = entry.Children.FirstOrDefault();
            if (cubeUuid is null || !elementsByUuid.TryGetValue(cubeUuid, out var element) || !element.Export)
                continue;

            if (!string.Equals(element.Type, "cube", StringComparison.OrdinalIgnoreCase))
                continue;

            var partGeometry = ConvertElement(group, element, inflationOffset);
            var part = CreateModelPart(partGeometry);
            var boneName = group.Name;

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

        var ox = group.Origin[0];
        var oy = group.Origin[1];
        var oz = group.Origin[2];

        var dx = element.To[0] - element.From[0];
        var dy = element.To[1] - element.From[1];
        var dz = element.To[2] - element.From[2];

        var sizeX = (int)MathF.Round(dx);
        var sizeY = (int)MathF.Round(dy);
        var sizeZ = (int)MathF.Round(dz);

        var boxX = ox - element.From[0] - dx;
        var boxY = oy - element.From[1] - dy;
        var boxZ = element.From[2] - oz;

        var pivotX = -ox;
        var pivotY = 24f - oy;
        var pivotZ = oz;

        var uvU = element.UvOffset.Length > 0 ? element.UvOffset[0] : 0;
        var uvV = element.UvOffset.Length > 1 ? element.UvOffset[1] : 0;
        var inflate = element.Inflate + inflationOffset;

        return new BbModelPartGeometry(group.Name, pivotX, pivotY, pivotZ, boxX, boxY, boxZ, sizeX, sizeY, sizeZ, uvU, uvV, inflate, element.MirrorUv);
    }

    private static ModelPart CreateModelPart(BbModelPartGeometry geometry)
    {
        ModelPart part = new(geometry.UvU, geometry.UvV)
        {
            Mirror = geometry.Mirror,
            Name = geometry.Name
        };
        part.AddBox(geometry.BoxX, geometry.BoxY, geometry.BoxZ, geometry.SizeX, geometry.SizeY, geometry.SizeZ, geometry.Inflate);
        part.SetRotationPoint(geometry.PivotX, geometry.PivotY, geometry.PivotZ);
        return part;
    }
}
