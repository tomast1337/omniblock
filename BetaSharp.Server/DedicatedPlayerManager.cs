using java.io;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Server;

internal class DedicatedPlayerManager : PlayerManager
{
    private readonly ILogger<DedicatedPlayerManager> _logger = Log.Instance.For<DedicatedPlayerManager>();
    private readonly java.io.File BANNED_PLAYERS_FILE;
    private readonly java.io.File BANNED_IPS_FILE;
    private readonly java.io.File OPERATORS_FILE;
    private readonly java.io.File WHITELIST_FILE;

    public DedicatedPlayerManager(BetaSharpServer server) : base(server)
    {
        BANNED_PLAYERS_FILE = server.getFile("banned-players.txt");
        BANNED_IPS_FILE = server.getFile("banned-ips.txt");
        OPERATORS_FILE = server.getFile("ops.txt");
        WHITELIST_FILE = server.getFile("white-list.txt");

        loadBannedPlayers();
        loadBannedIps();
        loadOperators();
        loadWhitelist();
        saveBannedPlayers();
        saveBannedIps();
        saveOperators();
        saveWhitelist();
    }

    protected override void loadBannedPlayers()
    {
        try
        {
            bannedPlayers.Clear();
            BufferedReader reader = new(new FileReader(BANNED_PLAYERS_FILE));
            string entry = "";

            while ((entry = reader.readLine()) != null)
            {
                bannedPlayers.Add(entry.Trim().ToLower());
            }

            reader.close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"Failed to load ban list: {exception}");
        }
    }

    protected override void saveBannedPlayers()
    {
        try
        {
            PrintWriter writer = new(new FileWriter(BANNED_PLAYERS_FILE, false));

            foreach (string bannedPlayer in bannedPlayers)
            {
                writer.println(bannedPlayer);
            }

            writer.close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"Failed to save ban list: {exception}");
        }
    }

    protected override void loadBannedIps()
    {
        try
        {
            bannedIps.Clear();
            BufferedReader reader = new(new FileReader(BANNED_IPS_FILE));
            string entry = "";

            while ((entry = reader.readLine()) != null)
            {
                bannedIps.Add(entry.Trim().ToLower());
            }

            reader.close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"Failed to load ip ban list: {exception}");
        }
    }

    protected override void saveBannedIps()
    {
        try
        {
            PrintWriter writer = new(new FileWriter(BANNED_IPS_FILE, false));

            foreach (string bannedIp in bannedIps)
            {
                writer.println(bannedIp);
            }

            writer.close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"Failed to save ip ban list: {exception}");
        }
    }

    protected override void loadOperators()
    {
        try
        {
            ops.Clear();
            BufferedReader reader = new(new FileReader(OPERATORS_FILE));
            string entry = "";

            while ((entry = reader.readLine()) != null)
            {
                ops.Add(entry.Trim().ToLower());
            }

            reader.close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"Failed to load ip ban list: {exception}");
        }
    }

    protected override void saveOperators()
    {
        try
        {
            PrintWriter writer = new(new FileWriter(OPERATORS_FILE, false));

            foreach (string op in ops)
            {
                writer.println(op);
            }

            writer.close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"Failed to save ip ban list: {exception}");
        }
    }

    protected override void loadWhitelist()
    {
        try
        {
            whitelist.Clear();
            BufferedReader reader = new(new FileReader(WHITELIST_FILE));
            string entry = "";

            while ((entry = reader.readLine()) != null)
            {
                whitelist.Add(entry.Trim().ToLower());
            }

            reader.close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"Failed to load white-list: {exception}");
        }
    }

    protected override void saveWhitelist()
    {
        try
        {
            PrintWriter writer = new(new FileWriter(WHITELIST_FILE, false));

            foreach (string whitelistedPlayer in whitelist)
            {
                writer.println(whitelistedPlayer);
            }

            writer.close();
        }
        catch (Exception exception)
        {
            _logger.LogWarning($"Failed to save white-list: {exception}");
        }
    }
}
