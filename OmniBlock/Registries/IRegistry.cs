namespace OmniBlock.Registries;

public interface IRegistry<T> : IReadableRegistry<T> where T : class
{
    bool IsFrozen { get; }
    void Register(ResourceLocation key, T value);
    void Register(int id, ResourceLocation key, T value);

    void Freeze();
}
