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

        // 영역 편집은 AI가 경계 밖의 조명/색/형태를 충분히 볼 수 있도록
        // 선택 크기의 50%만큼 주변 문맥을 포함한다.
        // 잘라서 편집은 선택 영역 그 자체만 독립 이미지로 전달한다.
        var requestRect = includeContext
            ? Expand(selection, source.PixelWidth, source.PixelHeight, 0.50)
            : selection;

        var cropped = new CroppedBitmap(source, requestRect);
        return new PreparedRegionRequest(
            EncodePng(cropped),
            requestRect,
            selection,
            includeContext);
    }

    /// <summary>
    /// 영역 편집은 원본 전체 이미지에 자연스럽게 합성하고,
    /// 잘라서 편집은 AI가 반환한 독립 이미지를 그대로 PNG로 정규화해 반환한다.
    /// </summary>
    public byte[] ComposeResult(
        string sourceImagePath,
        byte[] editedRegionBytes,
        PreparedRegionRequest request)
    {
        if (!request.IncludeContext)
        {
            // Crop 모드: 원본에 다시 붙이지 않는다.
            return EncodePng(LoadBitmap(editedRegionBytes));
        }

        return ComposeRegionResult(sourceImagePath, editedRegionBytes, request);
    }

    /// <summary>
    /// 영역 편집 결과를 원본 전체 이미지에 합성한다.
    /// 선택 영역 밖은 원본 그대로 유지하며, 선택 영역 가장자리 안쪽에서는
    /// 원본과 편집 결과를 feather blending해 사각형 경계가 드러나지 않게 한다.
    /// </summary>
    private byte[] ComposeRegionResult(
        string sourceImagePath,
        byte[] editedRegionBytes,
        PreparedRegionRequest request)
    {
        var source = ConvertToBgra32(LoadBitmap(sourceImagePath));
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
        var resizedEditedSelection = ResizeTo(
            editedSelection,
            request.SelectionRect.Width,
            request.SelectionRect.Height);
        var edited = ConvertToBgra32(resizedEditedSelection);

        var sourceStride = source.PixelWidth * 4;
        var sourcePixels = new byte[sourceStride * source.PixelHeight];
        source.CopyPixels(sourcePixels, sourceStride, 0);

        var editedStride = edited.PixelWidth * 4;
        var editedPixels = new byte[editedStride * edited.PixelHeight];
        edited.CopyPixels(editedPixels, editedStride, 0);

        var feather = Math.Clamp(
            (int)Math.Round(Math.Min(request.SelectionRect.Width, request.SelectionRect.Height) * 0.06),
            4,
            32);

        for (var y = 0; y < request.SelectionRect.Height; y++)
        {
            for (var x = 0; x < request.SelectionRect.Width; x++)
            {
                var distanceToEdge = Math.Min(
                    Math.Min(x, request.SelectionRect.Width - 1 - x),
                    Math.Min(y, request.SelectionRect.Height - 1 - y));

                // 가장자리에서는 원본 비중이 높고, feather 폭 안쪽으로 갈수록
                // AI 편집 결과가 완전히 적용된다.
                var alpha = Math.Clamp((distanceToEdge + 1) / (double)feather, 0.0, 1.0);
                alpha = alpha * alpha * (3.0 - 2.0 * alpha); // smoothstep

                var sourceX = request.SelectionRect.X + x;
                var sourceY = request.SelectionRect.Y + y;
                var sourceIndex = sourceY * sourceStride + sourceX * 4;
                var editedIndex = y * editedStride + x * 4;

                for (var channel = 0; channel < 3; channel++)
                {
                    sourcePixels[sourceIndex + channel] = (byte)Math.Clamp(
                        (int)Math.Round(
                            sourcePixels[sourceIndex + channel] * (1.0 - alpha) +
                            editedPixels[editedIndex + channel] * alpha),
                        0,
                        255);
                }

                sourcePixels[sourceIndex + 3] = 255;
            }
        }

        var result = BitmapSource.Create(
            source.PixelWidth,
            source.PixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            sourcePixels,
            sourceStride);
        result.Freeze();
        return EncodePng(result);
    }

    private static BitmapSource ResizeTo(BitmapSource source, int width, int height)
    {
        if (source.PixelWidth == width && source.PixelHeight == height)
        {
            return source;
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(source, new Rect(0, 0, width, height));
        }

        var rendered = new RenderTargetBitmap(
            width,
            height,
            96,
            96,
            PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    private static BitmapSource ConvertToBgra32(BitmapSource source)
    {
        if (source.Format == PixelFormats.Bgra32)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(
            source,
            PixelFormats.Bgra32,
            null,
            0);
        converted.Freeze();
        return converted;
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
    Int32Rect SelectionRect,
    bool IncludeContext);
