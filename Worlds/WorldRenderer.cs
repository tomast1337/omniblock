using betareborn.Chunks;
using betareborn.Rendering;
using Silk.NET.Maths;

namespace betareborn.Worlds
{
    public class WorldRenderer
    {
        static WorldRenderer()
        {
            var offsets = new List<Vector3D<int>>();

            for (int x = -MAX_RENDER_DISTANCE; x <= MAX_RENDER_DISTANCE; x++)
            {
                for (int y = -8; y <= 8; y++)
                {
                    for (int z = -MAX_RENDER_DISTANCE; z <= MAX_RENDER_DISTANCE; z++)
                    {
                        offsets.Add(new Vector3D<int>(x, y, z));
                    }
                }
            }

            offsets.Sort((a, b) =>
                (a.X * a.X + a.Y * a.Y + a.Z * a.Z).CompareTo(b.X * b.X + b.Y * b.Y + b.Z * b.Z));

            spiralOffsets = [.. offsets];
        }

        private class SubChunkState(bool isLit, SubChunkRenderer renderer)
        {
            public bool IsLit { get; set; } = isLit;
            public SubChunkRenderer Renderer { get; } = renderer;
        }

        private class ChunkToMeshInfo(Vector3D<int> pos, long version, bool priority)
        {
            public Vector3D<int> Pos = pos;
            public long Version = version;
            public bool priority = priority;
        }

        private static readonly Vector3D<int>[] spiralOffsets;
        private const int MAX_RENDER_DISTANCE = 33;
        private readonly Dictionary<Vector3D<int>, SubChunkState> renderers = [];
        private readonly List<SubChunkRenderer> translucentRenderers = [];
        private readonly List<SubChunkRenderer> renderersToRemove = [];
        private readonly ChunkMeshGenerator meshGenerator;
        private readonly World world;
        private readonly Dictionary<Vector3D<int>, ChunkMeshVersion> chunkVersions = [];
        private readonly Queue<ChunkToMeshInfo> dirtyChunks = [];
        private int lastRenderDistance = 16;
        private Vector3D<double> lastViewPos;

        public WorldRenderer(World world, int workerCount)
        {
            meshGenerator = new(workerCount);
            this.world = world;
        }

        private static int CalculateRealRenderDistance(int val)
        {
            if (val == 0)
            {
                return 16;
            }
            else if (val == 1)
            {
                return 8;
            }
            else if (val == 2)
            {
                return 4;
            }
            else if (val == 3)
            {
                return 2;
            }

            return 0;
        }

        public void Render(ICamera camera, Vector3D<double> viewPos, int renderDistance)
        {
            lastRenderDistance = CalculateRealRenderDistance(renderDistance);
            lastViewPos = viewPos;

            foreach (var state in renderers.Values)
            {
                if (IsChunkInRenderDistance(state.Renderer.Position, viewPos))
                {
                    if (camera.isBoundingBoxInFrustum(state.Renderer.BoundingBox))
                    {
                        state.Renderer.Render(0, viewPos);

                        if (state.Renderer.HasTranslucentMesh())
                        {
                            translucentRenderers.Add(state.Renderer);
                        }
                    }
                }
                else
                {
                    renderersToRemove.Add(state.Renderer);
                }
            }

            foreach (var renderer in renderersToRemove)
            {
                renderers.Remove(renderer.Position);
                renderer.Dispose();

                chunkVersions.Remove(renderer.Position);
            }

            renderersToRemove.Clear();

            ProcessOneMeshUpdate(camera);

            const int MAX_CHUNKS_PER_FRAME = 2;

            LoadNewMeshes(viewPos, MAX_CHUNKS_PER_FRAME);
        }

        public void RenderTransparent(Vector3D<double> viewPos)
        {
            translucentRenderers.Sort((a, b) =>
            {
                double distA = Vector3D.DistanceSquared(ToDoubleVec(a.Position), viewPos);
                double distB = Vector3D.DistanceSquared(ToDoubleVec(b.Position), viewPos);
                return distB.CompareTo(distA);
            });

            foreach (var renderer in translucentRenderers)
            {
                renderer.Render(1, viewPos);
            }

            translucentRenderers.Clear();
        }

        private void ProcessOneMeshUpdate(ICamera camera)
        {
            int maxChecks = dirtyChunks.Count;
            int checks = 0;

            while (checks < maxChecks && dirtyChunks.TryDequeue(out ChunkToMeshInfo? info))
            {
                checks++;

                if (!IsChunkInRenderDistance(info.Pos, lastViewPos))
                {
                    chunkVersions.Remove(info.Pos);
                    continue;
                }

                var aabb = AxisAlignedBB.getBoundingBoxFromPool(
                    info.Pos.X, info.Pos.Y, info.Pos.Z,
                    info.Pos.X + SubChunkRenderer.SIZE,
                    info.Pos.Y + SubChunkRenderer.SIZE,
                    info.Pos.Z + SubChunkRenderer.SIZE
                );

                if (!camera.isBoundingBoxInFrustum(aabb))
                {
                    dirtyChunks.Enqueue(info);
                    continue;
                }

                meshGenerator.MeshChunk(world, info.Pos, info.Version, info.priority);
                return;
            }
        }

        public void Tick(Vector3D<double> viewPos)
        {
            lastViewPos = viewPos;

            Vector3D<int> currentChunk = new(
                (int)Math.Floor(viewPos.X / 16.0),
                (int)Math.Floor(viewPos.Y / 16.0),
                (int)Math.Floor(viewPos.Z / 16.0)
            );

            int radiusSq = lastRenderDistance * lastRenderDistance;
            int enqueuedCount = 0;
            const int MAX_CHUNKS_PER_FRAME = 16;

            for (int i = 0; i < spiralOffsets.Length; i++)
            {
                var offset = spiralOffsets[i];
                int distSq = offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z;

                if (distSq > radiusSq)
                {
                    break;
                }

                var chunkPos = (currentChunk + offset) * 16;

                if (renderers.ContainsKey(chunkPos) || chunkVersions.ContainsKey(chunkPos))
                {
                    continue;
                }

                if (MarkDirty(chunkPos))
                {
                    enqueuedCount++;
                    if (enqueuedCount >= MAX_CHUNKS_PER_FRAME)
                    {
                        break;
                    }
                }
            }
        }


        public bool MarkDirty(Vector3D<int> chunkPos, bool priority = false)
        {
            if (!IsChunkInRenderDistance(chunkPos, lastViewPos))
            {
                return false;
            }

            if (!world.checkChunksExist(chunkPos.X - 1, chunkPos.Y - 1, chunkPos.Z - 1, chunkPos.X + SubChunkRenderer.SIZE + 1, chunkPos.Y + SubChunkRenderer.SIZE + 1, chunkPos.Z + SubChunkRenderer.SIZE + 1))
            {
                return false;
            }

            if (!chunkVersions.TryGetValue(chunkPos, out var version))
            {
                version = new();
                chunkVersions[chunkPos] = version;
            }

            version.MarkDirty();

            long? snapshot = version.SnapshotIfNeeded();
            if (snapshot.HasValue)
            {
                dirtyChunks.Enqueue(new(chunkPos, snapshot.Value, priority));
                return true;
            }

            return false;
        }

        private void LoadNewMeshes(Vector3D<double> viewPos, int maxChunks)
        {
            for (int i = 0; i < maxChunks; i++)
            {
                var mesh = meshGenerator.GetMesh();
                if (mesh == null) break;

                if (IsChunkInRenderDistance(mesh.Pos, viewPos))
                {
                    if (!chunkVersions.TryGetValue(mesh.Pos, out var version))
                    {
                        version = new ChunkMeshVersion();
                        chunkVersions[mesh.Pos] = version;
                    }

                    version.CompleteMesh(mesh.Version);

                    if (version.IsStale(mesh.Version))
                    {
                        long? snapshot = version.SnapshotIfNeeded();
                        if (snapshot.HasValue)
                        {
                            meshGenerator.MeshChunk(world, mesh.Pos, snapshot.Value, false);
                        }
                        continue;
                    }

                    if (renderers.TryGetValue(mesh.Pos, out SubChunkState? state))
                    {
                        state.Renderer.UploadMeshData(mesh.Solid, mesh.Translucent);
                        state.IsLit = mesh.IsLit;
                    }
                    else
                    {
                        var renderer = new SubChunkRenderer(mesh.Pos);
                        renderer.UploadMeshData(mesh.Solid, mesh.Translucent);
                        renderers[mesh.Pos] = new SubChunkState(mesh.IsLit, renderer);
                    }
                }
            }
        }

        private bool IsChunkInRenderDistance(Vector3D<int> chunkWorldPos, Vector3D<double> viewPos)
        {
            int chunkX = chunkWorldPos.X >> 4;
            int chunkZ = chunkWorldPos.Z >> 4;

            int viewChunkX = (int)Math.Floor(viewPos.X / 16.0);
            int viewChunkZ = (int)Math.Floor(viewPos.Z / 16.0);

            int dist = Vector2D.Distance(new Vector2D<int>(chunkX, chunkZ), new Vector2D<int>(viewChunkX, viewChunkZ));
            bool isIn = dist <= lastRenderDistance;
            return isIn;
        }

        private static Vector3D<double> ToDoubleVec(Vector3D<int> vec)
        {
            return new(vec.X, vec.Y, vec.Z);
        }

        public void Dispose()
        {
            meshGenerator.Stop();

            foreach (var state in renderers.Values)
            {
                state.Renderer.Dispose();
            }

            renderers.Clear();

            translucentRenderers.Clear();
            renderersToRemove.Clear();
            chunkVersions.Clear();
        }
    }
}