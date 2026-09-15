using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DKImageAIEditor.Services;

public sealed class ImageRegionService
{
    public PreparedRegionRequest PrepareRequest(
        string sourceImagePath,
        Int32Rect selection,
        bool includeContext)
    {
        var source = LoadBitmap(sourceImagePath);
        ValidateSelection(selection, source.PixelWidth, source.PixelHeight);

        var requestRect = includeContext
            ? Expand(selection, source.PixelWidth, source.PixelHeight, 0.25)
            : selection;

        var cropped = new CroppedBitmap(source, requestRect);
        return new PreparedRegionRequest(
            EncodePng(cropped),
            requestRect,
            selection);
    }

    public byte[] ComposeResult(
        string sourceImagePath,
        byte[] editedRegionBytes,
        PreparedRegionRequest request)
    {
        var source = LoadBitmap(sourceImagePath);
        var editedRegion = LoadBitmap(editedRegionBytes);

        var relativeSelection = new Int32Rect(
            request.SelectionRect.X - request.RequestRect.X,
            request.SelectionRect.Y - request.RequestRect.Y,
            request.SelectionRect.Width,
            request.SelectionRect.Height);

        var scaleX = editedRegion.PixelWidth / (double)request.RequestRect.Width;
        var scaleY = editedRegion.PixelHeight / (double)request.RequestRect.Height;

        var resultSelection = new Int32Rect(
            Clamp((int)Math.Round(relativeSelection.X * scaleX), 0, editedRegion.PixelWidth - 1),
            Clamp((int)Math.Round(relativeSelection.Y * scaleY), 0, editedRegion.PixelHeight - 1),
            Math.Max(1, (int)Math.Round(relativeSelection.Width * scaleX)),
            Math.Max(1, (int)Math.Round(relativeSelection.Height * scaleY)));

        if (resultSelection.X + resultSelection.Width > editedRegion.PixelWidth)
        {
            resultSelection.Width = editedRegion.PixelWidth - resultSelection.X;
        }

        if (resultSelection.Y + resultSelection.Height > editedRegion.PixelHeight)
        {
            resultSelection.Height = editedRegion.PixelHeight - resultSelection.Y;
        }

        var editedSelection = new CroppedBitmap(editedRegion, resultSelection);

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            context.DrawImage(
                editedSelection,
                new Rect(
                    request.SelectionRect.X,
                    request.SelectionRect.Y,
                    request.SelectionRect.Width,
                    request.SelectionRect.Height));
        }

        var rendered = new RenderTargetBitmap(
            source.PixelWidth,
            source.PixelHeight,
            96,
            96,
            PixelFormats.Pbgra32);
        rendered.Render(visual);

        return EncodePng(rendered);
    }

    private static BitmapSource LoadBitmap(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static BitmapSource LoadBitmap(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static byte[] EncodePng(BitmapSource bitmap)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static Int32Rect Expand(Int32Rect rect, int imageWidth, int imageHeight, double fraction)
    {
        var expandX = Math.Max(1, (int)Math.Round(rect.Width * fraction));
        var expandY = Math.Max(1, (int)Math.Round(rect.Height * fraction));

        var left = Math.Max(0, rect.X - expandX);
        var top = Math.Max(0, rect.Y - expandY);
        var right = Math.Min(imageWidth, rect.X + rect.Width + expandX);
        var bottom = Math.Min(imageHeight, rect.Y + rect.Height + expandY);

        return new Int32Rect(left, top, right - left, bottom - top);
    }

    private static void ValidateSelection(Int32Rect selection, int width, int height)
    {
        if (selection.Width < 2 || selection.Height < 2 ||
            selection.X < 0 || selection.Y < 0 ||
            selection.X + selection.Width > width ||
            selection.Y + selection.Height > height)
        {
            throw new InvalidOperationException("선택 영역이 이미지 범위를 벗어났습니다. 영역을 다시 선택하세요.");
        }
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }
}

public sealed record PreparedRegionRequest(
    byte[] RequestImageBytes,
    Int32Rect RequestRect,
    Int32Rect SelectionRect);
