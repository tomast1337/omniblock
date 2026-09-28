using System.Diagnostics;
using OmniBlock.Blocks;
using OmniBlock.Client.Rendering.Blocks;
using OmniBlock.Client.Rendering.Core;
using OmniBlock.Util.Maths;
using OmniBlock.Worlds.Core.Systems;
using Xunit.Abstractions;

namespace OmniBlock.Tests.Rendering;

/// <summary>Allocation tripwire plus a reproducible microbenchmark, not a whole-mesh FPS claim.</summary>
public sealed class CompiledCuboidPerformanceTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("stone", 0)]
    [InlineData("slab", 0)]
    [InlineData("slab", 8)]
    public void Warm_compiled_emission_does_not_add_per_block_allocations(string name, int meta)
    {
        var world = new FakeWorldContext();
        var block = world.Content.Blocks.Get(name);
        var position = new BlockPos(7, 64, 9);
        world.Writer.SetBlock(position.X, position.Y, position.Z, block.Id, meta);
        var sink = new CountingSink();
        var light = new FixedLight();
        const int count = 10000;
        Run(false); Run(true); // Compile templates and warm both JIT paths before measuring.
        var legacyMs = new List<double>();
        var compiledMs = new List<double>();
        var legacyBytes = new List<long>();
        var compiledBytes = new List<long>();
        for (var i = 0; i < 8; i++)
        {
            // Alternate the order rather than always favoring the second warm measurement.
            Measure(i % 2 == 0);
            Measure(i % 2 != 0);
        }
        Assert.Equal(legacyBytes.Min(), compiledBytes.Min());
        legacyMs.Sort(); compiledMs.Sort();
        output.WriteLine($"{name}:{meta}: {count} blocks/sample; legacy median={legacyMs[4]:F3}ms, compiled median={compiledMs[4]:F3}ms; allocated legacy={legacyBytes.Min()}, compiled={compiledBytes.Min()} bytes.");
        Assert.True(sink.Vertices > 0);
        Assert.True(double.IsFinite(sink.Checksum));

        void Run(bool compiled)
        {
            for (var i = 0; i < count; i++)
                BlockRenderer.RenderBlockByRenderType(world.Reader, world.Content.Blocks, light, block,
                    position, sink, renderAllFaces: true, doVariance: true, useCompiledCuboids: compiled);
        }

        void Measure(bool compiled)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            Run(compiled);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
            (compiled ? compiledMs : legacyMs).Add(elapsed);
            (compiled ? compiledBytes : legacyBytes).Add(bytes);
        }
    }

    private sealed class FixedLight : ILightProvider
    {
        public float GetNaturalBrightness(int x, int y, int z, int minLight) => 1;
        public float GetLuminance(int x, int y, int z) => 1;
        public LightLevels GetLightLevels(int x, int y, int z, int minBlockLight) => new LightLevels(15, 2).WithBlockFloor(minBlockLight);
    }

    private sealed class CountingSink : IBlockVertexSink
    {
        public long Vertices;
        public double Checksum;
        public void addVertexWithUV(double x, double y, double z, double u, double v) { Vertices++; Checksum += x + y + z + u + v; }
        public void setArrayLayer(int layer) => Checksum += layer;
        public void setColorOpaque_F(float red, float green, float blue) => Checksum += red + green + blue;
        public void setLight(float sky, float block) => Checksum += sky + block;
        public void setTranslationF(float x, float y, float z) => throw new InvalidOperationException();
    }
}
