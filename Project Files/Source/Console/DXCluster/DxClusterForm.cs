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
        private const int COL_SPOT_BAND = 2;
        private const int COL_SPOT_CALL = 3;
        private const int COL_SPOT_COUNTRY = 4;
        private const int COL_SPOT_LANGUAGE = 5;
        private const int COL_SPOT_MODE = 6;
        private const int COL_SPOT_SPOTTER = 7;
        private const int COL_SPOT_COMMENT = 8;
        private const int COL_SPOT_NODE = 9;

        private readonly Console _console;

        private ToolStrip toolStrip;
        private ToolStripButton btnDisconnectAll;
        private ToolStripButton btnServers;
        private ToolStripButton btnShowOnPanadapter;
        private ToolStripButton btnSwl;
        private ToolStripButton btnClear;
        private ToolStripButton btnTheme;

        private ToolStrip filterStrip;
        private ToolStripComboBox comboBand;
        private ToolStripComboBox comboMode;
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
        private readonly Dictionary<long, Image> _spotFlagById = new Dictionary<long, Image>();

        private bool _updatingConns;
        private bool _updatingFilters;
        private bool _updatingSwl;
        private bool? _swlColumnsHidden;

        private string _bandFilter = "All";
        private string _modeFilter = "All";
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

            DxClusterTheme.SetDark(DxClusterManager.Settings.DarkTheme);

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
                Renderer = new DxToggleRenderer(new DxColorTable()),
                BackColor = DxClusterTheme.FaceTop,
                ForeColor = DxClusterTheme.TextPrimary,
            };

            btnServers = CreateButton("Server List", onServers);
            btnServers.ToolTipText = "Add, connect and remove cluster nodes, and set your login callsign";

            btnDisconnectAll = CreateButton("Disconnect All", onDisconnectAll);

            btnShowOnPanadapter = new ToolStripButton("Spots on Panadapter")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor = DxClusterTheme.TextPrimary,
                CheckOnClick = true,
                Tag = "green-toggle",
                ToolTipText = "Draw DX cluster spots on the panadapter (requires Setup > TCI Server > Show TCI Spots)",
            };
            btnShowOnPanadapter.CheckStateChanged += onTogglePanadapter;

            btnSwl = new ToolStripButton("SWL Mode")
            {
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor = DxClusterTheme.TextPrimary,
                CheckOnClick = true,
                Tag = "green-toggle",
                ToolTipText = "Show 'on air now' shortwave broadcast stations (EiBi schedule) instead of cluster spots",
            };
            btnSwl.CheckStateChanged += onToggleSwl;

            btnClear = CreateButton("Clear Spots", onClearSpots);

            btnTheme = new ToolStripButton
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                ForeColor = DxClusterTheme.TextPrimary,
                ToolTipText = "Toggle light/dark theme",
            };
            btnTheme.Click += onThemeToggle;

            ToolStripLabel lblTheme = new ToolStripLabel("Theme:")
            {
                Alignment = ToolStripItemAlignment.Right,
                ForeColor = DxClusterTheme.TextSecondary,
            };

            toolStrip.Items.Add(btnServers);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnDisconnectAll);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnShowOnPanadapter);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnClear);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(btnSwl);
            toolStrip.Items.Add(btnTheme);
            toolStrip.Items.Add(lblTheme);

            foreach (ToolStripItem item in new ToolStripItem[] { btnServers, btnDisconnectAll, btnShowOnPanadapter, btnClear, btnSwl, btnTheme })
                item.Margin = new Padding(7, 2, 7, 2);

            UpdateThemeButton();
        }

        private void BuildFilterStrip()
        {
            filterStrip = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new ToolStripProfessionalRenderer(new DxColorTable()),
                BackColor = DxClusterTheme.FaceBottom,
                ForeColor = DxClusterTheme.TextPrimary,
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
                "All", "160m", "80m", "60m", "40m", "30m", "20m", "17m", "15m", "12m", "10m", "6m",
                "120m", "90m", "75m", "49m", "41m", "31m", "25m", "22m", "19m", "16m", "13m", "11m"
            });
            comboBand.BackColor = DxClusterTheme.Inset;
            comboBand.ForeColor = DxClusterTheme.TextPrimary;
            comboBand.SelectedIndexChanged += onFilterChanged;

            ToolStripLabel lblMode = new ToolStripLabel("Mode:") { ForeColor = DxClusterTheme.TextSecondary };

            comboMode = new ToolStripComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 80,
                FlatStyle = FlatStyle.Flat,
            };
            comboMode.Items.AddRange(new object[]
            {
                "All", "CW", "SSB", "USB", "LSB", "DIGI", "FM", "AM"
            });
            comboMode.BackColor = DxClusterTheme.Inset;
            comboMode.ForeColor = DxClusterTheme.TextPrimary;
            comboMode.SelectedIndexChanged += onFilterChanged;

            ToolStripLabel lblText = new ToolStripLabel("Text Filter:") { ForeColor = DxClusterTheme.TextSecondary };

            txtFilter = new ToolStripTextBox { Width = 180, ToolTipText = "Show only spots whose callsign, country, mode, band, spotter or comment contains this text" };
            StyleInput(txtFilter);
            txtFilter.TextChanged += onFilterChanged;

            btnFilterReset = CreateButton("Reset", onFilterReset);

            filterStrip.Items.Add(lblBand);
            filterStrip.Items.Add(comboBand);
            filterStrip.Items.Add(lblMode);
            filterStrip.Items.Add(comboMode);
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
            connMenu.Renderer = new ToolStripProfessionalRenderer(new DxColorTable());
            connMenu.BackColor = DxClusterTheme.Inset;
            connMenu.ForeColor = DxClusterTheme.TextPrimary;
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
                HeaderText = "Frequency (MHz)",
                Width = 120,
                ReadOnly = true,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    Alignment = DataGridViewContentAlignment.MiddleRight,
                    Format = "F4",
                },
            });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Band", Width = 55, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Callsign", Width = 100, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Country", Width = 150, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Language", Width = 110, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mode", Width = 70, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Spotter", Width = 90, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Comment", Width = 280, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridSpots.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Node", Width = 160, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });

            gridSpots.CellDoubleClick += onSpotDoubleClick;
            gridSpots.CellPainting += onSpotCellPainting;

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

        private void onThemeToggle(object sender, EventArgs e)
        {
            DxClusterTheme.SetDark(!DxClusterTheme.IsDark);
            DxClusterManager.Settings.DarkTheme = DxClusterTheme.IsDark;
            DxClusterManager.SaveSettings();
            ApplyTheme();
        }

        private void UpdateThemeButton()
        {
            if (btnTheme != null)
                btnTheme.Text = DxClusterTheme.IsDark ? "Light" : "Dark";
        }

        private void ApplyTheme()
        {
            UpdateThemeButton();

            BackColor = DxClusterTheme.Void;
            ForeColor = DxClusterTheme.TextPrimary;

            if (toolStrip != null)
            {
                toolStrip.BackColor = DxClusterTheme.FaceTop;
                toolStrip.ForeColor = DxClusterTheme.TextPrimary;
                ApplyStripItems(toolStrip);
            }

            if (filterStrip != null)
            {
                filterStrip.BackColor = DxClusterTheme.FaceBottom;
                filterStrip.ForeColor = DxClusterTheme.TextPrimary;
                ApplyStripItems(filterStrip);
            }

            if (comboBand != null)
            {
                comboBand.BackColor = DxClusterTheme.Inset;
                comboBand.ForeColor = DxClusterTheme.TextPrimary;
                comboBand.ComboBox.BackColor = DxClusterTheme.Inset;
                comboBand.ComboBox.ForeColor = DxClusterTheme.TextPrimary;
            }

            if (comboMode != null)
            {
                comboMode.BackColor = DxClusterTheme.Inset;
                comboMode.ForeColor = DxClusterTheme.TextPrimary;
                comboMode.ComboBox.BackColor = DxClusterTheme.Inset;
                comboMode.ComboBox.ForeColor = DxClusterTheme.TextPrimary;
            }

            if (txtFilter != null)
            {
                txtFilter.BackColor = DxClusterTheme.Inset;
                txtFilter.ForeColor = DxClusterTheme.TextPrimary;
                txtFilter.TextBox.BackColor = DxClusterTheme.Inset;
                txtFilter.TextBox.ForeColor = DxClusterTheme.TextPrimary;
            }

            if (split != null) split.BackColor = DxClusterTheme.Void;

            ApplyGridTheme(gridConns);
            ApplyGridTheme(gridSpots);

            if (status != null)
            {
                status.BackColor = DxClusterTheme.FaceTop;
                status.ForeColor = DxClusterTheme.TextSecondary;
            }
            if (lblStatus != null) lblStatus.ForeColor = DxClusterTheme.TextSecondary;

            if (connMenu != null)
            {
                connMenu.BackColor = DxClusterTheme.Inset;
                connMenu.ForeColor = DxClusterTheme.TextPrimary;
                foreach (ToolStripItem item in connMenu.Items)
                    item.ForeColor = DxClusterTheme.TextPrimary;
            }

            Refresh();
        }

        private static void ApplyStripItems(ToolStrip strip)
        {
            foreach (ToolStripItem item in strip.Items)
            {
                if (item is ToolStripSeparator) continue;
                item.ForeColor = (item is ToolStripLabel) ? DxClusterTheme.TextSecondary : DxClusterTheme.TextPrimary;
            }
        }

        private static void ApplyGridTheme(DataGridView grid)
        {
            if (grid == null) return;

            grid.BackgroundColor = DxClusterTheme.Inset;
            grid.GridColor = DxClusterTheme.Border;

            grid.ColumnHeadersDefaultCellStyle.BackColor = DxClusterTheme.FaceTop;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = DxClusterTheme.TextPrimary;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = DxClusterTheme.FaceTop;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = DxClusterTheme.TextPrimary;

            grid.DefaultCellStyle.BackColor = DxClusterTheme.Inset;
            grid.DefaultCellStyle.ForeColor = DxClusterTheme.TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = DxClusterTheme.Selection;
            grid.DefaultCellStyle.SelectionForeColor = DxClusterTheme.TextPrimary;

            grid.AlternatingRowsDefaultCellStyle.BackColor = DxClusterTheme.Gutter;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = DxClusterTheme.TextPrimary;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = DxClusterTheme.Selection;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = DxClusterTheme.TextPrimary;

            grid.Invalidate();
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

            _updatingSwl = true;
            try { btnSwl.Checked = DxClusterManager.SwlMode; } catch { }
            finally { _updatingSwl = false; }

            RefreshSpots();
        }

        private void loadFilterState()
        {
            _updatingFilters = true;
            try
            {
                _bandFilter = string.IsNullOrEmpty(DxClusterManager.Settings.FilterBand) ? "All" : DxClusterManager.Settings.FilterBand;
                _modeFilter = string.IsNullOrEmpty(DxClusterManager.Settings.FilterMode) ? "All" : DxClusterManager.Settings.FilterMode;
                _textFilter = DxClusterManager.Settings.FilterText ?? "";

                int index = comboBand.Items.IndexOf(_bandFilter);
                comboBand.SelectedIndex = index >= 0 ? index : 0;

                int modeIndex = comboMode.Items.IndexOf(_modeFilter);
                comboMode.SelectedIndex = modeIndex >= 0 ? modeIndex : 0;

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

        private void onToggleSwl(object sender, EventArgs e)
        {
            if (_updatingSwl) return;

            DxClusterManager.SetSwlMode(btnSwl.Checked);

            // drop the rows belonging to the previous mode so they are rebuilt cleanly
            _spotRowById.Clear();
            _spotFlagById.Clear();
            gridSpots.Rows.Clear();

            RefreshSpots();
        }

        private void onClearSpots(object sender, EventArgs e)
        {
            DxClusterManager.ClearSpots();

            _spotRowById.Clear();
            _spotFlagById.Clear();
            gridSpots.Rows.Clear();
        }

        private void onFilterChanged(object sender, EventArgs e)
        {
            if (_updatingFilters) return;

            _bandFilter = comboBand.SelectedItem as string ?? "All";
            _modeFilter = comboMode.SelectedItem as string ?? "All";
            _textFilter = (txtFilter.Text ?? "").Trim();

            DxClusterManager.Settings.FilterBand = _bandFilter;
            DxClusterManager.Settings.FilterMode = _modeFilter;
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
                comboMode.SelectedIndex = 0;
                txtFilter.Text = "";
                _bandFilter = "All";
                _modeFilter = "All";
                _textFilter = "";
                DxClusterManager.Settings.FilterBand = "All";
                DxClusterManager.Settings.FilterMode = "All";
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

                int modeNumber = 0;
                object modeTag = row.Cells[COL_SPOT_MODE].Tag;
                if (modeTag is int mn)
                {
                    modeNumber = mn;
                }
                else
                {
                    // try to get from row tag (could be spot object or id)
                    object tag = row.Tag;
                    DxClusterSpot spot = null;
                    if (tag is DxClusterSpot sp) spot = sp;
                    else if (tag is long id) spot = DxClusterManager.FindSpot(id);
                    if (spot != null) modeNumber = spot.ModeNumber;
                }

                DSPMode mode = SpotManager2.SpotModeNumberToDSPMode(modeNumber, hz);
                if (_console.VFOAFreq != mhz) _console.VFOAFreq = mhz;
                if (mode != DSPMode.FIRST && _console.RX1DSPMode != mode) _console.RX1DSPMode = mode;

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
            if (_bandFilter != "All" && bandOf(spot) != _bandFilter) return false;
            if (_modeFilter != "All" && !string.Equals(spot.Mode ?? "", _modeFilter, StringComparison.OrdinalIgnoreCase)) return false;

            if (!string.IsNullOrEmpty(_textFilter))
            {
                string t = _textFilter.ToUpperInvariant();
                if ((spot.Callsign ?? "").ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0 &&
                    (spot.Spotter ?? "").ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0 &&
                    (spot.Country ?? "").ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0 &&
                    (spot.Language ?? "").ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0 &&
                    (spot.Mode ?? "").ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0 &&
                    bandOf(spot).ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0 &&
                    (spot.Comment ?? "").ToUpperInvariant().IndexOf(t, StringComparison.Ordinal) < 0)
                    return false;
            }

            return true;
        }

        private static string bandOf(DxClusterSpot spot)
        {
            if (spot == null) return "";
            return spot.IsSwl ? broadcastBandOf(spot.FrequencyHz) : hamBandOf(spot.FrequencyHz);
        }

        private static string hamBandOf(long hz)
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

        // shortwave broadcast meter bands
        private static string broadcastBandOf(long hz)
        {
            double m = hz / 1000000.0;

            if (m >= 2.3 && m <= 2.495) return "120m";
            if (m >= 3.2 && m <= 3.4) return "90m";
            if (m >= 3.9 && m <= 4.0) return "75m";
            if (m >= 4.75 && m <= 5.06) return "60m";
            if (m >= 5.9 && m <= 6.2) return "49m";
            if (m >= 7.2 && m <= 7.45) return "41m";
            if (m >= 9.4 && m <= 9.9) return "31m";
            if (m >= 11.6 && m <= 12.1) return "25m";
            if (m >= 13.57 && m <= 13.87) return "22m";
            if (m >= 15.1 && m <= 15.8) return "19m";
            if (m >= 17.48 && m <= 17.9) return "16m";
            if (m >= 18.9 && m <= 19.02) return "15m";
            if (m >= 21.45 && m <= 21.85) return "13m";
            if (m >= 25.6 && m <= 26.1) return "11m";

            return "";
        }

        // hide telnet nodes panel + spot list columns in SWL mode for a cleaner broadcast view
        private void updateSwlColumns()
        {
            bool swl = DxClusterManager.SwlMode;
            if (_swlColumnsHidden == swl) return;
            _swlColumnsHidden = swl;

            try
            {
                if (split != null) split.Panel1Collapsed = swl;
                gridSpots.Columns[COL_SPOT_LANGUAGE].Visible = swl;
                gridSpots.Columns[COL_SPOT_SPOTTER].Visible = !swl;
                gridSpots.Columns[COL_SPOT_NODE].Visible = !swl;
            }
            catch { }
        }

        private void RefreshSpots()
        {
            if (gridSpots == null || IsDisposed || !IsHandleCreated) return;

            try
            {
                updateSwlColumns();

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
                        if (tag is DxClusterSpot sp2 && live.Contains(sp2.Id)) continue;
                        if (tag is long idl && live.Contains(idl)) continue;

                        gridSpots.Rows.RemoveAt(i);
                        if (tag is DxClusterSpot sp3) { _spotRowById.Remove(sp3.Id); _spotFlagById.Remove(sp3.Id); }
                        else if (tag is long rid) { _spotRowById.Remove(rid); _spotFlagById.Remove(rid); }
                    }
                }
                finally { gridSpots.ResumeLayout(); }

                string text;

                if (DxClusterManager.SwlMode)
                {
                    text = "SWL: " + visible.Count + " broadcast station(s) on air now";
                    if (DxClusterManager.SwlLoading) text += "   |   loading...";
                    else if (!string.IsNullOrEmpty(DxClusterManager.SwlStatus)) text += "   |   " + DxClusterManager.SwlStatus;
                    else if (!string.IsNullOrEmpty(DxClusterManager.SwlUrl)) text += "   |   " + DxClusterManager.SwlUrl;
                }
                else
                {
                    int nodeCount = DxClusterManager.Settings.Nodes.Count;
                    int connectionCount = DxClusterManager.Connections.Length;

                    text = nodeCount + " node(s), " + connectionCount + " connected, " + visible.Count + " shown"
                        + (visible.Count >= MaxDisplayedSpots ? " (newest " + MaxDisplayedSpots + ")" : "");
                    if (!DxClusterManager.Settings.ShowOnPanadapter)
                        text += "   |   panadapter display off";
                }

                lblStatus.Text = text;
            }
            catch { }
        }

        private DataGridViewRow buildSpotRow(DxClusterSpot spot)
        {
            DataGridViewRow row = new DataGridViewRow();
            row.CreateCells(gridSpots);
            row.Tag = spot; // store spot directly for quick access on click
            row.Cells[COL_SPOT_TIME].Value = spot.UtcTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            row.Cells[COL_SPOT_MHZ].Value = spot.FrequencyHz / 1000000.0;
            row.Cells[COL_SPOT_BAND].Value = bandOf(spot);
            row.Cells[COL_SPOT_CALL].Value = spot.Callsign;
            row.Cells[COL_SPOT_COUNTRY].Value = spot.Country;
            row.Cells[COL_SPOT_LANGUAGE].Value = spot.Language;
            row.Cells[COL_SPOT_MODE].Value = spot.Mode;
            row.Cells[COL_SPOT_MODE].Tag = spot.ModeNumber;
            row.Cells[COL_SPOT_SPOTTER].Value = spot.Spotter;
            row.Cells[COL_SPOT_COMMENT].Value = spot.Comment;
            row.Cells[COL_SPOT_NODE].Value = spot.Node;

            DxClusterNodeSettings node = DxClusterManager.FindNodeByKey(spot.NodeKey);
            if (node != null)
                row.Cells[COL_SPOT_CALL].Style.ForeColor = Color.FromArgb(node.ColorArgb);
            else if (spot.IsSwl)
                row.Cells[COL_SPOT_CALL].Style.ForeColor = DxClusterManager.DefaultSpotColor;

            try
            {
                Image flag = spot.IsSwl
                    ? SpotManager2.GetFlagImageForAssetCode(spot.CountryCode)
                    : SpotManager2.GetFlagImageForCallsign(spot.Callsign);
                if (flag != null) _spotFlagById[spot.Id] = flag;
            }
            catch { }

            _spotRowById[spot.Id] = row;
            return row;
        }

        private void onSpotCellPainting(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != COL_SPOT_COUNTRY) return;

            Image flag = null;
            if (gridSpots.Rows[e.RowIndex].Tag is long id)
                _spotFlagById.TryGetValue(id, out flag);

            if (flag == null) return; // no flag, let the grid draw the text normally

            e.PaintBackground(e.CellBounds, true);

            Rectangle bounds = e.CellBounds;
            int left = bounds.Left + 2;

            try
            {
                int h = Math.Min(bounds.Height - 4, flag.Height);
                if (h < 1) h = 1;
                int w = (int)Math.Round(flag.Width * (h / (double)flag.Height));
                if (w < 1) w = 1;
                int y = bounds.Top + (bounds.Height - h) / 2;

                Rectangle dest = new Rectangle(left, y, w, h);
                e.Graphics.DrawImage(flag, dest);
                left = dest.Right + 4;
            }
            catch { }

            string text = Convert.ToString(e.Value);
            if (!string.IsNullOrEmpty(text))
            {
                bool selected = (e.State & DataGridViewElementStates.Selected) != 0;
                Color fore = selected ? e.CellStyle.SelectionForeColor : e.CellStyle.ForeColor;

                Rectangle textRect = new Rectangle(left, bounds.Top, Math.Max(0, bounds.Right - left), bounds.Height);
                TextRenderer.DrawText(e.Graphics, text, e.CellStyle.Font ?? gridSpots.Font, textRect, fore,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            e.Handled = true;
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

        // paints a checked toggle (Tag = "green-toggle") with a green highlight so its on/off
        // state is obvious at a glance
        private sealed class DxToggleRenderer : ToolStripProfessionalRenderer
        {
            private static readonly Color GreenIdle = Color.FromArgb(0x2E, 0x7D, 0x32);
            private static readonly Color GreenHot = Color.FromArgb(0x3E, 0xA0, 0x44);

            public DxToggleRenderer(ProfessionalColorTable table) : base(table) { }

            private static bool isGreenChecked(ToolStripItem item)
            {
                ToolStripButton button = item as ToolStripButton;
                return button != null && button.Checked && (button.Tag as string) == "green-toggle";
            }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (isGreenChecked(e.Item))
                {
                    Color fill = (e.Item.Selected || e.Item.Pressed) ? GreenHot : GreenIdle;
                    using (SolidBrush brush = new SolidBrush(fill))
                        e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
                    return;
                }

                base.OnRenderButtonBackground(e);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (isGreenChecked(e.Item)) e.TextColor = Color.White;
                base.OnRenderItemText(e);
            }
        }
    }

    // light/dark palettes for the DX cluster window; dark matches the look of the main window / VST rack
    internal static class DxClusterTheme
    {
        internal sealed class Palette
        {
            public Color Void;
            public Color FaceTop;
            public Color FaceBottom;
            public Color Inset;
            public Color Border;
            public Color TextPrimary;
            public Color TextSecondary;
            public Color SelectionBorder;
            public Color Gutter;
            public Color IconHover;
            public Color AccentOn;
            public Color Selection;
        }

        private static readonly Palette Dark = new Palette
        {
            Void = Color.FromArgb(0x1E, 0x1E, 0x1E),
            FaceTop = Color.FromArgb(0x4A, 0x4A, 0x4A),
            FaceBottom = Color.FromArgb(0x3A, 0x3A, 0x3A),
            Inset = Color.FromArgb(0x33, 0x33, 0x33),
            Border = Color.FromArgb(0x1A, 0x1A, 0x1A),
            TextPrimary = Color.FromArgb(0xD8, 0xD8, 0xD8),
            TextSecondary = Color.FromArgb(0x9A, 0x9A, 0x9A),
            SelectionBorder = Color.FromArgb(0x8A, 0x8A, 0x8A),
            Gutter = Color.FromArgb(0x26, 0x26, 0x26),
            IconHover = Color.FromArgb(0x60, 0x60, 0x60),
            AccentOn = Color.FromArgb(0x7E, 0xC8, 0xF0),
            Selection = Color.FromArgb(0x3D, 0x5A, 0x78),
        };

        private static readonly Palette Light = new Palette
        {
            Void = Color.FromArgb(0xF0, 0xF0, 0xF0),
            FaceTop = Color.FromArgb(0xE8, 0xE8, 0xE8),
            FaceBottom = Color.FromArgb(0xDC, 0xDC, 0xDC),
            Inset = Color.FromArgb(0xFF, 0xFF, 0xFF),
            Border = Color.FromArgb(0xC8, 0xC8, 0xC8),
            TextPrimary = Color.FromArgb(0x20, 0x20, 0x20),
            TextSecondary = Color.FromArgb(0x60, 0x60, 0x60),
            SelectionBorder = Color.FromArgb(0xB0, 0xB0, 0xB0),
            Gutter = Color.FromArgb(0xF6, 0xF6, 0xF6),
            IconHover = Color.FromArgb(0xCD, 0xDD, 0xEE),
            AccentOn = Color.FromArgb(0x1E, 0x6F, 0xA8),
            Selection = Color.FromArgb(0xCC, 0xE4, 0xFF),
        };

        private static bool _dark = true;

        public static bool IsDark { get { return _dark; } }
        public static void SetDark(bool dark) { _dark = dark; }

        private static Palette P { get { return _dark ? Dark : Light; } }

        public static Color Void { get { return P.Void; } }
        public static Color FaceTop { get { return P.FaceTop; } }
        public static Color FaceBottom { get { return P.FaceBottom; } }
        public static Color Inset { get { return P.Inset; } }
        public static Color Border { get { return P.Border; } }
        public static Color TextPrimary { get { return P.TextPrimary; } }
        public static Color TextSecondary { get { return P.TextSecondary; } }
        public static Color SelectionBorder { get { return P.SelectionBorder; } }
        public static Color Gutter { get { return P.Gutter; } }
        public static Color IconHover { get { return P.IconHover; } }
        public static Color AccentOn { get { return P.AccentOn; } }
        public static Color Selection { get { return P.Selection; } }
    }
}
