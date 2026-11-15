using System.Windows;
using System.Windows.Controls;
using TDU2SaveEditor.Services;

namespace TDU2SaveEditor;

public partial class CarPickerWindow : Window
{
    private readonly List<CarInfo> _all;

    public CarInfo? SelectedCar { get; private set; }

    public CarPickerWindow()
    {
        InitializeComponent();
        _all = App.Cars.All.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        DgCars.ItemsSource = _all;
        DgCars.MouseDoubleClick += (_, _) => Ok_Click(this, new RoutedEventArgs());
        UpdateCount(_all.Count);
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        string q = TxtSearch.Text.Trim();
        List<CarInfo> result;
        if (string.IsNullOrEmpty(q))
        {
            result = _all;
        }
        else
        {
            result = _all.Where(c =>
                c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Brand.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Model.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Id.ToString().Contains(q)).ToList();
        }
        DgCars.ItemsSource = result;
        UpdateCount(result.Count);
    }

    private void UpdateCount(int n) => TxtCount.Text = $"{n} Fahrzeuge";

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (DgCars.SelectedItem is CarInfo car)
        {
            SelectedCar = car;
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
