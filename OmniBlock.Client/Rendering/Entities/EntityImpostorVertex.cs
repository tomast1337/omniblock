using System.Numerics;
using System.Runtime.InteropServices;

namespace OmniBlock.Client.Rendering.Entities;

/// <summary>Provider-independent capture geometry consumed by an entity impostor atlas.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct EntityImpostorVertex(Vector3 Position, Vector2 UV, Vector3 Normal);
