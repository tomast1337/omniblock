namespace OmniBlock.Client.Rendering.Chunks.Lod;

internal readonly record struct TerrainLodLayerSelection(int Level, bool HasGeometry)
{
    public bool Available => Level >= 0;

    public static TerrainLodLayerSelection Select<T>(
        Dictionary<int, T> compiledLevels, int requested, Func<T, bool> hasGeometry)
    {
        // Presence means compilation completed, including an intentionally empty layer. Looking
        // for a non-empty mesh instead resurrects coarse water/solid volumes absent at fine detail.
        if (compiledLevels.TryGetValue(requested, out var exact))
            return new(requested, hasGeometry(exact));

        var selected = -1;
        var distance = long.MaxValue;
        foreach (var level in compiledLevels.Keys)
        {
            var candidateDistance = Math.Abs((long)level - requested);
            if (candidateDistance > distance || (candidateDistance == distance && level >= selected)) continue;
            selected = level;
            distance = candidateDistance;
        }
        return selected < 0 ? new(-1, false) : new(selected, hasGeometry(compiledLevels[selected]));
    }
}
