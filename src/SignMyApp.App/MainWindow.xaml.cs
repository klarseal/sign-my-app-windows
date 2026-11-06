using System.Windows;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using SignMyApp.App.Controls;
using SignMyApp.App.Services;
using System.Windows.Media;

namespace SignMyApp.App;

public partial class MainWindow : Window
{
    HwndSource? _source;
    bool _maximizeCaptured;

    public MainWindow()
    {
        InitializeComponent();
        Icon = AppIdentity.LoadWindowIcon();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        UpdateWindowFrame();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        UpdateWindowFrame();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        UpdateWindowFrame();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        UpdateWindowFrame();
    }

    void UpdateWindowFrame()
    {
        if (_source is null) return; // Never create an HWND just to update chrome.
        WindowFrame.Apply(FallbackWindowBorder, IsActive, WindowState == WindowState.Maximized,
            VisualTreeHelper.GetDpi(this), (attribute, value) => WindowFrame.SetAttribute(_source.Handle, attribute, value));
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // AddHook prepends: this runs before WindowChrome's default HTCLIENT mapping for buttons.
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source?.AddHook(WindowProc);
        UpdateWindowFrame();
    }

    protected override void OnClosed(EventArgs e)
    {
        _source?.RemoveHook(WindowProc);
        _source = null;
        base.OnClosed(e);
    }

    Rect MaximizeScreenBounds()
    {
        var button = Caption.MaximizeButton;
        if (!button.IsVisible || button.ActualWidth <= 0 || button.ActualHeight <= 0) return Rect.Empty;
        // PointToScreen uses the HWND's current monitor DPI, including after a monitor move.
        return new Rect(button.PointToScreen(new Point()), button.PointToScreen(new Point(button.ActualWidth, button.ActualHeight)));
    }

    bool OverMaximize(Point point) => CaptionHitTest.Map(point, MaximizeScreenBounds(),
        ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip) == CaptionHitTest.MaxButton;

    void MaximizeMouse(bool hover, bool pressed = false)
    {
        Caption.MaximizeButton.IsNonClientHover = hover;
        Caption.MaximizeButton.IsNonClientPressed = pressed;
    }

    nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        switch (message)
        {
            case 0x0084: // WM_NCHITTEST: Windows 11 uses HTMAXBUTTON to offer Snap layouts.
                if (OverMaximize(CaptionHitTest.ScreenPoint(lParam)))
                {
                    handled = true;
                    return CaptionHitTest.MaxButton;
                }
                break;
            case 0x00A0: // WM_NCMOUSEMOVE: WPF doesn't receive client hover over HTMAXBUTTON.
                var hover = OverMaximize(CaptionHitTest.ScreenPoint(lParam));
                MaximizeMouse(hover);
                if (hover)
                {
                    var track = new TrackMouseEvent { Size = (uint)Marshal.SizeOf<TrackMouseEvent>(), Flags = 0x12, Hwnd = hwnd }; // LEAVE | NONCLIENT
                    _ = TrackMouseEventNative(ref track);
                }
                break; // Let Windows see hover so it can show its Snap flyout.
            case 0x02A2: // WM_NCMOUSELEAVE
                if (!_maximizeCaptured) MaximizeMouse(false);
                break;
            case 0x00A1: // WM_NCLBUTTONDOWN
            case 0x00A3: // WM_NCLBUTTONDBLCLK: don't let a second click invoke the native button too.
                if (wParam == CaptionHitTest.MaxButton)
                {
                    _maximizeCaptured = true;
                    MaximizeMouse(true, true);
                    _ = SetCapture(hwnd);
                    handled = true;
                }
                break;
            case 0x0200: // WM_MOUSEMOVE while captured: signed client pixels -> screen pixels.
            case 0x0202: // WM_LBUTTONUP
                if (_maximizeCaptured)
                {
                    var client = CaptionHitTest.ScreenPoint(lParam);
                    var screen = new NativePoint { X = (int)client.X, Y = (int)client.Y };
                    _ = ClientToScreen(hwnd, ref screen);
                    var inside = OverMaximize(new Point(screen.X, screen.Y));
                    if (message == 0x0202)
                    {
                        _maximizeCaptured = false;
                        _ = ReleaseCapture();
                        MaximizeMouse(false);
                        if (inside)
                        {
                            if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
                            else SystemCommands.MaximizeWindow(this);
                        }
                    }
                    else MaximizeMouse(inside, inside);
                    handled = true;
                }
                break;
            case 0x0215: // WM_CAPTURECHANGED
            case 0x001F: // WM_CANCELMODE
                _maximizeCaptured = false;
                MaximizeMouse(false);
                break;
        }
        return 0; // Drag, double click, resize, system menu and Win+arrows stay with WindowChrome/Windows.
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct TrackMouseEvent { public uint Size; public uint Flags; public nint Hwnd; public uint HoverTime; }

    [DllImport("user32.dll", EntryPoint = "TrackMouseEvent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool TrackMouseEventNative(ref TrackMouseEvent track);

    [DllImport("user32.dll")]
    static extern nint SetCapture(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
}
