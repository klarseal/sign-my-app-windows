using System.Windows;
using SignMyApp.App.Controls;
using SignMyApp.App.Demo;
using SignMyApp.App.Services;
using SignMyApp.App.ViewModels;

namespace SignMyApp.App;

public partial class App : Application
{
    /// <summary>Builds the view model for the given command line. <c>--demo</c> shows the canvas sample data.</summary>
    public static MainViewModel CreateViewModel(string[] args)
    {
        bool demo = args.Any(a => string.Equals(a, "--demo", StringComparison.OrdinalIgnoreCase));
        AppServices services;
        if (demo)
        {
            var options = new DemoOptions
            {
                CheckDelay = TimeSpan.FromMilliseconds(500),
                SignDelay = TimeSpan.FromMilliseconds(900),
            };
            options.Trusted.Add((DemoData.OtherProject, DemoData.Store.Thumbprint));
            services = new DemoServices(options, new WindowsShell()).Services;
        }
        else
        {
            services = CoreComposition.CreateReal();
        }
        return new MainViewModel(services);
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        AppIdentity.Register(); // Before any window, including an initialization error message.
        base.OnStartup(e);
        FocusRing.Register();
        MainViewModel vm;
        try { vm = CreateViewModel(e.Args); }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ex.Message, Text.AppTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
        await vm.InitializeAsync();
    }
}
