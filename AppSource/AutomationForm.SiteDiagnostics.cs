using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class AutomationForm
{
    // Does not export HTML, account IDs, input values, passwords, cookies or tokens.
    // Users may inspect this JSON before sharing it to diagnose site-selector changes.
    private async Task ExportSiteDiagnosticsAsync()
    {
        if (_busy || _schedulerRunning)
        { MessageBox.Show(this, "작업을 일시정지한 후 사이트 진단을 실행하세요."); return; }
        try
        {
            await _web.EnsureCoreWebView2Async(await ItemManiaWebViewProfile.GetAsync(_system));
            // Navigate to the selected listing's own page or the configured
            // official registration form; no submit actions are performed.
            if (_selected is not null)
            {
                var flow = _config.WorkflowFor(EffectiveOperation(_selected));
                var target = EntryFor(_selected, flow);
                if (IsAllowedUrl(target) &&
                    !string.Equals(_web.Source?.AbsoluteUri, target, StringComparison.OrdinalIgnoreCase))
                    await NavigateOwnedListAsync(target, System.Threading.CancellationToken.None);
            }
            if (_web.CoreWebView2 is null || !IsAllowedCurrentPage())
            { MessageBox.Show(this, "로그인된 아이템매니아 공식 사이트가 아직 열리지 않았습니다. 잠시 후 다시 시도하세요."); return; }
            const string script = """
            (() => {
              const norm=x=>String(x||'').replace(/\s+/g,' ').trim().slice(0,65);
              const visible=e=>e.getClientRects().length>0&&getComputedStyle(e).visibility!=='hidden';
              const fields=[...document.querySelectorAll('input,select,textarea,[role="combobox"]')]
                .filter(e=>visible(e)&&!['hidden','password','email','tel','file'].includes((e.type||'').toLowerCase()))
                .slice(0,120).map(e=>({tag:e.tagName.toLowerCase(),type:norm(e.type),
                  name:norm(e.getAttribute('name')),id:norm(e.id),
                  label:norm([...(e.labels||[])].map(x=>x.innerText||x.textContent).join(' ')),
                  placeholder:norm(e.getAttribute('placeholder')),aria:norm(e.getAttribute('aria-label'))}));
              const actions=[...document.querySelectorAll('button,input[type="button"],input[type="submit"],a[href],a[role="button"]')]
                .filter(visible).slice(0,90).map(e=>({tag:e.tagName.toLowerCase(),text:norm(e.innerText||e.value),
                  id:norm(e.id),type:norm(e.type)}));
              const u=new URL(location.href);
              return {diagnosticVersion:22,site:u.hostname,pathname:u.pathname.slice(0,140),
                title:norm(document.title),loginFormPresent:!!document.querySelector('input[type="password"]'),
                fieldCount:fields.length,fields,actions};
            })()
            """;
            string raw = await _web.CoreWebView2.ExecuteScriptAsync(script);
            using var parsed = JsonDocument.Parse(raw);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("페이지 진단 결과가 잘못되었습니다.");
            object report = parsed.RootElement.Clone();
            if (_selected is not null)
            {
                var identity = await InspectCurrentIdentityAsync(_selected, System.Threading.CancellationToken.None);
                OwnedListRepostResult? ownedRow = MyListingsImport.IsOwnedListUrl(_web.Source?.AbsoluteUri)
                    ? await InspectOwnedListRepostAsync(_selected, false, System.Threading.CancellationToken.None)
                    : null;
                report = new
                {
                    Page = parsed.RootElement.Clone(),
                    OwnedListRowCheck = ownedRow is null ? null : new { ownedRow.Status, ownedRow.Message },
                    ListingCheck = new
                    {
                        NumberMatches = string.Equals(identity.Number, _selected.ListingNumber, StringComparison.Ordinal),
                        identity.TitlePresent,
                        identity.DetailPage,
                        identity.Source,
                        identity.Error
                    }
                };
            }
            using var save = new SaveFileDialog { Title = "아이템매니아 등록화면 진단 저장", Filter = "JSON 파일 (*.json)|*.json", FileName = "luzen_site_diagnostic.json" };
            if (save.ShowDialog(this) != DialogResult.OK) return;
            File.WriteAllText(save.FileName, JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }));
            AppendLog("사이트 진단 파일 저장 완료 (계정정보/입력값/쿠키 제외)");
            MessageBox.Show(this, "페이지 입력칸과 버튼 이름만 저장했습니다. 파일 내용을 검토한 뒤 공유하세요.\n" + save.FileName,
                "사이트 진단", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("사이트 진단 실패: " + ex.GetType().Name);
            MessageBox.Show(this, "사이트 진단 실패: " + ex.Message);
        }
    }
}
