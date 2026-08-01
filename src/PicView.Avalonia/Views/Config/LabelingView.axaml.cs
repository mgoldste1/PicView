using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PicView.Avalonia.Input;
using PicView.Core.DebugTools;
using PicView.Core.Models;

namespace PicView.Avalonia.Views.Config;

public partial class LabelingView : UserControl
{
    private const string LabelPrefix = "Label:";

    private readonly ObservableCollection<LabelCategoryItem> _categories = new();

    private bool _isSyncingClearLabelKey;

    public LabelingView()
    {
        InitializeComponent();

        LabelCategoriesItemsControl.ItemsSource = _categories;
        Loaded += (_, _) => RefreshCategories();
    }

    private void RefreshCategories()
    {
        _categories.Clear();

        if (KeybindingManager.CustomShortcuts is null)
        {
            RefreshAvailableKeys();
            SyncClearLabelKeySelection();
            return;
        }

        var items = KeybindingManager.CustomShortcuts
            .Where(x => x.Value.StartsWith(LabelPrefix, StringComparison.Ordinal))
            .Select(x => new LabelCategoryItem(x.Value[LabelPrefix.Length..], x.Key.ToString()))
            .OrderBy(x => x.Key, StringComparer.Ordinal);

        foreach (var item in items)
        {
            _categories.Add(item);
        }

        RefreshAvailableKeys();
        SyncClearLabelKeySelection();
    }

    private void RefreshAvailableKeys()
    {
        var previouslySelected = (NewLabelKeyComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

        NewLabelKeyComboBox.Items.Clear();

        for (var digit = 1; digit <= 9; digit++)
        {
            if (IsDigitInUse(digit))
            {
                continue;
            }

            NewLabelKeyComboBox.Items.Add(new ComboBoxItem
            {
                Content = digit.ToString()
            });
        }

        if (previouslySelected is not null)
        {
            NewLabelKeyComboBox.SelectedItem = NewLabelKeyComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(x => string.Equals(x.Content?.ToString(), previouslySelected, StringComparison.Ordinal));
        }
    }

    private static bool IsDigitInUse(int digit)
    {
        if (KeybindingManager.CustomShortcuts is null)
        {
            return false;
        }

        var gesture = KeyGesture.Parse("D" + digit);
        return KeybindingManager.CustomShortcuts.ContainsKey(gesture);
    }

    private async void OnAddLabelClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var name = NewLabelNameTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowMessage("Enter a label name.");
                return;
            }

            if (NewLabelKeyComboBox.SelectedItem is not ComboBoxItem { Content: { } content })
            {
                ShowMessage("Select a key (1-9) for the label.");
                return;
            }

            var digit = content.ToString();
            if (string.IsNullOrEmpty(digit))
            {
                ShowMessage("Select a key (1-9) for the label.");
                return;
            }

            var gestureText = "D" + digit;
            var gesture = KeyGesture.Parse(gestureText);
            var action = LabelPrefix + name;

            if (KeybindingManager.CustomShortcuts is null)
            {
                ShowMessage("Keybindings are not loaded yet.");
                return;
            }

            if (KeybindingManager.CustomShortcuts.Any(x =>
                    string.Equals(x.Value, action, StringComparison.OrdinalIgnoreCase)))
            {
                ShowMessage($"A label named \"{name}\" already exists.");
                return;
            }

            if (KeybindingManager.CustomShortcuts.TryGetValue(gesture, out var existingAction))
            {
                if (!existingAction.StartsWith(LabelPrefix, StringComparison.Ordinal))
                {
                    ShowMessage($"Key {digit} is already used by another action.");
                    return;
                }
            }

            KeybindingManager.CustomShortcuts[gesture] = action;
            await KeybindingManager.UpdateKeyBindingsFile().ConfigureAwait(true);

            NewLabelNameTextBox.Text = string.Empty;
            NewLabelKeyComboBox.SelectedItem = null;
            HideMessage();
            RefreshCategories();
        }
        catch (Exception exception)
        {
            DebugHelper.LogDebug(nameof(LabelingView), nameof(OnAddLabelClick), exception);
            ShowMessage("Could not add the label.");
        }
    }

    private async void OnRemoveLabelClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button { Tag: string gestureText } || KeybindingManager.CustomShortcuts is null)
            {
                return;
            }

            var gesture = KeyGesture.Parse(gestureText);
            KeybindingManager.CustomShortcuts.Remove(gesture);

            await KeybindingManager.UpdateKeyBindingsFile().ConfigureAwait(true);
            HideMessage();
            RefreshCategories();
        }
        catch (Exception exception)
        {
            DebugHelper.LogDebug(nameof(LabelingView), nameof(OnRemoveLabelClick), exception);
        }
    }

    private void OnRenameLabelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox textBox)
        {
            e.Handled = true;
            CommitRename(textBox);
        }
    }

    private void OnRenameLabelCommit(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
        {
            CommitRename(textBox);
        }
    }

    private async void CommitRename(TextBox textBox)
    {
        try
        {
            if (textBox.Tag is not string gestureText || KeybindingManager.CustomShortcuts is null)
            {
                return;
            }

            var gesture = KeyGesture.Parse(gestureText);
            if (!KeybindingManager.CustomShortcuts.TryGetValue(gesture, out var currentAction) ||
                !currentAction.StartsWith(LabelPrefix, StringComparison.Ordinal))
            {
                return;
            }

            var newName = textBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(newName))
            {
                // Revert to the current name and warn.
                textBox.Text = currentAction[LabelPrefix.Length..];
                ShowMessage("Label name cannot be empty.");
                return;
            }

            var newAction = LabelPrefix + newName;
            if (string.Equals(newAction, currentAction, StringComparison.Ordinal))
            {
                return;
            }

            if (KeybindingManager.CustomShortcuts.Any(x =>
                    x.Key != gesture &&
                    string.Equals(x.Value, newAction, StringComparison.OrdinalIgnoreCase)))
            {
                textBox.Text = currentAction[LabelPrefix.Length..];
                ShowMessage($"A label named \"{newName}\" already exists.");
                return;
            }

            KeybindingManager.CustomShortcuts[gesture] = newAction;
            await KeybindingManager.UpdateKeyBindingsFile().ConfigureAwait(true);
            HideMessage();
        }
        catch (Exception exception)
        {
            DebugHelper.LogDebug(nameof(LabelingView), nameof(CommitRename), exception);
        }
    }

    private static KeyGesture DigitZeroGesture => KeyGesture.Parse("D0");

    private static KeyGesture BacktickGesture => KeyGesture.Parse("OemTilde");

    private static bool IsClearLabelBoundTo(KeyGesture gesture)
    {
        return KeybindingManager.CustomShortcuts is not null &&
               KeybindingManager.CustomShortcuts.TryGetValue(gesture, out var action) &&
               string.Equals(action, "ClearLabel", StringComparison.OrdinalIgnoreCase);
    }

    private void SyncClearLabelKeySelection()
    {
        if (ClearLabelKeyComboBox is null)
        {
            return;
        }

        _isSyncingClearLabelKey = true;
        try
        {
            // Default to "0" (index 0); select backtick (index 1) if that gesture is bound to ClearLabel.
            ClearLabelKeyComboBox.SelectedIndex = IsClearLabelBoundTo(BacktickGesture) ? 1 : 0;
        }
        finally
        {
            _isSyncingClearLabelKey = false;
        }
    }

    private async void OnClearLabelKeyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingClearLabelKey)
        {
            return;
        }

        try
        {
            if (KeybindingManager.CustomShortcuts is null)
            {
                return;
            }

            var useBacktick = ClearLabelKeyComboBox.SelectedIndex == 1;
            var newGesture = useBacktick ? BacktickGesture : DigitZeroGesture;
            var oldGesture = useBacktick ? DigitZeroGesture : BacktickGesture;

            // Only allow assigning the new gesture if it is free or already used by ClearLabel.
            if (KeybindingManager.CustomShortcuts.TryGetValue(newGesture, out var existingAction) &&
                !string.Equals(existingAction, "ClearLabel", StringComparison.OrdinalIgnoreCase))
            {
                ShowMessage("That key is already used by another action.");
                SyncClearLabelKeySelection();
                return;
            }

            // Remove any existing ClearLabel binding, then set the chosen one.
            if (IsClearLabelBoundTo(oldGesture))
            {
                KeybindingManager.CustomShortcuts.Remove(oldGesture);
            }

            KeybindingManager.CustomShortcuts[newGesture] = "ClearLabel";
            await KeybindingManager.UpdateKeyBindingsFile().ConfigureAwait(true);
            HideMessage();
        }
        catch (Exception exception)
        {
            DebugHelper.LogDebug(nameof(LabelingView), nameof(OnClearLabelKeyChanged), exception);
        }
    }

    private void ShowMessage(string message)
    {
        LabelEditorMessage.Text = message;
        LabelEditorMessage.IsVisible = true;
    }

    private void HideMessage()
    {
        LabelEditorMessage.Text = string.Empty;
        LabelEditorMessage.IsVisible = false;
    }
}
