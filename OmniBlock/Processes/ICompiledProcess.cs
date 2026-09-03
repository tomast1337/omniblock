namespace OmniBlock.Processes;

/// <summary>
/// Immutable provider-owned process produced during content construction. Consumers narrow this
/// value to the contract understood by their crafting station or machine.
/// </summary>
public interface ICompiledProcess
{
    ResourceLocation Id { get; }
    ResourceLocation ProviderType { get; }
}
