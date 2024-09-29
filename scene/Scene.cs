using System.IO;
using Godot;
using Microsoft.VisualBasic;

#if __ANDROID__
using SkiaSharp.Views.Android;
#else
using SkiaSharp.Views.Desktop;
#endif

using SKControl = SkiaSharp.Views.Godot.SKControl;

namespace Scene;

public partial class Scene : Control
{
	Runtime runtime = null!;
	static readonly string dirname = "app";
	static readonly string sourceDir = "res://" + dirname;
	static readonly string targetDir = "user://" + dirname;

	public static void UnWrapApp()
	{
		GD.Print("Granted: " + Strings.Join(OS.GetGrantedPermissions(), ", "));
		GD.Print("Copying " + sourceDir + " to " + targetDir);

		try
		{
			DirAccess.RemoveAbsolute(targetDir);
		}
		catch { }
		try
		{
			DirAccess.Open("user://").Remove(dirname);
		}
		catch { }
		DirAccess.Open("user://").MakeDir(dirname);
		CopyDirectory(sourceDir, targetDir);
	}

	public static void EnsureApp()
	{
		UnWrapApp();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		if (runtime.RunUpdate(delta) && sKPaintSurfaceEventArgs != null)
		{
			skControl.QueueRedraw();
		}
	}

	SKControl skControl = null!;

	public override void _Ready()
	{
		skControl = (SKControl)FindChild("SKControl");
		skControl.PaintSurface += OnSKControlPaintSurface;

		SetProcess(true);
		EnsureApp();

		GD.Print("Main File: " + ProjectSettings.GlobalizePath(targetDir) + "/main.ms");

		runtime = Runtime.RunFile(this, ProjectSettings.GlobalizePath(targetDir) + "/main.ms");

		if (runtime == null)
		{
			GetTree().Quit();

			return;
		}

		runtime.errorHandler.On((message) =>
		{
			GD.Print("Unhandled Rejection " + message);
			GetTree().Quit();

			return true;
		});

		runtime.Run();

		while (!runtime.RunReady()) { }
	}

	static void CopyDirectory(string source, string target)
	{
		GD.Print("---Opening " + source + "---");

		var dir = DirAccess.Open(source);

		GD.Print(dir);
		GD.Print("Files: " + Strings.Join(dir.GetFiles(), ", "));

		foreach (var name in dir.GetFiles())
		{
			GD.Print("--Copying " + dir + "/" + name + "---");

			var fileAccess = Godot.FileAccess.Open(Path.Join(target, name), Godot.FileAccess.ModeFlags.Write);
			var src = Godot.FileAccess.Open(Path.Join(source, name), Godot.FileAccess.ModeFlags.Read);

			fileAccess.StoreBuffer(src.GetBuffer((long)src.GetLength()));
			fileAccess.Close();
			GD.Print("--Success copying " + dir + "/" + name + "---");
		}
		GD.Print("Directories: " + Strings.Join(dir.GetDirectories(), ", "));

		foreach (var name in dir.GetDirectories())
		{
			GD.Print("--Copying " + dir + "/" + name + "---");
			DirAccess.Open(source).MakeDir(name);
			CopyDirectory(Path.Join(source, name), Path.Join(target, name));
			GD.Print("--Success copying " + dir + "/" + name + "---");
		}

		GD.Print("---Closing " + source + "---");
	}

	SKPaintSurfaceEventArgs sKPaintSurfaceEventArgs = null!;

	// Based on https://swharden.com/csdv/skiasharp/skiasharp/
	private void OnSKControlPaintSurface(object? _, SKPaintSurfaceEventArgs e)
	{
		var imageInfo = e.Info;

		if (sKPaintSurfaceEventArgs != null && (imageInfo.Width != sKPaintSurfaceEventArgs.Info.Width || imageInfo.Height != sKPaintSurfaceEventArgs.Info.Height))
		{
			if (runtime.RunResize(imageInfo.Width, imageInfo.Height))
			{
				sKPaintSurfaceEventArgs = e;
				runtime.RunRender(sKPaintSurfaceEventArgs);

				return;
			}
		}
		sKPaintSurfaceEventArgs = e;

		runtime.RunRender(sKPaintSurfaceEventArgs);

	}
}
