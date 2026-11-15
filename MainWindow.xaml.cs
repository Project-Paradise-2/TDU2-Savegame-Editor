using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TDU2SaveEditor.Core;
using TDU2SaveEditor.Services;
using TDU2SaveEditor.ViewModels;

namespace TDU2SaveEditor;

public partial class MainWindow : Window
{
    private SaveGame? _save;
    private List<SaveAsset> _assets = new();
    private bool _loading;
    private readonly ObservableCollection<GarageRow> _garageRows = new();
    private TreeItem? _selectedTreeItem;

    private static readonly int[] StandardThresholds =
        { 0, 125, 300, 525, 825, 1200, 1650, 2175, 2795, 3510, 4320, 5225, 6225, 7355, 8615, 10000 };
    private static readonly int[] DiscoveryThresholds =
        { 0, 125, 300, 525, 825, 1200, 1650, 2175, 2795, 3510, 4320, 5225, 6225, 7355, 8615, 10000, 11500, 13000, 14500 };

    public MainWindow()
    {
        InitializeComponent();
        DgGarage.ItemsSource = _garageRows;
        ColCarName.ItemsSource = App.Cars.Names;
        ColHouse.ItemsSource = App.Houses.Names;

        DgExplorer.Columns.Add(new DataGridTextColumn { Header = "Feld / Index", Binding = new System.Windows.Data.Binding("Name"), Width = 220, IsReadOnly = true });
        DgExplorer.Columns.Add(new DataGridTextColumn { Header = "Typ", Binding = new System.Windows.Data.Binding("TypeName"), Width = 130, IsReadOnly = true });
        DgExplorer.Columns.Add(new DataGridTextColumn { Header = "Wert", Binding = new System.Windows.Data.Binding("ValueText") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        DgStats.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new System.Windows.Data.Binding("Name"), Width = 320, IsReadOnly = true });
        DgStats.Columns.Add(new DataGridTextColumn { Header = "Typ", Binding = new System.Windows.Data.Binding("TypeName"), Width = 120, IsReadOnly = true });
        DgStats.Columns.Add(new DataGridTextColumn { Header = "Wert", Binding = new System.Windows.Data.Binding("ValueText") { Mode = System.Windows.Data.BindingMode.TwoWay }, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });

        SetStatus("status.ready");
        LocalizeTree(this);
        LocalizeColumns(this);
        BuildNav();
    }

    // =====================================================================
    // Section navigation
    // =====================================================================

    private bool _navSyncing;
    private readonly List<int> _navMap = new();

    /// <summary>
    /// Mirrors the TabControl sections into the left-hand navigation list,
    /// honouring the optional filter box. Rebuilt after language switches so the
    /// labels stay in sync.
    /// </summary>
    private void BuildNav()
    {
        string filter = TxtNavFilter?.Text?.Trim() ?? "";
        _navSyncing = true;
        int keep = Tabs.SelectedIndex;
        _navMap.Clear();
        NavSections.Items.Clear();
        int select = -1;
        for (int i = 0; i < Tabs.Items.Count; i++)
        {
            if (Tabs.Items[i] is not TabItem ti) continue;
            string header = ti.Header?.ToString() ?? "";
            if (filter.Length > 0 && header.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            NavSections.Items.Add(header);
            _navMap.Add(i);
            if (i == keep) select = _navMap.Count - 1;
        }
        NavSections.SelectedIndex = select;
        _navSyncing = false;
    }

    private void NavFilter_Changed(object sender, TextChangedEventArgs e) => BuildNav();

    private void NavSections_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_navSyncing) return;
        int i = NavSections.SelectedIndex;
        if (i >= 0 && i < _navMap.Count && _navMap[i] != Tabs.SelectedIndex)
            Tabs.SelectedIndex = _navMap[i];
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navSyncing) return;
        int idx = _navMap.IndexOf(Tabs.SelectedIndex);
        if (idx >= 0 && idx != NavSections.SelectedIndex)
        {
            _navSyncing = true;
            NavSections.SelectedIndex = idx;
            _navSyncing = false;
        }
    }

    // =====================================================================
    // Loading
    // =====================================================================

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "TDU2 Savegame-Ordner wählen (Profil oder PLAYERSAVE)" };
        string? def = SaveGameService.FindDefaultSavegameDir();
        if (def != null) dlg.InitialDirectory = def;
        if (dlg.ShowDialog() == true)
            LoadFrom(dlg.FolderName);
    }

    private void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "TDU2 DATA-Datei wählen",
            Filter = "TDU2 Save (DATA)|DATA|Alle Dateien (*.*)|*.*"
        };
        string? def = SaveGameService.FindDefaultSavegameDir();
        if (def != null) dlg.InitialDirectory = def;
        if (dlg.ShowDialog() == true)
            LoadFrom(dlg.FileName);
    }

    private void LoadFrom(string path)
    {
        try
        {
            _loading = true;
            _save = SaveGameService.Load(path);
            ProfileBox.Text = _save.ProfileName;
            SaveDirText.Text = _save.PlayerSaveDir;
            PopulateAll();
            SetStatus(Loc.F("status.loaded", _save.ProfileName));
            ProfileStatus.Text = $"Profil: {_save.ProfileName}  |  {_save.PlayerSaveDir}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Savegame konnte nicht geladen werden:\n\n" + ex.Message,
                "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("status.load_failed");
        }
        finally
        {
            _loading = false;
        }
    }

    private void ReloadProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) { OpenFolder_Click(sender, e); return; }
        try
        {
            _save = SaveGameService.Load(_save.PlayerSaveDir, ProfileBox.Text.Trim());
            _loading = true;
            PopulateAll();
            _loading = false;
            SetStatus("status.reloaded");
        }
        catch (Exception ex)
        {
            _loading = false;
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PopulateAll()
    {
        PopulateDriver();
        PopulateLevels();
        PopulateGarage();
        PopulateStats();
        PopulateExplorer();
        PopulateFiles();
        PopulateSections();
        PopulateAssets();
        UpdateProgressSummary();
    }

    // =====================================================================
    // Generic section tabs
    // =====================================================================

    private void PopulateSections()
    {
        var r = _save?.DataRoot;
        if (r == null)
        {
            foreach (var ed in new[]
            {
                SecDriver, SecHouse, SecLicenses, SecEvents, SecWrecks, SecPhotos, SecSpots,
                SecCasino, SecAvatar, SecStickers, SecChase, SecMyLife, SecPhone, SecRewards,
                SecNews, SecPlayers, SecMisc
            })
                ed.Clear();
            return;
        }

        SecDriver.Load(MarkChanged, (Loc.T("sec.driver"), r["Driver"]));
        SecHouse.Load(MarkChanged, (Loc.T("sec.house"), r["MyHouse"]));
        SecLicenses.Load(MarkChanged, (Loc.T("sec.licenses"), r["PlayerLicenses"]));
        SecEvents.Load(MarkChanged, (Loc.T("sec.events"), r["EventsSave"]));
        SecWrecks.Load(MarkChanged, (Loc.T("sec.wrecks"), r["Wrecks"]));
        SecPhotos.Load(MarkChanged, (Loc.T("sec.photos"), r["PhotosGP"]));
        SecSpots.Load(MarkChanged,
            (Loc.T("sec.myspots"), r["MySpots"]),
            (Loc.T("sec.bookmarks"), r["Bookmarks"]),
            (Loc.T("sec.knownspots"), r["KnownSpots"]));
        SecCasino.Load(MarkChanged, (Loc.T("sec.casino"), r["Casino"]));
        SecAvatar.Load(MarkChanged, (Loc.T("sec.avatar"), r["AvatarSave"]));
        SecStickers.Load(MarkChanged, (Loc.T("sec.stickers"), r["Stickers"]));
        SecChase.Load(MarkChanged, (Loc.T("sec.chase"), r["Chase"]));
        SecMyLife.Load(MarkChanged, (Loc.T("sec.mylife"), r["MyLife"]));
        SecPhone.Load(MarkChanged, (Loc.T("sec.phone"), r["SCPhoneCalls"]));
        SecRewards.Load(MarkChanged, (Loc.T("sec.rewards"), r["Rewards"]));
        SecNews.Load(MarkChanged,
            (Loc.T("sec.news"), r["NewsList"]),
            (Loc.T("sec.radio"), r["Radio"]));
        SecPlayers.Load(MarkChanged,
            (Loc.T("sec.players"), r["PlayersList"]),
            (Loc.T("sec.ranking"), r["Ranking"]));

        var scalars = Scalars(r, "FileVersion", "ScreenshotCounter", "SaveServerIndex", "soisson");
        SecMisc.Load(MarkChanged,
            (Loc.T("sec.scalars"), scalars),
            ("RoadLock", r["RoadLock"]),
            ("CEnvironment", r["CEnvironment"]),
            ("ClassicCarStock", r["ClassicCarStock"]),
            ("Achievements", r["AchievementsData"]),
            ("GServiceTutorial", r["GServiceTutorial"]),
            ("FirstIntros", r["FirstIntros"]),
            ("HelpSystem", r["HelpSystem"]),
            ("FrogCoopObjectives", r["FrogCoopObjectives"]),
            ("CurrentObjective", r["CurrentObjective"]),
            ("StolenCarReceiver", r["StolenCarReceiver"]),
            ("Extends", r["Extends"]));
    }

    private static XObject? Scalars(XObject root, params string[] keys)
    {
        var o = new XObject();
        foreach (var k in keys)
            if (root[k] is XValue v)
                o[k] = v;
        return o.Items.Any() ? o : null;
    }

    // =====================================================================
    // Overview / driver
    // =====================================================================

    private XObject? Driver => _save?.DataRoot["Driver"] as XObject;

    private void PopulateDriver()
    {
        var d = Driver;
        if (d == null) return;
        TxtDriverName.Text = Editing.GetString(d, "Name");
        TxtMoney.Text = Editing.GetLong(d, "Money").ToString(CultureInfo.InvariantCulture);
        TxtCoupons.Text = Editing.GetLong(d, "NbCoupon").ToString(CultureInfo.InvariantCulture);
        TxtOdometer.Text = ((d["OdometerKms"] as XPrim)?.AsDouble() ?? 0).ToString("0.###", CultureInfo.InvariantCulture);
        TxtCurrentCar.Text = Editing.GetLong(d, "CurrentCar").ToString(CultureInfo.InvariantCulture);
        TxtLevel.Text = ComputeGlobalLevel().ToString(CultureInfo.InvariantCulture);
        TxtCurrentHouse.Text = App.Houses.NameOfHouse(Editing.GetULong(d, "CurrentHouse"));
        TxtCurrentIsland.Text = IslandName(Editing.GetLong(d, "CurrentIsland"));
        TxtCasinoPoints.Text = TxtCoupons.Text;
    }

    private static string IslandName(long island) => island switch
    {
        0 => "Ibiza",
        1 => "Hawaii",
        2 => "Casino",
        _ => island.ToString(CultureInfo.InvariantCulture)
    };

    private void ApplyDriver_Click(object sender, RoutedEventArgs e)
    {
        var d = Driver;
        if (d == null) return;
        if (TryLong(TxtMoney.Text, out var money)) Editing.SetLong(d, "Money", money);
        if (TryLong(TxtCoupons.Text, out var cp)) Editing.SetLong(d, "NbCoupon", cp);
        if (TryLong(TxtCurrentCar.Text, out var cc)) Editing.SetLong(d, "CurrentCar", cc);
        if (TryFloat(TxtOdometer.Text, out var odo) && d["OdometerKms"] is XPrim op) op.Value = odo;
        PopulateDriver();
        SetStatus("status.driver_applied");
    }

    private void MaxMoney_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.SetMoney(_save.DataRoot, 2_147_483_647);
        PopulateDriver();
        SetStatus("status.money_max");
    }

    private void MaxCoupons_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.SetCoupons(_save.DataRoot, 2_147_483_647);
        PopulateDriver();
        SetStatus("status.coupons_max");
    }

    private void MaxCasino_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.MaxCasino(_save.DataRoot);
        SetStatus("status.casino_max");
    }

    private void MaxLevels_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.MaxLevels(_save.DataRoot);
        PopulateLevels();
        PopulateDriver();
        PopulateGarage();
        PopulateStats();
        UpdateProgressSummary();
        SetStatus("status.levels_max");
    }

    private void UnlockRoads_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.UnlockRoads(_save.DataRoot);
        PopulateLevels();
        PopulateDriver();
        SetStatus("status.roads_unlocked");
    }

    private void UnlockLicenses_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.UnlockLicenses(_save.DataRoot);
        UpdateProgressSummary();
        SetStatus("status.licenses_unlocked");
    }

    private void UnlockContests_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.MaxContests(_save.DataRoot);
        UpdateProgressSummary();
        SetStatus("status.cups_unlocked");
    }

    private void UnlockEvents_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.UnlockEvents(_save.DataRoot);
        UpdateProgressSummary();
        SetStatus("status.events_unlocked");
    }

    private void UnlockWrecks_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.UnlockWrecks(_save.DataRoot);
        UpdateProgressSummary();
        SetStatus("status.wrecks_unlocked");
    }

    private void UnlockPhotos_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.UnlockPhotos(_save.DataRoot);
        UpdateProgressSummary();
        SetStatus("status.photos_unlocked");
    }

    private void CleanCars_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) return;
        Editing.CleanCars(_save.DataRoot);
        SetStatus("status.cars_cleaned");
    }

    private void ApplyCasino_Click(object sender, RoutedEventArgs e)
    {
        var d = Driver;
        if (d == null) return;
        if (TryLong(TxtCasinoPoints.Text, out var cp)) Editing.SetLong(d, "NbCoupon", cp);
        PopulateDriver();
        SetStatus("status.casino_applied");
    }

    // =====================================================================
    // Levels
    // =====================================================================

    private XObject? Levels => _save?.DataRoot["PlayerLevels"] as XObject;

    private void PopulateLevels()
    {
        var lvl = Levels;
        if (lvl == null) return;
        TxtRacingLevel.Text = Editing.GetLong(lvl, "RacingLevel").ToString(CultureInfo.InvariantCulture);
        TxtCollectionLevel.Text = Editing.GetLong(lvl, "CollectionLevel").ToString(CultureInfo.InvariantCulture);
        TxtSocialLevel.Text = Editing.GetLong(lvl, "SocialLevel").ToString(CultureInfo.InvariantCulture);

        if (lvl["CruisingLevels"] is XObject areas)
        {
            TxtArea0.Text = Editing.GetLong(areas, "Area_0").ToString(CultureInfo.InvariantCulture);
            TxtArea1.Text = Editing.GetLong(areas, "Area_1").ToString(CultureInfo.InvariantCulture);
            TxtArea2.Text = Editing.GetLong(areas, "Area_2").ToString(CultureInfo.InvariantCulture);
            TxtArea3.Text = Editing.GetLong(areas, "Area_3").ToString(CultureInfo.InvariantCulture);
            TxtArea4.Text = Editing.GetLong(areas, "Area_4").ToString(CultureInfo.InvariantCulture);
            TxtArea5.Text = Editing.GetLong(areas, "Area_5").ToString(CultureInfo.InvariantCulture);
        }

        long total = Editing.GetLong(lvl, "RacingLevel") + Editing.GetLong(lvl, "CollectionLevel") + Editing.GetLong(lvl, "SocialLevel")
                     + (lvl["CruisingLevels"] is XObject a2 ? Editing.GetLong(a2, "Area_0") + Editing.GetLong(a2, "Area_1") + Editing.GetLong(a2, "Area_2")
                        + Editing.GetLong(a2, "Area_3") + Editing.GetLong(a2, "Area_4") + Editing.GetLong(a2, "Area_5") : 0);
        TxtTotalPoints.Text = total.ToString(CultureInfo.InvariantCulture);
        TxtLevelCalc.Text = ComputeGlobalLevel().ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyLevels_Click(object sender, RoutedEventArgs e)
    {
        var lvl = Levels;
        if (lvl == null) return;
        if (TryLong(TxtRacingLevel.Text, out var r)) Editing.SetLong(lvl, "RacingLevel", r);
        if (TryLong(TxtCollectionLevel.Text, out var c)) Editing.SetLong(lvl, "CollectionLevel", c);
        if (TryLong(TxtSocialLevel.Text, out var s)) Editing.SetLong(lvl, "SocialLevel", s);
        if (lvl["CruisingLevels"] is XObject areas)
        {
            if (TryLong(TxtArea0.Text, out var a0)) Editing.SetLong(areas, "Area_0", a0);
            if (TryLong(TxtArea1.Text, out var a1)) Editing.SetLong(areas, "Area_1", a1);
            if (TryLong(TxtArea2.Text, out var a2)) Editing.SetLong(areas, "Area_2", a2);
            if (TryLong(TxtArea3.Text, out var a3)) Editing.SetLong(areas, "Area_3", a3);
            if (TryLong(TxtArea4.Text, out var a4)) Editing.SetLong(areas, "Area_4", a4);
            if (TryLong(TxtArea5.Text, out var a5)) Editing.SetLong(areas, "Area_5", a5);
        }
        PopulateLevels();
        PopulateDriver();
        SetStatus("status.levels_applied");
    }

    private int ComputeGlobalLevel()
    {
        var lvl = Levels;
        if (lvl == null) return 0;
        int r = PointsToLevel(Editing.GetLong(lvl, "RacingLevel"), false);
        int c = PointsToLevel(Editing.GetLong(lvl, "CollectionLevel"), false);
        int s = PointsToLevel(Editing.GetLong(lvl, "SocialLevel"), false);
        long disc = 0;
        if (lvl["CruisingLevels"] is XObject areas)
            disc = Editing.GetLong(areas, "Area_0") + Editing.GetLong(areas, "Area_1") + Editing.GetLong(areas, "Area_2")
                   + Editing.GetLong(areas, "Area_3") + Editing.GetLong(areas, "Area_4") + Editing.GetLong(areas, "Area_5");
        int d = PointsToLevel(disc, true);
        return Math.Clamp(r + c + s + d, 1, 73);
    }

    private static int PointsToLevel(long pts, bool discovery)
    {
        var table = discovery ? DiscoveryThresholds : StandardThresholds;
        int level = 0;
        for (int i = 0; i < table.Length; i++)
            if (pts >= table[i]) level = i;
        return level;
    }

    // =====================================================================
    // Garage
    // =====================================================================

    private void PopulateGarage()
    {
        _garageRows.Clear();
        if (_save?.DataRoot["Garage"] is XArray garage)
        {
            for (int i = 0; i < garage.Items.Count; i++)
                if (garage.Items[i] is XObject entry)
                    _garageRows.Add(new GarageRow(i, entry, MarkChanged));
        }
    }

    private void SwapCar_Click(object sender, RoutedEventArgs e)
    {
        if (DgGarage.SelectedItem is not GarageRow row)
        {
            SetStatus("status.pick_car");
            return;
        }
        var picker = new CarPickerWindow { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedCar != null)
        {
            row.Archetype = picker.SelectedCar.Id;
            SetStatus(Loc.F("status.car_swapped", picker.SelectedCar.Name));
        }
    }

    private void TuneMax_Click(object sender, RoutedEventArgs e)
    {
        if (DgGarage.SelectedItem is not GarageRow row) { SetStatus("status.pick_car"); return; }
        row.Acc = 4; row.Top = 4; row.Brk = 4;
        SetStatus("status.car_tuned");
    }

    private void CleanOne_Click(object sender, RoutedEventArgs e)
    {
        if (DgGarage.SelectedItem is not GarageRow row) { SetStatus("status.pick_car"); return; }
        if (row.Car["Car Dirt"] is XObject dirt)
        {
            if (dirt["Dirt Data"] is XArray data)
                for (int i = 0; i < data.Items.Count; i++)
                    if (data.Items[i] is XPrim p) p.Value = p.Type == XType.Float ? 0f : (p.Type == XType.Double ? 0.0 : (object)0L);
            Editing.SetFloat(dirt, "CarWashCoating", 0f);
            foreach (var k in new[] { "BodyDirt", "GlassDirt", "RimsDirt", "BodyScratches" })
                if (dirt[k] is XPrim p2) p2.Value = p2.Type == XType.Double ? 0.0 : (object)0f;
        }
        SetStatus("status.car_cleaned");
    }

    private void DeleteCar_Click(object sender, RoutedEventArgs e)
    {
        if (DgGarage.SelectedItem is not GarageRow row) { SetStatus("status.pick_car"); return; }
        if (_save?.DataRoot["Garage"] is not XArray garage) return;

        var result = MessageBox.Show(this,
            $"Fahrzeug wirklich aus der Garage entfernen?\n\n{row.CarName}\n(Slot {row.Slot})\n\n" +
            "Der Garage-Platz wird geleert (das Auto gilt als verkauft/entfernt). " +
            "Die Anzahl der Stellplätze und deren Indizes bleiben erhalten. " +
            "Beim Speichern wird automatisch ein Backup erstellt.",
            "Auto löschen", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        Editing.DeleteCar(_save.DataRoot, garage, row.Slot);
        PopulateGarage();
        SetStatus(Loc.F("status.car_removed", row.Slot));
    }

    // =====================================================================
    // Statistics
    // =====================================================================

    private void PopulateStats()
    {
        _statRowsAsField.Clear();
        if (_save?.DataRoot["StatisticsData"] is XObject stats)
            foreach (var kv in stats.Items)
                _statRowsAsField.Add(new FieldRow(kv.Key, kv.Value, MarkChanged));
        ApplyStatsFilter();
    }

    private readonly List<FieldRow> _statRowsAsField = new();

    private void StatsFilter_Changed(object sender, TextChangedEventArgs e) => ApplyStatsFilter();

    private void ApplyStatsFilter()
    {
        string q = TxtStatsFilter.Text.Trim();
        IEnumerable<FieldRow> rows = _statRowsAsField;
        if (!string.IsNullOrEmpty(q))
            rows = rows.Where(r => r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        DgStats.ItemsSource = rows.ToList();
    }

    // =====================================================================
    // Explorer
    // =====================================================================

    private void PopulateExplorer()
    {
        _loading = true;
        CmbFile.Items.Clear();
        if (_save != null)
            foreach (var name in SaveGameService.KnownFiles)
                if (_save.Files.ContainsKey(name))
                    CmbFile.Items.Add(name);
        if (CmbFile.Items.Count > 0)
            CmbFile.SelectedIndex = 0;
        _loading = false;
        BuildExplorerTree();
    }

    private void CmbFile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        BuildExplorerTree();
    }

    private XmbfDocument? CurrentDoc
    {
        get
        {
            if (_save == null || CmbFile.SelectedItem is not string name) return null;
            return _save.Files.TryGetValue(name, out var f) ? f.Doc : null;
        }
    }

    private void BuildExplorerTree()
    {
        var doc = CurrentDoc;
        if (doc == null) { TreeExplorer.ItemsSource = null; DgExplorer.ItemsSource = null; return; }
        var root = new TreeItem(doc.RootName, doc.Root) { IsExpanded = true };
        TreeExplorer.ItemsSource = new ObservableCollection<TreeItem> { root };
    }

    private void TreeExplorer_Selected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedTreeItem = e.NewValue as TreeItem;
        ShowNode(_selectedTreeItem);
    }

    private void ShowNode(TreeItem? item)
    {
        if (item == null) { DgExplorer.ItemsSource = null; TxtNodePath.Text = ""; return; }
        TxtNodePath.Text = item.Name + "  " + item.Node.TypeName;
        switch (item.Node)
        {
            case XObject obj:
                DgExplorer.ItemsSource = obj.Items
                    .Select(kv => new FieldRow(kv.Key, kv.Value, MarkChanged)).ToList();
                break;
            case XArray arr:
                DgExplorer.ItemsSource = arr.Items
                    .Select((v, i) => new ItemRow(i, v, MarkChanged)).ToList();
                break;
            default:
                DgExplorer.ItemsSource = null;
                break;
        }
    }

    private void ArrayAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTreeItem?.Node is not XArray arr)
        {
            SetStatus("status.select_array");
            return;
        }
        XValue newItem = arr.ElementType switch
        {
            XType.Object => new XObject(),
            XType.String => new XPrim(XType.String, ""),
            XType.Float => new XPrim(XType.Float, 0f),
            XType.Double => new XPrim(XType.Double, 0.0),
            XType.Bool => new XPrim(XType.Bool, false),
            XType.UInt8 or XType.UInt16 or XType.UInt32 or XType.UInt64 => new XPrim(arr.ElementType, 0UL),
            _ => new XPrim(arr.ElementType, 0L)
        };
        arr.Items.Add(newItem);
        ShowNode(_selectedTreeItem);
        SetStatus("status.entry_added");
    }

    private void ArrayDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedTreeItem?.Node is not XArray arr)
        {
            SetStatus("status.select_array");
            return;
        }
        int index = -1;
        if (DgExplorer.SelectedItem is ItemRow ir) index = ir.Index;
        else if (DgExplorer.SelectedItem is FieldRow fr)
        {
            var match = arr.Items.FirstOrDefault(v => ReferenceEquals(v, fr.Node));
            if (match != null) index = arr.Items.IndexOf(match);
        }
        if (index < 0 || index >= arr.Items.Count) { SetStatus("status.pick_car"); return; }

        var result = MessageBox.Show(this, $"Array-Eintrag [{index}] wirklich löschen?", "Löschen",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        arr.Items.RemoveAt(index);
        ShowNode(_selectedTreeItem);
        SetStatus("status.entry_deleted");
    }

    // =====================================================================
    // Files / backups
    // =====================================================================

    private void PopulateFiles()
    {
        LstFiles.Items.Clear();
        if (_save == null) return;
        foreach (var kv in _save.Files)
            LstFiles.Items.Add(kv.Key);
        if (LstFiles.Items.Count > 0) LstFiles.SelectedIndex = 0;
    }

    private void FileList_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_save == null || LstFiles.SelectedItem is not string name || !_save.Files.TryGetValue(name, out var entry))
        {
            TxtFileInfo.Text = "";
            return;
        }
        var doc = entry.Doc;
        TxtFileInfo.Text =
            $"Datei: {entry.Name}\n" +
            $"Pfad: {entry.Path}\n" +
            $"Verschlüsselte Größe: {entry.OriginalEncrypted.Length:N0} Bytes\n" +
            $"Footer-Länge: {entry.Footer.Length} Bytes\n" +
            $"XMBF-Größe: {doc.Template.Length:N0} Bytes\n" +
            $"Root-Name: {doc.RootName}\n" +
            $"Top-Level-Felder: {doc.Root.Count}\n";
    }

    // =====================================================================
    // Image gallery (PHOTOS / LICENCES / AVATAR sidecar files)
    // =====================================================================

    private void PopulateAssets()
    {
        _assets = new List<SaveAsset>();
        CmbAssetCategory.Items.Clear();
        LstAssets.Items.Clear();
        ImgAsset.Source = null;
        TxtAssetInfo.Text = "";
        if (_save == null) return;

        _assets = AssetsService.Scan(_save.PlayerSaveDir, _save.ProfileName);

        var categories = _assets.Select(a => a.Category).Distinct().ToList();
        if (categories.Count == 0)
        {
            CmbAssetCategory.Items.Add(new ComboBoxItem { Content = Loc.T("assets.none"), IsEnabled = false });
            CmbAssetCategory.SelectedIndex = 0;
            return;
        }

        foreach (var cat in categories)
            CmbAssetCategory.Items.Add(new ComboBoxItem { Content = Loc.T("cat." + cat), Tag = cat });
        CmbAssetCategory.SelectedIndex = 0; // triggers AssetCategory_Changed
    }

    private void AssetCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        LstAssets.Items.Clear();
        ImgAsset.Source = null;
        TxtAssetInfo.Text = "";
        if (CmbAssetCategory.SelectedItem is not ComboBoxItem item || item.Tag is not string cat)
            return;

        foreach (var asset in _assets.Where(a => a.Category == cat))
            LstAssets.Items.Add(asset);

        if (LstAssets.Items.Count > 0)
            LstAssets.SelectedIndex = 0;
    }

    private void AssetList_Changed(object sender, SelectionChangedEventArgs e)
    {
        ImgAsset.Source = null;
        if (LstAssets.SelectedItem is not SaveAsset asset)
        {
            TxtAssetInfo.Text = "";
            return;
        }

        TxtAssetInfo.Text = asset.Display;

        if (asset.Category == "STICKERS")
        {
            var sticker = asset.TryDecodeSticker();
            if (sticker == null)
            {
                TxtAssetInfo.Text += "  -  " + Loc.T("status.load_failed");
                return;
            }
            TxtAssetInfo.Text += $"  -  {sticker.Width}x{sticker.Height} {sticker.Format}, {sticker.MipCount} Mip";
            ImgAsset.Source = ToBitmap(sticker);
            return;
        }

        byte[]? bytes = asset.TryDecode(out bool isImage);
        if (bytes == null)
        {
            TxtAssetInfo.Text += "  -  " + Loc.T("status.load_failed");
            return;
        }

        if (!isImage)
        {
            TxtAssetInfo.Text += "  -  " + Loc.T("assets.no_preview");
            return;
        }

        try
        {
            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            ImgAsset.Source = bmp;
        }
        catch (Exception ex)
        {
            TxtAssetInfo.Text += "  -  " + ex.Message;
        }
    }

    private void ExportAsset_Click(object sender, RoutedEventArgs e)
    {
        if (LstAssets.SelectedItem is not SaveAsset asset) { SetStatus("status.no_asset"); return; }

        if (asset.Category == "STICKERS")
        {
            ExportSticker(asset);
            return;
        }

        byte[]? bytes = asset.TryDecode(out _);
        if (bytes == null) { SetStatus("status.load_failed"); return; }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = System.IO.Path.GetFileNameWithoutExtension(asset.Name) + ".jpg",
            Filter = "JPEG (*.jpg)|*.jpg|Alle Dateien (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            System.IO.File.WriteAllBytes(dlg.FileName, bytes);
            SetStatus(Loc.F("status.asset_exported", System.IO.Path.GetFileName(dlg.FileName)));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportSticker(SaveAsset? asset)
    {
        if (asset == null) { SetStatus("status.no_asset"); return; }
        var sticker = asset.TryDecodeSticker();
        if (sticker == null) { SetStatus("status.not_sticker"); return; }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = System.IO.Path.GetFileNameWithoutExtension(asset.Name) + ".png",
            Filter = "PNG (*.png)|*.png|Alle Dateien (*.*)|*.*"
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            var bmp = ToBitmap(sticker);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using (var fs = System.IO.File.Create(dlg.FileName))
                enc.Save(fs);
            SetStatus(Loc.F("status.sticker_exported", System.IO.Path.GetFileName(dlg.FileName)));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static readonly string ImageOpenFilter =
        "Bilddateien|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp|" +
        "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|Bitmap (*.bmp)|*.bmp|Alle Dateien (*.*)|*.*";

    private void ImportAsset_Click(object sender, RoutedEventArgs e)
    {
        if (LstAssets.SelectedItem is not SaveAsset asset) { SetStatus("status.no_asset"); return; }

        if (asset.Category == "STICKERS")
            ImportSticker(asset);
        else
            ImportJpegAsset(asset);
    }

    private void ImportSticker(SaveAsset asset)
    {
        var sticker = asset.TryDecodeSticker();
        if (sticker == null || asset.WorkingProfile == null || asset.Footer == null || asset.ContainerFileName == null)
        {
            SetStatus("status.load_failed");
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = ImageOpenFilter };
        if (dlg.ShowDialog(this) != true) return;

        var confirm = MessageBox.Show(this,
            Loc.F("confirm.sticker_import", System.IO.Path.GetFileName(asset.FilePath)),
            Loc.T("confirm.title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            byte[] bgra = LoadImageAsBgra(dlg.FileName, sticker.Width, sticker.Height);
            byte[] payload = Sticker2Db.EncodeDxt5(bgra, sticker.Width, sticker.Height, sticker.MipCount);
            byte[] db = Sticker2Db.BuildSticker(sticker.Header, sticker.Width, sticker.Height, sticker.MipCount, payload);
            byte[] encrypted = SaveCrypto.Encrypt(db, asset.WorkingProfile, asset.ContainerFileName, asset.Footer, asset.ContainerName);

            System.IO.File.Copy(asset.FilePath, asset.FilePath + ".bak", overwrite: true);
            System.IO.File.WriteAllBytes(asset.FilePath, encrypted);

            SetStatus(Loc.F("status.sticker_imported", System.IO.Path.GetFileName(asset.FilePath)));
            PopulateAssets();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportJpegAsset(SaveAsset asset)
    {
        if (asset.WorkingProfile == null || asset.Footer == null || asset.ContainerFileName == null)
        {
            SetStatus("status.load_failed");
            return;
        }

        byte[]? original = asset.TryDecode(out bool isImage);
        if (original == null || !isImage)
        {
            SetStatus("status.load_failed");
            return;
        }

        int tw, th;
        try { (tw, th) = ImageDimensions(original); }
        catch { tw = th = 256; }
        if (tw <= 0 || th <= 0) { tw = th = 256; }

        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = ImageOpenFilter };
        if (dlg.ShowDialog(this) != true) return;

        var confirm = MessageBox.Show(this,
            Loc.F("confirm.image_import", System.IO.Path.GetFileName(asset.FilePath), tw, th),
            Loc.T("confirm.title"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            BitmapSource img = LoadImageFit(dlg.FileName, tw, th);
            byte[] jpeg = EncodeJpegBaseline(img, 92);
            byte[] encrypted = SaveCrypto.Encrypt(jpeg, asset.WorkingProfile, asset.ContainerFileName, asset.Footer, asset.ContainerName);

            System.IO.File.Copy(asset.FilePath, asset.FilePath + ".bak", overwrite: true);
            System.IO.File.WriteAllBytes(asset.FilePath, encrypted);

            SetStatus(Loc.F("status.image_imported", System.IO.Path.GetFileName(asset.FilePath), tw, th));
            PopulateAssets();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static (int width, int height) ImageDimensions(byte[] imageBytes)
    {
        using var ms = new System.IO.MemoryStream(imageBytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return (bmp.PixelWidth, bmp.PixelHeight);
    }

    // Loads any supported image, scales it to fit the target box without
    // distortion (letterboxed on white) and returns Bgr24 pixels for a
    // baseline JPEG (no alpha channel, so the game decoder always accepts it).
    private static BitmapSource LoadImageFit(string path, int targetWidth, int targetHeight)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        bi.Freeze();

        double scale = Math.Min((double)targetWidth / bi.PixelWidth, (double)targetHeight / bi.PixelHeight);
        double w = bi.PixelWidth * scale;
        double h = bi.PixelHeight * scale;
        double x = (targetWidth - w) / 2.0;
        double y = (targetHeight - h) / 2.0;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, targetWidth, targetHeight));
            dc.DrawImage(bi, new Rect(x, y, w, h));
        }

        var rtb = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();

        var conv = new FormatConvertedBitmap(rtb, PixelFormats.Bgr24, null, 0);
        conv.Freeze();
        return conv;
    }

    private static byte[] EncodeJpegBaseline(BitmapSource image, int quality)
    {
        var enc = new JpegBitmapEncoder { QualityLevel = quality };
        enc.Frames.Add(BitmapFrame.Create(image));
        using var ms = new System.IO.MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    private static BitmapSource ToBitmap(StickerImage s)
    {
        var bmp = BitmapSource.Create(s.Width, s.Height, 96, 96, PixelFormats.Bgra32, null, s.Bgra, s.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    private static byte[] LoadImageAsBgra(string path, int targetWidth, int targetHeight)
    {
        var bi = new BitmapImage();
        bi.BeginInit();
        bi.CacheOption = BitmapCacheOption.OnLoad;
        bi.UriSource = new Uri(path);
        bi.EndInit();
        bi.Freeze();

        BitmapSource src = bi;
        if (bi.PixelWidth != targetWidth || bi.PixelHeight != targetHeight)
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
                dc.DrawImage(bi, new Rect(0, 0, targetWidth, targetHeight));
            var rtb = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            src = rtb;
        }

        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int stride = targetWidth * 4;
        var buffer = new byte[targetHeight * stride];
        conv.CopyPixels(buffer, stride, 0);
        return buffer;
    }

    private void UpdateProgressSummary()
    {
        if (_save == null) { TxtProgressSummary.Text = ""; return; }
        var r = _save.DataRoot;

        int licenses = 0, licensesTotal = 0;
        if (r["PlayerLicenses"] is XObject lic && lic["Licenses"] is XArray la)
        {
            licensesTotal = la.Count;
            foreach (var l in la.Items.OfType<XObject>())
                if (Editing.GetBool(l, "Passed")) licenses++;
        }

        int contests = 0, contestsTotal = 0;
        if (r["PlayerLicenses"] is XObject lic2 && lic2["Contests"] is XArray ca)
        {
            contestsTotal = ca.Count;
            foreach (var c in ca.Items.OfType<XObject>())
                if (Editing.GetBool(c, "Passed")) contests++;
        }

        int wrecks = 0, wrecksTotal = 0;
        if (r["Wrecks"] is XObject wr)
            foreach (var key in new[] { "Wrecks_IBIZA", "Wrecks_HAWAI" })
                if (wr[key] is XArray wa)
                {
                    wrecksTotal += wa.Count;
                    foreach (var w in wa.Items.OfType<XObject>())
                        if (Editing.GetBool(w, "Taken")) wrecks++;
                }

        int photos = 0, photosTotal = 0;
        if (r["PhotosGP"] is XObject ph && ph["PhotosGP"] is XArray pa)
        {
            photosTotal = pa.Count;
            foreach (var p in pa.Items.OfType<XObject>())
                if (Editing.GetBool(p, "Taken")) photos++;
        }

        int cars = 0, slots = 0;
        if (r["Garage"] is XArray garageArr)
        {
            slots = garageArr.Count;
            foreach (var e in garageArr.Items.OfType<XObject>())
                if (e["Car"] is XObject carObj && Editing.GetULong(carObj, "ID") != 0xFFFFFFFFUL)
                    cars++;
        }

        TxtProgressSummary.Text =
            $"Fahrzeuge in Garage: {cars} (von {slots} Stellplätzen)\n" +
            $"Lizenzen bestanden: {licenses}/{licensesTotal}\n" +
            $"Cups & Championships: {contests}/{contestsTotal}\n" +
            $"Wracks eingesammelt: {wrecks}/{wrecksTotal}\n" +
            $"Fotospots: {photos}/{photosTotal}";
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) { SetStatus("status.no_save"); return; }
        var result = MessageBox.Show(this,
            "Savegame jetzt schreiben? Es wird automatisch ein Backup im Ordner 'Backups' erstellt.",
            "Speichern", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result != MessageBoxResult.OK) return;

        try
        {
            SaveGameService.Save(_save);
            SetStatus("status.saved");
            PopulateFiles();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Fehler beim Speichern:\n\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("status.save_failed");
        }
    }

    private void Backup_Click(object sender, RoutedEventArgs e)
    {
        if (_save == null) { SetStatus("status.no_save"); return; }
        try
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string dir = System.IO.Path.Combine(_save.PlayerSaveDir, "Backups", _save.ProfileName);
            System.IO.Directory.CreateDirectory(dir);
            int count = 0;
            foreach (var entry in _save.Files.Values)
            {
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, $"{entry.Name}.{stamp}.bak"), entry.OriginalEncrypted);
                count++;
            }
            SetStatus(Loc.F("status.backup_created", count, dir));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void About_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "TDU2 Savegame Editor\n\n" +
            "C# WPF-Editor zum Lesen und Bearbeiten von Test Drive Unlimited 2 Savegames.\n" +
            "Kryptografie (DES-CBC + Eden-SHA1 + KDF) portiert nach C#, XMBF-Codec mit\n" +
            "offset-erhaltendem (surgical) Encoder.\n\n" +
            "Nutzung auf eigene Gefahr. Immer Backups anlegen!",
            "Über", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private void MarkChanged()
    {
        if (!_loading)
            SetStatus("status.changed");
    }

    private void SetStatus(string text)
    {
        StatusText.Text = Loc.T(text);
    }

    private static bool TryLong(string s, out long v)
        => long.TryParse(s?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    private static bool TryFloat(string s, out float v)
        => float.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private void LangDE_Click(object sender, RoutedEventArgs e) => ApplyLanguage(Loc.Lang.DE);
    private void LangEN_Click(object sender, RoutedEventArgs e) => ApplyLanguage(Loc.Lang.EN);

    private void ApplyLanguage(Loc.Lang lang)
    {
        Loc.Set(lang);
        LocalizeTree(this);
        LocalizeColumns(this);
        BuildNav();
        if (_save != null)
        {
            ProfileStatus.Text = $"{Loc.T("bar.profile")} {_save.ProfileName}  |  {_save.PlayerSaveDir}";
            PopulateAssets();
        }
        SetStatus("status.ready");
    }

    /// <summary>
    /// Walks the logical tree and swaps any text that is a known localized
    /// string into the current language. Elements remember their key via Tag so
    /// switching back and forth keeps working.
    /// </summary>
    private void LocalizeTree(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject d) continue;
            LocalizeElement(d);
            LocalizeTree(d);
        }
    }

    private static void LocalizeElement(DependencyObject d)
    {
        switch (d)
        {
            case TextBlock tb when !string.IsNullOrWhiteSpace(tb.Text):
                tb.Text = Rekey(tb, tb.Text);
                break;
            case Button b when b.Content is string bs:
                b.Content = Rekey(b, bs);
                break;
            case MenuItem mi when mi.Header is string hs:
                mi.Header = Rekey(mi, hs);
                break;
            case TabItem ti when ti.Header is string ts:
                ti.Header = Rekey(ti, ts);
                break;
            case GroupBox gb when gb.Header is string gs:
                gb.Header = Rekey(gb, gs);
                break;
        }

        if (d is FrameworkElement fe)
        {
            if (fe.ToolTip is string tt)
                fe.ToolTip = Rekey(fe, tt);
            else if (fe.ToolTip is ToolTip tip && tip.Content is string tc)
                tip.Content = Rekey(tip, tc);
        }
    }

    /// <summary>
    /// DataGrid column headers are not part of the logical tree, so they are not
    /// reached by <see cref="LocalizeTree"/>. This walks the tree, finds every
    /// DataGrid and localizes its column headers (e.g. the garage "Fahrzeug"/
    /// "Car" column).
    /// </summary>
    private void LocalizeColumns(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is not DependencyObject d) continue;
            if (d is DataGrid dg)
                foreach (var col in dg.Columns)
                    if (col.Header is string header)
                        col.Header = Rekey(col, header);
            LocalizeColumns(d);
        }
    }

    private static string Rekey(DependencyObject element, string current)
    {
        var key = element is FrameworkElement fe && fe.Tag is string t ? t : Loc.KeyFor(current);
        if (key == null) return current;
        var text = Loc.T(key);
        if (text == key && key != current) return current;
        if (element is FrameworkElement f2) f2.Tag = key;
        return text;
    }
}
