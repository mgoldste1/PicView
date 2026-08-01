using Avalonia.Threading;
using PicView.Avalonia.Views.UC;
using PicView.Core.ViewModels;

namespace PicView.Avalonia.UI;

public static class ViewChangeHelper
{
    public static void SwitchToStartUpMenu(MainWindowViewModel vm)
    {
        var tab = vm.WindowTabs.ActiveTab.CurrentValue;
        if (tab.CurrentView.CurrentValue is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Dispatcher.UIThread.Invoke(() =>
        {
            tab.CurrentView.Value = new StartUpMenu();
        });
        vm.IsLoadingIndicatorShown.Value = false;
    }
}