using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using ImageMagick;
using PicView.Avalonia.ColorManagement;
using PicView.Avalonia.CustomControls;
using PicView.Avalonia.Functions;
using PicView.Avalonia.Input;
using PicView.Avalonia.Navigation;
using PicView.Avalonia.SettingsManagement;
using PicView.Avalonia.UI;
using PicView.Avalonia.Views.UC;
using PicView.Avalonia.WindowBehavior;
using PicView.Core.FileAssociations;
using PicView.Core.FileHistory;
using PicView.Core.FileSorting;
using PicView.Core.Localization;
using PicView.Core.ProcessHandling;
using PicView.Core.ViewModels;

namespace PicView.Avalonia.StartUp;

public static class StartUpHelper
{
    public static void StartWithArguments(CoreViewModel vm, bool settingsExists,
        IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        // Assign the main window up front so that services relying on it (e.g. UIHelper.GetFrameProvider)
        // resolve correctly while the initial tab and its subscriptions are being created below.
        desktop.MainWindow = window;

        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1)
        {
            var arg = args[1];
            if (arg.StartsWith("associate:", StringComparison.OrdinalIgnoreCase))
            {
                // Set file associations and exit
                Task.Run(async () =>
                {
                    try
                    {
                        vm.PlatformService.InitiateFileAssociationService();
                        Debug.WriteLine($"Processing file association argument: {arg}");
                        await FileAssociationProcessor.ProcessFileAssociationArguments(arg);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in file association processing: {ex.Message}");
                    }
                    finally
                    {
                        // Always exit the elevated process after processing associations
                        Environment.Exit(0);
                    }
                });
            }
            else if (arg.Equals("blank:", StringComparison.OrdinalIgnoreCase))
            {
                BlankStartUp();
            }
            else if (Settings.UIProperties.OpenInSameWindow && ProcessHelper.CheckIfAnotherInstanceIsRunning())
            {
                IPC.SendWithArgs(args);
            }
            else
            {
                ImageStartUp(arg);
            }
        }
        else
        {
            RegularStartUp(vm, settingsExists, desktop, window);
        }
            
        return;
        
        void ImageStartUp(string filePath)
        {
            SettingsUpdater.InitializeSettings(vm.MainWindows.ActiveWindow.CurrentValue, settingsExists);

            HandleWindowScalingMode(vm, window);

            HandleStartImage(vm, filePath);
            window.Show();

            HandlePostWindowUpdates(vm, settingsExists, desktop, window);
        }

        void BlankStartUp()
        {
            SettingsUpdater.InitializeSettings(vm.MainWindows.ActiveWindow.CurrentValue, settingsExists);

            HandleWindowScalingMode(vm, window);

            HandlePostWindowUpdates(vm, settingsExists, desktop, window);
        }
    }

    
    public static void StartUpBlank(CoreViewModel vm, bool settingsExists,
        IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        SettingsUpdater.InitializeSettings(vm.MainWindows.ActiveWindow.CurrentValue, settingsExists);
        
        HandleWindowScalingMode(vm, window);

        window.Show();

        HandlePostWindowUpdates(vm, settingsExists, desktop, window);
    }
    
    public static void DetachedWindowStartup(CoreViewModel vm,  IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        SettingsUpdater.InitializeSettings(vm.MainWindows.ActiveWindow.CurrentValue, true);

        window.Show();
        
        HandlePostWindowUpdates(vm, true, desktop, window);
    }
    
    public static void RegularStartUp(CoreViewModel vm, bool settingsExists,
        IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        TranslationManager.Init();
        SettingsUpdater.InitializeSettings(vm.MainWindows.ActiveWindow.CurrentValue, settingsExists);

        HandleWindowScalingMode(vm, window);

        StartUpMenuOrLastFile(vm);
        window.Show();

        HandlePostWindowUpdates(vm, settingsExists, desktop, window);
    }
    
    public static void HandleWindowScalingMode(CoreViewModel vm, MainWindow window)
    {
        ScreenHelper.UpdateScreenSize(window);

        if (Settings.WindowProperties.Margin < 0)
        {
            Settings.WindowProperties.Margin = 45;
        }

        if (Settings.WindowProperties.Maximized && !Settings.WindowProperties.Fullscreen)
        {
            vm.MainWindows.ActiveWindow.CurrentValue.PlatformWindowService.Maximize(false);
        }
        else if (Settings.WindowProperties.Fullscreen)
        {
            vm.MainWindows.ActiveWindow.CurrentValue.PlatformWindowService.Fullscreen(false);
        }
        else if (Settings.WindowProperties.AutoFit)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowFunctions.SetAutoFit(vm.MainWindows.ActiveWindow.CurrentValue, window, false);
        }
        else 
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            WindowFunctions.SetManualWindow(vm.MainWindows.ActiveWindow.CurrentValue, window);
            WindowFunctions.InitializeWindowSizeAndPosition(window);
        }
    }

    public static void HandlePostWindowUpdates(CoreViewModel vm, bool settingsExists,
        IClassicDesktopStyleApplicationLifetime desktop, MainWindow window)
    {
        SetMemorySettings();
        
        BackGroundLoadings(settingsExists);

        SetWindowEventHandlers(window);
        HandleThemeUpdates();

        UIHelper.SetControls(window);

        if (Settings.UIProperties.ShowHoverNavigationBar)
        {
            vm.MainWindows.ActiveWindow.CurrentValue.WindowTabs.ActiveTab.CurrentValue.Hoverbar.IsHoverbarVisible
                .Value = !Settings.UIProperties.ShowBottomNavBar;
        }
        
        desktop.MainWindow = window;

        vm.MainWindows.ActiveWindow.CurrentValue.ToolTip ??= new ToolTipViewModel();
        TooltipHelper.StartTooltipSubscription(vm.MainWindows.ActiveWindow.CurrentValue.ToolTip);
        
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Windows needs a named pipe server to open files in the same window
            if (Settings.UIProperties.OpenInSameWindow && !ProcessHelper.CheckIfAnotherInstanceIsRunning())
            {
                _ = IPC.StartListeningForArguments();
            }
        }
        
        Application.Current.Name = "PicView";
        
        return;
        
        void BackGroundLoadings(bool defaultKeybindings)
        {
            Task.Run(async() =>
            {
                // LoadKeybindings already falls back to platform defaults when no
                // keybindings file exists, so always load from disk here. Using
                // SetDefaultKeybindings unconditionally would discard the user's
                // saved (and renamed) keybindings on every launch.
                _ = defaultKeybindings;
                await KeybindingManager.LoadKeybindings(vm.PlatformService);
                vm.MainWindows.ActiveWindow.Value.Mapper =
                    new FunctionsMapper(vm.MainWindows.ActiveWindow.CurrentValue, window);
                FileHistoryManager.Initialize();
                HandleWindowControlSettings(vm, desktop);
                vm.MainWindows.ActiveWindow.CurrentValue.WindowTabs.SetSortOrder((SortFilesBy)Settings.Sorting.SortPreference);
            });
        }
    }

    private static void SetMemorySettings()
    {
        ResourceLimits.LimitMemory(new Percentage(80));
        GCSettings.LatencyMode = GCLatencyMode.LowLatency;
    }

    private static void HandleThemeUpdates()
    {
        if (Settings.Theme.GlassTheme)
        {
            GlassThemeHelper.GlassThemeUpdates();
        }

        BackgroundManager.SetBackground(Settings.UIProperties.BgColorChoice);
        ColorManager.UpdateAccentColors(Settings.Theme.ColorTheme);
    }

    private static void HandleWindowControlSettings(CoreViewModel vm, IClassicDesktopStyleApplicationLifetime desktop)
    {
        vm.MainWindows.ActiveWindow.CurrentValue.IsScrollingEnabled.Value = Settings.Zoom.ScrollEnabled;

        if (Settings.WindowProperties.TopMost)
        {
            Dispatcher.UIThread.Invoke(() => { desktop.MainWindow.Topmost = true; });
        }
    }

    private static void HandleStartImage(CoreViewModel vm, string arg)
    {
        Task.Run(() => QuickLoad.QuickLoadAsync(vm, arg, false));
    }

    public static void StartUpMenuOrLastFile(CoreViewModel vm)
    {
        if (Settings.StartUp.OpenLastFile)
        {
            if (string.IsNullOrWhiteSpace(Settings.StartUp.LastFile))
            {
                ShowStartUpMenu();
            }
            else
            {
                Task.Run(() => QuickLoad.QuickLoadAsync(vm, Settings.StartUp.LastFile, true));
            }
        }
        else
        {
            ShowStartUpMenu();
        }

        return;

        void ShowStartUpMenu()
        {
            var startUpMenu = new StartUpMenu
            {
                Buttons =
                {
                    DataContext = vm
                }
            };
            vm.MainWindows.ActiveWindow.CurrentValue.WindowTabs.ActiveTab.Value.CurrentView.Value = startUpMenu;
            TabNavigationInitializer.Initialize(vm);
        }
    }

    private static void SetWindowEventHandlers(Window w)
    {
        // Using AddHandler fixes the first keydown event not firing properly
        w.AddHandler(InputElement.KeyDownEvent, MainWindow_KeysDownAsync, RoutingStrategies.Tunnel);
        w.AddHandler(InputElement.KeyUpEvent, MainWindow_KeyUpAsync, RoutingStrategies.Tunnel);
        w.PointerPressed += async (_, e) => await MouseShortcuts.MainWindow_PointerPressed(e, w).ConfigureAwait(false);

        w.Deactivated += delegate
        {
            MainKeyboardShortcuts.Reset();
            MainKeyboardShortcuts.ClearKeyDownModifiers();
        };
    }

    private static async ValueTask MainWindow_KeysDownAsync(object? sender, KeyEventArgs e)
    {
        // Extract the ViewModel from the window that received the key press
        var vm = (sender as Control)?.DataContext as MainWindowViewModel;
        await MainKeyboardShortcuts.MainWindow_KeysDownAsync(e, vm).ConfigureAwait(false);
    }

    private static async ValueTask MainWindow_KeyUpAsync(object? sender, KeyEventArgs e)
    {
        // Extract the ViewModel from the window that received the key press
        var vm = (sender as Control)?.DataContext as MainWindowViewModel;
        await MainKeyboardShortcuts.MainWindow_KeysUpAsync(e, vm);
    }
}