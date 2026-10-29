using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json.Nodes;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using Brushes = System.Windows.Media.Brushes;

namespace KalradyaCoop;

public sealed class CampaignWindow : Window
{
    private readonly AppSettings settings;
    private readonly HostController controller;
    private readonly bool isNew;
    private readonly JsonObject options;
    private readonly TextBox name = new() { MaxLength = 48 };
    private readonly TextBlock error = new() { Foreground = Brushes.Salmon, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock explanation = new() { Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(185,175,152)), TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0,8,0,12) };
    private readonly Dictionary<string, ComboBox> levels = new();
    private readonly Dictionary<string, Button> presets = new();
    private readonly CheckBox aging = new() { Content = "Yaşlanma, doğum ve doğal ölüm" };
    private readonly CheckBox perks = new() { Content = "Klan üyelerinin yeteneklerini otomatik dağıt" };
    private readonly ComboBox battleSize = new() { Foreground = Brushes.Black, MinHeight = 32 };
    public CampaignWindow(AppSettings settings, HostController controller, bool isNew)
    {
        this.settings = settings; this.controller = controller; this.isNew = isNew;
        options = Campaigns.ReadOptions(settings);
        Title = isNew ? "Yeni oyun" : "Zorluk ve oyun ayarları";
        Width = 620; Height = 660; MinWidth = 540; MinHeight = 500;
        MaxHeight = SystemParameters.WorkArea.Height; Height = Math.Min(Height, MaxHeight);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window)); WindowChrome.Install(this);
        var body = new StackPanel { Margin = new Thickness(26) };
        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        body.Children.Add(new TextBlock { Text = isNew ? "Yeni bir macera." : "Nasıl oynamak istiyorsunuz?", Style = (Style)FindResource("Heading"), Margin = new Thickness(0,0,0,10) });
        body.Children.Add(new TextBlock { Text = isNew ? "Eski seferin silinmez. Kayıt seç ile geri dönebilirsin." : "Bu seferdeki herkes için geçerlidir. Bir sonraki açılışta uygulanır.", TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0,0,0,18) });
        if (isNew)
        {
            body.Children.Add(new TextBlock { Text = "Seferin adı", Margin = new Thickness(0,0,0,6) });
            name.Text = "Yeni sefer " + DateTime.Now.ToString("dd.MM HH:mm");
            System.Windows.Automation.AutomationProperties.SetName(name, "Seferin adı");
            body.Children.Add(name);
        }
        body.Children.Add(new TextBlock { Text = "Zorluk", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,18,0,10) });
        var presetRow = new System.Windows.Controls.Primitives.UniformGrid { Rows = 1, Columns = 3 };
        body.Children.Add(presetRow);
        foreach (var pair in new[] { ("VeryEasy", "Çok kolay"), ("Easy", "Kolay"), ("Realistic", "Gerçekçi") })
        {
            var button = new Button { Content = pair.Item2, Margin = new Thickness(0,0,8,0) };
            button.Click += (_, _) => { foreach (var selector in levels.Values) selector.SelectedIndex = Array.IndexOf(Campaigns.Levels, pair.Item1); UpdatePreset(); };
            presets[pair.Item1] = button; presetRow.Children.Add(button);
        }
        body.Children.Add(explanation);
        var details = new StackPanel { Margin = new Thickness(0,10,0,12) };
        body.Children.Add(new Expander { Header = "Ayrıntıları kendim ayarlayacağım", Content = details });
        var difficulty = options["difficulty"] as JsonObject ?? new JsonObject();
        foreach (var field in Campaigns.DifficultyFields)
        {
            var row = new Grid { Margin = new Thickness(0,0,0,8) };
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.Children.Add(new TextBlock { Text = field.Label, VerticalAlignment = VerticalAlignment.Center, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,10,0) });
            var selector = new ComboBox { ItemsSource = new[] { "Çok kolay", "Kolay", "Gerçekçi" }, Foreground = Brushes.Black, MinHeight = 30 };
            selector.SelectedIndex = Math.Max(0, Array.IndexOf(Campaigns.Levels, difficulty[field.Key]?.GetValue<string>() ?? "VeryEasy"));
            System.Windows.Automation.AutomationProperties.SetName(selector, field.Label);
            levels[field.Key] = selector; Grid.SetColumn(selector, 1); row.Children.Add(selector); details.Children.Add(row);
            selector.SelectionChanged += (_, _) => UpdatePreset();
        }
        aging.IsChecked = difficulty["birthAndDeath"]?.GetValue<bool>() ?? true;
        perks.IsChecked = difficulty["autoAllocateClanMemberPerks"]?.GetValue<bool>() ?? false;
        details.Children.Add(aging); details.Children.Add(perks);
        details.Children.Add(new TextBlock { Text = "Aynı anda savaşan asker sayısı", Margin = new Thickness(0,10,0,6), FontSize = 13 });
        var currentSize = options["modOptions"]?["battleSize"]?.GetValue<int>() ?? 1000;
        battleSize.ItemsSource = new[] { 200, 400, 600, 800, 1000, currentSize }.Distinct().Order().ToArray(); battleSize.SelectedItem = currentSize;
        System.Windows.Automation.AutomationProperties.SetName(battleSize, "Savaş büyüklüğü"); details.Children.Add(battleSize);
        error.Margin = new Thickness(0,14,0,10); body.Children.Add(error);
        var actions = new Grid(); actions.ColumnDefinitions.Add(new ColumnDefinition()); actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) }); actions.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = new Button { Content = "Vazgeç", IsCancel = true }; cancel.Click += (_, _) => DialogResult = false;
        var save = new Button { Content = isNew ? "Oluştur ve başlat" : "Ayarları kaydet", Style = (Style)FindResource("Primary"), FontSize = 15, Padding = new Thickness(12), IsDefault = true }; Grid.SetColumn(save, 2); save.Click += Save_Click;
        actions.Children.Add(cancel); actions.Children.Add(save); body.Children.Add(actions);
        UpdatePreset();
    }
    private void UpdatePreset()
    {
        if (levels.Count != Campaigns.DifficultyFields.Length) return;
        var selections = levels.Values.Select(x => x.SelectedIndex).Distinct().ToArray();
        var level = selections.Length == 1 && selections[0] >= 0 ? Campaigns.Levels[selections[0]] : "";
        foreach (var pair in presets)
        {
            pair.Value.Background = (System.Windows.Media.Brush)FindResource(pair.Key == level ? "Gold" : "Panel");
            pair.Value.Foreground = (System.Windows.Media.Brush)FindResource(pair.Key == level ? "Bg" : "Ink");
        }
        explanation.Text = level switch
        {
            "VeryEasy" => "Daha az hasar, daha kolay savaşlar. Kahramanların savaşta ölümü kapalı.",
            "Easy" => "Hasar azaltılır. Oyuncu kahramanları savaşta ölmez; diğer kahramanlar ölebilir.",
            "Realistic" => "Tam hasar, daha zor yapay zekâ. Oyuncular dahil kahramanlar savaşta ölebilir.",
            _ => "Özel seçim. Ayrıntılardaki ayarların korunur."
        };
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var difficulty = options["difficulty"] as JsonObject;
            if (difficulty == null) options["difficulty"] = difficulty = new JsonObject();
            foreach (var field in levels) difficulty[field.Key] = Campaigns.Levels[field.Value.SelectedIndex];
            difficulty["birthAndDeath"] = aging.IsChecked == true; difficulty["autoAllocateClanMemberPerks"] = perks.IsChecked == true;
            var mod = options["modOptions"] as JsonObject;
            if (mod == null) options["modOptions"] = mod = new JsonObject();
            mod["battleSize"] = (int)battleSize.SelectedItem;
            if (isNew) Campaigns.Create(settings, name.Text, options, controller);
            else Campaigns.SaveOptions(settings, options, controller);
            DialogResult = true;
        }
        catch (Exception ex) { error.Text = ex.Message; }
    }
}

public sealed class CampaignPicker : Window
{
    public CampaignSlot? Selected { get; private set; }
    public CampaignPicker(AppSettings settings)
    {
        Title = "Kayıt seç"; Width = 520; Height = 420; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)FindResource(typeof(Window)); WindowChrome.Install(this);
        var body = new StackPanel { Margin = new Thickness(24) }; Content = body;
        body.Children.Add(new TextBlock { Text = "Hangi sefere devam edelim?", Style = (Style)FindResource("Heading"), FontSize = 22, Margin = new Thickness(0,0,0,18) });
        var list = new ListBox { ItemsSource = Campaigns.List(settings), Height = 220, Background = (System.Windows.Media.Brush)FindResource("Panel"), Foreground = (System.Windows.Media.Brush)FindResource("Ink"), FontSize = 16 };
        list.SelectedItem = list.Items.Cast<CampaignSlot>().First(x => x.DataDir.Equals(settings.DataDir, StringComparison.OrdinalIgnoreCase));
        System.Windows.Automation.AutomationProperties.SetName(list, "Seferler"); body.Children.Add(list);
        var select = new Button { Content = "Bu kaydı seç", Margin = new Thickness(0,16,0,0), Style = (Style)FindResource("Primary") };
        select.Click += (_, _) => { if (list.SelectedItem is CampaignSlot slot) { Selected = slot; DialogResult = true; } }; body.Children.Add(select);
    }
}
