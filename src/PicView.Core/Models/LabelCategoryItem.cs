namespace PicView.Core.Models;

/// <summary>
/// UI model representing a single label category that is backed by a keybinding.
/// </summary>
public class LabelCategoryItem
{
    public LabelCategoryItem(string name, string key)
    {
        Name = name;
        Key = key;
    }

    /// <summary>
    /// The label name, e.g. "red". Combined with the "Label:" prefix to form the action name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The assigned key gesture display, e.g. "D1".
    /// </summary>
    public string Key { get; set; } = string.Empty;
}
