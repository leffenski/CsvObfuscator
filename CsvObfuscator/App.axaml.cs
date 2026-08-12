using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CsvObfuscator.ViewModels;
using CsvObfuscator.Views;

namespace CsvObfuscator;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel()
            };
        else if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
            singleViewFactoryApplicationLifetime.MainViewFactory = () => new PageNavigationHost
            {
                Page = new ContentPage { Content = new MainView { DataContext = new MainViewModel() } }
            };
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            // The browser supplies a single Control host; attach the root view directly.
            singleViewPlatform.MainView = new MainView { DataContext = new MainViewModel() };

        base.OnFrameworkInitializationCompleted();
    }
}