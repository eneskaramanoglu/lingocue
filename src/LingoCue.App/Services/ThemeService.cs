using Avalonia;
using Avalonia.Styling;

namespace LingoCue.App.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public interface IThemeService
{
    AppTheme Current { get; }

    void Apply(AppTheme theme);
}

internal sealed class ThemeService : IThemeService
{
    public AppTheme Current { get; private set; } = AppTheme.System;

    public void Apply(AppTheme theme)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }

        Current = theme;
    }
}
