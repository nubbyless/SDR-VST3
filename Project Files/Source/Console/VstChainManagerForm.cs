/*  VstChainManagerForm.cs

This file is part of a program that implements a Software-Defined Radio.

This code/file can be found on GitHub : https://github.com/nubbyless/Thetis-Plus

Copyright (C) 2026 ChasingCoffee
Copyright (C) 2026 nubbyless <nubbyless@yahoo.com>

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

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Thetis
{
    public sealed class VstChainManagerForm : Form
    {
        private const int DeferredUiRefreshDelayMs = 200;
        private const int MaxPluginsPerChain = 16;
        private const int EditorCaptureSettleMs = 1500;

        private static readonly Color FormBack = Color.FromArgb(0x2B, 0x2B, 0x2B);
        private static readonly Color PanelBack = Color.FromArgb(0x33, 0x33, 0x33);
        private static readonly Color TextPrimary = Color.FromArgb(0xE0, 0xE0, 0xE0);
        private static readonly Color TextSecondary = Color.FromArgb(0xA8, 0xA8, 0xA8);
        private static readonly Color ButtonBack = Color.FromArgb(0x45, 0x45, 0x45);

        private sealed class StatusSnapshot
        {
            public VstHostState RxHostState;
            public VstHostState TxHostState;
            public bool RxReady;
            public bool TxReady;
            public int RxLatencyBlocks;
            public int RxLatencyFloor;
            public int RxSampleRate;
            public int RxBlockSize;
            public int TxLatencyBlocks;
            public int TxLatencyFloor;
            public int TxSampleRate;
            public int TxBlockSize;
        }

        private sealed class ChainPage
        {
            public VstChainKind Kind;
            public Control Column;
            public CheckBox ChainBypassCheckBox;
            public NumericUpDown GainUpDown;
            public NumericUpDown LatencyFloorUpDown;
            public Label LatencyLabel;
            public Label ChainStatusLabel;
            public Panel ViewHost;
            public FlowLayoutPanel ButtonPanel;
            public ListView PluginListView;
            public VstRackView RackView;
            public Button AddButton;
            public Button RemoveButton;
            public Button MoveUpButton;
            public Button MoveDownButton;
            public Button ToggleEnabledButton;
            public Button ToggleBypassButton;
            public Button OpenEditorButton;
            public Button RefreshButton;
            public Button ExportButton;
            public Button ImportButton;
            public VstChainInfo LastChainInfo;
            public List<VstPluginState> LastDisplayPlugins;
            public string LastTrackingSignature;
            public bool RefreshInProgress;
            public bool RefreshPending;
            public int PendingPreferredIndex = -1;
            public System.Windows.Forms.Timer DeferredRefreshTimer;
        }

        private readonly Label _summaryLabel;
        private readonly Label _hostStatusLabel;
        private readonly TableLayoutPanel _columnsPanel;
        private TableLayoutPanel _rootPanel;
        private readonly RadioButton _listViewRadio;
        private readonly RadioButton _rackViewRadio;
        private readonly CheckBox _showSnapshotsCheckBox;
        private readonly Button _clearSnapshotsButton;
        private readonly Label _detailLabel;
        private readonly ChainPage _rxPage;
        private readonly ChainPage _txPage;

        public static bool HighlightProfileSaveItems;
        private readonly System.Windows.Forms.Timer _statusTimer;
        private ChainPage _activePage;
        private VstChainViewMode _viewMode;
        private bool _statusRefreshInProgress;
        private bool _updatingUi;
        private bool _openingEditor;

        public VstChainManagerForm()
        {
            Text = "VST Chains";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1000, 520);
            Size = new Size(1280, 760);
            ShowInTaskbar = false;
            BackColor = FormBack;
            ForeColor = TextPrimary;

            _viewMode = VstHost.UiState.ChainViewMode;

            TableLayoutPanel rootPanel = new TableLayoutPanel();
            _rootPanel = rootPanel;
            rootPanel.ColumnCount = 1;
            rootPanel.RowCount = 3;
            rootPanel.Dock = DockStyle.Fill;
            rootPanel.Padding = new Padding(8);
            rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            rootPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ---- header ----------------------------------------------------
            TableLayoutPanel headerPanel = new TableLayoutPanel();
            headerPanel.ColumnCount = 2;
            headerPanel.RowCount = 2;
            headerPanel.AutoSize = true;
            headerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            headerPanel.Dock = DockStyle.Fill;
            headerPanel.Margin = new Padding(0, 0, 0, 6);
            headerPanel.Padding = new Padding(0);
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            headerPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _summaryLabel = new Label();
            _summaryLabel.AutoSize = true;
            _summaryLabel.Dock = DockStyle.Fill;
            _summaryLabel.Margin = new Padding(0);
            _summaryLabel.ForeColor = TextSecondary;

            _hostStatusLabel = new Label();
            _hostStatusLabel.AutoSize = true;
            _hostStatusLabel.Dock = DockStyle.Fill;
            _hostStatusLabel.Margin = new Padding(0);
            _hostStatusLabel.Padding = new Padding(0, 2, 0, 2);
            _hostStatusLabel.Font = new Font(_hostStatusLabel.Font, FontStyle.Bold);
            _hostStatusLabel.ForeColor = TextPrimary;

            FlowLayoutPanel viewTogglePanel = new FlowLayoutPanel();
            viewTogglePanel.AutoSize = true;
            viewTogglePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            viewTogglePanel.FlowDirection = FlowDirection.LeftToRight;
            viewTogglePanel.WrapContents = false;
            viewTogglePanel.Margin = new Padding(0);

            _listViewRadio = CreateViewToggle("List");
            _rackViewRadio = CreateViewToggle("Rack");
            _listViewRadio.Checked = _viewMode == VstChainViewMode.List;
            _rackViewRadio.Checked = _viewMode == VstChainViewMode.Rack;
            _listViewRadio.CheckedChanged += delegate
            {
                if (_updatingUi || !_listViewRadio.Checked) return;
                SetViewMode(VstChainViewMode.List);
            };
            _rackViewRadio.CheckedChanged += delegate
            {
                if (_updatingUi || !_rackViewRadio.Checked) return;
                SetViewMode(VstChainViewMode.Rack);
            };
            _showSnapshotsCheckBox = new CheckBox();
            _showSnapshotsCheckBox.AutoSize = true;
            _showSnapshotsCheckBox.Margin = new Padding(0, 4, 10, 0);
            _showSnapshotsCheckBox.Text = "Show Snapshots";
            _showSnapshotsCheckBox.ForeColor = TextPrimary;
            _showSnapshotsCheckBox.Checked = VstHost.UiState.ShowSnapshots;
            _showSnapshotsCheckBox.CheckedChanged += delegate
            {
                if (_updatingUi) return;
                SetShowSnapshots(_showSnapshotsCheckBox.Checked);
            };

            _clearSnapshotsButton = CreateButton("Clear Snapshots", 106);
            // Wider right margin separates the snapshot controls from the
            // view-mode pair that follows.
            _clearSnapshotsButton.Margin = new Padding(0, 0, 16, 0);
            _clearSnapshotsButton.Click += delegate { ClearCapturedSnapshots(); };

            _rackViewRadio.Margin = new Padding(0, 0, 0, 0);

            viewTogglePanel.Controls.Add(_showSnapshotsCheckBox);
            viewTogglePanel.Controls.Add(_clearSnapshotsButton);
            viewTogglePanel.Controls.Add(_listViewRadio);
            viewTogglePanel.Controls.Add(_rackViewRadio);

            headerPanel.Controls.Add(_summaryLabel, 0, 0);
            headerPanel.Controls.Add(_hostStatusLabel, 0, 1);
            headerPanel.Controls.Add(viewTogglePanel, 1, 0);
            headerPanel.SetRowSpan(viewTogglePanel, 2);

            // ---- side-by-side chain columns --------------------------------
            _columnsPanel = new TableLayoutPanel();
            _columnsPanel.ColumnCount = 2;
            _columnsPanel.RowCount = 1;
            _columnsPanel.Dock = DockStyle.Fill;
            _columnsPanel.Margin = new Padding(0);
            _columnsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _columnsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            _columnsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _rxPage = CreateChainPage(VstChainKind.Rx, "RX");
            _txPage = CreateChainPage(VstChainKind.Tx, "TX");

            if (HighlightProfileSaveItems)
                HighlightTXProfileSaveItems(true);

            // Asymmetric margins put a visible gap between the two racks.
            _rxPage.Column.Margin = new Padding(0, 0, 6, 0);
            _txPage.Column.Margin = new Padding(6, 0, 0, 0);

            _columnsPanel.Controls.Add(_rxPage.Column, 0, 0);
            _columnsPanel.Controls.Add(_txPage.Column, 1, 0);

            // ---- shared detail strip ---------------------------------------
            _detailLabel = new Label();
            _detailLabel.AutoSize = false;
            _detailLabel.Dock = DockStyle.Fill;
            _detailLabel.Height = 34;
            _detailLabel.Margin = new Padding(0, 6, 0, 0);
            _detailLabel.Padding = new Padding(8, 0, 8, 0);
            _detailLabel.TextAlign = ContentAlignment.MiddleLeft;
            _detailLabel.BackColor = PanelBack;
            _detailLabel.ForeColor = TextSecondary;
            _detailLabel.Text = "Select a plugin to view its load state and path.";

            _statusTimer = new System.Windows.Forms.Timer();
            _statusTimer.Interval = 500;
            _statusTimer.Tick += delegate
            {
                if (!Visible || _updatingUi || _statusRefreshInProgress)
                    return;

                RefreshStatusOnlyAsync();
            };

            rootPanel.Controls.Add(headerPanel, 0, 0);
            rootPanel.Controls.Add(_columnsPanel, 0, 1);
            rootPanel.Controls.Add(_detailLabel, 0, 2);
            Controls.Add(rootPanel);

            VstHost.ChainStateChanged += OnChainStateChanged;

            ApplyViewMode();

            Shown += delegate { _statusTimer.Start(); RefreshChains(); };
            Activated += delegate { RefreshChains(); };
            VisibleChanged += delegate
            {
                if (Visible && IsHandleCreated)
                {
                    _statusTimer.Start();
                    RefreshStatusOnlyAsync();
                }
                else
                {
                    _statusTimer.Stop();
                }
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Run after load so DPI scaling has already been applied to the
            // buttons; measuring them in the constructor would size the form
            // from unscaled widths.
            ApplyExactButtonRowWidth();
        }

        /// <summary>
        /// Sizes the form so each chain column is exactly wide enough for its
        /// button row on a single line, with no slack. Also becomes the minimum
        /// width, so the buttons can never be made to wrap.
        /// </summary>
        private void ApplyExactButtonRowWidth()
        {
            int columnWidth = Math.Max(
                GetButtonRowWidth(_rxPage.ButtonPanel),
                GetButtonRowWidth(_txPage.ButtonPanel));

            if (columnWidth <= 0)
                return;

            int chrome = Width - ClientSize.Width;
            int gaps = _rxPage.Column.Margin.Horizontal
                     + _txPage.Column.Margin.Horizontal
                     + _rootPanel.Padding.Horizontal;

            // The two columns split the remaining space 50/50, so an odd width
            // leaves one of them a pixel short and wraps its row. Keep the
            // column area even.
            int columnArea = columnWidth * 2;

            if (columnArea % 2 != 0)
                columnArea++;

            int target = columnArea + gaps + chrome;

            MinimumSize = new Size(target, MinimumSize.Height);

            if (Width != target)
                Width = target;
        }

        private static int GetButtonRowWidth(FlowLayoutPanel panel)
        {
            if (panel == null || panel.Controls.Count == 0)
                return 0;

            // Ask the panel itself how wide one unwrapped row is. A hand-rolled
            // sum is easy to get wrong: the layout counts every control's full
            // margin when deciding whether it fits, including the trailing one
            // on the last button, so trimming that margin wraps the row.
            int width = panel.GetPreferredSize(new Size(int.MaxValue, 1)).Width;

            if (width > 0)
                return width;

            for (int i = 0; i < panel.Controls.Count; i++)
            {
                Control control = panel.Controls[i];
                width += control.Width + control.Margin.Horizontal;
            }

            return width + panel.Padding.Horizontal;
        }

        public void HighlightTXProfileSaveItems(bool bHighlight)
        {
            Common.HightlightControl(_rxPage.ChainBypassCheckBox, bHighlight);
            Common.HightlightControl(_txPage.ChainBypassCheckBox, bHighlight);
        }

        public void RefreshChains()
        {
            RefreshStatusOnlyAsync();
            RefreshChainPageAsync(_rxPage);
            RefreshChainPageAsync(_txPage);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }

            base.OnFormClosing(e);
        }

        #region Construction

        private static RadioButton CreateViewToggle(string text)
        {
            RadioButton button = new RadioButton();
            button.Appearance = Appearance.Button;
            button.AutoSize = false;
            button.Size = new Size(60, 26);
            button.Margin = new Padding(0, 0, 4, 0);
            button.Text = text;
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = ButtonBack;
            button.ForeColor = TextPrimary;
            button.FlatAppearance.BorderColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
            button.FlatAppearance.CheckedBackColor = Color.FromArgb(0x5E, 0x5E, 0x5E);
            return button;
        }

        private ChainPage CreateChainPage(VstChainKind kind, string title)
        {
            ChainPage page = new ChainPage();

            page.Kind = kind;

            TableLayoutPanel column = new TableLayoutPanel();
            column.ColumnCount = 1;
            column.RowCount = 4;
            column.Dock = DockStyle.Fill;
            column.Padding = new Padding(0);
            column.BackColor = FormBack;
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            column.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            column.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            page.Column = column;

            // Row 0 — chain title + status
            FlowLayoutPanel titlePanel = new FlowLayoutPanel();
            titlePanel.AutoSize = true;
            titlePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            titlePanel.Dock = DockStyle.Fill;
            titlePanel.WrapContents = false;
            titlePanel.Margin = new Padding(0, 0, 0, 2);

            Label titleLabel = new Label();
            titleLabel.AutoSize = true;
            titleLabel.Text = title;
            titleLabel.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
            titleLabel.ForeColor = TextPrimary;
            titleLabel.Margin = new Padding(0, 0, 10, 0);
            titlePanel.Controls.Add(titleLabel);

            page.ChainStatusLabel = new Label();
            page.ChainStatusLabel.AutoSize = true;
            page.ChainStatusLabel.Margin = new Padding(0, 8, 0, 0);
            page.ChainStatusLabel.ForeColor = TextSecondary;
            titlePanel.Controls.Add(page.ChainStatusLabel);

            // Row 1 — chain controls
            FlowLayoutPanel headerPanel = new FlowLayoutPanel();
            headerPanel.AutoSize = true;
            headerPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            headerPanel.Dock = DockStyle.Fill;
            headerPanel.WrapContents = true;
            headerPanel.Margin = new Padding(0, 0, 0, 4);

            page.ChainBypassCheckBox = new CheckBox();
            page.ChainBypassCheckBox.AutoSize = true;
            page.ChainBypassCheckBox.Margin = new Padding(0, 5, 14, 0);
            page.ChainBypassCheckBox.Text = "Bypass";
            page.ChainBypassCheckBox.ForeColor = TextPrimary;
            page.ChainBypassCheckBox.CheckedChanged += delegate
            {
                if (_updatingUi) return;
                if (VstHost.SetChainBypass(page.Kind, page.ChainBypassCheckBox.Checked))
                    QueueChainPageRefresh(page, GetSelectedIndex(page));
            };
            headerPanel.Controls.Add(page.ChainBypassCheckBox);

            headerPanel.Controls.Add(CreateFieldLabel("Gain:"));

            page.GainUpDown = new NumericUpDown();
            page.GainUpDown.DecimalPlaces = 2;
            page.GainUpDown.Increment = 0.05M;
            page.GainUpDown.Maximum = 8M;
            page.GainUpDown.Minimum = 0M;
            page.GainUpDown.Value = 1.00M;
            page.GainUpDown.Size = new Size(64, 24);
            page.GainUpDown.Margin = new Padding(0, 2, 12, 0);
            page.GainUpDown.BorderStyle = BorderStyle.FixedSingle;
            page.GainUpDown.BackColor = PanelBack;
            page.GainUpDown.ForeColor = TextPrimary;
            page.GainUpDown.ValueChanged += delegate
            {
                if (_updatingUi) return;
                if (VstHost.SetChainGain(page.Kind, (double)page.GainUpDown.Value))
                    QueueChainPageRefresh(page, GetSelectedIndex(page));
            };
            headerPanel.Controls.Add(page.GainUpDown);

            headerPanel.Controls.Add(CreateFieldLabel("Floor:"));

            page.LatencyFloorUpDown = new NumericUpDown();
            page.LatencyFloorUpDown.DecimalPlaces = 0;
            page.LatencyFloorUpDown.Increment = 1M;
            page.LatencyFloorUpDown.Maximum = 64M;
            page.LatencyFloorUpDown.Minimum = 1M;
            page.LatencyFloorUpDown.Value = kind == VstChainKind.Rx ? 8M : 2M;
            page.LatencyFloorUpDown.Size = new Size(48, 24);
            page.LatencyFloorUpDown.Margin = new Padding(0, 2, 8, 0);
            page.LatencyFloorUpDown.BorderStyle = BorderStyle.FixedSingle;
            page.LatencyFloorUpDown.BackColor = PanelBack;
            page.LatencyFloorUpDown.ForeColor = TextPrimary;
            page.LatencyFloorUpDown.ValueChanged += delegate
            {
                if (_updatingUi) return;
                VstHost.SetPipelineLatencyFloor(page.Kind, (int)page.LatencyFloorUpDown.Value);
            };
            headerPanel.Controls.Add(page.LatencyFloorUpDown);

            page.LatencyLabel = new Label();
            page.LatencyLabel.AutoSize = true;
            page.LatencyLabel.Margin = new Padding(0, 6, 0, 0);
            page.LatencyLabel.ForeColor = TextSecondary;
            page.LatencyLabel.Text = "";
            headerPanel.Controls.Add(page.LatencyLabel);

            // Row 2 — the view host, holding both presentations
            page.ViewHost = new Panel();
            page.ViewHost.Dock = DockStyle.Fill;
            page.ViewHost.Margin = new Padding(0);
            page.ViewHost.BackColor = FormBack;

            page.PluginListView = new ListView();
            page.PluginListView.Dock = DockStyle.Fill;
            page.PluginListView.FullRowSelect = true;
            page.PluginListView.GridLines = true;
            page.PluginListView.HideSelection = false;
            page.PluginListView.MultiSelect = false;
            page.PluginListView.View = View.Details;
            page.PluginListView.BackColor = Color.FromArgb(0x24, 0x24, 0x24);
            page.PluginListView.ForeColor = TextPrimary;
            page.PluginListView.BorderStyle = BorderStyle.FixedSingle;
            page.PluginListView.Columns.Add("#", 30);
            page.PluginListView.Columns.Add("Plugin", 150);
            page.PluginListView.Columns.Add("Format", 55);
            page.PluginListView.Columns.Add("Load", 90);
            page.PluginListView.Columns.Add("On", 44);
            page.PluginListView.Columns.Add("Byp", 44);
            page.PluginListView.Columns.Add("Path", 300);
            page.PluginListView.SelectedIndexChanged += delegate
            {
                if (_updatingUi) return;
                _activePage = page;
                UpdateSelection(page);
            };
            page.PluginListView.DoubleClick += delegate { OpenPluginEditorAt(page, GetSelectedIndex(page)); };

            page.RackView = new VstRackView(kind);
            page.RackView.Dock = DockStyle.Fill;
            page.RackView.SelectionChanged += delegate
            {
                if (_updatingUi) return;
                _activePage = page;
                UpdateSelection(page);
            };
            page.RackView.AddRequested += delegate { AddPluginFromCatalog(page); };
            page.RackView.RemoveRequested += delegate(object s, VstRackSlotEventArgs e)
            {
                RemovePluginAt(page, e.Index);
            };
            page.RackView.MoveRequested += delegate(object s, VstRackMoveEventArgs e)
            {
                MovePluginAt(page, e.Index, e.Delta);
            };
            page.RackView.EnabledToggleRequested += delegate(object s, VstRackSlotEventArgs e)
            {
                TogglePluginEnabledAt(page, e.Index);
            };
            page.RackView.BypassToggleRequested += delegate(object s, VstRackSlotEventArgs e)
            {
                TogglePluginBypassAt(page, e.Index);
            };
            page.RackView.EditorRequested += delegate(object s, VstRackSlotEventArgs e)
            {
                OpenPluginEditorAt(page, e.Index);
            };

            page.ViewHost.Controls.Add(page.PluginListView);
            page.ViewHost.Controls.Add(page.RackView);

            // Row 3 — compact button cluster, wraps on narrow columns
            FlowLayoutPanel buttonPanel = new FlowLayoutPanel();
            buttonPanel.AutoSize = true;
            buttonPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            buttonPanel.Dock = DockStyle.Fill;
            buttonPanel.FlowDirection = FlowDirection.LeftToRight;
            buttonPanel.WrapContents = true;
            buttonPanel.Margin = new Padding(0, 4, 0, 0);
            page.ButtonPanel = buttonPanel;

            page.AddButton = CreateButton("+ VST3", 66);
            page.AddButton.Click += delegate { AddPluginFromCatalog(page); };
            buttonPanel.Controls.Add(page.AddButton);

            page.RemoveButton = CreateButton("Remove", 66);
            page.RemoveButton.Click += delegate { RemovePluginAt(page, GetSelectedIndex(page)); };
            buttonPanel.Controls.Add(page.RemoveButton);

            page.MoveUpButton = CreateButton("↑", 32);
            page.MoveUpButton.Click += delegate { MovePluginAt(page, GetSelectedIndex(page), -1); };
            buttonPanel.Controls.Add(page.MoveUpButton);

            page.MoveDownButton = CreateButton("↓", 32);
            page.MoveDownButton.Click += delegate { MovePluginAt(page, GetSelectedIndex(page), 1); };
            buttonPanel.Controls.Add(page.MoveDownButton);

            // Wide enough for the longest label each toggle can show
            // ("Disable" / "Unbypass") so the text never wraps.
            page.ToggleEnabledButton = CreateButton("Enable", 68);
            page.ToggleEnabledButton.Click += delegate { TogglePluginEnabledAt(page, GetSelectedIndex(page)); };
            buttonPanel.Controls.Add(page.ToggleEnabledButton);

            page.ToggleBypassButton = CreateButton("Bypass", 78);
            page.ToggleBypassButton.Click += delegate { TogglePluginBypassAt(page, GetSelectedIndex(page)); };
            buttonPanel.Controls.Add(page.ToggleBypassButton);

            page.OpenEditorButton = CreateButton("Editor", 56);
            page.OpenEditorButton.Click += delegate { OpenPluginEditorAt(page, GetSelectedIndex(page)); };
            buttonPanel.Controls.Add(page.OpenEditorButton);

            page.RefreshButton = CreateButton("Refresh", 62);
            page.RefreshButton.Click += delegate { RefreshChainPageAsync(page); };
            buttonPanel.Controls.Add(page.RefreshButton);

            page.ExportButton = CreateButton("Export", 62);
            page.ExportButton.Click += delegate { ExportChain(page); };
            buttonPanel.Controls.Add(page.ExportButton);

            page.ImportButton = CreateButton("Import", 62);
            page.ImportButton.Click += delegate { ImportChain(page); };
            buttonPanel.Controls.Add(page.ImportButton);

            page.DeferredRefreshTimer = new System.Windows.Forms.Timer();
            page.DeferredRefreshTimer.Interval = DeferredUiRefreshDelayMs;
            page.DeferredRefreshTimer.Tick += delegate
            {
                page.DeferredRefreshTimer.Stop();
                RefreshChainPageAsync(page, page.PendingPreferredIndex);
            };

            column.Controls.Add(titlePanel, 0, 0);
            column.Controls.Add(headerPanel, 0, 1);
            column.Controls.Add(page.ViewHost, 0, 2);
            column.Controls.Add(buttonPanel, 0, 3);

            return page;
        }

        private static Label CreateFieldLabel(string text)
        {
            return new Label
            {
                AutoSize = true,
                Margin = new Padding(0, 6, 4, 0),
                Text = text,
                ForeColor = TextSecondary
            };
        }

        private static Button CreateButton(string text, int width)
        {
            Button button = new Button();
            button.AutoSize = false;
            button.Size = new Size(width, 26);
            button.Margin = new Padding(0, 0, 4, 4);
            button.Text = text;
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = ButtonBack;
            button.ForeColor = TextPrimary;
            button.FlatAppearance.BorderColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
            // Labels swap at runtime (Enable/Disable, Bypass/Unbypass); keep a
            // longer one on a single line rather than wrapping it.
            button.AutoEllipsis = false;
            button.TextImageRelation = TextImageRelation.Overlay;
            return button;
        }

        #endregion

        #region View mode

        private void SetViewMode(VstChainViewMode mode)
        {
            if (_viewMode == mode)
                return;

            _viewMode = mode;
            VstHost.UiState.ChainViewMode = mode;
            VstHost.ScheduleUiStateSave();

            ApplyViewMode();

            // Re-seed whichever presentation just became visible.
            RefreshChainPageAsync(_rxPage, GetSelectedIndex(_rxPage));
            RefreshChainPageAsync(_txPage, GetSelectedIndex(_txPage));
        }

        private void SetShowSnapshots(bool show)
        {
            if (VstHost.UiState.ShowSnapshots == show)
                return;

            VstHost.UiState.ShowSnapshots = show;
            VstHost.ScheduleUiStateSave();

            // Free the decoded bitmaps while hidden; they reload on demand when
            // switched back on.
            if (!show)
                VstPluginArt.Clear();

            _rxPage.RackView.Invalidate(true);
            _txPage.RackView.Invalidate(true);
        }

        /// <summary>
        /// Deletes the editor screenshots Thetis captured. Vendor snapshots
        /// shipped inside plugin bundles are untouched.
        /// </summary>
        private void ClearCapturedSnapshots()
        {
            DialogResult confirm = MessageBox.Show(
                this,
                "Delete all plugin snapshots captured by Thetis?\r\n\r\n" +
                "Artwork supplied by the plugin vendor is not affected. " +
                "Captured snapshots are recreated the next time you open a plugin's editor.",
                "Clear Snapshots",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            // Release the cached bitmaps first so nothing holds the files open.
            VstPluginArt.Clear();

            int removed = VstHost.ClearCapturedArt();

            _rxPage.RackView.Invalidate(true);
            _txPage.RackView.Invalidate(true);

            MessageBox.Show(
                this,
                removed == 1
                    ? "Removed 1 captured snapshot."
                    : string.Format("Removed {0} captured snapshots.", removed),
                "Clear Snapshots",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ApplyViewMode()
        {
            bool rack = _viewMode == VstChainViewMode.Rack;

            _updatingUi = true;
            try
            {
                _listViewRadio.Checked = !rack;
                _rackViewRadio.Checked = rack;

                ApplyViewModeToPage(_rxPage, rack);
                ApplyViewModeToPage(_txPage, rack);
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private static void ApplyViewModeToPage(ChainPage page, bool rack)
        {
            page.RackView.Visible = rack;
            page.PluginListView.Visible = !rack;

            if (rack)
                page.RackView.BringToFront();
            else
                page.PluginListView.BringToFront();
        }

        #endregion

        #region Status refresh

        private void UpdateHostStatus(VstHostState rxHostState, VstHostState txHostState)
        {
            string modeText = string.Format(
                "RX {0} | TX {1}",
                VstHost.GetHostStateDisplayName(rxHostState),
                VstHost.GetHostStateDisplayName(txHostState));
            string statusText = "VST3 plugins are supported.";
            if (!VstHost.NativeAvailable || !VstHost.SdkAvailable)
                statusText = VstHost.NativeStatusText + " " + statusText;
            if (!string.IsNullOrWhiteSpace(VstHost.PersistenceStatusText))
                statusText += " " + VstHost.PersistenceStatusText;

            _summaryLabel.Text = statusText;
            _hostStatusLabel.Text = modeText;

            _columnsPanel.Enabled = VstHost.NativeAvailable;
        }

        private void RefreshStatusOnlyAsync()
        {
            _statusRefreshInProgress = true;
            Task.Run(() =>
            {
                var snapshot = new StatusSnapshot
                {
                    RxHostState = VstHost.GetHostState(VstChainKind.Rx),
                    TxHostState = VstHost.GetHostState(VstChainKind.Tx),
                    RxReady = VstHost.GetChainReady(VstChainKind.Rx),
                    TxReady = VstHost.GetChainReady(VstChainKind.Tx)
                };
                int rxLatBlocks, rxLatFloor, rxSR, rxBS;
                int txLatBlocks, txLatFloor, txSR, txBS;
                VstHost.GetPipelineLatency(VstChainKind.Rx, out rxLatBlocks, out rxLatFloor, out rxSR, out rxBS);
                VstHost.GetPipelineLatency(VstChainKind.Tx, out txLatBlocks, out txLatFloor, out txSR, out txBS);
                snapshot.RxLatencyBlocks = rxLatBlocks;
                snapshot.RxLatencyFloor = rxLatFloor;
                snapshot.RxSampleRate = rxSR;
                snapshot.RxBlockSize = rxBS;
                snapshot.TxLatencyBlocks = txLatBlocks;
                snapshot.TxLatencyFloor = txLatFloor;
                snapshot.TxSampleRate = txSR;
                snapshot.TxBlockSize = txBS;
                return snapshot;
            }).ContinueWith(task =>
            {
                if (IsDisposed || Disposing || !IsHandleCreated)
                {
                    _statusRefreshInProgress = false;
                    return;
                }

                BeginInvoke((MethodInvoker)delegate
                {
                    _statusRefreshInProgress = false;

                    if (task.IsFaulted)
                    {
                        System.Diagnostics.Trace.WriteLine("VST status refresh failed: " +
                            (task.Exception != null ? task.Exception.GetBaseException().Message : "unknown"));
                        return;
                    }

                    if (task.Result == null)
                        return;

                    UpdateHostStatus(task.Result.RxHostState, task.Result.TxHostState);
                    UpdateChainStatusLabel(_rxPage, task.Result.RxReady, GetDisplayedPluginCount(_rxPage), task.Result.RxHostState);
                    UpdateChainStatusLabel(_txPage, task.Result.TxReady, GetDisplayedPluginCount(_txPage), task.Result.TxHostState);
                    UpdateLatencyLabel(_rxPage, task.Result.RxLatencyBlocks, task.Result.RxLatencyFloor, task.Result.RxSampleRate, task.Result.RxBlockSize);
                    UpdateLatencyLabel(_txPage, task.Result.TxLatencyBlocks, task.Result.TxLatencyFloor, task.Result.TxSampleRate, task.Result.TxBlockSize);
                });
            }, TaskScheduler.Default);
        }

        private static void UpdateChainStatusLabel(ChainPage page, bool ready, int pluginCount, VstHostState hostState)
        {
            page.ChainStatusLabel.Text = string.Format(
                "{0} · {1} · {2}/{3}",
                ready ? "Ready" : "Not ready",
                VstHost.GetHostStateDisplayName(hostState),
                pluginCount,
                MaxPluginsPerChain);
        }

        private void UpdateLatencyLabel(ChainPage page, int currentBlocks, int floorBlocks, int sampleRate, int blockSize)
        {
            double latencyMs = sampleRate > 0 && blockSize > 0
                ? (double)currentBlocks * blockSize / sampleRate * 1000.0
                : 0.0;

            page.LatencyLabel.Text = sampleRate > 0
                ? string.Format("{0} blocks ({1:F1}ms)", currentBlocks, latencyMs)
                : "";

            _updatingUi = true;
            if (floorBlocks >= (int)page.LatencyFloorUpDown.Minimum &&
                floorBlocks <= (int)page.LatencyFloorUpDown.Maximum)
                page.LatencyFloorUpDown.Value = floorBlocks;
            _updatingUi = false;
        }

        private static int GetDisplayedPluginCount(ChainPage page)
        {
            if (page.LastChainInfo != null && page.LastChainInfo.Plugins != null)
                return page.LastChainInfo.Plugins.Count;

            return 0;
        }

        #endregion

        #region Chain refresh

        private static bool IsStubRow(VstPluginState plugin)
        {
            return plugin != null && plugin.LoadState == VstPluginLoadState.Failed;
        }

        /// <summary>
        /// Builds the ordered rows the UI shows for a chain: imported entries
        /// keep their original position, with failed ones rendered as
        /// "not installed" stubs and healthy ones matched to the live chain
        /// plugins in order. With no import history the live chain is used
        /// directly.
        /// </summary>
        private static List<VstPluginState> BuildDisplayPluginList(ChainPage page, VstChainInfo chainInfo)
        {
            List<VstPluginState> requested = VstHost.GetRequestedPlugins(page.Kind);
            Dictionary<int, VstPluginState> failed = VstHost.GetFailedPlugins(page.Kind);

            List<VstPluginState> result = new List<VstPluginState>();

            if (requested == null || requested.Count == 0)
            {
                if (chainInfo != null && chainInfo.Plugins != null)
                    result.AddRange(chainInfo.Plugins);
                return result;
            }

            HashSet<string> failedPaths = new HashSet<string>();
            if (failed != null)
            {
                foreach (VstPluginState s in failed.Values)
                    failedPaths.Add(s.Path ?? string.Empty);
            }

            int loadedIdx = 0;
            List<VstPluginState> loaded = chainInfo != null ? chainInfo.Plugins : null;

            for (int i = 0; i < requested.Count; i++)
            {
                VstPluginState requestedPlugin = requested[i];
                if (requestedPlugin == null || string.IsNullOrEmpty(requestedPlugin.Path))
                    continue;

                if (failedPaths.Contains(requestedPlugin.Path))
                {
                    result.Add(requestedPlugin);
                }
                else if (loaded != null && loadedIdx < loaded.Count)
                {
                    result.Add(loaded[loadedIdx++]);
                }
            }

            return result;
        }

        /// <summary>
        /// Maps a display row (which may include "not installed" stub rows) to
        /// the live native chain index, or -1 for a stub row.
        /// </summary>
        private static int GetNativePluginIndex(ChainPage page, int rowIndex)
        {
            if (rowIndex < 0)
                return -1;

            List<VstPluginState> requested = VstHost.GetRequestedPlugins(page.Kind);
            if (requested == null || requested.Count == 0)
                return rowIndex;

            HashSet<string> failedPaths = new HashSet<string>();
            Dictionary<int, VstPluginState> failed = VstHost.GetFailedPlugins(page.Kind);
            if (failed != null)
            {
                foreach (VstPluginState s in failed.Values)
                    failedPaths.Add(s.Path ?? string.Empty);
            }

            int row = 0;
            int nativeIdx = 0;
            for (int ri = 0; ri < requested.Count; ri++)
            {
                VstPluginState p = requested[ri];
                if (p == null || string.IsNullOrEmpty(p.Path))
                    continue;

                bool isFailed = failedPaths.Contains(p.Path ?? string.Empty);
                if (row == rowIndex)
                    return isFailed ? -1 : nativeIdx;

                if (!isFailed)
                    nativeIdx++;
                row++;
            }
            return -1;
        }

        private static bool ChainStructureEquals(VstChainInfo previous, VstChainInfo current)
        {
            if (previous == null || current == null)
                return false;
            if (previous.Plugins == null || current.Plugins == null)
                return previous.Plugins == null && current.Plugins == null;
            if (previous.Plugins.Count != current.Plugins.Count)
                return false;

            for (int i = 0; i < current.Plugins.Count; i++)
            {
                string prevPath = previous.Plugins[i] != null ? previous.Plugins[i].Path : null;
                string currPath = current.Plugins[i] != null ? current.Plugins[i].Path : null;
                if (!string.Equals(prevPath, currPath, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private static bool ChainPluginStatesEqual(VstChainInfo previous, VstChainInfo current)
        {
            if (previous == null || current == null)
                return false;
            if (previous.Plugins == null || current.Plugins == null)
                return previous.Plugins == null && current.Plugins == null;
            if (previous.Plugins.Count != current.Plugins.Count)
                return false;

            for (int i = 0; i < current.Plugins.Count; i++)
            {
                VstPluginState prev = previous.Plugins[i];
                VstPluginState curr = current.Plugins[i];
                if (prev == null || curr == null)
                {
                    if (prev != null || curr != null)
                        return false;
                    continue;
                }

                if (prev.Enabled != curr.Enabled ||
                    prev.Bypass != curr.Bypass ||
                    prev.LoadState != curr.LoadState)
                    return false;
            }

            return true;
        }

        private static string BuildTrackingSignature(List<VstPluginState> requested, Dictionary<int, VstPluginState> failed)
        {
            if (requested == null || requested.Count == 0)
                return string.Empty;

            HashSet<string> failedPaths = new HashSet<string>();
            if (failed != null)
            {
                foreach (VstPluginState s in failed.Values)
                    failedPaths.Add(s.Path ?? string.Empty);
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < requested.Count; i++)
            {
                VstPluginState p = requested[i];
                if (p == null || string.IsNullOrEmpty(p.Path))
                    continue;
                sb.Append(p.Path);
                sb.Append(failedPaths.Contains(p.Path) ? "F;" : "L;");
            }
            return sb.ToString();
        }

        private void ApplyChainPageRefresh(ChainPage page, VstChainInfo chainInfo, int preferredIndex)
        {
            int selectedIndex = preferredIndex;

            _updatingUi = true;

            try
            {
                List<VstPluginState> requested = VstHost.GetRequestedPlugins(page.Kind);
                Dictionary<int, VstPluginState> failed = VstHost.GetFailedPlugins(page.Kind);
                string trackingSignature = BuildTrackingSignature(requested, failed);
                bool structureUnchanged = ChainStructureEquals(page.LastChainInfo, chainInfo);
                bool stateUnchanged = ChainPluginStatesEqual(page.LastChainInfo, chainInfo);
                bool trackingUnchanged = string.Equals(trackingSignature, page.LastTrackingSignature, StringComparison.Ordinal);
                bool bypassChanged = page.LastChainInfo == null || page.LastChainInfo.Bypass != chainInfo.Bypass;

                page.LastChainInfo = chainInfo;
                page.ChainBypassCheckBox.Checked = chainInfo.Bypass;
                page.RackView.ChainBypassed = chainInfo.Bypass;
                page.GainUpDown.Value = ClampDecimal(chainInfo.Gain, page.GainUpDown.Minimum, page.GainUpDown.Maximum);
                if (chainInfo.LatencyFloorBlocks >= (int)page.LatencyFloorUpDown.Minimum &&
                    chainInfo.LatencyFloorBlocks <= (int)page.LatencyFloorUpDown.Maximum)
                    page.LatencyFloorUpDown.Value = chainInfo.LatencyFloorBlocks;
                UpdateChainStatusLabel(page, chainInfo.Ready, chainInfo.Plugins.Count, chainInfo.HostState);

                if (structureUnchanged && stateUnchanged && trackingUnchanged && !bypassChanged)
                {
                    UpdateSelection(page);
                    return;
                }

                page.LastTrackingSignature = trackingSignature;

                List<VstPluginState> displayPlugins = BuildDisplayPluginList(page, chainInfo);
                page.LastDisplayPlugins = displayPlugins;

                page.PluginListView.BeginUpdate();
                page.PluginListView.Items.Clear();

                for (int i = 0; i < displayPlugins.Count; i++)
                {
                    VstPluginState plugin = displayPlugins[i];
                    bool isStub = IsStubRow(plugin);
                    ListViewItem item = new ListViewItem((i + 1).ToString());
                    item.SubItems.Add(VstHost.GetPluginDisplayName(plugin));
                    item.SubItems.Add(VstHost.GetPluginFormatDisplayName(plugin.Format));
                    item.SubItems.Add(isStub ? "Failed" : VstHost.GetLoadStateDisplayName(plugin.LoadState));
                    item.SubItems.Add(isStub ? "No" : (plugin.Enabled ? "Yes" : "No"));
                    item.SubItems.Add(isStub ? "Yes" : ((chainInfo.Bypass || plugin.Bypass) ? "Yes" : "No"));
                    item.SubItems.Add(isStub ? "Not Installed" : (plugin.Path ?? string.Empty));
                    item.Tag = plugin;
                    if (isStub)
                        item.ForeColor = SystemColors.GrayText;
                    page.PluginListView.Items.Add(item);
                }

                for (int i = displayPlugins.Count; i < MaxPluginsPerChain; i++)
                {
                    ListViewItem item = new ListViewItem((i + 1).ToString());
                    item.SubItems.Add(string.Empty);
                    item.SubItems.Add(string.Empty);
                    item.SubItems.Add(string.Empty);
                    item.SubItems.Add(string.Empty);
                    item.SubItems.Add(string.Empty);
                    item.SubItems.Add(string.Empty);
                    item.ForeColor = SystemColors.GrayText;
                    page.PluginListView.Items.Add(item);
                }

                page.PluginListView.EndUpdate();

                page.RackView.SetPlugins(displayPlugins, MaxPluginsPerChain);

                if (selectedIndex >= 0 && selectedIndex < displayPlugins.Count)
                {
                    page.PluginListView.Items[selectedIndex].Selected = true;
                    page.PluginListView.Items[selectedIndex].Focused = true;
                    page.RackView.SelectedIndex = selectedIndex;
                }
                else
                {
                    page.RackView.SelectedIndex = -1;
                }
            }
            finally
            {
                _updatingUi = false;
            }

            UpdateSelection(page);
        }

        private void RefreshChainPageAsync(ChainPage page)
        {
            RefreshChainPageAsync(page, GetSelectedIndex(page));
        }

        private void QueueChainPageRefresh(ChainPage page, int preferredIndex)
        {
            page.PendingPreferredIndex = preferredIndex;
            if (page.DeferredRefreshTimer == null)
            {
                RefreshChainPageAsync(page, preferredIndex);
                return;
            }

            page.DeferredRefreshTimer.Stop();
            page.DeferredRefreshTimer.Start();
        }

        private void RefreshChainPageAsync(ChainPage page, int preferredIndex)
        {
            if (page.RefreshInProgress)
            {
                page.RefreshPending = true;
                page.PendingPreferredIndex = preferredIndex;
                return;
            }

            page.RefreshInProgress = true;
            Task.Run(() => VstHost.GetChainInfo(page.Kind)).ContinueWith(task =>
            {
                if (IsDisposed || Disposing || !IsHandleCreated)
                {
                    page.RefreshInProgress = false;
                    return;
                }

                BeginInvoke((MethodInvoker)delegate
                {
                    page.RefreshInProgress = false;

                    if (task.IsFaulted)
                        System.Diagnostics.Trace.WriteLine("VST chain refresh failed: " +
                            (task.Exception != null ? task.Exception.GetBaseException().Message : "unknown"));

                    if (!task.IsFaulted && task.Result != null)
                        ApplyChainPageRefresh(page, task.Result, preferredIndex);

                    if (page.RefreshPending)
                    {
                        page.RefreshPending = false;
                        RefreshChainPageAsync(page, page.PendingPreferredIndex);
                    }
                });
            }, TaskScheduler.Default);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                VstHost.ChainStateChanged -= OnChainStateChanged;

                if (_statusTimer != null)
                {
                    _statusTimer.Stop();
                    _statusTimer.Dispose();
                }
                DisposeChainPageTimer(_rxPage);
                DisposeChainPageTimer(_txPage);
                VstPluginArt.Clear();
            }

            base.Dispose(disposing);
        }

        private void OnChainStateChanged(VstChainKind kind)
        {
            if (IsDisposed || Disposing || !IsHandleCreated)
                return;

            BeginInvoke((MethodInvoker)delegate
            {
                if (IsDisposed || Disposing)
                    return;

                ChainPage page = kind == VstChainKind.Rx ? _rxPage : _txPage;
                RefreshChainPageAsync(page);
            });
        }

        private static void DisposeChainPageTimer(ChainPage page)
        {
            if (page == null || page.DeferredRefreshTimer == null)
                return;

            page.DeferredRefreshTimer.Stop();
            page.DeferredRefreshTimer.Dispose();
            page.DeferredRefreshTimer = null;
        }

        #region Selection

        private void UpdateSelection(ChainPage page)
        {
            int selectedIndex = GetSelectedIndex(page);
            VstPluginState plugin = GetSelectedPlugin(page);
            bool hasSelection = plugin != null;
            int pluginCount = GetDisplayedPluginCount(page);
            bool chainHasCapacity = pluginCount < MaxPluginsPerChain;
            bool isStub = IsStubRow(plugin);

            page.AddButton.Enabled = chainHasCapacity;
            page.RemoveButton.Enabled = hasSelection;
            page.MoveUpButton.Enabled = hasSelection && !isStub && selectedIndex > 0;
            page.MoveDownButton.Enabled = hasSelection && !isStub && selectedIndex >= 0 && selectedIndex < GetDisplayCount(page) - 1;
            page.ToggleEnabledButton.Enabled = hasSelection && !isStub;
            page.ToggleBypassButton.Enabled = hasSelection && !isStub;
            page.OpenEditorButton.Enabled = hasSelection && !isStub && plugin != null && plugin.LoadState == VstPluginLoadState.Active;

            if (!hasSelection)
            {
                page.ToggleEnabledButton.Text = "Enable";
                page.ToggleBypassButton.Text = "Bypass";

                if (_activePage == page)
                    _detailLabel.Text = "Select a plugin to view its load state and path.";

                return;
            }

            page.ToggleEnabledButton.Text = isStub ? "Enable" : (plugin.Enabled ? "Disable" : "Enable");
            page.ToggleBypassButton.Text = isStub ? "Bypass" : (plugin.Bypass ? "Unbypass" : "Bypass");

            if (_activePage == page)
            {
                _detailLabel.Text = string.Format(
                    "{0}  ·  {1}  ·  {2}  ·  {3}  ·  {4}  ·  {5}",
                    VstHost.GetChainDisplayName(page.Kind),
                    VstHost.GetPluginDisplayName(plugin),
                    VstHost.GetPluginFormatDisplayName(plugin.Format),
                    isStub ? "Failed" : VstHost.GetLoadStateDisplayName(plugin.LoadState),
                    isStub ? "not installed" : (plugin.Enabled ? (plugin.Bypass ? "bypassed" : "enabled") : "disabled"),
                    isStub ? "Not Installed" : (plugin.Path ?? string.Empty));
            }
        }

        private int GetSelectedIndex(ChainPage page)
        {
            if (_viewMode == VstChainViewMode.Rack)
                return page.RackView.SelectedIndex;

            if (page.PluginListView.SelectedIndices.Count == 0)
                return -1;

            return page.PluginListView.SelectedIndices[0];
        }

        private VstPluginState GetSelectedPlugin(ChainPage page)
        {
            int selectedIndex = GetSelectedIndex(page);

            if (selectedIndex < 0)
                return null;

            return GetPluginAt(page, selectedIndex);
        }

        private VstPluginState GetPluginAt(ChainPage page, int index)
        {
            if (index < 0 || page.LastDisplayPlugins == null)
                return null;

            if (index >= page.LastDisplayPlugins.Count)
                return null;

            return page.LastDisplayPlugins[index];
        }

        private int GetDisplayCount(ChainPage page)
        {
            if (page.LastDisplayPlugins != null)
                return page.LastDisplayPlugins.Count;

            return GetDisplayedPluginCount(page);
        }

        #endregion

        #region Chain operations

        private void AddPluginFromCatalog(ChainPage page)
        {
            if (!CanAddPlugin(page))
                return;

            using (VstPluginPickerForm dialog = new VstPluginPickerForm())
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                AddPlugin(page, dialog.SelectedPluginPath, dialog.SelectedPluginClassId);
            }
        }

        private void AddPlugin(ChainPage page, string pluginPath, string classId = null)
        {
            VstOperationResult result = VstHost.AddPlugin(page.Kind, pluginPath, classId);
            _activePage = page;
            RefreshChainPageAsync(page, result.PluginIndex);

            if (!result.Success || result.HasWarning)
            {
                MessageBox.Show(
                    this,
                    result.Message,
                    "VST Chains",
                    MessageBoxButtons.OK,
                    result.Success ? MessageBoxIcon.Warning : MessageBoxIcon.Error);
            }
        }

        private bool CanAddPlugin(ChainPage page)
        {
            if (GetDisplayedPluginCount(page) < MaxPluginsPerChain)
                return true;

            MessageBox.Show(
                this,
                string.Format("That chain has reached the maximum of {0} plugins.", MaxPluginsPerChain),
                "Chain Full",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return false;
        }

        private void RemovePluginAt(ChainPage page, int index)
        {
            VstPluginState plugin = GetPluginAt(page, index);

            if (index < 0 || plugin == null)
                return;

            if (IsStubRow(plugin))
            {
                if (!VstHost.RemoveRequestedPlugin(page.Kind, plugin.Path))
                {
                    MessageBox.Show(this, "The missing plugin entry could not be removed.", "VST Chains", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                RefreshChainPageAsync(page, Math.Max(0, index - 1));
                return;
            }

            int nativeIndex = GetNativePluginIndex(page, index);
            if (nativeIndex < 0 || !VstHost.RemovePlugin(page.Kind, nativeIndex))
            {
                MessageBox.Show(this, "The native host could not remove the selected plugin.", "VST Chains", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _activePage = page;
            RefreshChainPageAsync(page, Math.Max(0, index - 1));
        }

        private void MovePluginAt(ChainPage page, int index, int delta)
        {
            int targetIndex = index + delta;

            if (index < 0)
                return;
            if (targetIndex < 0 || targetIndex >= GetDisplayCount(page))
                return;

            int fromNativeIndex = GetNativePluginIndex(page, index);
            int toNativeIndex = GetNativePluginIndex(page, targetIndex);

            if (fromNativeIndex < 0 || toNativeIndex < 0)
                return;

            if (!VstHost.MovePlugin(page.Kind, fromNativeIndex, toNativeIndex))
            {
                MessageBox.Show(this, "The native host could not reorder the selected plugin.", "VST Chains", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _activePage = page;
            RefreshChainPageAsync(page, targetIndex);
        }

        private void TogglePluginEnabledAt(ChainPage page, int index)
        {
            VstPluginState plugin = GetPluginAt(page, index);

            if (plugin == null || IsStubRow(plugin))
                return;

            int nativeIndex = GetNativePluginIndex(page, index);
            if (nativeIndex < 0 || !VstHost.SetPluginEnabled(page.Kind, nativeIndex, !plugin.Enabled))
            {
                MessageBox.Show(this, "The native host could not update the plugin enabled state.", "VST Chains", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _activePage = page;
            QueueChainPageRefresh(page, index);
        }

        private void TogglePluginBypassAt(ChainPage page, int index)
        {
            VstPluginState plugin = GetPluginAt(page, index);

            if (plugin == null || IsStubRow(plugin))
                return;

            int nativeIndex = GetNativePluginIndex(page, index);
            if (nativeIndex < 0 || !VstHost.SetPluginBypass(page.Kind, nativeIndex, !plugin.Bypass))
            {
                MessageBox.Show(this, "The native host could not update the plugin bypass state.", "VST Chains", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _activePage = page;
            QueueChainPageRefresh(page, index);
        }

        private void OpenPluginEditorAt(ChainPage page, int index)
        {
            VstPluginState plugin = GetPluginAt(page, index);

            if (plugin == null || IsStubRow(plugin))
                return;

            int nativeIndex = GetNativePluginIndex(page, index);
            if (nativeIndex < 0 || plugin.LoadState != VstPluginLoadState.Active)
            {
                MessageBox.Show(this, "Only loaded plugins can open an editor.", "VST Chains", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_openingEditor)
                return;

            _activePage = page;
            _openingEditor = true;
            page.OpenEditorButton.Enabled = false;
            UseWaitCursor = true;

            Task.Run(() => VstHost.OpenPluginEditorWindow(page.Kind, nativeIndex)).ContinueWith(task =>
            {
                if (IsDisposed || Disposing)
                    return;

                BeginInvoke((MethodInvoker)delegate
                {
                    _openingEditor = false;
                    UseWaitCursor = false;
                    UpdateSelection(page);

                    if (task.IsFaulted || !task.Result)
                    {
                        MessageBox.Show(this, "The plugin editor could not be opened.", "VST Chains", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }

                    CaptureEditorArtworkAsync(page, plugin);
                });
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// Screenshots the editor the host just opened, so plugins without
        /// vendor snapshot artwork still get a face in the rack. Best effort —
        /// failures leave the drawn placeholder in place.
        /// </summary>
        private void CaptureEditorArtworkAsync(ChainPage page, VstPluginState plugin)
        {
            if (plugin == null || string.IsNullOrWhiteSpace(plugin.Path))
                return;

            string pluginPath = plugin.Path;
            string pluginClassId = plugin.ClassId;

            Task.Run(() =>
            {
                // Give the plugin time to finish painting; editors draw
                // asynchronously and an immediate grab catches a half-built UI.
                System.Threading.Thread.Sleep(EditorCaptureSettleMs);
                return VstEditorCapture.TryCaptureEditor(plugin);
            }).ContinueWith(captureTask =>
            {
                if (IsDisposed || Disposing || !IsHandleCreated)
                    return;

                if (captureTask.IsFaulted || string.IsNullOrEmpty(captureTask.Result))
                    return;

                VstPluginArt.Invalidate(pluginPath, pluginClassId);

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (!IsDisposed && !Disposing)
                            page.RackView.Invalidate(true);
                    });
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            }, TaskScheduler.Default);
        }

        private void ExportChain(ChainPage page)
        {
            VstProfileState profileState = new VstProfileState();
            VstChainState rxState = VstHost.CaptureChainPersistentState(VstChainKind.Rx);
            VstChainState txState = VstHost.CaptureChainPersistentState(VstChainKind.Tx);
            if (rxState != null && rxState.Plugins != null && rxState.Plugins.Count > 0)
                profileState.Rx = rxState;
            if (txState != null && txState.Plugins != null && txState.Plugins.Count > 0)
                profileState.Tx = txState;

            string activeProfile = VstHost.GetCurrentProfileName();
            string vstDir = VstHost.GetVstDirectoryPath();

            string suggestedName = string.IsNullOrEmpty(activeProfile)
                ? "vst_profile.json"
                : "vst_chains_tx_" + activeProfile + ".json";

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "Export Full Profile";
                dialog.Filter = "TX Profile Chains (vst_chains_tx_*.json)|vst_chains_tx_*.json|All JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                dialog.DefaultExt = "json";
                dialog.FileName = suggestedName;
                if (!string.IsNullOrEmpty(vstDir) && Directory.Exists(vstDir))
                    dialog.InitialDirectory = vstDir;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    string json = Newtonsoft.Json.JsonConvert.SerializeObject(profileState, Newtonsoft.Json.Formatting.Indented);
                    File.WriteAllText(dialog.FileName, json);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Failed to export chain: " + ex.Message, "Export Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportChain(ChainPage page)
        {
            string chainName = VstHost.GetChainDisplayName(page.Kind);
            string vstDir = VstHost.GetVstDirectoryPath();

            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Import " + chainName + " Chain";
                dialog.Filter = "TX Profile Chains (vst_chains_tx_*.json)|vst_chains_tx_*.json|All JSON Files (*.json)|*.json|All Files (*.*)|*.*";
                dialog.DefaultExt = "json";
                dialog.CheckFileExists = true;
                if (!string.IsNullOrEmpty(vstDir) && Directory.Exists(vstDir))
                    dialog.InitialDirectory = vstDir;

                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                try
                {
                    string json = File.ReadAllText(dialog.FileName);
                    Newtonsoft.Json.Linq.JObject obj = Newtonsoft.Json.Linq.JObject.Parse(json);
                    Dictionary<int, VstPluginState> rxFailed = null;
                    Dictionary<int, VstPluginState> txFailed = null;

                    if (obj.Property("Rx") != null || obj.Property("Tx") != null)
                    {
                        VstProfileState profileState = Newtonsoft.Json.JsonConvert.DeserializeObject<VstProfileState>(json);
                        if (profileState == null || (profileState.Rx == null && profileState.Tx == null))
                        {
                            MessageBox.Show(this, "Failed to parse the profile chain file.", "Import Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        if (profileState.Rx != null)
                            rxFailed = VstHost.ApplyChainPersistentState(VstChainKind.Rx, profileState.Rx);
                        if (profileState.Tx != null)
                            txFailed = VstHost.ApplyChainPersistentState(VstChainKind.Tx, profileState.Tx);
                    }
                    else
                    {
                        VstChainState state = Newtonsoft.Json.JsonConvert.DeserializeObject<VstChainState>(json);
                        if (state == null)
                        {
                            MessageBox.Show(this, "Failed to parse the chain file.", "Import Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        if (page.Kind == VstChainKind.Rx)
                            rxFailed = VstHost.ApplyChainPersistentState(VstChainKind.Rx, state);
                        else
                            txFailed = VstHost.ApplyChainPersistentState(VstChainKind.Tx, state);
                    }

                    RefreshChainPageAsync(_rxPage);
                    RefreshChainPageAsync(_txPage);

                    List<string> failedNames = new List<string>();
                    if (rxFailed != null)
                    {
                        foreach (var kv in rxFailed)
                            failedNames.Add(!string.IsNullOrEmpty(kv.Value.Name) ? kv.Value.Name : kv.Value.Path);
                    }
                    if (txFailed != null)
                    {
                        foreach (var kv in txFailed)
                            failedNames.Add(!string.IsNullOrEmpty(kv.Value.Name) ? kv.Value.Name : kv.Value.Path);
                    }

                    if (failedNames.Count > 0)
                    {
                        string msg = "The following plugins could not be loaded:\n\n  - "
                            + string.Join("\n  - ", failedNames)
                            + "\n\nThe plugins may not be installed on this system.\n"
                            + "They are shown at their original position with 'Failed' status.";
                        MessageBox.Show(this, msg, "Plugins Not Found",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Failed to import chain: " + ex.Message, "Import Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        #endregion

        private static decimal ClampDecimal(double value, decimal minimum, decimal maximum)
        {
            decimal decimalValue = (decimal)value;
            if (decimalValue < minimum) return minimum;
            if (decimalValue > maximum) return maximum;
            return decimalValue;
        }
    }
}
