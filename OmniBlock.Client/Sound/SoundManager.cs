using OmniBlock.Client.Options;
using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using SFML.Audio;
using SFML.System;

namespace OmniBlock.Client.Sound;

public class SoundManager : IDisposable
{
    private const int MaxChannels = 32;
    private static bool s_started;

    private readonly Dictionary<ResourceLocation, MusicCategory> _musicCategories = [];
    private readonly JavaRandom _rand = new();

    private readonly Dictionary<string, List<SoundBuffer>> _soundBuffers = [];
    private readonly SFML.Audio.Sound?[] _soundChannels = new SFML.Audio.Sound?[MaxChannels];
    private readonly SoundPool _soundPoolSounds = new();
    private readonly SoundPool _soundPoolStreaming = new();

    private Music? _currentMusic;
    private Music? _currentStreaming;
    private GameOptions? _options;
    private GameOptions Options => _options ?? throw new InvalidOperationException("Sound settings have not been loaded.");

    private int _soundSourceSuffix;

    public int ActiveChannelCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < MaxChannels; i++)
            {
                if (_soundChannels[i] is { Status: SoundStatus.Playing })
                    count++;
            }

            return count;
        }
    }

    public int LoadedSoundNameCount => _soundBuffers.Count;
    public int LoadedSoundFileCount => _soundPoolSounds.LoadedSoundCount;
    public int LoadedStreamingFileCount => _soundPoolStreaming.LoadedSoundCount;
    public bool IsMusicPlaying => _currentMusic != null && _currentMusic.Status == SoundStatus.Playing;
    public bool IsStreamingPlaying => _currentStreaming != null && _currentStreaming.Status == SoundStatus.Playing;
    public ResourceLocation? ActiveCategory { get; private set; }
    public IReadOnlyDictionary<ResourceLocation, MusicCategory> MusicCategories => _musicCategories;
    public string? CurrentMusicName { get; private set; }
    public string? CurrentStreamingName { get; private set; }

    public void Dispose()
    {
        if (!s_started) return;

        _currentMusic?.Stop();
        _currentMusic?.Dispose();
        _currentMusic = null;
        _currentStreaming?.Stop();
        _currentStreaming?.Dispose();
        _currentStreaming = null;

        for (var i = 0; i < MaxChannels; i++)
        {
            if (_soundChannels[i] is { } channel)
            {
                channel.Stop();
                channel.Dispose();
                _soundChannels[i] = null;
            }
        }

        foreach (var bufferList in _soundBuffers.Values)
        {
            foreach (var buffer in bufferList)
            {
                buffer.Dispose();
            }
        }

        _soundBuffers.Clear();
    }

    public void RegisterMusicCategory(ResourceLocation name, int minDelayTicks, int maxDelayTicks) => _musicCategories[name] = new MusicCategory(name, minDelayTicks, maxDelayTicks);

    public void LoadSoundSettings(GameOptions options)
    {
        _soundPoolStreaming.IsRandom = false;
        _options = options;
        if (!s_started && (options == null || options.SoundVolume != 0.0F || options.MusicVolume != 0.0F))
        {
            TryToSetLibraryAndCodecs();
        }
    }

    private static string SanitizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        if (path.StartsWith('/') && path.Length >= 3 && path[2] == ':')
        {
            path = path[1..];
        }

        var separator = Path.DirectorySeparatorChar;
        return path.Replace('/', separator).Replace('\\', separator);
    }

    private void TryToSetLibraryAndCodecs()
    {
        var soundVolume = Options.SoundVolume;
        var musicVolume = Options.MusicVolume;
        Options.SoundVolume = 0.0F;
        Options.MusicVolume = 0.0F;
        Options.SaveOptions();

        Options.SoundVolume = soundVolume;
        Options.MusicVolume = musicVolume;
        Options.SaveOptions();

        s_started = true;
    }

    public void OnSoundOptionsChanged()
    {
        if (!s_started && (Options.SoundVolume != 0.0F || Options.MusicVolume != 0.0F))
        {
            TryToSetLibraryAndCodecs();
        }

        if (s_started)
        {
            if (Options.MusicVolume == 0.0F)
            {
                _currentMusic?.Stop();
            }
            else
            {
                _currentMusic?.Volume = Options.MusicVolume * 100.0F;
            }
        }
    }

    public void AddSound(string name, FileInfo file)
    {
        _soundPoolSounds.AddSound(name, file);
        LoadSoundBuffer(name, file);
    }

    public void AddStreaming(string name, FileInfo file) => _soundPoolStreaming.AddSound(name, file);

    public void AddMusic(ResourceLocation category, string name, FileInfo file)
    {
        if (_musicCategories.TryGetValue(category, out var musicCategory))
        {
            musicCategory.Pool.AddSound(name, file);
        }
    }

    private void LoadSoundBuffer(string name, FileInfo file)
    {
        var filepath = SanitizePath(file.FullName);
        var resourceName = name;

        var dotIndex = resourceName.IndexOf('.');
        if (dotIndex >= 0)
        {
            resourceName = resourceName[..dotIndex];
        }

        if (_soundPoolSounds.IsRandom)
        {
            while (resourceName.Length > 0 && char.IsDigit(resourceName[resourceName.Length - 1]))
            {
                resourceName = resourceName[..^1];
            }
        }

        resourceName = resourceName.Replace("/", ".");

        if (!_soundBuffers.TryGetValue(resourceName, out var value))
        {
            value = [];
            _soundBuffers[resourceName] = value;
        }

        SoundBuffer buffer = new(filepath);
        value.Add(buffer);
    }

    private SoundBuffer? getRandomSoundBuffer(string? name)
    {
        if (name == null)
        {
            return null;
        }

        if (!_soundBuffers.TryGetValue(name, out var value) || value.Count == 0)
        {
            return null;
        }

        var index = _rand.NextInt(value.Count);
        return value[index];
    }

    private SFML.Audio.Sound getFreeSoundChannel(SoundBuffer buffer)
    {
        for (var i = 0; i < MaxChannels; i++)
        {
            if (_soundChannels[i] is not { } channel)
            {
                channel = new SFML.Audio.Sound(buffer);
                _soundChannels[i] = channel;
                return channel;
            }

            if (channel.Status == SoundStatus.Stopped)
            {
                channel.SoundBuffer = buffer;
                return channel;
            }
        }

        var stolen = _soundChannels[0] ?? new SFML.Audio.Sound(buffer);
        _soundChannels[0] = stolen;
        stolen.Stop();
        stolen.SoundBuffer = buffer;
        return stolen;
    }

    public void PlayRandomMusicIfReady(ResourceLocation category)
    {
        if (!s_started || Options.MusicVolume == 0.0F) return;

        if (!_musicCategories.TryGetValue(category, out var musicCategory)) return;

        var isMusicPlaying = _currentMusic != null && _currentMusic.Status == SoundStatus.Playing;
        var isStreamingPlaying = _currentStreaming != null && _currentStreaming.Status == SoundStatus.Playing;

        if ((isMusicPlaying || isStreamingPlaying) && ActiveCategory == category) return;

        if (musicCategory.TicksBeforeNext > 0)
        {
            --musicCategory.TicksBeforeNext;
            return;
        }

        var entry = musicCategory.Pool.GetRandomSound();
        if (entry == null) return;

        musicCategory.ResetDelay();

        _currentMusic?.Stop();
        _currentMusic?.Dispose();
        _currentMusic = null;

        var musicName = SanitizePath(entry.SoundUrl.LocalPath);

        _currentMusic = new Music(musicName)
        {
            Volume = Options.MusicVolume * 100.0F,
            IsLooping = false,
            RelativeToListener = true,
            Position = new Vector3f(0, 0, 0)
        };

        _currentMusic.Play();
        ActiveCategory = category;
        CurrentMusicName = entry.SoundName;
    }

    public void StopCurrentMusic()
    {
        _currentMusic?.Stop();
        _currentMusic?.Dispose();
        _currentMusic = null;
        _currentStreaming?.Stop();
        _currentStreaming?.Dispose();
        _currentStreaming = null;
        ActiveCategory = null;
        CurrentMusicName = null;
        CurrentStreamingName = null;
    }

    public void StopMusic(ResourceLocation? category = null)
    {
        if (category == null || ActiveCategory == category)
        {
            StopCurrentMusic();

            if (category != null && _musicCategories.TryGetValue(category, out var musicCategory))
            {
                musicCategory.TicksBeforeNext = 0;
            }
        }
    }

    public void UpdateListener(EntityLiving? player, float partialTicks)
    {
        if (!s_started || Options.SoundVolume == 0.0F || player == null) return;


        var yaw = player.PrevYaw + (player.Yaw - player.PrevYaw) * partialTicks;
        var x = player.PrevX + (player.X - player.PrevX) * partialTicks;
        var y = player.PrevY + (player.Y - player.PrevY) * partialTicks;
        var z = player.PrevZ + (player.Z - player.PrevZ) * partialTicks;

        var lookX = MathHelper.Cos(-yaw * ((float)Math.PI / 180.0F) - (float)Math.PI);
        var lookY = MathHelper.Sin(-yaw * ((float)Math.PI / 180.0F) - (float)Math.PI);

        Listener.Position = new Vector3f((float)x, (float)y, (float)z);
        Listener.Direction = new Vector3f(-lookY, 0.0F, -lookX);
        Listener.UpVector = new Vector3f(0.0F, 1.0F, 0.0F);
    }

    public void PlayStreaming(string? name, float x, float y, float z, float volume, float pitch)
    {
        if (!(s_started && Options.SoundVolume != 0.0F)) return;

        if (_currentStreaming != null && _currentStreaming.Status == SoundStatus.Playing)
        {
            _currentStreaming.Stop();
        }

        if (name == null) return;

        var entry = _soundPoolStreaming.GetRandomSoundFromSoundPool(name);
        if (entry == null || volume <= 0.0F) return;


        if (_currentMusic != null && _currentMusic.Status == SoundStatus.Playing)
        {
            _currentMusic.Stop();
        }

        _currentStreaming?.Dispose();
        _currentStreaming = new Music(SanitizePath(entry.SoundUrl.LocalPath))
        {
            Volume = 0.5F * Options.SoundVolume * 100.0F,
            IsLooping = false,
            RelativeToListener = false,
            Position = new Vector3f(x, y, z)
        };

        _currentStreaming.Play();
        CurrentStreamingName = entry.SoundName;
    }

    public void PlayBreakSound(BlockSoundGroup soundGroup, int x, int y, int z) =>
        PlaySound(soundGroup.BreakSound, x + 0.5F, y + 0.5F, z + 0.5F, (soundGroup.Volume + 1.0F) / 2.0F, soundGroup.Pitch * 0.8F);

    public void PlayStepSound(BlockSoundGroup soundGroup, int x, int y, int z) =>
        PlaySound(soundGroup.StepSound, x + 0.5F, y + 0.5F, z + 0.5F, (soundGroup.Volume + 1.0F) / 8.0F, soundGroup.Pitch * 0.5F);

    public void PlayDoorSound(int x, int y, int z) => PlayDoorSound(x, y, z, _rand.NextDouble() < 0.5D);

    public void PlayDoorSound(int x, int y, int z, bool state) =>
        PlaySound(state ? "random.door_open" : "random.door_close", x + 0.5F, y + 0.5F, z + 0.5F, 1.0F, _rand.NextFloat() * 0.1F + 0.9F);

    public void PlaySound(string name, float x, float y, float z, float volume, float pitch)
    {
        if (!(s_started && Options.SoundVolume != 0.0F)) return;

        var buffer = getRandomSoundBuffer(name);
        if (buffer == null || volume <= 0.0F) return;


        _soundSourceSuffix = (_soundSourceSuffix + 1) % 256;

        var sound = getFreeSoundChannel(buffer);

        sound.Position = new Vector3f(x, y, z);
        sound.RelativeToListener = false;

        var minDistance = 16.0F;
        if (volume > 1.0F)
        {
            minDistance *= volume;
        }

        sound.MinDistance = minDistance;
        sound.Attenuation = 2.0F;

        sound.Pitch = pitch;

        var finalVolume = volume;
        if (finalVolume > 1.0F)
        {
            finalVolume = 1.0F;
        }

        sound.Volume = finalVolume * Options.SoundVolume * 100.0F;

        sound.Play();
    }

    public void PlaySoundFX(string name, float volume, float pitch)
    {
        if (!(s_started && Options.SoundVolume != 0.0F)) return;

        var buffer = getRandomSoundBuffer(name);
        if (buffer == null) return;

        _soundSourceSuffix = (_soundSourceSuffix + 1) % 256;

        var sound = getFreeSoundChannel(buffer);

        sound.RelativeToListener = true;
        sound.Position = new Vector3f(0.0F, 0.0F, 0.0F);

        sound.Pitch = pitch;

        var finalVolume = volume;
        if (finalVolume > 1.0F)
        {
            finalVolume = 1.0F;
        }

        finalVolume *= 0.25F;
        sound.Volume = finalVolume * Options.SoundVolume * 100.0F;

        sound.MinDistance = 1.0f;
        sound.Attenuation = 1.0f;

        sound.Play();
    }
}
