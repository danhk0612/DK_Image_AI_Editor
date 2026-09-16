using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DKImageAIEditor.Services;

public sealed class ImageCostEstimatorService
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, CachedPricing> PricingCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _httpClient;

    public ImageCostEstimatorService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<ImageCostEstimate> EstimateAsync(
        string apiKey,
        string modelId,
        double inputMegapixels,
        string? prompt,
        CancellationToken cancellationToken = default)
    {
        var body = await GetPricingJsonAsync(apiKey, modelId, cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return new ImageCostEstimate(null, null, false, true, "가격 정보 조회 불가");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("endpoints", out var endpoints) ||
            endpoints.ValueKind != JsonValueKind.Array || endpoints.GetArrayLength() == 0)
        {
            return new ImageCostEstimate(null, null, false, true, "가격 정보 없음");
        }

        var megapixels = Math.Max(inputMegapixels, 0.01);
        var textTokens = EstimateTextTokens(prompt);
        var inputImageTokens = Math.Max(256.0, megapixels * 1024.0);
        var outputImageTokens = Math.Max(512.0, megapixels * 1024.0);
        var estimates = new List<double>();
        var hasTokenPricing = false;
        var approximate = false;

        foreach (var endpoint in endpoints.EnumerateArray())
        {
            if (!endpoint.TryGetProperty("pricing", out var pricing) || pricing.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var endpointCost = 0.0;
            var hasCost = false;
            var endpointApproximate = false;

            foreach (var price in pricing.EnumerateArray())
            {
                var unit = GetString(price, "unit");
                var billable = GetString(price, "billable");
                var cost = GetDouble(price, "cost_usd");
                if (!cost.HasValue)
                {
                    endpointApproximate = true;
                    continue;
                }

                if (unit.Equals("image", StringComparison.OrdinalIgnoreCase))
                {
                    endpointCost += cost.Value;
                    hasCost = true;
                    continue;
                }

                if (unit.Equals("megapixel", StringComparison.OrdinalIgnoreCase))
                {
                    endpointCost += cost.Value * megapixels;
                    hasCost = true;
                    if (billable.Contains("output", StringComparison.OrdinalIgnoreCase))
                    {
                        endpointApproximate = true;
                    }
                    continue;
                }

                if (unit.Contains("token", StringComparison.OrdinalIgnoreCase))
                {
                    hasTokenPricing = true;
                    endpointApproximate = true;
                    var estimatedTokens = EstimateBillableTokens(
                        billable,
                        textTokens,
                        inputImageTokens,
                        outputImageTokens);
                    var divisor = unit.Contains("million", StringComparison.OrdinalIgnoreCase) ||
                                  unit.Contains("1m", StringComparison.OrdinalIgnoreCase)
                        ? 1_000_000.0
                        : 1.0;
                    endpointCost += cost.Value * estimatedTokens / divisor;
                    hasCost = true;
                    continue;
                }

                endpointApproximate = true;
            }

            if (hasCost)
            {
                estimates.Add(endpointCost);
                approximate |= endpointApproximate;
            }
        }

        if (estimates.Count == 0)
        {
            return new ImageCostEstimate(
                null,
                null,
                hasTokenPricing,
                true,
                "예상 비용 계산 불가 · 완료 후 실제 비용 표시");
        }

        var min = estimates.Min();
        var max = estimates.Max();
        var prefix = approximate || hasTokenPricing ? "예상 약" : "예상";
        var display = Math.Abs(max - min) < 0.0000001
            ? $"{prefix} ${min:F4}"
            : $"{prefix} ${min:F4}~${max:F4}";

        if (hasTokenPricing)
        {
            display += $" · 텍스트≈{textTokens:F0}t, 이미지≈{inputImageTokens:F0}t";
        }

        return new ImageCostEstimate(min, max, hasTokenPricing, approximate || hasTokenPricing, display);
    }

    private async Task<string?> GetPricingJsonAsync(
        string apiKey,
        string modelId,
        CancellationToken cancellationToken)
    {
        if (PricingCache.TryGetValue(modelId, out var cached) &&
            DateTimeOffset.UtcNow - cached.FetchedAt < CacheLifetime)
        {
            return cached.Json;
        }

        var endpointUri = new Uri($"https://openrouter.ai/api/v1/images/models/{modelId}/endpoints");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpointUri);
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        PricingCache[modelId] = new CachedPricing(DateTimeOffset.UtcNow, json);
        return json;
    }

    private static double EstimateTextTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 1.0;
        }

        var tokens = 0.0;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                tokens += 0.05;
            }
            else if (character <= 0x7F)
            {
                tokens += 0.25;
            }
            else
            {
                tokens += 0.7;
            }
        }

        return Math.Max(1.0, Math.Ceiling(tokens));
    }

    private static double EstimateBillableTokens(
        string billable,
        double textTokens,
        double inputImageTokens,
        double outputImageTokens)
    {
        var normalized = billable.ToLowerInvariant();
        if (normalized.Contains("input_text"))
        {
            return textTokens;
        }

        if (normalized.Contains("input_image"))
        {
            return inputImageTokens;
        }

        if (normalized.Contains("output_image"))
        {
            return outputImageTokens;
        }

        if (normalized.Contains("output"))
        {
            return outputImageTokens;
        }

        if (normalized.Contains("input"))
        {
            return textTokens + inputImageTokens;
        }

        return textTokens + inputImageTokens + outputImageTokens;
    }

    private static double? GetDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var numericValue))
        {
            return numericValue;
        }

        return property.ValueKind == JsonValueKind.String &&
               double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var stringValue)
            ? stringValue
            : null;
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed record CachedPricing(DateTimeOffset FetchedAt, string Json);
}
