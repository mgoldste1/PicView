using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using PicView.Avalonia.CustomControls;
using PicView.Avalonia.Gallery;
using PicView.Avalonia.StartUp;
using PicView.Avalonia.Views.Main;
using PicView.Avalonia.Views.UC;
using PicView.Avalonia.Views.UC.Buttons;
using PicView.Avalonia.WindowBehavior;
using PicView.Core.Config;
using PicView.Core.FileHistory;
using PicView.Core.ViewModels;
using R3.Avalonia;

namespace PicView.Avalonia.UI;

/// <summary>
/// Provides UI-related helper methods and properties
/// </summary>
public static class UIHelper
{
    #region Controls

    public static MainView? GetMainView { get; private set; }
    public static DraggableTabControl? GetMainTabControl { get; private set; }
    public static Control? GetTitlebar { get; private set; }
    public static EditableTitlebar? GetEditableTitlebar { get; private set; }
    public static BottomBar? GetBottomBar { get; private set; }
    public static DropDownMenu? GetDropDownMenu { get; private set; }
    public static ToolTipMessage? GetToolTipMessage { get; private set; }

    public static AvaloniaRenderingFrameProvider? GetFrameProvider =>
        Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow window } ? null : window.FrameProvider;

    public static CoreViewModel CoreViewModel => Application.Current.DataContext as CoreViewModel ?? throw new InvalidOperationException("CoreViewModel is null");

    /// <summary>
    /// Sets up control references from the main desktop application
    /// </summary>
    public static void SetControls(Window mainWindow)
    {
        GetMainView = mainWindow?.FindControl<MainView>("MainView");
        GetTitlebar = mainWindow?.FindControl<Control>("Titlebar");
        GetEditableTitlebar = GetTitlebar?.FindControl<EditableTitlebar>("EditableTitlebar");
        GetBottomBar = mainWindow?.FindControl<BottomBar>("BottomBar");
        GetToolTipMessage = GetMainView?.MainPanel.FindControl<ToolTipMessage>("ToolTipMessage");
        GetMainTabControl = GetMainView.MainTabControl;
    }

    public static HoverBar? GetHoverBar()
    {
        if (Application.Current.DataContext is not CoreViewModel core)
        {
            return null;
        }

        if (core.MainWindows.ActiveWindow.CurrentValue.WindowTabs.ActiveTab.CurrentValue.CurrentView.CurrentValue is ImageViewer imageViewer)
        {
            return imageViewer.HoverBar;
        }

        return null;
    }
    

    
    public static void AddDropDownMenu()
    {
        var dropDownMenu = new DropDownMenu
        {
            Name = "DropDownMenu",
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(3, 0, 3, 0),
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            ZIndex = 2
        };
        GetMainView.MainPanel.Children.Add(dropDownMenu);
        GetDropDownMenu = dropDownMenu;
    }

    #endregion

    #region Helper functions
    
    public static ClickArrowRight? GetClickArrowRight(MainWindowViewModel vm)
    {
        if (vm.WindowTabs.ActiveTab.CurrentValue.CurrentView.CurrentValue is ImageViewer imageViewer)
        {
            return imageViewer.ClickArrowRight;
        }
        return null;
    }
    
    public static ClickArrowLeft? GetClickArrowLeft(MainWindowViewModel vm)
    {
        if (vm.WindowTabs.ActiveTab.CurrentValue.CurrentView.CurrentValue is ImageViewer imageViewer)
        {
            return imageViewer.ClickArrowLeft;
        }
        return null;
    }
    
    private const string BoldFontLocation = "avares://PicView.Avalonia/Assets/Fonts/Roboto-Medium.ttf#Roboto";
    public static FontFamily BoldFontFamily => new(BoldFontLocation);
    private const string MediumFontLocation = "avares://PicView.Avalonia/Assets/Fonts/Roboto-Medium.ttf#Roboto";
    public static FontFamily MediumFontFamily => new(MediumFontLocation);

    public static void ShowMainContextMenu()
    {
        if (GetMainView.Resources.TryGetResource("MainContextMenu", Application.Current.ActualThemeVariant,
                out var value)
            && value is ContextMenu mainContextMenu)
        {
            mainContextMenu.Open();
        }
    }
    
    public static async Task OpenLastFile(MainWindowViewModel vm)
    {
        var lastFilePath = !string.IsNullOrEmpty(Settings.StartUp.LastFile)
            ? Settings.StartUp.LastFile
            : FileHistoryManager.GetLastEntry();

        // If the last file is a local path that no longer exists, show a tooltip and abort.
        if (!string.IsNullOrEmpty(lastFilePath)
            && !lastFilePath.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            && !File.Exists(lastFilePath)
            && !Directory.Exists(lastFilePath))
        {
            TooltipHelper.ShowTooltipMessage(
                $"File does not exist:{Environment.NewLine}{lastFilePath}",
                true,
                TimeSpan.FromSeconds(5));
            return;
        }

        vm.IsLoadingIndicatorShown.Value = true;
        var isViewStartupMenu = false;
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (vm.WindowTabs.ActiveTab.CurrentValue.CurrentView.CurrentValue is StartUpMenu)
            {
                isViewStartupMenu = true;
                vm.WindowTabs.ActiveTab.Value.CurrentView.Value = new ImageViewer();
            }
        }, DispatcherPriority.Send);
        TabNavigationInitializer.InitializeNewTab(vm.WindowTabs.ActiveTab.Value, vm);
        var isSuccessFullyLoaded = await vm.WindowTabs.LoadLastFileAsync();
        if (!isSuccessFullyLoaded && isViewStartupMenu)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                vm.WindowTabs.ActiveTab.Value.CurrentView.Value = new StartUpMenu();
            });
        }
        else
        {
            WindowResizing.SetSize(vm, WindowResizeReason.Layout);
        }
        vm.WindowTabs.ActiveTab.CurrentValue.UpdateTabTitle();
        vm.IsLoadingIndicatorShown.Value = false;
    }
    
    public static async ValueTask OpenPreviousFileHistoryEntry(MainWindowViewModel vm)
    {
        vm.IsLoadingIndicatorShown.Value = true;
        if (await vm.WindowTabs.LoadFromStringAsync(FileHistoryManager.GetPreviousEntry()).ConfigureAwait(false))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (vm.WindowTabs.ActiveTab.CurrentValue.CurrentView.CurrentValue is StartUpMenu)
                {
                    vm.WindowTabs.ActiveTab.Value.CurrentView.Value = new ImageViewer();
                }
            });
            TabNavigationInitializer.InitializeNewTab(vm.WindowTabs.ActiveTab.Value, vm);
        }
        vm.IsLoadingIndicatorShown.Value = false;
    }
    
    public static async ValueTask OpenNextFileHistoryEntry(MainWindowViewModel vm)
    {
        vm.IsLoadingIndicatorShown.Value = true;
        if (await vm.WindowTabs.LoadFromStringAsync(FileHistoryManager.GetNextEntry()).ConfigureAwait(false))
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (vm.WindowTabs.ActiveTab.CurrentValue.CurrentView.CurrentValue is StartUpMenu)
                {
                    vm.WindowTabs.ActiveTab.Value.CurrentView.Value = new ImageViewer();
                }
            });
            TabNavigationInitializer.InitializeNewTab(vm.WindowTabs.ActiveTab.Value, vm);
        }
        vm.IsLoadingIndicatorShown.Value = false;
    }

    /// <summary>
    /// Centers the window or gallery based on current state
    /// </summary>
    public static void Center(MainWindowViewModel vm)
    {
        if (vm.WindowTabs.ActiveTab.CurrentValue.Gallery.IsGalleryExpanded.CurrentValue)
        {
            GalleryHelper.CenterGallery(vm);
        }
        else
        {
            WindowFunctions.CenterWindowOnScreen();
        }
    }

    public static void SetButtonInterval(RepeatButton? button)
    {
        button?.Interval = (int)TimeSpan.FromSeconds(Settings.UIProperties.NavSpeed).TotalMilliseconds;
    }

    public static void SetButtonInterval(IconButton? button)
    {
        button?.Interval = (int)TimeSpan.FromSeconds(Settings.UIProperties.NavSpeed).TotalMilliseconds;
    }

    public static DrawingImage? GetIcon(string resourceName)
    {
        if (!Application.Current.TryGetResource(resourceName,
                Application.Current.RequestedThemeVariant, out var icon))
        {
            return null;
        }

        return icon as DrawingImage;
    }

    public static SolidColorBrush GetBrush(string resourceName) =>
        new(GetColor(resourceName));

    public static Color GetColor(string resourceName)
    {
        if (!Application.Current.TryGetResource(resourceName,
                Application.Current.RequestedThemeVariant, out var textColor))
        {
            return default;
        }

        return textColor is not Color color ? default : color;
    }

    public static SolidColorBrush? GetSolidColorBrush(string resourceName)
    {
        if (!Application.Current.TryGetResource(resourceName,
        Application.Current.RequestedThemeVariant, out var textColor))
        {
            return null;
        }

        return textColor as SolidColorBrush ?? null;
    }

    public static void SetButtonHover(Control button, SolidColorBrush brush)
    {
        button.PointerEntered += (_, _) =>
        {
            brush.Color = GetColor("SecondaryTextColor");
        };
        button.PointerExited += (s, e) =>
        {
            brush.Color = GetColor("MainTextColor");
        };
    }

    public static void SwitchHoverClass(Control control)
    {
        control.Classes.Remove("altHover");
        control.Classes.Add("hover");
    }
    
    public static void SwitchAccentHoverClass(Control control)
    {
        control.Classes.Remove("altHover");
        control.Classes.Add("accentHover");
    }

    public static void SwitchHoverBorderClass(Control control)
    {
        control.Classes.Remove("noBorderHover");
        control.Classes.Add("hover");
    }

    public static SolidColorBrush? GetMenuBackgroundColor() =>
        GetBrush("MenuBackgroundColor");

    #endregion
}