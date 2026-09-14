using System.Globalization;
using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Server.Worlds;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.InGame;

/// <summary>
///     Integrated-server progress surface for persistent fixed-area generation. Progress enters
///     through immutable server-published snapshots; actions are queued back to the server thread.
/// </summary>
public sealed class WorldPreparationScreen(
    UIContext context,
    UIScreen? parent,
    Func<IReadOnlyList<FixedAreaPregenerationSnapshot>> getSnapshots,
    Func<IReadOnlyList<AutomaticPregenerationSnapshot>> getAutomaticSnapshots,
    Action<string, string> queueAction) : UIScreen(context)
{
    private Panel _jobList = null!;
    private Label _automaticSummary = null!;
    private Label _summary = null!;
    private int _refreshTicks;

    // Pregeneration orchestration advances from the server tick. Keeping this screen non-pausing
    // also makes its throughput and throttling display truthful while the operator watches it.
    public override bool PausesGame => false;

    protected override void Init()
    {
        Root.Style.AlignItems = Align.Center;
        Root.Style.JustifyContent = Justify.FlexStart;
        Root.AddChild(new Background(BackgroundType.World));

        var title = new Label
        {
            Text = "World Preparation",
            TextColor = Color.White,
            AutomationId = "worldPreparation.title"
        };
        title.Style.MarginTop = 20;
        title.Style.MarginBottom = 8;
        Root.AddChild(title);
        AddTitleSpacer();

        _summary = new Label
        {
            TextColor = Color.GrayA0,
            AutomationId = "worldPreparation.summary"
        };
        _summary.Style.MarginBottom = 6;
        Root.AddChild(_summary);

        _automaticSummary = new Label
        {
            TextColor = Color.GrayA0,
            AutomationId = "worldPreparation.automatic"
        };
        _automaticSummary.Style.MarginBottom = 6;
        Root.AddChild(_automaticSummary);

        var content = new Panel();
        content.Style.Width = 440;
        content.Style.FlexGrow = 1;
        content.Style.MaxHeight = 184;
        content.Style.BackgroundColor = new Color(0, 0, 0, 160);
        content.Style.SetPadding(4);
        Root.AddChild(content);

        var scroll = new ScrollView();
        scroll.Style.FlexGrow = 1;
        content.AddChild(scroll);

        _jobList = new Panel();
        _jobList.Style.FlexDirection = FlexDirection.Column;
        _jobList.Style.Width = null;
        scroll.AddContent(_jobList);

        var done = CreateButton();
        done.Text = Translations.Get("gui.done");
        done.AutomationId = "worldPreparation.done";
        done.Style.MarginTop = 10;
        done.Style.MarginBottom = 20;
        done.Style.FlexShrink = 0;
        done.OnClick += _ => Context.Navigator.Navigate(parent);
        Root.AddChild(done);

        Refresh();
    }

    public override void Update(float partialTicks)
    {
        base.Update(partialTicks);
        if (++_refreshTicks < 10) return;
        _refreshTicks = 0;
        Refresh();
    }

    private void Refresh()
    {
        var snapshots = getSnapshots();
        var automatic = getAutomaticSnapshots();
        _summary.Text = snapshots.Count == 0
            ? "No fixed-area jobs. Start one with /worldgen start."
            : $"{snapshots.Count} persistent job{(snapshots.Count == 1 ? "" : "s")}";
        var enabled = automatic.FirstOrDefault(static snapshot => snapshot.Options.Enabled);
        _automaticSummary.Text = enabled is null
            ? "Automatic generation: disabled (/worldgen auto on <radius> play)"
            : $"Automatic: {enabled.Options.Profile.ToString().ToLowerInvariant()} " +
              $"radius {enabled.Options.RadiusChunks}; {enabled.ThrottleReason}";

        foreach (var child in _jobList.Children.ToArray()) _jobList.RemoveChild(child);
        foreach (var snapshot in snapshots) _jobList.AddChild(CreateJobCard(snapshot));
    }

    private Panel CreateJobCard(FixedAreaPregenerationSnapshot snapshot)
    {
        var definition = snapshot.Definition;
        var percent = definition.TotalTargets == 0
            ? 100
            : snapshot.NextTarget * 100.0 / definition.TotalTargets;
        var card = new Panel
        {
            AutomationId = $"worldPreparation.job.{definition.Id}"
        };
        card.Style.Width = null;
        card.Style.Height = 91;
        card.Style.MarginBottom = 6;
        card.Style.PaddingLeft = 6;
        card.Style.PaddingRight = 6;
        card.Style.PaddingTop = 4;
        card.Style.BackgroundColor = new Color(0, 0, 0, 120);

        AddLine(card, $"{definition.Id}  [{snapshot.Status}]  dimension {definition.Dimension}", Color.White);
        AddLine(card,
            $"{snapshot.NextTarget}/{definition.TotalTargets} ({percent:F1}%)  " +
            $"{snapshot.TargetsPerSecond:F2} targets/s", Color.GrayE0);
        AddLine(card,
            $"prepared {snapshot.PreparedTargets}  decorated {snapshot.DecoratedTargets}  " +
            $"saved {snapshot.SavedTargets}  skipped {snapshot.SkippedTargets}", Color.GrayA0);
        AddLine(card,
            $"written {snapshot.WrittenChunks} chunks  retained {FormatBytes(snapshot.RetainedBytes)} " +
            $"(peak {FormatBytes(snapshot.PeakRetainedBytes)})  disk +{FormatBytes(snapshot.DiskBytes)}",
            Color.GrayA0);
        AddLine(card,
            snapshot.LastError is null
                ? snapshot.ThrottleReason
                : $"{snapshot.ThrottleReason}: {snapshot.LastError}",
            snapshot.Status == FixedAreaPregenerationStatus.Failed
                ? Color.AchievementRequiresRed
                : Color.GrayA0);

        var actions = new Panel();
        actions.Style.FlexDirection = FlexDirection.Row;
        actions.Style.MarginTop = 3;

        actions.AddChild(CreateActionButton("Pause", "pause", definition.Id,
            snapshot.Status == FixedAreaPregenerationStatus.Running));
        actions.AddChild(CreateActionButton("Resume", "resume", definition.Id,
            snapshot.Status is FixedAreaPregenerationStatus.Paused or
                FixedAreaPregenerationStatus.Failed));
        actions.AddChild(CreateActionButton("Cancel", "cancel", definition.Id,
            snapshot.Status is not (FixedAreaPregenerationStatus.Completed or
                FixedAreaPregenerationStatus.Canceled)));
        card.AddChild(actions);
        return card;
    }

    private Button CreateActionButton(
        string text,
        string action,
        string id,
        bool enabled)
    {
        var button = CreateButton();
        button.Text = text;
        button.AutomationId = $"worldPreparation.{action}.{id}";
        button.Enabled = enabled;
        button.Style.Width = 72;
        button.Style.Height = 16;
        button.Style.MarginRight = 4;
        button.OnClick += _ =>
        {
            if (!button.Enabled) return;
            button.Enabled = false;
            queueAction(action, id);
        };
        return button;
    }

    private static void AddLine(Panel card, string text, Color color)
    {
        var label = new Label { Text = text, TextColor = color };
        label.Style.MarginBottom = 2;
        card.AddChild(label);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        var value = Math.Abs((double)bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        if (bytes < 0) value = -value;
        return unit == 0
            ? $"{bytes.ToString(CultureInfo.InvariantCulture)} {units[unit]}"
            : $"{value.ToString("F1", CultureInfo.InvariantCulture)} {units[unit]}";
    }
}
