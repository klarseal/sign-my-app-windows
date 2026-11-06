using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Interop;
using SignMyApp.App.Services;

namespace SignMyApp.App.Tests;

public class AppIconTests
{
    [Fact]
    public void Ico_decodes_all_sizes_and_Window_retains_the_icon_decoder() => WpfHost.Run(() =>
    {
        var uri = new Uri("pack://application:,,,/SignMyApp;component/Assets/klarseal.ico");
        var decoder = new IconBitmapDecoder(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        Assert.Equal(new[] { 16, 24, 32, 48, 256 }, decoder.Frames.Select(f => f.PixelWidth));
        foreach (var frame in decoder.Frames)
        {
            Assert.Equal(frame.PixelWidth, frame.PixelHeight);
            var pixels = new byte[frame.PixelHeight * frame.PixelWidth * 4];
            frame.CopyPixels(pixels, frame.PixelWidth * 4, 0);
            Assert.Contains(pixels, p => p > 240);
        }
        var window = new MainWindow();
        var icon = Assert.IsAssignableFrom<BitmapFrame>(window.Icon);
        Assert.IsType<IconBitmapDecoder>(icon.Decoder);
        Assert.Equal(5, icon.Decoder.Frames.Count);
        Assert.Equal(0, new System.Windows.Interop.WindowInteropHelper(window).Handle);
    });

    [Fact]
    public void Built_executable_has_extractable_large_and_small_Klarseal_icons() =>
        AssertExecutableIcons(BuildMetadata("SignMyAppExecutable"));

    [PublishFact]
    public void Published_single_file_executable_has_extractable_large_and_small_Klarseal_icons()
    {
        var sourceDirectory = Path.GetFullPath(BuildMetadata("SignMyAppSourceDirectory"));
        const string version = "0.0.0-icon-test";
        var exe = Path.Combine(sourceDirectory, "out", version, $"SignMyApp-{version}-win-x64.exe");
        Assert.True(File.Exists(exe), $"Published single-file executable missing: {exe}. From a clean checkout, run build/Publish-SignMyApp.ps1 -Version {version} -Thumbprint {new string('A', 40)} before enabling SIGNMYAPP_PUBLISH_TESTS=1.");
        AssertExecutableIcons(exe);
    }

    static string BuildMetadata(string key) => typeof(AppIconTests).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

    static void AssertExecutableIcons(string exe) => WpfHost.Run(() =>
    {
        Assert.True(File.Exists(exe), $"Application executable missing: {exe}. Build the app project or run the publish script before inspecting its icons.");
        var count = ExtractIconEx(exe, -1, null, null, 0);
        Assert.True(count == 1, $"ExtractIconEx({exe}) returned {count}; Win32 error {Marshal.GetLastWin32Error()}");
        var large = new nint[1];
        var small = new nint[1];
        try
        {
            Assert.True(ExtractIconEx(exe, 0, large, small, 1) > 0);
            Assert.NotEqual(0, large[0]);
            Assert.NotEqual(0, small[0]);
            foreach (var handle in new[] { large[0], small[0] })
            {
                var actual = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                var expected = AppIdentity.LoadWindowIcon().Decoder.Frames.Single(f => f.PixelWidth == actual.PixelWidth);
                Assert.Equal(Pixels(expected), Pixels(actual));
            }
        }
        finally
        {
            if (large[0] != 0) DestroyIcon(large[0]);
            if (small[0] != 0) DestroyIcon(small[0]);
        }
    });

    [Fact]
    public void Explicit_identity_is_registered_with_the_shell_before_any_window() => WpfHost.Run(() =>
    {
        AppIdentity.Register();
        Assert.Equal(0, GetCurrentProcessExplicitAppUserModelID(out var pointer));
        try { Assert.Equal("Klarseal.SignMyApp", Marshal.PtrToStringUni(pointer)); }
        finally { Marshal.FreeCoTaskMem(pointer); }
        // The startup routine must register before its first possible UI (including error UI).
        var source = File.ReadAllText(Path.Combine(WpfHost.ArtifactsDir(), "..", "..", "src", "SignMyApp.App", "App.xaml.cs"));
        Assert.True(source.IndexOf("AppIdentity.Register();", StringComparison.Ordinal) < source.IndexOf("base.OnStartup(e);", StringComparison.Ordinal));
    });

    static byte[] Pixels(BitmapSource bitmap)
    {
        var bgra = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bgra.PixelWidth * bgra.PixelHeight * 4];
        bgra.CopyPixels(pixels, bgra.PixelWidth * 4, 0);
        return pixels;
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    static extern int GetCurrentProcessExplicitAppUserModelID(out nint appId);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint ExtractIconEx(string file, int index, [Out] nint[]? large, [Out] nint[]? small, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(nint icon);
}

public sealed class PublishFactAttribute : FactAttribute
{
    public PublishFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SIGNMYAPP_PUBLISH_TESTS") != "1")
            Skip = "Publish version 0.0.0-icon-test with build/Publish-SignMyApp.ps1, then set SIGNMYAPP_PUBLISH_TESTS=1 to inspect its icons.";
    }
}
