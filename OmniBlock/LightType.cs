namespace OmniBlock;

public readonly struct LightType : IEquatable<LightType>
{
    public static readonly LightType Sky = new(15);
    public static readonly LightType Block = new(0);

    public readonly int lightValue;

    private LightType(int lightValue) => this.lightValue = lightValue;

    public override bool Equals(object? obj) => obj is LightType other && Equals(other);

    public bool Equals(LightType other) => lightValue == other.lightValue;

    public static bool operator ==(LightType left, LightType right) => left.Equals(right);

    public static bool operator !=(LightType left, LightType right) => !(left == right);

    public override int GetHashCode() => lightValue;
}
