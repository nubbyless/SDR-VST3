/*  SigIdForm.cs

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
// SigID - "Digi Id" window.                                                                    //
//                                                                                              //
// Modeless popup, opened from the main menu directly after WSJT-X.  Phase 1 scope: the        //
// signal-database group (install / update / status).  The identification group is a            //
// placeholder until the analysis engine lands (Phase 3/4).                                     //
//                                                                                              //
// All UI is built in code (same pattern as DxClusterForm / VstChainManagerForm).  The          //
// palette below is PROVISIONAL - final colours are decided once the feature works.             //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Thetis
{
    public sealed class SigIdForm : Form
    {
        private const string GEOMETRY_TABLE = "SigIdForm";

        private SigIdAnalyzer _analyzer;
        private Console _console;

        private Label lblDatabaseTitle;
        private Label lblDatabaseUrl;
        private LinkLabel lnkDatabaseUrl;
        private Label lblNotice;
        private Label lblStatus;
        private Label lblInstalled;
        private ProgressBar progress;
        private Button btnInstall;
        private Button btnRebuild;
        private Button btnCancel;
        private CheckBox chkEnabled;
        private Label lblReceiver;
        private ComboBox comboReceiver;
        private ComboBox comboScope;
        private Label lblScope;
        private Label lblFeatures;
        private DataGridView gridCandidates;
        private CheckBox chkAlwaysOnTop;
        private ToolStripButton btnTheme;
        private ToolStrip toolStrip;
        private GroupBox groupDatabase;
        private GroupBox groupEngine;
        private System.Windows.Forms.Timer refreshTimer;
        private StatusStrip statusStrip;
        private ToolStripStatusLabel lblPath;

        private CancellationTokenSource _cancel;
        private SigIdReleaseInfo _latestRelease;
        private SigIdDatabase _database;
        private bool _busy;

        public SigIdForm(Console console)
        {
            _console = console;

            // the theme has to be set before the controls are built, they pick their
            // colours up from the palette
            SigIdTheme.SetDark(LoadDarkTheme());

            BuildUi();
            ApplyTheme();
            RefreshState();

            // closing the window only hides it - the analyzer keeps running so the
            // panadapter tag stays live, and the menu entry brings the window back
            FormClosing += (s, e) =>
            {
                if (e.CloseReason != CloseReason.UserClosing) return;
                e.Cancel = true;
                Hide();
            };

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 500;
            refreshTimer.Tick += (s, e) => UpdateCandidates();
            refreshTimer.Start();
        }

        /// <summary>Attaches the analyzer that produces the candidates.</summary>
        public void AttachAnalyzer(SigIdAnalyzer analyzer)
        {
            _analyzer = analyzer;
            if (_analyzer == null) return;

            chkEnabled.Checked = _analyzer.Enabled;
            comboScope.SelectedIndex = (int)_analyzer.Scope;
            UpdateCandidates();
        }

        // ------------------------------------------------------------------ UI

        private void BuildUi()
        {
            SuspendLayout();

            Text = "Digi Id";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            // own taskbar button, like the DX Cluster window - otherwise the window
            // disappears behind the console with no way of getting it back
            ShowInTaskbar = true;
            ClientSize = new Size(700, 586);
            BackColor = SigIdTheme.Void;
            ForeColor = SigIdTheme.TextPrimary;
            Font = new Font("Microsoft Sans Serif", 8.25f);

            GroupBox groupDatabase = new GroupBox
            {
                Text = "Signal database",
                BackColor = SigIdTheme.Face,
                ForeColor = SigIdTheme.TextPrimary,
                Location = new Point(12, 36),
                Size = new Size(676, 176)
            };

            lblDatabaseTitle = new Label
            {
                Text = "Artemis community signal-identification database:",
                ForeColor = SigIdTheme.TextSecondary,
                AutoSize = true,
                Location = new Point(14, 24)
            };

            lblDatabaseUrl = new Label
            {
                Text = "https://github.com/AresValley/Artemis-Database/releases",
                ForeColor = SigIdTheme.Accent,
                AutoSize = true,
                Location = new Point(14, 44)
            };

            lnkDatabaseUrl = new LinkLabel
            {
                Text = "open in browser",
                AutoSize = true,
                Location = new Point(300, 44),
                LinkColor = SigIdTheme.Accent,
                ActiveLinkColor = SigIdTheme.Accent
            };
            lnkDatabaseUrl.LinkClicked += (s, e) => OpenUrl(SigIdInstaller.RELEASES_PAGE_URL);

            lblNotice = new Label
            {
                Text = "The signal database is required to operate Digi Id.",
                ForeColor = SigIdTheme.Error,
                AutoSize = true,
                Location = new Point(14, 70)
            };

            lblStatus = new Label
            {
                Text = "Checking...",
                ForeColor = SigIdTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(14, 92)
            };

            lblInstalled = new Label
            {
                Text = string.Empty,
                ForeColor = SigIdTheme.TextSecondary,
                AutoSize = true,
                Location = new Point(14, 112)
            };

            progress = new ProgressBar
            {
                Style = ProgressBarStyle.Continuous,
                Location = new Point(14, 134),
                Size = new Size(508, 14),
                Visible = false
            };

            btnInstall = new Button
            {
                Text = "Install Database",
                Location = new Point(14, 154),
                Size = new Size(140, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = SigIdTheme.Face,
                ForeColor = SigIdTheme.TextPrimary
            };
            btnInstall.Click += btnInstall_Click;

            btnRebuild = new Button
            {
                Text = "Rebuild reference data",
                Location = new Point(160, 154),
                Size = new Size(150, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = SigIdTheme.Face,
                ForeColor = SigIdTheme.TextPrimary
            };
            btnRebuild.Click += btnRebuild_Click;

            btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(160, 154),
                Size = new Size(90, 26),
                FlatStyle = FlatStyle.Flat,
                BackColor = SigIdTheme.Face,
                ForeColor = SigIdTheme.TextPrimary,
                Enabled = false,
                Visible = false
            };
            btnCancel.Click += btnCancel_Click;


            groupDatabase.Controls.Add(lblDatabaseTitle);
            groupDatabase.Controls.Add(lblDatabaseUrl);
            groupDatabase.Controls.Add(lnkDatabaseUrl);
            groupDatabase.Controls.Add(lblNotice);
            groupDatabase.Controls.Add(lblStatus);
            groupDatabase.Controls.Add(lblInstalled);
            groupDatabase.Controls.Add(progress);
            groupDatabase.Controls.Add(btnInstall);
            groupDatabase.Controls.Add(btnRebuild);
            groupDatabase.Controls.Add(btnCancel);

            GroupBox groupEngine = new GroupBox
            {
                Text = "Identification",
                BackColor = SigIdTheme.Face,
                ForeColor = SigIdTheme.TextPrimary,
                Location = new Point(12, 218),
                Size = new Size(676, 96)
            };

            chkEnabled = new CheckBox
            {
                Text = "Enable identification",
                ForeColor = SigIdTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(14, 24),
                Checked = false
            };
            chkEnabled.CheckedChanged += chkEnabled_CheckedChanged;

            lblReceiver = new Label
            {
                Text = "Receiver:",
                ForeColor = SigIdTheme.TextSecondary,
                AutoSize = true,
                Location = new Point(150, 26)
            };

            lblScope = new Label
            {
                Text = "Look for:",
                ForeColor = SigIdTheme.TextSecondary,
                AutoSize = true,
                Location = new Point(300, 26)
            };

            comboScope = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = SigIdTheme.Inset,
                ForeColor = SigIdTheme.TextPrimary,
                Location = new Point(356, 22),
                Size = new Size(150, 22)
            };
            comboScope.Items.Add(SigIdScopes.Describe(SigIdScope.AmateurRadio));
            comboScope.Items.Add(SigIdScopes.Describe(SigIdScope.HfDigitalModes));
            comboScope.Items.Add(SigIdScopes.Describe(SigIdScope.AllSignals));
            comboScope.SelectedIndex = (int)LoadScope();
            comboScope.SelectedIndexChanged += comboScope_SelectedIndexChanged;

            comboReceiver = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = SigIdTheme.Inset,
                ForeColor = SigIdTheme.TextPrimary,
                Location = new Point(204, 22),
                Size = new Size(70, 22)
            };
            comboReceiver.Items.Add("RX1");
            comboReceiver.Items.Add("RX2");
            comboReceiver.SelectedIndex = 0;
            comboReceiver.SelectedIndexChanged += comboReceiver_SelectedIndexChanged;



            chkAlwaysOnTop = new CheckBox
            {
                Text = "Always on top",
                ForeColor = SigIdTheme.TextPrimary,
                AutoSize = true,
                Location = new Point(528, 24),
                Checked = LoadAlwaysOnTop()
            };
            chkAlwaysOnTop.CheckedChanged += chkAlwaysOnTop_CheckedChanged;
            TopMost = chkAlwaysOnTop.Checked;


            lblFeatures = new Label
            {
                Text = "Identification is switched off.",
                ForeColor = SigIdTheme.TextSecondary,
                AutoSize = false,
                Location = new Point(14, 52),
                Size = new Size(500, 36)
            };

            groupEngine.Controls.Add(chkEnabled);
            groupEngine.Controls.Add(lblReceiver);
            groupEngine.Controls.Add(comboReceiver);
            groupEngine.Controls.Add(lblScope);
            groupEngine.Controls.Add(comboScope);
            groupEngine.Controls.Add(chkAlwaysOnTop);
            groupEngine.Controls.Add(lblFeatures);

            // the candidate list, themed the same way as the DX Cluster grids
            gridCandidates = new DataGridView
            {
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
                BackgroundColor = SigIdTheme.Inset,
                GridColor = SigIdTheme.Border,
                EnableHeadersVisualStyles = false,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
                ColumnHeadersHeight = 24,
                ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = SigIdTheme.Face,
                    ForeColor = SigIdTheme.TextPrimary,
                    SelectionBackColor = SigIdTheme.Face,
                    SelectionForeColor = SigIdTheme.TextPrimary,
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                },
                DefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = SigIdTheme.Inset,
                    ForeColor = SigIdTheme.TextPrimary,
                    SelectionBackColor = SigIdTheme.Selection,
                    SelectionForeColor = SigIdTheme.TextPrimary,
                    Alignment = DataGridViewContentAlignment.MiddleLeft,
                },
                AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
                {
                    BackColor = SigIdTheme.Gutter,
                    ForeColor = SigIdTheme.TextPrimary,
                    SelectionBackColor = SigIdTheme.Selection,
                    SelectionForeColor = SigIdTheme.TextPrimary,
                },
                Location = new Point(12, 322),
                Size = new Size(676, 214)
            };
            gridCandidates.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Signal", Width = 210, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridCandidates.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Confidence", Width = 80, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridCandidates.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Matched on", Width = 90, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridCandidates.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Parameters", Width = 280, ReadOnly = true, SortMode = DataGridViewColumnSortMode.NotSortable });
            gridCandidates.CellDoubleClick += gridCandidates_CellDoubleClick;


            statusStrip = new StatusStrip { BackColor = SigIdTheme.Face, ForeColor = SigIdTheme.TextSecondary };
            lblPath = new ToolStripStatusLabel { Text = SigIdPaths.Root };
            statusStrip.Items.Add(lblPath);

            // the theme picker sits in the top right corner, like the DX Cluster window
            toolStrip = new ToolStrip
            {
                Dock = DockStyle.Top,
                BackColor = SigIdTheme.Face,
                ForeColor = SigIdTheme.TextPrimary,
                GripStyle = ToolStripGripStyle.Hidden,
                Padding = new Padding(0)
            };
            btnTheme = new ToolStripButton("Light");
            btnTheme.DisplayStyle = ToolStripItemDisplayStyle.Text;
            btnTheme.Alignment = ToolStripItemAlignment.Right;
            btnTheme.Click += btnTheme_Click;
            toolStrip.Items.Add(btnTheme);

            ToolStripLabel themeLabel = new ToolStripLabel("Theme:");
            themeLabel.Alignment = ToolStripItemAlignment.Right;
            themeLabel.ForeColor = SigIdTheme.TextSecondary;
            toolStrip.Items.Add(themeLabel);

            Controls.Add(groupDatabase);
            Controls.Add(groupEngine);
            Controls.Add(gridCandidates);
            Controls.Add(statusStrip);
            Controls.Add(toolStrip);

            ResumeLayout(true);

            try
            {
                Common.RestoreForm(this, GEOMETRY_TABLE, false);
            }
            catch (Exception)
            {
                // geometry is cosmetic only
            }
        }

        // ------------------------------------------------------------------ state

        /// <summary>Re-reads the installed database and updates every control.</summary>
        public void RefreshState()
        {
            _database = SigIdDatabase.Open();

            if (_database != null && _database.HasSignals)
            {
                lblNotice.Text = "The signal database is installed.";
                lblNotice.ForeColor = SigIdTheme.Ok;
                lblStatus.Text = "Database v" + _database.Version +
                                 (string.IsNullOrWhiteSpace(_database.ReleaseDate) ? string.Empty : " (" + _database.ReleaseDate + ")") +
                                 " - " + _database.Signals.Count + " signals.";
                lblStatus.ForeColor = SigIdTheme.TextPrimary;
                btnInstall.Text = "Reinstall Database";
                btnInstall.Enabled = !_busy;
            }
            else
            {
                lblNotice.Text = "The signal database is required to operate Digi Id.";
                lblNotice.ForeColor = SigIdTheme.Error;
                lblStatus.Text = "No database installed.";
                lblStatus.ForeColor = SigIdTheme.TextSecondary;
                btnInstall.Text = "Install Database";
                btnInstall.Enabled = !_busy;
            }

            lblInstalled.Text = "Folder: " + SigIdPaths.Root;

            // the matcher is only as good as the measured reference data, so say when it is
            // missing - the user has to press "Rebuild reference data" in that case
            if (_database != null && _database.HasSignals)
            {
                SigIdFeatureIndex index = SigIdFeatureIndex.Load();
                int profiles = index != null ? index.Count : 0;
                if (profiles == 0)
                {
                    lblInstalled.Text += "     Reference data not built - click Rebuild reference data.";
                    lblInstalled.ForeColor = SigIdTheme.Warning;
                }
                else
                {
                    lblInstalled.Text += "     Reference data: " + profiles + " signals.";
                }
            }

            if (_latestRelease != null && _database != null && _latestRelease.Version > _database.Version)
            {
                lblInstalled.Text += "     Update available: v" + _latestRelease.Version;
                lblInstalled.ForeColor = SigIdTheme.Warning;
            }
            else
            {
                lblInstalled.ForeColor = SigIdTheme.TextSecondary;
            }
        }

        /// <summary>
        /// Refreshes the state from the UI thread.  The release check runs on a background
        /// thread, so every refresh has to be marshalled.
        /// </summary>
        private void SafeRefreshState()
        {
            try
            {
                if (IsDisposed) return;
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(SafeRefreshState));
                    return;
                }
                RefreshState();
            }
            catch (Exception)
            {
                // the window may have gone away mid-update
            }
        }

        /// <summary>
        /// Checks GitHub for a newer release.  Never downloads - it only reports.
        /// </summary>
        public async Task CheckForUpdateAsync(bool force)
        {
            SigIdInstallInfo info = SigIdInstaller.GetCurrentInstallInfo();
            if (!force && info != null && !info.CheckIsStale(SigIdInstaller.CHECK_INTERVAL_FORM))
            {
                _latestRelease = new SigIdReleaseInfo
                {
                    Version = info.LatestVersion,
                    TagName = info.LatestTag,
                    PublishedUtc = info.LatestPublishedUtc
                };
                SafeRefreshState();
                return;
            }

            try
            {
                SigIdReleaseInfo release = await SigIdInstaller
                    .FetchLatestReleaseAsync(CancellationToken.None)
                    .ConfigureAwait(true);

                if (release == null) return;

                _latestRelease = release;
                SigIdInstaller.RecordCheck(release);
                SafeRefreshState();
            }
            catch (Exception)
            {
                // an unreachable GitHub is not an error worth surfacing
            }
        }

        // ------------------------------------------------------------------ actions

        private async void btnInstall_Click(object sender, EventArgs e)
        {
            if (_busy) return;

            _busy = true;
            btnInstall.Enabled = false;
            progress.Visible = true;
            progress.Value = 0;

            _cancel = new CancellationTokenSource();

            string archive = null;

            try
            {
                // the check that ran earlier may have been restored from install.json, which
                // only keeps the version and the tag - no asset URL - so a cached release is
                // not installable.  Fetch the full release description in that case.
                if (_latestRelease == null || !_latestRelease.IsValid)
                {
                    lblStatus.Text = "Looking for the newest database release...";
                    _latestRelease = await SigIdInstaller
                        .FetchLatestReleaseAsync(_cancel.Token)
                        .ConfigureAwait(true);
                }

                if (_latestRelease == null || !_latestRelease.IsValid)
                {
                    lblStatus.Text = "No database release could be found.";
                    lblStatus.ForeColor = SigIdTheme.Error;
                    return;
                }

                lblStatus.Text = "Downloading database v" + _latestRelease.Version + "...";
                lblStatus.ForeColor = SigIdTheme.TextPrimary;
                btnCancel.Enabled = true;
                btnCancel.Visible = true;

                archive = await SigIdInstaller
                    .DownloadReleaseAsync(_latestRelease, OnDownloadProgress, _cancel.Token)
                    .ConfigureAwait(true);

                btnCancel.Enabled = false;
                lblStatus.Text = "Extracting...";

                // the reference data is measured in the background: the matcher cannot use
                // the new database until that has finished, so say so and report the end
                SigIdInstaller.Install(archive, _latestRelease, OnIndexProgress, OnIndexDone);

                lblStatus.Text = "Database installed.";
                lblStatus.ForeColor = SigIdTheme.Ok;
            }
            catch (OperationCanceledException)
            {
                lblStatus.Text = "Download cancelled.";
                lblStatus.ForeColor = SigIdTheme.Warning;
            }
            catch (Exception ex)
            {
                lblStatus.Text = "Install failed: " + ex.Message;
                lblStatus.ForeColor = SigIdTheme.Error;
            }
            finally
            {
                if (archive != null) SigIdInstaller.DeleteArchive(archive);

                if (_cancel != null)
                {
                    _cancel.Dispose();
                    _cancel = null;
                }

                _busy = false;
                progress.Visible = false;
                btnCancel.Enabled = false;
                btnCancel.Visible = false;
                RefreshState();
            }
        }

        /// <summary>
        /// Rebuilds the measured profile of every reference recording.  This is what gives
        /// the matcher a repetition interval and a bandwidth for every signal, even the
        /// ones the wiki documents none of.
        /// </summary>
        private void btnRebuild_Click(object sender, EventArgs e)
        {
            btnRebuild.Enabled = false;
            lblStatus.Text = "Measuring the reference recordings, this takes a minute...";
            lblStatus.ForeColor = SigIdTheme.TextSecondary;
            Refresh();

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    SigIdDatabase database = SigIdDatabase.Open();
                    if (database == null) return;

                    SigIdFeatureIndex index = SigIdFeatureIndex.Build(database, SigIdPaths.MediaDirectory, null);
                    if (index.Count > 0) index.Save();
                }
                catch (Exception ex)
                {
                    Common.LogString("[sigid] reference data build failed: " + ex.Message);
                }
            }).ContinueWith(t =>
            {
                try
                {
                    if (IsDisposed) return;
                    BeginInvoke(new Action(() =>
                    {
                        btnRebuild.Enabled = true;
                        lblStatus.Text = "Reference data rebuilt.";
                        lblStatus.ForeColor = SigIdTheme.Ok;
                    }));
                }
                catch (Exception) { }
            });
        }

        /// <summary>
        /// Called from the background index build while it measures the reference
        /// recordings, so the user can see that the job after the download is still running.
        /// </summary>
        private void OnIndexProgress(string message)
        {
            if (IsDisposed) return;

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnIndexProgress(message)));
                    return;
                }

                lblStatus.Text = "Building reference data...  " + message;
                lblStatus.ForeColor = SigIdTheme.Warning;
            }
            catch (Exception)
            {
                // the window may have gone away mid-update
            }
        }

        /// <summary>
        /// Called when the background index build finished.  A positive count is the number
        /// of measured profiles, 0 means nothing could be measured and -1 means it failed.
        /// </summary>
        private void OnIndexDone(int count)
        {
            if (IsDisposed) return;

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnIndexDone(count)));
                    return;
                }

                if (count > 0)
                {
                    lblStatus.Text = "Reference data ready (" + count + " signals measured).";
                    lblStatus.ForeColor = SigIdTheme.Ok;
                }
                else if (count == 0)
                {
                    lblStatus.Text = "Reference data was not built - use \"Rebuild reference data\".";
                    lblStatus.ForeColor = SigIdTheme.Warning;
                }
                else
                {
                    lblStatus.Text = "Reference data build failed - see the log, then rebuild.";
                    lblStatus.ForeColor = SigIdTheme.Error;
                }
            }
            catch (Exception)
            {
                // the window may have gone away mid-update
            }
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            try
            {
                if (_cancel != null) _cancel.Cancel();
            }
            catch (Exception)
            {
                // ignore
            }
        }

        private void OnDownloadProgress(SigIdDownloadProgress progress)
        {
            if (IsDisposed) return;

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => OnDownloadProgress(progress)));
                    return;
                }

                this.progress.Value = Math.Max(0, Math.Min(100, progress.Percentage));

                long totalMb = progress.TotalBytes / 1048576;
                long doneMb = progress.BytesDownloaded / 1048576;
                lblStatus.Text = "Downloading database v" +
                                 (_latestRelease != null ? _latestRelease.Version.ToString() : "?") +
                                 "...  " + doneMb + " / " + totalMb + " MB (" + progress.Percentage + " %)  " +
                                 progress.MegabytesPerSecond.ToString("0.0") + " MB/s";
            }
            catch (Exception)
            {
                // the window may have gone away mid-update
            }
        }

        /// <summary>Copies the current analysis result into the candidate list.</summary>
        private void UpdateCandidates()
        {
            // tell the analyzer what the receiver is doing
            if (_console != null && _analyzer != null)
            {
                try
                {
                    int receiver = comboReceiver.SelectedIndex == 1 ? 1 : 0;
                    double vfo = receiver == 1 ? _console.VFOBFreq : _console.VFOAFreq;
                    int low = _console.radio.GetDSPRX(receiver, 0).RXFilterLow;
                    int high = _console.radio.GetDSPRX(receiver, 0).RXFilterHigh;
                    _analyzer.SetTuning(vfo * 1e6, Math.Abs(high - low));
                }
                catch (Exception)
                {
                    // the radio may not be connected yet
                }
            }

            SigIdAnalysisSnapshot snapshot = _analyzer != null ? _analyzer.Snapshot : null;

            bool databaseReady = SigIdDatabase.Open() != null;
            int tapLevel = SigIdAudioTap.FifoLevel(comboReceiver.SelectedIndex == 1 ? SigIdAudioTap.RX2 : SigIdAudioTap.RX1);
            int tapBlocks = (int)SigIdAudioTap.BlockCount(comboReceiver.SelectedIndex == 1 ? SigIdAudioTap.RX2 : SigIdAudioTap.RX1);

            if (!databaseReady)
            {
                lblFeatures.Text = "Install the signal database to use identification.";
                lblFeatures.ForeColor = SigIdTheme.Warning;
            }
            else if (_analyzer == null)
            {
                lblFeatures.Text = "The signal database could not be opened - see the log.";
                lblFeatures.ForeColor = SigIdTheme.Error;
            }
            else if (snapshot == null || !snapshot.Valid)
            {
                lblFeatures.Text = "Waiting for audio.  Tap blocks " + tapBlocks + ", queued " + tapLevel +
                                   " samples - check that a receiver is running.";
                lblFeatures.ForeColor = SigIdTheme.TextSecondary;
            }
            else
            {
                SigIdFeatures features = snapshot.Features;
                lblFeatures.Text = "Signal: " + features.Summarise() +
                                   "    modulation: " + features.ModulationClass +
                                   "    SNR " + features.SnrDb.ToString("0") + " dB" +
                                   (features.FilterLimited ? "    (limited by the RX filter)" : string.Empty);
                lblFeatures.ForeColor = SigIdTheme.TextPrimary;
            }

            gridCandidates.SuspendLayout();
            try
            {
                gridCandidates.Rows.Clear();
                if (snapshot == null || snapshot.Candidates.Count == 0) return;

                int count = Math.Min(25, snapshot.Candidates.Count);
                for (int i = 0; i < count; i++)
                {
                    SigIdCandidate candidate = snapshot.Candidates[i];
                    int rowIndex = gridCandidates.Rows.Add(candidate.Name,
                                                           candidate.Percent + " %",
                                                           candidate.Reason,
                                                           DescribeCandidate(candidate));
                    gridCandidates.Rows[rowIndex].Tag = candidate;
                }
            }
            finally
            {
                gridCandidates.ResumeLayout();
            }
        }

        private static string DescribeCandidate(SigIdCandidate candidate)
        {
            if (candidate == null || candidate.Signal == null) return string.Empty;

            List<string> parts = new List<string>();
            if (candidate.Signal.Bandwidths.Count > 0)
            {
                List<string> widths = new List<string>();
                foreach (int width in candidate.Signal.Bandwidths)
                {
                    widths.Add(width >= 1000 ? (width / 1000.0).ToString("0.#") + " kHz" : width + " Hz");
                }
                parts.Add(string.Join("/", widths.ToArray()));
            }
            if (candidate.Signal.Modulations.Count > 0)
                parts.Add(string.Join("/", candidate.Signal.Modulations.ToArray()));
            if (candidate.Signal.Categories.Count > 0)
                parts.Add(string.Join("/", candidate.Signal.Categories.ToArray()));

            return string.Join("  ", parts.ToArray());
        }

        private void btnTheme_Click(object sender, EventArgs e)
        {
            SigIdTheme.SetDark(!SigIdTheme.IsDark);
            SaveTheme();
            ApplyTheme();
        }

        /// <summary>Applies the current palette to every control of the window.</summary>
        private void ApplyTheme()
        {
            if (btnTheme != null) btnTheme.Text = SigIdTheme.IsDark ? "Light" : "Dark";

            BackColor = SigIdTheme.Void;
            ForeColor = SigIdTheme.TextPrimary;

            if (toolStrip != null)
            {
                toolStrip.BackColor = SigIdTheme.Face;
                toolStrip.ForeColor = SigIdTheme.TextPrimary;
                if (btnTheme != null)
                {
                    btnTheme.BackColor = SigIdTheme.Button;
                    btnTheme.ForeColor = SigIdTheme.TextPrimary;
                }
            }

            foreach (Control control in Controls)
            {
                if (control is GroupBox group)
                {
                    group.BackColor = SigIdTheme.Face;
                    group.ForeColor = SigIdTheme.TextPrimary;
                }
                else if (control is ListView list)
                {
                    list.BackColor = SigIdTheme.Inset;
                    list.ForeColor = SigIdTheme.TextPrimary;
                }
                else if (control is StatusStrip strip)
                {
                    strip.BackColor = SigIdTheme.Face;
                    strip.ForeColor = SigIdTheme.TextSecondary;
                }
            }

            if (groupDatabase != null)
            {
                groupDatabase.BackColor = SigIdTheme.Face;
                groupDatabase.ForeColor = SigIdTheme.TextPrimary;
            }
            if (groupEngine != null)
            {
                groupEngine.BackColor = SigIdTheme.Face;
                groupEngine.ForeColor = SigIdTheme.TextPrimary;
            }

            if (gridCandidates != null)
            {
                gridCandidates.BackgroundColor = SigIdTheme.Inset;
                gridCandidates.GridColor = SigIdTheme.Border;
                gridCandidates.ColumnHeadersDefaultCellStyle.BackColor = SigIdTheme.Face;
                gridCandidates.ColumnHeadersDefaultCellStyle.ForeColor = SigIdTheme.TextPrimary;
                gridCandidates.ColumnHeadersDefaultCellStyle.SelectionBackColor = SigIdTheme.Face;
                gridCandidates.ColumnHeadersDefaultCellStyle.SelectionForeColor = SigIdTheme.TextPrimary;
                gridCandidates.DefaultCellStyle.BackColor = SigIdTheme.Inset;
                gridCandidates.DefaultCellStyle.ForeColor = SigIdTheme.TextPrimary;
                gridCandidates.DefaultCellStyle.SelectionBackColor = SigIdTheme.Selection;
                gridCandidates.DefaultCellStyle.SelectionForeColor = SigIdTheme.TextPrimary;
                gridCandidates.AlternatingRowsDefaultCellStyle.BackColor = SigIdTheme.Gutter;
                gridCandidates.AlternatingRowsDefaultCellStyle.ForeColor = SigIdTheme.TextPrimary;
                gridCandidates.AlternatingRowsDefaultCellStyle.SelectionBackColor = SigIdTheme.Selection;
                gridCandidates.AlternatingRowsDefaultCellStyle.SelectionForeColor = SigIdTheme.TextPrimary;
                foreach (DataGridViewRow row in gridCandidates.Rows)
                {
                    row.DefaultCellStyle.BackColor = SigIdTheme.Inset;
                    row.DefaultCellStyle.ForeColor = SigIdTheme.TextPrimary;
                    row.DefaultCellStyle.SelectionBackColor = SigIdTheme.Selection;
                    row.DefaultCellStyle.SelectionForeColor = SigIdTheme.TextPrimary;
                }
                gridCandidates.Invalidate();
            }
            if (comboReceiver != null)
            {
                comboReceiver.BackColor = SigIdTheme.Inset;
                comboReceiver.ForeColor = SigIdTheme.TextPrimary;
            }
            if (chkEnabled != null) chkEnabled.ForeColor = SigIdTheme.TextPrimary;
            if (chkAlwaysOnTop != null) chkAlwaysOnTop.ForeColor = SigIdTheme.TextPrimary;

            if (lblDatabaseUrl != null) lblDatabaseUrl.ForeColor = SigIdTheme.Accent;
            if (lnkDatabaseUrl != null)
            {
                lnkDatabaseUrl.LinkColor = SigIdTheme.Accent;
                lnkDatabaseUrl.ActiveLinkColor = SigIdTheme.Accent;
            }
            if (lblReceiver != null) lblReceiver.ForeColor = SigIdTheme.TextSecondary;
            if (lblScope != null) lblScope.ForeColor = SigIdTheme.TextSecondary;
            if (comboScope != null)
            {
                comboScope.BackColor = SigIdTheme.Inset;
                comboScope.ForeColor = SigIdTheme.TextPrimary;
            }

            foreach (Button button in new Button[] { btnInstall, btnRebuild, btnCancel })
            {
                if (button == null) continue;
                button.BackColor = SigIdTheme.Button;
                button.ForeColor = SigIdTheme.TextPrimary;
            }
        }

        /// <summary>Reads the theme choice from the Thetis database.</summary>
        private static bool LoadDarkTheme()
        {
            try
            {
                List<string> vars = DB.GetVars("SigId");
                if (vars == null) return true;
                foreach (string value in vars)
                {
                    if (value.StartsWith("darkTheme/"))
                        return value.Substring("darkTheme/".Length) == "True";
                }
            }
            catch (Exception) { }
            return true;
        }

        /// <summary>Stores the theme choice in the Thetis database.</summary>
        private void SaveTheme()
        {
            try
            {
                List<string> vars = DB.GetVars("SigId") ?? new List<string>();
                vars.RemoveAll(delegate (string v) { return v.StartsWith("darkTheme/"); });
                vars.Add("darkTheme/" + SigIdTheme.IsDark);
                DB.SaveVars("SigId", vars, true);
            }
            catch (Exception) { }
        }

        private void comboScope_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_analyzer == null) return;
            _analyzer.Scope = CurrentScope();
            SaveScope();
            UpdateCandidates();
        }

        /// <summary>The scope the operator picked in the combo box.</summary>
        private SigIdScope CurrentScope()
        {
            switch (comboScope.SelectedIndex)
            {
                case 1: return SigIdScope.HfDigitalModes;
                case 2: return SigIdScope.AllSignals;
                default: return SigIdScope.AmateurRadio;
            }
        }

        /// <summary>Reads the scope from the Thetis database.</summary>
        private static SigIdScope LoadScope()
        {
            try
            {
                List<string> vars = DB.GetVars("SigId");
                if (vars == null) return SigIdScope.AmateurRadio;
                foreach (string value in vars)
                {
                    if (!value.StartsWith("scope/")) continue;
                    int number;
                    if (int.TryParse(value.Substring("scope/".Length), out number) &&
                        Enum.IsDefined(typeof(SigIdScope), number))
                    {
                        return (SigIdScope)number;
                    }
                }
            }
            catch (Exception) { }
            return SigIdScope.AmateurRadio;
        }

        /// <summary>Stores the scope in the Thetis database.</summary>
        private void SaveScope()
        {
            try
            {
                List<string> vars = DB.GetVars("SigId") ?? new List<string>();
                vars.RemoveAll(delegate (string v) { return v.StartsWith("scope/"); });
                vars.Add("scope/" + (int)CurrentScope());
                DB.SaveVars("SigId", vars, true);
            }
            catch (Exception) { }
        }

        private void chkEnabled_CheckedChanged(object sender, EventArgs e)
        {
            if (_analyzer == null) return;
            _analyzer.SetEnabled(chkEnabled.Checked);
            UpdateCandidates();
        }

        private void chkAlwaysOnTop_CheckedChanged(object sender, EventArgs e)
        {
            TopMost = chkAlwaysOnTop.Checked;
            SaveAlwaysOnTop();
        }

        /// <summary>Reads the "always on top" setting from the Thetis database.</summary>
        private static bool LoadAlwaysOnTop()
        {
            try
            {
                List<string> vars = DB.GetVars("SigId");
                if (vars == null) return false;
                foreach (string value in vars)
                {
                    if (value.StartsWith("alwaysOnTop/"))
                        return value.Substring("alwaysOnTop/".Length) == "True";
                }
            }
            catch (Exception)
            {
                // a missing setting is not an error
            }
            return false;
        }

        /// <summary>Stores the "always on top" setting in the Thetis database.</summary>
        private void SaveAlwaysOnTop()
        {
            try
            {
                List<string> vars = DB.GetVars("SigId") ?? new List<string>();
                vars.RemoveAll(delegate (string v) { return v.StartsWith("alwaysOnTop/"); });
                vars.Add("alwaysOnTop/" + chkAlwaysOnTop.Checked);
                DB.SaveVars("SigId", vars, true);
            }
            catch (Exception)
            {
                // the setting is a convenience, never fatal
            }
        }


        private void comboReceiver_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_analyzer == null) return;
            _analyzer.SetReceiver(comboReceiver.SelectedIndex == 1 ? SigIdAudioTap.RX2 : SigIdAudioTap.RX1);
        }

        private void gridCandidates_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (gridCandidates.CurrentRow == null) return;
            SigIdCandidate candidate = gridCandidates.CurrentRow.Tag as SigIdCandidate;
            if (candidate == null || candidate.Signal == null) return;
            if (string.IsNullOrWhiteSpace(candidate.Signal.Url)) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(candidate.Signal.Url)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception)
            {
                // no browser available
            }
        }

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // no browser available - nothing useful to do
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try
            {
                Common.SaveForm(this, GEOMETRY_TABLE);
            }
            catch (Exception)
            {
                // geometry is cosmetic only
            }
            base.OnFormClosed(e);
        }
    }
}
