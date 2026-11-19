using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private bool _splitStatusMode;
    private readonly Dictionary<string, (bool Visible, int Width, int MinimumWidth,
        DataGridViewAutoSizeColumnMode AutoSizeMode)> _preSplitAutoColumns = new();

    public event Action<string>? RequestSingleViewFromSplit;

    public bool IsAutomationRunning => _schedulerRunning;

    // Read-only snapshot created on the WinForms UI thread. No site request,
    // registration, or account change is triggered by Telegram.
    public string TelegramStatusLine
    {
        get
        {
            string state = _schedulerRunning && _loginRecoveryPauseUntilUtc != DateTime.MinValue
                ? "로그인 복구 대기" :
                _system == "B" && _schedulerRunning &&
                (_bConnectionRecheckRunning || _bConnectionRecoveryUtc > DateTime.UtcNow)
                ? "VPN 복구 대기" :
                _system != "B" && _schedulerRunning &&
                (_ownedListRecoveryRecheckRunning || _ownedListRecoveryUtc > DateTime.UtcNow)
                ? "관리목록 복구 대기" :
                _schedulerRunning ? "자동 실행 중" : _busy ? "작업 중" : "중지/대기";
            int enabled = _jobs.Count(job => job.Enabled && !job.RequiresReview);
            int held = _jobs.Count(job => job.RequiresReview);
            int prices = _jobs.Count(job => !string.IsNullOrWhiteSpace(job.PendingSitePriceTarget));
            DateTime? last = _jobs.Where(job => job.LastSucceededUtc.HasValue)
                .Select(job => job.LastSucceededUtc).OrderByDescending(t => t).FirstOrDefault();
            string latest = last.HasValue ? last.Value.ToLocalTime().ToString("MM-dd HH:mm") : "기록 없음";
            string route = _system == "B"
                ? (_bConnectionRecheckRunning || _bConnectionRecoveryUtc > DateTime.UtcNow
                    ? " · VPN 재검증 중" : DualSystemNetwork.IsRouteVerified("B") ? " · VPN IP 검증됨" : " · VPN 미확인")
                : _system == "C"
                    ? (DualSystemNetwork.IsRouteVerified("C") ? " · VPN IP 검증됨" : " · VPN 미확인") : "";
            return $"{_system}: {state}{route}\n" +
                $"   등록 대상 {enabled}/{_jobs.Count} · 가격 대기 {prices} · 확인 필요 {held}\n" +
                $"   마지막 성공 {latest}";
        }
    }

    public string SplitStatusSummary
    {
        get
        {
            int enabled = _jobs.Count(job => job.Enabled);
            int pending = _jobs.Count(job => job.RequiresReview);
            string state = _schedulerRunning && _loginRecoveryPauseUntilUtc != DateTime.MinValue
                ? "로그인 복구 대기" :
                _system == "B" && _schedulerRunning && _bConnectionRecheckRunning
                ? "VPN 연결 재확인 중" :
                _system == "B" && _schedulerRunning && _bConnectionRecoveryUtc > DateTime.UtcNow
                ? "VPN·관리목록 복구 대기" :
                _system != "B" && _schedulerRunning && _ownedListRecoveryRecheckRunning
                ? "관리목록 복구 확인 중" :
                _system != "B" && _schedulerRunning && _ownedListRecoveryUtc > DateTime.UtcNow
                ? "관리목록 복구 대기" :
                _schedulerRunning ? "실행 중" : _busy ? "작업 중" : "중지/대기";
            return $"{state}   ·   전체 {_jobs.Count}   ·   자동 {enabled}   ·   확인 필요 {pending}";
        }
    }

    // The same AutomationForm, jobs and WebView2 are reused in both layouts.
    // These are only transient column settings, never saved as user preferences.
    public void SetSplitStatusMode(bool enabled)
    {
        if (_splitStatusMode == enabled) return;
        _saveColumnLayoutTimer.Stop();
        _readingColumnLayout = true;
        try
        {
            if (enabled)
            {
                _preSplitAutoColumns.Clear();
                foreach (DataGridViewColumn column in _autoGrid.Columns)
                    _preSplitAutoColumns[ColumnIdentity(column)] = (column.Visible, column.Width,
                        column.MinimumWidth, column.AutoSizeMode);
                ShowAutomationView();
                if (_segmentedNavigation is not null) _segmentedNavigation.Visible = false;
                foreach (DataGridViewColumn column in _autoGrid.Columns)
                {
                    string key = ColumnIdentity(column);
                    column.Visible = key is "Enabled" or "Server" or "Price" or "Title"
                        or "NextDueLocal" or "LastStatus";
                    if (!column.Visible) continue;
                    if (key == "Title")
                    {
                        column.MinimumWidth = 130;
                        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                    }
                    else
                    {
                        column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                        column.MinimumWidth = 38;
                        column.Width = key switch
                        {
                            "Enabled" => 49, "Server" => 91, "Price" => 86,
                            "NextDueLocal" => 120, "LastStatus" => 135, _ => column.Width
                        };
                    }
                }
            }
            else
            {
                foreach (DataGridViewColumn column in _autoGrid.Columns)
                {
                    if (!_preSplitAutoColumns.TryGetValue(ColumnIdentity(column), out var previous)) continue;
                    column.MinimumWidth = Math.Min(previous.MinimumWidth, column.MinimumWidth);
                    column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                    column.Width = previous.Width;
                    column.MinimumWidth = previous.MinimumWidth;
                    column.AutoSizeMode = previous.AutoSizeMode;
                    column.Visible = previous.Visible;
                }
                if (_segmentedNavigation is not null) _segmentedNavigation.Visible = true;
                _preSplitAutoColumns.Clear();
            }
            _splitStatusMode = enabled;
        }
        finally { _readingColumnLayout = false; }
    }
}
