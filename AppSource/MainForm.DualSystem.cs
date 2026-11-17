using System;
using System.Drawing;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private void OpenSystemBNetworkSettings()
    {
        if (_systemBShown)
        {
            MessageBox.Show(this,
                "시스템 B의 브라우저가 이미 시작되었습니다. 네트워크 연결을 변경하려면 프로그램을 종료한 후 다시 실행하세요.\n\n현재 실행 중인 물품은 먼저 정상 종료하세요.",
                "B 연결 변경", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var window = new Form
        {
            Text = "시스템 B · 별도 VPN/프록시 연결",
            Font = Font, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(675, 364),
            MaximizeBox = false, MinimizeBox = false,
            BackColor = UiTheme.Surface
        };
        var intro = new Label
        {
            Left = 23, Top = 19, Width = 620, Height = 80,
            Text = "시스템 A는 현재 인터넷을 그대로 사용합니다.\n" +
                   "시스템 B만 VPN 서비스가 제공한 SOCKS5/HTTP 프록시로 연결합니다.\n" +
                   "B는 별도 로그인·등록목록·실행기록을 사용합니다. 프록시 연결이 안 되면 B는 등록하지 않습니다."
        };
        var addressLabel = new Label { Left = 23, Top = 111, Width = 115, Height = 27, Text = "B 프록시 주소" };
        var address = new TextBox
        {
            Left = 145, Top = 106, Width = 493,
            Text = DualSystemNetwork.Load().ProxyUrl,
            PlaceholderText = "예: socks5://127.0.0.1:1080"
        };
        var result = new Label
        {
            Left = 23, Top = 154, Width = 615, Height = 72,
            Text = "연결 테스트를 실행하면 현재 IP와 B 프록시의 외부 IP를 비교합니다.\n서로 다른 IP가 확인되어야 B 자동등록을 시작할 수 있습니다.",
            ForeColor = UiTheme.Muted
        };
        var copyAccount = new Button
        {
            Left = 23, Top = 242, Width = 178, Height = 37,
            Text = "A 계정정보 B에 복사"
        };
        copyAccount.Click += (_, _) =>
        {
            var a = ItemManiaAccountStore.Load("A");
            if (!a.IsConfigured)
            {
                MessageBox.Show(window, "시스템 A에 저장된 아이템매니아 계정정보가 없습니다.");
                return;
            }
            ItemManiaAccountStore.Save(a, "B");
            result.Text = "시스템 A에 저장된 계정정보를 B에 복사했습니다. 브라우저 세션은 서로 공유하지 않습니다.";
        };
        var test = new Button
        {
            Left = 211, Top = 242, Width = 211, Height = 37,
            Text = "연결 저장 + IP 확인", BackColor = UiTheme.SurfaceAlt
        };
        test.Click += async (_, _) =>
        {
            test.Enabled = false;
            try
            {
                string proposed = address.Text.Trim();
                if (!DualSystemNetwork.TryNormalizeProxy(proposed, out var normalized))
                    throw new ArgumentException("SOCKS5 또는 HTTP 주소와 포트를 확인하세요. 예: socks5://127.0.0.1:1080");
                if (!string.Equals(DualSystemNetwork.Load().ProxyUrl, normalized, StringComparison.OrdinalIgnoreCase))
                    DualSystemNetwork.SaveProxy(normalized);
                result.Text = "일반 연결과 B 연결의 외부 IP를 확인하는 중입니다...";
                var (direct, b) = await DualSystemNetwork.VerifyBRouteAsync();
                result.ForeColor = Color.ForestGreen;
                result.Text = $"연결 확인 성공 ✓\n시스템 A: {direct}    |    시스템 B: {b}\nB를 선택하면 해당 연결로 독립 브라우저가 시작됩니다.";
            }
            catch (Exception ex)
            {
                result.ForeColor = Color.DarkRed;
                result.Text = "시스템 B 연결 확인 실패: " + ex.Message +
                    "\nB는 기본 연결로 대체하지 않으며 자동등록을 시작하지 않습니다.";
            }
            finally { if (!window.IsDisposed) test.Enabled = true; }
        };
        var close = new Button { Left = 511, Top = 242, Width = 127, Height = 37, Text = "닫기" };
        close.Click += (_, _) => window.Close();
        window.Controls.AddRange(new Control[] { intro, addressLabel, address, result, copyAccount, test, close });
        UiTheme.ApplyControlStyles(window);
        window.ShowDialog(this);
    }
}
