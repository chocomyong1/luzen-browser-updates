using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private sealed record IntegratedPriceRequest(string System, ListingJob Job, string TargetPrice);
    private readonly Dictionary<string, IntegratedPriceRequest> _integratedPricePending = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IntegratedPriceRequest> _integratedPriceInFlight = new(StringComparer.Ordinal);
    private bool _integratedPriceQueueRunning;
    private bool _integratedPriceStopRequested;
    private string? _integratedPriceActiveSystem;

    // A/B/C must not post the same account's same listing ID in parallel.
    private static string IntegratedPriceKey(string siteId, string listingNumber) =>
        siteId.Trim().ToUpperInvariant() + "|" + listingNumber.Trim();
    private static string IntegratedPriceKey(IntegratedPriceRequest request) =>
        IntegratedPriceKey(request.Job.SiteId ?? "", request.Job.ListingNumber ?? "");

    private Task SaveIntegratedCheckedPricesAsync()
    {
        _integratedGrid?.EndEdit();
        var selected = VisibleIntegratedRows().Where(row => row.Selected).ToList();
        if (selected.Count == 0)
        {
            _status.Text = "  순차 가격 반영 대상이 없습니다. 먼저 물품을 체크하세요.";
            return Task.CompletedTask;
        }
        return ApplyIntegratedSavedPricesAsync(selected);
    }

    // Both the integrated bulk editor's Save and the toolbar Save feed ONE
    // queue. Repeated clicks are idempotent, while new saved edits join after
    // the current system's in-flight submission/readback has completed.
    private async Task ApplyIntegratedSavedPricesAsync(IReadOnlyList<IntegratedRow> rows,
        string? newlySavedPrice = null)
    {
        var targets = new List<IntegratedPriceRequest>();
        foreach (var row in rows)
        {
            if (row.Job.ListingNumber is not { Length: 16 }) continue;
            string key = IntegratedPriceKey(row.Job.SiteId ?? "", row.Job.ListingNumber);
            string target = row.Job.PendingSitePriceTarget ?? "";

            // ApplyIntegratedEdit defers overwriting the actively submitted
            // job until its readback. Retain a different newly saved target for
            // a later turn, but never requeue the same in-flight price.
            if (newlySavedPrice is not null &&
                _integratedPriceInFlight.TryGetValue(key, out var inFlight) &&
                ReferenceEquals(inFlight.Job, row.Job) &&
                !string.Equals(inFlight.TargetPrice, newlySavedPrice, StringComparison.Ordinal))
                target = newlySavedPrice;

            if (string.IsNullOrWhiteSpace(target)) continue;
            targets.Add(new IntegratedPriceRequest(row.System, row.Job, target));
        }

        if (targets.Count == 0)
        {
            _status.Text = _integratedPriceQueueRunning
                ? $"  가격 순차 반영 진행 중 · 추가할 새 변경 없음 · 대기 {_integratedPricePending.Count}건 · 중복 제출 없음"
                : "  저장 완료 · 사이트에 반영할 새 가격 변경이 없습니다.";
            return;
        }

        // Never silently deduplicate different objects or profiles for the
        // same account/registration number: that would hide an identity error.
        if (targets.GroupBy(IntegratedPriceKey).Any(group =>
            group.Select(item => item.Job).Distinct().Count() > 1 ||
            group.Select(item => item.System).Distinct(StringComparer.Ordinal).Count() > 1))
        {
            _status.Text = "  같은 계정·등록번호가 여러 물품/시스템에 중복되어 있습니다. 사이트에 제출하지 않았습니다.";
            return;
        }

        int added = 0, same = 0;
        foreach (var request in targets)
        {
            string key = IntegratedPriceKey(request);
            if (_integratedPriceInFlight.TryGetValue(key, out var processing))
            {
                if (!ReferenceEquals(processing.Job, request.Job) ||
                    !string.Equals(processing.System, request.System, StringComparison.Ordinal))
                {
                    _status.Text = "  다른 시스템에서 같은 등록번호를 처리 중입니다. 사이트에 중복 제출하지 않았습니다.";
                    return;
                }
                if (string.Equals(processing.TargetPrice, request.TargetPrice, StringComparison.Ordinal))
                {
                    same++;
                    continue;
                }
            }
            if (_integratedPricePending.TryGetValue(key, out var pending))
            {
                if (!ReferenceEquals(pending.Job, request.Job) ||
                    !string.Equals(pending.System, request.System, StringComparison.Ordinal))
                {
                    _status.Text = "  같은 등록번호의 대기열 소유자가 일치하지 않습니다. 중복 제출을 막았습니다.";
                    return;
                }
                if (string.Equals(pending.TargetPrice, request.TargetPrice, StringComparison.Ordinal))
                {
                    same++;
                    continue;
                }
            }
            _integratedPricePending[key] = request; // latest saved target wins
            added++;
        }

        if (_integratedPriceQueueRunning)
        {
            _status.Text = added > 0
                ? $"  순차 가격 반영 진행 중 · 새 변경 {added}건 뒤에 추가 · 대기 {_integratedPricePending.Count}건 · 중복 {same}건 제외"
                : $"  순차 가격 반영 진행 중 · 이미 처리/대기 중인 {same}건은 추가 제출하지 않습니다 · 대기 {_integratedPricePending.Count}건";
            return;
        }
        if (_integratedPricePending.Count == 0)
        {
            _status.Text = "  저장 완료 · 새 사이트 반영 요청 없음";
            return;
        }

        _integratedPriceQueueRunning = true;
        _integratedPriceStopRequested = false;
        int verified = 0, requested = 0;
        string failure = "";
        try
        {
            while (_integratedPricePending.Count > 0 && !_integratedPriceStopRequested)
            {
                string system = _integratedPricePending.Values.Select(item => item.System)
                    .OrderBy(side => side, StringComparer.Ordinal).First();
                var batch = _integratedPricePending.Values.Where(item => item.System == system).ToList();
                foreach (var item in batch)
                {
                    string key = IntegratedPriceKey(item);
                    _integratedPricePending.Remove(key);
                    _integratedPriceInFlight[key] = item;
                }

                _integratedPriceActiveSystem = system;
                requested += batch.Count;
                try
                {
                    _status.Text = $"  순차 가격 반영 · {system} 시스템 · {verified}건 사이트 확인 · 현재 {batch.Count}건 · 추가 대기 {_integratedPricePending.Count}건";
                    var result = await IntegratedOwner(system).ApplySavedPricesSequentialAsync(
                        batch.Select(item => item.Job).ToList());
                    verified += result.Verified;
                    if (!result.Succeeded)
                    {
                        failure = system + " · " + result.Failure;
                        break; // Inconclusive site outcome: stop all later systems.
                    }
                }
                finally
                {
                    foreach (var item in batch) _integratedPriceInFlight.Remove(IntegratedPriceKey(item));
                    _integratedPriceActiveSystem = null;
                }
            }
            if (_integratedPriceStopRequested && failure.Length == 0)
                failure = "남은 가격 반영 중지 요청 · 미처리 글은 사이트에 제출하지 않았습니다.";
            _status.Text = failure.Length == 0
                ? $"  순차 가격 반영 완료 · {verified}/{requested}건 사이트 확인 · 대기 0건"
                : $"  순차 가격 반영 중단 · {verified}/{requested}건 사이트 확인 · {failure}";
        }
        catch (Exception ex)
        {
            failure = "순차 반영 예외: " + ex.Message;
            _status.Text = "  " + failure;
        }
        finally
        {
            // A stopped/failed queue is never retried automatically. Saved
            // local targets remain available for an explicit future Save.
            _integratedPricePending.Clear();
            _integratedPriceInFlight.Clear();
            _integratedPriceActiveSystem = null;
            _integratedPriceStopRequested = false;
            _integratedPriceQueueRunning = false;
            if (_integratedVisible && _integratedEditor is not null && !_integratedEditor.IsDisposed)
                RebuildIntegratedRows();
        }
    }

    private void StopIntegratedPriceQueue()
    {
        if (!_integratedPriceQueueRunning)
        {
            _status.Text = "  진행 중인 순차 가격 반영이 없습니다.";
            return;
        }
        _integratedPriceStopRequested = true;
        int pending = _integratedPricePending.Count;
        _integratedPricePending.Clear();
        if (_integratedPriceActiveSystem is not null)
            IntegratedOwner(_integratedPriceActiveSystem).RequestStopSitePriceBatch();
        _status.Text = $"  중지 요청 · 현재 건의 결과 확인 후 종료 · 추가 대기 {pending}건 제출 취소";
    }
}
