using OmniBlock.Util.Maths;

namespace OmniBlock.Client.Sound;

public class SoundPool
{
    private readonly List<SoundPoolEntry> _allLoadedSounds = [];
    private readonly JavaRandom _rand = new();
    private readonly Dictionary<string, List<SoundPoolEntry>> _weightedSoundSet = [];
    public bool IsRandom { get; set; } = true;
    public int LoadedSoundCount => _allLoadedSounds.Count;


    public SoundPoolEntry AddSound(string soundPath, FileInfo fileInfo)
    {
        var soundKey = soundPath;
        var dotIndex = soundKey.IndexOf('.');

        if (dotIndex != -1)
        {
            soundKey = soundKey[..dotIndex];
        }

        if (IsRandom)
        {
            while (soundKey.Length > 0 && char.IsDigit(soundKey[^1]))
            {
                soundKey = soundKey[..^1];
            }
        }

        soundKey = soundKey.Replace('/', '.');

        if (!_weightedSoundSet.TryGetValue(soundKey, out var variations))
        {
            variations = [];
            _weightedSoundSet[soundKey] = variations;
        }

        SoundPoolEntry entry = new(soundPath, new Uri(fileInfo.FullName));

        variations.Add(entry);
        _allLoadedSounds.Add(entry);

        return entry;
    }

    public SoundPoolEntry? GetRandomSoundFromSoundPool(string soundKey)
    {
        if (_weightedSoundSet.TryGetValue(soundKey, out var variations) && variations.Count > 0)
        {
            return variations[_rand.NextInt(variations.Count)];
        }

        return null;
    }

    public SoundPoolEntry? GetRandomSound() => _allLoadedSounds.Count == 0 ? null : _allLoadedSounds[_rand.NextInt(_allLoadedSounds.Count)];
}
