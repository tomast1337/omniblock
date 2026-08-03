using System.Diagnostics.CodeAnalysis;
using BetaSharp.Client.Rendering.Core;
using BetaSharp.Client.Rendering.Core.OpenGL;
using BetaSharp.Client.Rendering.Entities;
using Silk.NET.Maths;

namespace BetaSharp.Client.Rendering.Entities.Models;

public class BbModelEntityModel : ModelBase
{
    private readonly IReadOnlyDictionary<string, ModelPart> _parts;
    private readonly IReadOnlyList<string> _renderOrder;

    // Assigns each part's LocalSlot once, at construction. RegisterExtraPart also covers parts
    // built by hand outside the bbmodel system (ModelBiped's BipedEars/BipedCloak).
    private int _nextLocalSlot;

    // [min, max) static-vertex-offset span this model's parts occupy, so the instanced renderer
    // can draw one model as a single contiguous vertex range. The running total check below
    // catches it if that's ever not true.
    private int _minStaticVertexOffset = int.MaxValue;
    private int _maxStaticVertexOffsetEnd = int.MinValue;
    private int _totalBakedVertices;

    /// <summary>Start offset, in the shared static GPU buffer, of this model's contiguous vertex range.</summary>
    public int StaticVertexBase => _minStaticVertexOffset;

    /// <summary>Vertex count of this model's contiguous range in the shared static GPU buffer.</summary>
    public int StaticVertexCount => _maxStaticVertexOffsetEnd - _minStaticVertexOffset;

    protected BbModelEntityModel(BbModelBuiltModel built)
    {
        _renderOrder = built.RenderOrder;
        _parts = built.Parts;

        foreach (ModelPart part in _parts.Values)
        {
            RegisterExtraPart(part);
        }
    }

    protected BbModelEntityModel(string entityId, float inflationOffset = 0f)
        : this(BbModelModelBuilder.Build(BbModelLoader.LoadCached(entityId), inflationOffset))
    {
    }

    protected ModelPart GetPart(string name) => !_parts.TryGetValue(name, out ModelPart? part) ? throw new KeyNotFoundException($"Bbmodel bone '{name}' was not found.") : part;

    /// <summary>Assigns the next <see cref="ModelPart.LocalSlot"/> to a part built by hand, outside the bbmodel-driven set.</summary>
    protected void RegisterExtraPart(ModelPart part)
    {
        if (_nextLocalSlot >= ModelPart.MaxPartsPerModel)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} registers more than {ModelPart.MaxPartsPerModel} parts. Raise ModelPart.MaxPartsPerModel.");
        }

        part.LocalSlot = _nextLocalSlot++;

        int start = part.StaticVertexOffset;
        int end = start + part.BakedVertexCount;
        if (start < _minStaticVertexOffset) _minStaticVertexOffset = start;
        if (end > _maxStaticVertexOffsetEnd) _maxStaticVertexOffsetEnd = end;
        _totalBakedVertices += part.BakedVertexCount;

        if (_maxStaticVertexOffsetEnd - _minStaticVertexOffset != _totalBakedVertices)
        {
            throw new InvalidOperationException(
                $"{GetType().Name}'s parts are not contiguous in the shared static GPU buffer.");
        }

        EntityInstanceBatchRenderer.Instance.RegisterStaticGeometry(part, part.GetBakedVertices());
    }

    protected bool TryGetPart(string name, [NotNullWhen(true)] out ModelPart? part) => _parts.TryGetValue(name, out part);

    public override void Render(float limbSwing, float limbSwingAmount, float ageInTicks, float netHeadYaw, float headPitch, float scale)
    {
        SetRotationAngles(limbSwing, limbSwingAmount, ageInTicks, netHeadYaw, headPitch, scale);

        if (EntityInstanceBatchRenderer.Instance.IsActive)
        {
            Span<System.Numerics.Matrix4x4> poseMatrices = stackalloc System.Numerics.Matrix4x4[ModelPart.MaxPartsPerModel];
            foreach (string boneName in _renderOrder)
            {
                ModelPart part = _parts[boneName];
                part.CapturePose(scale);
                if (part.LocalSlot >= 0)
                {
                    poseMatrices[part.LocalSlot] = part.CapturedPose;
                }
            }

            LegacyGL legacyGl = (LegacyGL)GLManager.GL;
            Vector4D<float> tint = ((EmulatedGL)legacyGl).GetCurrentColorTint();
            EntityInstanceBatchRenderer.Instance.SubmitInstance(this, legacyGl.BoundTexture2D, poseMatrices, tint);
        }
        else
        {
            foreach (string boneName in _renderOrder)
            {
                _parts[boneName].Render(scale);
            }
        }
    }
}
