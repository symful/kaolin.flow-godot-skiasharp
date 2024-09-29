using System.IO;
using Godot;
using Kaolin.Flow.Core;
using Miniscript;

#if __ANDROID__
using SkiaSharp.Views.Android;
#else
using SkiaSharp.Views.Desktop;
#endif
using Engine = Kaolin.Flow.Core.Engine;

class Runtime(Scene.Scene scene, Interpreter interpreter, string path, bool isDebugging = false) : Engine(interpreter, path, isDebugging)
{
	public Scene.Scene scene = scene;
	public CanvasPlugin canvasPlugin = null!;

	public override void Print(string content, bool newLine)
	{
		if (newLine) GD.Print(content);
		else GD.PrintRaw(content);
	}

	public static Runtime RunFile(Scene.Scene scene, string path)
	{
		GD.Print("---Engine starting---");
		StreamReader file = new(path);
		if (file == null)
		{
			GD.Print("Unable to read: " + path);
			return null!;
		}
		Interpreter miniscript = new()
		{
			standardOutput = (content, newLine) =>
			{
				if (newLine) GD.Print(content);
				else GD.PrintRaw(content);
			}
		};
		Parser parser = new()
		{
			errorContext = Utils.WrapPath(path)
		};
		parser.Parse(file.ReadToEnd());
		miniscript.Compile();
		miniscript.vm.ManuallyPushCall(new ValFunction(parser.CreateImport()));

		var core = new Runtime(scene, miniscript, Utils.WrapPath(path), false);
		GD.Print("---Engine running---");

		return core;
	}
	public override void Inject()
	{
		base.Inject();

		canvasPlugin ??= new(this);


	}
	new void Invoke(ValFunction function, Value[] arguments)
	{
		base.Invoke(function, arguments);
		interpreter.RunUntilDone();
	}
	new Value InvokeValue(ValFunction function, Value[] arguments)
	{
		return base.InvokeValue(function, arguments);
	}
	public bool RunResize(int width, int height)
	{
		var f = (ValFunction)interpreter.GetGlobalValue("onResize");

		if (f == null) return false;

		return InvokeValue(f, [Utils.Cast(width), Utils.Cast(height)]).IntValue() == 1;
	}
	public void RunRender(SKPaintSurfaceEventArgs e)
	{
		var f = (ValFunction)interpreter.GetGlobalValue("onRender");

		SetSurfaceEventArgs(e);

		if (f == null) return;

		Invoke(f, [mainCanvas]);
	}
	public bool RunReady()
	{
		var f = (ValFunction)interpreter.GetGlobalValue("onReady");

		if (f == null) return true;

		return InvokeValue(f, []).BoolValue();
	}
	public void SetSurfaceEventArgs(SKPaintSurfaceEventArgs e)
	{
		if (mainCanvas == null)
		{
			mainCanvas = CanvasPlugin.WrapCanvas(e);
		}
		else
		{
			mainCanvas.userData = e;
		}
	}
	public bool RunUpdate(double delta)
	{
		var f = (ValFunction)interpreter.GetGlobalValue("onUpdate");

		if (f == null) return false;

		return InvokeValue(f, [Utils.Cast(delta)]).IntValue() == 1;
	}

	public ValMap mainCanvas = null!;
}
