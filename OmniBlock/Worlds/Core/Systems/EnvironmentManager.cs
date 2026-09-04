using OmniBlock.Entities;
using OmniBlock.Util.Maths;
using Silk.NET.Maths;

namespace OmniBlock.Worlds.Core.Systems;

public class EnvironmentManager
{
    private readonly IWorldContext _world;
    private readonly long _worldTimeMask = 0xFFFFFFL;

    public EnvironmentManager(IWorldContext world) => _world = world;

    public float PrevRainingStrength { get; private set; }
    public float RainingStrength { get; private set; }
    public float PrevThunderingStrength { get; private set; }
    public float ThunderingStrength { get; private set; }

    public int TicksSinceLightning { get; set; }
    public int LightningTicksLeft { get; set; }

    public int AmbientDarkness { get; set; }
    public bool IsRaining => GetRainGradient(1.0F) > 0.2D;
    public event Action<bool>? OnRainingStateChanged;

    public void PrepareWeather()
    {
        if (!_world.Properties.IsRaining) return;

        RainingStrength = 1.0F;
        if (_world.Properties.IsThundering)
        {
            ThunderingStrength = 1.0F;
        }
    }

    public void UpdateWeatherCycles()
    {
        if (_world.Dimension.HasCeiling)
        {
            return;
        }

        var wasRaining = IsRaining;

        if (TicksSinceLightning > 0)
        {
            --TicksSinceLightning;
        }

        var thunderTime = _world.Properties.ThunderTime;
        if (thunderTime <= 0)
        {
            _world.Properties.ThunderTime = _world.Properties.IsThundering ? _world.Random.NextInt(12000) + 3600 : _world.Random.NextInt(168000) + 12000;
        }
        else
        {
            --thunderTime;
            _world.Properties.ThunderTime = thunderTime;
            if (thunderTime <= 0)
            {
                _world.Properties.IsThundering = !_world.Properties.IsThundering;
            }
        }

        var rainTime = _world.Properties.RainTime;
        if (rainTime <= 0)
        {
            _world.Properties.RainTime = _world.Properties.IsRaining ? _world.Random.NextInt(12000) + 12000 : _world.Random.NextInt(168000) + 12000;
        }
        else
        {
            --rainTime;
            _world.Properties.RainTime = rainTime;
            if (rainTime <= 0)
            {
                _world.Properties.IsRaining = !_world.Properties.IsRaining;
            }
        }

        PrevRainingStrength = RainingStrength;
        RainingStrength += _world.Properties.IsRaining ? 0.01F : -0.01F;
        RainingStrength = Math.Clamp(RainingStrength, 0.0F, 1.0F);

        PrevThunderingStrength = ThunderingStrength;
        ThunderingStrength += _world.Properties.IsThundering ? 0.01F : -0.01F;
        ThunderingStrength = Math.Clamp(ThunderingStrength, 0.0F, 1.0F);

        if (wasRaining != IsRaining)
        {
            OnRainingStateChanged?.Invoke(IsRaining);
        }
    }

    public void ClearWeather()
    {
        _world.Properties.RainTime = 0;
        _world.Properties.IsRaining = false;
        _world.Properties.ThunderTime = 0;
        _world.Properties.IsThundering = false;
    }

    public float GetTime(float delta) => _world.Dimension.GetTimeOfDay(_world.Properties.WorldTime, delta);

    public int GetAmbientDarkness(float delta)
    {
        var timeOfDay = GetTime(delta);
        var sunIntensity = 1.0F - (MathHelper.Cos(timeOfDay * (float)Math.PI * 2.0F) * 2.0F + 0.5F);
        sunIntensity = Math.Clamp(sunIntensity, 0.0F, 1.0F);

        var lightLevel = 1.0F - sunIntensity;
        lightLevel = (float)(lightLevel * (1.0D - GetRainGradient(delta) * 5.0F / 16.0D));
        lightLevel = (float)(lightLevel * (1.0D - GetThunderGradient(delta) * 5.0F / 16.0D));

        return (int)((1.0F - lightLevel) * 11.0F);
    }

    public void UpdateSkyBrightness()
    {
        var darkness = GetAmbientDarkness(1.0F);
        if (darkness != AmbientDarkness)
        {
            AmbientDarkness = darkness;
        }
    }

    public float GetThunderGradient(float delta) => (PrevThunderingStrength + (ThunderingStrength - PrevThunderingStrength) * delta) * GetRainGradient(delta);
    public float GetRainGradient(float delta) => PrevRainingStrength + (RainingStrength - PrevRainingStrength) * delta;
    public void SetRainGradient(float rainGradient) => PrevRainingStrength = RainingStrength = rainGradient;
    public void SetThunderGradient(float thunderGradient) => PrevThunderingStrength = ThunderingStrength = thunderGradient;

    public bool IsThundering() => GetThunderGradient(1.0F) > 0.9D;

    public bool IsRainingAt(int x, int y, int z)
    {
        if (!IsRaining || y < _world.Reader.GetTopSolidBlockY(x, z))
        {
            return false;
        }

        var biome = _world.Dimension.BiomeSource.GetBiome(x, z);
        return !biome.GetEnableSnow() && biome.CanSpawnLightningBolt();
    }

    public void SkipNightAndClearWeather()
    {
        var nextWorldTime = _world.Properties.WorldTime + 24000L;
        _world.Properties.WorldTime = nextWorldTime - nextWorldTime % 24000L;
        ClearWeather();
    }

    public Vector3D<double> GetCloudColor(float partialTicks)
    {
        var timeOfDay = _world.Dimension.GetTimeOfDay(_world.Properties.WorldTime, partialTicks);

        var sunIntensity = MathHelper.Cos(timeOfDay * (float)Math.PI * 2.0F) * 2.0F + 0.5F;
        sunIntensity = Math.Clamp(sunIntensity, 0.0F, 1.0F);

        var red = ((_worldTimeMask >> 16) & 255L) / 255.0F;
        var green = ((_worldTimeMask >> 8) & 255L) / 255.0F;
        var blue = (_worldTimeMask & 255L) / 255.0F;

        var rainStrength = GetRainGradient(partialTicks);
        if (rainStrength > 0.0F)
        {
            var grayscaleLuminance = (red * 0.3F + green * 0.59F + blue * 0.11F) * 0.6F;
            var rainFactor = 1.0F - rainStrength * 0.95F;

            red = red * rainFactor + grayscaleLuminance * (1.0F - rainFactor);
            green = green * rainFactor + grayscaleLuminance * (1.0F - rainFactor);
            blue = blue * rainFactor + grayscaleLuminance * (1.0F - rainFactor);
        }

        red *= sunIntensity * 0.9F + 0.1F;
        green *= sunIntensity * 0.9F + 0.1F;
        blue *= sunIntensity * 0.85F + 0.15F;

        var thunderStrength = GetThunderGradient(partialTicks);
        if (thunderStrength > 0.0F)
        {
            var grayscaleLuminance = (red * 0.3F + green * 0.59F + blue * 0.11F) * 0.2F;
            var thunderFactor = 1.0F - thunderStrength * 0.95F;

            red = red * thunderFactor + grayscaleLuminance * (1.0F - thunderFactor);
            green = green * thunderFactor + grayscaleLuminance * (1.0F - thunderFactor);
            blue = blue * thunderFactor + grayscaleLuminance * (1.0F - thunderFactor);
        }

        return new Vector3D<double>(red, green, blue);
    }

    public Vector3D<double> GetSkyColor(Entity entity, float partialTicks)
    {
        var timeOfDay = _world.Dimension.GetTimeOfDay(_world.Properties.WorldTime, partialTicks);

        var sunIntensity = MathHelper.Cos(timeOfDay * (float)Math.PI * 2.0F) * 2.0F + 0.5F;
        sunIntensity = Math.Clamp(sunIntensity, 0.0F, 1.0F);

        var blockX = MathHelper.Floor(entity.X);
        var blockZ = MathHelper.Floor(entity.Z);
        var temperature = (float)_world.Dimension.BiomeSource.GetTemperature(blockX, blockZ);
        var biomeSkyColorInt = _world.Dimension.BiomeSource.GetBiome(blockX, blockZ).GetSkyColorByTemp(temperature);

        var red = ((biomeSkyColorInt >> 16) & 255) / 255.0F;
        var green = ((biomeSkyColorInt >> 8) & 255) / 255.0F;
        var blue = (biomeSkyColorInt & 255) / 255.0F;

        red *= sunIntensity;
        green *= sunIntensity;
        blue *= sunIntensity;

        var rainStrength = GetRainGradient(partialTicks);
        if (rainStrength > 0.0F)
        {
            var grayscaleLuminance = (red * 0.3F + green * 0.59F + blue * 0.11F) * 0.6F;
            var rainFactor = 1.0F - rainStrength * (12.0F / 16.0F);

            red = red * rainFactor + grayscaleLuminance * (1.0F - rainFactor);
            green = green * rainFactor + grayscaleLuminance * (1.0F - rainFactor);
            blue = blue * rainFactor + grayscaleLuminance * (1.0F - rainFactor);
        }

        var thunderStrength = GetThunderGradient(partialTicks);
        if (thunderStrength > 0.0F)
        {
            var grayscaleLuminance = (red * 0.3F + green * 0.59F + blue * 0.11F) * 0.2F;
            var thunderFactor = 1.0F - thunderStrength * (12.0F / 16.0F);

            red = red * thunderFactor + grayscaleLuminance * (1.0F - thunderFactor);
            green = green * thunderFactor + grayscaleLuminance * (1.0F - thunderFactor);
            blue = blue * thunderFactor + grayscaleLuminance * (1.0F - thunderFactor);
        }

        if (LightningTicksLeft <= 0) return new Vector3D<double>(red, green, blue);


        var lightningFactor = LightningTicksLeft - partialTicks;
        if (lightningFactor > 1.0F)
        {
            lightningFactor = 1.0F;
        }

        lightningFactor *= 0.45F;

        red = red * (1.0F - lightningFactor) + 0.8F * lightningFactor;
        green = green * (1.0F - lightningFactor) + 0.8F * lightningFactor;
        blue = blue * (1.0F - lightningFactor) + 1.0F * lightningFactor;

        return new Vector3D<double>(red, green, blue);
    }

    public bool CanMonsterSpawn() => AmbientDarkness < 4;
}
