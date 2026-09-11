using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace OmniBlock.Luau.Host;

/// <summary>Read-only FFI facade for client readiness observed by automation scripts.</summary>
public static unsafe class LuauClientStateHost
{
    public const string Bootstrap = """
                                    OMNI.client = OMNI.client or {}
                                    OMNI.client.state = setmetatable({}, {
                                        __index = function(_, key)
                                            if key == "worldLoaded" then return __ClientState.worldLoaded() end
                                            if key == "playerReady" then return __ClientState.playerReady() end
                                            if key == "worldId" then return __ClientState.worldId() end
                                            if key == "meshPending" then return __ClientState.meshPending() end
                                            if key == "meshCancelledCount" then return __ClientState.meshCancelledCount() end
                                            if key == "meshSupersededCount" then return __ClientState.meshSupersededCount() end
                                            if key == "meshBuildFailureCount" then return __ClientState.meshBuildFailureCount() end
                                            if key == "meshAwaitingUpload" then return __ClientState.meshAwaitingUpload() end
                                            if key == "meshAwaitingDraw" then return __ClientState.meshAwaitingDraw() end
                                            if key == "meshRequestToGpuMs" then return __ClientState.meshRequestToGpuMs() end
                                            if key == "frameTimeMs" then return __ClientState.frameTimeMs() end
                                            if key == "meshSafetyLoadedColumns" then return __ClientState.meshSafetyLoadedColumns() end
                                            if key == "meshSafetyExpectedSections" then return __ClientState.meshSafetyExpectedSections() end
                                            if key == "meshSafetyHoles" then return __ClientState.meshSafetyHoles() end
                                            if key == "meshReadyRadius" then return __ClientState.meshReadyRadius() end
                                            if key == "residentMeshCount" then return __ClientState.residentMeshCount() end
                                            if key == "presentedMeshCount" then return __ClientState.presentedMeshCount() end
                                            if key == "foregroundPending" then return __ClientState.foregroundPending() end
                                            if key == "backgroundPending" then return __ClientState.backgroundPending() end
                                            if key == "oldestForegroundAge" then return __ClientState.oldestForegroundAge() end
                                            if key == "presentationRegressionCount" then return __ClientState.presentationRegressionCount() end
                                            if key == "playerX" then return __ClientState.playerX() end
                                            if key == "playerY" then return __ClientState.playerY() end
                                            if key == "playerZ" then return __ClientState.playerZ() end
                                            return nil
                                        end,
                                        __newindex = function()
                                            error("OMNI.client.state is read-only", 2)
                                        end,
                                    })
                                    """;

    public static Func<bool>? WorldLoaded;
    public static Func<bool>? PlayerReady;
    public static Func<string?>? WorldId;
    public static Func<double>? MeshPending;
    public static Func<double>? MeshCancelledCount;
    public static Func<double>? MeshSupersededCount;
    public static Func<double>? MeshBuildFailureCount;
    public static Func<double>? MeshAwaitingUpload;
    public static Func<double>? MeshAwaitingDraw;
    public static Func<double>? MeshRequestToGpuMs;
    public static Func<double>? FrameTimeMs;
    public static Func<double>? MeshSafetyLoadedColumns;
    public static Func<double>? MeshSafetyExpectedSections;
    public static Func<double>? MeshSafetyHoles;
    public static Func<double>? MeshReadyRadius;
    public static Func<double>? ResidentMeshCount;
    public static Func<double>? PresentedMeshCount;
    public static Func<double>? ForegroundPending;
    public static Func<double>? BackgroundPending;
    public static Func<double>? OldestForegroundAge;
    public static Func<double>? PresentationRegressionCount;
    public static Func<double>? PlayerX;
    public static Func<double>? PlayerY;
    public static Func<double>? PlayerZ;

    public static void Install(IntPtr l)
    {
        LuauNative.lua_createtable(l, 0, 24);
        Add(l, "worldLoaded", &WorldLoadedClosure);
        Add(l, "playerReady", &PlayerReadyClosure);
        Add(l, "worldId", &WorldIdClosure);
        Add(l, "meshPending", &MeshPendingClosure);
        Add(l, "meshCancelledCount", &MeshCancelledCountClosure);
        Add(l, "meshSupersededCount", &MeshSupersededCountClosure);
        Add(l, "meshBuildFailureCount", &MeshBuildFailureCountClosure);
        Add(l, "meshAwaitingUpload", &MeshAwaitingUploadClosure);
        Add(l, "meshAwaitingDraw", &MeshAwaitingDrawClosure);
        Add(l, "meshRequestToGpuMs", &MeshRequestToGpuMsClosure);
        Add(l, "frameTimeMs", &FrameTimeMsClosure);
        Add(l, "meshSafetyLoadedColumns", &MeshSafetyLoadedColumnsClosure);
        Add(l, "meshSafetyExpectedSections", &MeshSafetyExpectedSectionsClosure);
        Add(l, "meshSafetyHoles", &MeshSafetyHolesClosure);
        Add(l, "meshReadyRadius", &MeshReadyRadiusClosure);
        Add(l, "residentMeshCount", &ResidentMeshCountClosure);
        Add(l, "presentedMeshCount", &PresentedMeshCountClosure);
        Add(l, "foregroundPending", &ForegroundPendingClosure);
        Add(l, "backgroundPending", &BackgroundPendingClosure);
        Add(l, "oldestForegroundAge", &OldestForegroundAgeClosure);
        Add(l, "presentationRegressionCount", &PresentationRegressionCountClosure);
        Add(l, "playerX", &PlayerXClosure);
        Add(l, "playerY", &PlayerYClosure);
        Add(l, "playerZ", &PlayerZClosure);
        LuauNative.lua_setfield(l, LuauNative.GlobalsIndex, "__ClientState");
    }

    private static void Add(IntPtr l, string name, delegate* unmanaged[Cdecl]<IntPtr, int> function)
    {
        LuauNative.lua_pushcclosurek(l, function, "__ClientState." + name, 0, IntPtr.Zero);
        LuauNative.lua_setfield(l, -2, name);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WorldLoadedClosure(IntPtr l)
    {
        LuauNative.lua_pushboolean(l, ReadBool(WorldLoaded) ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PlayerReadyClosure(IntPtr l)
    {
        LuauNative.lua_pushboolean(l, ReadBool(PlayerReady) ? 1 : 0);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int WorldIdClosure(IntPtr l)
    {
        string? id = null;
        try
        {
            id = WorldId?.Invoke();
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
        }

        if (id == null) LuauNative.lua_pushnil(l);
        else LuauNative.lua_pushstring(l, id);
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshPendingClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshPending));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshRequestToGpuMsClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshRequestToGpuMs));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int FrameTimeMsClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(FrameTimeMs));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshSafetyLoadedColumnsClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshSafetyLoadedColumns));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshSafetyExpectedSectionsClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshSafetyExpectedSections));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshSafetyHolesClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshSafetyHoles));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshReadyRadiusClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshReadyRadius));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ResidentMeshCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(ResidentMeshCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PresentedMeshCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PresentedMeshCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int ForegroundPendingClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(ForegroundPending));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int BackgroundPendingClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(BackgroundPending));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int OldestForegroundAgeClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(OldestForegroundAge));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PresentationRegressionCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PresentationRegressionCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PlayerXClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PlayerX));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PlayerYClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PlayerY));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int PlayerZClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(PlayerZ));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshCancelledCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshCancelledCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshSupersededCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshSupersededCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshBuildFailureCountClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshBuildFailureCount));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshAwaitingUploadClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshAwaitingUpload));
        return 1;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int MeshAwaitingDrawClosure(IntPtr l)
    {
        LuauNative.lua_pushnumber(l, ReadNumber(MeshAwaitingDraw));
        return 1;
    }

    private static bool ReadBool(Func<bool>? getter)
    {
        try
        {
            return getter?.Invoke() == true;
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
            return false;
        }
    }

    private static double ReadNumber(Func<double>? getter)
    {
        try
        {
            return getter?.Invoke() ?? 0;
        }
        catch
        {
            // Managed exceptions must never cross an unmanaged Luau callback boundary.
            return 0;
        }
    }
}
