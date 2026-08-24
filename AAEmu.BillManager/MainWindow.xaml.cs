using System.ComponentModel;

using System.Diagnostics;

using System.IO;

using System.Net.Http;

using System.Text;

using System.Text.Json;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using System.Windows.Threading;

using AAEmu.BillServer.Cash;

namespace AAEmu.BillManager;



public sealed class ProductRow : INotifyPropertyChanged

{

    private byte _mainTab = 1;

    private byte _subTab = 1;



    public uint ShopId { get; set; }

    public string Name { get; set; } = "";

    public byte Available { get; set; }

    public uint Price { get; set; }

    public uint DiscountPrice { get; set; }

    public uint BuyLimit { get; set; }

    public byte LimitType { get; set; }

    public uint ItemId { get; set; }

    public int TabPos { get; set; }

    private bool _isDirty;

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (_isDirty == value)
                return;
            _isDirty = value;
            Notify(nameof(IsDirty));
        }
    }



    public byte MainTab

    {

        get => _mainTab;

        set

        {

            if (_mainTab == value)

                return;

            _mainTab = value;

            Notify(nameof(MainTab));
            Notify(nameof(TabPath));
            Notify(nameof(SubTabName));

        }

    }



    public byte SubTab

    {

        get => _subTab;

        set

        {

            if (_subTab == value)

                return;

            _subTab = value;

            Notify(nameof(SubTab));
            Notify(nameof(TabPath));
            Notify(nameof(SubTabName));

        }

    }



    public string TabPath => IcsTabCatalog.TabPath(MainTab, SubTab);
    public string SubTabName => IcsTabCatalog.SubName(MainTab, SubTab);



    public event PropertyChangedEventHandler? PropertyChanged;



    private void Notify(string name) =>

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

}



public partial class MainWindow : Window

{

    private static readonly JsonSerializerOptions JsonOpts = new()

    {

        PropertyNameCaseInsensitive = true

    };



    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private readonly DispatcherTimer _pollTimer;

    private readonly List<TabChoice> _mainTabChoices;

    private List<ProductRow> _allRows = [];

    private Process? _managedBill;

    private bool _pollInFlight;

    private DataGridComboBoxColumn? _mainTabColumn;

    private DataGridTemplateColumn? _subTabColumn;

    private bool _updatingFilters;



    private string AdminBase => AdminUrlBox.Text.Trim().TrimEnd('/');

    private string BillExe => ExePathBox.Text.Trim();



    public MainWindow()

    {

        InitializeComponent();

        _mainTabChoices = IcsTabCatalog.MainTabs

            .Select(t => new TabChoice { Id = t.Id, Name = t.Name })

            .ToList();



        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        _pollTimer.Tick += async (_, _) => await PollServerStateAsync();

    }



    private void OnLoaded(object sender, RoutedEventArgs e)

    {

        ExePathBox.Text = ResolveDefaultBillExe();
        CompactPathBox.Text = CompactItemNameCatalog.ResolveCompactPath(null) ?? "";
        SetupGridTabColumns();

        InitFilterCombos();

        UpdateStartStopButtons();

        _pollTimer.Start();

        _ = PollServerStateAsync();

    }



    private void SetupGridTabColumns()

    {

        foreach (var col in Grid.Columns)

        {

            if (col is DataGridComboBoxColumn combo && col.Header?.ToString() == "Main tab")

            {

                _mainTabColumn = combo;

                combo.ItemsSource = _mainTabChoices;

            }

            else if (col is DataGridTemplateColumn template && col.Header?.ToString() == "Sub tab")
            {
                _subTabColumn = template;
            }

        }



        Grid.PreparingCellForEdit += OnPreparingCellForEdit;

        Grid.CellEditEnding += OnCellEditEnding;

    }



    private void InitFilterCombos()

    {

        var allMain = new TabChoice { Id = 0, Name = "(All)" };

        FilterMainTab.ItemsSource = new[] { allMain }.Concat(_mainTabChoices).ToList();

        FilterMainTab.SelectedIndex = 0;

        RefreshSubFilter(0);

    }



    private void RefreshSubFilter(byte mainTab)

    {

        _updatingFilters = true;

        try

        {

            var allSub = new TabChoice { Id = 0, Name = "(All)" };

            if (mainTab == 0)

            {

                FilterSubTab.ItemsSource = new[] { allSub };

                FilterSubTab.SelectedIndex = 0;

                FilterSubTab.IsEnabled = false;

                return;

            }



            FilterSubTab.IsEnabled = true;

            var subs = IcsTabCatalog.SubTabsFor(mainTab)

                .Select(t => new TabChoice { Id = t.Id, Name = t.Name })

                .ToList();

            FilterSubTab.ItemsSource = new[] { allSub }.Concat(subs).ToList();

            FilterSubTab.SelectedIndex = 0;

        }

        finally

        {

            _updatingFilters = false;

        }

    }



    private void OnPreparingCellForEdit(object sender, DataGridPreparingCellForEditEventArgs e)

    {

        if (_subTabColumn is null || e.Column != _subTabColumn)

            return;



        if (e.EditingElement is not ComboBox combo)

            return;



        if (e.Row.Item is not ProductRow row)

            return;



        combo.ItemsSource = IcsTabCatalog.SubTabsFor(row.MainTab)

            .Select(t => new TabChoice { Id = t.Id, Name = t.Name })

            .ToList();

        combo.DisplayMemberPath = nameof(TabChoice.Name);

        combo.SelectedValuePath = nameof(TabChoice.Id);

        combo.SelectedValue = row.SubTab;

    }



    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)

    {

        if (e.EditAction != DataGridEditAction.Commit)

            return;



        if (_mainTabColumn is not null && e.Column == _mainTabColumn && e.Row.Item is ProductRow row)

        {

            if (e.EditingElement is ComboBox combo && combo.SelectedValue is byte main)

            {

                row.MainTab = main;

                var validSubs = IcsTabCatalog.SubTabsFor(main).Select(s => s.Id).ToHashSet();

                if (!validSubs.Contains(row.SubTab))

                    row.SubTab = validSubs.Min();

            }

        }

        else if (_subTabColumn is not null && e.Column == _subTabColumn && e.Row.Item is ProductRow rowSub)

        {

            if (e.EditingElement is ComboBox combo && combo.SelectedValue is byte sub)

                rowSub.SubTab = sub;

        }

        if (e.Row.Item is ProductRow edited)
            edited.IsDirty = true;

    }



    private void OnFilterTabChanged(object sender, SelectionChangedEventArgs e)

    {

        if (_updatingFilters)

            return;



        if (sender == FilterMainTab && FilterMainTab.SelectedItem is TabChoice main)

            RefreshSubFilter(main.Id);



        ApplyFilter();

    }



    private void OnClearFilter(object sender, RoutedEventArgs e)

    {

        FilterMainTab.SelectedIndex = 0;

        ApplyFilter();

    }



    private void ApplyFilter()

    {

        var main = FilterMainTab.SelectedItem is TabChoice m ? m.Id : (byte)0;

        var sub = FilterSubTab.SelectedItem is TabChoice s ? s.Id : (byte)0;



        var filtered = _allRows

            .Where(r => (main == 0 || r.MainTab == main) && (sub == 0 || r.SubTab == sub))

            .OrderBy(r => r.MainTab)

            .ThenBy(r => r.SubTab)

            .ThenBy(r => r.TabPos)

            .ThenBy(r => r.ShopId)

            .ToList();



        Grid.ItemsSource = filtered;

        FilterSummary.Text = filtered.Count == _allRows.Count

            ? $"{_allRows.Count} items"

            : $"{filtered.Count} of {_allRows.Count} items";

    }



    private void OnClosed(object? sender, EventArgs e)

    {

        _pollTimer.Stop();

        _http.Dispose();
        _adminClient.Dispose();

    }



    private static string ResolveDefaultBillExe()

    {

        var fromManagerBin = Path.GetFullPath(Path.Combine(

            AppContext.BaseDirectory,

            "..", "..", "..", "..",

            "AAEmu.BillServer", "bin", "Debug", "net10.0", "AAEmu.BillServer.exe"));



        if (File.Exists(fromManagerBin))

            return fromManagerBin;



        var fromRepoRoot = Path.GetFullPath(Path.Combine(

            AppContext.BaseDirectory,

            "..", "..", "..",

            "AAEmu.BillServer", "bin", "Debug", "net10.0", "AAEmu.BillServer.exe"));



        if (File.Exists(fromRepoRoot))

            return fromRepoRoot;



        return fromManagerBin;

    }



    private async Task PollServerStateAsync()

    {

        if (_pollInFlight)

            return;



        _pollInFlight = true;

        try

        {

            var online = await TryGetStatusAsync();

            if (online is null)

            {

                ServerStateText.Text = "Bill Server: offline";

                ServerStateText.Foreground = new SolidColorBrush(Color.FromRgb(0xE5, 0x55, 0x55));

            }

            else

            {

                var managed = _managedBill is { HasExited: false };

                var tag = managed ? "managed" : "external";

                ServerStateText.Text =

                    $"Bill Server: online ({tag}) — {online.Value.productCount} products, {online.Value.availableCount} available";

                ServerStateText.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xD9, 0x64));

            }



            if (_managedBill is { HasExited: true })

            {

                _managedBill.Dispose();

                _managedBill = null;

            }



            UpdateStartStopButtons();

        }

        finally

        {

            _pollInFlight = false;

        }

    }



    private async Task<(int productCount, int availableCount)?> TryGetStatusAsync()

    {

        try

        {

            using var resp = await _http.GetAsync($"{AdminBase}/status");

            if (!resp.IsSuccessStatusCode)

                return null;



            var json = await resp.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);

            var root = doc.RootElement;

            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())

                return null;



            var products = root.TryGetProperty("productCount", out var pc) ? pc.GetInt32() : 0;

            var available = root.TryGetProperty("availableCount", out var ac) ? ac.GetInt32() : 0;

            return (products, available);

        }

        catch

        {

            return null;

        }

    }



    private void UpdateStartStopButtons()

    {

        var managedRunning = _managedBill is { HasExited: false };

        StartBillButton.IsEnabled = !managedRunning;

        StopBillButton.IsEnabled = managedRunning || IsBillProcessRunning();

        RestartBillButton.IsEnabled = StopBillButton.IsEnabled;

    }



    private static bool IsBillProcessRunning()

    {

        try

        {

            return Process.GetProcessesByName("AAEmu.BillServer").Length > 0;

        }

        catch

        {

            return false;

        }

    }



    private async void OnRefresh(object sender, RoutedEventArgs e)

    {

        try

        {

            var status = await _http.GetStringAsync($"{AdminBase}/status");

            StatusLine.Text = status.Replace('\n', ' ');

            var json = await _http.GetStringAsync($"{AdminBase}/catalog");

            _allRows = JsonSerializer.Deserialize<List<ProductRow>>(json, JsonOpts) ?? [];
            foreach (var row in _allRows)
                row.IsDirty = false;
            ApplyDisplayNames();
            ApplyFilter();

            await PollServerStateAsync();

        }

        catch (Exception ex)

        {

            StatusLine.Text = "Refresh failed: " + ex.Message;

        }

    }



    private async void OnSaveSelected(object sender, RoutedEventArgs e)

    {

        if (Grid.SelectedItem is not ProductRow row)

        {

            StatusLine.Text = "Select a product row first.";

            return;

        }



        try

        {

            var payload = JsonSerializer.Serialize(new

            {

                row.Name,

                row.Available,

                row.Price,

                row.DiscountPrice,

                row.BuyLimit,

                row.LimitType,

                row.ItemId,

                row.MainTab,

                row.SubTab,

                row.TabPos

            });

            var content = new StringContent(payload, Encoding.UTF8, "application/json");

            var resp = await _http.PutAsync($"{AdminBase}/catalog/{row.ShopId}", content);

            StatusLine.Text = resp.IsSuccessStatusCode

                ? $"Saved shopId={row.ShopId} → {row.TabPath} price={row.Price}"

                : $"Save failed: {(int)resp.StatusCode}";

            if (resp.IsSuccessStatusCode)
                row.IsDirty = false;

            OnRefresh(sender, e);

        }

        catch (Exception ex)

        {

            StatusLine.Text = "Save failed: " + ex.Message;

        }

    }



    private async void OnFillNames(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = new StringContent("", Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{AdminBase}/catalog/fill-names", content);
            var body = await resp.Content.ReadAsStringAsync();
            StatusLine.Text = resp.IsSuccessStatusCode
                ? "Fill names: " + body
                : $"Fill names failed ({(int)resp.StatusCode}): {body}";
            if (resp.IsSuccessStatusCode)
                OnRefresh(sender, e);
        }
        catch (Exception ex)
        {
            StatusLine.Text = "Fill names failed: " + ex.Message;
        }
    }

    private void ApplyDisplayNames()
    {
        using var catalog = OpenNameCatalog();
        if (!catalog.IsAvailable)
            return;

        foreach (var row in _allRows)
        {
            if (!CompactItemNameCatalog.NeedsResolvedName(row.Name))
                continue;

            var resolved = catalog.ResolveDisplayName(row.Name, row.ItemId);
            if (!string.IsNullOrWhiteSpace(resolved))
                row.Name = resolved;
        }
    }

    private CompactItemNameCatalog OpenNameCatalog()
    {
        var path = CompactPathBox.Text.Trim();
        return string.IsNullOrWhiteSpace(path)
            ? new CompactItemNameCatalog(null)
            : new CompactItemNameCatalog(path);
    }

    private void OnBrowseCompact(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "SQLite compact|compact.sqlite3|All files|*.*",
            FileName = "compact.sqlite3",
            InitialDirectory = Path.GetDirectoryName(CompactPathBox.Text) ?? AppContext.BaseDirectory
        };

        if (dlg.ShowDialog() == true)
            CompactPathBox.Text = dlg.FileName;
    }

    private void OnBrowseExe(object sender, RoutedEventArgs e)

    {

        var dlg = new Microsoft.Win32.OpenFileDialog

        {

            Filter = "Bill Server|AAEmu.BillServer.exe|All files|*.*",

            FileName = "AAEmu.BillServer.exe",

            InitialDirectory = Path.GetDirectoryName(BillExe) ?? AppContext.BaseDirectory

        };



        if (dlg.ShowDialog() == true)

            ExePathBox.Text = dlg.FileName;

    }



    private void OnStartBill(object sender, RoutedEventArgs e)

    {

        if (_managedBill is { HasExited: false })

        {

            StatusLine.Text = "Bill Server already running (managed by this window).";

            return;

        }



        if (IsBillProcessRunning())

        {

            StatusLine.Text = "Bill Server is already running (started outside Bill Manager). Use Stop first if you want to relaunch.";

            UpdateStartStopButtons();

            return;

        }



        if (!File.Exists(BillExe))

        {

            StatusLine.Text = "Bill exe not found: " + BillExe;

            return;

        }



        try

        {

            var workDir = Path.GetDirectoryName(BillExe) ?? AppContext.BaseDirectory;

            var logPath = Path.Combine(workDir, "Logs", "BillManager-launch.log");

            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);



            var psi = new ProcessStartInfo

            {

                FileName = BillExe,

                WorkingDirectory = workDir,

                UseShellExecute = false,

                CreateNoWindow = true,

                RedirectStandardOutput = true,

                RedirectStandardError = true

            };

            var compactPath = CompactPathBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(compactPath))
                psi.Environment["AAEMU_CLIENT_COMPACT"] = compactPath;



            _managedBill = new Process { EnableRaisingEvents = true };

            _managedBill.StartInfo = psi;

            _managedBill.OutputDataReceived += (_, args) => AppendLaunchLog(logPath, args.Data);

            _managedBill.ErrorDataReceived += (_, args) => AppendLaunchLog(logPath, args.Data);

            _managedBill.Exited += (_, _) => Dispatcher.Invoke(async () =>

            {

                StatusLine.Text = "Managed Bill Server exited.";

                UpdateStartStopButtons();

                await PollServerStateAsync();

            });



            if (!_managedBill.Start())

            {

                StatusLine.Text = "Failed to start Bill Server.";

                _managedBill.Dispose();

                _managedBill = null;

                return;

            }



            _managedBill.BeginOutputReadLine();

            _managedBill.BeginErrorReadLine();



            StatusLine.Text = $"Started Bill Server PID {_managedBill.Id} (log: {logPath})";

            UpdateStartStopButtons();

            _ = PollServerStateAsync();

        }

        catch (Exception ex)

        {

            StatusLine.Text = "Start failed: " + ex.Message;

        }

    }



    private static void AppendLaunchLog(string logPath, string? line)

    {

        if (string.IsNullOrEmpty(line))

            return;



        try

        {

            File.AppendAllText(logPath, line + Environment.NewLine);

        }

        catch

        {

            // ignore log write failures

        }

    }



    private async void OnAddCash(object sender, RoutedEventArgs e)
    {
        if (!ulong.TryParse(AccountBox.Text, out var acc) || !int.TryParse(AmountBox.Text, out var amount))
        {
            StatusLine.Text = "Account id and amount required.";
            return;
        }

        try
        {
            var payload = JsonSerializer.Serialize(new { accountId = acc, charId = 0, amount, priceType = 0 });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{AdminBase}/cash/add", content);
            StatusLine.Text = "Cash add: " + await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            StatusLine.Text = "Cash add failed: " + ex.Message;
        }
    }
}
