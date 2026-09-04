using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu;

public class ConfirmationScreen(
    UIContext context,
    UIScreen parent,
    string title,
    string message,
    string confirmText,
    string cancelText,
    Action<bool> callback) : UIScreen(context)
{
    protected override void Init()
    {
        Root.AutomationId = "confirmation";
        Root.AddChild(new Background());
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.Center;
        Root.Style.SetPadding(20);

        Label lblTitle = new()
        {
            Text = title,
            TextColor = Color.White
        };
        lblTitle.Style.MarginBottom = 10;
        Root.AddChild(lblTitle);

        Label lblMsg = new()
        {
            Text = message,
            TextColor = Color.GrayA0
        };
        lblMsg.Style.MarginBottom = 20;
        Root.AddChild(lblMsg);

        Panel buttonPanel = new();
        buttonPanel.Style.FlexDirection = FlexDirection.Row;

        var btnConfirm = CreateButton();
        btnConfirm.AutomationId = "confirmation.confirm";
        btnConfirm.Text = confirmText;
        btnConfirm.Style.Width = 100;
        btnConfirm.Style.SetMargin(0, 4, 0, 0);
        btnConfirm.OnClick += e =>
        {
            callback(true);
            Context.Navigator.Navigate(parent);
        };
        buttonPanel.AddChild(btnConfirm);

        var btnCancel = CreateButton();
        btnCancel.AutomationId = "confirmation.cancel";
        btnCancel.Text = cancelText;
        btnCancel.Style.Width = 100;
        btnCancel.OnClick += e =>
        {
            callback(false);
            Context.Navigator.Navigate(parent);
        };
        buttonPanel.AddChild(btnCancel);

        Root.AddChild(buttonPanel);
    }
}
