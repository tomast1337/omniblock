using OmniBlock.Client.Network;
using OmniBlock.Client.Threading;
using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu.Net;

public class ConnectingScreen : UIScreen
{
    public ConnectingScreen(
        UIContext context,
        ClientNetworkContext networkContext,
        string host,
        int port) : base(context)
    {
        networkContext.WorldHost.ChangeWorld(null);
        new ThreadConnectToServer(this, networkContext, host, port).Start();
    }

    public ConnectingScreen(UIContext context, ClientNetworkHandler clientHandler) : base(context) => ClientHandler = clientHandler;
    public ClientNetworkHandler? ClientHandler { get; set; }
    public bool IsCancelled { get; private set; }
    public override bool PausesGame => false;

    protected override void Init()
    {
        Root.AddChild(new Background());
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.Center;
        Root.Style.SetPadding(20);

        Label lblStatus = new()
        {
            Text = GetStatusText(),
            TextColor = Color.White,
            Centered = true
        };
        lblStatus.Style.MarginBottom = 20;
        Root.AddChild(lblStatus);

        if (ClientHandler != null)
        {
            Label lblDetail = new()
            {
                Text = ClientHandler.StatusMessage,
                TextColor = Color.GrayA0,
                Centered = true
            };
            lblDetail.Style.MarginBottom = 10;
            Root.AddChild(lblDetail);
        }

        var btnCancel = CreateButton();
        btnCancel.Text = Translations.Get("gui.cancel");
        btnCancel.OnClick += e => Cancel();
        Root.AddChild(btnCancel);
    }

    private string GetStatusText() =>
        ClientHandler == null
            ? Translations.Get("connect.connecting")
            : Translations.Get("connect.authorizing");

    public void Cancel()
    {
        IsCancelled = true;
        ClientHandler?.Disconnect();
        Context.Navigator.Navigate(null);
    }

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);
        ClientHandler?.Tick();

        if (Root.Children.Count >= 2 && Root.Children[1] is Label lblStatus)
        {
            var newText = GetStatusText();
            if (lblStatus.Text != newText)
            {
                lblStatus.Text = newText;
            }
        }
    }
}
