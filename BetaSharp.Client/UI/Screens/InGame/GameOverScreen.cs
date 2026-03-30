using BetaSharp.Client.Guis;
using BetaSharp.Client.UI.Controls;
using BetaSharp.Client.UI.Controls.Core;
using BetaSharp.Client.UI.Layout.Flexbox;
using BetaSharp.Client.UI.Screens.Menu;

namespace BetaSharp.Client.UI.Screens.InGame;

public class GameOverScreen(BetaSharp game) : UIScreen(game)
{
    public override bool PausesGame => false;

    protected override void Init()
    {
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.Center;

        Root.AddChild(new Background(BackgroundType.GameOver));

        Panel titleContainer = new();
        titleContainer.Style.MarginBottom = 40;
        titleContainer.Style.Height = 40;

        Label title = new()
        {
            Text = "Game over!",
            TextColor = Color.White,
            Scale = 2.0f,
            Centered = true
        };
        titleContainer.AddChild(title);
        Root.AddChild(titleContainer);

        Label scoreLabel = new()
        {
            Text = "Score: &e" + Game.Player.getScore(),
            TextColor = Color.White
        };
        scoreLabel.Style.MarginBottom = 20;
        Root.AddChild(scoreLabel);

        Button btnRespawn = CreateButton();
        btnRespawn.Text = "Respawn";
        btnRespawn.OnClick += (e) =>
        {
            Game.Player.respawn();
            Navigator.Navigate(null);
        };
        btnRespawn.Style.MarginBottom = 4;

        if (Game.Session == null)
        {
            btnRespawn.Enabled = false;
        }
        Root.AddChild(btnRespawn);

        Button btnTitle = CreateButton();
        btnTitle.Text = "Title menu";
        btnTitle.OnClick += (e) =>
        {
            Game.ChangeWorld(null!);
            Navigator.Navigate(new MainMenuScreen(Game));
        };
        Root.AddChild(btnTitle);
    }
}
