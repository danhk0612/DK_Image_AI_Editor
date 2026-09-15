using System.IO;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace DKImageAIEditor.Services;

public sealed class OpenRouterImageService
{
    private static readonly Uri ImagesEndpoint = new("https://openrouter.ai/api/v1/images");
    private readonly HttpClient _httpClient;

    public OpenRouterImageService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ImageEditResult> EditImageAsync(
        string apiKey,
        string modelId,
        byte[] sourceImageBytes,
        string sourceMediaType,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var normalizedSource = NormalizeSourceImage(sourceImageBytes, sourceMediaType);
        var sourceDataUrl = $"data:{normalizedSource.MediaType};base64,{Convert.ToBase64String(normalizedSource.ImageBytes)}";
        var payload = new
        {
            model = modelId,
            prompt,
            input_references = new[]
            {
                new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = sourceDataUrl
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ImagesEndpoint)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"OpenRouter 요청 실패 ({(int)response.StatusCode} {response.ReasonPhrase})\n{responseBody}");
        }

        using var document = JsonDocument.Parse(responseBody);
        var data = document.RootElement.GetProperty("data");
        if (data.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("OpenRouter 응답에 이미지가 없습니다.");
        }

        var image = data[0];
        var base64 = image.GetProperty("b64_json").GetString()
                     ?? throw new InvalidOperationException("OpenRouter 이미지 데이터가 비어 있습니다.");
        var mediaType = image.TryGetProperty("media_type", out var mediaTypeElement)
            ? mediaTypeElement.GetString() ?? "image/png"
            : "image/png";

        return new ImageEditResult(Convert.FromBase64String(base64), mediaType);
    }

    private static ImageEditResult NormalizeSourceImage(byte[] imageBytes, string mediaType)
    {
        if (!mediaType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase))
        {
            return new ImageEditResult(imageBytes, mediaType);
        }

        using var sourceStream = new MemoryStream(imageBytes);
        var decoder = BitmapDecoder.Create(
            sourceStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));

        using var outputStream = new MemoryStream();
        encoder.Save(outputStream);
        return new ImageEditResult(outputStream.ToArray(), "image/png");
    }
}

public sealed record ImageEditResult(byte[] ImageBytes, string MediaType);
