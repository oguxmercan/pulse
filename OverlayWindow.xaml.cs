using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Pulse.Core;

namespace Pulse;

/// <summary>The click-through, always-on-top overlay. Its look is just a XAML DataTemplate file.</summary>
public partial class OverlayWindow : Window
{
    const int GWL_EXSTYLE = -20;
    const int WS_EX_LAYERED = 0x00080000;
    const int WS_EX_TRANSPARENT = 0x00000020;
    const int WS_EX_TOOLWINDOW = 0x00000080;
    const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    public Metrics Metrics { get; } = new();
    public bool ClickThrough { get; private set; } = true;
    public string ThemeError { get; private set; } = "";

    public OverlayWindow()
    {
        InitializeComponent();
        Host.Content = Metrics;
        ShowActivated = false;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyClickThrough();
    }

    public bool LoadThemeFile(string path)
    {
        try
        {
            return LoadThemeSource(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            ThemeError = e.Message;
            return false;
        }
    }

    public bool LoadThemeSource(string xaml)
    {
        try
        {
            var parsed = XamlReader.Parse(xaml);
            if (parsed is not DataTemplate template) { ThemeError = "Kök öğe <DataTemplate> olmalı."; return false; }
            Host.ContentTemplate = template;
            ThemeError = "";
            return true;
        }
        catch (Exception e)
        {
            ThemeError = e.Message.Length > 300 ? e.Message[..300] : e.Message;
            return false;
        }
    }

    public void SetClickThrough(bool on)
    {
        ClickThrough = on;
        EditFrame.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        ApplyClickThrough();
    }

    void ApplyClickThrough()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        style |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;              // never a taskbar/alt-tab entry
        if (ClickThrough) style |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        else style &= ~(WS_EX_TRANSPARENT | WS_EX_NOACTIVATE);
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (ClickThrough) return;
        DragMove();
    }
}
