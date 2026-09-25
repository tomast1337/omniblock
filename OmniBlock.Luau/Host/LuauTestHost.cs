using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace OmniBlock.Luau.Host;

/// <summary>Restricted process-control facade installed only for explicit E2E launches.</summary>
public static unsafe class LuauTestHost
{
    public const string Bootstrap = """
                                    OMNI.test = {
                                        pass = function() __Test.pass() end,
                                        fail = function(reason) __Test.fail(tostring(reason or "Test failed")) end,
                                        creative = function() __Test.creative() end,
                                        disconnect = function() return __Test.disconnect() end,
                                        summon = function(entity, count) return __Test.summon(tostring(entity), count or 1) end,
                                        countEntities = function(entity, minDistance, maxDistance) return __Test.countEntities(tostring(entity), minDistance or 0, maxDistance or 1000000) end,
                                        breakBlock = function(x, y, z) return __Test.breakBlock(x, y, z) end,
                                        setBlock = function(id, x, y, z) __Test.setBlock(tostring(id), x, y, z) end,
                                        hasBlock = function(id, x, y, z) return __Test.hasBlock(tostring(id), x, y, z) end,
                                        isMeshCurrent = function(x, y, z) return __Test.isMeshCurrent(x, y, z) end,
                                        meshDeadlineMissCount = function(x, y, z) return __Test.meshDeadlineMissCount(x, y, z) end,
                                        setFlying = function(value) __Test.setFlying(value) end,
                                        teleport = function(x, y, z) __Test.teleport(x, y, z) end,
                                        setLook = function(yaw, pitch) __Test.setLook(yaw, pitch) end,
                                        setMovement = function(forward, strafe, vertical) __Test.setMovement(forward, strafe, vertical) end,
                                        flyPath = function(ax, ay, az, bx, by, bz, seconds) __Test.flyPath(ax, ay, az, bx, by, bz, seconds) end,
                                        screenshot = function() __Test.screenshot() end,
                                        dumpTerrain = function(label) __Test.dumpTerrain(tostring(label or "terrain")) end,
                                        terrainLodColumnMinimumLevel = function(x, z) return __Test.terrainLodColumnMinimumLevel(x, z) end,
                                        terrainLodColumnSourceLoaded = function(x, z) return __Test.terrainLodColumnSourceLoaded(x, z) end,
                                        terrainLodPresentedMaterial = function(x, y, z) return __Test.terrainLodPresentedMaterial(x, y, z) end,
                                        terrainLodPresentedSample = function(x, y, z) return __Test.terrainLodPresentedSample(x, y, z) end,
                                        dumpProfiler = function(label) __Test.dumpProfiler(tostring(label or "profile")) end,
                                        worldGenerationAuto = function(profile, radius) return __Test.worldGenerationAuto(tostring(profile), radius or 32) end,
                                        worldGenerationMetric = function(metric) return __Test.worldGenerationMetric(tostring(metric)) end,
                                        terrainLodServerTileReady = function(level, x, z) return __Test.terrainLodServerTileReady(level, x, z) end,
                                        configureTerrainLodScaleProfile = function(horizon) return __Test.configureTerrainLodScaleProfile(horizon) end,
                                        prepareTerrainLodFixture = function(radius, x, z) return __Test.prepareTerrainLodFixture(radius or 64, x, z) end,
                                        terrainLodFixtureMetric = function(metric) return __Test.terrainLodFixtureMetric(tostring(metric)) end,
                                        entityBaseline = function(scene, count, distance) return __Test.entityBaseline(scene, count, distance) end,
                                        entityBaselineState = function(state) return __Test.entityBaselineState(tostring(state)) end,
                                        entityBaselineEnvironment = function(state) return __Test.entityBaselineEnvironment(tostring(state)) end,
                                        beginEntitySample = function() return __Test.beginEntitySample() end,
                                        endEntitySample = function(label) return __Test.endEntitySample(label) end,
                                        clearEntityBaseline = function() __Test.clearEntityBaseline() end,
                                        entityImpostors = function(enabled, forceForTest) return __Test.entityImpostors(enabled, forceForTest) end,
                                        impostorCache = function(action) return __Test.impostorCache(action) end,
                                    }
                                    local previousHas = OMNI.has
                                    OMNI.has = function(capability)
                                        return capability == "test" or previousHas(capability)
                                    end
                                    """;

    public static Action? Pass;
    public static Action<string>? Fail;
    public static Action? Creative;
    public static Func<bool>? Disconnect;
    public static Func<string, int, bool>? Summon;
    public static Func<string, double, double, int>? CountEntities;
    public static Func<int, int, int, bool>? BreakBlock;
    public static Action<string, int, int, int>? SetBlock;
    public static Func<string, int, int, int, bool>? HasBlock;
    public static Func<int, int, int, bool>? IsMeshCurrent;
    public static Func<int, int, int, double>? MeshDeadlineMissCount;
    public static Action<bool>? SetFlying;
    public static Action<int, int, int>? Teleport;
    public static Action<double, double>? SetLook;
    public static Action<double, double, double>? SetMovement;
    public static Action<double, double, double, double, double, double, double>? FlyPath;
    public static Action? Screenshot;
    public static Action<string>? DumpTerrain;
    public static Func<int, int, int>? TerrainLodColumnMinimumLevel;
    public static Func<int, int, bool>? TerrainLodColumnSourceLoaded;
    public static Func<int, int, int, (string? Material, int SampleSize, string? Owner)>? TerrainLodPresentedMaterial;
    public static Func<int, int, int, (string? Material, int SampleSize, string? Owner,
        int SkyLight, int BlockLight, bool OccludesFaces, string? PresentedMaterial)>? TerrainLodPresentedSample;
    public static Action<string>? DumpProfiler;
    public static Func<string, int, bool>? WorldGenerationAuto;
    public static Func<string, double>? WorldGenerationMetric;
    public static Func<int, int, int, bool>? TerrainLodServerTileReady;
    public static Func<int, bool>? ConfigureTerrainLodScaleProfile;
    public static Func<int, double?, double?, bool>? PrepareTerrainLodFixture;
    public static Func<string, double>? TerrainLodFixtureMetric;
    public static Func<string, int, double, bool>? EntityBaseline;
    public static Func<string, bool>? EntityBaselineState;
    public static Func<string, bool>? EntityBaselineEnvironment;
    public static Func<bool>? BeginEntitySample;
    public static Func<string, bool>? EndEntitySample;
    public static Action? ClearEntityBaseline;
    public static Func<bool, bool, bool>? EntityImpostors;
    public static Func<string, bool>? ImpostorCache;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 34);
        Add(l, "pass", &PassClosure);
        Add(l, "fail", &FailClosure);
        Add(l, "creative", &CreativeClosure);
        Add(l, "disconnect", &DisconnectClosure);
        Add(l, "summon", &SummonClosure);
        Add(l, "countEntities", &CountEntitiesClosure);
        Add(l, "breakBlock", &BreakBlockClosure);
        Add(l, "setBlock", &SetBlockClosure);
        Add(l, "hasBlock", &HasBlockClosure);
        Add(l, "isMeshCurrent", &IsMeshCurrentClosure);
        Add(l, "meshDeadlineMissCount", &MeshDeadlineMissCountClosure);
        Add(l, "setFlying", &SetFlyingClosure);
        Add(l, "teleport", &TeleportClosure);
        Add(l, "setLook", &SetLookClosure);
        Add(l, "setMovement", &SetMovementClosure);
        Add(l, "flyPath", &FlyPathClosure);
        Add(l, "screenshot", &ScreenshotClosure);
        Add(l, "dumpTerrain", &DumpTerrainClosure);
        Add(l, "terrainLodColumnMinimumLevel", &TerrainLodColumnMinimumLevelClosure);
        Add(l, "terrainLodColumnSourceLoaded", &TerrainLodColumnSourceLoadedClosure);
        Add(l, "terrainLodPresentedMaterial", &TerrainLodPresentedMaterialClosure);
        Add(l, "terrainLodPresentedSample", &TerrainLodPresentedSampleClosure);
        Add(l, "dumpProfiler", &DumpProfilerClosure);
        Add(l, "worldGenerationAuto", &WorldGenerationAutoClosure);
        Add(l, "worldGenerationMetric", &WorldGenerationMetricClosure);
        Add(l, "terrainLodServerTileReady", &TerrainLodServerTileReadyClosure);
        Add(l, "configureTerrainLodScaleProfile", &ConfigureTerrainLodScaleProfileClosure);
        Add(l, "prepareTerrainLodFixture", &PrepareTerrainLodFixtureClosure);
        Add(l, "terrainLodFixtureMetric", &TerrainLodFixtureMetricClosure);
        Add(l, "entityBaseline", &EntityBaselineClosure);
        Add(l, "entityBaselineState", &EntityBaselineStateClosure);
        Add(l, "entityBaselineEnvironment", &EntityBaselineEnvironmentClosure);
        Add(l, "beginEntitySample", &BeginEntitySampleClosure);
        Add(l, "endEntitySample", &EndEntitySampleClosure);
        Add(l, "clearEntityBaseline", &ClearEntityBaselineClosure);
        Add(l, "entityImpostors", &EntityImpostorsClosure);
        Add(l, "impostorCache", &ImpostorCacheClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__Test");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__Test." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EntityImpostorsClosure(IntPtr l)
    {
        var ok = false;
        try { ok = EntityImpostors?.Invoke(LuauNative.lua_toboolean(l, 1) != 0, LuauNative.lua_toboolean(l, 2) != 0) == true; } catch { }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ImpostorCacheClosure(IntPtr l)
    {
        var ok = false;
        try { ok = ImpostorCache?.Invoke(ReadString(l, 1) ?? "") == true; }
        catch (Exception ex) { Fail?.Invoke("Impostor cache test: " + ex.Message); }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0); return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EntityBaselineClosure(IntPtr l)
    {
        var ok = false;
        try { ok = EntityBaseline?.Invoke(ReadString(l, 1) ?? "", LuauNative.luaL_checkinteger(l, 2),
            LuauNative.luaL_checknumber(l, 3)) == true; }
        catch (Exception error) { Fail?.Invoke($"Entity baseline: {error.Message}"); }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EntityBaselineStateClosure(IntPtr l)
    {
        var ok = false;
        try { ok = EntityBaselineState?.Invoke(ReadString(l, 1) ?? "") == true; }
        catch (Exception error) { Fail?.Invoke($"Entity baseline state: {error.Message}"); }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EntityBaselineEnvironmentClosure(IntPtr l)
    {
        var ok = false;
        try { ok = EntityBaselineEnvironment?.Invoke(ReadString(l, 1) ?? "") == true; }
        catch (Exception error) { Fail?.Invoke($"Entity baseline environment: {error.Message}"); }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int BeginEntitySampleClosure(IntPtr l)
    {
        var ok = false;
        try { ok = BeginEntitySample?.Invoke() == true; }
        catch (Exception error) { Fail?.Invoke($"Entity sample: {error.Message}"); }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int EndEntitySampleClosure(IntPtr l)
    {
        var ok = false;
        try { ok = EndEntitySample?.Invoke(ReadString(l, 1) ?? "sample") == true; }
        catch (Exception error) { Fail?.Invoke($"Entity sample: {error.Message}"); }
        LuauNative.lua_pushboolean(l, ok ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ClearEntityBaselineClosure(IntPtr l)
    {
        Invoke(ClearEntityBaseline);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PassClosure(IntPtr l)
    {
        try
        {
            Pass?.Invoke();
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FailClosure(IntPtr l)
    {
        try
        {
            Fail?.Invoke(ReadString(l, 1) ?? "Test failed");
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CreativeClosure(IntPtr l)
    {
        Invoke(Creative);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SummonClosure(IntPtr l)
    {
        var result = false;
        try
        {
            result = Summon?.Invoke(
                ReadString(l, 1) ?? string.Empty,
                LuauNative.luaL_checkinteger(l, 2)) == true;
        }
        catch
        {
        }

        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int CountEntitiesClosure(IntPtr l)
    {
        var result = 0;
        try
        {
            result = CountEntities?.Invoke(
                ReadString(l, 1) ?? string.Empty,
                LuauNative.luaL_checknumber(l, 2),
                LuauNative.luaL_checknumber(l, 3)) ?? 0;
        }
        catch
        {
        }

        LuauNative.lua_pushinteger(l, result);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int BreakBlockClosure(IntPtr l)
    {
        var result = false;
        try
        {
            result = BreakBlock?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.luaL_checkinteger(l, 2),
                LuauNative.luaL_checkinteger(l, 3)) == true;
        }
        catch
        {
        }

        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetBlockClosure(IntPtr l)
    {
        try
        {
            SetBlock?.Invoke(
                ReadString(l, 1) ?? string.Empty,
                LuauNative.luaL_checkinteger(l, 2),
                LuauNative.luaL_checkinteger(l, 3),
                LuauNative.luaL_checkinteger(l, 4));
        }
        catch
        {
        }
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int IsMeshCurrentClosure(IntPtr l)
    {
        var result = InvokeAt(IsMeshCurrent, l, false);
        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int HasBlockClosure(IntPtr l)
    {
        var result = false;
        try
        {
            result = HasBlock?.Invoke(ReadString(l, 1) ?? string.Empty,
                LuauNative.luaL_checkinteger(l, 2), LuauNative.luaL_checkinteger(l, 3),
                LuauNative.luaL_checkinteger(l, 4)) == true;
        }
        catch (Exception error) { Fail?.Invoke($"Block observation: {error.Message}"); }
        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshDeadlineMissCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, InvokeAt(MeshDeadlineMissCount, l, 0));
        return 1;
    }

    private static T InvokeAt<T>(Func<int, int, int, T>? callback, IntPtr l, T fallback)
    {
        try
        {
            return callback == null
                ? fallback
                : callback(
                    LuauNative.luaL_checkinteger(l, 1),
                    LuauNative.luaL_checkinteger(l, 2),
                    LuauNative.luaL_checkinteger(l, 3));
        }
        catch
        {
            return fallback;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetFlyingClosure(IntPtr l)
    {
        try
        {
            SetFlying?.Invoke(LuauNative.lua_toboolean(l, 1) != 0);
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TeleportClosure(IntPtr l)
    {
        try
        {
            Teleport?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.luaL_checkinteger(l, 2),
                LuauNative.luaL_checkinteger(l, 3));
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetLookClosure(IntPtr l)
    {
        try
        {
            SetLook?.Invoke(
                LuauNative.luaL_checknumber(l, 1),
                LuauNative.luaL_checknumber(l, 2));
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int SetMovementClosure(IntPtr l)
    {
        try
        {
            SetMovement?.Invoke(
                LuauNative.luaL_checknumber(l, 1),
                LuauNative.luaL_checknumber(l, 2),
                LuauNative.luaL_checknumber(l, 3));
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FlyPathClosure(IntPtr l)
    {
        try
        {
            FlyPath?.Invoke(
                LuauNative.luaL_checknumber(l, 1),
                LuauNative.luaL_checknumber(l, 2),
                LuauNative.luaL_checknumber(l, 3),
                LuauNative.luaL_checknumber(l, 4),
                LuauNative.luaL_checknumber(l, 5),
                LuauNative.luaL_checknumber(l, 6),
                LuauNative.luaL_checknumber(l, 7));
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ScreenshotClosure(IntPtr l)
    {
        Invoke(Screenshot);
        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DumpTerrainClosure(IntPtr l)
    {
        try
        {
            DumpTerrain?.Invoke(ReadString(l, 1) ?? "terrain");
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DumpProfilerClosure(IntPtr l)
    {
        try
        {
            DumpProfiler?.Invoke(ReadString(l, 1) ?? "profile");
        }
        catch
        {
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WorldGenerationAutoClosure(IntPtr l)
    {
        var result = false;
        try
        {
            result = WorldGenerationAuto?.Invoke(
                ReadString(l, 1) ?? string.Empty,
                LuauNative.luaL_checkinteger(l, 2)) == true;
        }
        catch (Exception error)
        {
            Fail?.Invoke($"Automatic world generation: {error.Message}");
        }
        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WorldGenerationMetricClosure(IntPtr l)
    {
        double result = 0;
        try { result = WorldGenerationMetric?.Invoke(ReadString(l, 1) ?? string.Empty) ?? 0; }
        catch (Exception error) { Fail?.Invoke($"Automatic generation metric: {error.Message}"); }
        LuauNative.lua_pushnumber(l, result);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainLodServerTileReadyClosure(IntPtr l)
    {
        var result = false;
        try
        {
            result = TerrainLodServerTileReady?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.luaL_checkinteger(l, 2),
                LuauNative.luaL_checkinteger(l, 3)) == true;
        }
        catch (Exception error) { Fail?.Invoke($"Terrain LOD server tile: {error.Message}"); }
        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PrepareTerrainLodFixtureClosure(IntPtr l)
    {
        var result = false;
        try
        {
            result = PrepareTerrainLodFixture?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.lua_type(l, 2) <= 0 ? null : LuauNative.luaL_checknumber(l, 2),
                LuauNative.lua_type(l, 3) <= 0 ? null : LuauNative.luaL_checknumber(l, 3)) == true;
        }
        catch (Exception error)
        {
            Fail?.Invoke($"Terrain LOD fixture: {error.Message}");
        }
        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int DisconnectClosure(IntPtr l)
    {
        var accepted = false;
        try { accepted = Disconnect?.Invoke() == true; }
        catch (Exception error) { Fail?.Invoke($"Disconnect: {error.Message}"); }
        LuauNative.lua_pushboolean(l, accepted ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ConfigureTerrainLodScaleProfileClosure(IntPtr l)
    {
        var result = false;
        try
        {
            result = ConfigureTerrainLodScaleProfile?.Invoke(
                LuauNative.luaL_checkinteger(l, 1)) == true;
        }
        catch (Exception error)
        {
            Fail?.Invoke($"Terrain LOD scale profile: {error.Message}");
        }
        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainLodFixtureMetricClosure(IntPtr l)
    {
        double result = 0;
        try
        {
            result = TerrainLodFixtureMetric?.Invoke(
                ReadString(l, 1) ?? string.Empty) ?? 0;
        }
        catch (Exception error)
        {
            Fail?.Invoke($"Terrain LOD fixture metric: {error.Message}");
        }
        LuauNative.lua_pushnumber(l, result);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainLodColumnMinimumLevelClosure(IntPtr l)
    {
        var result = -1;
        try
        {
            result = TerrainLodColumnMinimumLevel?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.luaL_checkinteger(l, 2)) ?? -1;
        }
        catch (Exception error)
        {
            Fail?.Invoke($"Terrain LOD column level: {error.Message}");
        }
        LuauNative.lua_pushinteger(l, result);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainLodColumnSourceLoadedClosure(IntPtr l)
    {
        var result = false;
        try
        {
            result = TerrainLodColumnSourceLoaded?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.luaL_checkinteger(l, 2)) == true;
        }
        catch (Exception error)
        {
            Fail?.Invoke($"Terrain LOD column source: {error.Message}");
        }
        LuauNative.lua_pushboolean(l, result ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainLodPresentedMaterialClosure(IntPtr l)
    {
        (string? Material, int SampleSize, string? Owner) result = (null, -1, null);
        try
        {
            result = TerrainLodPresentedMaterial?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.luaL_checkinteger(l, 2),
                LuauNative.luaL_checkinteger(l, 3)) ?? (null, -1, null);
        }
        catch (Exception error)
        {
            Fail?.Invoke($"Terrain LOD presented material: {error.Message}");
        }
        if (result.Material is { } material) LuauNative.lua_pushstring(l, material);
        else LuauNative.lua_pushnil(l);
        LuauNative.lua_pushinteger(l, result.SampleSize);
        if (result.Owner is { } owner) LuauNative.lua_pushstring(l, owner);
        else LuauNative.lua_pushnil(l);
        return 3;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int TerrainLodPresentedSampleClosure(IntPtr l)
    {
        (string? Material, int SampleSize, string? Owner, int SkyLight, int BlockLight,
            bool OccludesFaces, string? PresentedMaterial) result = (null, -1, null, -1, -1, false, null);
        try
        {
            result = TerrainLodPresentedSample?.Invoke(
                LuauNative.luaL_checkinteger(l, 1),
                LuauNative.luaL_checkinteger(l, 2),
                LuauNative.luaL_checkinteger(l, 3)) ?? result;
        }
        catch (Exception error)
        {
            Fail?.Invoke($"Terrain LOD presented sample: {error.Message}");
        }
        if (result.Material is { } material) LuauNative.lua_pushstring(l, material);
        else LuauNative.lua_pushnil(l);
        LuauNative.lua_pushinteger(l, result.SampleSize);
        if (result.Owner is { } owner) LuauNative.lua_pushstring(l, owner);
        else LuauNative.lua_pushnil(l);
        LuauNative.lua_pushinteger(l, result.SkyLight);
        LuauNative.lua_pushinteger(l, result.BlockLight);
        LuauNative.lua_pushboolean(l, result.OccludesFaces ? 1 : 0);
        if (result.PresentedMaterial is { } presentedMaterial) LuauNative.lua_pushstring(l, presentedMaterial);
        else LuauNative.lua_pushnil(l);
        return 7;
    }

    private static void Invoke(Action? action)
    {
        try
        {
            action?.Invoke();
        }
        catch
        {
        }
    }

    private static string? ReadString(IntPtr l, int index)
    {
        var pointer = LuauNative.lua_tolstring(l, index, out var length);
        return pointer == IntPtr.Zero ? null : Encoding.UTF8.GetString((byte*)pointer, (int)length);
    }
}
