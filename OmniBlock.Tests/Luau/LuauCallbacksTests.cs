using OmniBlock.Luau;

namespace OmniBlock.Tests.Luau;

/// <summary>
///     Covers <see cref="LuauCallbacks.Allocate" /> directly — plain C#, no native
///     <c>omniblock_luau</c> library required, since it never calls into it. Taking its address
///     via <c>&amp;LuauCallbacks.Allocate</c> is a plain CLR operation (no native library
///     resolution involved); the C# compiler requires calling
///     <see cref="System.Runtime.InteropServices.UnmanagedCallersOnlyAttribute" />-attributed
///     methods this way rather than directly (CS8901).
///     <see cref="LuauCallbacks.Interrupt" /> has no equivalent pure unit test — it calls
///     <see cref="LuauNative.lua_getthreaddata" />, a pointer resolved from the real native
///     library at <see cref="LuauNative" />'s static-init time, with no seam to fake. See
///     <see cref="LuauInterruptIntegrationTests" /> instead.
/// </summary>
public sealed unsafe class LuauCallbacksTests
{
    private static readonly delegate* unmanaged[Cdecl]<void*, void*, nuint, nuint, void*> s_allocate = &LuauCallbacks.Allocate;

    [Fact]
    public void Allocate_growingFromNull_returnsWritableMemory()
    {
        void* ptr = s_allocate(null, null, 0, 64);

        Assert.True(ptr != null);
        new Span<byte>(ptr, 64).Fill(0xAB);
        Assert.Equal(0xAB, new Span<byte>(ptr, 64)[63]);

        s_allocate(null, ptr, 64, 0);
    }

    [Fact]
    public void Allocate_growingExisting_preservesLeadingBytes()
    {
        void* small = s_allocate(null, null, 0, 32);
        new Span<byte>(small, 32).Fill(0x42);

        void* grown = s_allocate(null, small, 32, 128);

        Assert.True(grown != null);
        Span<byte> data = new(grown, 128);
        for (int i = 0; i < 32; i++)
        {
            Assert.Equal(0x42, data[i]);
        }

        s_allocate(null, grown, 128, 0);
    }

    [Fact]
    public void Allocate_shrinkingToZero_freesAndReturnsNull()
    {
        void* ptr = s_allocate(null, null, 0, 16);

        void* result = s_allocate(null, ptr, 16, 0);

        Assert.True(result == null);
    }

    [Fact]
    public void Allocate_freeingNull_isNoOp()
    {
        void* result = s_allocate(null, null, 0, 0);

        Assert.True(result == null);
    }
}
