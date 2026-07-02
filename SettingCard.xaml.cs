using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace TrueRotate;

/// <summary>
/// A lightweight "settings row" container: a rounded card with a header, an optional
/// description, and a right-aligned action (the <see cref="ActionContent"/>). A native
/// replacement for CommunityToolkit's SettingsCard so the app takes no third-party
/// control-library dependency (which failed to load in the packaged build).
/// </summary>
[ContentProperty(Name = nameof(ActionContent))]
public sealed partial class SettingCard : UserControl
{
    public SettingCard() => this.InitializeComponent();

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingCard),
            new PropertyMetadata(null));

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingCard),
            new PropertyMetadata(null));

    public object ActionContent
    {
        get => GetValue(ActionContentProperty);
        set => SetValue(ActionContentProperty, value);
    }
    public static readonly DependencyProperty ActionContentProperty =
        DependencyProperty.Register(nameof(ActionContent), typeof(object), typeof(SettingCard),
            new PropertyMetadata(null));

    // x:Bind function: collapse the description line when there's no text.
    private Visibility HasText(string? text)
        => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}
