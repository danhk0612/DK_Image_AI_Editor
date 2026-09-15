using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DKImageAIEditor.Services;

namespace DKImageAIEditor;

public partial class MainWindow
{
    private TextBlock? _openRouterBalanceTextBlock;
    private DispatcherTimer? _openRouterBalanceRefreshTimer;
    private bool _openRouterBalanceInitialized;

    private void InitializeOpenRouterBalanceDisplay()
    {
        if (_openRouterBalanceInitialized)
        {
            return;
        }

        _openRouterBalanceInitialized = true;

        if (_openRouterBalanceTextBlock is not null)
        {
            _openRouterBalanceTextBlock.Cursor = Cursors.Hand;
            _openRouterBalanceTextBlock.ToolTip = "현재 설정된 OpenRouter Key 상태 · 클릭하면 새로고침";
            _openRouterBalanceTextBlock.MouseLeftButtonUp += async (_, _) =>
                await RefreshOpenRouterBalanceAsync();
        }

        SettingsButton.Click += async (_, _) => await RefreshOpenRouterBalanceAsync();

        _openRouterBalanceRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _openRouterBalanceRefreshTimer.Tick += async (_, _) =>
            await RefreshOpenRouterBalanceAsync();
        _openRouterBalanceRefreshTimer.Start();

        _ = RefreshOpenRouterBalanceAsync();
    }

    private async Task RefreshOpenRouterBalanceAsync()
    {
        if (_openRouterBalanceTextBlock is null)
        {
            return;
        }

        var apiKey = CredentialStore.LoadApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _openRouterBalanceTextBlock.Text = "OpenRouter: API Key 없음";
            _openRouterBalanceTextBlock.ToolTip = "설정에서 OpenRouter API Key를 입력하세요.";
            return;
        }

        _openRouterBalanceTextBlock.Text = "OpenRouter: 확인 중...";

        try
        {
            var status = await _openRouterImageService.GetKeyBalanceStatusAsync(apiKey);

            if (status.IsManagementKey)
            {
                _openRouterBalanceTextBlock.Text = status.CreditBalance.HasValue
                    ? $"충전 잔액 {FormatUsd(status.CreditBalance.Value)}"
                    : "Management Key · 잔액 확인 불가";

                _openRouterBalanceTextBlock.ToolTip =
                    "Management Key\n" +
                    $"총 충전: {FormatNullableUsd(status.TotalCredits)}\n" +
                    $"총 사용: {FormatNullableUsd(status.TotalUsage)}\n" +
                    $"잔액: {FormatNullableUsd(status.CreditBalance)}\n\n" +
                    "클릭하면 새로고침";
                return;
            }

            var usageText = FormatNullableUsd(status.Usage);
            if (status.LimitRemaining.HasValue)
            {
                _openRouterBalanceTextBlock.Text =
                    $"키 사용 {usageText} · 잔여 {FormatUsd(status.LimitRemaining.Value)}";
                _openRouterBalanceTextBlock.ToolTip =
                    $"현재 API Key\n사용: {usageText}\n" +
                    $"한도: {FormatNullableUsd(status.Limit)}\n" +
                    $"잔여 한도: {FormatUsd(status.LimitRemaining.Value)}" +
                    (string.IsNullOrWhiteSpace(status.LimitReset)
                        ? string.Empty
                        : $"\n한도 초기화: {status.LimitReset}") +
                    "\n\n클릭하면 새로고침";
            }
            else
            {
                _openRouterBalanceTextBlock.Text = $"키 사용 {usageText} · 한도 없음";
                _openRouterBalanceTextBlock.ToolTip =
                    $"현재 API Key\n사용: {usageText}\n별도 사용 한도 없음\n\n클릭하면 새로고침";
            }
        }
        catch (Exception exception)
        {
            _openRouterBalanceTextBlock.Text = "OpenRouter: 키 상태 확인 실패";
            _openRouterBalanceTextBlock.ToolTip =
                $"{exception.Message}\n\n클릭하면 다시 시도";
        }
    }

    private static string FormatNullableUsd(double? value) =>
        value.HasValue ? FormatUsd(value.Value) : "없음";

    private static string FormatUsd(double value)
    {
        var format = Math.Abs(value) < 1 ? "0.0000" : "0.00";
        return "$" + value.ToString(format, CultureInfo.InvariantCulture);
    }
}
