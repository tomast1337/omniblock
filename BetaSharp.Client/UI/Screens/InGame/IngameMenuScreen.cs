using BetaSharp.Client.Guis;
using BetaSharp.Client.UI.Controls;
using BetaSharp.Client.UI.Controls.Core;
using BetaSharp.Client.UI.Layout.Flexbox;
using BetaSharp.Client.UI.Screens.Menu;
using BetaSharp.Client.UI.Screens.Menu.Options;
using BetaSharp.Stats;

namespace BetaSharp.Client.UI.Screens.InGame;

public class IngameMenuScreen(BetaSharp game) : UIScreen(game)
{
    protected override void Init()
    {
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.FlexStart;

        Root.AddChild(new Background(BackgroundType.World));

        Label title = new() { Text = "Game menu", TextColor = Color.White };
        title.Style.MarginTop = 20;
        title.Style.MarginBottom = 8;
        Root.AddChild(title);
        AddTitleSpacer();

        TranslationStorage translator = TranslationStorage.Instance;

        Button btnBack = CreateButton();
        btnBack.Text = "Back to Game";
        btnBack.OnClick += (e) =>
        {
            Game.DisplayUIScreen(null);
            Game.SetIngameFocus();
        };
        btnBack.Style.MarginBottom = 4;
        Root.AddChild(btnBack);

        // --- Stats & Achievements Row ---
        Panel rowStats = new();
        rowStats.Style.FlexDirection = FlexDirection.Row;
        rowStats.Style.JustifyContent = Justify.SpaceBetween;
        rowStats.Style.Width = 200;
        rowStats.Style.MarginBottom = 4;

        Button btnAchievements = CreateButton();
        btnAchievements.Text = StatCollector.TranslateToLocal("gui.achievements");
        btnAchievements.Style.Width = 98;
        btnAchievements.OnClick += (e) => Game.DisplayUIScreen(new AchievementsScreen(Game, this, Game.StatFileWriter));

        Button btnStats = CreateButton();
        btnStats.Text = StatCollector.TranslateToLocal("gui.stats");
        btnStats.Style.Width = 98;
        btnStats.OnClick += (e) => Game.DisplayUIScreen(new StatsScreen(Game, this, Game.StatFileWriter));

        rowStats.AddChild(btnAchievements);
        rowStats.AddChild(btnStats);
        Root.AddChild(rowStats);

        Button btnOptions = CreateButton();
        btnOptions.Text = translator.TranslateKey("menu.options");
        btnOptions.OnClick += (e) => Game.DisplayUIScreen(new OptionsScreen(Game, this, Game.Options));
        btnOptions.Style.MarginBottom = 4;
        Root.AddChild(btnOptions);

        string quitText = (Game.IsMultiplayerWorld() && Game.InternalServer == null) ? "Disconnect" : "Save and quit to title";
        Button btnQuit = CreateButton();
        btnQuit.Text = quitText;
        btnQuit.OnClick += (e) =>
        {
            Game.StatFileWriter.ReadStat(Stats.Stats.LeaveGameStat, 1);
            if (Game.IsMultiplayerWorld())
            {
                Game.World.Disconnect();
            }

            Game.StopInternalServer();
            Game.ChangeWorld(null!);
            Game.Options.ShowDebugInfo = false;
            Game.DisplayUIScreen(new MainMenuScreen(Game));
        };
        Root.AddChild(btnQuit);

        SavingIndicator savingIndicator = new(Game.World.AttemptSaving);
        savingIndicator.Style.Position = PositionType.Absolute;
        savingIndicator.Style.Left = 8;
        savingIndicator.Style.Bottom = 8;
        Root.AddChild(savingIndicator);
    }
}
