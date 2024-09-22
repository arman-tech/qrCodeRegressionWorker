using System.Drawing;
using ZXing;

public class BitmapLuminanceSource : BaseLuminanceSource {
    public BitmapLuminanceSource(Bitmap bitmap) : base(bitmap.Width, bitmap.Height) {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixels = new byte[width * height];

        for (int y = 0; y < height; y++) {
            for (int x = 0; x < width; x++) {
                var color = bitmap.GetPixel(x, y);
                var luminance = (byte)((color.R + color.G + color.B) / 3);
                pixels[y * width + x] = luminance;
            }
        }

        // Set the luminance values in the base class
        for (int i = 0; i < pixels.Length; i++) {
            this.luminances[i] = pixels[i];
        }
    }

    protected BitmapLuminanceSource(int width, int height) : base(width, height) { }

    protected override LuminanceSource CreateLuminanceSource(byte[] newLuminances, int width, int height) {
        return new BitmapLuminanceSource(width, height) {
            luminances = newLuminances
        };
    }
}
