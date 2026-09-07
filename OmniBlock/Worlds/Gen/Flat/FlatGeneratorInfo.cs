using System.Text;
using OmniBlock.Blocks;
using OmniBlock.Registries;

namespace OmniBlock.Worlds.Gen.Flat;

public class FlatGeneratorInfo
{
    public int Biome { get; set; } = 1;

    public Dictionary<string, Dictionary<string, string>> WorldFeatures { get; } = [];
    public List<FlatLayerInfo> FlatLayers { get; } = [];

    public void UpdateLayerHeights()
    {
        var totalHeight = 0;
        foreach (var layer in FlatLayers)
        {
            layer.MinY = totalHeight;
            totalHeight += layer.LayerCount;
        }
    }

    public override string ToString()
    {
        StringBuilder sb = new();
        sb.Append(2); // Version 2
        sb.Append(";");

        for (var i = 0; i < FlatLayers.Count; ++i)
        {
            if (i > 0) sb.Append(",");
            sb.Append(FlatLayers[i]);
        }

        sb.Append(";");
        sb.Append(Biome);

        if (WorldFeatures.Count > 0)
        {
            sb.Append(";");
            var i = 0;
            foreach (var feature in WorldFeatures)
            {
                if (i++ > 0) sb.Append(",");
                sb.Append(feature.Key.ToLower());

                if (feature.Value.Count > 0)
                {
                    sb.Append("(");
                    var j = 0;
                    foreach (var param in feature.Value)
                    {
                        if (j++ > 0) sb.Append(" ");
                        sb.Append(param.Key);
                        sb.Append("=");
                        sb.Append(param.Value);
                    }

                    sb.Append(")");
                }
            }
        }
        else
        {
            sb.Append(";");
        }

        return sb.ToString();
    }

    private static FlatLayerInfo? ParseLayer(string input, int minY, IBlockRuntimeView blocks)
    {
        var parts = input.Split('x');
        var count = 1;
        var meta = 0;

        int blockId;
        try
        {
            if (parts.Length == 2)
            {
                count = int.Parse(parts[0]);
            }

            var blockData = parts[^1];
            var blockParts = blockData.Split(':');
            blockId = int.Parse(blockParts[0]);

            if (blockParts.Length > 1)
            {
                meta = int.Parse(blockParts[1]);
            }

            if (!blocks.TryGetByProtocolId(blockId, out _))
            {
                blockId = 0;
                meta = 0;
            }

            if (meta < 0 || meta > 15) meta = 0;
        }
        catch
        {
            return null;
        }

        return new FlatLayerInfo(count, blockId, meta)
        {
            MinY = minY
        };
    }

    public static FlatGeneratorInfo CreateFromString(string input, IBlockRuntimeView blocks)
    {
        if (string.IsNullOrEmpty(input))
        {
            return GetDefault(blocks);
        }

        var parts = input.Split(';');
        var version = parts.Length > 0 ? int.Parse(parts[0]) : 0;

        FlatGeneratorInfo info = new();
        var partIndex = parts.Length == 1 ? 0 : 1;

        if (parts.Length > partIndex)
        {
            var layers = parts[partIndex++].Split(',');
            var currentY = 0;
            foreach (var layerStr in layers)
            {
                var layer = ParseLayer(layerStr, currentY, blocks);
                if (layer != null)
                {
                    info.FlatLayers.Add(layer);
                    currentY += layer.LayerCount;
                }
            }
        }

        if (version > 0 && parts.Length > partIndex)
        {
            info.Biome = int.Parse(parts[partIndex++]);
        }

        if (version > 0 && parts.Length > partIndex)
        {
            var features = parts[partIndex++].ToLower().Split(',');
            foreach (var featureStr in features)
            {
                var featureParts = featureStr.Split('(');
                var featureName = featureParts[0];
                if (string.IsNullOrEmpty(featureName)) continue;

                var featureParams = new Dictionary<string, string>();
                info.WorldFeatures[featureName] = featureParams;

                if (featureParts.Length > 1 && featureParts[1].EndsWith(")"))
                {
                    var paramsStr = featureParts[1][..^1];
                    var paramPairs = paramsStr.Split(' ');
                    foreach (var pair in paramPairs)
                    {
                        var kv = pair.Split('=');
                        if (kv.Length == 2)
                        {
                            featureParams[kv[0]] = kv[1];
                        }
                    }
                }
            }
        }
        else
        {
            info.WorldFeatures["village"] = [];
        }

        return info;
    }

    public static FlatGeneratorInfo GetDefault(IBlockRuntimeView blocks)
    {
        FlatGeneratorInfo info = new()
        {
            Biome = 1
        };

        info.FlatLayers.Add(new FlatLayerInfo(1, blocks.Get("bedrock").Id));
        info.FlatLayers.Add(new FlatLayerInfo(2, blocks.Get("dirt").Id));
        info.FlatLayers.Add(new FlatLayerInfo(1, blocks.Get("grass_block").Id));
        info.UpdateLayerHeights();
        info.WorldFeatures["village"] = [];
        return info;
    }
}
