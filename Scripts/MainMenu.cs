using Godot;

// Ana menü: START (oyun sahnesine geç), SETTINGS (ayarlar paneli), QUIT (çıkış).
public partial class MainMenu : Control
{
    public override void _Ready()
    {
        // Ortak koyu tema
        UITheme.ApplyTheme(this);

        // Sahne değişiminde pause kilitli kalabilir -> ana menü her zaman canlı açılır.
        GetTree().Paused = false;

        // Butonları bağla (unique_name_in_owner sahnede işaretli)
        var startBtn = GetNode<Button>("%StartButton");
        var settingsBtn = GetNode<Button>("%SettingsButton");
        var quitBtn = GetNode<Button>("%QuitButton");

        startBtn.Pressed += OnStartPressed;
        settingsBtn.Pressed += OnSettingsPressed;
        quitBtn.Pressed += OnQuitPressed;

        // Hover scale efekti
        UITheme.AddHoverScale(startBtn);
        UITheme.AddHoverScale(settingsBtn);
        UITheme.AddHoverScale(quitBtn);

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