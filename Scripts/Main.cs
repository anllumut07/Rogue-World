using Godot;

// Oyunun kök sahnesi: sistemlerin sırasıyla ayağa kalkmasını koordine eder.
public partial class Main : Node
{
	public override void _Ready()
	{
		GD.Print("Rogue World hazır! (Main scene yüklendi)");
	}
}