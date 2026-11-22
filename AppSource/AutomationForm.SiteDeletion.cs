using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    private Form? _siteDeletionWindow;
    private Action? _markSiteDeletionRequested;

    internal async Task DeleteSiteListingAsync()
    {
        if (_busy || _myListingsImportRunning || _sitePriceBatchRunning || _siteRequestTraceActive)
        {
            MessageBox.Show(this, "진행 중인 사이트 작업이 끝난 뒤 삭제해 주세요.", "사이트 등록글 삭제");
            return;
        }

        _jobsGrid.EndEdit();
        _autoGrid.EndEdit();
        var marked = _filteredJobs.Where(job => job.SelectedForBulk).ToList();
        var current = (_editorTabs.SelectedTab == _autoTab ? _autoGrid : _jobsGrid)
            .CurrentRow?.DataBoundItem as ListingJob;
        var job = marked.Count == 1 ? marked[0] : marked.Count == 0 ? current : null;
        if (job is null || !_jobs.Contains(job) || job.ListingNumber is not { Length: 16 } ||
            !job.ListingNumber.All(char.IsAsciiDigit) || !MyListingsImport.IsOwnedListUrl(OwnedListUrlFor(job)))
        {
            MessageBox.Show(this, "본인 등록글 중 16자리 등록번호가 확인된 한 건을 선택해 주세요.", "사이트 등록글 삭제");
            return;
        }

        string account = ItemManiaAccountStore.Load(_system).UserId;
        if (string.IsNullOrWhiteSpace(account) || !string.Equals(job.SiteId, account, StringComparison.OrdinalIgnoreCase) ||
            (_system is "B" or "C") && !DualSystemNetwork.IsRouteVerified(_system) ||
            _jobs.Any(other => !ReferenceEquals(other, job) && other.ListingNumber == job.ListingNumber &&
                string.Equals(other.SiteId, account, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "등록글의 계정·등록번호 또는 이 시스템의 연결을 확인해 주세요.", "사이트 등록글 삭제");
            return;
        }

        if (_schedulerRunning) PauseScheduler();
        _busy = true;
        bool deletionStarted = false;
        try
        {
            using (var lookup = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
            {
                await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
                if (_autoLogin is not null) await _autoLogin.WaitForLoginNavigationAsync(lookup.Token);
                var result = await LocateOwnedListRepostAsync(job, lookup.Token, requireRepostButton: false);
                if (result.Status != "found")
                    throw new InvalidOperationException("현재 사이트 목록에서 같은 등록번호를 확인하지 못했습니다. " + result.Message);
            }

            bool verify = ShowSiteDeletionWindow(job, () => deletionStarted = true);
            if (!verify) return;
            deletionStarted = true;
            using var check = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var resultAfter = await LocateOwnedListRepostAsync(job, check.Token, requireRepostButton: false);
            if (!resultAfter.MissingAfterCompleteScan)
                throw new InvalidOperationException("사이트 목록에서 삭제 결과를 확인하지 못했습니다. VANTA 목록의 글은 유지합니다. " +
                    resultAfter.Message);
            if (!TryRemoveLocalJobs(new[] { job }, out var problem))
                throw new InvalidOperationException("사이트 목록에서 등록번호가 사라졌지만 VANTA 목록을 저장하지 못했습니다. " + problem);
            AppendLog($"사이트 삭제 후 관리목록 재조회 완료: {_system} · {job.ListingNumber}");
            MessageBox.Show(this, $"사이트 등록목록에서 {job.ListingNumber}가 사라진 것을 확인했습니다. VANTA 목록에서도 제거했습니다.",
                "삭제 결과 확인", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"사이트 삭제 확인 중단: {job.ListingNumber} · {ex.Message}");
            MessageBox.Show(this, ex.Message, "사이트 등록글 삭제", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (deletionStarted && _jobs.Contains(job))
            {
                job.Enabled = false;
                job.NextDueUtc = DateTime.MaxValue;
                job.LastStatus = "사이트 삭제 결과 확인 필요 · 자동 OFF";
                try { _config.Save(); }
                catch (Exception ex) { AppendLog("삭제 확인 상태를 저장하지 못했습니다: " + ex.Message); }
            }
            _busy = false;
            _jobsGrid.Refresh();
            _autoGrid.Refresh();
            RefreshAutoSummary();
            DispatchQueuedMyListingsImport();
        }
    }

    private bool ShowSiteDeletionWindow(ListingJob job, Action onDelete)
    {
        var parent = _web.Parent ?? throw new InvalidOperationException("사이트 브라우저의 원래 창을 찾을 수 없습니다.");
        var dock = _web.Dock;
        var bounds = _web.Bounds;
        using var window = new Form
        {
            Text = "아이템매니아 등록글 삭제 · " + job.ListingNumber,
            StartPosition = FormStartPosition.CenterParent, Width = 1200, Height = 850,
            MinimumSize = new Size(800, 550)
        };
        var instructions = new Label
        {
            Dock = DockStyle.Top, Height = 58, Padding = new Padding(12, 8, 12, 0),
            Text = $"{_system} 시스템 · {job.Server} · {job.ListingNumber}\n초록색 테두리의 글만 삭제 대상입니다."
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 52, Padding = new Padding(8) };
        var delete = new Button { Text = "이 글 사이트에서 삭제", Width = 175, Height = 31, Enabled = false };
        var verify = new Button { Text = "삭제 후 결과 확인", Width = 160, Height = 31 };
        var close = new Button { Text = "닫기", Width = 82, Height = 31 };
        verify.Click += (_, _) => window.DialogResult = DialogResult.OK;
        close.Click += (_, _) => window.DialogResult = DialogResult.Cancel;
        actions.Controls.AddRange(new Control[] { delete, verify, close });
        var host = new Panel { Dock = DockStyle.Fill };
        window.Controls.Add(host);
        window.Controls.Add(actions);
        window.Controls.Add(instructions);

        async Task<OwnedListRepostResult> ActionAsync(string action)
        {
            var core = _web.CoreWebView2;
            if (core is null || !IsAllowedCurrentPage())
                return new OwnedListRepostResult { Status = "error", Message = "공식 관리목록을 확인할 수 없습니다." };
            return OwnedListRepost.Decode(await core.ExecuteScriptAsync(OwnedListRepost.BuildScript(
                job.ListingNumber, job.Title, null, job.MinQuantity, job.MaxQuantity, job.Server, job.OriginalTradeSide, action)));
        }

        window.Shown += async (_, _) =>
        {
            try
            {
                var result = await ActionAsync("focus");
                if (window.IsDisposed) return;
                delete.Enabled = result.Status == "delete-ready";
                instructions.Text += delete.Enabled ? " 아래 삭제 버튼을 누르세요." :
                    result.Status == "delete-manual" ? " 해당 행에서 사이트의 삭제 버튼을 직접 누르세요." : "\n" + result.Message;
            }
            catch (Exception ex)
            {
                if (!window.IsDisposed) instructions.Text += "\n" + ex.Message;
            }
        };
        delete.Click += async (_, _) =>
        {
            if (MessageBox.Show(window, $"등록번호 {job.ListingNumber}의 글을 아이템매니아에서 삭제할까요?",
                    "사이트 등록글 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            delete.Enabled = verify.Enabled = false;
            onDelete();
            try
            {
                var result = await ActionAsync("click");
                if (!window.IsDisposed) instructions.Text += "\n" + result.Message;
            }
            catch (Exception ex)
            {
                if (!window.IsDisposed) instructions.Text += "\n요청 결과를 확인해 주세요. " + ex.Message;
            }
            finally
            {
                if (!window.IsDisposed) verify.Enabled = true;
            }
        };
        try
        {
            host.Controls.Add(_web);
            _web.Dock = DockStyle.Fill;
            _siteDeletionWindow = window;
            _markSiteDeletionRequested = onDelete;
            return window.ShowDialog(this) == DialogResult.OK;
        }
        finally
        {
            _siteDeletionWindow = null;
            _markSiteDeletionRequested = null;
            parent.Controls.Add(_web);
            _web.Dock = dock;
            _web.Bounds = bounds;
        }
    }

    private void HandleSiteDeletionDialog(CoreWebView2ScriptDialogOpeningEventArgs e)
    {
        if (_siteDeletionWindow is not Form window ||
            !Uri.TryCreate(e.Uri, UriKind.Absolute, out var source) ||
            !ItemManiaAccountStore.IsOfficialSite(source) ||
            !MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri)) return;
        if (e.Kind == CoreWebView2ScriptDialogKind.Alert)
        {
            MessageBox.Show(window, e.Message, "아이템매니아 안내", MessageBoxButtons.OK);
            e.Accept();
        }
        else if (e.Kind == CoreWebView2ScriptDialogKind.Confirm &&
            e.Message.Contains("삭제", StringComparison.Ordinal) &&
            MessageBox.Show(window, e.Message, "아이템매니아 삭제 확인", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes)
        {
            _markSiteDeletionRequested?.Invoke();
            e.Accept();
        }
    }
}
