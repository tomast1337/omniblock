using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Client.Rendering.Core.Textures;

namespace OmniBlock.Client.Rendering.Entities.Models;

public class BbModelEntityModel : ModelBase
{
    private readonly IReadOnlyDictionary<string, ModelPart> _parts;
    private readonly IReadOnlyList<string> _renderOrder;
    private int _maxStaticVertexOffsetEnd = int.MinValue;

    // [min, max) static-vertex-offset span this model's parts occupy, so the instanced renderer
    // can draw one model as a single contiguous vertex range. The running total check below
    // catches it if that's ever not true.

    // Assigns each part's LocalSlot once, at construction. RegisterExtraPart also covers parts
    // built by hand outside the bbmodel system (ModelBiped's BipedEars/BipedCloak).
    private int _nextLocalSlot;
    private int _totalBakedVertices;

    protected BbModelEntityModel(BbModelBuiltModel built)
    {
        _renderOrder = built.RenderOrder;
        _parts = built.Parts;

        foreach (var part in _parts.Values)
        {
            RegisterExtraPart(part);
        }
    }

    protected BbModelEntityModel(string entityId, float inflationOffset = 0f)
        : this(BbModelModelBuilder.Build(BbModelLoader.LoadCached(entityId), inflationOffset))
    {
    }

    /// <summary>Start offset, in the shared static GPU buffer, of this model's contiguous vertex range.</summary>
    public int StaticVertexBase { get; private set; } = int.MaxValue;

    /// <summary>Vertex count of this model's contiguous range in the shared static GPU buffer.</summary>
    public int StaticVertexCount => _maxStaticVertexOffsetEnd - StaticVertexBase;

    protected ModelPart GetPart(string name) => !_parts.TryGetValue(name, out var part) ? throw new KeyNotFoundException($"Bbmodel bone '{name}' was not found.") : part;

    /// <summary>Assigns the next <see cref="ModelPart.LocalSlot" /> to a part built by hand, outside the bbmodel-driven set.</summary>
    protected void RegisterExtraPart(ModelPart part)
    {
        if (_nextLocalSlot >= ModelPart.MaxPartsPerModel)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} registers more than {ModelPart.MaxPartsPerModel} parts. Raise ModelPart.MaxPartsPerModel.");
        }

        part.LocalSlot = _nextLocalSlot++;

        var start = part.StaticVertexOffset;
        var end = start + part.BakedVertexCount;
        if (start < StaticVertexBase) StaticVertexBase = start;
        if (end > _maxStaticVertexOffsetEnd) _maxStaticVertexOffsetEnd = end;
        _totalBakedVertices += part.BakedVertexCount;

        if (_maxStaticVertexOffsetEnd - StaticVertexBase != _totalBakedVertices)
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
            Span<Matrix4x4> poseMatrices = stackalloc Matrix4x4[ModelPart.MaxPartsPerModel];
            foreach (var boneName in _renderOrder)
            {
                var part = _parts[boneName];
                part.CapturePose(scale);
                if (part.LocalSlot >= 0)
                {
                    poseMatrices[part.LocalSlot] = part.CapturedPose;
                }
            }

            var tint = RenderSystem.Color;
            // See EntityBatchRenderer's identical BoundTextureId: WebGPU has no binding point to
            // read back from, so what the caller bound is tracked on Texture2D itself.
            var boundTexture = Texture2D.Bound?.Id ?? 0;
            EntityInstanceBatchRenderer.Instance.SubmitInstance(this, boundTexture, poseMatrices, tint);
        }
        else
        {
            foreach (var boneName in _renderOrder)
            {
                _parts[boneName].Render(scale);
            }
        }
    }
}
