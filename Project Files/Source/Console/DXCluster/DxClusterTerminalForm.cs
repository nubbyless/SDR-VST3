using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace Thetis
{
    public class DxClusterTerminalForm : Form
    {
        private const int MaxLines = 5000;
        private const int TrimChunk = 500;

        private ToolStrip toolStrip;
        private ToolStripButton btnClear;
        private ToolStripButton btnAutoScroll;
        private ToolStripButton btnCopy;
        private ToolStripButton btnSave;
        private ToolStripButton btnTheme;
        private ToolStripLabel lblTheme;
        private RichTextBox txtLog;
        private StatusStrip status;
        private ToolStripStatusLabel lblStatus;

        private bool _autoScroll = true;
        private bool _replayed;
        private int _inCount;
        private int _outCount;
        private int _lineCount;

        public DxClusterTerminalForm()
        {
            BuildUi();

            DxClusterManager.RawLineReceived += onRawReceived;
            DxClusterManager.RawLineSent += onRawSent;
            DxClusterManager.ConnectionStateChanged += onStateChanged;

            FormClosing += (s, e) =>
            {
                try { Common.SaveForm(this, "DxClusterTerminalForm"); } catch { }

                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    Hide();
                }
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DxClusterManager.RawLineReceived -= onRawReceived;
                DxClusterManager.RawLineSent -= onRawSent;
                DxClusterManager.ConnectionStateChanged -= onStateChanged;
            }
            base.Dispose(disposing);
        }

        private void BuildUi()
        {
            Text = "DX Cluster Terminal";
            MinimumSize = new Size(520, 300);
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(900, 520);
            BackColor = DxClusterTheme.Void;
            ForeColor = DxClusterTheme.TextPrimary;
            Font = new Font("Segoe UI", 8.5f);

            DxClusterTheme.SetDark(DxClusterManager.Settings.DarkTheme);

            BuildToolStrip();
            BuildStatusBar();

            txtLog = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                WordWrap = false,
                DetectUrls = false,
                HideSelection = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f),
                BackColor = DxClusterTheme.Inset,
                ForeColor = DxClusterTheme.TextPrimary,
            };

            Controls.Add(txtLog);
            Controls.Add(toolStrip);
            Controls.Add(status);

            Load += (s, e) =>
            {
                try { Common.RestoreForm(this, "DxClusterTerminalForm", true); } catch { }
                replayHistory();
                UpdateThemeButton();
                updateStatus();
            };

            applyTheme();
        }

        private void BuildToolStrip()
        {
            toolStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                BackColor = DxClusterTheme.FaceTop,
                ForeColor = DxClusterTheme.TextPrimary,
            };

            btnClear = CreateButton("Clear", onClear);

            btnAutoScroll = new ToolStripButton("Auto-scroll")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor = DxClusterTheme.TextPrimary,
                CheckOnClick = true,
                Checked = true,
                ToolTipText = "Scroll to the newest line automatically",
            };
            btnAutoScroll.CheckStateChanged += (s, e) => _autoScroll = btnAutoScroll.Checked;

            btnCopy = CreateButton("Copy All", onCopy);
            btnSave = CreateButton("Save Log...", onSave);

            btnTheme = new ToolStripButton
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor = DxClusterTheme.TextPrimary,
                ToolTipText = "Toggle light/dark theme",
            };
            btnTheme.Click += onThemeToggle;

            lblTheme = new ToolStripLabel("Theme:")
            {
                Alignment = ToolStripItemAlignment.Right,
                ForeColor = DxClusterTheme.TextSecondary,
            };

            toolStrip.Items.Add(btnClear);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnAutoScroll);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnCopy);
            toolStrip.Items.Add(btnSave);
            toolStrip.Items.Add(btnTheme);
            toolStrip.Items.Add(lblTheme);

            foreach (ToolStripItem item in new ToolStripItem[] { btnClear, btnAutoScroll, btnCopy, btnSave, btnTheme })
                item.Margin = new Padding(7, 2, 7, 2);
        }

        private void BuildStatusBar()
        {
            status = new StatusStrip
            {
                SizingGrip = false,
                BackColor = DxClusterTheme.FaceTop,
                ForeColor = DxClusterTheme.TextSecondary,
            };

            lblStatus = new ToolStripStatusLabel("") { ForeColor = DxClusterTheme.TextSecondary };
            status.Items.Add(lblStatus);
        }

        private static ToolStripButton CreateButton(string text, EventHandler handler)
        {
            ToolStripButton button = new ToolStripButton(text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor = DxClusterTheme.TextPrimary,
            };
            button.Click += handler;
            return button;
        }

        private void onRawReceived(DxClusterConnection connection, string line)
        {
            append(connection, "IN ", line, DxClusterTheme.TextPrimary);
        }

        private void onRawSent(DxClusterConnection connection, string line)
        {
            append(connection, "OUT", line, DxClusterTheme.AccentOn);
        }

        private void onStateChanged(DxClusterConnection connection)
        {
            string state = connection.State.ToString();
            if (!string.IsNullOrEmpty(connection.LastError))
                state += " (" + connection.LastError + ")";

            append(connection, "STATE", state, DxClusterTheme.TextSecondary);
        }

        private void append(DxClusterConnection connection, string direction, string line, Color color)
        {
            string host = connection == null ? "" : connection.Host;
            int port = connection == null ? 0 : connection.Port;
            append(DateTime.Now, host, port, direction, line, color);
        }

        private void append(DateTime timestamp, string host, int port, string direction, string line, Color color)
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke(new Action(() => append(timestamp, host, port, direction, line, color)));
                }
                catch
                {
                }
                return;
            }

            if (txtLog == null || txtLog.IsDisposed) return;

            string node = string.IsNullOrEmpty(host) ? "" : host + ":" + port.ToString();
            string text = timestamp.ToString("HH:mm:ss.fff") + " " + node + " " + direction + " " + (line ?? "");
            text = text.Replace("\r", "").Replace("\n", " ");

            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = color;
            txtLog.AppendText(text + Environment.NewLine);
            txtLog.SelectionColor = DxClusterTheme.TextPrimary;

            if (direction == "IN ") _inCount++;
            else if (direction == "OUT") _outCount++;

            _lineCount++;
            trimLog();

            if (_autoScroll)
            {
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }

            updateStatus();
        }

        private void replayHistory()
        {
            if (_replayed) return;
            _replayed = true;

            foreach (DxClusterRawLogEntry entry in DxClusterManager.GetRawLog())
                append(entry.Timestamp, entry.Host, entry.Port, entry.Direction, entry.Text, colorForDirection(entry.Direction));
        }

        private static Color colorForDirection(string direction)
        {
            if (direction == "OUT") return DxClusterTheme.AccentOn;
            if (direction == "STATE") return DxClusterTheme.TextSecondary;
            return DxClusterTheme.TextPrimary;
        }

        private void trimLog()
        {
            if (_lineCount <= MaxLines) return;

            int remove = _lineCount - MaxLines + TrimChunk;
            int index = txtLog.GetFirstCharIndexFromLine(remove);
            if (index <= 0) return;

            txtLog.Select(0, index);
            txtLog.SelectedText = "";
            _lineCount -= remove;
        }

        private void updateStatus()
        {
            if (lblStatus == null) return;

            lblStatus.Text = "IN: " + _inCount.ToString() + "    OUT: " + _outCount.ToString() + "    Lines: " + _lineCount.ToString();
        }

        private void onClear(object sender, EventArgs e)
        {
            txtLog.Clear();
            _inCount = 0;
            _outCount = 0;
            _lineCount = 0;
            updateStatus();
        }

        private void onCopy(object sender, EventArgs e)
        {
            try
            {
                if (txtLog.TextLength > 0)
                    Clipboard.SetText(txtLog.Text);
            }
            catch
            {
            }
        }

        private void onSave(object sender, EventArgs e)
        {
            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*";
                dialog.FileName = "dxcluster-terminal-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log";

                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.WriteAllText(dialog.FileName, txtLog.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Save Log", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void onThemeToggle(object sender, EventArgs e)
        {
            DxClusterTheme.SetDark(!DxClusterTheme.IsDark);
            DxClusterManager.Settings.DarkTheme = DxClusterTheme.IsDark;
            DxClusterManager.SaveSettings();

            applyTheme();
            UpdateThemeButton();
        }

        private void UpdateThemeButton()
        {
            if (btnTheme != null)
                btnTheme.Text = DxClusterTheme.IsDark ? "Light" : "Dark";
        }

        private void applyTheme()
        {
            BackColor = DxClusterTheme.Void;
            ForeColor = DxClusterTheme.TextPrimary;

            if (toolStrip != null)
            {
                toolStrip.BackColor = DxClusterTheme.FaceTop;
                toolStrip.ForeColor = DxClusterTheme.TextPrimary;
            }

            if (status != null)
            {
                status.BackColor = DxClusterTheme.FaceTop;
                status.ForeColor = DxClusterTheme.TextSecondary;
            }

            if (lblStatus != null) lblStatus.ForeColor = DxClusterTheme.TextSecondary;
            if (lblTheme != null) lblTheme.ForeColor = DxClusterTheme.TextSecondary;

            if (txtLog != null)
            {
                txtLog.BackColor = DxClusterTheme.Inset;
                txtLog.ForeColor = DxClusterTheme.TextPrimary;
            }

            foreach (ToolStripItem item in new ToolStripItem[] { btnClear, btnAutoScroll, btnCopy, btnSave, btnTheme })
                if (item != null) item.ForeColor = DxClusterTheme.TextPrimary;
        }
    }
}
