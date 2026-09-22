using StringReader = Brigadier.NET.StringReader;
using Brigadier.NET.Exceptions;
using OmniBlock.Server.Command;
using OmniBlock.Tests.TestSupport;

namespace OmniBlock.Tests.Server;

public sealed class BlockCommandArgumentTests
{
    [Theory]
    [InlineData("omniblock:air")]
    [InlineData("air")]
    [InlineData("0")]
    public void Air_aliases_resolve_to_protocol_zero(string input)
    {
        var content = new FakeWorldContext().Content;
        var parser = new Command.ArgBlock(content.Items, content.Blocks);
        Assert.Equal((0, 0), parser.Parse(new StringReader(input)));
    }

    [Fact]
    public void Canonical_block_names_still_resolve_and_unknown_names_do_not_alias_air()
    {
        var content = new FakeWorldContext().Content;
        var parser = new Command.ArgBlock(content.Items, content.Blocks);
        Assert.Equal((content.Blocks.Get("omniblock:stone").Id, 0),
            parser.Parse(new StringReader("omniblock:stone")));
        Assert.Throws<CommandSyntaxException>(() => parser.Parse(new StringReader("missing_mod:air")));
    }
}
