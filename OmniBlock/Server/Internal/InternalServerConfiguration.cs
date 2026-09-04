namespace OmniBlock.Server.Internal;

internal class InternalServerConfiguration : IServerConfiguration
{
    private readonly string _levelName;
    private readonly string _levelOptions;
    private readonly string _levelType;
    private readonly string _seed;
    private int _viewDistance;

    public InternalServerConfiguration(string levelName, string levelType, string seed, string levelOptions, int viewDistance)
    {
        _levelName = levelName;
        _levelType = levelType;
        _seed = seed;
        _levelOptions = levelOptions;
        _viewDistance = viewDistance;
    }

    public bool GetAllowFlight(bool fallback) => true;

    public bool GetAllowNether(bool fallback) => true;

    public string GetLevelName(string fallback) => _levelName;

    public string GetLevelType(string fallback) => _levelType ?? fallback;

    public string GetLevelSeed(string fallback) => _seed;

    public string GetLevelOptions(string fallback) => _levelOptions ?? fallback;

    public int GetMaxPlayers(int fallback) => 1;

    public bool GetOnlineMode(bool fallback) => false;

    public bool GetProperty(string property, bool fallback) => false;

    public int GetProperty(string property, int fallback) => -1;

    public string GetProperty(string property, string fallback) => string.Empty;

    public bool GetPvpEnabled(bool fallback) => false;

    public string GetServerIp(string fallback) => "";

    public bool GetDualStack(bool fallback) => false;

    public int GetServerPort(int fallback) => 25565;

    public bool GetSpawnAnimals(bool fallback) => true;

    public bool GetSpawnMonsters(bool fallback) => true;

    public int GetViewDistance(int fallback) => _viewDistance;

    public bool GetWhiteList(bool fallback) => false;

    public int GetSpawnRegionSize(int fallback) => fallback;

    public string GetDefaultGamemode(string fallback) => fallback;

    public void Save()
    {
    }

    public void SetProperty(string property, bool value)
    {
    }

    public void SetViewDistance(int distance) => _viewDistance = distance;
}
