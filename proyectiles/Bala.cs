using Godot;

public partial class Bala : Area2D
{
	[Export] public float Speed = 500f;
	[Export] public int Danio = 1;
	public float Direction = 1f;

	private double _vida = 2.0;

	public override void _Ready()
	{
		// Por si la escena trae valores raros
		Visible = true;
		Modulate = Colors.White;
		SelfModulate = Colors.White;
		Scale = Vector2.One;
		Rotation = 0;
		ZAsRelative = false;
		ZIndex = 100;

		QueueRedraw();
		BodyEntered += OnBodyEntered;
	}

	public override void _Draw()
	{
		DrawRect(new Rect2(-8, -4, 16, 8), Colors.Yellow);
	}

	public override void _PhysicsProcess(double delta)
	{
		Position += new Vector2(Direction * Speed * (float)delta, 0);

		_vida -= delta;
		if (_vida <= 0)
			QueueFree();
	}

	private void OnBodyEntered(Node2D body)
	{
		if (body is Personaje) return;

		if (body is IGolpeable g)
			g.RecibirGolpe(Danio, Direction);

		QueueFree();
	}
}
