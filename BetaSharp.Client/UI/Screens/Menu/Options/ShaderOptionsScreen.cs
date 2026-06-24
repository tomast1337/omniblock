using BetaSharp.Client.Input;
using BetaSharp.Client.Options;
using BetaSharp.Client.UI.Controls;
using BetaSharp.Client.UI.Controls.Core;

namespace BetaSharp.Client.UI.Screens.Menu.Options;

public class ShaderOptionsScreen(UIContext context, UIScreen? parent)
    : BaseOptionsScreen(context, parent, "options.shader.text")
{
    protected override List<OptionSection> GetOptions()
    {
        List<OptionSection> sections = [];
        foreach (var set in Context.Options.ShaderOptions.Sets)
        {
            if (set.Value.Options.Count == 0) continue;

            List<GameOption> options = [];
            if (set.Value.Presets.Count > 0)
                options.Add(new ShaderPresetOption(set.Key, set.Value));
            options.AddRange(set.Value.Options.Select(def => def.IsRange
                ? (GameOption)new ShaderRangeOption(set, def)
                : new ShaderConstOption(set, def)));

            sections.Add(new(
                Translations.Get("options.shader." + set.Key + ".text"),
                options));
        }
        return sections;
    }

    protected override UIElement CreateControlForOption(GameOption option)
    {
        if (option is ShaderPresetOption presetOpt)
        {
            Button btn = CreateButton();
            btn.Text = option.GetDisplayString();
            btn.OnMouseDown += (e) =>
            {
                if (e.Button == MouseButton.Left) presetOpt.Cycle();
                else if (e.Button == MouseButton.Right) presetOpt.Cycle(-1);
                Context.Navigator.Navigate(new ShaderOptionsScreen(Context, Parent));
            };
            return btn;
        }
        return base.CreateControlForOption(option);
    }
}
