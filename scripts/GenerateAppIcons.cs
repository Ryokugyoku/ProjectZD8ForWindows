// iOS 版のアプリアイコン（1024 px）から、Windows のパッケージ用のロゴ（Assets/）を全サイズ作ります。
//
// 使い方（リポジトリのルートで）:
//   dotnet run --file scripts/GenerateAppIcons.cs -- Design/AppIcon-1024.png Assets
//
// - 元の画像は、紺の背景に「R の回転計」とシアン・オレンジの信号の線（iOS 版と同じ図柄）。
// - 背景の色を四隅から求め、その色との差から図柄だけを透明な背景に抜き出す（タスクバー用・ロック画面用）。
// - タイル・ストア・スプラッシュは、元と同じ紺の背景に図柄を置く。
// - タスクバーとスタートの一覧（Square44x44Logo の targetsize-*_altform-unplated）は、紺の角丸の板に図柄を置く。
//   Windows 11 のアプリアイコンと同じく、それ自体が形を持つアイコンにするため。
#:package System.Drawing.Common@10.0.0
#:property TargetFramework=net10.0-windows

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: dotnet run --file scripts/GenerateAppIcons.cs -- <1024px の PNG> <出力フォルダ>");
    return 2;
}

var outputDirectory = Directory.CreateDirectory(args[1]).FullName;
using var source = new Bitmap(args[0]);
var background = AverageCorners(source);
using var glyph = ExtractGlyph(source, background);
Console.WriteLine($"背景 #{background.R:X2}{background.G:X2}{background.B:X2}、図柄 {glyph.Width}x{glyph.Height}");

foreach (var file in Directory.GetFiles(outputDirectory, "*.png")) File.Delete(file);

int[] scales = [100, 125, 150, 200, 400];

// スタートのタイル（Windows 10）とアプリの一覧の既定。紺の正方形に図柄。
foreach (var scale in scales)
{
    var side = 150 * scale / 100;
    Save(Tile(side, side, glyph, background, 0.62), $"Square150x150Logo.scale-{scale}.png");
}
foreach (var scale in scales)
{
    Save(Tile(310 * scale / 100, 150 * scale / 100, glyph, background, 0.70), $"Wide310x150Logo.scale-{scale}.png");
}

// 小さいアイコン。scale-* は板付き（紺の正方形）、targetsize-* は角丸の板、unplated はタスクバーとスタート用。
foreach (var scale in scales)
{
    var side = 44 * scale / 100;
    Save(Tile(side, side, glyph, background, 0.80), $"Square44x44Logo.scale-{scale}.png");
}
foreach (var size in new[] { 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256 })
{
    Save(RoundedTile(size, glyph, background), $"Square44x44Logo.targetsize-{size}.png");
    Save(RoundedTile(size, glyph, background), $"Square44x44Logo.targetsize-{size}_altform-unplated.png");
    Save(RoundedTile(size, glyph, background), $"Square44x44Logo.targetsize-{size}_altform-lightunplated.png");
}

// ストア・インストーラーのロゴ。
foreach (var scale in scales)
{
    var side = 50 * scale / 100;
    Save(RoundedTile(side, glyph, background), $"StoreLogo.scale-{scale}.png");
}

// スプラッシュ（620x300）。背景はマニフェストの SplashScreen BackgroundColor と同じ紺。
foreach (var scale in new[] { 100, 125, 150, 200, 400 })
{
    Save(Tile(620 * scale / 100, 300 * scale / 100, glyph, background, 0.55), $"SplashScreen.scale-{scale}.png");
}

// ロック画面のバッジ（24x24）。白一色の図柄を透明な背景に。
foreach (var scale in new[] { 100, 125, 150, 200, 400 })
{
    var side = 24 * scale / 100;
    Save(Silhouette(side, glyph), $"LockScreenLogo.scale-{scale}.png");
}

Console.WriteLine($"マニフェストの背景色: #{background.R:X2}{background.G:X2}{background.B:X2}");
return 0;

void Save(Bitmap bitmap, string name)
{
    using (bitmap) bitmap.Save(Path.Combine(outputDirectory, name), ImageFormat.Png);
}

static Color AverageCorners(Bitmap bitmap)
{
    long r = 0, g = 0, b = 0, n = 0;
    const int patch = 24;
    foreach (var (ox, oy) in new[] { (0, 0), (bitmap.Width - patch, 0), (0, bitmap.Height - patch), (bitmap.Width - patch, bitmap.Height - patch) })
    {
        for (var y = oy; y < oy + patch; y++)
        for (var x = ox; x < ox + patch; x++)
        {
            var c = bitmap.GetPixel(x, y);
            r += c.R; g += c.G; b += c.B; n++;
        }
    }
    return Color.FromArgb(255, (int)(r / n), (int)(g / n), (int)(b / n));
}

// 画素 = α·図柄 + (1−α)·背景 とみなし、背景との距離から α を決め、図柄の色を戻す。
static Bitmap ExtractGlyph(Bitmap source, Color background)
{
    const double noise = 14;   // 背景の揺らぎ
    const double solid = 190;  // これ以上離れていれば図柄そのもの（シアン・オレンジは背景から約 270）
    int minX = source.Width, minY = source.Height, maxX = -1, maxY = -1;
    using var full = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < source.Height; y++)
    for (var x = 0; x < source.Width; x++)
    {
        var c = source.GetPixel(x, y);
        double dr = c.R - background.R, dg = c.G - background.G, db = c.B - background.B;
        var distance = Math.Sqrt(dr * dr + dg * dg + db * db);
        var alpha = Math.Clamp((distance - noise) / (solid - noise), 0, 1);
        if (alpha <= 0) continue;
        int Restore(int value, int bg) => (int)Math.Clamp(bg + (value - bg) / alpha, 0, 255);
        full.SetPixel(x, y, Color.FromArgb((int)Math.Round(alpha * 255), Restore(c.R, background.R), Restore(c.G, background.G), Restore(c.B, background.B)));
        if (alpha > 0.1)
        {
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
    }
    var bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    return full.Clone(bounds, PixelFormat.Format32bppArgb);
}

static Graphics HighQuality(Bitmap bitmap)
{
    var g = Graphics.FromImage(bitmap);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.CompositingQuality = CompositingQuality.HighQuality;
    return g;
}

// 図柄を、幅・高さの小さい方の ratio に収まるよう中央に置く。
static void DrawGlyph(Graphics g, Bitmap glyph, RectangleF area, double ratio)
{
    var scale = Math.Min(area.Width * ratio / glyph.Width, area.Height * ratio / glyph.Height);
    var w = (float)(glyph.Width * scale);
    var h = (float)(glyph.Height * scale);
    using var attributes = new ImageAttributes();
    attributes.SetWrapMode(WrapMode.TileFlipXY); // 縁がにじまないように
    g.DrawImage(glyph, new Rectangle((int)Math.Round(area.X + (area.Width - w) / 2), (int)Math.Round(area.Y + (area.Height - h) / 2), (int)Math.Round(w), (int)Math.Round(h)),
        0, 0, glyph.Width, glyph.Height, GraphicsUnit.Pixel, attributes);
}

static Bitmap Tile(int width, int height, Bitmap glyph, Color background, double ratio)
{
    var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using var g = HighQuality(bitmap);
    g.Clear(background);
    DrawGlyph(g, glyph, new RectangleF(0, 0, width, height), ratio);
    return bitmap;
}

static Bitmap RoundedTile(int side, Bitmap glyph, Color background)
{
    var bitmap = new Bitmap(side, side, PixelFormat.Format32bppArgb);
    using var g = HighQuality(bitmap);
    g.Clear(Color.Transparent);
    // 小さいサイズでは余白を減らして図柄を大きく見せる。
    var inset = side <= 24 ? 0f : side / 16f;
    var plate = new RectangleF(inset, inset, side - inset * 2, side - inset * 2);
    var radius = plate.Width * 0.22f;
    using var path = new GraphicsPath();
    path.AddArc(plate.X, plate.Y, radius * 2, radius * 2, 180, 90);
    path.AddArc(plate.Right - radius * 2, plate.Y, radius * 2, radius * 2, 270, 90);
    path.AddArc(plate.Right - radius * 2, plate.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
    path.AddArc(plate.X, plate.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
    path.CloseFigure();
    using (var brush = new SolidBrush(background)) g.FillPath(brush, path);
    DrawGlyph(g, glyph, plate, side <= 32 ? 0.90 : 0.80);
    return bitmap;
}

static Bitmap Silhouette(int side, Bitmap glyph)
{
    using var white = new Bitmap(glyph.Width, glyph.Height, PixelFormat.Format32bppArgb);
    for (var y = 0; y < glyph.Height; y++)
    for (var x = 0; x < glyph.Width; x++)
    {
        white.SetPixel(x, y, Color.FromArgb(glyph.GetPixel(x, y).A, 255, 255, 255));
    }
    var bitmap = new Bitmap(side, side, PixelFormat.Format32bppArgb);
    using var g = HighQuality(bitmap);
    g.Clear(Color.Transparent);
    DrawGlyph(g, white, new RectangleF(0, 0, side, side), 1.0);
    return bitmap;
}
