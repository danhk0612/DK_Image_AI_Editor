using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DKImageAIEditor.Services;

public sealed class OpenRouterImageService
{
    private static readonly Uri ImagesEndpoint = new("https://openrouter.ai/api/v1/images");
    private static readonly Uri CurrentKeyEndpoint = new("https://openrouter.ai/api/v1/key");
    private static readonly Uri CreditsEndpoint = new("https://openrouter.ai/api/v1/credits");
    private readonly HttpClient _httpClient;

    public OpenRouterImageService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<OpenRouterKeyBalanceStatus> GetKeyBalanceStatusAsync(
        string apiKey,
        CancellationToken cancellationToken = default)
    {
        using var keyRequest = new HttpRequestMessage(HttpMethod.Get, CurrentKeyEndpoint);
        keyRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var keyResponse = await _httpClient.SendAsync(keyRequest, cancellationToken);
        var keyResponseBody = await keyResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!keyResponse.IsSuccessStatusCode)
        {
            throw CreateOpenRouterException(keyResponse.StatusCode, keyResponseBody);
        }

        using var keyDocument = JsonDocument.Parse(keyResponseBody);
        if (!keyDocument.RootElement.TryGetProperty("data", out var keyData))
        {
            throw new InvalidOperationException("OpenRouter API Key 정보를 확인할 수 없습니다.");
        }

        var isManagementKey = TryGetBoolean(keyData, "is_management_key") ?? false;
        var usage = TryGetDouble(keyData, "usage");
        var limit = TryGetDouble(keyData, "limit");
        var limitRemaining = TryGetDouble(keyData, "limit_remaining");
        var limitReset = TryGetString(keyData, "limit_reset");

        if (!isManagementKey)
        {
            return new OpenRouterKeyBalanceStatus(
                false, usage, limit, limitRemaining, limitReset, null, null, null);
        }

        using var creditsRequest = new HttpRequestMessage(HttpMethod.Get, CreditsEndpoint);
        creditsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var creditsResponse = await _httpClient.SendAsync(creditsRequest, cancellationToken);
        var creditsResponseBody = await creditsResponse.Content.ReadAsStringAsync(cancellationToken);
        if (!creditsResponse.IsSuccessStatusCode)
        {
            throw CreateOpenRouterException(creditsResponse.StatusCode, creditsResponseBody);
        }

        using var creditsDocument = JsonDocument.Parse(creditsResponseBody);
        if (!creditsDocument.RootElement.TryGetProperty("data", out var creditsData))
        {
            throw new InvalidOperationException("OpenRouter 크레딧 정보를 확인할 수 없습니다.");
        }

        var totalCredits = TryGetDouble(creditsData, "total_credits");
        var totalUsage = TryGetDouble(creditsData, "total_usage");
        double? creditBalance = totalCredits.HasValue && totalUsage.HasValue
            ? totalCredits.Value - totalUsage.Value
            : null;

        return new OpenRouterKeyBalanceStatus(
            true, usage, limit, limitRemaining, limitReset, creditBalance, totalCredits, totalUsage);
    }

    public async Task<ImageCostEstimate> GetImageCostEstimateAsync(
        string apiKey,
        string modelId,
        double inputMegapixels,
        CancellationToken cancellationToken = default)
    {
        var endpointUri = new Uri($"https://openrouter.ai/api/v1/images/models/{modelId}/endpoints");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpointUri);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new ImageCostEstimate(null, null, false, true, "가격 정보 조회 불가");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("endpoints", out var endpoints) ||
            endpoints.ValueKind != JsonValueKind.Array || endpoints.GetArrayLength() == 0)
        {
            return new ImageCostEstimate(null, null, false, true, "가격 정보 없음");
        }

        var estimates = new List<double>();
        var tokenBased = false;
        var approximate = false;

        foreach (var endpoint in endpoints.EnumerateArray())
        {
            if (!endpoint.TryGetProperty("pricing", out var pricing) || pricing.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var endpointCost = 0.0;
            var hasKnownCost = false;
            var endpointUnknown = false;

            foreach (var price in pricing.EnumerateArray())
            {
                var unit = TryGetString(price, "unit") ?? string.Empty;
                var billable = TryGetString(price, "billable") ?? string.Empty;
                var cost = TryGetDouble(price, "cost_usd");
                if (!cost.HasValue)
                {
                    endpointUnknown = true;
                    continue;
                }

                if (unit.Equals("image", StringComparison.OrdinalIgnoreCase))
                {
                    endpointCost += cost.Value;
                    hasKnownCost = true;
                }
                else if (unit.Equals("megapixel", StringComparison.OrdinalIgnoreCase))
                {
                    endpointCost += cost.Value * Math.Max(inputMegapixels, 0.01);
                    hasKnownCost = true;
                    if (billable.Contains("output", StringComparison.OrdinalIgnoreCase))
                    {
                        approximate = true;
                    }
                }
                else if (unit.Contains("token", StringComparison.OrdinalIgnoreCase))
                {
                    tokenBased = true;
                    endpointUnknown = true;
                }
                else
                {
                    endpointUnknown = true;
                }
            }

            if (hasKnownCost)
            {
                estimates.Add(endpointCost);
                approximate |= endpointUnknown;
            }
            else if (endpointUnknown)
            {
                tokenBased = true;
            }
        }

        if (estimates.Count == 0)
        {
            return tokenBased
                ? new ImageCostEstimate(null, null, true, true, "토큰 기반 과금 · 완료 후 실제 비용 표시")
                : new ImageCostEstimate(null, null, false, true, "예상 비용 계산 불가 · 완료 후 실제 비용 표시");
        }

        var min = estimates.Min();
        var max = estimates.Max();
        string display;
        if (Math.Abs(max - min) < 0.0000001)
        {
            display = $"{(approximate ? "예상 약" : "예상")} ${min:F4}";
        }
        else
        {
            display = $"{(approximate ? "예상 약" : "예상")} ${min:F4}~${max:F4}";
        }

        if (tokenBased)
        {
            display += " + 토큰 비용 가능";
        }

        return new ImageCostEstimate(min, max, tokenBased, approximate, display);
    }

    public async Task<ImageEditResult> EditImageAsync(
        string apiKey,
        string modelId,
        byte[] sourceImageBytes,
        string sourceMediaType,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (!IsOpenAiImageModel(modelId))
        {
            var normalizedSource = NormalizeSourceImage(sourceImageBytes, sourceMediaType);
            return await SendEditRequestAsync(
                apiKey,
                modelId,
                normalizedSource,
                prompt,
                cancellationToken);
        }

        var variants = BuildOpenAiCompatibleVariants(sourceImageBytes);
        OpenRouterImageException? lastInvalidImageException = null;

        foreach (var variant in variants)
        {
            try
            {
                return await SendEditRequestAsync(
                    apiKey,
                    modelId,
                    variant,
                    prompt,
                    cancellationToken);
            }
            catch (OpenRouterImageException exception) when (IsInvalidImageModeError(exception))
            {
                lastInvalidImageException = exception;
            }
        }

        if (lastInvalidImageException is not null)
        {
            throw new OpenRouterImageException(
                lastInvalidImageException.StatusCode,
                $"{lastInvalidImageException.ProviderMessage}\n\nOpenAI 호환 입력으로 자동 재시도했습니다: 8-bit RGB JPEG → 8-bit RGB PNG.",
                lastInvalidImageException.ProviderName,
                lastInvalidImageException.FinishReason,
                lastInvalidImageException.BlockReason,
                lastInvalidImageException.RawResponse);
        }

        throw new InvalidOperationException("OpenAI 이미지 편집 요청을 처리하지 못했습니다.");
    }

    private async Task<ImageEditResult> SendEditRequestAsync(
        string apiKey,
        string modelId,
        ImageEditSource source,
        string prompt,
        CancellationToken cancellationToken)
    {
        var sourceDataUrl = $"data:{source.MediaType};base64,{Convert.ToBase64String(source.ImageBytes)}";
        var payload = new
        {
            model = modelId,
            prompt,
            input_references = new[]
            {
                new
                {
                    type = "image_url",
                    image_url = new { url = sourceDataUrl }
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
                null, null, null, responseBody);
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
                null, null, null, responseBody);
        }

        var mediaType = image.TryGetProperty("media_type", out var mediaTypeElement)
            ? mediaTypeElement.GetString() ?? "image/png"
            : "image/png";
        var actualCostUsd = document.RootElement.TryGetProperty("usage", out var usage)
            ? TryGetDouble(usage, "cost")
            : null;

        return new ImageEditResult(Convert.FromBase64String(base64), mediaType, actualCostUsd);
    }

    private static bool IsOpenAiImageModel(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var value = modelId.Trim().ToLowerInvariant();
        return value.StartsWith("openai/", StringComparison.Ordinal) ||
               value.Contains("gpt-image", StringComparison.Ordinal) ||
               value.Contains("gpt-5-image", StringComparison.Ordinal) ||
               value.Contains("chatgpt-image", StringComparison.Ordinal);
    }

    private static bool IsInvalidImageModeError(OpenRouterImageException exception)
    {
        var message = exception.ProviderMessage ?? exception.RawResponse;
        return message.Contains("Invalid image file or mode", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("invalid image", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ImageEditSource> BuildOpenAiCompatibleVariants(byte[] imageBytes)
    {
        using var sourceStream = new MemoryStream(imageBytes);
        var decoder = BitmapDecoder.Create(
            sourceStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        var source = decoder.Frames[0];

        var rgb = new FormatConvertedBitmap();
        rgb.BeginInit();
        rgb.Source = source;
        rgb.DestinationFormat = PixelFormats.Bgr24;
        rgb.EndInit();
        rgb.Freeze();

        var jpegEncoder = new JpegBitmapEncoder { QualityLevel = 95 };
        jpegEncoder.Frames.Add(BitmapFrame.Create(rgb));
        using var jpegStream = new MemoryStream();
        jpegEncoder.Save(jpegStream);

        var pngEncoder = new PngBitmapEncoder();
        pngEncoder.Frames.Add(BitmapFrame.Create(rgb));
        using var pngStream = new MemoryStream();
        pngEncoder.Save(pngStream);

        return new[]
        {
            new ImageEditSource(jpegStream.ToArray(), "image/jpeg"),
            new ImageEditSource(pngStream.ToArray(), "image/png")
        };
    }

    private static double? TryGetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var numericValue))
        {
            return numericValue;
        }

        if (property.ValueKind == JsonValueKind.String &&
            double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var stringValue))
        {
            return stringValue;
        }

        return null;
    }

    private static bool? TryGetBoolean(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
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
        }

        return new OpenRouterImageException(
            statusCode, providerMessage, providerName, finishReason, blockReason, responseBody);
    }

    private static ImageEditSource NormalizeSourceImage(byte[] imageBytes, string mediaType)
    {
        if (!mediaType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase))
        {
            return new ImageEditSource(imageBytes, mediaType);
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
        return new ImageEditSource(outputStream.ToArray(), "image/png");
    }
}

public sealed record ImageEditSource(byte[] ImageBytes, string MediaType);

public sealed record ImageEditResult(byte[] ImageBytes, string MediaType, double? ActualCostUsd);

public sealed record ImageCostEstimate(
    double? MinCostUsd,
    double? MaxCostUsd,
    bool HasTokenBasedComponent,
    bool IsApproximate,
    string DisplayText);

public sealed record OpenRouterKeyBalanceStatus(
    bool IsManagementKey,
    double? Usage,
    double? Limit,
    double? LimitRemaining,
    string? LimitReset,
    double? CreditBalance,
    double? TotalCredits,
    double? TotalUsage);

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
            return "OpenRouter 인증에 실패했습니다.\n\n설정 창의 API Key가 올바른지 확인하세요.";
        }

        if ((int)statusCode == 402)
        {
            return "OpenRouter 크레딧 또는 결제 상태 때문에 요청을 처리할 수 없습니다.\n\nOpenRouter 계정의 사용 가능 크레딧을 확인하세요.";
        }

        if ((int)statusCode == 429)
        {
            return "요청 한도에 도달했거나 모델 공급자가 일시적으로 요청을 제한했습니다.\n\n잠시 후 다시 시도하거나 다른 모델을 선택하세요.";
        }

        if ((int)statusCode >= 500)
        {
            return "OpenRouter 또는 모델 공급자에서 일시적인 서버 오류가 발생했습니다.\n\n잠시 후 다시 시도하세요.";
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
