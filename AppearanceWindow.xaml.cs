using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Image = System.Windows.Controls.Image;

namespace KalradyaCoop;

public partial class AppearanceWindow : Window
{
    private readonly AppSettings settings;
    private string symbol, color;
    public AppearanceWindow(AppSettings settings)
    {
        InitializeComponent();
        WindowChrome.Install(this);
        MaxHeight = SystemParameters.WorkArea.Height;
        Height = Math.Min(Height, MaxHeight);
        this.settings = settings;
        symbol = Appearance.Symbol(settings.BannerSymbol).Id;
        color = Appearance.Palette(settings.BannerColor).Id;
        foreach (var choice in Appearance.Symbols)
        {
            var body = new StackPanel();
            body.Children.Add(new Image { Source = Appearance.Emblem(choice.Id, "gold"), Width = 30, Height = 38, Margin = new Thickness(0, 0, 0, 7) });
            body.Children.Add(new TextBlock { Text = choice.Name, FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            var button = new Button { Content = body, Tag = choice.Id, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(6, 10, 6, 10) };
            System.Windows.Automation.AutomationProperties.SetName(button, choice.Name);
            button.Click += (_, _) => { symbol = choice.Id; Preview(); };
            SymbolChoices.Children.Add(button);
        }
        foreach (var choice in Appearance.Colors)
        {
            var body = new StackPanel();
            body.Children.Add(new Border { Background = Appearance.Accent(choice.Id), Height = 10, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 0, 0, 8) });
            body.Children.Add(new TextBlock { Text = choice.Name, FontSize = 12, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
            var button = new Button { Content = body, Tag = choice.Id, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10) };
            System.Windows.Automation.AutomationProperties.SetName(button, choice.Name);
            button.Click += (_, _) => { color = choice.Id; Preview(); };
            ColorChoices.Children.Add(button);
        }
        Preview();
    }
    private void Preview()
    {
        PreviewImage.Source = Appearance.Emblem(symbol, color);
        PreviewAction.Background = Appearance.ButtonBrush(color);
        SelectionText.Text = "Seçimin: " + Appearance.Symbol(symbol).Name + " · " + Appearance.Palette(color).Name;
        foreach (var button in SymbolChoices.Children.Cast<Button>().Concat(ColorChoices.Children.Cast<Button>()))
        {
            var selected = (string)button.Tag == symbol || (string)button.Tag == color;
            button.BorderBrush = selected ? Appearance.Accent(color) : (System.Windows.Media.Brush)FindResource("Line");
            button.BorderThickness = new Thickness(selected ? 2 : 1);
            button.Background = selected ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(57, 57, 46)) : (System.Windows.Media.Brush)FindResource("Panel");
        }
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var previousSymbol = settings.BannerSymbol;
        var previousColor = settings.BannerColor;
        try
        {
            settings.BannerSymbol = symbol; settings.BannerColor = color;
            Files.Write(Files.SettingsPath, settings);
            DialogResult = true;
        }
        catch (Exception ex) { settings.BannerSymbol = previousSymbol; settings.BannerColor = previousColor; ErrorText.Text = "Görünüm kaydedilemedi: " + ex.Message; }
    }
}
