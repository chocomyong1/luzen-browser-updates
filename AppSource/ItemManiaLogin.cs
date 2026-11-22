using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LuzenBrowserReplica;

// ItemMania account information is separate from the original LuzenSoft/local-mode login.
// Only DPAPI-encrypted passwords are persisted; cookies are kept in a dedicated WebView2 profile.
internal sealed class ItemManiaCredentials
{
    public string UserId { get; set; } = "";
    public string PasswordProtected { get; set; } = "";
    public bool AutoLoginEnabled { get; set; } = true;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(UserId) && !string.IsNullOrWhiteSpace(PasswordProtected);
}

internal static class ItemManiaAccountStore
{
    private static string PathName(string system) => Path.Combine(Settings.AutomationDirectory(system), "itemmania_account.json");
    public static int Revision { get; private set; }
    public static ItemManiaCredentials Load(string system = "A")
    {
        try
        {
            if (!File.Exists(PathName(system))) return new();
            return JsonSerializer.Deserialize<ItemManiaCredentials>(File.ReadAllText(PathName(system))) ?? new();
        }
        catch { return new(); }
    }
    public static void Save(ItemManiaCredentials account, string system = "A")
    {
        Directory.CreateDirectory(Settings.AutomationDirectory(system));
        var temp = PathName(system) + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(account, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, PathName(system), overwrite: true);
        Revision++;
    }
    public static void Clear(string system = "A")
    {
        if (File.Exists(PathName(system))) File.Delete(PathName(system));
        Revision++;
    }
    public static bool IsOfficialSite(Uri? uri) =>
        uri is not null && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("itemmania.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".itemmania.com", StringComparison.OrdinalIgnoreCase));
}

internal sealed class ItemManiaAccountForm : Form
{
    private readonly TextBox _id = new() { Width = 275 };
    private readonly TextBox _password = new() { Width = 275, UseSystemPasswordChar = true };
    private readonly CheckBox _enabled = new() { Text = "다음 실행부터 자동 로그인 (이 Windows 사용자만)", AutoSize = true, Checked = true };
    private readonly ItemManiaCredentials _existing;
    private readonly string _system;

    public ItemManiaAccountForm(string system = "A")
    {
        _system = system;
        _existing = ItemManiaAccountStore.Load(system);
        Text = $"시스템 {system} · 아이템매니아 로그인 설정";
        Width = 485; Height = 350;
        MinimumSize = MaximumSize = Size;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Font = new Font("맑은 고딕", 9);
        Controls.Add(new Label
        {
            Left = 25, Top = 17, Width = 425, Height = 46,
            Text = $"시스템 {system}에서만 사용할 아이템매니아 계정입니다.\nWindows 암호화(DPAPI)로 저장하며 다른 시스템과 공유하지 않습니다."
        });
        Controls.Add(new Label { Text = "아이디", Left = 25, Top = 86, Width = 90, Height = 28 });
        _id.Left = 132; _id.Top = 82; _id.Text = _existing.UserId;
        Controls.Add(_id);
        Controls.Add(new Label { Text = "비밀번호", Left = 25, Top = 124, Width = 90, Height = 28 });
        _password.Left = 132; _password.Top = 121;
        // Existing password is never displayed back in plaintext or placed into the form.
        _password.PlaceholderText = _existing.IsConfigured ? "변경하지 않으려면 비워 두세요" : "처음 한 번 입력";
        Controls.Add(_password);
        _enabled.Left = 129; _enabled.Top = 164; _enabled.Checked = _existing.AutoLoginEnabled;
        Controls.Add(_enabled);
        var info = new Label
        {
            Left = 25, Top = 196, Width = 425, Height = 47,
            ForeColor = Color.FromArgb(90, 98, 108),
            Text = "세션이 유효하면 자동으로 로그인 상태를 유지합니다.\n만료 시 저장한 계정으로 재로그인을 시도하며 추가 인증은 수동 처리합니다."
        };
        Controls.Add(info);
        var save = new Button { Text = "계정 저장", Left = 132, Top = 260, Width = 166, Height = 32 };
        save.Click += (_, _) => SaveAccount();
        Controls.Add(save); AcceptButton = save;
        var clear = new Button { Text = "계정 삭제", Left = 308, Top = 260, Width = 106, Height = 32 };
        clear.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "저장한 아이템매니아 계정정보를 삭제할까요?\n기존 브라우저 쿠키까지 삭제하지는 않습니다.", "확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ItemManiaAccountStore.Clear(_system);
            DialogResult = DialogResult.Cancel; Close();
        };
        Controls.Add(clear);
    }

    private void SaveAccount()
    {
        var user = _id.Text.Trim();
        if (string.IsNullOrWhiteSpace(user)) { MessageBox.Show(this, "아이디를 입력하세요."); return; }
        string encrypted = _existing.PasswordProtected;
        if (!string.Equals(user, _existing.UserId, StringComparison.Ordinal)) encrypted = "";
        try
        {
            if (_password.Text.Length > 0) encrypted = PasswordVault.Protect(_password.Text);
            if (string.IsNullOrWhiteSpace(encrypted))
            {
                MessageBox.Show(this, "처음 연결할 때는 비밀번호를 입력해야 합니다."); return;
            }
            ItemManiaAccountStore.Save(new ItemManiaCredentials
            {
                UserId = user, PasswordProtected = encrypted, AutoLoginEnabled = _enabled.Checked
            }, _system);
            _password.Clear();
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { MessageBox.Show(this, "계정 설정 저장 실패: " + ex.Message); }
    }
}

internal static class ItemManiaWebViewProfile
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<CoreWebView2Environment>>> Environments = new();
    public static Task<CoreWebView2Environment> GetAsync(string system = "A") =>
        Environments.GetOrAdd(system, key => new Lazy<Task<CoreWebView2Environment>>(() =>
            CoreWebView2Environment.CreateAsync(null,
                Path.Combine(Settings.AutomationDirectory(key), "ItemManiaWebView2"),
                key is "B" or "C"
                    ? DualSystemNetwork.BrowserOptionsFor(key)
                    // Do not inherit a second VPN client's Windows-wide proxy
                    // settings for A's independent direct browser connection.
                    : new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--no-proxy-server" }))).Value;
}

// Detects a recognizable login form on the *official HTTPS ItemMania domain only*.
// It never fills a third-party site, never bypasses 2FA/CAPTCHA, and tries at most
// once per 10 minutes to avoid repeated attempts after invalid credentials.
internal sealed class ItemManiaAutoLogin : IDisposable
{
    private readonly WebView2 _view;
    private readonly string _system;
    private readonly Action<string>? _notify;
    private readonly System.Windows.Forms.Timer _watcher = new() { Interval = 2500 };
    private DateTime _lastAttemptUtc = DateTime.MinValue;
    private DateTime _lastOpenLoginUtc = DateTime.MinValue;
    private DateTime _lastLoginNavigationUtc = DateTime.MinValue;
    private int _accountRevision = ItemManiaAccountStore.Revision;
    private bool _checking;
    private bool _disposed;

    // The login link and the submitted login form can both navigate the same
    // WebView that reposting uses. Give the authentication flow a quiet window
    // before a listing starts its own navigation.
    public bool IsLoginNavigationBusy => _checking ||
        DateTime.UtcNow - _lastLoginNavigationUtc < TimeSpan.FromSeconds(3);

    // Only a definite login-required response from the official site should
    // invalidate this throttle. A prior login click does not prove the session
    // is still authenticated; the server may expire it minutes later.
    public void ResetAfterOfficialLoginRejection()
    {
        _lastAttemptUtc = DateTime.MinValue;
        _lastOpenLoginUtc = DateTime.MinValue;
    }

    // A verified redirect away from MY registered listings proves that the
    // login state needs checking. The normal ten-minute submit throttle is
    // too long for an expired session, but never retry bad credentials in a
    // rapid loop: leave at least two minutes after a real login form submit.
    public void ObserveOwnedListLoginRedirect()
    {
        if (DateTime.UtcNow - _lastAttemptUtc >= TimeSpan.FromMinutes(2))
            _lastAttemptUtc = DateTime.MinValue;
        // Keep the existing homepage/login-link throttle in place.
    }

    public async Task WaitForLoginNavigationAsync(CancellationToken token)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(18);
        while (IsLoginNavigationBusy)
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow >= deadline)
                throw new InvalidOperationException("아이템매니아 로그인 페이지 이동이 완료되지 않았습니다. 잠시 후 자동으로 다시 시도합니다.");
            await Task.Delay(200, token);
        }
    }

    public ItemManiaAutoLogin(WebView2 view, Action<string>? notify = null, string system = "A")
    {
        _view = view; _notify = notify; _system = system;
        _view.NavigationCompleted += OnNavigationComplete;
        _watcher.Tick += async (_, _) => await TryLoginAsync();
        _watcher.Start();
        _view.Disposed += (_, _) => Dispose();
    }

    private async void OnNavigationComplete(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        await TryLoginAsync();
    }

    public async Task TryLoginAsync()
    {
        if (_checking || _disposed || _view.IsDisposed || _view.CoreWebView2 is null) return;
        if (!ItemManiaAccountStore.IsOfficialSite(_view.Source)) return;
        if (_accountRevision != ItemManiaAccountStore.Revision)
        {
            _accountRevision = ItemManiaAccountStore.Revision;
            _lastAttemptUtc = DateTime.MinValue;
            _lastOpenLoginUtc = DateTime.MinValue;
        }
        if (DateTime.UtcNow - _lastAttemptUtc < TimeSpan.FromMinutes(10)) return;
        var account = ItemManiaAccountStore.Load(_system);
        if (!account.IsConfigured || !account.AutoLoginEnabled) return;
        var password = PasswordVault.Unprotect(account.PasswordProtected);
        if (password.Length == 0) { _notify?.Invoke("저장된 아이템매니아 비밀번호를 복호화할 수 없습니다. 계정을 다시 설정하세요."); return; }
        _checking = true;
        try
        {
            // Escaped as JS string literals. Do not log or persist the generated script.
            var userJs = JsonSerializer.Serialize(account.UserId);
            var passJs = JsonSerializer.Serialize(password);
            var script = """
                (() => {
                  if (location.protocol !== 'https:' || !/(^|\.)itemmania\.com$/i.test(location.hostname)) return 'untrusted-origin';
                  const user = __ITEMMANIA_USER__;
                  const pass = __ITEMMANIA_PASSWORD__;
                  const visible = e => !!(e && e.getClientRects().length && !e.disabled && !e.readOnly);
                  const passwords = [...document.querySelectorAll('input[type="password"]')].filter(visible);
                  // More than one password field usually means reset/change-password: do not touch.
                  if (passwords.length !== 1) return 'no-login-form';
                  const password = passwords[0];
                  const form = password.form || password.closest('form');
                  if (!form) return 'no-login-form';
                  const context = [location.pathname, form.getAttribute('action') || '', form.id, form.className].join(' ');
                  const options = [...form.querySelectorAll('input:not([type="hidden"]):not([type="password"])')]
                    .filter(visible).filter(e => /^(text|email|tel|)$/i.test(e.type));
                  const idInput = options.find(e => /(^|\W)(id|uid|userid|user_id|login|account|member)(\W|$)/i.test(
                       [e.name, e.id, e.getAttribute('autocomplete') || ''].join(' '))) ||
                    (options.length === 1 ? options[0] : null);
                  if (!idInput) return 'no-login-form';
                  const buttons = [...form.querySelectorAll('button, input[type="submit"], a[role="button"]')].filter(visible);
                  const submit = buttons.find(e => /로그인|log\s*in|sign\s*in/i.test(
                       [e.textContent || '', e.value || '', e.getAttribute('aria-label') || ''].join(' '))) ||
                    (buttons.length === 1 && /^(submit)$/i.test(buttons[0].type) ? buttons[0] : null);
                  if (!submit || !(/login|sign|member|로그인/i.test(context) || /로그인|log\s*in|sign\s*in/i.test(submit.textContent || submit.value || '')))
                    return 'no-login-form';
                  if (document.querySelector('iframe[src*="captcha"], [class*="captcha"], [id*="captcha"]')) return 'needs-verification';
                  const setValue = (el, value) => {
                    const descriptor = Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value');
                    descriptor.set.call(el, value);
                    el.dispatchEvent(new Event('input', { bubbles: true }));
                    el.dispatchEvent(new Event('change', { bubbles: true }));
                  };
                  setValue(idInput, user);
                  setValue(password, pass);
                  if (submit.tagName === 'BUTTON' || submit.tagName === 'INPUT' || submit.tagName === 'A') submit.click();
                  return 'submitted';
                })()
                """.Replace("__ITEMMANIA_USER__", userJs, StringComparison.Ordinal)
                     .Replace("__ITEMMANIA_PASSWORD__", passJs, StringComparison.Ordinal);
            string response = await _view.CoreWebView2.ExecuteScriptAsync(script);
            var result = JsonSerializer.Deserialize<string>(response) ?? "";
            if (result == "submitted")
            {
                _lastAttemptUtc = DateTime.UtcNow;
                _lastLoginNavigationUtc = DateTime.UtcNow;
                _notify?.Invoke("백그라운드 아이템매니아 로그인 요청 완료. 결과 확인은 등록 작업 상태로 판단합니다.");
            }
            else if (result == "needs-verification")
            {
                _lastAttemptUtc = DateTime.UtcNow;
                _notify?.Invoke("아이템매니아 추가 인증이 필요합니다. 자동 작업을 진행할 수 없습니다.");
            }
            else if (result == "no-login-form" && DateTime.UtcNow - _lastOpenLoginUtc > TimeSpan.FromMinutes(2))
            {
                await OpenLoginLinkFromHomepageAsync();
            }
        }
        catch (Exception ex)
        {
            // Navigation can interrupt script execution. Never print scripts/credentials.
            if (!_disposed) _notify?.Invoke("자동 로그인 페이지 확인 중 오류: " + ex.GetType().Name);
        }
        finally { _checking = false; }
    }

    private async Task OpenLoginLinkFromHomepageAsync()
    {
        if (!ItemManiaAccountStore.IsOfficialSite(_view.Source)) return;
        // Only on the front page. Clicking an arbitrary Login-labelled link from a listing
        // or checkout page could disrupt the user's own work.
        string path = _view.Source!.AbsolutePath.Trim('/').ToLowerInvariant();
        if (path.Length > 0 && path != "index.html" && path != "main" && path != "main/index.html") return;
        const string findLogin = """
            (() => {
              if (document.querySelector('a[href*="logout"], button[onclick*="logout"]')) return 'already-signed-in';
              const elements = [...document.querySelectorAll('a, button')];
              const link = elements.find(el => {
                const text = (el.textContent || '').trim().replace(/\s+/g, ' ');
                if (!/^(로그인|login|log in|sign in)$/i.test(text)) return false;
                if (!el.getClientRects().length) return false;
                const href = (el.getAttribute('href') || '').trim().toLowerCase();
                if (href && href !== '#' && !href.startsWith('javascript:')) {
                  try {const u = new URL(href, location.href); if (u.protocol !== 'https:' || !/(^|\.)itemmania\.com$/i.test(u.hostname)) return false;}
                  catch {return false;}
                }
                return true;
              });
              if (!link) return 'not-found';
              link.click();
              return 'opened-login';
            })()
            """;
        string answer = await _view.CoreWebView2.ExecuteScriptAsync(findLogin);
        if (JsonSerializer.Deserialize<string>(answer) == "opened-login")
        {
            _lastOpenLoginUtc = DateTime.UtcNow;
            _lastLoginNavigationUtc = DateTime.UtcNow;
            _notify?.Invoke("백그라운드에서 아이템매니아 로그인 절차를 진행 중입니다.");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _watcher.Stop(); _watcher.Dispose();
        _view.NavigationCompleted -= OnNavigationComplete;
    }
}

// Handles site login popups inside the same isolated, persistent profile. Leaving
// them in a new system browser would lose the app's session/cookies.
internal static class ItemManiaPopupHandler
{
    // V8 no-website-UI mode: consume requests for pop-up windows instead of
    // creating a Form. Same-domain links can be handled inside the existing
    // background WebView2. External domains and unsupported auth popups are
    // blocked: never bypass verification or silently open a visible browser.
    public static void InstallSilent(WebView2 view, Action<string>? notify,
        Func<bool>? approvedPriceEditInProgress = null)
    {
        if (view.CoreWebView2 is null) return;
        view.CoreWebView2.NewWindowRequested += async (_, args) =>
        {
            // NewWindowRequested is NOT a navigation. Redirecting a window.open()
            // into the opener destroys window.opener and any completion callback
            // installed by the official site. Retain an authentic child browsing
            // context for an explicitly approved, exact-ID price edit instead.
            // Ordinary login/navigation popups retain the previous policy.
            if (approvedPriceEditInProgress?.Invoke() == true &&
                view.Source?.AbsolutePath.Equals("/myroom/buy/buy_re_reg.html",
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                await OpenApprovedSitePopupAsync(view, args, notify);
                return;
            }
            args.Handled = true;
            if (!Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri) ||
                !ItemManiaAccountStore.IsOfficialSite(uri))
            {
                notify?.Invoke("외부 팝업 또는 추가 인증이 필요하여 자동 팝업을 열지 않았습니다.");
                return;
            }
            notify?.Invoke("사이트의 새 창 요청을 백그라운드 페이지에서 처리합니다.");
            view.CoreWebView2?.Navigate(uri.AbsoluteUri);
        };
    }

    private static async Task OpenApprovedSitePopupAsync(WebView2 opener,
        CoreWebView2NewWindowRequestedEventArgs args, Action<string>? notify)
    {
        // A site may first open about:blank and then set its location. Only
        // this initial blank target or an HTTPS ItemMania URL is acceptable.
        var requested = args.Uri ?? "";
        var blank = requested.Length == 0 ||
            string.Equals(requested, "about:blank", StringComparison.OrdinalIgnoreCase);
        if (!blank && (!Uri.TryCreate(requested, UriKind.Absolute, out var requestedUri) ||
            !ItemManiaAccountStore.IsOfficialSite(requestedUri)))
        {
            args.Handled = true;
            notify?.Invoke("구매글 가격 수정 중 외부 사이트 새 창 요청 차단");
            return;
        }

        var deferral = args.GetDeferral();
        Form? host = null;
        try
        {
            if (opener.CoreWebView2 is null) throw new InvalidOperationException("원본 브라우저 연결 없음");
            host = new OfficialSitePopupWindow();
            var ownedHost = host;
            var child = new WebView2 { Dock = DockStyle.Fill };
            host.Controls.Add(child);
            host.Show(); // A real native HWND is required for the child WebView.
            await child.EnsureCoreWebView2Async(opener.CoreWebView2.Environment);
            if (child.CoreWebView2 is null) throw new InvalidOperationException("새 창 브라우저 연결 없음");

            child.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            child.CoreWebView2.NavigationStarting += (_, navigation) =>
            {
                if (string.Equals(navigation.Uri, "about:blank", StringComparison.OrdinalIgnoreCase)) return;
                if (Uri.TryCreate(navigation.Uri, UriKind.Absolute, out var target) &&
                    ItemManiaAccountStore.IsOfficialSite(target)) return;
                navigation.Cancel = true;
                notify?.Invoke("가격 수정 새 창의 외부 이동을 차단했습니다");
            };
            child.CoreWebView2.WindowCloseRequested += (_, _) =>
            {
                if (!ownedHost.IsDisposed) ownedHost.Close();
            };
            // Never automatically accept a payment, security challenge or
            // registration confirmation from the child browsing context.
            // Its own first-party JavaScript is responsible for any callback
            // to the original edit page; the automator never sends a POST.
            args.NewWindow = child.CoreWebView2;
            args.Handled = true;
            notify?.Invoke("사이트 가격 수정 새 창을 원본 창과 연결된 별도 브라우저로 처리합니다");
            host = null; // The site closes the child via window.close().
        }
        catch (Exception ex)
        {
            args.Handled = true;
            notify?.Invoke("사이트 가격 수정 새 창 생성 실패: " + ex.GetType().Name);
        }
        finally
        {
            if (host is not null && !host.IsDisposed) host.Close();
            deferral.Complete();
        }
    }

    // Retains the site opener relationship without a new foreground modal.
    // Site payment and account verification dialogs remain disabled; a flow
    // that requires explicit consent must fail closed rather than be accepted.
    private sealed class OfficialSitePopupWindow : Form
    {
        protected override bool ShowWithoutActivation => true;

        public OfficialSitePopupWindow()
        {
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(-20000, -20000);
            Size = new Size(960, 720);
            FormBorderStyle = FormBorderStyle.None;
        }
    }

}
