using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MoergoLayerViz.App.Localization;
using MoergoLayerViz.App.Services;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App.Views;

/// <summary>
/// One-shot dialog shown on macOS when the SharpHook global hook fails to
/// register — almost always because Accessibility permission has not been
/// granted. Offers a deep link into the System Settings pane.
/// </summary>
public sealed class AccessibilityPromptWindow : Window
{
    public AccessibilityPromptWindow()
    {
        Title = Loc.Instance["Accessibility_Title"];
        Width = 480;
        Height = 220;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.Parse(AppTheme.BgBaseHex));

        var title = new TextBlock
        {
            Text = Loc.Instance["Accessibility_Title"],
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse(AppTheme.KeyDefaultFillHex)),
            Margin = new Thickness(0, 0, 0, 12),
        };

        var body = new TextBlock
        {
            Text = Loc.Instance["Accessibility_Body"],
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse(AppTheme.TextAccessibilityBodyHex)),
            Margin = new Thickness(0, 0, 0, 20),
        };

        var openBtn = new Button
        {
            Content = Loc.Instance["Accessibility_OpenSettings"],
            Padding = new Thickness(14, 6),
        };
        openBtn.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
                    UseShellExecute = false,
                });
            }
            catch (System.Exception ex)
            {
                DiagnosticLog.Warn("Accessibility", $"Failed to open System Settings: {ex.Message}");
            }
            Close();
        };

        var dismissBtn = new Button
        {
            Content = Loc.Instance["Accessibility_Dismiss"],
            Padding = new Thickness(14, 6),
            Margin = new Thickness(8, 0, 0, 0),
        };
        dismissBtn.Click += (_, _) => Close();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { openBtn, dismissBtn },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children = { title, body, buttons },
        };
    }
}
