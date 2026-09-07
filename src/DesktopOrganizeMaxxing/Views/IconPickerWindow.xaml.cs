using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DesktopOrganizeMaxxing.Services;

namespace DesktopOrganizeMaxxing.Views;

/// <summary>
/// Dialog allowing users to select an icon from a rich emoji bank or import a custom .ico/image file.
/// </summary>
public partial class IconPickerWindow : Window
{
    private static readonly string[] PresetEmojis =
    [
        // Folders & Office
        "📁", "📂", "🗂️", "💼", "📦", "🏷️", "📌", "💾", "📋", "📚", "📖", "📰",
        // Games & Entertainment
        "🎮", "🕹️", "🎲", "🏆", "🎯", "🧩", "👾", "⚔️", "🏎️", "🚀", "🛸", "🪄",
        // Tech, Tools & Code
        "🛠️", "⚙️", "💻", "🖥️", "🌐", "⚡", "🔧", "🔌", "🔨", "🔒", "🔑", "📡",
        // Media & Creativity
        "🎵", "🎧", "🎬", "🎥", "📷", "🎙️", "🎹", "🎨", "🖌️", "📐", "📝", "📻",
        // General, Work & Lifestyle
        "⭐", "💎", "💡", "🔥", "☕", "📱", "💬", "📧", "🛒", "🧪", "🧭", "🌟",
        "🍕", "🍔", "🚗", "✈️", "⌚", "🏠", "🎪", "🌍", "🔮", "🛎️", "🔋", "💡"
    ];

    public string SelectedEmoji { get; private set; } = "📁";
    public string? SelectedCustomIconPath { get; private set; }

    public IconPickerWindow(string currentEmoji, string? currentCustomIconPath)
    {
        InitializeComponent();

        SelectedEmoji = string.IsNullOrWhiteSpace(currentEmoji) ? "📁" : currentEmoji;
        SelectedCustomIconPath = currentCustomIconPath;

        PopulateEmojiBank();
        UpdatePreview();
    }

    private void PopulateEmojiBank()
    {
        EmojiBankPanel.Children.Clear();

        foreach (var emoji in PresetEmojis)
        {
            var btn = new Border
            {
                Width = 38,
                Height = 38,
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                Cursor = Cursors.Hand,
                ToolTip = emoji
            };

            var text = new TextBlock
            {
                Text = emoji,
                FontSize = 20,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btn.Child = text;

            var chosenEmoji = emoji;
            btn.MouseEnter += (_, _) => btn.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            btn.MouseLeave += (_, _) => btn.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));

            btn.MouseLeftButtonDown += (_, _) =>
            {
                SelectedEmoji = chosenEmoji;
                SelectedCustomIconPath = null;
                UpdatePreview();
            };

            EmojiBankPanel.Children.Add(btn);
        }
    }

    private void UpdatePreview()
    {
        if (!string.IsNullOrEmpty(SelectedCustomIconPath) && File.Exists(SelectedCustomIconPath))
        {
            try
            {
                var iconImg = DesktopScanner.ExtractIcon(SelectedCustomIconPath);
                if (iconImg != null)
                {
                    PreviewCustomImage.Source = iconImg;
                    PreviewCustomImage.Visibility = Visibility.Visible;
                    PreviewEmojiText.Visibility = Visibility.Collapsed;
                    PreviewInfoText.Text = Path.GetFileName(SelectedCustomIconPath);

                    CustomPathText.Text = SelectedCustomIconPath;
                    CustomPathText.Visibility = Visibility.Visible;
                    RemoveCustomBtn.Visibility = Visibility.Visible;
                    return;
                }
            }
            catch { }
        }

        // Fallback to emoji
        PreviewCustomImage.Visibility = Visibility.Collapsed;
        PreviewEmojiText.Text = SelectedEmoji;
        PreviewEmojiText.Visibility = Visibility.Visible;
        PreviewInfoText.Text = $"Emoji: {SelectedEmoji}";

        CustomPathText.Visibility = Visibility.Collapsed;
        RemoveCustomBtn.Visibility = Visibility.Collapsed;
    }

    private void BrowseCustomIcon_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Sélectionner une icône ou une image",
            Filter = "Fichiers d'icônes et images (*.ico;*.png;*.jpg)|*.ico;*.png;*.jpg|Fichiers icône (*.ico)|*.ico|Tous les fichiers (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            SelectedCustomIconPath = dialog.FileName;
            UpdatePreview();
        }
    }

    private void RemoveCustomIcon_Click(object sender, RoutedEventArgs e)
    {
        SelectedCustomIconPath = null;
        UpdatePreview();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
