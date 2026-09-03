using OmniBlock.Client.Network;
using OmniBlock.Network.Messages;
using OmniBlock.Registries;

namespace OmniBlock.Tests.Recipes;

public sealed class ProcessReloadAtomicityTests
{
    [Fact]
    public void Client_stages_complete_process_snapshot_only_at_configuration_boundary()
    {
        ContentRuntime original = ContentRuntime.Current;
        ContentRuntime? staged = null;
        var access = new ClientRegistryAccess(original, candidate => staged = candidate);
        RegistryDataMessage message = Message("example:coal_to_stick", """
            {"type":"shaped","pattern":["#"],"key":{"#":"omniblock:coal"},
             "result":{"id":"omniblock:stick","count":2}}
            """);

        access.Accumulate(message);

        Assert.Null(staged);
        Assert.Equal(160, original.Processes.Count);

        access.CompleteConfiguration();

        Assert.NotNull(staged);
        Assert.Equal(1, staged.Processes.Count);
        Assert.Same(original.Items, staged.Items);
        Assert.Same(original.Blocks, staged.Blocks);
    }

    [Fact]
    public void Invalid_client_process_catalog_never_reaches_staging_callback()
    {
        ContentRuntime? staged = null;
        var access = new ClientRegistryAccess(ContentRuntime.Current, candidate => staged = candidate);
        RegistryDataMessage message = Message("example:invalid", """
            {"type":"shaped","pattern":["#"],"key":{"#":"example:missing"},
             "result":{"id":"omniblock:stick"}}
            """);

        Assert.Throws<InvalidOperationException>(() => access.Accumulate(message));
        access.CompleteConfiguration();

        Assert.Null(staged);
    }

    private static RegistryDataMessage Message(string id, string json)
    {
        var message = new RegistryDataMessage { RegistryId = RegistryKeys.Recipes.Location };
        message.Entries.Add(new RegistryDataMessage.Entry(ResourceLocation.Parse(id), json));
        return message;
    }
}
