using R3;

namespace PicView.Core.ViewModels;

public class ToolTipViewModel
{
    // Pass a null equality comparer so the property re-emits even when the same
    // message string is set consecutively (e.g. labeling several files in a row
    // with the same label). Without this, R3 suppresses duplicate values and the
    // toast would not appear for repeated identical messages.
    public BindableReactiveProperty<string?> ToolTipMessageSource { get; } = new(null, equalityComparer: null);
    public BindableReactiveProperty<bool> ToolTipMessageCentered { get; } = new();
    public ReactiveProperty<TimeSpan> ToolTipMessageInterval { get; } = new(TimeSpan.FromSeconds(2.5));
}