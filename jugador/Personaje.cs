using Godot;
using System.Collections.Generic;

public partial class Personaje : CharacterBody2D
{
	[Export] public float Speed = 250f;
	[Export] public float JumpVelocity = -450f;
	[Export] public float Gravity = 1200f;
	[Export] public int MaxJumps = 2;
	[Export] public PackedScene BalaScene;

	[Export] public float DashSpeed = 600f;
	[Export] public float DashTime = 0.15f;
	[Export] public float DashCooldown = 0.5f;

	[Export] public int Danio = 1;
	[Export] public float AtaqueInicio = 0.08f;
	[Export] public float AtaqueActivo = 0.10f;
	[Export] public float AtaqueRecuperacion = 0.15f;
	[Export] public float AtaqueCooldown = 0.10f;
	[Export] public float AtaqueMovimiento = 0.3f;
	[Export] public float HitboxOffset = 30f;

	// Agacharse
	[Export] public Texture2D TexturaAgachado;
	[Export] public float AlturaAgachado = 28f;
	[Export] public float VelocidadAgachado = 0.4f;
	[Export] public float AjusteSpriteAgachado = 0f;

	private enum Fase { Ninguna, Inicio, Activa, Recuperacion }

	private int _jumpsLeft = 0;
	private float _facing = 1f;
	private double _dashTimer = 0;
	private double _dashCooldownTimer = 0;

	private Area2D _hitbox;
	private Fase _fase = Fase.Ninguna;
	private double _faseTimer = 0;
	private double _ataqueCooldownTimer = 0;
	private readonly HashSet<Node> _golpeados = new();

	// Agacharse
	private bool _agachado = false;
	private CollisionShape2D _col;
	private RectangleShape2D _forma;
	private Sprite2D _sprite;
	private Texture2D _texturaParado;
	private Area2D _techo;
	private float _alturaParado;
	private float _colYParado;
	private float _fondoParado;

	// Reaparición
	public Vector2 PuntoReaparicion;
	private bool _estaMuerto = false; // Bloquea interacciones extra mientras cae

	public bool IsDashing => _dashTimer > 0;

	public override void _Ready()
	{
		PuntoReaparicion = GlobalPosition;

		_hitbox = GetNode<Area2D>("Hitbox");
		_hitbox.Monitoring = false;
		_hitbox.Visible = false;

		// Medidas de pie y sensor de techo
		_col = GetNode<CollisionShape2D>("CollisionShape2D");
		_forma = (RectangleShape2D)_col.Shape.Duplicate();
		_col.Shape = _forma;
		_alturaParado = _forma.Size.Y;
		_colYParado = _col.Position.Y;

		_sprite = GetNode<Sprite2D>("Sprite2D");
		_texturaParado = _sprite.Texture;
		_fondoParado = _sprite.Position.Y + FondoRelativo(_texturaParado);

		float pies = _colYParado + _alturaParado / 2f;
		_techo = new Area2D();
		_techo.CollisionLayer = 0;
		_techo.AddChild(new CollisionShape2D
		{
			Shape = new RectangleShape2D
			{
				Size = new Vector2(_forma.Size.X - 4, _alturaParado - AlturaAgachado)
			},
			Position = new Vector2(0, pies - (_alturaParado + AlturaAgachado) / 2f)
		});
		AddChild(_techo);
	}

	public override void _PhysicsProcess(double delta)
	{
		// Si el personaje está muriendo y esperando el respawn, solo aplicamos gravedad y anulamos el input
		if (_estaMuerto)
		{
			Vector2 deadVelocity = Velocity;
			deadVelocity.Y += Gravity * (float)delta;
			Velocity = deadVelocity;
			MoveAndSlide();
			return;
		}

		Vector2 velocity = Velocity;
		_dashCooldownTimer -= delta;
		_ataqueCooldownTimer -= delta;

		if (IsOnFloor())
			_jumpsLeft = MaxJumps;

		ActualizarAgachado();

		// Dirección (no cambia mientras atacás)
		float dir = Input.GetAxis("move_left", "move_right");
		if (dir != 0 && _fase == Fase.Ninguna)
			_facing = Mathf.Sign(dir);

		_sprite.FlipH = _facing < 0;

		// Iniciar ataque
		if (Input.IsActionJustPressed("attack") && _fase == Fase.Ninguna
			&& _dashTimer <= 0 && _ataqueCooldownTimer <= 0 && !_agachado)
			IniciarAtaque();

		// Iniciar dash
		if (Input.IsActionJustPressed("dash") && _fase == Fase.Ninguna
			&& _dashCooldownTimer <= 0 && _dashTimer <= 0 && !_agachado)
		{
			_dashTimer = DashTime;
			_dashCooldownTimer = DashCooldown;
		}

		if (_dashTimer > 0)
		{
			_dashTimer -= delta;
			velocity.X = _facing * DashSpeed;
			velocity.Y = 0;
		}
		else
		{
			if (!IsOnFloor())
				velocity.Y += Gravity * (float)delta;

			if (Input.IsActionJustPressed("jump") && _jumpsLeft > 0 && !_agachado)
			{
				velocity.Y = JumpVelocity;
				_jumpsLeft--;
			}

			float mult = _fase == Fase.Ninguna ? 1f : AtaqueMovimiento;
			if (_agachado)
				mult = VelocidadAgachado;
			velocity.X = dir * Speed * mult;
		}

		Velocity = velocity;
		MoveAndSlide();

		ActualizarAtaque(delta);

		// Disparo
		if (Input.IsActionJustPressed("shoot") && _dashTimer <= 0)
			Disparar();
	}

	// ---------- REAPARICIÓN ----------

	public async void Reaparecer()
	{
		// Si ya está muerto, ignoramos múltiples choques con la zona de muerte
		if (_estaMuerto) return;
		
		_estaMuerto = true; 

		// Tiempo de espera para mejorar el Game Feel (1 segundo)
		await ToSignal(GetTree().CreateTimer(1.0f), SceneTreeTimer.SignalName.Timeout);

		// Pasó el tiempo, ejecutamos el respawn delegando al motor de físicas
		Callable.From(EjecutarReaparicion).CallDeferred();
	}

	private void EjecutarReaparicion()
	{
		GlobalPosition = PuntoReaparicion;
		Velocity = Vector2.Zero;
		_dashTimer = 0;
		
		// Lo revivimos para devolverle el control
		_estaMuerto = false; 

		// Evita que la cámara intente interpolar el viaje desde el vacío hasta el spawn
		GetNodeOrNull<Camera2D>("Camera2D")?.ResetSmoothing();
	}

	// ---------- AGACHARSE ----------

	private void ActualizarAgachado()
	{
		bool quiere = Input.IsActionPressed("crouch") && IsOnFloor()
			&& _dashTimer <= 0 && _fase == Fase.Ninguna;

		if (quiere && !_agachado)
			SetAgachado(true);
		else if (!quiere && _agachado && !HayTecho())
			SetAgachado(false);
	}

	private bool HayTecho()
	{
		foreach (var body in _techo.GetOverlappingBodies())
		{
			if (body != this)
				return true;
		}
		return false;
	}

	private void SetAgachado(bool valor)
	{
		_agachado = valor;

		float h = valor ? AlturaAgachado : _alturaParado;
		float pies = _colYParado + _alturaParado / 2f;

		_forma.Size = new Vector2(_forma.Size.X, h);
		_col.Position = new Vector2(_col.Position.X, pies - h / 2f);

		Texture2D tex = (valor && TexturaAgachado != null) ? TexturaAgachado : _texturaParado;
		_sprite.Texture = tex;

		float ajuste = valor ? AjusteSpriteAgachado : 0f;
		_sprite.Position = new Vector2(_sprite.Position.X, _fondoParado - FondoRelativo(tex) + ajuste);
	}

	private float FondoRelativo(Texture2D tex)
	{
		float h = tex.GetHeight();
		float centro = _sprite.Centered ? h / 2f : h;
		return (_sprite.Offset.Y + centro) * _sprite.Scale.Y;
	}

	// ---------- ATAQUE CUERPO A CUERPO ----------

	private void IniciarAtaque()
	{
		_fase = Fase.Inicio;
		_faseTimer = AtaqueInicio;
		_golpeados.Clear();
		_hitbox.Position = new Vector2(_facing * HitboxOffset, 0);
	}

	private void ActualizarAtaque(double delta)
	{
		if (_fase == Fase.Ninguna) return;
		_faseTimer -= delta;

		switch (_fase)
		{
			case Fase.Inicio:
				if (_faseTimer <= 0)
				{
					_fase = Fase.Activa;
					_faseTimer = AtaqueActivo;
					_hitbox.Monitoring = true;
					_hitbox.Visible = true;
				}
				break;

			case Fase.Activa:
				AplicarGolpes();
				if (_faseTimer <= 0)
				{
					_fase = Fase.Recuperacion;
					_faseTimer = AtaqueRecuperacion;
					_hitbox.Monitoring = false;
					_hitbox.Visible = false;
				}
				break;

			case Fase.Recuperacion:
				if (_faseTimer <= 0)
				{
					_fase = Fase.Ninguna;
					_ataqueCooldownTimer = AtaqueCooldown;
				}
				break;
		}
	}

	private void AplicarGolpes()
	{
		foreach (var body in _hitbox.GetOverlappingBodies())
			Golpear(body);

		foreach (var area in _hitbox.GetOverlappingAreas())
			Golpear(area);
	}

	private void Golpear(Node objetivo)
	{
		if (objetivo is IGolpeable g && !_golpeados.Contains(objetivo))
		{
			_golpeados.Add(objetivo);
			g.RecibirGolpe(Danio, _facing);
		}
	}

	// ---------- DISPARO ----------

	private void Disparar()
	{
		if (BalaScene == null)
		{
			GD.PrintErr("BalaScene está vacío");
			return;
		}

		var nodo = BalaScene.Instantiate();
		if (nodo is not Bala bala)
		{
			GD.PrintErr("La raíz de bala.tscn no tiene el script Bala.cs");
			nodo.QueueFree();
			return;
		}

		GetParent().AddChild(bala);
		bala.GlobalPosition = GlobalPosition + new Vector2(_facing * 30, _agachado ? 6 : -4);
		bala.Direction = _facing;
	}
}
