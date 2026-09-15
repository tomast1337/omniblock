using System.Numerics;
using OmniBlock.Client.Rendering.Entities.Models;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>Compiles supported BB models into isolated, provider-owned capture geometry.</summary>
internal static class EntityImpostorGeometry
{
    internal static readonly ResourceLocation QuadrupedPoseProvider = new(Namespace.OmniBlock, "quadruped");
    internal static readonly ResourceLocation StaticPoseProvider = new(Namespace.OmniBlock, "static");
    internal static readonly ResourceLocation ChickenPoseProvider = new(Namespace.OmniBlock, "chicken");
    internal static readonly ResourceLocation SpiderPoseProvider = new(Namespace.OmniBlock, "spider");
    internal static readonly ResourceLocation ZombiePoseProvider = new(Namespace.OmniBlock, "zombie");
    internal static readonly ResourceLocation CreeperPoseProvider = new(Namespace.OmniBlock, "creeper");
    internal static readonly ResourceLocation WolfPoseProvider = new(Namespace.OmniBlock, "wolf");

    internal static bool Supports(ResourceLocation provider) =>
        provider == QuadrupedPoseProvider || provider == StaticPoseProvider ||
        provider == ChickenPoseProvider || provider == SpiderPoseProvider ||
        provider == ZombiePoseProvider || provider == CreeperPoseProvider || provider == WolfPoseProvider;

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
            var (angleX, angleY, angleZ) = RotationFor(poseProvider.Path, g.Name, gaitAngle, gaitAmount);
            var rotation = Matrix4x4.CreateRotationX(angleX) * Matrix4x4.CreateRotationY(angleY) *
                           Matrix4x4.CreateRotationZ(angleZ);
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

    private static (float X, float Y, float Z) RotationFor(
        string provider, string part, float gaitAngle, float gaitAmount)
    {
        if (provider == "static") return default;
        if (provider == "spider" && part.StartsWith("spiderLeg", StringComparison.Ordinal))
        {
            var leg = part[^1] - '0';
            var pair = (leg - 1) / 2;
            var right = leg % 2 != 0;
            var baseZ = pair switch { 0 or 3 => MathF.PI * .25f, _ => MathF.PI * .25f * .74f };
            var baseY = pair switch
            {
                0 => MathF.PI * .25f,
                1 => MathF.PI * .125f,
                2 => -MathF.PI * .125f,
                _ => -MathF.PI * .25f
            };
            var phase = pair switch { 0 => 0, 1 => MathF.PI, 2 => MathF.PI * .5f, _ => MathF.PI * 1.5f };
            var swing = -MathF.Cos(gaitAngle * 2 + phase) * .4f * gaitAmount;
            var lift = MathF.Abs(MathF.Sin(gaitAngle + phase) * .4f) * gaitAmount;
            return (0, (right ? 1 : -1) * (baseY + swing), (right ? -1 : 1) * (baseZ + lift));
        }

        var x = (provider, part) switch
            {
                ("quadruped", "body" or "udders") => MathF.PI / 2,
                ("chicken", "body") => MathF.PI / 2,
                ("chicken", "rightLeg") => MathF.Cos(gaitAngle) * 1.4f * gaitAmount,
                ("chicken", "leftLeg") => MathF.Cos(gaitAngle + MathF.PI) * 1.4f * gaitAmount,
                ("quadruped" or "creeper", "leg1" or "leg4") =>
                    MathF.Cos(gaitAngle) * 1.4f * gaitAmount,
                ("quadruped" or "creeper", "leg2" or "leg3") =>
                    MathF.Cos(gaitAngle + MathF.PI) * 1.4f * gaitAmount,
                ("zombie", "bipedRightLeg") => MathF.Cos(gaitAngle) * 1.4f * gaitAmount,
                ("zombie", "bipedLeftLeg") => MathF.Cos(gaitAngle + MathF.PI) * 1.4f * gaitAmount,
                ("zombie", "bipedRightArm" or "bipedLeftArm") => -MathF.PI / 2,
                ("wolf", "wolfBody" or "wolfMane") => MathF.PI / 2,
                ("wolf", "wolfLeg1" or "wolfLeg4") =>
                    MathF.Cos(gaitAngle) * 1.4f * gaitAmount,
                ("wolf", "wolfLeg2" or "wolfLeg3") =>
                    MathF.Cos(gaitAngle + MathF.PI) * 1.4f * gaitAmount,
                ("wolf", "wolfTail") => MathF.PI * 0.2f,
                _ => 0
            };
        var z = (provider, part) switch
        {
            ("chicken", "rightWing") => .35f + MathF.Cos(gaitAngle) * .2f * gaitAmount,
            ("chicken", "leftWing") => -.35f - MathF.Cos(gaitAngle) * .2f * gaitAmount,
            _ => 0
        };
        return (x, 0, z);
    }
}

internal sealed record EntityImpostorCaptureLayer(string TexturePath, EntityImpostorVertex[][] Poses);
