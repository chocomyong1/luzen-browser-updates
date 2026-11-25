using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private bool _integratedPriceQueueRunning;

    // The integrated Save action dispatches A -> B -> C strictly in series.
    // It reuses each account's own WebView and registration-number matching.
    private Task SaveIntegratedCheckedPricesAsync()
    {
        _integratedGrid?.EndEdit();
        var selected = VisibleIntegratedRows().Where(row => row.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "저장 후 반영할 물품을 먼저 체크하세요.", "가격 저장");
            return Task.CompletedTask;
        }
        return ApplyIntegratedSavedPricesAsync(selected);
    }

    private async Task ApplyIntegratedSavedPricesAsync(IReadOnlyList<IntegratedRow> rows)
    {
        if (_integratedPriceQueueRunning)
        {
            _status.Text = "  가격 순차 반영이 이미 진행 중입니다. 중복 제출하지 않습니다.";
            return;
        }
        var targets = rows.Where(row =>
            !string.IsNullOrWhiteSpace(row.Job.PendingSitePriceTarget) &&
            row.Job.ListingNumber is { Length: 16 }).ToList();
        if (targets.Count == 0)
        {
            _status.Text = "  저장 완료 · 사이트에 반영할 가격 변경이 없습니다.";
            return;
        }

        // One account and registration number must never be submitted from two
        // different A/B/C profiles in the same Save operation.
        bool duplicate = targets.GroupBy(row => (
            (row.Job.SiteId ?? "").Trim().ToLowerInvariant(), row.Job.ListingNumber))
            .Any(group => group.Count() > 1);
        if (duplicate)
        {
            _status.Text = "  같은 등록번호가 A/B/C에 중복되어 순차 반영을 중단했습니다. 사이트 제출 없음.";
            return;
        }

        _integratedPriceQueueRunning = true;
        int verified = 0;
        string failure = "";
        try
        {
            foreach (var group in targets.GroupBy(row => row.System).OrderBy(group => group.Key))
            {
                var form = IntegratedOwner(group.Key);
                var jobs = group.Select(row => row.Job).ToList();
                _status.Text = $"  통합 가격 순차 반영 · {group.Key} 시스템 · {verified}/{targets.Count}건 확인";
                var result = await form.ApplySavedPricesSequentialAsync(jobs);
                verified += result.Verified;
                if (!result.Succeeded)
                {
                    failure = group.Key + " · " + result.Failure;
                    break; // Never proceed to B/C after an uncertain A/B request.
                }
            }
            _status.Text = failure.Length == 0
                ? $"  통합 가격 순차 반영 완료 · {verified}/{targets.Count}건 실제 사이트 확인"
                : $"  통합 가격 순차 반영 중단 · {verified}/{targets.Count}건 확인 · {failure}";
        }
        catch (Exception ex)
        {
            _status.Text = "  통합 가격 순차 반영 중단 · " + ex.Message;
        }
        finally
        {
            _integratedPriceQueueRunning = false;
            if (_integratedVisible && _integratedEditor is not null && !_integratedEditor.IsDisposed)
                RebuildIntegratedRows();
        }
    }
}
