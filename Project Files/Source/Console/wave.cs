using System;
using System.Collections;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace Thetis
{
    public class WaveControl : Form
    {
        private Console console;
        private WaveOptions waveOptionsForm;
        private readonly ArrayList file_list = new ArrayList();

        private readonly string wave_folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Thetis");

        private OpenFileDialog openFileDialog1;
        private ButtonTS btnPlay;
        private GroupBoxTS groupBox2;
        public CheckBoxTS checkBoxRecord;
        private RadioButtonTS radRecRX1;
        private RadioButtonTS radRecRX2;
        private GroupBoxTS grpPlayback;
        private ButtonTS btnStop;
        private ButtonTS btnPause;
        private ButtonTS btnPrevious;
        private ButtonTS btnNext;
        private ListBox lstPlaylist;
        private ButtonTS btnAdd;
        private ButtonTS btnRemove;
        private CheckBoxTS checkBoxLoop;
        private GroupBox grpPlaylist;
        private TextBoxTS txtCurrentFile;
        private LabelTS lblCurrentlyPlaying;
        private GroupBoxTS grpTxGain;
        private NumericUpDownTS udTxGain;
        private TrackBar tbTxGain;
        private LabelTS lblNote;
        private TextBox txtNotes;
        private PictureBox picLogo;
        private MenuStrip menuStripOptions;
        private ToolStripMenuItem optionsToolStripMenuItem;
        private CheckBoxTS chkAlwaysOnTop;
        private CheckBoxTS chkQuickRecMp3;
        private ButtonTS btnQuickRecMp3Folder;
        private IContainer components;
        private bool _restoring_controls;

        private int currently_playing = -1;
        private bool temp_vacbypass_play = false;
        private bool _old_record_state = false;
        private int _record_wfw_id = 0;
        private volatile bool _advancing = false;
        private bool _isPlaying = false;
        private bool _isPaused = false;

        public WaveControl(Console c)
        {
            console = c;
            if (!Directory.Exists(wave_folder))
            {
                try { Directory.CreateDirectory(wave_folder); } catch { }
            }

            InitializeComponent();
            openFileDialog1.InitialDirectory = wave_folder;

            waveOptionsForm = new WaveOptions(console);

            _restoring_controls = true;
            try { Common.RestoreForm(this, "WaveOptions", false); } catch { }

            _isPlaying = false;
            _isPaused = false;
            try { btnPlay.BackColor = SystemColors.Control; btnPause.BackColor = SystemColors.Control; } catch { }
            try { btnPause.Enabled = false; btnStop.Enabled = false; } catch { }
            checkBoxRecord.Checked = false;
            checkBoxLoop.Checked = false;
            try
            {
                if (console != null && console.ARP != null)
                {
                    decimal v = (decimal)Math.Max((double)udTxGain.Minimum, Math.Min((double)udTxGain.Maximum, console.ARP.WDSPTXAudioGainInDb));
                    udTxGain.Value = v;
                    try { tbTxGain.Value = (int)v; } catch { }
                }
            }
            catch { }
            try { if (console != null) { chkQuickRecMp3.Checked = console.QuickRecGenerateMp3; } } catch { }
            _restoring_controls = false;

            this.FormClosing += WaveControl_FormClosing;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { StopPlaybackInternal(); } catch { }
                try { StopRecordInternal(); } catch { }
                if (components != null) components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WaveControl));
            this.openFileDialog1 = new OpenFileDialog();
            this.grpPlaylist = new GroupBox();
            this.checkBoxLoop = new CheckBoxTS();
            this.btnAdd = new ButtonTS();
            this.lstPlaylist = new ListBox();
            this.btnRemove = new ButtonTS();
            this.groupBox2 = new GroupBoxTS();
            this.checkBoxRecord = new CheckBoxTS();
            this.radRecRX1 = new RadioButtonTS();
            this.radRecRX2 = new RadioButtonTS();
            this.grpTxGain = new GroupBoxTS();
            this.udTxGain = new NumericUpDownTS();
            this.tbTxGain = new TrackBar();
            this.grpPlayback = new GroupBoxTS();
            this.txtCurrentFile = new TextBoxTS();
            this.lblCurrentlyPlaying = new LabelTS();
            this.lblNote = new LabelTS();
            this.txtNotes = new TextBox();
            this.picLogo = new PictureBox();
            this.btnNext = new ButtonTS();
            this.btnPrevious = new ButtonTS();
            this.btnPause = new ButtonTS();
            this.btnStop = new ButtonTS();
            this.btnPlay = new ButtonTS();
            this.menuStripOptions = new MenuStrip();
            this.optionsToolStripMenuItem = new ToolStripMenuItem();
            this.chkAlwaysOnTop = new CheckBoxTS();
            this.chkQuickRecMp3 = new CheckBoxTS();
            this.btnQuickRecMp3Folder = new ButtonTS();
            this.grpPlaylist.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.grpTxGain.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.udTxGain)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbTxGain)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.picLogo)).BeginInit();
            this.grpPlayback.SuspendLayout();
            this.menuStripOptions.SuspendLayout();
            this.SuspendLayout();
            // 
            // openFileDialog1
            // 
            this.openFileDialog1.Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*";
            this.openFileDialog1.Multiselect = true;
            this.openFileDialog1.FileOk += new CancelEventHandler(this.openFileDialog1_FileOk);
            // 
            // grpPlaylist
            // 
            this.grpPlaylist.Controls.Add(this.checkBoxLoop);
            this.grpPlaylist.Controls.Add(this.btnAdd);
            this.grpPlaylist.Controls.Add(this.lstPlaylist);
            this.grpPlaylist.Controls.Add(this.btnRemove);
            this.grpPlaylist.Location = new Point(12, 150);
            this.grpPlaylist.Name = "grpPlaylist";
            this.grpPlaylist.Size = new Size(345, 184);
            this.grpPlaylist.TabIndex = 6;
            this.grpPlaylist.TabStop = false;
            this.grpPlaylist.Text = "Playlist";
            // 
            // checkBoxLoop
            // 
            this.checkBoxLoop.Appearance = Appearance.Button;
            this.checkBoxLoop.Enabled = false;
            this.checkBoxLoop.Image = null;
            this.checkBoxLoop.Location = new Point(208, 24);
            this.checkBoxLoop.Name = "checkBoxLoop";
            this.checkBoxLoop.Size = new Size(56, 23);
            this.checkBoxLoop.TabIndex = 12;
            this.checkBoxLoop.Text = "Loop";
            this.checkBoxLoop.TextAlign = ContentAlignment.MiddleCenter;
            this.checkBoxLoop.CheckedChanged += new EventHandler(this.checkBoxLoop_CheckedChanged);
            // 
            // btnAdd
            // 
            this.btnAdd.Image = null;
            this.btnAdd.Location = new Point(16, 24);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Selectable = true;
            this.btnAdd.Size = new Size(56, 23);
            this.btnAdd.TabIndex = 7;
            this.btnAdd.Text = "Add";
            this.btnAdd.Click += new EventHandler(this.btnAdd_Click);
            // 
            // lstPlaylist
            // 
            this.lstPlaylist.Location = new Point(16, 56);
            this.lstPlaylist.Name = "lstPlaylist";
            this.lstPlaylist.Size = new Size(313, 121);
            this.lstPlaylist.TabIndex = 11;
            this.lstPlaylist.SelectedIndexChanged += new EventHandler(this.lstPlaylist_SelectedIndexChanged);
            // 
            // btnRemove
            // 
            this.btnRemove.Enabled = false;
            this.btnRemove.Image = null;
            this.btnRemove.Location = new Point(80, 24);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Selectable = true;
            this.btnRemove.Size = new Size(56, 23);
            this.btnRemove.TabIndex = 8;
            this.btnRemove.Text = "Remove";
            this.btnRemove.Click += new EventHandler(this.btnRemove_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.radRecRX2);
            this.groupBox2.Controls.Add(this.radRecRX1);
            this.groupBox2.Controls.Add(this.checkBoxRecord);
            this.groupBox2.Location = new Point(363, 32);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new Size(106, 68);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Record";
            // 
            // checkBoxRecord
            // 
            this.checkBoxRecord.Appearance = Appearance.Button;
            this.checkBoxRecord.Image = null;
            this.checkBoxRecord.Location = new Point(17, 14);
            this.checkBoxRecord.Name = "checkBoxRecord";
            this.checkBoxRecord.Size = new Size(72, 23);
            this.checkBoxRecord.TabIndex = 2;
            this.checkBoxRecord.Text = "Record";
            this.checkBoxRecord.TextAlign = ContentAlignment.MiddleCenter;
            this.checkBoxRecord.CheckedChanged += new EventHandler(this.checkBoxRecord_CheckedChanged);
            // 
            // radRecRX1
            // 
            this.radRecRX1.AutoSize = true;
            this.radRecRX1.Checked = true;
            this.radRecRX1.Image = null;
            this.radRecRX1.Location = new Point(8, 44);
            this.radRecRX1.Name = "radRecRX1";
            this.radRecRX1.Size = new Size(46, 17);
            this.radRecRX1.TabIndex = 3;
            this.radRecRX1.TabStop = true;
            this.radRecRX1.Text = "RX1";
            this.radRecRX1.UseVisualStyleBackColor = true;
            // 
            // radRecRX2
            // 
            this.radRecRX2.AutoSize = true;
            this.radRecRX2.Image = null;
            this.radRecRX2.Location = new Point(54, 44);
            this.radRecRX2.Name = "radRecRX2";
            this.radRecRX2.Size = new Size(46, 17);
            this.radRecRX2.TabIndex = 4;
            this.radRecRX2.Text = "RX2";
            this.radRecRX2.UseVisualStyleBackColor = true;
            // 
            // grpTxGain
            // 
            this.grpTxGain.Controls.Add(this.tbTxGain);
            this.grpTxGain.Controls.Add(this.udTxGain);
            this.grpTxGain.Location = new Point(363, 104);
            this.grpTxGain.Name = "grpTxGain";
            this.grpTxGain.Size = new Size(106, 72);
            this.grpTxGain.TabIndex = 2;
            this.grpTxGain.TabStop = false;
            this.grpTxGain.Text = "TX Gain (dB)";
            // 
            // udTxGain
            // 
            this.udTxGain.Location = new Point(18, 16);
            this.udTxGain.Maximum = new decimal(new int[] { 70, 0, 0, 0 });
            this.udTxGain.Minimum = new decimal(new int[] { 70, 0, 0, -2147483648 });
            this.udTxGain.Name = "udTxGain";
            this.udTxGain.Size = new Size(70, 20);
            this.udTxGain.TabIndex = 0;
            this.udTxGain.TinyStep = false;
            this.udTxGain.Value = new decimal(new int[] { 0, 0, 0, 0 });
            this.udTxGain.ValueChanged += new EventHandler(this.udTxGain_ValueChanged);
            // 
            // tbTxGain
            // 
            this.tbTxGain.AutoSize = false;
            this.tbTxGain.Location = new Point(8, 42);
            this.tbTxGain.Maximum = 70;
            this.tbTxGain.Minimum = -70;
            this.tbTxGain.Name = "tbTxGain";
            this.tbTxGain.Size = new Size(90, 20);
            this.tbTxGain.TabIndex = 1;
            this.tbTxGain.TickFrequency = 35;
            this.tbTxGain.TickStyle = TickStyle.None;
            this.tbTxGain.Scroll += new EventHandler(this.tbTxGain_Scroll);
            // 
            // grpPlayback
            // 
            this.grpPlayback.Controls.Add(this.txtCurrentFile);
            this.grpPlayback.Controls.Add(this.lblCurrentlyPlaying);
            this.grpPlayback.Controls.Add(this.btnNext);
            this.grpPlayback.Controls.Add(this.btnPrevious);
            this.grpPlayback.Controls.Add(this.btnPause);
            this.grpPlayback.Controls.Add(this.btnStop);
            this.grpPlayback.Controls.Add(this.btnPlay);
            this.grpPlayback.Location = new Point(12, 32);
            this.grpPlayback.Name = "grpPlayback";
            this.grpPlayback.Size = new Size(345, 112);
            this.grpPlayback.TabIndex = 4;
            this.grpPlayback.TabStop = false;
            this.grpPlayback.Text = "Playback";
            // 
            // txtCurrentFile
            // 
            this.txtCurrentFile.Location = new Point(112, 24);
            this.txtCurrentFile.Name = "txtCurrentFile";
            this.txtCurrentFile.ReadOnly = true;
            this.txtCurrentFile.Size = new Size(217, 20);
            this.txtCurrentFile.TabIndex = 11;
            this.txtCurrentFile.TabStop = false;
            // 
            // lblCurrentlyPlaying
            // 
            this.lblCurrentlyPlaying.Location = new Point(16, 26);
            this.lblCurrentlyPlaying.Name = "lblCurrentlyPlaying";
            this.lblCurrentlyPlaying.Size = new Size(96, 16);
            this.lblCurrentlyPlaying.TabIndex = 10;
            this.lblCurrentlyPlaying.Text = "Currently Playing:";
            this.lblCurrentlyPlaying.TextAlign = ContentAlignment.MiddleLeft;
            // 
            // btnNext
            // 
            this.btnNext.Enabled = false;
            this.btnNext.Image = null;
            this.btnNext.Location = new Point(198, 60);
            this.btnNext.Name = "btnNext";
            this.btnNext.Selectable = true;
            this.btnNext.Size = new Size(40, 23);
            this.btnNext.TabIndex = 9;
            this.btnNext.Text = "Next";
            this.btnNext.Click += new EventHandler(this.btnNext_Click);
            // 
            // btnPrevious
            // 
            this.btnPrevious.Enabled = false;
            this.btnPrevious.Image = null;
            this.btnPrevious.Location = new Point(154, 60);
            this.btnPrevious.Name = "btnPrevious";
            this.btnPrevious.Selectable = true;
            this.btnPrevious.Size = new Size(40, 23);
            this.btnPrevious.TabIndex = 8;
            this.btnPrevious.Text = "Prev";
            this.btnPrevious.Click += new EventHandler(this.btnPrevious_Click);
            // 
            // btnPause
            // 
            this.btnPause.Enabled = false;
            this.btnPause.Image = null;
            this.btnPause.Location = new Point(104, 60);
            this.btnPause.Name = "btnPause";
            this.btnPause.Selectable = true;
            this.btnPause.Size = new Size(46, 23);
            this.btnPause.TabIndex = 6;
            this.btnPause.Text = "Pause";
            this.btnPause.Click += new EventHandler(this.btnPause_Click);
            // 
            // btnPlay
            // 
            this.btnPlay.Enabled = false;
            this.btnPlay.Image = null;
            this.btnPlay.Location = new Point(60, 60);
            this.btnPlay.Name = "btnPlay";
            this.btnPlay.Selectable = true;
            this.btnPlay.Size = new Size(40, 23);
            this.btnPlay.TabIndex = 5;
            this.btnPlay.Text = "Play";
            this.btnPlay.Click += new EventHandler(this.btnPlay_Click);
            // 
            // btnStop
            // 
            this.btnStop.Enabled = false;
            this.btnStop.Image = null;
            this.btnStop.Location = new Point(16, 60);
            this.btnStop.Name = "btnStop";
            this.btnStop.Selectable = true;
            this.btnStop.Size = new Size(40, 23);
            this.btnStop.TabIndex = 4;
            this.btnStop.Text = "Stop";
            this.btnStop.Click += new EventHandler(this.btnStop_Click);
            // 
            // menuStripOptions
            // 
            this.menuStripOptions.Items.AddRange(new ToolStripItem[] { this.optionsToolStripMenuItem });
            this.menuStripOptions.Location = new Point(0, 0);
            this.menuStripOptions.Name = "menuStripOptions";
            this.menuStripOptions.Size = new Size(481, 24);
            this.menuStripOptions.TabIndex = 66;
            // 
            // optionsToolStripMenuItem
            // 
            this.optionsToolStripMenuItem.Name = "optionsToolStripMenuItem";
            this.optionsToolStripMenuItem.Size = new Size(61, 20);
            this.optionsToolStripMenuItem.Text = "Options";
            this.optionsToolStripMenuItem.Click += new EventHandler(this.optionsToolStripMenuItem_Click);
            // 
            // chkAlwaysOnTop
            // 
            this.chkAlwaysOnTop.AutoSize = true;
            this.chkAlwaysOnTop.Image = null;
            this.chkAlwaysOnTop.Location = new Point(12, 408);
            this.chkAlwaysOnTop.Name = "chkAlwaysOnTop";
            this.chkAlwaysOnTop.Size = new Size(95, 20);
            this.chkAlwaysOnTop.TabIndex = 69;
            this.chkAlwaysOnTop.Text = "Always On Top";
            this.chkAlwaysOnTop.UseVisualStyleBackColor = true;
            this.chkAlwaysOnTop.CheckedChanged += new EventHandler(this.chkAlwaysOnTop_CheckedChanged);
            // 
            // chkQuickRecMp3
            // 
            this.chkQuickRecMp3.AutoSize = true;
            this.chkQuickRecMp3.Image = null;
            this.chkQuickRecMp3.Location = new Point(130, 408);
            this.chkQuickRecMp3.Name = "chkQuickRecMp3";
            this.chkQuickRecMp3.Size = new Size(165, 20);
            this.chkQuickRecMp3.TabIndex = 70;
            this.chkQuickRecMp3.Text = "Frontpanel Quick Rec As MP3";
            this.chkQuickRecMp3.UseVisualStyleBackColor = true;
            this.chkQuickRecMp3.CheckedChanged += new EventHandler(this.chkQuickRecMp3_CheckedChanged);
            // 
            // btnQuickRecMp3Folder
            // 
            this.btnQuickRecMp3Folder.Image = null;
            this.btnQuickRecMp3Folder.Location = new Point(320, 405);
            this.btnQuickRecMp3Folder.Name = "btnQuickRecMp3Folder";
            this.btnQuickRecMp3Folder.Selectable = true;
            this.btnQuickRecMp3Folder.Size = new Size(80, 25);
            this.btnQuickRecMp3Folder.TabIndex = 71;
            this.btnQuickRecMp3Folder.Text = "MP3 Folder";
            this.btnQuickRecMp3Folder.UseVisualStyleBackColor = true;
            this.btnQuickRecMp3Folder.Click += new EventHandler(this.btnQuickRecMp3Folder_Click);
            // 
            // txtNotes
            // 
            this.txtNotes.Location = new Point(12, 340);
            this.txtNotes.Multiline = true;
            this.txtNotes.Name = "txtNotes";
            this.txtNotes.ScrollBars = ScrollBars.Vertical;
            this.txtNotes.Size = new Size(457, 62);
            this.txtNotes.TabIndex = 67;
            // 
            // lblNote
            // 
            this.lblNote.AutoSize = false;
            this.lblNote.Location = new Point(12, 432);
            this.lblNote.Name = "lblNote";
            this.lblNote.Size = new Size(457, 32);
            this.lblNote.TabIndex = 68;
            this.lblNote.Text = "NOTE: In Receive, playback goes to the input of the receiver.\r\nIn Transmit, playback goes to the input of the transmitter.";
            // 
            // picLogo
            // 
            this.picLogo.Location = new Point(376, 210);
            this.picLogo.Name = "picLogo";
            this.picLogo.Size = new Size(80, 80);
            this.picLogo.SizeMode = PictureBoxSizeMode.Zoom;
            this.picLogo.TabIndex = 69;
            this.picLogo.TabStop = false;
            // 
            // WaveControl
            // 
            this.ClientSize = new Size(481, 472);
            this.Controls.Add(this.picLogo);
            this.Controls.Add(this.lblNote);
            this.Controls.Add(this.txtNotes);
            this.Controls.Add(this.grpPlaylist);
            this.Controls.Add(this.grpTxGain);
            this.Controls.Add(this.groupBox2);
            this.Controls.Add(this.grpPlayback);
            this.Controls.Add(this.btnQuickRecMp3Folder);
            this.Controls.Add(this.chkQuickRecMp3);
            this.Controls.Add(this.chkAlwaysOnTop);
            this.Controls.Add(this.menuStripOptions);
            this.chkAlwaysOnTop.BringToFront();
            this.chkQuickRecMp3.BringToFront();
            this.btnQuickRecMp3Folder.BringToFront();
            try
            {
                if (console != null && console.Icon != null) this.Icon = console.Icon;
                else this.Icon = ((Icon)(resources.GetObject("$this.Icon")));
            }
            catch { try { this.Icon = ((Icon)(resources.GetObject("$this.Icon"))); } catch { } }
            try { this.picLogo.Image = new Bitmap(this.Icon.ToBitmap(), new Size(80, 80)); } catch { }
            this.MainMenuStrip = this.menuStripOptions;
            this.Name = "WaveControl";
            this.Text = "Wave File Controls";
            this.grpPlaylist.ResumeLayout(false);
            this.groupBox2.ResumeLayout(false);
            this.groupBox2.PerformLayout();
            this.grpTxGain.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.udTxGain)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.tbTxGain)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.picLogo)).EndInit();
            this.grpPlayback.ResumeLayout(false);
            this.grpPlayback.PerformLayout();
            this.menuStripOptions.ResumeLayout(false);
            this.menuStripOptions.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private void WaveControl_FormClosing(object sender, FormClosingEventArgs e)
        {
            try { StopPlaybackInternal(); } catch { }
            try { StopRecordInternal(); } catch { }
            try { Common.SaveForm(this, "WaveOptions"); } catch { }
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
        }

        private int CurrentlyPlaying
        {
            get { return currently_playing; }
            set
            {
                if (lstPlaylist.Items.Count == 0)
                {
                    currently_playing = -1;
                }
                else
                {
                    if (value < 0) value = 0;
                    if (value > lstPlaylist.Items.Count - 1) value = lstPlaylist.Items.Count - 1;
                    currently_playing = value;
                }

                btnPrevious.Enabled = currently_playing > 0;
                btnNext.Enabled = currently_playing >= 0 && currently_playing < lstPlaylist.Items.Count - 1;

                if (!_isPlaying && currently_playing >= 0 && currently_playing < lstPlaylist.Items.Count)
                    lstPlaylist.SelectedIndex = currently_playing;
            }
        }

        private void UpdatePlaylist()
        {
            lstPlaylist.BeginUpdate();
            int prev = lstPlaylist.SelectedIndex;
            lstPlaylist.Items.Clear();
            foreach (string s in file_list)
            {
                lstPlaylist.Items.Add(Path.GetFileNameWithoutExtension(s));
            }
            if (prev >= 0 && prev < lstPlaylist.Items.Count) lstPlaylist.SelectedIndex = prev;
            else if (lstPlaylist.Items.Count > 0 && lstPlaylist.SelectedIndex < 0) lstPlaylist.SelectedIndex = 0;
            lstPlaylist.EndUpdate();

            bool has = lstPlaylist.Items.Count > 0;
            btnPlay.Enabled = has;
            btnRemove.Enabled = has;
            checkBoxLoop.Enabled = has;
            btnPrevious.Enabled = currently_playing > 0;
            btnNext.Enabled = currently_playing >= 0 && currently_playing < lstPlaylist.Items.Count - 1;
        }

        private void removeFromList(int index)
        {
            try
            {
                file_list.RemoveAt(index);
                if (currently_playing == index) currently_playing = -1;
                else if (index < currently_playing) currently_playing--;
            }
            catch { }
        }

        private void openFileDialog1_FileOk(object sender, CancelEventArgs e)
        {
            foreach (string s in openFileDialog1.FileNames)
            {
                if (!Path.GetExtension(s).Equals(".wav", StringComparison.OrdinalIgnoreCase)) continue;
                if (!file_list.Contains(s)) file_list.Add(s);
            }
            if (currently_playing < 0 && file_list.Count > 0) currently_playing = 0;
            UpdatePlaylist();
        }

        private void btnAdd_Click(object sender, EventArgs e)
        {
            openFileDialog1.InitialDirectory = wave_folder;
            openFileDialog1.ShowDialog(this);
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (lstPlaylist.Items.Count == 0 || lstPlaylist.SelectedIndices.Count == 0) return;
            ArrayList selections = new ArrayList();
            foreach (int i in lstPlaylist.SelectedIndices) selections.Add(i);
            selections.Sort();
            selections.Reverse();
            bool wasPlaying = _isPlaying;
            if (wasPlaying) StopPlaybackUI();
            foreach (int i in selections) removeFromList(i);
            UpdatePlaylist();
            if (wasPlaying && lstPlaylist.Items.Count > 0)
            {
                CurrentlyPlaying = 0;
                StartPlayback();
            }
            else if (lstPlaylist.Items.Count == 0)
            {
                currently_playing = -1;
            }
        }

        private void lstPlaylist_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (lstPlaylist.SelectedIndex < 0) return;
            if (!_isPlaying) CurrentlyPlaying = lstPlaylist.SelectedIndex;
        }

        private void btnPrevious_Click(object sender, EventArgs e)
        {
            if (_isPlaying)
            {
                StopPlaybackInternalOnly();
                CurrentlyPlaying--;
                StartPlayback();
            }
            else if (lstPlaylist.SelectedIndex > 0)
            {
                lstPlaylist.SelectedIndex--;
            }
        }

        private void btnNext_Click(object sender, EventArgs e)
        {
            if (_isPlaying)
            {
                StopPlaybackInternalOnly();
                if (CurrentlyPlaying == lstPlaylist.Items.Count - 1) CurrentlyPlaying = 0;
                else CurrentlyPlaying++;
                StartPlayback();
            }
            else if (lstPlaylist.SelectedIndex < lstPlaylist.Items.Count - 1)
            {
                lstPlaylist.SelectedIndex++;
            }
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            StopPlaybackUI();
        }

        private void checkBoxLoop_CheckedChanged(object sender, EventArgs e)
        {
            if (_restoring_controls) return;
            checkBoxLoop.BackColor = checkBoxLoop.Checked ? console.ButtonSelectedColor : SystemColors.Control;
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (_restoring_controls || !_isPlaying) return;
            _isPaused = !_isPaused;
            try { Audio.WavePlayback = !_isPaused && _isPlaying; } catch { }
            try { btnPause.BackColor = _isPaused ? console.ButtonSelectedColor : SystemColors.Control; } catch { }
        }

        private string BitDepthLabel()
        {
            try
            {
                switch (console.ARP.BitDepthMode)
                {
                    case AudioBitDepthMode.Pcm8: return "8";
                    case AudioBitDepthMode.Pcm16: return "16";
                    case AudioBitDepthMode.Pcm24: return "24";
                    case AudioBitDepthMode.Pcm32: return "32";
                    case AudioBitDepthMode.IeeeFloat32: return "32";
                }
            }
            catch { }
            return "32";
        }

        private string MakeBaseName(string mode, double freqMhz, DateTime dt)
        {
            string temp = mode + " ";
            temp += freqMhz.ToString("f6", CultureInfo.InvariantCulture) + "MHz [";
            int short_sample_rate = 48;
            try { short_sample_rate = console.ARP.SampleRate / 1000; } catch { }
            temp += BitDepthLabel() + "bit ";
            temp += short_sample_rate.ToString(CultureInfo.InvariantCulture) + "k] ";
            temp += dt.ToString(CultureInfo.CurrentCulture);
            temp = temp.Replace("/", "-");
            temp = temp.Replace(":", " ");
            return temp;
        }

        private void checkBoxRecord_CheckedChanged(object sender, EventArgs e)
        {
            if (_restoring_controls) return;

            if (checkBoxRecord.Checked)
            {
                try
                {
                    _record_wfw_id = radRecRX2.Checked ? 1 : 0;
                    if (_record_wfw_id == 1 && !console.RX2Enabled)
                    {
                        MessageBox.Show("RX2 is not enabled.", "Wave Record", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        setRecordChecked(false);
                        return;
                    }

                    // avoid conflicts with ARP/other wave ops
                    try { console.ARP.StopPlayback(out _); } catch { }
                    try { console.ARP.StopRecord(out _); } catch { }
                    try { StopPlaybackInternal(); } catch { }

                    DateTime now = DateTime.Now;
                    string modeStr = _record_wfw_id == 1 ? console.RX2DSPMode.ToString() : console.RX1DSPMode.ToString();
                    double freq = _record_wfw_id == 1 ? console.VFOBFreq : console.VFOAFreq;
                    string baseName = MakeBaseName(modeStr, freq, now);
                    string file_name = Path.Combine(wave_folder, baseName + ".wav");

                    try { if (!Directory.Exists(wave_folder)) Directory.CreateDirectory(wave_folder); } catch { }

                    if (!Common.CanCreateFile(file_name))
                    {
                        MessageBox.Show($"Unable to open the file : \n\n{file_name}\n\nThis may be due to controlled folder access or some other reason.",
                            "Wave Record", MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, Common.MB_TOPMOST);
                        setRecordChecked(false);
                        return;
                    }

                    bool rxPre = false;
                    bool txPre = true;
                    short formatTag = 3;
                    short bitDepth = 32;
                    int sampleRate = 48000;
                    try
                    {
                        rxPre = console.ARP.RxSource == AudioRecordRxSource.ReceiverInputIQ;
                        txPre = console.ARP.TxSource == AudioRecordTxSource.MicAudio;
                        formatTag = console.ARP.BitDepthMode == AudioBitDepthMode.IeeeFloat32 ? (short)3 : (short)1;
                        switch (console.ARP.BitDepthMode)
                        {
                            case AudioBitDepthMode.Pcm8: bitDepth = 8; break;
                            case AudioBitDepthMode.Pcm16: bitDepth = 16; break;
                            case AudioBitDepthMode.Pcm24: bitDepth = 24; break;
                            default: bitDepth = 32; break;
                        }
                        sampleRate = console.ARP.SampleRate;
                    }
                    catch { }

                    // ensure slot clear
                    try { if (WaveThing.wave_file_writer[_record_wfw_id] != null) { WaveThing.wave_file_writer[_record_wfw_id].Stop(); WaveThing.wave_file_writer[_record_wfw_id] = null; } } catch { }

                    WaveThing.wave_file_writer[_record_wfw_id] = new WaveFileWriter(_record_wfw_id, 2, sampleRate, file_name, rxPre, txPre, formatTag, bitDepth, null);
                    try
                    {
                        WaveThing.wave_file_writer[_record_wfw_id].DitherEnabled = console.ARP.DitherEnabled;
                        WaveThing.wave_file_writer[_record_wfw_id].DitherAmount = console.ARP.DitherAmount;
                    }
                    catch { }

                    Audio.WaveRecord = true;
                    radRecRX1.Enabled = false;
                    radRecRX2.Enabled = false;
                    checkBoxRecord.BackColor = console.ButtonSelectedColor;
                }
                catch (Exception ex)
                {
                    try { if (WaveThing.wave_file_writer[0] != null) { WaveThing.wave_file_writer[0].Stop(); WaveThing.wave_file_writer[0] = null; } } catch { }
                    try { if (WaveThing.wave_file_writer[1] != null) { WaveThing.wave_file_writer[1].Stop(); WaveThing.wave_file_writer[1] = null; } } catch { }
                    try { Audio.WaveRecord = false; } catch { }
                    radRecRX1.Enabled = true;
                    radRecRX2.Enabled = true;
                    setRecordChecked(false);
                    MessageBox.Show(ex.Message, "Wave Record", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                StopRecordInternal();
                radRecRX1.Enabled = true;
                radRecRX2.Enabled = true;
                checkBoxRecord.BackColor = SystemColors.Control;
            }

            if (_old_record_state != checkBoxRecord.Checked)
            {
                _old_record_state = checkBoxRecord.Checked;
                try { console.WaveRecordChangedHandlers?.Invoke(_record_wfw_id + 1, _old_record_state, checkBoxRecord.Checked); } catch { }
            }
        }

        private void setRecordChecked(bool val)
        {
            bool was = _restoring_controls;
            _restoring_controls = true;
            checkBoxRecord.Checked = val;
            _restoring_controls = was;
            if (!val) checkBoxRecord.BackColor = SystemColors.Control;
        }

        private void StopRecordInternal()
        {
            try
            {
                // stop any active writer(s)
                for (int i = 0; i < 2; i++)
                {
                    try
                    {
                        if (WaveThing.wave_file_writer[i] != null)
                        {
                            WaveThing.wave_file_writer[i].Stop();
                            WaveThing.wave_file_writer[i] = null;
                        }
                    }
                    catch { }
                }
            }
            catch { }
            try { Audio.WaveRecord = false; } catch { }
        }

        private bool OpenWaveFile(string filename, int id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filename) || !File.Exists(filename)) return false;

                // stop existing reader in this slot
                try { if (WaveThing.wave_file_reader[id] != null) { WaveThing.wave_file_reader[id].Stop(); } } catch { }

                BinaryReader reader = null;
                try
                {
                    reader = new BinaryReader(File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read));
                }
                catch { return false; }

                if (!tryParseWaveHeader(reader, out int formatTag, out int sampleRate, out int channels, out int bitsPerSample, out long dataStart, out long dataLengthBytes))
                {
                    try { reader.Close(); } catch { }
                    return false;
                }

                try { reader.BaseStream.Seek(dataStart, SeekOrigin.Begin); } catch { try { reader.Close(); } catch { } return false; }

                bool fadeEnabled = clsAudioRecordPlayback.PlaybackCosineFadeEnabled;
                int fadeMs = clsAudioRecordPlayback.PlaybackCosineFadeMs;
                double monoGainDb = 6.0;
                try
                {
                    monoGainDb = console.ARP.MonoToStereoGainDb;
                }
                catch { }

                int rid = id;
                WaveThing.wave_file_reader[id] = new WaveFileReader1(
                    id, formatTag, sampleRate, channels, bitsPerSample, dataLengthBytes,
                    fadeEnabled, fadeMs, monoGainDb, reader,
                    (ex) => OnPlaybackFinished(rid, ex));

                return true;
            }
            catch { return false; }
        }

        private void OnPlaybackFinished(int id, Exception ex)
        {
            if (id != 0) return; // advance UI based on primary stream
            try
            {
                if (IsDisposed) return;
                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed) return;
                    if (!_isPlaying) return;
                    if (_advancing) return;
                    _advancing = true;
                    try { Next(autoAdvance: true); }
                    finally { _advancing = false; }
                }));
            }
            catch { }
        }

        private static bool tryParseWaveHeader(BinaryReader br, out int formatTag, out int sampleRate, out int channels, out int bitsPerSample, out long dataStart, out long dataLengthBytes)
        {
            formatTag = 0; sampleRate = 0; channels = 0; bitsPerSample = 0; dataStart = 0; dataLengthBytes = 0;
            try
            {
                if (br == null || br.BaseStream.Length < 12) return false;
                br.BaseStream.Seek(0, SeekOrigin.Begin);
                int riff = br.ReadInt32();
                br.ReadInt32(); // file size
                int wave = br.ReadInt32();
                if (riff != 0x46464952 || wave != 0x45564157) return false;

                bool fmtFound = false, dataFound = false;
                while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
                {
                    int chunkId = br.ReadInt32();
                    int chunkSize = br.ReadInt32();
                    long chunkDataPos = br.BaseStream.Position;
                    long next = chunkDataPos + chunkSize + (chunkSize & 1);

                    if (chunkId == 0x20746d66) // fmt 
                    {
                        formatTag = br.ReadInt16();
                        channels = br.ReadInt16();
                        sampleRate = br.ReadInt32();
                        br.ReadInt32();
                        br.ReadInt16();
                        bitsPerSample = br.ReadInt16();
                        fmtFound = true;
                    }
                    else if (chunkId == 0x61746164) // data
                    {
                        dataStart = chunkDataPos;
                        dataLengthBytes = chunkSize;
                        dataFound = true;
                        break;
                    }

                    if (next <= chunkDataPos || next > br.BaseStream.Length) break;
                    br.BaseStream.Seek(next, SeekOrigin.Begin);
                }

                return fmtFound && dataFound && sampleRate > 0 && channels > 0 && dataLengthBytes > 0;
            }
            catch { return false; }
        }

        private void btnPlay_Click(object sender, EventArgs e)
        {
            if (_restoring_controls || _isPlaying) return;
            StartPlayback();
        }

        private void StartPlayback()
        {
            if (currently_playing < 0 || currently_playing >= file_list.Count)
            {
                StopPlaybackUI();
                return;
            }

            // avoid conflicts
            try { console.ARP.StopPlayback(out _); } catch { }
            try { console.ARP.StopRecord(out _); } catch { }
            try { if (checkBoxRecord.Checked) { _restoring_controls = true; checkBoxRecord.Checked = false; _restoring_controls = false; StopRecordInternal(); checkBoxRecord.BackColor = SystemColors.Control; radRecRX1.Enabled = true; radRecRX2.Enabled = true; } } catch { }

            string filename = (string)file_list[currently_playing];
            if (!OpenWaveFile(filename, 0))
            {
                StopPlaybackUI();
                MessageBox.Show("Unable to open the selected WAV file.", "Wave Playback", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // paired rx2 sidecar support if present
            try
            {
                if (console.RX2Enabled)
                {
                    string dir = Path.GetDirectoryName(filename) ?? "";
                    string baseNoExt = Path.Combine(dir, Path.GetFileNameWithoutExtension(filename));
                    string f2a = baseNoExt + "-rx2.wav";
                    string f2b = filename + "-rx2";
                    string f2 = File.Exists(f2a) ? f2a : (File.Exists(f2b) ? f2b : null);
                    if (f2 != null) OpenWaveFile(f2, 1);
                }
            }
            catch { }

            txtCurrentFile.Text = Path.GetFileNameWithoutExtension(filename);
            _isPlaying = true;
            _isPaused = false;
            try { btnPlay.BackColor = console.ButtonSelectedColor; } catch { }
            try { btnPause.BackColor = SystemColors.Control; btnPause.Text = "Pause"; } catch { }
            btnPause.Enabled = true;
            btnStop.Enabled = true;

            if (console.BypassVACWhenPlayingWAV)
            {
                try { temp_vacbypass_play = Audio.VACBypass; Audio.VACBypass = true; } catch { }
            }

            try { Audio.WavePlayback = true; } catch { }
            try { console.SetWavePlayback(0, true); } catch { }
            try { if (WaveThing.wave_file_reader[1] != null) console.SetWavePlayback(1, true); } catch { }
        }

        private void StopPlaybackUI()
        {
            StopPlaybackInternalOnly();
            if (console.BypassVACWhenPlayingWAV)
            {
                try { Audio.VACBypass = temp_vacbypass_play; } catch { }
            }
        }

        private void StopPlaybackInternalOnly(bool clearCurrentFile = true)
        {
            StopPlaybackInternal();
            _isPlaying = false;
            _isPaused = false;
            btnPause.Enabled = false;
            btnStop.Enabled = false;
            if (clearCurrentFile) txtCurrentFile.Text = "";
            try { btnPlay.BackColor = SystemColors.Control; } catch { }
            try { btnPause.BackColor = SystemColors.Control; btnPause.Text = "Pause"; } catch { }
        }

        private void StopPlaybackInternal()
        {
            try
            {
                for (int i = 0; i < 2; i++)
                {
                    try { if (WaveThing.wave_file_reader[i] != null) WaveThing.wave_file_reader[i].Stop(); } catch { }
                }
            }
            catch { }
            try { Audio.WavePlayback = false; } catch { }
            try { console.SetWavePlayback(0, false); } catch { }
            try { console.SetWavePlayback(1, false); } catch { }
        }

        public void Next(bool autoAdvance = false)
        {
            if (!_isPlaying && !autoAdvance) return;

            if (autoAdvance)
            {
                StopPlaybackInternalOnly();
                if (checkBoxLoop.Checked && lstPlaylist.Items.Count >= 1)
                {
                    if (CurrentlyPlaying >= lstPlaylist.Items.Count - 1) CurrentlyPlaying = 0; else CurrentlyPlaying++;
                    StartPlayback();
                }
                else if (btnNext.Enabled)
                {
                    CurrentlyPlaying++;
                    StartPlayback();
                }
                else
                {
                    StopPlaybackUI();
                }
                return;
            }

            if (btnNext.Enabled) btnNext_Click(this, EventArgs.Empty);
            else if (checkBoxLoop.Checked && lstPlaylist.Items.Count >= 1)
            {
                StopPlaybackInternalOnly();
                CurrentlyPlaying = 0;
                StartPlayback();
            }
            else
            {
                StopPlaybackUI();
            }
        }

        private void udTxGain_ValueChanged(object sender, EventArgs e)
        {
            if (_restoring_controls) return;
            try
            {
                int v = (int)udTxGain.Value;
                if (tbTxGain != null)
                {
                    int tv = Math.Max(tbTxGain.Minimum, Math.Min(tbTxGain.Maximum, v));
                    if (tbTxGain.Value != tv) tbTxGain.Value = tv;
                }
                if (console != null && console.ARP != null)
                    console.ARP.WDSPTXAudioGainInDb = (double)udTxGain.Value;
            }
            catch { }
        }

        private void tbTxGain_Scroll(object sender, EventArgs e)
        {
            if (_restoring_controls) return;
            try { udTxGain.Value = tbTxGain.Value; } catch { }
        }

        private void optionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (waveOptionsForm == null || waveOptionsForm.IsDisposed)
                waveOptionsForm = new WaveOptions(console);
            try { waveOptionsForm.Show(this); } catch { waveOptionsForm.Show(); }
            waveOptionsForm.Focus();
        }

        private void chkAlwaysOnTop_CheckedChanged(object sender, EventArgs e)
        {
            this.TopMost = chkAlwaysOnTop.Checked;
        }

        private void chkQuickRecMp3_CheckedChanged(object sender, EventArgs e)
        {
            if (_restoring_controls) return;
            try { if (console != null) console.QuickRecGenerateMp3 = chkQuickRecMp3.Checked; } catch { }
        }

        private void btnQuickRecMp3Folder_Click(object sender, EventArgs e)
        {
            try
            {
                string folder = console != null && console.ARP != null
                    ? Path.Combine(console.ARP.AudioFolder, "quickrecord")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Thetis", "quickrecord");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", folder);
            }
            catch { }
        }
    }
}
