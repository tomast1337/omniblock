namespace BetaSharp.Blocks.Materials;

/// <summary>How a block reacts to being pushed by a piston.</summary>
public enum PistonBehavior : byte
{
    /// <summary>Pushed normally.</summary>
    Normal = 0,

    /// <summary>Destroyed when pushed (plants, snow layers, fluids, ...).</summary>
    Destroy = 1,

    /// <summary>Cannot be pushed at all (obsidian-class, portals, extended pistons).</summary>
    Unpushable = 2
}
