using System;
using System.Drawing;
using System.Windows.Forms;

namespace LuzenBrowserReplica;

internal sealed partial class MainForm
{
    private void OpenSettingsBackupDialog()
    {
        using var dialog = new Form
        {
            Text = "Luzen 설정 백업·복원", Width = 660, Height = 390,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = new Font("맑은 고딕", 9)
        };
        dialog.Controls.Add(new Label
        {
            Text = "A/B/C 물품·설정은 이 PC에만 보관됩니다. 로그인 정보는 암호화된 상태로 백업합니다.\n" +
                   "복원할 때는 사이트 중복 등록 방지를 위해 자동등록을 끄고, 다음 실행에 적용합니다.",
            Left = 18, Top = 16, Width = 610, Height = 48
        });
        var list = new ListBox { Left = 18, Top = 72, Width = 605, Height = 195,
            Font = new Font("Consolas", 9), HorizontalScrollbar = true };
        void RefreshList()
        {
            list.Items.Clear();
            foreach (string snapshot in UserSettingsSnapshots.ListSnapshots()) list.Items.Add(snapshot);
            if (list.Items.Count > 0) list.SelectedIndex = 0;
        }
        RefreshList();
        dialog.Controls.Add(list);
        var status = new Label { Left = 18, Top = 277, Width = 605, Height = 24, ForeColor = Color.DimGray,
            Text = "복원하면 사이트의 실제 물품 가격·등록글은 변경하지 않습니다." };
        dialog.Controls.Add(status);
        var create = new Button { Text = "지금 백업", Left = 18, Top = 309, Width = 115, Height = 32 };
        create.Click += (_, _) =>
        {
            if (!CanInstallUpdateAcrossSystems)
            { status.Text = "자동등록을 일시정지하고 진행 중인 작업이 끝난 다음 백업하세요."; return; }
            try
            {
                _moduleA.SaveListings(); _moduleB.SaveListings(); _moduleC.SaveListings();
                UserSettingsSnapshots.CreateSnapshot("manual");
                RefreshList();
                status.Text = "로컬 설정 백업 완료. 기존 백업은 그대로 유지됩니다.";
            }
            catch (Exception ex) { status.Text = "백업 실패: " + ex.Message; }
        };
        dialog.Controls.Add(create);
        var restore = new Button { Text = "선택 백업 복원", Left = 379, Top = 309, Width = 132, Height = 32 };
        restore.Click += (_, _) =>
        {
            if (list.SelectedItem is not string filename) return;
            if (!CanInstallUpdateAcrossSystems)
            { status.Text = "A/B/C 자동등록을 일시정지하고 진행 중인 제출이 끝난 뒤 복원하세요."; return; }
            if (MessageBox.Show(dialog,
                "선택한 시점의 로컬 설정으로 복원할까요?\n\n" +
                "복원 전 현재 설정도 백업하며, 모든 자동등록은 꺼집니다. 사이트에서 이미 제출된 글은 변경하지 않습니다.\n" +
                "프로그램을 닫은 뒤 다시 실행하면 복원이 적용됩니다.",
                "설정 백업 복원", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            try
            {
                UserSettingsSnapshots.RequestRestore(filename);
                dialog.Close();
                MessageBox.Show(this, "복원을 예약했습니다. 프로그램을 종료합니다. 다시 실행하면 복원됩니다.",
                    "설정 복원 예약", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex) { status.Text = "복원 예약 실패: " + ex.Message; }
        };
        dialog.Controls.Add(restore);
        var close = new Button { Text = "닫기", Left = 522, Top = 309, Width = 101, Height = 32,
            DialogResult = DialogResult.Cancel };
        dialog.Controls.Add(close);
        dialog.CancelButton = close;
        dialog.ShowDialog(this);
    }
}
