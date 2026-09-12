using OmniBlock.Client.Options;

namespace OmniBlock.Client.UI.Screens.Menu.Options;

public class VideoSettingsScreen(UIContext context, UIScreen? parent) : BaseOptionsScreen(context, parent, "options.videoTitle")
{
    protected override List<OptionSection> GetOptions() =>
    [
        new(Translations.Get("options.video.performance"), [
            Options.RenderDistanceOption,
            Options.SimulationDistanceOption,
            Options.PresentationQualityOption,
            Options.FramerateLimitOption,
            Options.VSyncOption,
            Options.MsaaOption,
            Options.MipmapsOption,
            Options.AnisotropicOption,
            Options.CloudsQualityOption,
            Options.SoftCloudsOption,
            new NavigationOption("options.shader.text",
                () => Context.Navigator.Navigate(new ShaderOptionsScreen(Context, this)))
        ]),

        new(Translations.Get("options.video.display"), [
            Options.ViewBobbingOption,
            Options.ChunkFadeOption,
            Options.AlternateBlocksOption
        ])
    ];
}
