using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;

namespace ClasseControle.Console;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        Styles.Add(new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://ClasseControle.Console/"))
        {
            Source = new Uri("avares://ClasseControle.Console/ConsoleStyles.axaml"),
        });
        // Les menus contextuels des vignettes sont plus larges que la limite par défaut.
        Styles.Add(new Avalonia.Styling.Style(x => x.OfType<Avalonia.Controls.FlyoutPresenter>())
        {
            Setters =
            {
                new Avalonia.Styling.Setter(Avalonia.Layout.Layoutable.MaxWidthProperty, 900.0),
                new Avalonia.Styling.Setter(Avalonia.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled),
            },
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
