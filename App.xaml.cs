using System.Windows;
using TDU2SaveEditor.Services;

namespace TDU2SaveEditor;

public partial class App : Application
{
    public static GameDatabase Game { get; private set; } = GameDatabase.Empty;
    public static CarsDatabase Cars => Game.Cars;
    public static HousesDatabase Houses { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        try
        {
            Game = GameDatabase.LoadEmbedded();
        }
        catch
        {
            Game = GameDatabase.Empty;
        }

        Houses = HousesDatabase.Load(System.IO.Path.Combine(baseDir, "Database", "houses_database.json"));
    }
}
