using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace SignMyApp.App.Services;

internal static class AppIdentity
{
    internal const string AppUserModelId = "Klarseal.SignMyApp";

    // Keep the IconBitmapDecoder attached: WPF can choose the right frame for each native icon size.
    internal static BitmapFrame LoadWindowIcon()
    {
        var decoder = new IconBitmapDecoder(new Uri("pack://application:,,,/SignMyApp;component/Assets/klarseal.ico"),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    internal static void Register() => Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(AppUserModelId));

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
