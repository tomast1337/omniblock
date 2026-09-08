using OmniBlock.Client.UI.Controls;
using OmniBlock.Client.UI.Controls.Core;
using OmniBlock.Client.UI.Controls.ListItems;
using OmniBlock.Client.UI.Layout.Flexbox;
using OmniBlock.Worlds;
using Color = OmniBlock.Client.UI.Colors.Color;

namespace OmniBlock.Client.UI.Screens.Menu.World;

public class SelectWorldTypeScreen(
    UIContext context,
    CreateWorldScreen parent,
    WorldType currentType) : UIScreen(context)
{
    private readonly List<SelectWorldTypeListItem> _listItems = [];
    private readonly List<WorldType> _types = [.. context.Content.WorldTypes.All.Where(static type => type.CanBeCreated)];
    private ScrollView _scrollView = null!;
    private int _selectedIndex = -1;

    protected override void Init()
    {
        Root.AddChild(new Background());
        Root.Style.AlignItems = Align.Center;
        Root.Style.SetPadding(20);

        Label title = new()
        {
            Text = Translations.Get("selectWorld.selectWorldType"),
            TextColor = Color.White
        };
        title.Style.MarginBottom = 10;
        Root.AddChild(title);

        _scrollView = new ScrollView();
        _scrollView.Style.Width = 300;
        _scrollView.Style.FlexGrow = 1;
        _scrollView.Style.MarginBottom = 10;
        _scrollView.Style.BackgroundColor = Color.BackgroundBlackAlpha;
        Root.AddChild(_scrollView);

        PopulateTypeList();

        Panel buttonPanel = new();
        buttonPanel.Style.FlexDirection = FlexDirection.Row;

        var btnDone = CreateButton();
        btnDone.Text = Translations.Get("gui.done");
        btnDone.Style.Width = 100;
        btnDone.Style.SetMargin(2);
        btnDone.OnClick += e =>
        {
            if (_selectedIndex >= 0)
            {
                parent.SetWorldType(_types[_selectedIndex]);
                Context.Navigator.Navigate(parent);
            }
        };
        buttonPanel.AddChild(btnDone);

        var btnCancel = CreateButton();
        btnCancel.Text = Translations.Get("gui.cancel");
        btnCancel.Style.Width = 100;
        btnCancel.Style.SetMargin(2);
        btnCancel.OnClick += e => Context.Navigator.Navigate(parent);
        buttonPanel.AddChild(btnCancel);

        Root.AddChild(buttonPanel);

        _selectedIndex = _types.IndexOf(currentType);
        if (_selectedIndex >= 0)
        {
            SelectItem(_selectedIndex);
        }
    }

    private void PopulateTypeList()
    {
        _listItems.Clear();
        foreach (var type in _types)
        {
            var index = _listItems.Count;
            SelectWorldTypeListItem item = new(type);
            item.OnClick += e => SelectItem(index);
            _scrollView.AddContent(item);
            _listItems.Add(item);
        }
    }

    private void SelectItem(int index)
    {
        _selectedIndex = index;
        foreach (var item in _listItems)
        {
            item.IsSelected = false;
        }

        if (index >= 0 && index < _listItems.Count)
        {
            _listItems[index].IsSelected = true;
        }
    }
}
