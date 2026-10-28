using Godot;

[Tool]
public partial class Plataforma : StaticBody2D, IGolpeable
{
	private Vector2 _size = new Vector2(200, 32);

	[Export]
	public Vector2 Size
	{
		get => _size;
		set
		{
			_size = value;
			Actualizar();
		}
	}

	[Export] public bool Golpeable = false;

	private Area2D _sensor;

	public override void _Ready()
	{
		Actualizar();

		// Solo en el juego, y solo si esta plataforma es destructible
		if (Engine.IsEditorHint() || !Golpeable) return;

		// Sensor un poco más grande que la plataforma (para el dash)
		_sensor = new Area2D();
		_sensor.AddChild(new CollisionShape2D
		{
			Shape = new RectangleShape2D { Size = _size + new Vector2(12, 12) }
		});
		AddChild(_sensor);
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_sensor == null) return;

		foreach (var body in _sensor.GetOverlappingBodies())
		{
			if (body is Personaje p && p.IsDashing)
			{
				QueueFree();
				return;
			}
		}
	}

	// Se rompe con el ataque
	public void RecibirGolpe(int danio, float direccion)
	{
		if (!Golpeable) return;
		QueueFree();
	}

	private void Actualizar()
	{
		var col = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
		var rect = GetNodeOrNull<ColorRect>("ColorRect");
		if (col == null || rect == null) return;

		col.Shape = new RectangleShape2D { Size = _size };
		rect.Size = _size;
		rect.Position = -_size / 2;
	}
}
