using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using PicView.Avalonia.Input;

namespace PicView.Avalonia.Views;

public partial class LabelOverviewWindow : Window
{
    public LabelOverviewWindow()
    {
        InitializeComponent();
        PopulateLabels();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void MoveWindow(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void Close(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PopulateLabels()
    {
        var items = this.FindControl<ItemsControl>("LabelItems");
        var emptyText = this.FindControl<TextBlock>("EmptyText");

        var rows = KeybindingManager.CustomShortcuts?
            .Where(x => x.Value.StartsWith("Label:", System.StringComparison.OrdinalIgnoreCase))
            .Select(x => new LabelShortcutRow
            {
                Key = FormatKey(x.Key),
                Label = x.Value["Label:".Length..].Trim()
            })
            .OrderBy(x => x.Key, System.StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

        if (items is not null)
        {
            items.ItemsSource = rows;
            items.IsVisible = rows.Count > 0;
        }

        if (emptyText is not null)
        {
            emptyText.IsVisible = rows.Count == 0;
        }
    }

    private static string FormatKey(KeyGesture gesture)
    {
        var modifiers = gesture.KeyModifiers == KeyModifiers.None
            ? string.Empty
            : gesture.KeyModifiers + "+";

        var key = gesture.Key.ToString();

        // Friendly-ize digit keys: D1 -> 1, NumPad1 -> 1
        if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1]))
        {
            key = key[1].ToString();
        }
        else if (key.StartsWith("NumPad", System.StringComparison.Ordinal)
                 && key.Length == 7 && char.IsDigit(key[6]))
        {
            key = key[6].ToString();
        }

        return modifiers + key;
    }

    private sealed class LabelShortcutRow
    {
        public string Key { get; init; } = string.Empty;
        public string Label { get; init; } = string.Empty;
    }
}
