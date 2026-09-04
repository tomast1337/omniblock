using OmniBlock.Client.Network;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Rendering;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Controls.ListItems;

public class ServerListItem(ServerData data) : ListItem<ServerData>(data)
{
    public override void Render(UIRenderer renderer)
    {
        base.Render(renderer);

        renderer.DrawText(Value.Name, 5, 5, Color.White);

        var secondary = string.IsNullOrEmpty(Value.Motd) ? Value.Ip : Value.Motd;
        renderer.DrawText(secondary, 5, 17, Color.GrayA0);
    }
}
