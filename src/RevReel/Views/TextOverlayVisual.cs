using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RevReel.Models;
using RevReel.Services;
using Drawing = System.Drawing;
using Shapes = System.Windows.Shapes;

namespace RevReel.Views
{
    /// <summary>
    /// Builds the on-screen picture of a text overlay for the GIF maker preview.
    /// The letters are drawn by the same code that draws the GIF, so the preview matches the GIF.
    /// </summary>
    internal static class TextOverlayVisual
    {
        /// <summary>
        /// Returns a canvas with the size of the text (children: grab area, picture, selection frame).
        /// <paramref name="em"/> is the letter size in screen units; <paramref name="dpiScale"/> is the
        /// screen scaling (1.0 at 100%) so the letters stay sharp.
        /// </summary>
        public static Canvas Build(TextOverlay t, double em, double dpiScale, double animTime, out UIElement frame)
        {
            var visual = new Canvas
            {
                Cursor = System.Windows.Input.Cursors.SizeAll,
                Tag = t,
            };

            // Almost invisible, but it makes the whole text box easy to grab with the mouse.
            visual.Children.Add(new Shapes.Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            });

            visual.Children.Add(new Image { Stretch = Stretch.Fill, IsHitTestVisible = false });

            Brush accent = Application.Current.TryFindResource("AccentBrush") as Brush ?? Brushes.Orange;
            var dashed = new Shapes.Rectangle
            {
                Stroke = accent,
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            visual.Children.Add(dashed);

            Refresh(visual, t, em, dpiScale, animTime);
            frame = dashed;
            return visual;
        }

        /// <summary>Draws the text again for the moment <paramref name="animTime"/> (seconds into its animation).</summary>
        public static void Refresh(Canvas visual, TextOverlay t, double em, double dpiScale, double animTime)
        {
            if (visual.Children.Count < 3 || visual.Children[0] is not Shapes.Rectangle hit
                || visual.Children[1] is not Image image || visual.Children[2] is not Shapes.Rectangle dashed)
            {
                return;
            }

            if (dpiScale <= 0)
            {
                dpiScale = 1;
            }

            using Drawing.Bitmap bitmap = TextOverlayRenderer.RenderPreview(
                t, Math.Max(1, em * dpiScale), animTime, out Drawing.SizeF size, out int pad);

            image.Source = ToSource(bitmap);
            image.Width = bitmap.Width / dpiScale;
            image.Height = bitmap.Height / dpiScale;
            Canvas.SetLeft(image, -pad / dpiScale);
            Canvas.SetTop(image, -pad / dpiScale);

            double width = Math.Max(1, size.Width / dpiScale);
            double height = Math.Max(1, size.Height / dpiScale);
            visual.Width = width;
            visual.Height = height;
            hit.Width = width;
            hit.Height = height;
            dashed.Width = width + 12;
            dashed.Height = height + 12;
            Canvas.SetLeft(dashed, -6);
            Canvas.SetTop(dashed, -6);
        }

        internal static BitmapSource ToSource(Drawing.Bitmap bitmap)
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, Drawing.Imaging.ImageFormat.Png);
            stream.Position = 0;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // reads the stream now, so it can be closed
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
