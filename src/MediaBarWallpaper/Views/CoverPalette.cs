using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediaBarWallpaper.Views;

/// <summary>
/// 封面主色提取：对封面缩略采样求平均色，用于媒体条玻璃感渐变背景。
/// </summary>
public static class CoverPalette
{
    /// <summary>提取封面平均色；失败返回默认强调蓝紫。</summary>
    public static Color Extract(ImageSource? source)
    {
        try
        {
            if (source is not BitmapSource bmp) return Default;
            if (bmp.PixelWidth < 4 || bmp.PixelHeight < 4) return Default;

            // 转 BGRA32 便于统一采样
            var fmt = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int stride = fmt.PixelWidth * 4;
            var pixels = new byte[stride * fmt.PixelHeight];
            fmt.CopyPixels(pixels, stride, 0);

            const int step = 7; // 抽稀采样，降低主色提取成本
            long r = 0, g = 0, b = 0, n = 0;
            for (int y = 0; y < fmt.PixelHeight; y += step)
            {
                for (int x = 0; x < fmt.PixelWidth; x += step)
                {
                    int i = y * stride + x * 4;
                    if (i + 2 >= pixels.Length) continue;
                    b += pixels[i];
                    g += pixels[i + 1];
                    r += pixels[i + 2];
                    n++;
                }
            }
            if (n == 0) return Default;

            byte R = (byte)Math.Clamp(r / n, 30, 255);
            byte G = (byte)Math.Clamp(g / n, 30, 255);
            byte B = (byte)Math.Clamp(b / n, 30, 255);
            return Color.FromRgb(R, G, B);
        }
        catch
        {
            return Default;
        }
    }

    public static Color Default => Color.FromRgb(0x81, 0x8C, 0xF8);
}
