namespace BetaSharp.Client.Rendering.Entities;

/// <summary>
/// Symbolic ids the entity shader branches on: which mob is being drawn, and which part of it.
/// <para>
/// Both come from a properties file next to the shaders rather than from constants, so a shader
/// pack can be written against stable names and the ids regrouped without touching C#. A name
/// absent from its file resolves to 0, which every shader treats as passthrough — an unmapped
/// texture or an unnamed bone renders exactly as it did before any of this existed.
/// </para>
/// </summary>
internal static class EntityShaderIds
{
    private static readonly Dictionary<string, int> s_textureIds = Load("shaders/entity_textures.properties");
    private static readonly Dictionary<string, uint> s_partIds =
        Load("shaders/entity_parts.properties").ToDictionary(e => e.Key, e => (uint)e.Value, StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves an asset path such as "/mob/cow.png". Leading slashes are optional.</summary>
    public static int ForTexture(string assetPath) =>
        s_textureIds.GetValueOrDefault(assetPath.TrimStart('/'));

    /// <summary>Resolves a bbmodel bone name such as "bipedHead".</summary>
    public static uint ForPart(string? boneName) =>
        boneName is null ? 0u : s_partIds.GetValueOrDefault(boneName);

    private static Dictionary<string, int> Load(string assetPath)
    {
        Dictionary<string, int> ids = new(StringComparer.OrdinalIgnoreCase);

        try
        {
            string text = AssetManager.Instance.getAsset(assetPath).GetTextContent();
            foreach (string line in text.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed[0] == '#') continue;

                int eq = trimmed.IndexOf('=');
                if (eq < 0) continue;

                if (int.TryParse(trimmed[(eq + 1)..].Trim(), out int id))
                {
                    ids[trimmed[..eq].Trim()] = id;
                }
            }
        }
        catch
        {
            // Missing or unparseable: everything resolves to 0 and the shader behaves as it did
            // before ids existed. A broken mapping file is not worth failing startup over.
        }

        return ids;
    }
}
