using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private static readonly HttpClient MarketPriceHttp = new()
    {
        Timeout = TimeSpan.FromSeconds(12)
    };
    private readonly System.Windows.Forms.Timer _marketPriceTimer = new() { Interval = 120_000 };
    private readonly Label _maniaMarketPrice = new()
    {
        Text = "조회 전", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("맑은 고딕", 11F, FontStyle.Bold), ForeColor = UiTheme.Text
    };
    private readonly Label _bayMarketPrice = new()
    {
        Text = "조회 전", AutoSize = false, TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("맑은 고딕", 11F, FontStyle.Bold), ForeColor = UiTheme.Text
    };
    private readonly Label _marketPriceStatus = new()
    {
        Text = "외부 참고 시세 · 로스트아크 10만 골드 기준",
        AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = UiTheme.Muted
    };
    private IReadOnlyDictionary<string, MarketPriceQuote> _marketQuotes =
        new Dictionary<string, MarketPriceQuote>(StringComparer.OrdinalIgnoreCase);
    private DateTime _marketPriceFetchedUtc = DateTime.MinValue;
    private DateTime _marketPriceAttemptedUtc = DateTime.MinValue;
    private bool _marketPriceLoading;
    private string _marketPriceError = "";

    private Control BuildMarketPricePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3,
            Margin = new Padding(0), Padding = new Padding(0, 1, 0, 0),
            BackColor = UiTheme.Surface
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));

        Label Heading(string title) => new()
        {
            Dock = DockStyle.Fill, Text = title, TextAlign = ContentAlignment.MiddleCenter,
            BackColor = UiTheme.SurfaceAlt, ForeColor = UiTheme.Text,
            Margin = new Padding(2, 0, 2, 0)
        };
        panel.Controls.Add(Heading("매니아"), 0, 0);
        panel.Controls.Add(Heading("베이"), 1, 0);
        panel.Controls.Add(new Label
        {
            Text = "10만 골드", Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter, ForeColor = UiTheme.Muted
        }, 2, 0);

        _maniaMarketPrice.Dock = DockStyle.Fill;
        _bayMarketPrice.Dock = DockStyle.Fill;
        panel.Controls.Add(_maniaMarketPrice, 0, 1);
        panel.Controls.Add(_bayMarketPrice, 1, 1);
        var refresh = new Button
        {
            Text = "조회", Dock = DockStyle.Fill, Margin = new Padding(2, 4, 2, 3)
        };
        refresh.Click += async (_, _) => await RefreshMarketPricesAsync(force: true);
        panel.Controls.Add(refresh, 2, 1);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = false,
            Margin = new Padding(0), Padding = new Padding(2, 0, 0, 0)
        };
        _marketPriceStatus.Width = 290;
        _marketPriceStatus.Height = 23;
        footer.Controls.Add(_marketPriceStatus);
        var source = new LinkLabel
        {
            Text = "출처·상세 시세", AutoSize = true, LinkColor = UiTheme.Primary,
            Margin = new Padding(5, 4, 0, 0)
        };
        source.LinkClicked += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(MarketPriceParser.SourceUrl)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex) { AppendLog("시세 페이지 열기 실패: " + ex.Message); }
        };
        footer.Controls.Add(source);
        panel.Controls.Add(footer, 0, 2);
        panel.SetColumnSpan(footer, 3);
        return panel;
    }

    private void InitializeMarketPrices()
    {
        _marketPriceTimer.Tick += async (_, _) => await RefreshMarketPricesAsync();
        _marketPriceTimer.Start();
        Shown += async (_, _) => await RefreshMarketPricesAsync();
    }

    private void UpdateMarketPriceDisplay()
    {
        if (!IsHandleCreated || IsDisposed) return;
        var game = _game.SelectedItem?.ToString();
        var server = _server.SelectedItem?.ToString();
        if (game != GameServerCatalog.LostArk || string.IsNullOrWhiteSpace(server))
        {
            _maniaMarketPrice.Text = "—";
            _bayMarketPrice.Text = "—";
            _marketPriceStatus.Text = game != GameServerCatalog.LostArk
                ? "로스트아크 서버 시세만 조회합니다"
                : "서버를 선택하세요";
            return;
        }
        if (_marketQuotes.TryGetValue(server, out var quote))
        {
            _maniaMarketPrice.Text = Price(quote.ItemManiaPer100k);
            _bayMarketPrice.Text = Price(quote.ItemBayPer100k);
            _marketPriceStatus.Text = $"외부 참고 · 조회 {_marketPriceFetchedUtc.ToLocalTime():HH:mm} · 10만 골드";
        }
        else
        {
            _maniaMarketPrice.Text = _marketPriceLoading ? "조회 중…" : "—";
            _bayMarketPrice.Text = _marketPriceLoading ? "조회 중…" : "—";
            _marketPriceStatus.Text = _marketPriceLoading ? "공개 시세 불러오는 중…"
                : string.IsNullOrWhiteSpace(_marketPriceError)
                    ? "해당 서버 시세를 확인할 수 없습니다"
                    : _marketPriceError;
        }
        if (!_marketPriceLoading && DateTime.UtcNow - _marketPriceAttemptedUtc > TimeSpan.FromMinutes(2))
            _ = RefreshMarketPricesAsync();
    }

    private static string Price(int? price) => price.HasValue ? $"{price.Value:N0}원" : "—";

    private async Task RefreshMarketPricesAsync(bool force = false)
    {
        if (IsDisposed || _marketPriceLoading) return;
        if (_game.SelectedItem?.ToString() != GameServerCatalog.LostArk)
        {
            UpdateMarketPriceDisplay();
            return;
        }
        if (!force && DateTime.UtcNow - _marketPriceAttemptedUtc < TimeSpan.FromMinutes(2))
        {
            UpdateMarketPriceDisplay();
            return;
        }
        _marketPriceAttemptedUtc = DateTime.UtcNow;
        _marketPriceLoading = true;
        UpdateMarketPriceDisplay();
        try
        {
            // No credentials, cookies, or browser session are sent to this
            // independent, public-price comparison website.
            var html = await MarketPriceHttp.GetStringAsync(MarketPriceParser.SourceUrl);
            var quotes = MarketPriceParser.Parse(html);
            if (quotes.Count == 0) throw new InvalidOperationException("공개 서버별 시세 표를 읽을 수 없음");
            _marketQuotes = quotes;
            _marketPriceFetchedUtc = DateTime.UtcNow;
            _marketPriceError = "";
        }
        catch (Exception ex)
        {
            // A failed refresh must never display old values as if newly fetched.
            _marketQuotes = new Dictionary<string, MarketPriceQuote>(StringComparer.OrdinalIgnoreCase);
            _marketPriceError = "외부 시세 조회 실패 · 출처에서 확인";
            AppendLog("시세 참고 자료 조회 실패 (등록 작업과 무관): " + ex.Message);
        }
        finally
        {
            _marketPriceLoading = false;
            if (!IsDisposed && IsHandleCreated) UpdateMarketPriceDisplay();
        }
    }
}
