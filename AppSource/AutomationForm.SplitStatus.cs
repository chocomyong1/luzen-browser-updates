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

    public string SplitStatusSummary
    {
        get
        {
            int enabled = _jobs.Count(job => job.Enabled);
            int pending = _jobs.Count(job => job.RequiresReview);
            string state = _system == "B" && _schedulerRunning && _bConnectionRecheckRunning
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
