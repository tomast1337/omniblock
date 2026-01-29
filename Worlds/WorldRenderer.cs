using betareborn.Blocks;
using betareborn.Chunks;
using betareborn.Entities;
using betareborn.Rendering;
using Silk.NET.Maths;
using Silk.NET.OpenGL.Legacy;
using System.Runtime.InteropServices;

namespace betareborn.Worlds
{
    public class WorldRenderer
    {
        //TODO: this code is horse shit
        //right now if you modify the renderer while its mesh is pending, the update will be ignored, or maybe something else is borked. FIX IT

        private sealed class MeshBuildResult
        {
            public List<Vertex>? Solid;
            public List<Vertex>? Translucent;
            public bool IsLit;
            public bool[] SkipRenderPass = new bool[2];
        }

        public World worldObj;
        public static int chunksUpdated = 0;

        private uint solidVBO = 0;
        private uint translucentVBO = 0;
        private int solidVertexCount = 0;
        private int translucentVertexCount = 0;

        public int posX;
        public int posY;
        public int posZ;
        public int sizeWidth;
        public int sizeHeight;
        public int sizeDepth;
        public int posXMinus;
        public int posYMinus;
        public int posZMinus;
        public int posXClip;
        public int posYClip;
        public int posZClip;
        public bool isInFrustum = false;
        public bool[] skipRenderPass = new bool[2];
        public int posXPlus;
        public int posYPlus;
        public int posZPlus;
        public float rendererRadius;
        public bool needsUpdate;
        public AxisAlignedBB rendererBoundingBox;
        public int chunkIndex;
        public bool isVisible = true;
        public bool isWaitingOnOcclusionQuery;
        public int glOcclusionQuery;
        public bool isChunkLit;
        private bool isInitialized = false;
        private TaskPool updateTaskPool;
        private Task<MeshBuildResult>? meshTask;
        private volatile bool meshTaskPending;
        private int meshVersion = 0;

        public unsafe WorldRenderer(World var1, int var3, int var4, int var5, int var6, TaskPool tp)
        {
            worldObj = var1;
            sizeWidth = sizeHeight = sizeDepth = var6;
            rendererRadius = MathHelper.sqrt_float((float)(sizeWidth * sizeWidth + sizeHeight * sizeHeight + sizeDepth * sizeDepth)) / 2.0F;

            uint* vbos = stackalloc uint[2];
            GLManager.GL.GenBuffers(2, vbos);
            solidVBO = vbos[0];
            translucentVBO = vbos[1];

            posX = -999;
            setPosition(var3, var4, var5);
            needsUpdate = false;
            updateTaskPool = tp;
        }

        public void setPosition(int var1, int var2, int var3)
        {
            if (var1 != posX || var2 != posY || var3 != posZ)
            {
                Interlocked.Increment(ref meshVersion);

                setDontDraw();
                posX = var1;
                posY = var2;
                posZ = var3;
                posXPlus = var1 + sizeWidth / 2;
                posYPlus = var2 + sizeHeight / 2;
                posZPlus = var3 + sizeDepth / 2;
                posXClip = var1 & 1023;
                posYClip = var2;
                posZClip = var3 & 1023;
                posXMinus = var1 - posXClip;
                posYMinus = var2 - posYClip;
                posZMinus = var3 - posZClip;
                float var4 = 6.0F;
                rendererBoundingBox = AxisAlignedBB.getBoundingBox((double)((float)var1 - var4), (double)((float)var2 - var4), (double)((float)var3 - var4), (double)((float)(var1 + sizeWidth) + var4), (double)((float)(var2 + sizeHeight) + var4), (double)((float)(var3 + sizeDepth) + var4));
                markDirty();
            }
        }

        private void setupGLTranslation()
        {
            GLManager.GL.Translate((float)posXClip, (float)posYClip, (float)posZClip);
        }

        private MeshBuildResult BuildMesh(Vector3D<int> pos, Vector3D<int> size, ChunkCacheSnapshot cache)
        {
            int minX = pos.X;
            int minY = pos.Y;
            int minZ = pos.Z;
            int maxX = pos.X + size.X;
            int maxY = pos.Y + size.Y;
            int maxZ = pos.Z + size.Z;

            var result = new MeshBuildResult();

            for (int i = 0; i < 2; i++)
                result.SkipRenderPass[i] = true;

            var tess = new Tessellator();
            var rb = new RenderBlocks(cache, tess);

            for (int pass = 0; pass < 2; pass++)
            {
                bool hasNextPass = false;
                bool renderedAnything = false;

                tess.startCapture();
                tess.startDrawingQuads();
                tess.setTranslationD(-pos.X, -pos.Y, -pos.Z);

                for (int y = minY; y < maxY; y++)
                    for (int z = minZ; z < maxZ; z++)
                        for (int x = minX; x < maxX; x++)
                        {
                            int id = cache.getBlockId(x, y, z);
                            if (id <= 0) continue;

                            Block b = Block.blocksList[id];
                            int blockPass = b.getRenderBlockPass();

                            if (blockPass != pass)
                            {
                                hasNextPass = true;
                            }
                            else
                            {
                                renderedAnything |= rb.renderBlockByRenderType(b, x, y, z);
                            }
                        }

                tess.draw();
                tess.setTranslationD(0, 0, 0);

                var verts = tess.endCapture();
                if (verts.Count > 0)
                {
                    renderedAnything = true;
                    if (pass == 0) result.Solid = verts;
                    else result.Translucent = verts;
                }

                result.SkipRenderPass[pass] = !renderedAnything;

                if (!hasNextPass)
                    break;
            }

            result.IsLit = cache.getIsLit();
            cache.Dispose();
            return result;
        }

        public bool updateRenderer(HashSet<Vector3D<int>> pendingMeshes)
        {
            Vector3D<int> pos = new(posX, posY, posZ);
            if (!needsUpdate) return true;

            if (!meshTaskPending)
            {
                if (pendingMeshes.Contains(pos)) return false;

                if (!worldObj.checkChunksExist(posX - 1, posY - 1, posZ - 1, posX + sizeWidth + 1, posY + sizeHeight + 1, posZ + sizeDepth + 1))
                    return false;

                meshTaskPending = true;
                pendingMeshes.Add(pos);

                int taskVersion = meshVersion;
                var capturedPos = pos;
                var capturedSize = new Vector3D<int>(sizeWidth, sizeHeight, sizeDepth);
                var cache = new ChunkCacheSnapshot(worldObj, posX - 1, posY - 1, posZ - 1, posX + sizeWidth + 1, posY + sizeHeight + 1, posZ + sizeDepth + 1);

                var tcs = new TaskCompletionSource<MeshBuildResult>();

                updateTaskPool.Enqueue(() =>
                {
                    try
                    {
                        if (taskVersion != Volatile.Read(ref meshVersion))
                        {
                            cache.Dispose();
                            tcs.SetCanceled();
                            return;
                        }

                        var result = BuildMesh(capturedPos, capturedSize, cache);
                        tcs.SetResult(result);
                    }
                    catch (Exception e)
                    {
                        cache.Dispose();
                        tcs.SetException(e);
                    }
                });

                meshTask = tcs.Task;
                return false;
            }

            if (meshTask == null)
            {
                meshTaskPending = false;
                pendingMeshes.Remove(pos);
                return false;
            }

            if (!meshTask.IsCompleted)
                return false;

            pendingMeshes.Remove(pos);

            if (meshTask.IsCanceled || meshTask.IsFaulted)
            {
                meshTask = null;
                meshTaskPending = false;
                return false;
            }

            var result = meshTask.Result;
            meshTask = null;
            meshTaskPending = false;
            needsUpdate = false;

            UploadMeshData(result.Solid, result.Translucent);

            isChunkLit = result.IsLit;
            skipRenderPass[0] = result.SkipRenderPass[0];
            skipRenderPass[1] = result.SkipRenderPass[1];
            isInitialized = true;

            return true;
        }

        private unsafe void UploadMeshData(List<Vertex>? solidVertices, List<Vertex>? translucentVertices)
        {
            if (solidVertices != null && solidVertices.Count > 0)
            {
                solidVertexCount = solidVertices.Count;

                var sv = CollectionsMarshal.AsSpan(solidVertices);
                GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, solidVBO);
                GLManager.GL.BufferData<Vertex>(GLEnum.ArrayBuffer, sv, GLEnum.StaticDraw);
            }
            else
            {
                solidVertexCount = 0;
            }

            if (translucentVertices != null && translucentVertices.Count > 0)
            {
                translucentVertexCount = translucentVertices.Count;

                var tv = CollectionsMarshal.AsSpan(translucentVertices);
                GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, translucentVBO);
                GLManager.GL.BufferData<Vertex>(GLEnum.ArrayBuffer, tv, GLEnum.StaticDraw);
            }
            else
            {
                translucentVertexCount = 0;
            }

            GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, 0);
        }

        public void ReloadRenderer()
        {
            Interlocked.Increment(ref meshVersion);

            needsUpdate = true;
            isInitialized = false;
            meshTaskPending = false;

            meshTask = null;

            for (int i = 0; i < 2; i++)
            {
                skipRenderPass[i] = true;
            }

            solidVertexCount = 0;
            translucentVertexCount = 0;
        }

        public unsafe void RenderPass(int pass, Vector3D<double> viewPos)
        {
            if (!isInFrustum || skipRenderPass[pass])
                return;

            uint vbo = pass == 0 ? solidVBO : translucentVBO;
            int vertexCount = pass == 0 ? solidVertexCount : translucentVertexCount;

            if (vertexCount == 0)
                return;

            GLManager.GL.PushMatrix();
            GLManager.GL.Translate(posXMinus - viewPos.X, posYMinus - viewPos.Y, posZMinus - viewPos.Z);
            setupGLTranslation();

            GLManager.GL.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);

            GLManager.GL.EnableClientState(GLEnum.VertexArray);
            GLManager.GL.VertexPointer(3, GLEnum.Float, 32, (void*)0);

            GLManager.GL.EnableClientState(GLEnum.TextureCoordArray);
            GLManager.GL.TexCoordPointer(2, GLEnum.Float, 32, (void*)12);

            GLManager.GL.EnableClientState(GLEnum.ColorArray);
            GLManager.GL.ColorPointer(4, ColorPointerType.UnsignedByte, 32, (void*)20);

            GLManager.GL.DrawArrays(GLEnum.Triangles, 0, (uint)vertexCount);

            GLManager.GL.DisableClientState(GLEnum.VertexArray);
            GLManager.GL.DisableClientState(GLEnum.TextureCoordArray);
            GLManager.GL.DisableClientState(GLEnum.ColorArray);

            GLManager.GL.PopMatrix();

            GLManager.GL.BindBuffer(GLEnum.ArrayBuffer, 0);
        }

        public float distanceToEntitySquared(Entity var1)
        {
            float var2 = (float)(var1.posX - (double)posXPlus);
            float var3 = (float)(var1.posY - (double)posYPlus);
            float var4 = (float)(var1.posZ - (double)posZPlus);
            return var2 * var2 + var3 * var3 + var4 * var4;
        }

        public void setDontDraw()
        {
            for (int var1 = 0; var1 < 2; ++var1)
            {
                skipRenderPass[var1] = true;
            }

            isInFrustum = false;
            isInitialized = false;
        }

        public unsafe void CleanupVBOs()
        {
            if (solidVBO != 0 || translucentVBO != 0)
            {
                uint* vbos = stackalloc uint[2];
                vbos[0] = solidVBO;
                vbos[1] = translucentVBO;
                GLManager.GL.DeleteBuffers(2, vbos);
                solidVBO = 0;
                translucentVBO = 0;
            }
        }

        public void func_1204_c()
        {
            setDontDraw();
            CleanupVBOs();
            worldObj = null;
        }

        public bool shouldRender(int var1)
        {
            return isInFrustum && !skipRenderPass[var1];
        }

        public void updateInFrustrum(ICamera var1)
        {
            isInFrustum = var1.isBoundingBoxInFrustum(rendererBoundingBox);
        }

        public bool skipAllRenderPasses()
        {
            return !isInitialized ? false : skipRenderPass[0] && skipRenderPass[1];
        }

        public void markDirty()
        {
            needsUpdate = true;
        }
    }
}