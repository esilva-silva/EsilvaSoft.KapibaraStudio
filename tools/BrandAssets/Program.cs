using System.Xml.Linq;
using SkiaSharp;
var root = args[0];
var assets = Path.Combine(root, "src/EsilvaSoft.KapibaraStudio.Desktop/Assets");
Directory.CreateDirectory(assets);
var resources = XDocument.Load(Path.Combine(assets, "Brand.axaml"));
XNamespace avalonia = "https://github.com/avaloniaui";
XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
var paths = resources.Descendants(avalonia + "GeometryDrawing")
    .Select(p => (Path: (string)p.Attribute("Geometry")!, Color: ((string)p.Attribute("Brush")!).Replace("{StaticResource BrandGradient}", "gradient", StringComparison.Ordinal))).ToArray();
foreach (var geometry in resources.Descendants(avalonia + "StreamGeometry"))
{
    var name = ((string)geometry.Attribute(xaml + "Key")!).Replace("Icon", "", StringComparison.Ordinal).ToLowerInvariant();
    File.WriteAllText(Path.Combine(assets, "Icons", name + ".svg"), $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.75\" stroke-linejoin=\"round\" stroke-linecap=\"round\"><path d=\"{geometry.Value}\"/></svg>");
}
string Svg(string body, int width = 64, int height = 64) => $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><defs><linearGradient id=\"brand\" x2=\"1\" y2=\"1\"><stop stop-color=\"#22B8FF\"/><stop offset=\".5\" stop-color=\"#397BFF\"/><stop offset=\"1\" stop-color=\"#7C3AED\"/></linearGradient></defs>{body}</svg>";
var symbol = string.Concat(paths.Select(p => $"<path d=\"{p.Path}\" fill=\"{(p.Color == "gradient" ? "url(#brand)" : p.Color)}\"/>"));
File.WriteAllText(Path.Combine(assets, "slop-symbol.svg"), Svg(symbol));
foreach (var (theme, color) in new[] { ("light", "#162033"), ("dark", "#F1F5F9") })
    File.WriteAllText(Path.Combine(assets, $"slop-logo-{theme}.svg"), Svg(symbol + $"<text x=\"76\" y=\"32\" font-family=\"Inter, sans-serif\" font-size=\"26\" font-weight=\"700\" fill=\"{color}\">kapibara studio</text><text x=\"77\" y=\"51\" font-family=\"Inter, sans-serif\" font-size=\"12\" fill=\"{color}\">EsilvaSoft · IDE para MongoDB</text>", 310));
var images = new List<(int Size, byte[] Data)>();
foreach (var size in new[] {16, 24, 32, 48, 64, 128, 256, 512})
{
    using var bitmap = new SKBitmap(size, size);
    using var canvas = new SKCanvas(bitmap);
    canvas.Clear(SKColors.Transparent); canvas.Scale(size / 64f);
    foreach (var (data, color) in paths)
    {
        using var path = SKPath.ParseSvgPathData(data);
        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, StrokeWidth = 2 };
        if (color == "gradient") paint.Shader = SKShader.CreateLinearGradient(new SKPoint(path.Bounds.Left, path.Bounds.Top), new SKPoint(path.Bounds.Right, path.Bounds.Bottom), new[] { SKColor.Parse("#22B8FF"), SKColor.Parse("#397BFF"), SKColor.Parse("#7C3AED") }, new float[] {0, .5f, 1}, SKShaderTileMode.Clamp);
        else paint.Color = SKColor.Parse(color);
        canvas.DrawPath(path, paint);
    }
    using var image = SKImage.FromBitmap(bitmap);
    using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
    var bytes = encoded.ToArray();
    File.WriteAllBytes(Path.Combine(assets, $"slop-icon-{size}.png"), bytes);
    if (size <= 256) images.Add((size, bytes));
}
using var stream = File.Create(Path.Combine(assets, "kapibara-studio.ico"));
using var writer = new BinaryWriter(stream);
writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)images.Count);
var offset = 6 + 16 * images.Count;
foreach (var (size, bytes) in images) { writer.Write((byte)(size == 256 ? 0 : size)); writer.Write((byte)(size == 256 ? 0 : size)); writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(bytes.Length); writer.Write(offset); offset += bytes.Length; }
foreach (var (_, bytes) in images) writer.Write(bytes);

