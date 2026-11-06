using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SignMyApp.App.Controls;

namespace SignMyApp.App.Tests;

/// <summary>
/// One STA thread with a running dispatcher for every WPF test, and the app's resources loaded once.
/// Nothing here creates a window or calls Show()/ShowDialog(): views are measured, arranged and rendered off-screen.
/// </summary>
public static class WpfHost
{
    static readonly Lazy<Dispatcher> _dispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    static Dispatcher Start()
    {
        Dispatcher? d = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            d = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(d));
            var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.InitializeComponent();
            FocusRing.Register();
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "WPF test STA",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        return d!;
    }

    public static void Run(Action action) => _dispatcher.Value.Invoke(action);

    public static T Run<T>(Func<T> func) => _dispatcher.Value.Invoke(func);

    public static void RunAsync(Func<Task> func) =>
        _dispatcher.Value.InvokeAsync(func).Task.Unwrap().GetAwaiter().GetResult();

    /// <summary>Let bindings, templates and queued focus work finish (call on the STA thread).</summary>
    public static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>Lay out an element at a size, off-screen.</summary>
    public static void Layout(FrameworkElement element, double width, double height)
    {
        element.Width = width;
        element.Height = height;
        for (int i = 0; i < 3; i++)
        {
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();
            Pump();
        }
    }

    public static string ArtifactsDir([CallerFilePath] string here = "")
    {
        // tests/SignMyApp.App.Tests/WpfHost.cs -> tests/artifacts
        var dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "artifacts"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Render to PNG in tests/artifacts. Returns the path.</summary>
    public static string Render(FrameworkElement element, double width, double height, string name, Brush? background = null)
    {
        // Full-window renders include the real caption control. No Window/HWND is needed.
        if (element is SignMyApp.App.Views.MainView main)
        {
            main.ClearValue(FrameworkElement.WidthProperty);
            main.ClearValue(FrameworkElement.HeightProperty);
            var frame = new System.Windows.Controls.Grid();
            frame.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = new GridLength(32) });
            frame.RowDefinitions.Add(new System.Windows.Controls.RowDefinition());
            frame.Children.Add(new SignMyApp.App.Views.TitleBar());
            System.Windows.Controls.Grid.SetRow(main, 1);
            frame.Children.Add(main);
            element = frame;
        }
        Layout(element, width, height);
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        if (background is not null)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen()) dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
            bmp.Render(dv);
        }
        bmp.Render(element);
        var path = Path.Combine(ArtifactsDir(), name + ".png");
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) enc.Save(fs);
        return path;
    }
}
