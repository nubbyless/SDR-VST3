/*  DxClusterForm.cs

This file is part of a program that implements a Software-Defined Radio.

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
*/
//
//============================================================================================//
// DX Cluster - modeless popup window.                                                         //
// Top: connection controls + a live node list (configured nodes, with per-node auto-connect,  //
// panadapter visibility and color). Middle: band/text filters. Bottom: live cluster spot     //
// list. All UI is built in code (same pattern as FreeDVReporterForm); window geometry and the  //
// node list persist (geometry via Common.SaveForm, nodes via DxClusterManager settings).       //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Thetis
{
    public class DxClusterForm : Form
    {
        private const string SourcePrefix = DxClusterManager.SourcePrefix;

        // cap on rows shown in the spot list, so a busy feed cannot flood the grid
        private const int MaxDisplayedSpots = 500;

        private const int COL_CONN_NODE = 0;
        private const int COL_CONN_STATE = 1;
        private const int COL_CONN_AUTO = 2;
        private const int COL_CONN_PANA = 3;
        private const int COL_CONN_COLOR = 4;

        private const int COL_SPOT_TIME = 0;
        private const int COL_SPOT_MHZ = 1;
        private const int COL_SPOT_CALL = 2;
        private const int COL_SPOT_SPOTTER = 3;
        private const int COL_SPOT_COMMENT = 4;
        private const int COL_SPOT_NODE = 5;

        private readonly Console _console;

        private ToolStrip toolStrip;
        private ToolStripButton btnDisconnectAll;
        private ToolStripButton btnServers;
        private ToolStripButton btnShowOnPanadapter;
        private ToolStripButton btnClear;

        private ToolStrip filterStrip;
        private ToolStripComboBox comboBand;
        private ToolStripTextBox txtFilter;
        private ToolStripButton btnFilterReset;

        private SplitContainer split;
        private DataGridView gridConns;
        private DataGridView gridSpots;
        private ContextMenuStrip connMenu;
        private ToolTip connTip;
        private int _tipCol = -1;
        private int _tipRow = int.MinValue;
        private StatusStrip status;
        private ToolStripStatusLabel lblStatus;
        private System.Windows.Forms.Timer refreshTimer;

        private readonly Dictionary<string, DataGridViewRow> _connRowByKey = new Dictionary<string, DataGridViewRow>(StringComparer.Ordinal);
        private readonly Dictionary<long, DataGridViewRow> _spotRowById = new Dictionary<long, DataGridViewRow>();

        private bool _updatingConns;
        private bool _updatingFilters;

        private string _bandFilter = "All";
        private string _textFilter = "";

        public DxClusterForm(Console console)
        {
            _console = console;

            BuildUi();

            FormClosing += (s, e) =>
            {
                try { Common.SaveForm(this, "DxClusterForm"); } catch { }
                try { DxClusterManager.SaveSettings(); } catch { }

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
                try { refreshTimer?.Stop(); } catch { }
            }
            base.Dispose(disposing);
        }

        private void BuildUi()
        {
            Text = "DX Cluster";
            MinimumSize = new Size(760, 420);
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(960, 580);
            BackColor = DxClusterTheme.Void;
            ForeColor = DxClusterTheme.TextPrimary;
            Font = new Font("Segoe UI", 8.5f);

            BuildToolStrip();
            BuildFilterStrip();
            BuildSplit();
            BuildStatusBar();

            Controls.Add(split);
            Controls.Add(filterStrip);
            Controls.Add(toolStrip);
            Controls.Add(status);

            Load += (s, e) =>
            {
                try { split.SplitterDistance = 150; } catch { }
                PrefillCallsign();
            };

            refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
            refreshTimer.Tick += (s, e) => { RefreshConnections(); RefreshSpots(); };
            refreshTimer.Start();

            try { Common.RestoreForm(this, "DxClusterForm", true); } catch { }
        }

        private void BuildToolStrip()
        {
            toolStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new ToolStripProfessionalRenderer(new DxColorTable()),
                BackColor = DxClusterTheme.FaceTop,
                ForeColor = DxClusterTheme.TextPrimary,
                RenderMode = ToolStripRenderMode.ManagerRenderMode,
            };

            btnServers = CreateButton("Server List", onServers);
            btnServers.ToolTipText = "Add, connect and remove cluster nodes, and set your login callsign";

            btnDisconnectAll = CreateButton("Disconnect All", onDisconnectAll);

            btnShowOnPanadapter = new ToolStripButton("Spots on Panadapter")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor = DxClusterTheme.TextPrimary,
                CheckOnClick = true,
                ToolTipText = "Draw DX cluster spots on the panadapter (requires Setup > TCI Server > Show TCI Spots)",
            };
            btnShowOnPanadapter.CheckStateChanged += onTogglePanadapter;

            btnClear = CreateButton("Clear Spots", onClearSpots);

            toolStrip.Items.Add(btnServers);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnDisconnectAll);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnShowOnPanadapter);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnClear);
        }

        private void BuildFilterStrip()
        {
            filterStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new ToolStripProfessionalRenderer(new DxColorTable()),
                BackColor = DxClusterTheme.FaceBottom,
                ForeColor = DxClusterTheme.TextPrimary,
                RenderMode = ToolStripRenderMode.ManagerRenderMode,
            };

            ToolStripLabel lblBand = new ToolStripLabel("Band:") { ForeColor = DxClusterTheme.TextSecondary };

            comboBand = new ToolStripComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 90,
                FlatStyle = FlatStyle.Flat,
            };
            comboBand.Items.AddRange(new object[]
            {
                "All", "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m"
            });
            comboBand.BackColor = DxClusterTheme.Inset;
            comboBand.ForeColor = DxClusterTheme.TextPrimary;
            comboBand.SelectedIndexChanged += onFilterChanged;

            ToolStripLabel lblText = new ToolStripLabel("Filter:") { ForeColor = DxClusterTheme.TextSecondary };

            txtFilter = new ToolStripTextBox { Width = 180, ToolTipText = "Show only spots whose callsign, spotter or comment contains this text" };
            StyleInput(txtFilter);
            txtFilter.TextChanged += onFilterChanged;

            btnFilterReset = CreateButton("Reset", onFilterReset);

            filterStrip.Items.Add(lblBand);
            filterStrip.Items.Add(comboBand);
            filterStrip.Items.Add(new ToolStripSeparator());
            filterStrip.Items.Add(lblText);
            filterStrip.Items.Add(txtFilter);
            filterStrip.Items.Add(btnFilterReset);

            loadFilterState();
        }

        private void BuildSplit()
        {
            split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                Panel1MinSize = 90,
                SplitterWidth = 6,
                BackColor = DxClusterTheme.Void,
            };

            gridConns = createGrid();
            gridConns.ReadOnly = false;

            gridConns.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Node",
                Width = 220,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
            gridConns.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "State",
                Width = 260,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
            });
            gridConns.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "Auto",
                Width = 45,
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ToolTipText = "Connect automatically on startup",
            });
            gridConns.Columns.Add(new DataGridViewCheckBoxColumn
            {
                HeaderText = "Pana",
                Width = 45,
                ReadOnly = false,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ToolTipText = "Draw this node's spots on the panadapter",
            });
            gridConns.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "Color",
                Width = 70,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ToolTipText = "Click to choose this node's spot color",
            });

            gridConns.CellValueChanged += onConnCellValueChanged;
            gridConns.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (gridConns.IsCurrentCellDirty)
                    gridConns.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            gridConns.CellClick += onConnCellContentClick;
            gridConns.CellDoubleClick += onConnDoubleClick;

            connMenu = new ContextMenuStrip();
            connMenu.ShowItemToolTips = true;
            ToolStripMenuItem miConnect = new ToolStripMenuItem("Connect") { ToolTipText = "Open this node's connection" };
            miConnect.Click += (s, e) => connectSelectedNode();
            ToolStripMenuItem miDisconnect = new ToolStripMenuItem("Disconnect") { ToolTipText = "Close this node's connection" };
            miDisconnect.Click += (s, e) => onDisconnectSelected(s, e);
            ToolStripMenuItem miRename = new ToolStripMenuItem("Remove Node") { ToolTipText = "Remove this node from the list" };
            miRename.Click += (s, e) => removeSelectedNode();
            connMenu.Items.Add(miConnect);
            connMenu.Items.Add(miDisconnect);
            connMenu.Items.Add(new ToolStripSeparator());
            connMenu.Items.Add(miRename);
            gridConns.ContextMenuStrip = connMenu;

            gridConns.ShowCellToolTips = false;
            connTip = new ToolTip { InitialDelay = 600, ReshowDelay = 200, AutoPopDelay = 8000 };
            gridConns.MouseMove += onConnMouseMove;
            gridConns.MouseLeave += (s, e) => hideConnTip();

            split.Panel1.Controls.Add(gridConns);

            gridSpots = createGrid();

            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time (UTC)", Width = 90, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "MHz",
                Width = 80,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight,
                    Format = "F4",
                },
            });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Callsign", Width = 100, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Spotter", Width = 100, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comment", Width = 300, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Node", Width = 170, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });

            gridSpots.CellDoubleClick += onSpotDoubleClick;

            split.Panel2.Controls.Add(gridSpots);
        }

        private DataGridView createGrid()
        {
            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AllowUserToOrderColumns = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
                BorderStyle = BorderStyle.None,
                BackgroundColor = DxClusterTheme.Inset,
                GridColor = DxClusterTheme.Border,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 24,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = DxClusterTheme.FaceTop,
                    ForeColor = DxClusterTheme.TextPrimary,
                    SelectionBackColor = DxClusterTheme.FaceTop,
                    SelectionForeColor = DxClusterTheme.TextPrimary,
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = DxClusterTheme.Inset,
                    ForeColor = DxClusterTheme.TextPrimary,
                    SelectionBackColor = DxClusterTheme.Selection,
                    SelectionForeColor = DxClusterTheme.TextPrimary,
                },
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = DxClusterTheme.Gutter,
                    ForeColor = DxClusterTheme.TextPrimary,
                    SelectionBackColor = DxClusterTheme.Selection,
                    SelectionForeColor = DxClusterTheme.TextPrimary,
                },
                RowTemplate = new DataGridViewRow { Height = 18 },
            };

            EnableDoubleBuffer(grid);
            return grid;
        }

        private static void EnableDoubleBuffer(Control control)
        {
            try
            {
                typeof(Control)
                    .GetProperty("DoubleBuffered", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.SetValue(control, true, null);
            }
            catch { }
        }

        private void BuildStatusBar()
        {
            status = new StatusStrip { SizingGrip = false, BackColor = DxClusterTheme.FaceTop, ForeColor = DxClusterTheme.TextSecondary };
            lblStatus = new ToolStripStatusLabel("0 node(s), 0 spot(s)") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            status.Items.Add(lblStatus);
        }

        private ToolStripButton CreateButton(string text, EventHandler handler)
        {
            ToolStripButton button = new ToolStripButton(text)
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor = DxClusterTheme.TextPrimary,
            };
            button.Click += handler;
            return button;
        }

        private static void StyleInput(ToolStripTextBox box)
        {
            box.BackColor = DxClusterTheme.Inset;
            box.ForeColor = DxClusterTheme.TextPrimary;
            box.BorderStyle = BorderStyle.FixedSingle;
        }

        private string defaultCallsign()
        {
            if (!string.IsNullOrWhiteSpace(DxClusterManager.Settings.LoginCallsign))
                return DxClusterManager.Settings.LoginCallsign.Trim();

            try
            {
                if (_console != null && !_console.IsSetupFormNull)
                {
                    string call = _console.SetupForm.TCIOwnCallsign;
                    if (!string.IsNullOrWhiteSpace(call)) return call.Trim();
                }
            }
            catch { }

            return "";
        }

        private void PrefillCallsign()
        {
            try { btnShowOnPanadapter.Checked = DxClusterManager.Settings.ShowOnPanadapter; } catch { }
        }

        private void loadFilterState()
        {
            _updatingFilters = true;
            try
            {
                _bandFilter = string.IsNullOrEmpty(DxClusterManager.Settings.FilterBand) ? "All" : DxClusterManager.Settings.FilterBand;
                _textFilter = DxClusterManager.Settings.FilterText ?? "";

                int index = comboBand.Items.IndexOf(_bandFilter);
                comboBand.SelectedIndex = index >= 0 ? index : 0;
                txtFilter.Text = _textFilter;
            }
            catch { }
            finally { _updatingFilters = false; }
        }

        private static DxClusterConnection findLiveConnection(string host, int port)
        {
            string key = DxClusterManager.NodeKey(host, port);
            return DxClusterManager.Connections.FirstOrDefault(c => DxClusterManager.NodeKey(c.Host, c.Port) == key);
        }

        private void onServers(object sender, EventArgs e)
        {
            string callsign = defaultCallsign();

            List<DxClusterServerEntry> entries = new List<DxClusterServerEntry>();

            foreach (DxClusterServerEntry def in DxClusterDefaults.Servers)
                entries.Add(new DxClusterServerEntry(def.Name, def.Host, def.Port));

            // include any already-configured nodes that are not in the built-in list
            foreach (DxClusterNodeSettings node in DxClusterManager.Settings.Nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.Host)) continue;

                bool known = entries.Any(en =>
                    DxClusterManager.NodeKey(en.Host, en.Port) == DxClusterManager.NodeKey(node.Host, node.Port));

                if (!known) entries.Add(new DxClusterServerEntry(node.Host, node.Host, node.Port));
            }

            using (DxClusterServersForm dialog = new DxClusterServersForm(entries, callsign))
            {
                dialog.ShowDialog(this);
            }

            // the dialog applies check/untick changes live (connect/disconnect + spot cleanup),
            // so just refresh the views
            RefreshConnections();
            RefreshSpots();
        }

        private string selectedNodeKey()
        {
            try
            {
                DataGridViewRow row = gridConns?.CurrentRow;
                return row?.Tag as string;
            }
            catch { return null; }
        }

        private void connectSelectedNode()
        {
            string key = selectedNodeKey();
            if (string.IsNullOrEmpty(key)) return;

            DxClusterNodeSettings node = DxClusterManager.FindNodeByKey(key);
            if (node == null) return;

            DxClusterConnection existing = findLiveConnection(node.Host, node.Port);
            if (existing != null)
            {
                if (existing.State == DxClusterConnectionState.Disconnected || existing.State == DxClusterConnectionState.Error)
                    existing.Connect();
            }
            else
            {
                DxClusterManager.AddConnection(node.Host, node.Port, DxClusterManager.Settings.LoginCallsign).Connect();
            }

            RefreshConnections();
        }

        private void onDisconnectSelected(object sender, EventArgs e)
        {
            string key = selectedNodeKey();
            if (string.IsNullOrEmpty(key))
            {
                lblStatus.Text = "Select a node in the list first.";
                return;
            }

            DxClusterConnection connection = DxClusterManager.Connections.FirstOrDefault(c =>
                DxClusterManager.NodeKey(c.Host, c.Port) == key);

            if (connection != null) DxClusterManager.RemoveConnection(connection.Id);

            RefreshConnections();
        }

        private void onDisconnectAll(object sender, EventArgs e)
        {
            foreach (DxClusterConnection connection in DxClusterManager.Connections)
                DxClusterManager.RemoveConnection(connection.Id);

            RefreshConnections();
        }

        private void removeSelectedNode()
        {
            string key = selectedNodeKey();
            if (string.IsNullOrEmpty(key)) return;

            DxClusterConnection connection = DxClusterManager.Connections.FirstOrDefault(c =>
                DxClusterManager.NodeKey(c.Host, c.Port) == key);
            if (connection != null) DxClusterManager.RemoveConnection(connection.Id);

            DxClusterNodeSettings node = DxClusterManager.FindNodeByKey(key);
            if (node != null) DxClusterManager.Settings.Nodes.Remove(node);

            DxClusterManager.SaveSettings();
            DxClusterManager.ResyncPanadapter();

            RefreshConnections();
            RefreshSpots();
        }

        private void onTogglePanadapter(object sender, EventArgs e)
        {
            DxClusterManager.Settings.ShowOnPanadapter = btnShowOnPanadapter.Checked;
            DxClusterManager.SaveSettings();
            DxClusterManager.ResyncPanadapter();
            RefreshSpots();
        }

        private void onClearSpots(object sender, EventArgs e)
        {
            DxClusterManager.ClearSpots();

            _spotRowById.Clear();
            gridSpots.Rows.Clear();
        }

        private void onFilterChanged(object sender, EventArgs e)
        {
            if (_updatingFilters) return;

            _bandFilter = comboBand.SelectedItem as string ?? "All";
            _textFilter = (txtFilter.Text ?? "").Trim();

            DxClusterManager.Settings.FilterBand = _bandFilter;
            DxClusterManager.Settings.FilterText = txtFilter.Text ?? "";
            DxClusterManager.SaveSettings();

            RefreshSpots();
        }

        private void onFilterReset(object sender, EventArgs e)
        {
            _updatingFilters = true;
            try
            {
                comboBand.SelectedIndex = 0;
                txtFilter.Text = "";
                _bandFilter = "All";
                _textFilter = "";
                DxClusterManager.Settings.FilterBand = "All";
                DxClusterManager.Settings.FilterText = "";
                DxClusterManager.SaveSettings();
            }
            finally { _updatingFilters = false; }

            RefreshSpots();
        }

        private void onConnCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != COL_CONN_COLOR) return;

            string key = gridConns.Rows[e.RowIndex].Tag as string;
            DxClusterNodeSettings node = DxClusterManager.FindNodeByKey(key);
            if (node == null) return;

            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = Color.FromArgb(node.ColorArgb);
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                node.ColorArgb = dialog.Color.ToArgb();
            }

            DxClusterConnection connection = DxClusterManager.Connections.FirstOrDefault(c =>
                DxClusterManager.NodeKey(c.Host, c.Port) == key);
            if (connection != null) connection.SpotColor = Color.FromArgb(node.ColorArgb);

            DxClusterManager.SaveSettings();
            DxClusterManager.ResyncPanadapter();
            RefreshConnections();
        }

        private void onConnCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_updatingConns) return;
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            string key = gridConns.Rows[e.RowIndex].Tag as string;
            DxClusterNodeSettings node = DxClusterManager.FindNodeByKey(key);
            if (node == null) return;

            if (e.ColumnIndex == COL_CONN_AUTO)
            {
                node.AutoConnect = gridConns.Rows[e.RowIndex].Cells[COL_CONN_AUTO].Value is bool b && b;
                DxClusterManager.SaveSettings();
            }
            else if (e.ColumnIndex == COL_CONN_PANA)
            {
                bool visible = gridConns.Rows[e.RowIndex].Cells[COL_CONN_PANA].Value is bool b && b;
                node.ShowOnPanadapter = visible;

                DxClusterConnection connection = DxClusterManager.Connections.FirstOrDefault(c =>
                    DxClusterManager.NodeKey(c.Host, c.Port) == key);
                if (connection != null) connection.ShowOnPanadapter = visible;

                DxClusterManager.SaveSettings();
                DxClusterManager.ResyncPanadapter();
            }
        }

        private void onConnMouseMove(object sender, MouseEventArgs e)
        {
            DataGridView.HitTestInfo hit = gridConns.HitTest(e.X, e.Y);

            bool onCell = hit.Type == DataGridViewHitTestType.Cell;
            bool onHeader = hit.Type == DataGridViewHitTestType.ColumnHeader;
            if (hit.ColumnIndex < 0 || (!onCell && !onHeader))
            {
                hideConnTip();
                return;
            }

            string text = connTipText(hit.ColumnIndex);
            if (string.IsNullOrEmpty(text))
            {
                hideConnTip();
                return;
            }

            // only refresh the tooltip when the hover target changes
            if (hit.ColumnIndex == _tipCol && hit.RowIndex == _tipRow) return;

            _tipCol = hit.ColumnIndex;
            _tipRow = hit.RowIndex;

            connTip.Show(text, gridConns, e.X + 16, e.Y + 20, 8000);
        }

        private void hideConnTip()
        {
            connTip.Hide(gridConns);
            _tipCol = -1;
            _tipRow = int.MinValue;
        }

        private static string connTipText(int columnIndex)
        {
            switch (columnIndex)
            {
                case COL_CONN_NODE:
                case COL_CONN_STATE:
                    return "Right-click a node for Connect / Disconnect / Remove Node";
                case COL_CONN_AUTO:
                    return "Connect automatically on startup";
                case COL_CONN_PANA:
                    return "Draw this node's spots on the panadapter";
                case COL_CONN_COLOR:
                    return "Click to choose this node's spot color";
                default:
                    return null;
            }
        }

        private void onConnDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0) return;
            if (e.ColumnIndex == COL_CONN_COLOR) return;

            string key = gridConns.Rows[e.RowIndex].Tag as string;
            if (string.IsNullOrEmpty(key)) return;

            DxClusterConnection connection = DxClusterManager.Connections.FirstOrDefault(c =>
                DxClusterManager.NodeKey(c.Host, c.Port) == key);

            if (connection != null)
                DxClusterManager.RemoveConnection(connection.Id);
            else
            {
                DxClusterNodeSettings node = DxClusterManager.FindNodeByKey(key);
                if (node != null)
                    DxClusterManager.AddConnection(node.Host, node.Port, DxClusterManager.Settings.LoginCallsign).Connect();
            }

            RefreshConnections();
        }

        private void onSpotDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || _console == null) return;

            try
            {
                DataGridViewRow row = gridSpots.Rows[e.RowIndex];

                object mhzValue = row.Cells[COL_SPOT_MHZ].Value;
                if (mhzValue == null) return;

                double mhz = Convert.ToDouble(mhzValue, CultureInfo.InvariantCulture);
                if (mhz <= 0) return;

                string call = row.Cells[COL_SPOT_CALL].Value as string ?? "";
                long hz = (long)Math.Round(mhz * 1000000.0);

                DSPMode mode = SpotManager2.SpotModeNumberToDSPMode(0, hz);
                if (mode != DSPMode.FIRST && _console.RX1DSPMode != mode) _console.RX1DSPMode = mode;
                if (_console.VFOAFreq != mhz) _console.VFOAFreq = mhz;

                _console.SpotClickedHandlers?.Invoke(call, hz, 1, false);
            }
            catch { }
        }

        private void RefreshConnections()
        {
            if (gridConns == null || IsDisposed || !IsHandleCreated) return;

            _updatingConns = true;
            try
            {
                DxClusterNodeSettings[] nodes = DxClusterManager.Settings.Nodes.ToArray();
                HashSet<string> live = new HashSet<string>(StringComparer.Ordinal);

                foreach (DxClusterNodeSettings node in nodes)
                {
                    if (node == null || string.IsNullOrWhiteSpace(node.Host)) continue;

                    string key = DxClusterManager.NodeKey(node.Host, node.Port);
                    live.Add(key);

                    DxClusterConnection connection = DxClusterManager.Connections.FirstOrDefault(c =>
                        DxClusterManager.NodeKey(c.Host, c.Port) == key);

                    string state = connection == null ? "Disconnected" : connection.State.ToString();
                    if (connection != null && connection.State == DxClusterConnectionState.Error && !string.IsNullOrEmpty(connection.LastError))
                        state += ": " + connection.LastError;

                    DataGridViewRow row;
                    if (!_connRowByKey.TryGetValue(key, out row) || row.Index < 0)
                    {
                        int index = gridConns.Rows.Add();
                        row = gridConns.Rows[index];
                        row.Tag = key;
                        _connRowByKey[key] = row;
                    }

                    row.Cells[COL_CONN_NODE].Value = node.Host + ":" + node.Port;
                    row.Cells[COL_CONN_STATE].Value = state;
                    row.Cells[COL_CONN_AUTO].Value = node.AutoConnect;
                    row.Cells[COL_CONN_PANA].Value = node.ShowOnPanadapter;
                    row.Cells[COL_CONN_COLOR].Value = "";
                    row.Cells[COL_CONN_COLOR].Style.BackColor = Color.FromArgb(node.ColorArgb);
                    row.Cells[COL_CONN_COLOR].Style.SelectionBackColor = Color.FromArgb(node.ColorArgb);
                }

                for (int i = gridConns.Rows.Count - 1; i >= 0; i--)
                {
                    string key = gridConns.Rows[i].Tag as string;
                    if (key != null && live.Contains(key)) continue;

                    gridConns.Rows.RemoveAt(i);
                    if (key != null) _connRowByKey.Remove(key);
                }
            }
            catch { }
            finally { _updatingConns = false; }
        }

        private bool passesFilter(DxClusterSpot spot)
        {
            if (_bandFilter != "All" && bandOf(spot.FrequencyHz) != _bandFilter) return false;

            if (!string.IsNullOrEmpty(_textFilter))
            {
                string t = _textFilter.ToUpperInvariant();
                if ((spot.Callsign ?? "").ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0 &&
                    (spot.Spotter ?? "").ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0 &&
                    (spot.Comment ?? "").ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0)
                    return false;
            }

            return true;
        }

        private static string bandOf(long hz)
        {
            double m = hz / 1000000.0;

            if (m >= 1.8 && m <= 2.0) return "160m";
            if (m >= 3.5 && m <= 4.0) return "80m";
            if (m >= 5.0 && m <= 5.5) return "60m";
            if (m >= 7.0 && m <= 7.3) return "40m";
            if (m >= 10.1 && m <= 10.15) return "30m";
            if (m >= 14.0 && m <= 14.35) return "20m";
            if (m >= 18.068 && m <= 18.168) return "17m";
            if (m >= 21.0 && m <= 21.45) return "15m";
            if (m >= 24.89 && m <= 24.99) return "12m";
            if (m >= 28.0 && m <= 29.7) return "10m";
            if (m >= 50.0 && m <= 54.0) return "6m";

            return "";
        }

        private void RefreshSpots()
        {
            if (gridSpots == null || IsDisposed || !IsHandleCreated) return;

            try
            {
                DxClusterSpot[] spots = DxClusterManager.GetSpots(); // oldest -> newest

                // collect the newest matching spots, capped so a busy feed cannot flood the grid
                List<DxClusterSpot> visible = new List<DxClusterSpot>();
                for (int i = spots.Length - 1; i >= 0 && visible.Count < MaxDisplayedSpots; i--)
                {
                    DxClusterSpot spot = spots[i];
                    if (spot == null || !passesFilter(spot)) continue;
                    visible.Add(spot); // newest first
                }

                HashSet<long> live = new HashSet<long>();
                foreach (DxClusterSpot spot in visible) live.Add(spot.Id);

                gridSpots.SuspendLayout();
                try
                {
                    // insert oldest-of-the-new batch first so the newest ends up on top
                    for (int i = visible.Count - 1; i >= 0; i--)
                    {
                        DxClusterSpot spot = visible[i];
                        if (_spotRowById.TryGetValue(spot.Id, out DataGridViewRow existing) && existing.Index >= 0)
                            continue;

                        gridSpots.Rows.Insert(0, buildSpotRow(spot));
                    }

                    for (int i = gridSpots.Rows.Count - 1; i >= 0; i--)
                    {
                        object tag = gridSpots.Rows[i].Tag;
                        if (tag is long id && live.Contains(id)) continue;

                        gridSpots.Rows.RemoveAt(i);
                        if (tag is long rid) _spotRowById.Remove(rid);
                    }
                }
                finally { gridSpots.ResumeLayout(); }

                int nodeCount = DxClusterManager.Settings.Nodes.Count;
                int connectionCount = DxClusterManager.Connections.Length;

                string text = nodeCount + " node(s), " + connectionCount + " connected, " + visible.Count + " shown"
                    + (visible.Count >= MaxDisplayedSpots ? " (newest " + MaxDisplayedSpots + ")" : "");
                if (!DxClusterManager.Settings.ShowOnPanadapter)
                    text += "   |   panadapter display off";

                lblStatus.Text = text;
            }
            catch { }
        }

        private DataGridViewRow buildSpotRow(DxClusterSpot spot)
        {
            DataGridViewRow row = new DataGridViewRow();
            row.CreateCells(gridSpots);
            row.Tag = spot.Id;
            row.Cells[COL_SPOT_TIME].Value = spot.UtcTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            row.Cells[COL_SPOT_MHZ].Value = spot.FrequencyHz / 1000000.0;
            row.Cells[COL_SPOT_CALL].Value = spot.Callsign;
            row.Cells[COL_SPOT_SPOTTER].Value = spot.Spotter;
            row.Cells[COL_SPOT_COMMENT].Value = spot.Comment;
            row.Cells[COL_SPOT_NODE].Value = spot.Node;

            DxClusterNodeSettings node = DxClusterManager.FindNodeByKey(spot.NodeKey);
            if (node != null)
                row.Cells[COL_SPOT_CALL].Style.ForeColor = Color.FromArgb(node.ColorArgb);

            _spotRowById[spot.Id] = row;
            return row;
        }

        // color table for the dark tool strips
        private sealed class DxColorTable : ProfessionalColorTable
        {
            public DxColorTable() { UseSystemColors = false; }

            public override Color ToolStripBorder { get { return DxClusterTheme.Border; } }
            public override Color ToolStripGradientBegin { get { return DxClusterTheme.FaceTop; } }
            public override Color ToolStripGradientMiddle { get { return DxClusterTheme.FaceTop; } }
            public override Color ToolStripGradientEnd { get { return DxClusterTheme.FaceBottom; } }
            public override Color MenuStripGradientBegin { get { return DxClusterTheme.Void; } }
            public override Color MenuStripGradientEnd { get { return DxClusterTheme.Void; } }
            public override Color ToolStripDropDownBackground { get { return DxClusterTheme.Inset; } }
            public override Color ImageMarginGradientBegin { get { return DxClusterTheme.Inset; } }
            public override Color ImageMarginGradientMiddle { get { return DxClusterTheme.Inset; } }
            public override Color ImageMarginGradientEnd { get { return DxClusterTheme.Inset; } }
            public override Color MenuItemSelected { get { return DxClusterTheme.IconHover; } }
            public override Color MenuItemBorder { get { return DxClusterTheme.SelectionBorder; } }
            public override Color MenuItemSelectedGradientBegin { get { return DxClusterTheme.IconHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return DxClusterTheme.IconHover; } }
            public override Color ButtonSelectedHighlight { get { return DxClusterTheme.Inset; } }
            public override Color ButtonSelectedHighlightBorder { get { return DxClusterTheme.SelectionBorder; } }
            public override Color ButtonSelectedGradientBegin { get { return DxClusterTheme.Inset; } }
            public override Color ButtonSelectedGradientMiddle { get { return DxClusterTheme.Inset; } }
            public override Color ButtonSelectedGradientEnd { get { return DxClusterTheme.Inset; } }
            public override Color ButtonPressedHighlight { get { return DxClusterTheme.Inset; } }
            public override Color ButtonPressedHighlightBorder { get { return DxClusterTheme.SelectionBorder; } }
            public override Color ButtonPressedGradientBegin { get { return DxClusterTheme.Inset; } }
            public override Color ButtonPressedGradientMiddle { get { return DxClusterTheme.Inset; } }
            public override Color ButtonPressedGradientEnd { get { return DxClusterTheme.Inset; } }
            public override Color ButtonCheckedGradientBegin { get { return DxClusterTheme.Inset; } }
            public override Color ButtonCheckedGradientMiddle { get { return DxClusterTheme.Inset; } }
            public override Color ButtonCheckedGradientEnd { get { return DxClusterTheme.Inset; } }
            public override Color ButtonCheckedHighlight { get { return DxClusterTheme.Inset; } }
            public override Color ButtonCheckedHighlightBorder { get { return DxClusterTheme.AccentOn; } }
            public override Color SeparatorDark { get { return DxClusterTheme.Border; } }
            public override Color SeparatorLight { get { return DxClusterTheme.Border; } }
            public override Color OverflowButtonGradientBegin { get { return DxClusterTheme.FaceTop; } }
            public override Color OverflowButtonGradientMiddle { get { return DxClusterTheme.FaceTop; } }
            public override Color OverflowButtonGradientEnd { get { return DxClusterTheme.FaceBottom; } }
        }
    }

    // dark palette matching the look of the main window / VST rack
    internal static class DxClusterTheme
    {
        public static readonly Color Void = Color.FromArgb(0x1E, 0x1E, 0x1E);
        public static readonly Color FaceTop = Color.FromArgb(0x4A, 0x4A, 0x4A);
        public static readonly Color FaceBottom = Color.FromArgb(0x3A, 0x3A, 0x3A);
        public static readonly Color Inset = Color.FromArgb(0x33, 0x33, 0x33);
        public static readonly Color Border = Color.FromArgb(0x1A, 0x1A, 0x1A);
        public static readonly Color TextPrimary = Color.FromArgb(0xD8, 0xD8, 0xD8);
        public static readonly Color TextSecondary = Color.FromArgb(0x9A, 0x9A, 0x9A);
        public static readonly Color SelectionBorder = Color.FromArgb(0x8A, 0x8A, 0x8A);
        public static readonly Color Gutter = Color.FromArgb(0x26, 0x26, 0x26);
        public static readonly Color IconHover = Color.FromArgb(0x60, 0x60, 0x60);
        public static readonly Color AccentOn = Color.FromArgb(0x7E, 0xC8, 0xF0);
        public static readonly Color Selection = Color.FromArgb(0x3D, 0x5A, 0x78);
    }
}
