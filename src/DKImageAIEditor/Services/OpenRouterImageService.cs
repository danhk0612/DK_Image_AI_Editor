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
            // 구조화되지 않은 응답은 RawResponse로 보존한다.
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
        : base(BuildFriendlyMessage(statusCode, providerMessage, providerName, finishReason, blockReason))
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

    private static string BuildFriendlyMessage(
        HttpStatusCode statusCode,
        string? providerMessage,
        string? providerName,
        string? finishReason,
        string? blockReason)
    {
        var isSafetyBlock =
            string.Equals(blockReason, "IMAGE_SAFETY", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(finishReason, "IMAGE_SAFETY", StringComparison.OrdinalIgnoreCase);

        if (isSafetyBlock)
        {
            return
                "현재 이미지 또는 수정 요청이 모델의 안전정책에 의해 차단되었습니다.\n\n" +
                $"공급자: {providerName ?? "알 수 없음"}\n" +
                $"차단 사유: {blockReason ?? finishReason ?? "IMAGE_SAFETY"}\n\n" +
                "프롬프트 표현을 조정하거나 다른 이미지 편집 모델로 다시 시도하세요.";
        }

        if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
        {
            return
                "OpenRouter 인증에 실패했습니다.\n\n" +
                "설정 창의 API Key가 올바른지 확인하세요.";
        }

        if ((int)statusCode == 402)
        {
            return
                "OpenRouter 크레딧 또는 결제 상태 때문에 요청을 처리할 수 없습니다.\n\n" +
                "OpenRouter 계정의 사용 가능 크레딧을 확인하세요.";
        }

        if ((int)statusCode == 429)
        {
            return
                "요청 한도에 도달했거나 모델 공급자가 일시적으로 요청을 제한했습니다.\n\n" +
                "잠시 후 다시 시도하거나 다른 모델을 선택하세요.";
        }

        if ((int)statusCode >= 500)
        {
            return
                "OpenRouter 또는 모델 공급자에서 일시적인 서버 오류가 발생했습니다.\n\n" +
                "잠시 후 다시 시도하세요.";
        }

        var detail = string.IsNullOrWhiteSpace(providerMessage)
            ? "상세 메시지가 제공되지 않았습니다."
            : providerMessage.Trim();

        return
            $"OpenRouter 요청을 처리하지 못했습니다. ({(int)statusCode})\n\n" +
            $"공급자: {providerName ?? "알 수 없음"}\n" +
            $"내용: {detail}";
    }
}
