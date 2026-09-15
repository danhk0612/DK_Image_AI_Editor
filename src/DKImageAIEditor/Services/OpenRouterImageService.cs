using System.IO;
using System.Net;
using System.Net.Http;
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
            throw CreateOpenRouterException(response.StatusCode, responseBody);
        }

        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
        {
            throw new OpenRouterImageException(
                response.StatusCode,
                "OpenRouter 응답에 이미지가 없습니다.",
                null,
                null,
                null,
                responseBody);
        }

        var image = data[0];
        var base64 = image.TryGetProperty("b64_json", out var base64Element)
            ? base64Element.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new OpenRouterImageException(
                response.StatusCode,
                "OpenRouter 이미지 데이터가 비어 있습니다.",
                null,
                null,
                null,
                responseBody);
        }

        var mediaType = image.TryGetProperty("media_type", out var mediaTypeElement)
            ? mediaTypeElement.GetString() ?? "image/png"
            : "image/png";

        return new ImageEditResult(Convert.FromBase64String(base64), mediaType);
    }

    private static OpenRouterImageException CreateOpenRouterException(HttpStatusCode statusCode, string responseBody)
    {
        string? providerMessage = null;
        string? providerName = null;
        string? finishReason = null;
        string? blockReason = null;

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.TryGetProperty("message", out var messageElement))
                {
                    providerMessage = messageElement.GetString();
                }

                if (errorElement.TryGetProperty("metadata", out var metadataElement))
                {
                    if (metadataElement.TryGetProperty("provider_name", out var providerElement))
                    {
                        providerName = providerElement.GetString();
                    }

                    if (metadataElement.TryGetProperty("finish_reason", out var finishElement))
                    {
                        finishReason = finishElement.GetString();
                    }

                    if (metadataElement.TryGetProperty("block_reason", out var blockElement))
                    {
                        blockReason = blockElement.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // 구조화되지 않은 응답은 아래 RawResponse로만 보존한다.
        }

        return new OpenRouterImageException(
            statusCode,
            providerMessage,
            providerName,
            finishReason,
            blockReason,
            responseBody);
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

public sealed class OpenRouterImageException : Exception
{
    public OpenRouterImageException(
        HttpStatusCode statusCode,
        string? providerMessage,
        string? providerName,
        string? finishReason,
        string? blockReason,
        string rawResponse)
        : base(providerMessage ?? $"OpenRouter 요청 실패 ({(int)statusCode})")
    {
        StatusCode = statusCode;
        ProviderMessage = providerMessage;
        ProviderName = providerName;
        FinishReason = finishReason;
        BlockReason = blockReason;
        RawResponse = rawResponse;
    }

    public HttpStatusCode StatusCode { get; }
    public string? ProviderMessage { get; }
    public string? ProviderName { get; }
    public string? FinishReason { get; }
    public string? BlockReason { get; }
    public string RawResponse { get; }

    public bool IsImageSafetyBlock =>
        string.Equals(BlockReason, "IMAGE_SAFETY", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(FinishReason, "IMAGE_SAFETY", StringComparison.OrdinalIgnoreCase);
}
