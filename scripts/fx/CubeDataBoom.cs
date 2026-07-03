using Godot;
using System;

namespace Kuros.Fx
{
    public partial class CubeDataBoom : Node2D
    {
        [Export] public PackedScene? CubeScene { get; set; }

        [ExportCategory("Spawn")]
        [Export(PropertyHint.Range, "1,100,1")] public int SpawnCount = 12;
        [Export(PropertyHint.Range, "0,200,1")] public float SpawnRadius = 10f;
        [Export(PropertyHint.Range, "0.05,5,0.05")] public float SpawnDuration = 0.15f;

        [ExportCategory("Velocity")]
        [Export(PropertyHint.Range, "50,3000,10")] public float MinSpeed = 200f;
        [Export(PropertyHint.Range, "50,3000,10")] public float MaxSpeed = 600f;
        [Export(PropertyHint.Range, "0,360,1")] public float SpreadAngle = 360f;
        [Export(PropertyHint.Range, "-500,500,10")] public float Gravity = 200f;
        [Export] public Vector2 BaseDirection = Vector2.Up;

        [ExportCategory("Scale")]
        [Export(PropertyHint.Range, "0.1,5,0.05")] public float ScaleMin = 0.3f;
        [Export(PropertyHint.Range, "0.1,5,0.05")] public float ScaleMax = 1.2f;

        [ExportCategory("Lifetime")]
        [Export(PropertyHint.Range, "0.01,10,0.01")] public float MinLifetime = 0.5f;
        [Export(PropertyHint.Range, "0.01,10,0.01")] public float MaxLifetime = 1.5f;

        [ExportCategory("Auto")]
        [Export] public bool AutoPlay = true;

        private int _spawned;
        private float _spawnTimer;
        private bool _playing;

        public override void _Ready()
        {
            if (AutoPlay)
                Play();
        }

        public void Play()
        {
            _playing = true;
            _spawned = 0;
            _spawnTimer = 0f;
        }

        public override void _Process(double delta)
        {
            if (!_playing) return;

            float dt = (float)delta;
            _spawnTimer += dt;

            int targetSpawned = SpawnDuration > 0f
                ? Mathf.RoundToInt(Mathf.Lerp(0, SpawnCount, Mathf.Min(_spawnTimer / SpawnDuration, 1f)))
                : SpawnCount;

            while (_spawned < targetSpawned)
            {
                SpawnCube();
                _spawned++;
            }

            if (_spawned >= SpawnCount)
            {
                _playing = false;
                QueueFree();
            }
        }

        private void SpawnCube()
        {
            if (CubeScene == null) return;
            var parent = GetParent();
            if (parent == null) return;

            var instance = CubeScene.Instantiate<Node2D>();
            if (instance == null) return;

            float halfAngle = SpreadAngle * 0.5f;
            float angleOffset = Mathf.DegToRad((float)GD.RandRange(-halfAngle, halfAngle));
            Vector2 dir = BaseDirection.LengthSquared() > 0.01f
                ? BaseDirection.Normalized().Rotated(angleOffset)
                : Vector2.FromAngle((float)GD.RandRange(0f, Mathf.Tau));

            float speed = (float)GD.RandRange(MinSpeed, MaxSpeed);
            float lifetime = (float)GD.RandRange(MinLifetime, MaxLifetime);
            float scale = (float)GD.RandRange(ScaleMin, ScaleMax);

            // Set RotatingCube properties before AddChild — _Ready reads them in PlaySpawnAnimation
            if (instance is RotatingCube cube)
            {
                cube.BaseScale = scale;
                cube.Speed = 0f;
                cube.Duration = lifetime;
                cube.FacingRight = GD.Randf() > 0.5f;
                cube.MaxVerticalTiltDegrees = (float)GD.RandRange(0f, 90f);
            }

            parent.AddChild(instance);
            instance.GlobalPosition = GlobalPosition + RandomInsideCircle(SpawnRadius);

            if (instance is RotatingCube cube2)
            {
                float flightDuration = lifetime + cube2.DespawnDuration;
                Vector2 endPos = instance.GlobalPosition
                    + dir * (speed * flightDuration)
                    + new Vector2(0f, Gravity * flightDuration * flightDuration * 0.5f);

                var tween = instance.CreateTween();
                tween.TweenProperty(instance, "global_position", endPos, flightDuration);
                tween.SetEase(Tween.EaseType.Out);
                tween.SetTrans(Tween.TransitionType.Cubic);
            }
        }

        private static Vector2 RandomInsideCircle(float radius)
        {
            float angle = (float)GD.RandRange(0f, Mathf.Tau);
            float r = (float)GD.RandRange(0f, radius);
            return new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r);
        }
    }
}
