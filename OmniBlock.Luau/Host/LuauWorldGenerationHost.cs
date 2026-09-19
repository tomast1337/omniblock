using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau.Host;

/// <summary>A value-only, immutable view of one server-owned fixed-area generation job.</summary>
public readonly record struct LuauWorldGenerationInfo(
    string Id,
    string World,
    int DimensionId,
    string Seed,
    string GeneratorProfile,
    string GeneratorOptionsHash,
    string ContentFingerprint,
    int CenterChunkX,
    int CenterChunkZ,
    int RadiusChunks,
    double TotalTargets,
    string Status,
    double NextTarget,
    double RemainingTargets,
    double PreparedTargets,
    double DecoratedTargets,
    double SavedTargets,
    double SkippedTargets,
    double WrittenChunks,
    double RetainedBytes,
    double PeakRetainedBytes,
    double DiskBytes,
    double TargetsPerSecond,
    string ThrottleReason,
    string? LastError,
    string CreatedUtc,
    string UpdatedUtc);

public readonly record struct LuauWorldGenerationCommandResult(bool Accepted, string? Error = null);

/// <summary>
///     Restricted FFI facade for observing server-owned pregeneration jobs and queueing authorized
///     lifecycle requests. The client supplies immutable snapshots; every mutation is expected to
///     cross the server's command/tick boundary rather than touching a job from the Luau thread.
/// </summary>
public static unsafe class LuauWorldGenerationHost
{
    public const string Bootstrap = """
                                    local function readonly(value)
                                        if value == nil then return nil end
                                        return table.freeze(value)
                                    end

                                    local Job = {}
                                    Job.__index = function(self, key)
                                        if key == "progress" then
                                            return readonly(__WorldGeneration.inspect(self.id))
                                        end
                                        return Job[key]
                                    end
                                    Job.__newindex = function()
                                        error("world-generation jobs are read-only", 2)
                                    end

                                    local function job(id)
                                        return table.freeze(setmetatable({ id = id }, Job))
                                    end

                                    local function command(action, id)
                                        local accepted, message = __WorldGeneration.change(action, id)
                                        if not accepted then error(message or ("could not " .. action .. " job"), 3) end
                                        return true
                                    end

                                    function Job:pause() return command("pause", self.id) end
                                    function Job:resume() return command("resume", self.id) end
                                    function Job:cancel() return command("cancel", self.id) end

                                    local WorldGeneration = {}
                                    function WorldGeneration.list()
                                        local snapshots = __WorldGeneration.list()
                                        for index, snapshot in ipairs(snapshots) do snapshots[index] = readonly(snapshot) end
                                        return table.freeze(snapshots)
                                    end
                                    function WorldGeneration.get(id)
                                        id = tostring(id)
                                        if __WorldGeneration.inspect(id) == nil then return nil end
                                        return job(id)
                                    end
                                    function WorldGeneration.start(options)
                                        assert(type(options) == "table", "OMNI.worldgen.start expects an options table")
                                        local id = assert(options.id, "world-generation job id is required")
                                        local center = assert(options.center, "center is required")
                                        assert(type(center) == "table" and type(center.x) == "number" and
                                            type(center.z) == "number", "center.x and center.z must be numbers")
                                        local dimension = options.dimension or "omniblock:overworld"
                                        local dimensionId
                                        if dimension == "omniblock:overworld" or dimension == "overworld" or dimension == 0 then
                                            dimensionId = 0
                                        elseif dimension == "omniblock:nether" or dimension == "nether" or dimension == -1 then
                                            dimensionId = -1
                                        else
                                            error("unknown world-generation dimension '" .. tostring(dimension) .. "'", 2)
                                        end
                                        if options.shape ~= nil and options.shape ~= "circle" then
                                            error("only the 'circle' world-generation shape is currently supported", 2)
                                        end
                                        if options.profile ~= nil and options.profile ~= "play" then
                                            error("only the 'play' fixed-area generation profile is currently supported", 2)
                                        end
                                        local radius = assert(options.radiusChunks, "radiusChunks is required")
                                        assert(type(radius) == "number" and radius % 1 == 0,
                                            "radiusChunks must be an integer")
                                        local centerChunkX = math.floor(center.x / 16)
                                        local centerChunkZ = math.floor(center.z / 16)
                                        local accepted, message = __WorldGeneration.start(
                                            tostring(id), dimensionId, centerChunkX, centerChunkZ, radius)
                                        if not accepted then error(message or "could not start world-generation job", 2) end
                                        return job(tostring(id))
                                    end
                                    setmetatable(WorldGeneration, {
                                        __index = function(_, key)
                                            if key == "available" then return __WorldGeneration.available() end
                                        end,
                                        __newindex = function()
                                            error("OMNI.worldgen is read-only", 2)
                                        end,
                                    })
                                    table.freeze(WorldGeneration)
                                    OMNI.worldgen = WorldGeneration
                                    local previousHas = OMNI.has
                                    OMNI.has = function(capability)
                                        return capability == "worldgen" or previousHas(capability)
                                    end
                                    """;

    public static Func<bool>? Available;
    public static Func<IReadOnlyList<LuauWorldGenerationInfo>>? List;
    public static Func<string, LuauWorldGenerationInfo?>? Inspect;
    public static Func<string, int, int, int, int, LuauWorldGenerationCommandResult>? Start;
    public static Func<string, string, LuauWorldGenerationCommandResult>? Change;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 5);
        Add(l, "available", &AvailableClosure);
        Add(l, "list", &ListClosure);
        Add(l, "inspect", &InspectClosure);
        Add(l, "start", &StartClosure);
        Add(l, "change", &ChangeClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__WorldGeneration");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__WorldGeneration." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int AvailableClosure(IntPtr l)
    {
        var available = false;
        try { available = Available?.Invoke() == true; } catch { }
        LuauNative.lua_pushboolean(l, available ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ListClosure(IntPtr l)
    {
        IReadOnlyList<LuauWorldGenerationInfo> jobs = [];
        try { jobs = List?.Invoke() ?? []; } catch { }
        LuauNative.lua_createtable(l, jobs.Count, 0);
        for (var index = 0; index < jobs.Count; index++)
        {
            Push(l, jobs[index]);
            LuauNative.lua_rawseti(l, -2, index + 1);
        }
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int InspectClosure(IntPtr l)
    {
        LuauWorldGenerationInfo? info = null;
        try
        {
            var id = ReadString(l, 1);
            if (id != null) info = Inspect?.Invoke(id);
        }
        catch { }
        if (info is { } value) Push(l, value);
        else LuauNative.lua_pushnil(l);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int StartClosure(IntPtr l)
    {
        LuauWorldGenerationCommandResult result;
        try
        {
            var id = ReadString(l, 1);
            result = id == null
                ? new(false, "world-generation job id is required")
                : Start?.Invoke(
                    id,
                    LuauNative.luaL_checkinteger(l, 2),
                    LuauNative.luaL_checkinteger(l, 3),
                    LuauNative.luaL_checkinteger(l, 4),
                    LuauNative.luaL_checkinteger(l, 5))
                  ?? new(false, "world-generation control is unavailable");
        }
        catch (Exception error)
        {
            result = new(false, error.Message);
        }
        return Push(l, result);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ChangeClosure(IntPtr l)
    {
        LuauWorldGenerationCommandResult result;
        try
        {
            var action = ReadString(l, 1);
            var id = ReadString(l, 2);
            result = action == null || id == null
                ? new(false, "world-generation action and job id are required")
                : Change?.Invoke(action, id)
                  ?? new(false, "world-generation control is unavailable");
        }
        catch (Exception error)
        {
            result = new(false, error.Message);
        }
        return Push(l, result);
    }

    private static int Push(IntPtr l, LuauWorldGenerationCommandResult result)
    {
        LuauNative.lua_pushboolean(l, result.Accepted ? 1 : 0);
        if (result.Error == null) LuauNative.lua_pushnil(l);
        else LuauNative.lua_pushstring(l, result.Error);
        return 2;
    }

    private static void Push(IntPtr l, in LuauWorldGenerationInfo info)
    {
        LuauNative.lua_createtable(l, 0, 29);
        Set(l, "id", info.Id);
        Set(l, "world", info.World);
        Set(l, "dimension", info.DimensionId == -1 ? "omniblock:nether" : "omniblock:overworld");
        Set(l, "dimensionId", info.DimensionId);
        Set(l, "seed", info.Seed);
        Set(l, "generatorProfile", info.GeneratorProfile);
        Set(l, "generatorOptionsHash", info.GeneratorOptionsHash);
        Set(l, "contentFingerprint", info.ContentFingerprint);
        Set(l, "centerChunkX", info.CenterChunkX);
        Set(l, "centerChunkZ", info.CenterChunkZ);
        Set(l, "radiusChunks", info.RadiusChunks);
        Set(l, "totalTargets", info.TotalTargets);
        Set(l, "status", info.Status);
        Set(l, "nextTarget", info.NextTarget);
        Set(l, "remainingTargets", info.RemainingTargets);
        Set(l, "preparedTargets", info.PreparedTargets);
        Set(l, "decoratedTargets", info.DecoratedTargets);
        Set(l, "savedTargets", info.SavedTargets);
        Set(l, "skippedTargets", info.SkippedTargets);
        Set(l, "writtenChunks", info.WrittenChunks);
        Set(l, "savedChunks", info.WrittenChunks);
        Set(l, "retainedBytes", info.RetainedBytes);
        Set(l, "peakRetainedBytes", info.PeakRetainedBytes);
        Set(l, "diskBytes", info.DiskBytes);
        Set(l, "targetsPerSecond", info.TargetsPerSecond);
        Set(l, "throttleReason", info.ThrottleReason);
        if (info.LastError == null) LuauNative.lua_pushnil(l);
        else LuauNative.lua_pushstring(l, info.LastError);
        LuauNative.lua_setfield(l, -2, "lastError");
        Set(l, "createdUtc", info.CreatedUtc);
        Set(l, "updatedUtc", info.UpdatedUtc);
    }

    private static string? ReadString(IntPtr l, int index)
    {
        var pointer = LuauNative.lua_tolstring(l, index, out var length);
        return pointer == IntPtr.Zero ? null : Encoding.UTF8.GetString((byte*)pointer, (int)length);
    }

    private static void Set(IntPtr l, string key, string value)
    {
        LuauNative.lua_pushstring(l, value);
        LuauNative.lua_setfield(l, -2, key);
    }

    private static void Set(IntPtr l, string key, double value)
    {
        LuauNative.lua_pushnumber(l, value);
        LuauNative.lua_setfield(l, -2, key);
    }
}
