using Godot;

// Ana menü: START (oyun sahnesine geç), SETTINGS (ayarlar paneli), QUIT (çıkış).
public partial class MainMenu : Control
{
    public override void _Ready()
    {
        // Sahne değişiminde pause kilitli kalabilir -> ana menü her zaman canlı açılır.
        GetTree().Paused = false;

        // Butonları bağla (unique_name_in_owner sahnede işaretli)
        GetNode<Button>("%StartButton").Pressed += OnStartPressed;
        GetNode<Button>("%SettingsButton").Pressed += OnSettingsPressed;
        GetNode<Button>("%QuitButton").Pressed += OnQuitPressed;

        GD.Print("MainMenu: hazır");
    }

    private void OnStartPressed()
    {
        GD.Print("MainMenu: Oyun başlatılıyor");
        GetTree().ChangeSceneToFile("res://Scenes/game.tscn");
    }

    private void OnSettingsPressed()
    {
        GD.Print("MainMenu: Ayarlar açılıyor");
        // Panel overlay olarak açılır; BACK ile kapanınca menü yerinde kalır.
        SettingsPanel.OpenUnder(this);
    }

    private void OnQuitPressed()
    {
        GD.Print("MainMenu: Çıkış");
        GetTree().Quit();
    }
}