using System;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using LDOCE5ViewerX.Services;
using LDOCE5ViewerX.ViewModels;
using LDOCE5ViewerX.Views;

namespace LDOCE5ViewerX;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnNativeAboutClicked(object? sender, EventArgs e)
    {
        if (GetMainWindow() is { DataContext: MainWindowViewModel viewModel } mainWindow)
        {
            viewModel.ShowAboutCommand.Execute(null);
            mainWindow.Activate();
        }
    }

    private void OnNativeSettingsClicked(object? sender, EventArgs e)
    {
        if (GetMainWindow() is MainWindow mainWindow)
        {
            mainWindow.ShowSettingsDialog();
            mainWindow.Activate();
        }
    }

    private MainWindow? GetMainWindow()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow mainWindow })
        {
            return mainWindow;
        }

        return null;
    }

    /// <summary>
    /// Applies the selected theme mode to the running Avalonia application.
    /// </summary>
    /// <param name="themeMode">Theme mode selected by the user.</param>
    public static void ApplyThemeMode(ThemeMode themeMode)
    {
        if (Current is null)
        {
            return;
        }

        ThemeVariant theme = themeMode switch
        {
            ThemeMode.Automatic => ThemeVariant.Default,
            ThemeMode.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Light,
        };

        if (theme != Current.RequestedThemeVariant)
        {
            Current.RequestedThemeVariant = theme;
        }
    }
}
