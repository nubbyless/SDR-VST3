using System;
using System.Collections.Generic;
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
        private RichTextBox txtLog;
        private StatusStrip status;
        private ToolStripStatusLabel lblStatus;
        private Panel inputPanel;
        private ComboBox cboNode;
        private TextBox txtInput;
        private Button btnSend;

        private bool _autoScroll = true;
        private volatile string _selectedId;
        private bool _refreshingNodes;
        private int _inCount;
        private int _outCount;
        private int _lineCount;
        private readonly List<string> _history = new List<string>();
        private int _historyIndex;

        public DxClusterTerminalForm()
        {
            BuildUi();

            DxClusterTheme.ThemeChanged += onThemeChanged;

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
                DxClusterTheme.ThemeChanged -= onThemeChanged;
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
            BuildInputPanel();

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
            Controls.Add(inputPanel);
            Controls.Add(status);

            Load += (s, e) =>
            {
                try { Common.RestoreForm(this, "DxClusterTerminalForm", true); } catch { }
                refreshNodes();
                updateStatus();
                if (txtInput != null) txtInput.Focus();
            };

            VisibleChanged += (s, e) =>
            {
                if (!Visible) return;
                refreshNodes();
                if (txtInput != null) txtInput.Focus();
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

            toolStrip.Items.Add(btnClear);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnAutoScroll);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnCopy);
            toolStrip.Items.Add(btnSave);

            foreach (ToolStripItem item in new ToolStripItem[] { btnClear, btnAutoScroll, btnCopy, btnSave })
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

        private void BuildInputPanel()
        {
            inputPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 32,
                BackColor = DxClusterTheme.Void,
                Padding = new Padding(4, 4, 4, 4),
            };

            txtInput = new TextBox
            {
                Dock = DockStyle.Fill,
                BackColor = DxClusterTheme.Inset,
                ForeColor = DxClusterTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 9f),
                PlaceholderText = "Enter a cluster command...",
            };
            txtInput.KeyDown += onInputKeyDown;

            cboNode = new ComboBox
            {
                Dock = DockStyle.Left,
                Width = 210,
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = DxClusterTheme.Inset,
                ForeColor = DxClusterTheme.TextPrimary,
                FlatStyle = FlatStyle.Flat,
            };
            cboNode.SelectedIndexChanged += onNodeSelected;

            btnSend = new Button
            {
                Dock = DockStyle.Right,
                Width = 64,
                Text = "Send",
                FlatStyle = FlatStyle.Flat,
                ForeColor = DxClusterTheme.TextPrimary,
                BackColor = DxClusterTheme.FaceBottom,
            };
            btnSend.Click += (s, e) => sendCommand();

            inputPanel.Controls.Add(txtInput);
            inputPanel.Controls.Add(cboNode);
            inputPanel.Controls.Add(btnSend);
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
            if (!isSelected(connection)) return;
            append(connection, "IN ", line, DxClusterTheme.TextPrimary);
        }

        private void onRawSent(DxClusterConnection connection, string line)
        {
            if (!isSelected(connection)) return;
            append(connection, "OUT", line, DxClusterTheme.AccentOn);
        }

        private void onStateChanged(DxClusterConnection connection)
        {
            if (isSelected(connection))
            {
                string state = connection.State.ToString();
                if (!string.IsNullOrEmpty(connection.LastError))
                    state += " (" + connection.LastError + ")";

                append(connection, "STATE", state, DxClusterTheme.TextSecondary);
            }

            refreshNodes();
        }

        private bool isSelected(DxClusterConnection connection)
        {
            string selectedId = _selectedId;
            return connection != null && !string.IsNullOrEmpty(selectedId) && connection.Id == selectedId;
        }

        private void onNodeSelected(object sender, EventArgs e)
        {
            if (_refreshingNodes) return;

            NodeItem item = cboNode == null ? null : cboNode.SelectedItem as NodeItem;
            string id = item == null || item.Connection == null ? null : item.Connection.Id;

            if (string.Equals(id, _selectedId, StringComparison.Ordinal)) return;

            _selectedId = id;
            rebuildLog();
        }

        private void append(DxClusterConnection connection, string direction, string line, Color color)
        {
            string host = connection == null ? "" : connection.Host;
            int port = connection == null ? 0 : connection.Port;
            append(DateTime.Now, host, port, direction, line, color);
        }

        private void refreshNodes()
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(refreshNodes)); } catch { }
                return;
            }

            if (cboNode == null || cboNode.IsDisposed) return;

            string selectedId = _selectedId;

            _refreshingNodes = true;
            cboNode.BeginUpdate();
            try
            {
                cboNode.Items.Clear();

                foreach (DxClusterConnection connection in DxClusterManager.Connections)
                    cboNode.Items.Add(new NodeItem(connection));

                if (cboNode.Items.Count > 0)
                {
                    int select = 0;
                    bool found = false;

                    if (!string.IsNullOrEmpty(selectedId))
                    {
                        for (int i = 0; i < cboNode.Items.Count; i++)
                        {
                            NodeItem item = cboNode.Items[i] as NodeItem;
                            if (item != null && item.Connection != null && item.Connection.Id == selectedId)
                            {
                                select = i;
                                found = true;
                                break;
                            }
                        }
                    }

                    if (!found)
                    {
                        for (int i = 0; i < cboNode.Items.Count; i++)
                        {
                            NodeItem item = cboNode.Items[i] as NodeItem;
                            if (item != null && item.Connection != null && item.Connection.State == DxClusterConnectionState.Connected)
                            {
                                select = i;
                                break;
                            }
                        }
                    }

                    cboNode.SelectedIndex = select;
                }
            }
            finally
            {
                _refreshingNodes = false;
                cboNode.EndUpdate();
            }

            NodeItem selected = cboNode.SelectedItem as NodeItem;
            string newId = selected == null || selected.Connection == null ? null : selected.Connection.Id;

            if (!string.Equals(newId, _selectedId, StringComparison.Ordinal))
            {
                _selectedId = newId;
                rebuildLog();
            }
        }

        private void rebuildLog()
        {
            if (IsDisposed || !IsHandleCreated) return;

            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(rebuildLog)); } catch { }
                return;
            }

            if (txtLog == null || txtLog.IsDisposed) return;

            NodeItem item = cboNode == null ? null : cboNode.SelectedItem as NodeItem;
            string host = item == null || item.Connection == null ? null : item.Connection.Host;
            int port = item == null || item.Connection == null ? 0 : item.Connection.Port;

            txtLog.Clear();
            _inCount = 0;
            _outCount = 0;
            _lineCount = 0;

            if (host != null)
            {
                foreach (DxClusterRawLogEntry entry in DxClusterManager.GetRawLog())
                {
                    if (entry.Host != host || entry.Port != port) continue;
                    append(entry.Timestamp, entry.Host, entry.Port, entry.Direction, entry.Text, colorForDirection(entry.Direction));
                }
            }

            updateStatus();
        }

        private void onInputKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                sendCommand();
            }
            else if (e.KeyCode == Keys.Up)
            {
                e.SuppressKeyPress = true;
                historyUp();
            }
            else if (e.KeyCode == Keys.Down)
            {
                e.SuppressKeyPress = true;
                historyDown();
            }
        }

        private void sendCommand()
        {
            string text = txtInput == null ? "" : (txtInput.Text ?? "");
            if (text.Length == 0) return;

            NodeItem item = cboNode == null ? null : cboNode.SelectedItem as NodeItem;
            if (item == null || item.Connection == null)
            {
                if (lblStatus != null) lblStatus.Text = "No cluster node selected";
                return;
            }

            if (item.Connection.State != DxClusterConnectionState.Connected)
            {
                if (lblStatus != null)
                    lblStatus.Text = item.Connection.Host + ":" + item.Connection.Port.ToString() + " is not connected";
                return;
            }

            item.Connection.Send(text);
            addHistory(text);

            txtInput.Text = "";
            txtInput.SelectionStart = 0;
        }

        private void addHistory(string text)
        {
            if (_history.Count == 0 || !string.Equals(_history[_history.Count - 1], text, StringComparison.Ordinal))
                _history.Add(text);

            _historyIndex = _history.Count;
        }

        private void historyUp()
        {
            if (_history.Count == 0) return;

            if (_historyIndex > 0) _historyIndex--;
            txtInput.Text = _history[_historyIndex];
            txtInput.SelectionStart = txtInput.TextLength;
        }

        private void historyDown()
        {
            if (_history.Count == 0) return;

            if (_historyIndex < _history.Count - 1)
            {
                _historyIndex++;
                txtInput.Text = _history[_historyIndex];
            }
            else
            {
                _historyIndex = _history.Count;
                txtInput.Text = "";
            }

            txtInput.SelectionStart = txtInput.TextLength;
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

        private void onThemeChanged()
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke(new Action(applyTheme));
            else applyTheme();
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

            if (txtLog != null)
            {
                txtLog.BackColor = DxClusterTheme.Inset;
                txtLog.ForeColor = DxClusterTheme.TextPrimary;
            }

            if (inputPanel != null) inputPanel.BackColor = DxClusterTheme.Void;

            if (cboNode != null)
            {
                cboNode.BackColor = DxClusterTheme.Inset;
                cboNode.ForeColor = DxClusterTheme.TextPrimary;
            }

            if (txtInput != null)
            {
                txtInput.BackColor = DxClusterTheme.Inset;
                txtInput.ForeColor = DxClusterTheme.TextPrimary;
            }

            if (btnSend != null)
            {
                btnSend.BackColor = DxClusterTheme.FaceBottom;
                btnSend.ForeColor = DxClusterTheme.TextPrimary;
            }

            foreach (ToolStripItem item in new ToolStripItem[] { btnClear, btnAutoScroll, btnCopy, btnSave })
                if (item != null) item.ForeColor = DxClusterTheme.TextPrimary;
        }

        private sealed class NodeItem
        {
            public DxClusterConnection Connection { get; }

            public NodeItem(DxClusterConnection connection)
            {
                Connection = connection;
            }

            public override string ToString()
            {
                if (Connection == null) return "";
                return Connection.Host + ":" + Connection.Port.ToString() + " [" + Connection.State.ToString() + "]";
            }
        }
    }
}
