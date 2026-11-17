using System;
using System.Drawing;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private void OpenSystemNetworkSettings(string system)
    {
        if (system is not ("B" or "C")) return;
        if (system == "B" ? _systemBShown : _systemCShown)
        {
            MessageBox.Show(this,
                $"시스템 {system}의 브라우저가 이미 시작되었습니다. 네트워크 연결을 변경하려면 프로그램을 종료한 후 다시 실행하세요.\n\n현재 실행 중인 물품은 먼저 정상 종료하세요.",
                $"{system} 연결 변경", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var window = new Form
        {
            Text = $"시스템 {system} · 별도 VPN/프록시 연결",
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
                   $"시스템 {system}만 별도 VPN 서비스가 제공한 SOCKS5/HTTP 프록시로 연결합니다.\n" +
                   $"{system}는 별도 매니아 계정·등록목록·실행기록을 사용합니다. 연결 실패 시 등록하지 않습니다."
        };
        var addressLabel = new Label { Left = 23, Top = 111, Width = 115, Height = 27, Text = $"{system} 프록시 주소" };
        var address = new TextBox
        {
            Left = 145, Top = 106, Width = 493,
            Text = DualSystemNetwork.Load(system).ProxyUrl,
            PlaceholderText = system == "B" ? "예: socks5://127.0.0.1:40000" : "예: socks5://127.0.0.1:1080"
        };
        var result = new Label
        {
            Left = 23, Top = 154, Width = 615, Height = 72,
            Text = system == "B"
                ? "B 전용 IP가 현재 인터넷의 IP와 다른지 확인합니다.\n연결이 확인되면 다음 실행부터 저장된 주소를 자동 검증합니다."
                : "C는 A·B와 모두 다른 외부 IP가 필요합니다.\nB와 다른 프록시 서비스의 주소를 입력하고 세 IP를 확인하세요.",
            ForeColor = UiTheme.Muted
        };
        var account = new Button
        {
            Left = 23, Top = 242, Width = 178, Height = 37,
            Text = $"{system} 매니아 계정 설정"
        };
        account.Click += (_, _) =>
        {
            using var login = new ItemManiaAccountForm(system);
            if (login.ShowDialog(window) == DialogResult.OK)
                result.Text = $"{system} 전용 계정정보를 저장했습니다. 다른 시스템 계정에는 영향을 주지 않습니다.";
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
                if (!string.Equals(DualSystemNetwork.Load(system).ProxyUrl, normalized, StringComparison.OrdinalIgnoreCase))
                    DualSystemNetwork.SaveProxy(system, normalized);
                result.Text = $"일반 연결과 {system} 연결의 외부 IP를 확인하는 중입니다...";
                string status;
                if (system == "B")
                {
                    var (direct, b) = await DualSystemNetwork.VerifyBRouteAsync();
                    status = $"시스템 A: {direct}    |    시스템 B: {b}";
                }
                else
                {
                    var (direct, b, c) = await DualSystemNetwork.VerifyCRouteAsync();
                    status = $"A: {direct}    |    B: {b}    |    C: {c}";
                }
                result.ForeColor = Color.ForestGreen;
                result.Text = $"연결 확인 성공 ✓\n{status}\n{system}를 선택하면 독립 브라우저가 시작됩니다.";
            }
            catch (Exception ex)
            {
                result.ForeColor = Color.DarkRed;
                result.Text = $"시스템 {system} 연결 확인 실패: " + ex.Message +
                    $"\n{system}는 기본 연결로 대체하지 않으며 자동등록을 시작하지 않습니다.";
            }
            finally { if (!window.IsDisposed) test.Enabled = true; }
        };
        var close = new Button { Left = 511, Top = 242, Width = 127, Height = 37, Text = "닫기" };
        close.Click += (_, _) => window.Close();
        window.Controls.AddRange(new Control[] { intro, addressLabel, address, result, account, test, close });
        UiTheme.ApplyControlStyles(window);
        window.ShowDialog(this);
    }
}
