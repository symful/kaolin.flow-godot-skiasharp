using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Godot;
using Kaolin.Flow.Builders;
using Kaolin.Flow.Core;
using Kaolin.Flow.Plugins;
using Microsoft.VisualBasic;
using Miniscript;
using SkiaSharp;

#if __ANDROID__
using SkiaSharp.Views.Android;
#else
using SkiaSharp.Views.Desktop;
#endif

using SkiaSharp.Views.Godot;

class GradientPosition(float offset, SKColor color)
{
	public float offset = offset;
	public SKColor color = color;
}

enum GradientType
{

}

abstract class GradientData
{
	public List<GradientPosition> positions = [];

	public abstract SKShader Create();

	public void AddColorStop(float offset, SKColor color)
	{
		positions.Add(new GradientPosition(offset, color));
	}
}

class LinearGradientData(SKPoint start, SKPoint end) : GradientData
{
	SKPoint start = start;
	SKPoint end = end;
	public override SKShader Create()
	{
		return SKShader.CreateLinearGradient(
			start,
			end,
			[.. positions.Select(x => x.color)],
			[.. positions.Select(x => x.offset)],
			SKShaderTileMode.Clamp
		);
	}
}
class ConicGradientData(SKPoint center, float startAngle) : GradientData
{
	SKPoint center = center;
	readonly float startAngle = startAngle;
	public override SKShader Create()
	{
		return SKShader.CreateSweepGradient(
			center,
			[.. positions.Select(x => x.color)],
			[.. positions.Select(x => x.offset)],
			SKShaderTileMode.Clamp,
			startAngle,
			startAngle + 2 * (float)Math.PI
		);
	}
}
class RadialGradientData(SKPoint start, float startRadius, SKPoint end, float endRadius) : GradientData
{
	SKPoint start = start;
	readonly float startRadius = startRadius;
	SKPoint end = end;
	readonly float endRadius = endRadius;
	public override SKShader Create()
	{
		return SKShader.CreateTwoPointConicalGradient(
			start,
			startRadius,
			end,
			endRadius,
			[.. positions.Select(x => x.color)],
			[.. positions.Select(x => x.offset)],
			SKShaderTileMode.Clamp

		);
	}
}

class CanvasPattern(SKImage image, SKShaderTileMode tileModeX, SKShaderTileMode tileModeY)
{
	SKImage image = image;
	public SKMatrix matrix = SKMatrix.Empty;
	SKShaderTileMode tileModeX = tileModeX;
	SKShaderTileMode tileModeY = tileModeY;

	public SKShader Create()
	{
		return SKShader.CreateImage(image, tileModeX, tileModeY, matrix);
	}
}

class CanvasPlugin(Runtime engine) : Base(engine)
{
	public static async Task<SKImage> LoadImage(string url)
	{
		if (Utils.GetProtocol(url) == "file") return SKImage.FromBitmap(SKBitmap.Decode(System.IO.File.OpenRead(new Uri(url).AbsolutePath)));

		using System.Net.Http.HttpClient httpClient = new();

		var requestMessage = new HttpRequestMessage(HttpMethod.Get, url);
		var response = await httpClient.SendAsync(requestMessage);

		using var stream = await response.Content.ReadAsStreamAsync();
		return SKImage.FromBitmap(SKBitmap.Decode(stream));
	}
	public static ValMap CanvasClass = new MapBuilder()
		.AddProp("randomColor",
			new FunctionBuilder("randomColor")
				.SetCallback((context, p) =>
				{
					Random rand = new();

					int r = (int)(rand.NextDouble() * 255);
					int g = (int)(rand.NextDouble() * 255);
					int b = (int)(rand.NextDouble() * 255);

					string hexColor = $"#{r:X2}{g:X2}{b:X2}";
					return new Intrinsic.Result(Utils.Cast(hexColor));
				})
				.Function
		)
		.AddProp("create",
			new FunctionBuilder("create")
				.AddParam("width")
				.AddParam("height")
				.SetCallback((context, p) =>
				{
					var width = context.GetLocalInt("width");
					var height = context.GetLocalInt("height");

					return new Intrinsic.Result(WrapCanvas(
						new SKPaintSurfaceEventArgs(SKSurface.Create(new SKImageInfo(width, height)), new SKImageInfo(width, height))
					));
				})
				.Function
		)
		.map;
	public readonly static ValMap TextMetricsClass = new();
	public readonly static ValMap CanvasPatternClass = new MapBuilder()
		.map;
	public readonly static ValMap ImageClass = new MapBuilder()
		.AddProp("loadImage",
			new FunctionBuilder("loadImage")
				.AddParam("url")
				.SetCallback((context, p) =>
				{
					return new Intrinsic.Result(
						WrapImage(
							LoadImage(context.GetLocalString("url"))
								.GetAwaiter()
								.GetResult()
						)
					);
				})
				.Function
		)
		.map;

	public readonly static ValMap ImageDataClass = new MapBuilder()
		.AddProp("create",
			new FunctionBuilder("create")
				.AddParam("arg1")
				.AddParam("arg2")
				.AddParam("arg3")
				.SetCallback((context, p) =>
				{
					int width;
					int height;
					string colorSpace = "srgb";

					if (context.args.Count == 1)
					{
						var imageData = (ValMap)context.GetLocal("arg1");

						imageData.TryGetValue("width", out Value _w);
						imageData.TryGetValue("height", out Value _h);

						width = _w.IntValue();
						height = _h.IntValue();
					}
					else
					{
						width = context.GetLocalInt("width");
						height = context.GetLocalInt("height");
						var o = (ValMap)context.GetLocal("settings");

						o.TryGetValue("colorSpace", out Value v);

						if (v != null) colorSpace = v.ToString();
					}

					return new Intrinsic.Result(
						CreateImageData(width, height, colorSpace)
					);
				})
				.Function

		)
		.map;
	public readonly static ValMap MatrixClass = new MapBuilder()
		.AddProp("create",
			new FunctionBuilder("create")
				.AddParam("translateX")
				.AddParam("translateY")
				.AddParam("scaleX")
				.AddParam("scaleY")
				.AddParam("skewX")
				.AddParam("skewY")
				.SetCallback((context, p) =>
				{
					return new Intrinsic.Result(
						WrapMatrix(
							context.GetLocalFloat("translateX"),
							context.GetLocalFloat("translateY"),
							context.GetLocalFloat("scaleX"),
							context.GetLocalFloat("scaleY"),
							context.GetLocalFloat("skewX"),
							context.GetLocalFloat("skewY")
						)
					);
				})
				.Function
		)
		.map;

	public readonly static ValMap PathClass = new MapBuilder()
		.AddProp("create",
			new FunctionBuilder("create")
				.SetCallback((context, p) =>
				{
					return new Intrinsic.Result(WrapPath(new SKPath()));
				})
				.Function
		)
		.map;

	public readonly static ValMap CanvasGradientClass = new MapBuilder()
		.map;
	public static ValMap WrapImage(SKImage image)
	{
		return new MapBuilder(Runtime.New(ImageClass))
			.AddProp("width", Utils.Cast(image.Width))
			.AddProp("height", Utils.Cast(image.Height))
			.map;
	}
	public static SKBitmap ToSKBitmap(ValMap map)
	{
		map.TryGetValue("data", out Value _data);
		map.TryGetValue("colorSpace", out Value _colorSpace);
		map.TryGetValue("width", out Value _width);
		map.TryGetValue("height", out Value _height);

		var width = _width.IntValue();
		var height = _height.IntValue();
		var data = ((ValList)_data).values.Select(x => (byte)x.IntValue()).ToArray()!;
		var cs = _colorSpace.ToString();
		SKColorSpace colorSpace = cs == "srgb" ? SKColorSpace.CreateSrgb() : SKColorSpace.CreateIcc(data, data.Count());

		return new SKBitmap(width, height, SKColorType.Unknown, SKAlphaType.Unknown, colorSpace);
	}
	public static ValMap CreatePattern(SKImage image, string repetition)
	{

		SKShaderTileMode tileModeX;
		SKShaderTileMode tileModeY;

		if (repetition == "repeat")
		{
			tileModeX = SKShaderTileMode.Repeat;
			tileModeY = SKShaderTileMode.Repeat;
		}
		else if (repetition == "repeat-x")
		{
			tileModeX = SKShaderTileMode.Repeat;
			tileModeY = SKShaderTileMode.Clamp;
		}
		else if (repetition == "repeat-y")
		{
			tileModeX = SKShaderTileMode.Clamp;
			tileModeY = SKShaderTileMode.Repeat;
		}
		else
		{
			tileModeX = SKShaderTileMode.Clamp;
			tileModeY = SKShaderTileMode.Clamp;
		}

		CanvasPattern pattern = new(image, tileModeX, tileModeY);

		return new MapBuilder(Runtime.New(CanvasPatternClass))
			.SetUserData(pattern)
			.AddProp("setTransform",
				new FunctionBuilder()
					.AddParam("matrix")
					.SetCallback((context, p) =>
					{
						pattern.matrix = (SKMatrix)((ValMap)context.GetLocal("matrix")).userData;

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.map;
	}
	public static ValMap CreateImageData(int width, int height, string colorSpace)
	{
		return new MapBuilder(Runtime.New(ImageDataClass))
			.AddProp("width", Utils.Cast(width))
			.AddProp("height", Utils.Cast(height))
			.AddProp("colorSpace", Utils.Cast(colorSpace))
			.AddProp("data", Utils.Cast(new ValList(Enumerable.Repeat((Value)Utils.Cast(0), width * height).ToList())))
			.map;
	}
	public static ValMap WrapImageData(SKBitmap bitmap)
	{
		return new MapBuilder(Runtime.New(ImageDataClass))
			.AddProp("width", Utils.Cast(bitmap.Width))
			.AddProp("height", Utils.Cast(bitmap.Height))
			.AddProp("colorSpace", Utils.Cast(bitmap.ColorSpace))
			.AddProp("data", Utils.Cast(new ValList([.. bitmap.Bytes.Select(x => (Value)Utils.Cast(x))])))
			.map;
	}


	public override void Inject()
	{
		Register("Canvas", CanvasClass);
		Register("Path", PathClass);
		Register("TextMetrics", TextMetricsClass);
		Register("Matrix", MatrixClass);
		Register("Image", ImageClass);
		Register("ImageData", ImageDataClass);
		Register("CanvasGradient", CanvasGradientClass);
	}

	static private float NormalizeAngle(float radians)
	{
		float twoPi = 2 * (float)Math.PI;
		radians %= twoPi;
		if (radians < 0)
		{
			radians += twoPi;
		}
		return radians;
	}
	public static void PathArc(SKPath path, float x, float y, float radius, float startAngle, float endAngle, bool counterClockwise)
	{
		if (counterClockwise)
		{
			(endAngle, startAngle) = (startAngle, endAngle);
		}

		float sweepAngle = endAngle - startAngle;
		if (counterClockwise)
		{
			sweepAngle = -sweepAngle;
		}

		SKRect rect = new(x - radius, y - radius, x + radius, y + radius);
		path.AddArc(rect, startAngle, sweepAngle);
	}

	public static ValMap WrapGradient(GradientData gradientData)
	{
		return new MapBuilder(Runtime.New(CanvasGradientClass))
			.SetUserData(gradientData)
			.AddProp("addColorStop",
				new FunctionBuilder()
					.AddParam("offset")
					.AddParam("color")
					.SetCallback((context, p) =>
					{
						var offset = context.GetLocalFloat("offset");
						var color = context.GetLocalString("color");

						gradientData.AddColorStop(offset, SKColor.Parse(color));

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.map;
	}

	public static void PathRoundRect(SKPath path, float x, float y, float width, float height, Value radii)
	{
		var rect = new SKRect(x, y, width, height);
		SKRoundRect roundRect;

		if (radii is ValNumber)
		{
			var n = radii.FloatValue();
			roundRect = new SKRoundRect();

			roundRect.SetRectRadii(rect, [
				new SKPoint(n, n),
				new SKPoint(n, n),
				new SKPoint(n, n),
				new SKPoint(n, n)
			]);
		}
		else if (radii is ValList list)
		{
			var v = list.values;

			if (v.Count == 1)
			{
				var n = v[0].FloatValue();
				roundRect = new SKRoundRect();

				roundRect.SetRectRadii(rect, [
					new SKPoint(n, n),
					new SKPoint(n, n),
					new SKPoint(n, n),
					new SKPoint(n, n)
				]);

			}
			else if (v.Count == 2)
			{
				var radiusTLBR = v[0].FloatValue();
				var radiusTRBL = v[1].FloatValue();
				var r = new SKPoint[]
				{
					new(radiusTLBR, radiusTLBR),  // top-left
					new(radiusTRBL, radiusTRBL),  // top-right
					new(radiusTLBR, radiusTLBR),  // bottom-right
					new(radiusTRBL, radiusTRBL)   // bottom-left
				};
				roundRect = new SKRoundRect();
				roundRect.SetRectRadii(rect, r);
			}
			else if (v.Count == 3)
			{
				var radiusTL = v[0].FloatValue();
				var radiusBR = v[2].FloatValue();
				var radiusTRBL = v[1].FloatValue();
				var r = new SKPoint[]
				{
					new(radiusTL, radiusTL),      // top-left
					new(radiusTRBL, radiusTRBL),  // top-right
					new(radiusBR, radiusBR),      // bottom-right
					new(radiusTRBL, radiusTRBL)   // bottom-left
				};
				roundRect = new SKRoundRect();
				roundRect.SetRectRadii(rect, r);
			}
			else
			{
				var radiusTL = v[0].FloatValue();
				var radiusTR = v[1].FloatValue();
				var radiusBL = v[3].FloatValue();
				var radiusBR = v[2].FloatValue();
				var r = new SKPoint[]
				{
					new(radiusTL, radiusTL),  // top-left
					new(radiusTR, radiusTR),  // top-right
					new(radiusBR, radiusBR),  // bottom-right
					new(radiusBL, radiusBL)   // bottom-left
				};
				roundRect = new SKRoundRect();
				roundRect.SetRectRadii(rect, r);

			}
		}
		else
		{
			var o = (ValMap)radii;
			o.TryGetValue("x", out Value _x);
			o.TryGetValue("y", out Value _y);
			var xr = _x.FloatValue();
			var yr = _x.FloatValue();

			roundRect = new SKRoundRect(rect, xr, yr);
		}

		path.AddRoundRect(roundRect);
	}
	public static ValMap WrapPath(SKPath path)
	{
		return new MapBuilder(Runtime.New(PathClass))
			.SetUserData(path)
			.AddProp("addPath",
				new FunctionBuilder("addPath")
					.AddParam("path")
					.SetCallback((context, p) =>
					{
						path.AddPath((SKPath)((ValMap)context.GetLocal("path")).userData);
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("moveTo",
				new FunctionBuilder("moveTo")
					.AddParam("x")
					.AddParam("y")
					.SetCallback((context, p) =>
					{
						path.MoveTo(context.GetLocalFloat("x"), context.GetLocalFloat("y"));
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("lineTo",
				new FunctionBuilder("lineTo")
					.AddParam("x")
					.AddParam("y")
					.SetCallback((context, p) =>
					{
						path.LineTo(context.GetLocalFloat("x"), context.GetLocalFloat("y"));
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("bezierCurveTo",
				new FunctionBuilder("bezierCurveTo")
					.AddParam("x1")
					.AddParam("y1")
					.AddParam("x2")
					.AddParam("y2")
					.AddParam("x3")
					.AddParam("y3")
					.SetCallback((context, p) =>
					{
						path.CubicTo(
							context.GetLocalFloat("x1"),
							context.GetLocalFloat("y1"),
							context.GetLocalFloat("x2"),
							context.GetLocalFloat("y2"),
							context.GetLocalFloat("x3"),
							context.GetLocalFloat("y3")
						);
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("quadraticCurveTo",
				new FunctionBuilder("quadraticCurveTo")
					.AddParam("x1")
					.AddParam("y1")
					.AddParam("x2")
					.AddParam("y2")
					.SetCallback((context, p) =>
					{
						path.QuadTo(
							context.GetLocalFloat("x1"),
							context.GetLocalFloat("y1"),
							context.GetLocalFloat("x2"),
							context.GetLocalFloat("y2")
						);
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("arc",
				new FunctionBuilder("arc")
					.AddParam("x")
					.AddParam("y")
					.AddParam("radius")
					.AddParam("startAngle")
					.AddParam("endAngle")
					.AddParam("counterClockwise", Utils.Cast(true))
					.SetCallback((context, p) =>
					{
						var x = context.GetLocalFloat("x");
						var y = context.GetLocalFloat("y");
						var startAngle = NormalizeAngle(context.GetLocalFloat("startAngle"));
						var endAngle = NormalizeAngle(context.GetLocalFloat("endAngle"));
						var counterClockwise = context.GetLocalBool("counterClockwise");
						var radius = context.GetLocalFloat("radius");

						PathArc(path, x, y, radius, startAngle, endAngle, counterClockwise);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("arcTo",
				new FunctionBuilder("arcTo")
					.AddParam("x1")
					.AddParam("y1")
					.AddParam("x2")
					.AddParam("y2")
					.AddParam("radius")
					.SetCallback((context, p) =>
					{
						var x1 = context.GetLocalFloat("x1");
						var y1 = context.GetLocalFloat("y1");
						var x2 = context.GetLocalFloat("x2");
						var y2 = context.GetLocalFloat("y2");
						var radius = context.GetLocalFloat("radius");

						path.ArcTo(x1, y1, x2, y2, radius);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("rect",
				new FunctionBuilder("rect")
					.AddParam("x")
					.AddParam("y")
					.AddParam("width")
					.AddParam("height")
					.SetCallback((context, p) =>
					{
						var x = context.GetLocalFloat("x");
						var y = context.GetLocalFloat("y");
						var width = context.GetLocalFloat("width");
						var height = context.GetLocalFloat("height");

						path.AddRect(SKRect.Create(x, y, width, height));

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("roundRect",
				new FunctionBuilder("roundRect")
					.AddParam("x")
					.AddParam("y")
					.AddParam("width")
					.AddParam("height")
					.AddParam("radii")
					.SetCallback((context, p) =>
					{
						var x = context.GetLocalFloat("x");
						var y = context.GetLocalFloat("y");
						var width = context.GetLocalFloat("width");
						var height = context.GetLocalFloat("height");
						var radii = context.GetLocal("radii");

						PathRoundRect(path, x, y, width, height, radii);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.map;
	}


	public static ValMap WrapMatrix(float translateX, float translateY, float scaleX, float scaleY, float skewX, float skewY)
	{
		return new MapBuilder(Runtime.New(MatrixClass))
			.AddProp("a", Utils.Cast(scaleX))
			.AddProp("b", Utils.Cast(skewY))
			.AddProp("c", Utils.Cast(skewX))
			.AddProp("d", Utils.Cast(scaleY))
			.AddProp("e", Utils.Cast(translateX))
			.AddProp("f", Utils.Cast(translateY))
			.map;
	}
	public static SKPathFillType WrapFillRule(string fillRule)
	{
		if (fillRule == "evenodd") return SKPathFillType.Winding;
		else return SKPathFillType.InverseWinding;
	}
	public static ValMap WrapCanvas(SKPaintSurfaceEventArgs e)
	{
		ValMap map = null!;
		float[] lineDash = [];
		SKPath path = new();

		(SKImageInfo, SKCanvas, SKSurface) UnWrap()
		{
			var e = (SKPaintSurfaceEventArgs)map.userData;

			return (e.Info, e.Surface.Canvas, e.Surface);
		}

		(SKColor?, SKShader?) GetStrokeStyle()
		{
			map.TryGetValue("strokeStyle", out Value style);

			if (style is ValString)
			{
				return (SKColor.Parse(style.ToString()), null);
			}
			else
			{
				ValMap m = (ValMap)style;

				if (m.userData is GradientData gradientData)
				{
					return (null, gradientData.Create());
				}
				else
				{
					return (null, ((CanvasPattern)m.userData).Create());
				}
			}
		}

		(SKColor?, SKShader?) GetFillStyle()
		{
			map.TryGetValue("fillStyle", out Value style);

			if (style is ValString)
			{
				return (SKColor.Parse(style.ToString()), null);
			}
			else
			{
				ValMap m = (ValMap)style;

				if (m.userData is GradientData gradientData)
				{
					return (null, gradientData.Create());
				}
				else
				{
					return (null, ((CanvasPattern)m.userData).Create());
				}
			}
		}

		string GetTextBaseline()
		{
			map.TryGetValue("textBaseline", out Value style);

			return style.ToString();
		}

		float GetLineWidth()
		{
			map.TryGetValue("lineWidth", out Value style);

			return style.FloatValue();
		}

		SKPaint GetImagePaint(SKImage image)
		{
			SKPaint paint;
			map.TryGetValue("imageSmoothingEnabled", out Value _isEnabled);

			if (_isEnabled.BoolValue())
			{
				map.TryGetValue("imageSmoothingQuality", out Value _quality);

				var s = _quality.ToString();

				SKFilterQuality quality;

				if (s == "low") quality = SKFilterQuality.Low;
				else if (s == "medium") quality = SKFilterQuality.Medium;
				else quality = SKFilterQuality.High;

				paint = GetBasePaint(null, SKImageFilter.CreateImage(image, image.Info.Rect, image.Info.Rect, quality));
			}
			else
				paint = GetBasePaint(null, null);

			var r = GetFillStyle();

			if (r.Item2 != null) paint.Shader = r.Item2;

			return paint;
		}

		SKStrokeCap GetLineCap()
		{
			map.TryGetValue("lineCap", out Value style);

			string s = style.ToString();

			return s == "butt" ? SKStrokeCap.Butt : s == "round" ? SKStrokeCap.Round : SKStrokeCap.Square;
		}

		SKStrokeJoin GetLineJoin()
		{
			map.TryGetValue("lineJoin", out Value style);

			string s = style.ToString();

			return s == "bevel" ? SKStrokeJoin.Bevel : s == "miter" ? SKStrokeJoin.Miter : SKStrokeJoin.Round;
		}

		float GetMiterLimit()
		{
			map.TryGetValue("miterLimit", out Value style);

			return style.FloatValue();
		}

		float GetLineDashOffset()
		{
			map.TryGetValue("lineDashOffset", out Value style);

			return style.FloatValue();
		}

		float GetTextY(float y, string text)
		{
			return y + GetTextYOffset();
		}

		SKTextAlign GetTextAlign()
		{
			map.TryGetValue("textAlign", out Value style);

			string s = style.ToString();

			if (s == "start" || s == "end")
			{
				var direction = GetDirection();

				if (direction == "inherit" || direction == "ltr")
				{
					if (s == "start") return SKTextAlign.Left;
					else return SKTextAlign.Right;
				}
				else
				{
					if (s == "end") return SKTextAlign.Left;
					else return SKTextAlign.Right;
				}
			}

			return s == "left" ? SKTextAlign.Left : s == "center" ? SKTextAlign.Center : SKTextAlign.Right;
		}

		SKPaint GetBasePaint(SKColor? color, SKImageFilter? input)
		{
			var paint = new SKPaint();

			map.TryGetValue("globalAlpha", out Value globalAlpha);
			map.TryGetValue("globalCompositeOperation", out Value globalCompositeOperation);
			map.TryGetValue("shadowBlur", out Value shadowBlur);
			map.TryGetValue("shadowColor", out Value shadowColor);
			map.TryGetValue("shadowOffsetX", out Value shadowOffsetX);
			map.TryGetValue("shadowOffsetY", out Value shadowOffsetY);

			var operation = globalCompositeOperation.ToString() switch
			{
				"source-over" => SKBlendMode.SrcOver,
				"source-in" => SKBlendMode.SrcIn,
				"source-out" => SKBlendMode.SrcOut,
				"source-atop" => SKBlendMode.SrcATop,
				"destination-over" => SKBlendMode.DstOver,
				"destination-in" => SKBlendMode.DstIn,
				"destination-out" => SKBlendMode.DstOut,
				"destination-atop" => SKBlendMode.DstATop,
				"lighter" => SKBlendMode.Lighten,
				"copy" => SKBlendMode.Src,
				"xor" => SKBlendMode.Xor,
				"multiply" => SKBlendMode.Multiply,
				"screen" => SKBlendMode.Screen,
				"overlay" => SKBlendMode.Overlay,
				"darken" => SKBlendMode.Darken,
				"lighten" => SKBlendMode.Lighten,
				"color-dodge" => SKBlendMode.ColorDodge,
				"color-burn" => SKBlendMode.ColorBurn,
				"hard-light" => SKBlendMode.HardLight,
				"soft-light" => SKBlendMode.SoftLight,
				"difference" => SKBlendMode.Difference,
				"exclusion" => SKBlendMode.Exclusion,
				"hue" => SKBlendMode.Hue,
				"saturation" => SKBlendMode.Saturation,
				"color" => SKBlendMode.Color,
				"luminosity" => SKBlendMode.Luminosity,
				_ => SKBlendMode.SrcOver, // Default to 'source-over'
			};

			paint.ImageFilter = SKImageFilter.CreateDropShadow(
				shadowOffsetX.FloatValue(),
				shadowOffsetY.FloatValue(),
				shadowBlur.FloatValue(),
				shadowBlur.FloatValue(),
				SKColor.Parse(shadowColor.ToString()),
				input
			);
			paint.BlendMode = operation;

			if (color != null)
			{
				var c = new SKColor(color.Value.Red, color.Value.Green, color.Value.Blue, (byte)(color.Value.Alpha * globalAlpha.FloatValue() * 255 / 2 / 255));

				paint.Color = c;
			}

			return paint;
		}

		string GetDirection()
		{
			map.TryGetValue("direction", out Value style);

			return style.ToString();
		}

		float GetLetterSpacing()
		{
			map.TryGetValue("letterSpacing", out Value style);

			return style.ToString().Replace("px", "").ToFloat();
		}

		string GetFontKerning()
		{
			map.TryGetValue("fontKerning", out Value style);

			return style.ToString();
		}

		SKPaint GetTextPaint(SKPaint paint)
		{
			var font = GetFont();
			paint.TextSize = font.Size;
			paint.Typeface = font.Typeface;
			paint.TextAlign = GetTextAlign();
			return paint;
		}

		SKFontStyleWidth GetFontStretch()
		{
			map.TryGetValue("fontStretch", out Value style);

			string s = style.ToString();

			return s switch
			{
				"ultra-condensed" => SKFontStyleWidth.UltraCondensed,
				"extra-condensed" => SKFontStyleWidth.UltraExpanded,
				"condensed" => SKFontStyleWidth.Condensed,
				"semi-condensed" => SKFontStyleWidth.SemiCondensed,
				"normal" => SKFontStyleWidth.Normal,
				"semi-expanded" => SKFontStyleWidth.SemiExpanded,
				"expanded" => SKFontStyleWidth.Expanded,
				"extra-expanded" => SKFontStyleWidth.ExtraCondensed,
				"ultra-expanded" => SKFontStyleWidth.UltraExpanded,
				_ => SKFontStyleWidth.Normal,// Fallback to normal
			};
		}

		string GetFontVariantCaps()
		{
			map.TryGetValue("fontVariantCaps", out Value style);

			return style.ToString();
		}

		string GetTextRendering()
		{
			map.TryGetValue("textRendering", out Value style);

			return style.ToString();
		}

		float GetWordSpacing()
		{
			map.TryGetValue("wordSpacing", out Value style);

			return style.ToString().Replace("px", "").ToFloat();
		}

		SKPaint GetLinePaint(SKColor? color)
		{
			var paint = GetBasePaint(color, null);

			paint.StrokeWidth = GetLineWidth();
			paint.StrokeCap = GetLineCap();
			paint.StrokeJoin = GetLineJoin();
			paint.StrokeMiter = GetMiterLimit();
			paint.PathEffect = SKPathEffect.CreateDash([.. lineDash.Select(x => x + GetLineDashOffset())], 0);
			return paint;
		}

		SKPaint GetFillPathPaint()
		{
			var r = GetFillStyle();
			var paint = GetLinePaint(r.Item1);

			paint.Shader = r.Item2;
			paint.IsStroke = false;

			return paint;
		}

		SKPaint GetStrokePathPaint()
		{
			var r = GetStrokeStyle();
			var paint = GetLinePaint(r.Item1);

			paint.Shader = r.Item2;
			paint.IsStroke = true;

			return paint;
		}

		float GetTextYOffset()
		{
			string s = GetTextBaseline();
			var font = GetFont();

			SKFontMetrics textBounds = font.Metrics;

			float offsetY = 0;

			offsetY = s switch
			{
				"top" => textBounds.Top,
				"middle" => textBounds.Top + (textBounds.CapHeight / 2),
				"alphabetic" => textBounds.Bottom * 2,// Alphabetic baseline is generally similar to the bottom
				"hanging" => textBounds.Bottom,// Similar to alphabetic for many fonts
				_ => 0,// Bottom left means no additional offset
			};
			return offsetY;
		}

		SKFont GetFont()
		{
			map.TryGetValue("font", out Value font);

			string[] s = font.ToString().Split(" ");

			return new SKFont
			{
				Size = s[0].Replace("px", "").ToInt(),
				Typeface = SKTypeface.FromFamilyName(Strings.Join(s.Skip(1).ToArray()), SKFontStyleWeight.Normal, GetFontStretch(), SKFontStyleSlant.Upright),
				Subpixel = true,

			};
		}
		map = new MapBuilder(Runtime.New(CanvasClass))
			.AddProp("filter", Utils.Cast("none"))
			.AddProp("strokeStyle", Utils.Cast("#000"))
			.AddProp("fillStyle", Utils.Cast("#000"))
			.AddProp("imageSmoothingEnabled", Utils.Cast(true))
			.AddProp("imageSmoothingQuality", Utils.Cast("low"))
			.AddProp("shadowBlur", Utils.Cast(0))
			.AddProp("shadowColor", Utils.Cast("#000"))
			.AddProp("shadowOffsetX", Utils.Cast(0))
			.AddProp("shadowOffsetY", Utils.Cast(0))
			.AddProp("globalAlpha", Utils.Cast(1.0))
			.AddProp("globalCompositeOperation", Utils.Cast("source-over"))
			.AddProp("font", Utils.Cast("12px Arial"))
			.AddProp("textAlign", Utils.Cast("start"))
			.AddProp("textBaseline", Utils.Cast("alphabetic"))
			.AddProp("direction", Utils.Cast("inherit"))
			.AddProp("lineCap", Utils.Cast("butt"))
			.AddProp("lineJoin", Utils.Cast("miter"))
			.AddProp("miterLimit", Utils.Cast(10))
			.AddProp("lineWidth", Utils.Cast(1))
			.AddProp("lineDashOffset", Utils.Cast(0))
			.AddProp("letterSpacing", Utils.Cast("0px"))
			.AddProp("fontKerning", Utils.Cast("auto"))
			.AddProp("fontStretch", Utils.Cast("normal"))
			.AddProp("fontVariantCaps", Utils.Cast("normal"))
			.AddProp("textRendering", Utils.Cast("auto"))
			.AddProp("wordSpacing", Utils.Cast("0px"))
			.AddProp("clip",
				new FunctionBuilder("clip")
					.AddParam("arg1")
					.AddParam("arg2")
					.SetCallback((context, _) =>
					{
						SKPath p;
						SKPathFillType o = SKPathFillType.Winding;

						if (context.args.Count == 0)
						{
							p = path;
						}
						else if (context.args.Count == 1)
						{
							var v = context.GetLocal("arg1");

							if (v is ValMap valMap)
							{
								p = (SKPath)valMap.userData;
							}
							else
							{
								p = path;
								o = WrapFillRule(v.ToString());
							}
						}
						else
						{
							p = (SKPath)((ValMap)context.GetLocal("arg1")).userData;
							o = WrapFillRule(context.GetLocalString("arg2"));
						}

						p = new(p)
						{
							FillType = o
						};

						UnWrap().Item2.ClipPath(p);
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("isPointInPath",
				new FunctionBuilder("isPointInPath")
					.AddParam("arg1")
					.AddParam("arg2")
					.AddParam("arg3")
					.AddParam("arg4")
					.SetCallback((context, _) =>
					{
						if (context.args.Count == 2)
						{
							var x = context.GetLocalFloat("arg1");
							var y = context.GetLocalFloat("arg2");

							return new Intrinsic.Result(Utils.Cast(path.Contains(x, y)));
						}
						else if (context.args.Count == 3)
						{
							if (context.args.ToArray()[0] is ValNumber)
							{
								var x = context.GetLocalFloat("arg1");
								var y = context.GetLocalFloat("arg2");
								var fillType = WrapFillRule(context.GetLocalString("arg3"));
								var p = new SKPath(path)
								{
									FillType = fillType
								};

								return new Intrinsic.Result(Utils.Cast(p.Contains(x, y)));
							}
							else
							{
								var x = context.GetLocalFloat("arg2");
								var y = context.GetLocalFloat("arg3");
								var p = new SKPath((SKPath)((ValMap)context.GetLocal("arg1")).userData);

								return new Intrinsic.Result(Utils.Cast(p.Contains(x, y)));
							}
						}
						else
						{
							var x = context.GetLocalFloat("arg2");
							var y = context.GetLocalFloat("arg3");
							var fillType = WrapFillRule(context.GetLocalString("arg4"));
							var p = new SKPath((SKPath)((ValMap)context.GetLocal("arg1")).userData)
							{
								FillType = fillType
							};

							return new Intrinsic.Result(Utils.Cast(p.Contains(x, y)));
						}
					})
					.Function
			)
			.AddProp("rotate",
				new FunctionBuilder("rotate")
					.AddParam("angle")
					.SetCallback((context, p) =>
					{
						UnWrap().Item2.RotateRadians(context.GetLocalFloat("angle"));
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("scale",
				new FunctionBuilder("scale")
					.AddParam("x")
					.AddParam("y")
					.SetCallback((context, p) =>
					{
						UnWrap().Item2.Scale(context.GetLocalFloat("x"), context.GetLocalFloat("y"));
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("translate",
				new FunctionBuilder("translate")
					.AddParam("x")
					.AddParam("y")
					.SetCallback((context, p) =>
					{
						UnWrap().Item2.Translate(context.GetLocalFloat("x"), context.GetLocalFloat("y"));
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("drawImage",
				new FunctionBuilder("drawImage")
					.AddParam("image")
					.AddParam("arg2")
					.AddParam("arg3")
					.AddParam("arg4")
					.AddParam("arg5")
					.AddParam("arg6")
					.AddParam("arg7")
					.AddParam("arg8")
					.AddParam("arg9")
					.SetCallback((context, p) =>
					{
						SKImage image = (SKImage)((ValMap)context.GetLocal("image")).userData;
						var canvas = UnWrap().Item2;
						var paint = GetImagePaint(image);

						if (context.args.Count == 3)
						{
							//@DRAW
							canvas.DrawImage(image, context.GetLocalFloat("arg2"), context.GetLocalFloat("arg3"), paint);
						}
						else if (context.args.Count == 5)
						{
							//@DRAW
							canvas.DrawImage(image, SKRect.Create(
								context.GetLocalFloat("arg2"),
								context.GetLocalFloat("arg3"),
								context.GetLocalFloat("arg4"),
								context.GetLocalFloat("arg5")
							), paint);
						}
						else
						{
							//@DRAW
							canvas.DrawImage(image, SKRect.Create(
								context.GetLocalFloat("arg2"),
								context.GetLocalFloat("arg3"),
								context.GetLocalFloat("arg4"),
								context.GetLocalFloat("arg5")
							), SKRect.Create(
								context.GetLocalFloat("arg6"),
								context.GetLocalFloat("arg7"),
								context.GetLocalFloat("arg8"),
								context.GetLocalFloat("arg9")
							));
						}

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("beginPath",
				new FunctionBuilder("beginPath")
					.SetCallback((context, p) =>
					{
						path = new();
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("closePath",
				new FunctionBuilder("closePath")
					.SetCallback((context, p) =>
					{
						path.Close();
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("fill",
				new FunctionBuilder("fill")
					.SetCallback((context, p) =>
					{
						var v = context.GetLocal("path");
						//@DRAW
						UnWrap().Item2.DrawPath(v == null ? path : (SKPath)((ValMap)v).userData, GetFillPathPaint());
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("stroke",
				new FunctionBuilder("stroke")
					.AddParam("path")
					.SetCallback((context, p) =>
					{
						var v = context.GetLocal("path");
						//@DRAW
						UnWrap().Item2.DrawPath(v == null ? path : (SKPath)((ValMap)v).userData, GetStrokePathPaint());
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("moveTo",
				new FunctionBuilder("moveTo")
					.AddParam("x")
					.AddParam("y")
					.SetCallback((context, p) =>
					{
						path.MoveTo(context.GetLocalFloat("x"), context.GetLocalFloat("y"));
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("lineTo",
				new FunctionBuilder("lineTo")
					.AddParam("x")
					.AddParam("y")
					.SetCallback((context, p) =>
					{
						path.LineTo(context.GetLocalFloat("x"), context.GetLocalFloat("y"));
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("bezierCurveTo",
				new FunctionBuilder("bezierCurveTo")
					.AddParam("x1")
					.AddParam("y1")
					.AddParam("x2")
					.AddParam("y2")
					.AddParam("x3")
					.AddParam("y3")
					.SetCallback((context, p) =>
					{
						path.CubicTo(
							context.GetLocalFloat("x1"),
							context.GetLocalFloat("y1"),
							context.GetLocalFloat("x2"),
							context.GetLocalFloat("y2"),
							context.GetLocalFloat("x3"),
							context.GetLocalFloat("y3")
						);
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("quadraticCurveTo",
				new FunctionBuilder("quadraticCurveTo")
					.AddParam("x1")
					.AddParam("y1")
					.AddParam("x2")
					.AddParam("y2")
					.SetCallback((context, p) =>
					{
						path.QuadTo(
							context.GetLocalFloat("x1"),
							context.GetLocalFloat("y1"),
							context.GetLocalFloat("x2"),
							context.GetLocalFloat("y2")
						);
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("arc",
				new FunctionBuilder("arc")
					.AddParam("x")
					.AddParam("y")
					.AddParam("radius")
					.AddParam("startAngle")
					.AddParam("endAngle")
					.AddParam("counterClockwise", Utils.Cast(true))
					.SetCallback((context, p) =>
					{
						var x = context.GetLocalFloat("x");
						var y = context.GetLocalFloat("y");
						var startAngle = NormalizeAngle(context.GetLocalFloat("startAngle"));
						var endAngle = NormalizeAngle(context.GetLocalFloat("endAngle"));
						var counterClockwise = context.GetLocalBool("counterClockwise");
						var radius = context.GetLocalFloat("radius");

						PathArc(path, x, y, radius, startAngle, endAngle, counterClockwise);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("arcTo",
				new FunctionBuilder("arcTo")
					.AddParam("x1")
					.AddParam("y1")
					.AddParam("x2")
					.AddParam("y2")
					.AddParam("radius")
					.SetCallback((context, p) =>
					{
						var x1 = context.GetLocalFloat("x1");
						var y1 = context.GetLocalFloat("y1");
						var x2 = context.GetLocalFloat("x2");
						var y2 = context.GetLocalFloat("y2");
						var radius = context.GetLocalFloat("radius");

						path.ArcTo(x1, y1, x2, y2, radius);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("rect",
				new FunctionBuilder("rect")
					.AddParam("x")
					.AddParam("y")
					.AddParam("width")
					.AddParam("height")
					.SetCallback((context, p) =>
					{
						var x = context.GetLocalFloat("x");
						var y = context.GetLocalFloat("y");
						var width = context.GetLocalFloat("width");
						var height = context.GetLocalFloat("height");

						path.AddRect(SKRect.Create(x, y, width, height));

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("roundRect",
				new FunctionBuilder("roundRect")
					.AddParam("x")
					.AddParam("y")
					.AddParam("width")
					.AddParam("height")
					.AddParam("radii")
					.SetCallback((context, p) =>
					{
						var x = context.GetLocalFloat("x");
						var y = context.GetLocalFloat("y");
						var width = context.GetLocalFloat("width");
						var height = context.GetLocalFloat("height");
						var radii = context.GetLocal("radii");

						PathRoundRect(path, x, y, width, height, radii);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("save",
				new FunctionBuilder("save")
					.SetCallback((context, p) =>
					{
						UnWrap().Item2.Save();
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("restore",
				new FunctionBuilder("restore")
					.SetCallback((context, p) =>
					{
						UnWrap().Item2.Restore();
						return Intrinsic.Result.Null;
					})
					.Function
			)
			/*.AddProp("reset",
				new FunctionBuilder("reset")
					.SetCallback((context, p) =>
					{
						var e = (SKPaintSurfaceEventArgs)map.userData;

						map.userData = new SKPaintSurfaceEventArgs()
						return Intrinsic.Result.Null;
					})
					.Function
			)*/
			.AddProp("setLineDash",
				new FunctionBuilder("setLineDash")
					.AddParam("intervals")
					.SetCallback((context, p) =>
					{
						lineDash = (float[])((ValList)context.GetLocal("intervals")).values.ToArray().Select((x) => x.FloatValue());
						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("getLineDash",
				new FunctionBuilder("getLineDash")
					.SetCallback((context, p) =>
					{
						return new Intrinsic.Result(new ValList([.. lineDash.Select(x => (Value)new ValNumber(x))]));
					})
					.Function
			)
			.AddProp("clearRect",
				new FunctionBuilder("clearRect")
					.AddParam("x")
					.AddParam("y")
					.AddParam("width")
					.AddParam("height")
					.SetCallback((context, p) =>
					{
						var canvas = UnWrap().Item2;

						canvas.DrawRect(
							context.GetLocalFloat("x"),
							context.GetLocalFloat("y"),
							context.GetLocalFloat("width"),
							context.GetLocalFloat("height"),
							new SKPaint
							{
								Style = SKPaintStyle.Fill,
								Color = SKColor.Empty
							}
						);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("fillRect",
				new FunctionBuilder("fillRect")
					.AddParam("x")
					.AddParam("y")
					.AddParam("width")
					.AddParam("height")
					.SetCallback((context, p) =>
					{
						var canvas = UnWrap().Item2;

						canvas.DrawRect(
							context.GetLocalFloat("x"),
							context.GetLocalFloat("y"),
							context.GetLocalFloat("width"),
							context.GetLocalFloat("height"),
							GetFillPathPaint()
						);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("strokeRect",
				new FunctionBuilder("strokeRect")
					.AddParam("x")
					.AddParam("y")
					.AddParam("width")
					.AddParam("height")
					.SetCallback((context, p) =>
					{
						var canvas = UnWrap().Item2;

						canvas.DrawRect(
							context.GetLocalFloat("x"),
							context.GetLocalFloat("y"),
							context.GetLocalFloat("width"),
							context.GetLocalFloat("height"),
							GetStrokePathPaint()
						);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("fillText",
				new FunctionBuilder("fillText")
					.AddParam("text")
					.AddParam("x")
					.AddParam("y")
					.AddParam("maxWidth")
					.SetCallback((context, p) =>
					{
						var canvas = UnWrap().Item2;
						//var letterSpacing = GetLetterSpacing();
						//var wordSpacing = GetWordSpacing(); unsupported for now :)
						var text = context.GetLocalString("text");
						var x = context.GetLocalFloat("x");
						var y = GetTextY(context.GetLocalFloat("y"), text);
						var font = GetFont();
						var paint = GetTextPaint(GetFillPathPaint());
						/*
						float xOffset = 0;

						foreach (char c in text)
						{
							canvas.DrawText(c.ToString(), x + xOffset, y, font, paint);

							xOffset += font.MeasureText([c], paint);

							if (c == ' ')
							{
								xOffset += wordSpacing;
							}
							else
							{
								xOffset += letterSpacing;
							}
						}*/

						canvas.DrawText(text, x, y, paint);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("strokeText",
				new FunctionBuilder("strokeText")
					.AddParam("text")
					.AddParam("x")
					.AddParam("y")
					.AddParam("maxWidth")
					.SetCallback((context, p) =>
					{
						var canvas = UnWrap().Item2;
						var text = context.GetLocalString("text");
						var x = context.GetLocalFloat("x");
						var y = GetTextY(context.GetLocalFloat("y"), text);
						var paint = GetTextPaint(GetStrokePathPaint());

						canvas.DrawText(text, x, y, paint);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("getTransform",
				new FunctionBuilder("getTransform")
					.SetCallback((context, p) =>
					{
						var matrix = UnWrap().Item2.TotalMatrix;

						return new Intrinsic.Result(
							WrapMatrix(
								matrix.TransX,
								matrix.TransY,
								matrix.ScaleX,
								matrix.ScaleY,
								matrix.SkewX,
								matrix.SkewY
							)
						);
					})
					.Function
			)
			.AddProp("transform",
				new FunctionBuilder("transform")
					.AddParam("matrix")
					.SetCallback((context, p) =>
					{
						var canvas = UnWrap().Item2;
						var matrix = canvas.TotalMatrix;

						var a11 = matrix.ScaleX;
						var a21 = matrix.SkewY;
						var a12 = matrix.SkewX;
						var a22 = matrix.ScaleY;
						var a13 = matrix.TransX;
						var a23 = matrix.TransY;

						var b = (ValMap)context.GetLocal("matrix");

						b.TryGetValue("a", out Value _b11);
						b.TryGetValue("b", out Value _b12);
						b.TryGetValue("c", out Value _b21);
						b.TryGetValue("d", out Value _b22);
						b.TryGetValue("e", out Value _b13);
						b.TryGetValue("f", out Value _b23);

						var b11 = _b11.FloatValue();
						var b12 = _b12.FloatValue();
						var b21 = _b21.FloatValue();
						var b22 = _b22.FloatValue();
						var b13 = _b13.FloatValue();
						var b23 = _b23.FloatValue();
						var b31 = 0;
						var b32 = 0;
						var b33 = 1;

						var c11 = a11 * b11 + a12 * b21 + a13 * b31;
						var c12 = a11 * b12 + a12 * b22 + a13 * b32;
						var c13 = a11 * b13 + a12 * b23 + a13 * b33;

						var c21 = a21 * b11 + a22 * b21 + a23 * b31;
						var c22 = a21 * b12 + a22 * b22 + a23 * b32;
						var c23 = a21 * b13 + a22 * b23 + a23 * b33;

						canvas.SetMatrix(
							new SKMatrix
							{
								ScaleX = c13,
								SkewY = c23,
								SkewX = c11,
								ScaleY = c22,
								TransX = c12,
								TransY = c21
							}
						);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("resetTransform",
				new FunctionBuilder("resetTransform")
					.SetCallback((context, p) =>
					{
						UnWrap().Item2.ResetMatrix();

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("setTransform",
				new FunctionBuilder("setTransform")
					.AddParam("matrix")
					.SetCallback((context, p) =>
					{
						var b = (ValMap)context.GetLocal("matrix");

						b.TryGetValue("a", out Value _b11);
						b.TryGetValue("b", out Value _b12);
						b.TryGetValue("c", out Value _b21);
						b.TryGetValue("d", out Value _b22);
						b.TryGetValue("e", out Value _b13);
						b.TryGetValue("f", out Value _b23);

						var b11 = _b11.FloatValue();
						var b12 = _b12.FloatValue();
						var b21 = _b21.FloatValue();
						var b22 = _b22.FloatValue();
						var b13 = _b13.FloatValue();
						var b23 = _b23.FloatValue();

						UnWrap().Item2.SetMatrix(
							new SKMatrix
							{
								ScaleX = b13,
								SkewY = b23,
								SkewX = b11,
								ScaleY = b22,
								TransX = b12,
								TransY = b21
							}
						);

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("measureText",
				new FunctionBuilder("measureText")
					.AddParam("text")
					.SetCallback((context, p) =>
					{
						var canvas = UnWrap().Item2;
						var text = context.GetLocalString("text");
						var paint = GetTextPaint(GetBasePaint(null, null));
						var bounds = new SKRect();
						paint.MeasureText(text, ref bounds);
						SKFontMetrics fontMetrics = paint.FontMetrics;

						float fontBoundingBoxAscent = -bounds.Top;
						float fontBoundingBoxDescent = bounds.Bottom;

						float actualBoundingBoxAscent = -fontMetrics.Ascent;
						float actualBoundingBoxDescent = fontMetrics.Descent;

						float actualBoundingBoxLeft = bounds.Left;
						float actualBoundingBoxRight = bounds.Right;

						float emHeightAscent = paint.TextSize * (fontMetrics.Ascent / (fontMetrics.Ascent - fontMetrics.Descent));
						float emHeightDescent = paint.TextSize * (fontMetrics.Descent / (fontMetrics.Ascent - fontMetrics.Descent));

						float alphabeticBaseline = 0;

						float hangingBaseline = -fontMetrics.Ascent * 0.8f;

						float ideographicBaseline = fontMetrics.Descent * 0.8f;

						return new Intrinsic.Result(
							new MapBuilder(Runtime.New(TextMetricsClass))
								.AddProp("width", Utils.Cast(bounds.Width))
								.AddProp("actualBoundingBoxLeft", Utils.Cast(actualBoundingBoxLeft))
								.AddProp("actualBoundingBoxRight", Utils.Cast(actualBoundingBoxRight))
								.AddProp("actualBoundingBoxAscent", Utils.Cast(actualBoundingBoxAscent))
								.AddProp("actualBoundingBoxDescent", Utils.Cast(actualBoundingBoxDescent))
								.AddProp("fontBoundingBoxAscent", Utils.Cast(fontBoundingBoxAscent))
								.AddProp("fontBoundingBoxDescent", Utils.Cast(fontBoundingBoxDescent))
								.AddProp("emHeightAscent", Utils.Cast(emHeightAscent))
								.AddProp("emHeightDescent", Utils.Cast(emHeightDescent))
								.AddProp("alphabeticBaseline", Utils.Cast(alphabeticBaseline))
								.AddProp("hangingBaseline", Utils.Cast(hangingBaseline))
								.AddProp("ideographicBaseline", Utils.Cast(ideographicBaseline))
								.map
						);
					})
					.Function
			)
			.AddProp("getSize",
				new FunctionBuilder("getSize")
					.SetCallback((context, p) =>
					{
						var imageInfo = UnWrap().Item1;

						return new Intrinsic.Result(
							new MapBuilder()
								.AddProp("width", Utils.Cast(imageInfo.Width))
								.AddProp("height", Utils.Cast(imageInfo.Height))
								.map
						);
					})
					.Function
			)
			.AddProp("createImageData",
				new FunctionBuilder("createImageData")
					.AddParam("width")
					.AddParam("height")
					.AddParam("settings")
					.SetCallback((context, p) =>
					{
						map.TryGetValue("settings", out Value _settings);
						map.TryGetValue("width", out Value _width);
						map.TryGetValue("height", out Value _height);

						var width = _width.IntValue();
						var height = _height.IntValue();
						((ValMap)_settings).TryGetValue("colorSpace", out Value _colorSpace);

						return new Intrinsic.Result(
							CreateImageData(width, height, _colorSpace.ToString())
						);
					})
					.Function
			)
			.AddProp("toImageData",
				new FunctionBuilder("toImageData")
					.SetCallback((context, p) =>
					{
						using var stream = new MemoryStream();
#if __ANDROID__

						UnWrap().Item3.PeekPixels().ToBitmap().Compress(Android.Graphics.Bitmap.CompressFormat.Png, 100, stream);
#else
						UnWrap().Item3.PeekPixels().ToBitmap().Save(stream, System.Drawing.Imaging.ImageFormat.Png);
#endif

						stream.Seek(0, SeekOrigin.Begin);

						using var skBitmap = SKBitmap.Decode(stream.ToArray());
						return new Intrinsic.Result(WrapImageData(skBitmap));
					})
					.Function
			)
			.AddProp("toImage",
				new FunctionBuilder("toImage")
					.SetCallback((context, p) =>
					{
						using var stream = new MemoryStream();
#if __ANDROID__
						UnWrap().Item3.PeekPixels().ToBitmap().Compress(Android.Graphics.Bitmap.CompressFormat.Png, 100, stream);
#else
						UnWrap().Item3.PeekPixels().ToBitmap().Save(stream, System.Drawing.Imaging.ImageFormat.Png);
#endif

						stream.Seek(0, SeekOrigin.Begin);

						using var skImage = SKImage.FromEncodedData(stream.ToArray());
						return new Intrinsic.Result(WrapImage(skImage));
					})
					.Function
			)
			.AddProp("putImageData",
				new FunctionBuilder("putImageData")
					.AddParam("imageData")
					.AddParam("dx")
					.AddParam("dy")
					.AddParam("dirtyX")
					.AddParam("dirtyY")
					.AddParam("dirtyWidth")
					.AddParam("dirtyHeight")
					.SetCallback((context, p) =>
					{
						var canvas = UnWrap().Item2;
						var bitmap = ToSKBitmap((ValMap)context.GetLocal("imageData"));
						var dx = context.GetLocalInt("dx");
						var dy = context.GetLocalInt("dy");

						//@DRAW
						if (context.args.Count == 3) canvas.DrawBitmap(bitmap, new SKPoint(dx, dy));
						else
						{
							var dirtyX = context.GetLocalInt("dirtyX");
							var dirtyY = context.GetLocalInt("dirtyY");
							var dirtyWidth = context.GetLocalInt("dirtyWidth");
							var dirtyHeight = context.GetLocalInt("dirtyHeight");

							//@DRAW
							canvas.DrawBitmap(bitmap, SKRect.Create(dirtyX, dirtyY, dirtyWidth, dirtyHeight), SKRect.Create(dx, dy, dirtyWidth, dirtyHeight));
						}

						return Intrinsic.Result.Null;
					})
					.Function
			)
			.AddProp("createLinearGradient",
				new FunctionBuilder("createLinearGradient")
					.AddParam("x0")
					.AddParam("y0")
					.AddParam("x1")
					.AddParam("y1")
					.SetCallback((context, p) =>
					{
						return new Intrinsic.Result(
							WrapGradient(new LinearGradientData(
								new SKPoint(context.GetLocalFloat("x0"), context.GetLocalFloat("y0")),
								new SKPoint(context.GetLocalFloat("x1"), context.GetLocalFloat("y1"))
							))
						);
					})
					.Function
			)
			.AddProp("createConicGradient",
				new FunctionBuilder("createConicGradient")
					.AddParam("startAngle")
					.AddParam("x")
					.AddParam("y")
					.SetCallback((context, p) =>
					{
						var x = context.GetLocalFloat("x");
						var y = context.GetLocalFloat("y");
						var startAngle = context.GetLocalFloat("startAngle");
						return new Intrinsic.Result(
							WrapGradient(new ConicGradientData(new SKPoint(x, y), startAngle))
						);
					})
					.Function
			)
			.AddProp("createRadialGradient",
				new FunctionBuilder("createRadialGradient")
					.AddParam("x0")
					.AddParam("y0")
					.AddParam("r0")
					.AddParam("x1")
					.AddParam("y1")
					.AddParam("r1")
					.SetCallback((context, p) =>
					{
						return new Intrinsic.Result(
							WrapGradient(new RadialGradientData(
								new SKPoint(context.GetLocalFloat("x0"), context.GetLocalFloat("y0")),
								context.GetLocalFloat("r0"),
								new SKPoint(context.GetLocalFloat("x1"), context.GetLocalFloat("y1")),
								context.GetLocalFloat("r1")
							))
						);
					})
					.Function
			)
			.SetUserData(e)
			.map;

		return map;
	}
}
