using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace InfiniteAscensionLauncher;

internal sealed partial class LauncherForm
{
        private void BuildUi()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 82, BackColor = Color.FromArgb(20, 18, 30), Padding = new Padding(28, 0, 28, 0) };
            var title = new Label { Text = "INFINITE ASCENSION", AutoSize = true, Font = new Font("Segoe UI Semibold", 19F), ForeColor = Color.FromArgb(244, 242, 250), Location = new Point(28, 17) };
            var subtitle = new Label { Text = "GAME LAUNCHER", AutoSize = true, Font = new Font("Segoe UI", 8.5F), ForeColor = Color.FromArgb(150, 143, 174), Location = new Point(30, 49) };
            serverLabel.AutoSize = true; serverLabel.Font = new Font("Segoe UI Semibold", 9F); serverLabel.ForeColor = Color.FromArgb(180, 174, 198); serverLabel.Location = new Point(760, 18);
            buildLabel.AutoSize = true; buildLabel.Font = new Font("Segoe UI", 9F); buildLabel.ForeColor = Color.FromArgb(130, 124, 150); buildLabel.Location = new Point(760, 43);
            top.Controls.AddRange(new Control[] { title, subtitle, serverLabel, buildLabel });
    
            nav.Dock = DockStyle.Left; nav.Width = 184; nav.BackColor = Color.FromArgb(15, 14, 23); nav.Padding = new Padding(12, 18, 12, 0);
            AddNavButton("HOME", 18); AddNavButton("SETTINGS", 68); AddNavButton("LOGS", 118);
    
            content.Dock = DockStyle.Fill; content.Padding = new Padding(30, 28, 30, 30);
            home.Dock = DockStyle.Fill; home.BackColor = Color.FromArgb(10, 9, 16); content.Controls.Add(home);
    
            var hero = new Panel { Dock = DockStyle.Top, Height = 166, BackColor = Color.FromArgb(27, 23, 42), Padding = new Padding(26, 22, 26, 18) };
            hero.Controls.Add(new Label { Text = "READY FOR ASCENSION", AutoSize = true, Font = new Font("Segoe UI Semibold", 23F), ForeColor = Color.FromArgb(246, 244, 252), Location = new Point(24, 20) });
            statusLabel.Text = "Checking update service…"; statusLabel.AutoSize = false; statusLabel.Width = 820; statusLabel.Height = 46; statusLabel.Location = new Point(26, 70); statusLabel.Font = new Font("Segoe UI", 10F); statusLabel.ForeColor = Color.FromArgb(184, 178, 202); statusLabel.AutoEllipsis = true;
            hero.Controls.Add(statusLabel);
            home.Controls.Add(hero);
    
            var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(0, 20, 0, 0), WrapContents = false, BackColor = Color.Transparent };
            ConfigureButton(playButton, "PLAY", 150, true); ConfigureButton(updateButton, "UPDATE", 135, false); ConfigureButton(repairButton, "REPAIR", 135, false);
            ConfigureButton(stopButton, "STOP", 110, false); ConfigureButton(folderButton, "GAME FOLDER", 150, false);
            actions.Controls.AddRange(new Control[] { playButton, updateButton, repairButton, stopButton, folderButton }); home.Controls.Add(actions);
    
            var info = new Panel { Dock = DockStyle.Top, Height = 108, BackColor = Color.FromArgb(19, 17, 29), Padding = new Padding(20, 16, 20, 14) };
            var infoTitle = new Label { Text = "INSTALLATION", AutoSize = true, Font = new Font("Segoe UI Semibold", 9F), ForeColor = Color.FromArgb(158, 151, 181), Location = new Point(20, 14) };
            var infoText = new Label { AutoSize = false, Width = 850, Height = 30, Font = new Font("Segoe UI", 9.5F), ForeColor = Color.FromArgb(211, 207, 221), Location = new Point(20, 40), Text = "Local build 0  •  Remote build —  •  Verified packages" };
            info.Controls.AddRange(new Control[] { infoTitle, infoText });
            home.Controls.Add(info);
    
            progress.Dock = DockStyle.Top; progress.Height = 18; progress.Style = ProgressBarStyle.Continuous; progress.Margin = new Padding(0, 16, 0, 0); home.Controls.Add(progress);
            progressLabel.Dock = DockStyle.Top; progressLabel.Height = 34; progressLabel.Padding = new Padding(2, 7, 0, 0); progressLabel.ForeColor = Color.FromArgb(139, 133, 158); home.Controls.Add(progressLabel);
    
            playButton.Click += async (_, _) => await PlayAsync();
            updateButton.Click += async (_, _) => await UpdateAsync(true);
            repairButton.Click += async (_, _) => await RepairAsync();
            stopButton.Click += (_, _) => StopGame();
            folderButton.Click += (_, _) => OpenGameFolder();
    
            Controls.Add(content); Controls.Add(nav); Controls.Add(top);
        }

        private void AddNavButton(string text, int top)
        {
            var button = new Button { Text = text, Width = 160, Height = 40, Left = 12, Top = top, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(26, 23, 38), ForeColor = Color.FromArgb(207, 202, 219), FlatAppearance = { BorderColor = Color.FromArgb(53, 48, 71), BorderSize = 1 } };
            button.Click += (_, _) => ShowSection(text); nav.Controls.Add(button);
        }

        private static void ConfigureButton(Button button, string text, int width, bool primary)
        {
            button.Text = text; button.Width = width; button.Height = 46; button.FlatStyle = FlatStyle.Flat;
            button.BackColor = primary ? Color.FromArgb(91, 67, 145) : Color.FromArgb(37, 33, 54);
            button.ForeColor = Color.White; button.Margin = new Padding(0, 0, 10, 0);
            button.FlatAppearance.BorderColor = primary ? Color.FromArgb(132, 102, 197) : Color.FromArgb(62, 56, 82);
            button.FlatAppearance.BorderSize = 1;
        }

        private void ShowSection(string section)
        {
            if (section == "HOME") { content.Controls.Clear(); content.Controls.Add(home); return; }
            if (section == "LOGS") { OpenLogs(); return; }
            using var form = new Form { Text = "Infinite Ascension — Settings", Width = 620, Height = 360, StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor };
            var auto = new CheckBox { Text = "Automatic game updates", Checked = autoUpdate, AutoSize = true, Left = 26, Top = 28 };
            var close = new Button { Text = "Close", Width = 100, Left = 26, Top = 80 };
            close.Click += (_, _) => { autoUpdate = auto.Checked; form.Close(); }; form.Controls.Add(auto); form.Controls.Add(close); form.ShowDialog(this);
        }
}
