using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Simple;
using Avalonia.Styling;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;

namespace MovesetForge.Desktop;

internal static class Program
{
    [STAThread]public static void Main(string[] args)
    {
        var app=AppBuilder.Configure<App>().WithInterFont();
        if(args.Contains("--snapshot"))app.UseHeadless(new AvaloniaHeadlessPlatformOptions{UseHeadlessDrawing=false}).UseSkia();else app.UsePlatformDetect();
        app.StartWithClassicDesktopLifetime(args);
    }
}
public sealed class App:Application
{
    public const string DisplayName = "Animation Composer";

    public override void Initialize()
    {
        Name = DisplayName;
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new SimpleTheme());
        Styles.Add(new StyleInclude(new Uri("avares://AnimationComposer/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Simple.xaml")
        });
    }
    public override void OnFrameworkInitializationCompleted()
    {if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d)d.MainWindow=new MainWindow(d.Args??[]);base.OnFrameworkInitializationCompleted();}
}
