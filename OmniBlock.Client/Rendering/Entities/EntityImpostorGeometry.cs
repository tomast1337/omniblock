using System.Numerics;
using OmniBlock.Client.Rendering.Entities.Models;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>Compiles supported BB models into isolated, provider-owned capture geometry.</summary>
internal static class EntityImpostorGeometry
{
    internal static readonly ResourceLocation QuadrupedPoseProvider = new(Namespace.OmniBlock, "quadruped");
    internal static readonly ResourceLocation ZombiePoseProvider = new(Namespace.OmniBlock, "zombie");
    internal static readonly ResourceLocation CreeperPoseProvider = new(Namespace.OmniBlock, "creeper");

    internal static bool Supports(ResourceLocation provider) =>
        provider == QuadrupedPoseProvider || provider == ZombiePoseProvider || provider == CreeperPoseProvider;

    public static EntityImpostorVertex[][] BuildPoses(string modelName) =>
        BuildPoses(modelName, QuadrupedPoseProvider);

    public static EntityImpostorVertex[][] BuildPoses(string modelName, ResourceLocation poseProvider)
    {
        if (!Supports(poseProvider))
            throw new InvalidDataException($"Unknown impostor pose provider '{poseProvider}'.");
        var result = new EntityImpostorVertex[EntityImpostorLayout.Poses][];
        result[0] = BuildPose(modelName, poseProvider, 0, 0);
        for (var pose = 1; pose < result.Length; pose++)
            result[pose] = BuildPose(modelName, poseProvider, (pose - 1) * MathF.PI / 2, 1);
        return result;
    }

    private static EntityImpostorVertex[] BuildPose(
        string modelName, ResourceLocation poseProvider, float gaitAngle, float gaitAmount)
    {
        var document = BbModelLoader.LoadCached(modelName);
        List<EntityImpostorVertex> vertices = [];
        foreach (var entry in document.Outliner)
        {
            var group = document.Groups.FirstOrDefault(g => g.Uuid == entry.Uuid && g.Export);
            var element = document.Elements.FirstOrDefault(e => e.Uuid == entry.Children.FirstOrDefault() && e.Export);
            if (group == null || element?.Type != "cube") continue;
            var g = BbModelModelBuilder.ConvertElement(group, element, 0);
            var part = new ModelPart(g.UvU, g.UvV, reserveStaticGeometry: false) { Mirror = g.Mirror };
            part.AddBox(g.BoxX, g.BoxY, g.BoxZ, g.SizeX, g.SizeY, g.SizeZ, g.Inflate);
            var angleX = (poseProvider.Path, g.Name) switch
            {
                ("quadruped", "body" or "udders") => MathF.PI / 2,
                ("quadruped" or "creeper", "leg1" or "leg4") =>
                    MathF.Cos(gaitAngle) * 1.4f * gaitAmount,
                ("quadruped" or "creeper", "leg2" or "leg3") =>
                    MathF.Cos(gaitAngle + MathF.PI) * 1.4f * gaitAmount,
                ("zombie", "bipedRightLeg") => MathF.Cos(gaitAngle) * 1.4f * gaitAmount,
                ("zombie", "bipedLeftLeg") => MathF.Cos(gaitAngle + MathF.PI) * 1.4f * gaitAmount,
                ("zombie", "bipedRightArm" or "bipedLeftArm") => -MathF.PI / 2,
                _ => 0
            };
            var rotation = Matrix4x4.CreateRotationX(angleX);
            // LivingEntityRenderer at body yaw 0: rotate Y 180, scale (-1,-1,1),
            // translate root down 24/16 + 1/128, then bone pivot and model scale.
            var transform = rotation * Matrix4x4.CreateTranslation(g.PivotX, g.PivotY, g.PivotZ) *
                Matrix4x4.CreateScale(1 / 16f) * Matrix4x4.CreateTranslation(0, -1.5f - 1 / 128f, 0) *
                Matrix4x4.CreateScale(1, -1, -1);
            foreach (var v in part.GetBakedVertices())
                vertices.Add(new EntityImpostorVertex(
                    Vector3.Transform(new Vector3(v.Position.X, v.Position.Y, v.Position.Z), transform),
                    new Vector2(v.U, v.V),
                    Vector3.Normalize(Vector3.TransformNormal(new Vector3(v.Normal.X, v.Normal.Y, v.Normal.Z), transform))));
        }
        if (vertices.Count == 0 || vertices.Count > 65536)
            throw new InvalidDataException($"Invalid '{modelName}' impostor capture geometry.");
        return vertices.ToArray();
    }
}

internal sealed record EntityImpostorCaptureLayer(string TexturePath, EntityImpostorVertex[][] Poses);
