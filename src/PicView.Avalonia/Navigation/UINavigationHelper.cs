using Avalonia;
using Avalonia.Threading;
using PicView.Avalonia.CustomControls;
using PicView.Avalonia.StartUp;
using PicView.Core.FileHistory;
using PicView.Core.ViewModels;

namespace PicView.Avalonia.Navigation;

public static class UINavigationHelper
{
    public static async ValueTask OpenNextFileHistoryEntry(MainWindow mainWindow, MainWindowViewModel vm)
    {
        var tab = vm.WindowTabs.ActiveTab.CurrentValue;
        if (!tab.IsInitialized)
        {
            var nextEntry = FileHistoryManager.GetNextEntry();
            var lastFile = string.IsNullOrWhiteSpace(nextEntry) ? nextEntry : Settings.StartUp.LastFile;
            if (string.IsNullOrWhiteSpace(lastFile))
            {
                return;
            }

            var core = await Dispatcher.UIThread.InvokeAsync(() => Application.Current.DataContext as CoreViewModel);
            await QuickLoad.QuickLoadAsync(mainWindow, core, lastFile, true);
            return;
        }
        vm.IsLoadingIndicatorShown.Value = true;
        await vm.WindowTabs.LoadFromStringAsync(FileHistoryManager.GetNextEntry()).ConfigureAwait(false);
        vm.IsLoadingIndicatorShown.Value = false;
        vm.TopTitlebarViewModel.DropDownMenu.IsDropDownMenuVisible.Value = false;
    }
    
    public static async ValueTask OpenPreviousFileHistoryEntry(MainWindow mainWindow, MainWindowViewModel vm)
    {
        var tab = vm.WindowTabs.ActiveTab.CurrentValue;
        if (!tab.IsInitialized)
        {
            var prevEntry = FileHistoryManager.GetPreviousEntry();
            var lastFile = string.IsNullOrWhiteSpace(prevEntry) ? prevEntry : Settings.StartUp.LastFile;
            if (string.IsNullOrWhiteSpace(lastFile))
            {
                return;
            }

            var core = await Dispatcher.UIThread.InvokeAsync(() => Application.Current.DataContext as CoreViewModel);
            await QuickLoad.QuickLoadAsync(mainWindow, core, lastFile, true);
            return;
        }
        vm.IsLoadingIndicatorShown.Value = true;
        await vm.WindowTabs.LoadFromStringAsync(FileHistoryManager.GetPreviousEntry()).ConfigureAwait(false);
        vm.IsLoadingIndicatorShown.Value = false;
        vm.TopTitlebarViewModel.DropDownMenu.IsDropDownMenuVisible.Value = false;
    }
    
    public static async Task OpenLastFile(MainWindow mainWindow, MainWindowViewModel vm)
    {
        var tab = vm.WindowTabs.ActiveTab.CurrentValue;
        if (!tab.IsInitialized)
        {
            var lastEntry = FileHistoryManager.GetLastEntry();
            var lastFile = string.IsNullOrWhiteSpace(lastEntry) ? lastEntry : Settings.StartUp.LastFile;
            if (string.IsNullOrWhiteSpace(lastFile))
            {
                return;
            }

            // If the last file is a local path that no longer exists, show a tooltip and abort.
            if (!lastFile.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                && !File.Exists(lastFile)
                && !Directory.Exists(lastFile))
            {
                UI.TooltipHelper.ShowTooltipMessage(
                    $"File does not exist:{Environment.NewLine}{lastFile}",
                    true,
                    TimeSpan.FromSeconds(5));
                return;
            }

            var core = await Dispatcher.UIThread.InvokeAsync(() => Application.Current.DataContext as CoreViewModel);
            await QuickLoad.QuickLoadAsync(mainWindow, core, lastFile, true);
            return;
        }
        vm.IsLoadingIndicatorShown.Value = true;
        await vm.WindowTabs.LoadLastFileAsync();
        vm.WindowTabs.ActiveTab.CurrentValue.UpdateTabTitle();
        vm.IsLoadingIndicatorShown.Value = false;
        vm.TopTitlebarViewModel.DropDownMenu.IsDropDownMenuVisible.Value = false;
    }
}