using Godot;
using System;

namespace Polytoria.Client.Settings;

public partial class GraphicsBenchmarker : Node
{
	private const float BenchmarkDuration = 5f;

	private double _elapsed;
	private int _frames;
	private bool _running;

	public event Action<float>? Finished;

	public void Start()
	{
		_elapsed = 0f;
		_frames = 0;
		_running = true;
		SetProcess(true);
	}

	public override void _Process(double delta)
	{
		if (!_running) return;

		_elapsed += delta;
		_frames++;

		if (_elapsed >= BenchmarkDuration)
		{
			float averageFps = (float)(_frames / _elapsed);
			_running = false;
			SetProcess(false);
			Finished?.Invoke(averageFps);
		}
	}
}
