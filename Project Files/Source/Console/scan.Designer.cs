//=================================================================
// scan.cs
// created by Darrin Kohn ke9ns
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 2
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
//
//
//=================================================================


namespace Thetis
{
    public partial class ScanControl : System.Windows.Forms.Form
    {

        private System.Windows.Forms.GroupBoxTS grpPlayback;
        private System.Windows.Forms.GroupBox grpPlaylist;
        private System.Windows.Forms.MainMenu mainMenu1;
        private System.Windows.Forms.Button button1;
        private System.Windows.Forms.Button btnCustomList;
        private System.Windows.Forms.Button btnBandstack;
        private System.Windows.Forms.Button btnGroupMemory;
        public System.Windows.Forms.TextBox lowFBox;
        private System.Windows.Forms.TextBox textBox3;
        private System.Windows.Forms.Label label2;
        public System.Windows.Forms.TextBox highFBox;
        private System.Windows.Forms.Label label4;
        private System.Windows.Forms.Label label5;
        public System.Windows.Forms.CheckBoxTS chkAlwaysOnTop;
        private System.Windows.Forms.GroupBoxTS grpGenCustomTitleText;
        private System.Windows.Forms.Label label6;
        private System.Windows.Forms.CheckBoxTS chkBoxSQLBRK;
        private System.Windows.Forms.ComboBoxTS comboMemGroupName;
        public System.Windows.Forms.DataGridView dataGridView2;
        public System.Windows.Forms.TextBox currFBox;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label label7;
        private System.Windows.Forms.Label label8;
        private System.Windows.Forms.Label label9;
        private System.Windows.Forms.ComboBoxTS comboBoxTS1;
        private System.Windows.Forms.Button pausebtn;
        private System.Windows.Forms.NumericUpDownTS udspeedBox;
        private System.Windows.Forms.NumericUpDownTS udspeedBox1;
        private System.Windows.Forms.Label label10;
        private System.Windows.Forms.NumericUpDownTS udstepBox;
        private System.Windows.Forms.OpenFileDialog openFileDialog2;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.NumericUpDownTS udPauseLength;
        private System.Windows.Forms.Label label11;
        private System.Windows.Forms.Label label12;
        private System.Windows.Forms.GroupBoxTS groupBoxTS2;
        private System.Windows.Forms.LabelTS labelTS27;
        private System.Windows.Forms.LabelTS labelTS23;
        public System.Windows.Forms.CheckBoxTS chkBoxIdent;
        private System.Windows.Forms.Label label13;
        public System.Windows.Forms.NumericUpDownTS udIDTimer;
        public System.Windows.Forms.NumericUpDownTS udIDGap;
        public System.Windows.Forms.NumericUpDownTS udIDThres;
        private System.Windows.Forms.Label label14;
        private System.Windows.Forms.CheckBoxTS chkBoxLoop;
        private System.Windows.Forms.CheckBoxTS chkBoxSQLBRKWait;
        private System.Windows.Forms.Button button_reset;


        #region Windows Form Designer generated code
        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(ScanControl));
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle7 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle8 = new System.Windows.Forms.DataGridViewCellStyle();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle9 = new System.Windows.Forms.DataGridViewCellStyle();
            this.textBox3 = new System.Windows.Forms.TextBox();
            this.dataGridView2 = new System.Windows.Forms.DataGridView();
            this.currFBox = new System.Windows.Forms.TextBox();
            this.label3 = new System.Windows.Forms.Label();
            this.label7 = new System.Windows.Forms.Label();
            this.label8 = new System.Windows.Forms.Label();
            this.label9 = new System.Windows.Forms.Label();
            this.pausebtn = new System.Windows.Forms.Button();
            this.openFileDialog2 = new System.Windows.Forms.OpenFileDialog();
            this.toolTip1 = new System.Windows.Forms.ToolTip(this.components);
            this.udIDGap = new System.Windows.Forms.NumericUpDownTS();
            this.udIDThres = new System.Windows.Forms.NumericUpDownTS();
            this.udIDTimer = new System.Windows.Forms.NumericUpDownTS();
            this.chkBoxIdent = new System.Windows.Forms.CheckBoxTS();
            this.udPauseLength = new System.Windows.Forms.NumericUpDownTS();
            this.chkBoxSQLBRK = new System.Windows.Forms.CheckBoxTS();
            this.button_reset = new System.Windows.Forms.Button();
            this.chkBoxLoop = new System.Windows.Forms.CheckBoxTS();
            this.btnGroupMemory = new System.Windows.Forms.Button();
            this.btnCustomList = new System.Windows.Forms.Button();
            this.btnBandstack = new System.Windows.Forms.Button();
            this.button1 = new System.Windows.Forms.Button();
            this.chkBoxSQLBRKWait = new System.Windows.Forms.CheckBoxTS();
            this.chkIDdBM = new System.Windows.Forms.CheckBoxTS();
            this.chkIDSIG = new System.Windows.Forms.CheckBoxTS();
            this.label11 = new System.Windows.Forms.Label();
            this.label12 = new System.Windows.Forms.Label();
            this.groupBoxTS2 = new System.Windows.Forms.GroupBoxTS();
            this.chkAlwaysOnTop = new System.Windows.Forms.CheckBoxTS();
            this.label13 = new System.Windows.Forms.Label();
            this.labelTS27 = new System.Windows.Forms.LabelTS();
            this.labelTS23 = new System.Windows.Forms.LabelTS();
            this.comboMemGroupName = new System.Windows.Forms.ComboBoxTS();
            this.grpGenCustomTitleText = new System.Windows.Forms.GroupBoxTS();
            this.label14 = new System.Windows.Forms.Label();
            this.udspeedBox = new System.Windows.Forms.NumericUpDownTS();
            this.udstepBox = new System.Windows.Forms.NumericUpDownTS();
            this.udspeedBox1 = new System.Windows.Forms.NumericUpDownTS();
            this.comboBoxTS1 = new System.Windows.Forms.ComboBoxTS();
            this.label6 = new System.Windows.Forms.Label();
            this.label4 = new System.Windows.Forms.Label();
            this.highFBox = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label5 = new System.Windows.Forms.Label();
            this.lowFBox = new System.Windows.Forms.TextBox();
            this.label10 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView2)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.udIDGap)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.udIDThres)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.udIDTimer)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.udPauseLength)).BeginInit();
            this.groupBoxTS2.SuspendLayout();
            this.grpGenCustomTitleText.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.udspeedBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.udstepBox)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.udspeedBox1)).BeginInit();
            this.SuspendLayout();
            // 
            // textBox3
            // 
            this.textBox3.Font = new System.Drawing.Font("Microsoft Sans Serif", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.textBox3.Location = new System.Drawing.Point(18, 10);
            this.textBox3.Multiline = true;
            this.textBox3.Name = "textBox3";
            this.textBox3.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.textBox3.Size = new System.Drawing.Size(608, 104);
            this.textBox3.TabIndex = 9;
            this.textBox3.TabStop = false;
            this.textBox3.Text = resources.GetString("textBox3.Text");
            // 
            // dataGridView2
            // 
            this.dataGridView2.AllowDrop = true;
            this.dataGridView2.AllowUserToAddRows = false;
            this.dataGridView2.AllowUserToDeleteRows = false;
            this.dataGridView2.AllowUserToResizeColumns = false;
            this.dataGridView2.AllowUserToResizeRows = false;
            this.dataGridView2.BorderStyle = System.Windows.Forms.BorderStyle.Fixed3D;
            dataGridViewCellStyle7.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle7.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle7.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            dataGridViewCellStyle7.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle7.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle7.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle7.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dataGridView2.ColumnHeadersDefaultCellStyle = dataGridViewCellStyle7;
            this.dataGridView2.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataGridViewCellStyle8.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle8.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle8.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            dataGridViewCellStyle8.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle8.SelectionBackColor = System.Drawing.Color.OliveDrab;
            dataGridViewCellStyle8.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle8.WrapMode = System.Windows.Forms.DataGridViewTriState.False;
            this.dataGridView2.DefaultCellStyle = dataGridViewCellStyle8;
            this.dataGridView2.Location = new System.Drawing.Point(385, 153);
            this.dataGridView2.MultiSelect = false;
            this.dataGridView2.Name = "dataGridView2";
            dataGridViewCellStyle9.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle9.BackColor = System.Drawing.SystemColors.Control;
            dataGridViewCellStyle9.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            dataGridViewCellStyle9.ForeColor = System.Drawing.SystemColors.WindowText;
            dataGridViewCellStyle9.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle9.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle9.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dataGridView2.RowHeadersDefaultCellStyle = dataGridViewCellStyle9;
            this.dataGridView2.Size = new System.Drawing.Size(254, 94);
            this.dataGridView2.TabIndex = 76;
            this.dataGridView2.Visible = false;
            // 
            // currFBox
            // 
            this.currFBox.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.currFBox.BackColor = System.Drawing.Color.LightYellow;
            this.currFBox.Cursor = System.Windows.Forms.Cursors.Default;
            this.currFBox.Font = new System.Drawing.Font("Courier New", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.currFBox.HideSelection = false;
            this.currFBox.Location = new System.Drawing.Point(12, 133);
            this.currFBox.MaximumSize = new System.Drawing.Size(1000, 1000);
            this.currFBox.MaxLength = 10000000;
            this.currFBox.MinimumSize = new System.Drawing.Size(300, 100);
            this.currFBox.Multiline = true;
            this.currFBox.Name = "currFBox";
            this.currFBox.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.currFBox.Size = new System.Drawing.Size(620, 155);
            this.currFBox.TabIndex = 77;
            this.currFBox.TabStop = false;
            this.currFBox.MouseDown += new System.Windows.Forms.MouseEventHandler(this.currFBox_MouseDown);
            this.currFBox.MouseUp += new System.Windows.Forms.MouseEventHandler(this.currFBox_MouseUp);
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.label3.Location = new System.Drawing.Point(9, 117);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(33, 13);
            this.label3.TabIndex = 78;
            this.label3.Text = "Index";
            // 
            // label7
            // 
            this.label7.AutoSize = true;
            this.label7.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.label7.Location = new System.Drawing.Point(220, 117);
            this.label7.Name = "label7";
            this.label7.Size = new System.Drawing.Size(57, 13);
            this.label7.TabIndex = 79;
            this.label7.Text = "Frequency";
            // 
            // label8
            // 
            this.label8.AutoSize = true;
            this.label8.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.label8.Location = new System.Drawing.Point(352, 117);
            this.label8.Name = "label8";
            this.label8.Size = new System.Drawing.Size(35, 13);
            this.label8.TabIndex = 80;
            this.label8.Text = "Name";
            // 
            // label9
            // 
            this.label9.AutoSize = true;
            this.label9.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.label9.Location = new System.Drawing.Point(448, 117);
            this.label9.Name = "label9";
            this.label9.Size = new System.Drawing.Size(129, 13);
            this.label9.TabIndex = 81;
            this.label9.Text = "Squelch, Signal, Remarks";
            // 
            // pausebtn
            // 
            this.pausebtn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.pausebtn.BackColor = System.Drawing.SystemColors.ControlLight;
            this.pausebtn.Location = new System.Drawing.Point(18, 546);
            this.pausebtn.Name = "pausebtn";
            this.pausebtn.Size = new System.Drawing.Size(81, 23);
            this.pausebtn.TabIndex = 66;
            this.pausebtn.Text = "Pause Scan";
            this.toolTip1.SetToolTip(this.pausebtn, "Turns Yellow if \"Wait on Squelch Break\" ON or \"Pause on Squelch Break\" and squelc" +
        "h Breaks.\r\n\r\nClick the Yellow \"Pause Scan\" button to jump past an occupied chann" +
        "el and continue scanning.\r\n\r\n");
            this.pausebtn.UseVisualStyleBackColor = false;
            this.pausebtn.Click += new System.EventHandler(this.pausebtn_Click);
            // 
            // openFileDialog2
            // 
            this.openFileDialog2.FileName = "openFileDialog2";
            // 
            // toolTip1
            // 
            this.toolTip1.AutomaticDelay = 200;
            this.toolTip1.AutoPopDelay = 12000;
            this.toolTip1.InitialDelay = 500;
            this.toolTip1.ReshowDelay = 40;
            // 
            // udIDGap
            // 
            this.udIDGap.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.udIDGap.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.udIDGap.Increment = new decimal(new int[] {
            2,
            0,
            0,
            0});
            this.udIDGap.Location = new System.Drawing.Point(124, 64);
            this.udIDGap.Maximum = new decimal(new int[] {
            999,
            0,
            0,
            0});
            this.udIDGap.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.udIDGap.Name = "udIDGap";
            this.udIDGap.Size = new System.Drawing.Size(55, 22);
            this.udIDGap.TabIndex = 97;
            this.toolTip1.SetToolTip(this.udIDGap, resources.GetString("udIDGap.ToolTip"));
            this.udIDGap.Value = new decimal(new int[] {
            6,
            0,
            0,
            0});
            // 
            // udIDThres
            // 
            this.udIDThres.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.udIDThres.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.udIDThres.Increment = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.udIDThres.Location = new System.Drawing.Point(133, 36);
            this.udIDThres.Maximum = new decimal(new int[] {
            50,
            0,
            0,
            0});
            this.udIDThres.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.udIDThres.Name = "udIDThres";
            this.udIDThres.Size = new System.Drawing.Size(45, 22);
            this.udIDThres.TabIndex = 96;
            this.toolTip1.SetToolTip(this.udIDThres, "Sets Threshold to detect signals above the noise floor.");
            this.udIDThres.Value = new decimal(new int[] {
            6,
            0,
            0,
            0});
            // 
            // udIDTimer
            // 
            this.udIDTimer.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.udIDTimer.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.udIDTimer.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.udIDTimer.Location = new System.Drawing.Point(357, 36);
            this.udIDTimer.Maximum = new decimal(new int[] {
            200,
            0,
            0,
            0});
            this.udIDTimer.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.udIDTimer.Name = "udIDTimer";
            this.udIDTimer.Size = new System.Drawing.Size(53, 22);
            this.udIDTimer.TabIndex = 95;
            this.toolTip1.SetToolTip(this.udIDTimer, "How long to keep the detected peak signal on the screen.\r\nBased on how fast your " +
        "refreshing your Display.");
            this.udIDTimer.Value = new decimal(new int[] {
            30,
            0,
            0,
            0});
            // 
            // chkBoxIdent
            // 
            this.chkBoxIdent.Image = null;
            this.chkBoxIdent.Location = new System.Drawing.Point(6, 19);
            this.chkBoxIdent.Name = "chkBoxIdent";
            this.chkBoxIdent.Size = new System.Drawing.Size(58, 19);
            this.chkBoxIdent.TabIndex = 89;
            this.chkBoxIdent.Text = "ON";
            this.toolTip1.SetToolTip(this.chkBoxIdent, resources.GetString("chkBoxIdent.ToolTip"));
            this.chkBoxIdent.CheckedChanged += new System.EventHandler(this.chkBoxIdent_CheckedChanged);
            // 
            // udPauseLength
            // 
            this.udPauseLength.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.udPauseLength.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.udPauseLength.Increment = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.udPauseLength.Location = new System.Drawing.Point(118, 546);
            this.udPauseLength.Maximum = new decimal(new int[] {
            9999,
            0,
            0,
            0});
            this.udPauseLength.Minimum = new decimal(new int[] {
            0,
            0,
            0,
            0});
            this.udPauseLength.Name = "udPauseLength";
            this.udPauseLength.Size = new System.Drawing.Size(63, 22);
            this.udPauseLength.TabIndex = 92;
            this.toolTip1.SetToolTip(this.udPauseLength, "Set the Pause on Squelch Break Time (in Seconds).\r\n\r\n0=OFF (pause forever)");
            this.udPauseLength.Value = new decimal(new int[] {
            10,
            0,
            0,
            0});
            // 
            // chkBoxSQLBRK
            // 
            this.chkBoxSQLBRK.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.chkBoxSQLBRK.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.chkBoxSQLBRK.Image = null;
            this.chkBoxSQLBRK.Location = new System.Drawing.Point(112, 487);
            this.chkBoxSQLBRK.Name = "chkBoxSQLBRK";
            this.chkBoxSQLBRK.Size = new System.Drawing.Size(104, 31);
            this.chkBoxSQLBRK.TabIndex = 62;
            this.chkBoxSQLBRK.Text = "Pause on Squelch Break";
            this.toolTip1.SetToolTip(this.chkBoxSQLBRK, resources.GetString("chkBoxSQLBRK.ToolTip"));
            this.chkBoxSQLBRK.CheckedChanged += new System.EventHandler(this.chkBoxSQLBRK_CheckedChanged);
            // 
            // button_reset
            // 
            this.button_reset.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.button_reset.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.button_reset.Location = new System.Drawing.Point(14, 103);
            this.button_reset.Name = "button_reset";
            this.button_reset.Size = new System.Drawing.Size(80, 23);
            this.button_reset.TabIndex = 106;
            this.button_reset.Text = "Edge RST";
            this.toolTip1.SetToolTip(this.button_reset, "Click to Reset Low-High Band Edges");
            this.button_reset.UseVisualStyleBackColor = true;
            this.button_reset.Click += new System.EventHandler(this.button_reset_Click);
            // 
            // chkBoxLoop
            // 
            this.chkBoxLoop.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.chkBoxLoop.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.chkBoxLoop.Image = null;
            this.chkBoxLoop.Location = new System.Drawing.Point(175, 101);
            this.chkBoxLoop.Name = "chkBoxLoop";
            this.chkBoxLoop.Size = new System.Drawing.Size(60, 26);
            this.chkBoxLoop.TabIndex = 95;
            this.chkBoxLoop.Text = "Loop";
            this.toolTip1.SetToolTip(this.chkBoxLoop, "Frequency Lo-Hi Scan will Loop forever (instead of 1 time through)");
            // 
            // btnGroupMemory
            // 
            this.btnGroupMemory.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnGroupMemory.Enabled = false;
            this.btnGroupMemory.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.btnGroupMemory.Location = new System.Drawing.Point(14, 30);
            this.btnGroupMemory.Name = "btnGroupMemory";
            this.btnGroupMemory.Size = new System.Drawing.Size(115, 23);
            this.btnGroupMemory.TabIndex = 5;
            this.btnGroupMemory.Text = "Memory Scan (RX)";
            this.toolTip1.SetToolTip(this.btnGroupMemory, resources.GetString("btnGroupMemory.ToolTip"));
            this.btnGroupMemory.UseVisualStyleBackColor = true;
            this.btnGroupMemory.Click += new System.EventHandler(this.btnGroupMemory_Click);
            this.btnGroupMemory.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnGroupMemory_MouseDown);
            // 
            // btnCustomList
            // 
            this.btnCustomList.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnCustomList.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.btnCustomList.Location = new System.Drawing.Point(496, 58);
            this.btnCustomList.Name = "btnCustomList";
            this.btnCustomList.Size = new System.Drawing.Size(108, 23);
            this.btnCustomList.TabIndex = 3;
            this.btnCustomList.Text = "Cstm List Start";
            this.toolTip1.SetToolTip(this.btnCustomList, resources.GetString("btnCustomList.ToolTip"));
            this.btnCustomList.UseVisualStyleBackColor = true;
            this.btnCustomList.Click += new System.EventHandler(this.btnCustomList_Click);
            // 
            // btnBandstack
            // 
            this.btnBandstack.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.btnBandstack.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.btnBandstack.Location = new System.Drawing.Point(496, 30);
            this.btnBandstack.Name = "btnBandstack";
            this.btnBandstack.Size = new System.Drawing.Size(108, 23);
            this.btnBandstack.TabIndex = 4;
            this.btnBandstack.Text = "BandStack Start";
            this.toolTip1.SetToolTip(this.btnBandstack, "Click to start scanning your current BandStack.");
            this.btnBandstack.UseVisualStyleBackColor = true;
            this.btnBandstack.Click += new System.EventHandler(this.btnBandstack_Click);
            // 
            // button1
            // 
            this.button1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.button1.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.button1.Location = new System.Drawing.Point(115, 103);
            this.button1.Name = "button1";
            this.button1.Size = new System.Drawing.Size(54, 48);
            this.button1.TabIndex = 0;
            this.button1.Text = "Low-Hi Scan (RX)";
            this.toolTip1.SetToolTip(this.button1, "Click here to start scanning from Low Freq to High Freq.\r\n\r\nYou can manually chan" +
        "ge the Low and High Freq Edges.\r\n\r\nLoop checkbox for continous Scan");
            this.button1.UseVisualStyleBackColor = true;
            this.button1.Click += new System.EventHandler(this.button5_Click);
            // 
            // chkBoxSQLBRKWait
            // 
            this.chkBoxSQLBRKWait.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.chkBoxSQLBRKWait.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.chkBoxSQLBRKWait.Image = null;
            this.chkBoxSQLBRKWait.Location = new System.Drawing.Point(12, 487);
            this.chkBoxSQLBRKWait.Name = "chkBoxSQLBRKWait";
            this.chkBoxSQLBRKWait.Size = new System.Drawing.Size(104, 31);
            this.chkBoxSQLBRKWait.TabIndex = 95;
            this.chkBoxSQLBRKWait.Text = "Wait on Squelch Break";
            this.toolTip1.SetToolTip(this.chkBoxSQLBRKWait, resources.GetString("chkBoxSQLBRKWait.ToolTip"));
            this.chkBoxSQLBRKWait.CheckedChanged += new System.EventHandler(this.chkBoxSQLBRKWait_CheckedChanged);
            // 
            // chkIDdBM
            // 
            this.chkIDdBM.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.chkIDdBM.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.chkIDdBM.Image = null;
            this.chkIDdBM.Location = new System.Drawing.Point(182, 38);
            this.chkIDdBM.Name = "chkIDdBM";
            this.chkIDdBM.Size = new System.Drawing.Size(95, 16);
            this.chkIDdBM.TabIndex = 98;
            this.chkIDdBM.Text = "display dBm";
            this.toolTip1.SetToolTip(this.chkIDdBM, "Display \"Peak Hold\" signal dBm value \r\n\r\nNOTE: This is not the true Signal streng" +
        "h of the Signal\r\nbut rather the strengh of a single FFT Bin as displayed on the " +
        "Pan\r\n");
            // 
            // chkIDSIG
            // 
            this.chkIDSIG.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.chkIDSIG.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.chkIDSIG.Image = null;
            this.chkIDSIG.Location = new System.Drawing.Point(182, 60);
            this.chkIDSIG.Name = "chkIDSIG";
            this.chkIDSIG.Size = new System.Drawing.Size(95, 16);
            this.chkIDSIG.TabIndex = 99;
            this.chkIDSIG.Text = "display S Units";
            this.toolTip1.SetToolTip(this.chkIDSIG, "Display \"Peak Hold\" signal dBm value in S units\r\n\r\nNOTE: This is not the true Sig" +
        "nal strengh of the Signal\r\nbut rather the strengh of a single FFT Bin as display" +
        "ed on the Pan");
            // 
            // label11
            // 
            this.label11.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label11.AutoSize = true;
            this.label11.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.label11.Location = new System.Drawing.Point(115, 522);
            this.label11.Name = "label11";
            this.label11.Size = new System.Drawing.Size(101, 13);
            this.label11.TabIndex = 92;
            this.label11.Text = "Pause Length (Sec)";
            // 
            // label12
            // 
            this.label12.AutoSize = true;
            this.label12.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.label12.Location = new System.Drawing.Point(100, 117);
            this.label12.Name = "label12";
            this.label12.Size = new System.Drawing.Size(36, 13);
            this.label12.TabIndex = 93;
            this.label12.Text = "Group";
            // 
            // groupBoxTS2
            // 
            this.groupBoxTS2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.groupBoxTS2.Controls.Add(this.chkIDSIG);
            this.groupBoxTS2.Controls.Add(this.chkIDdBM);
            this.groupBoxTS2.Controls.Add(this.udIDGap);
            this.groupBoxTS2.Controls.Add(this.udIDThres);
            this.groupBoxTS2.Controls.Add(this.chkAlwaysOnTop);
            this.groupBoxTS2.Controls.Add(this.label13);
            this.groupBoxTS2.Controls.Add(this.udIDTimer);
            this.groupBoxTS2.Controls.Add(this.labelTS27);
            this.groupBoxTS2.Controls.Add(this.labelTS23);
            this.groupBoxTS2.Controls.Add(this.chkBoxIdent);
            this.groupBoxTS2.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.groupBoxTS2.Location = new System.Drawing.Point(222, 472);
            this.groupBoxTS2.Name = "groupBoxTS2";
            this.groupBoxTS2.Size = new System.Drawing.Size(410, 98);
            this.groupBoxTS2.TabIndex = 94;
            this.groupBoxTS2.TabStop = false;
            this.groupBoxTS2.Text = "Sig Ident";
            this.toolTip1.SetToolTip(this.groupBoxTS2, resources.GetString("groupBoxTS2.ToolTip"));
            // 
            // chkAlwaysOnTop
            // 
            this.chkAlwaysOnTop.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.chkAlwaysOnTop.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.chkAlwaysOnTop.Image = null;
            this.chkAlwaysOnTop.Location = new System.Drawing.Point(300, 70);
            this.chkAlwaysOnTop.Name = "chkAlwaysOnTop";
            this.chkAlwaysOnTop.Size = new System.Drawing.Size(104, 24);
            this.chkAlwaysOnTop.TabIndex = 59;
            this.chkAlwaysOnTop.Text = "Always On Top";
            this.chkAlwaysOnTop.CheckedChanged += new System.EventHandler(this.chkAlwaysOnTop_CheckedChanged);
            // 
            // label13
            // 
            this.label13.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label13.AutoSize = true;
            this.label13.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.label13.Location = new System.Drawing.Point(290, 41);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(65, 13);
            this.label13.TabIndex = 95;
            this.label13.Text = "Persistance:";
            // 
            // labelTS27
            // 
            this.labelTS27.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.labelTS27.Image = null;
            this.labelTS27.Location = new System.Drawing.Point(70, 64);
            this.labelTS27.Name = "labelTS27";
            this.labelTS27.Size = new System.Drawing.Size(51, 18);
            this.labelTS27.TabIndex = 91;
            this.labelTS27.Text = "Hz Gap:";
            // 
            // labelTS23
            // 
            this.labelTS23.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.labelTS23.Image = null;
            this.labelTS23.Location = new System.Drawing.Point(70, 36);
            this.labelTS23.Name = "labelTS23";
            this.labelTS23.Size = new System.Drawing.Size(63, 18);
            this.labelTS23.TabIndex = 89;
            this.labelTS23.Text = "dBm Thres:";
            // 
            // comboMemGroupName
            // 
            this.comboMemGroupName.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left)));
            this.comboMemGroupName.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboMemGroupName.DropDownWidth = 112;
            this.comboMemGroupName.Location = new System.Drawing.Point(404, 305);
            this.comboMemGroupName.Name = "comboMemGroupName";
            this.comboMemGroupName.Size = new System.Drawing.Size(223, 21);
            this.comboMemGroupName.TabIndex = 64;
            this.comboMemGroupName.Visible = false;
            this.comboMemGroupName.SelectedIndexChanged += new System.EventHandler(this.comboMemGroupName_SelectedIndexChanged);
            // 
            // grpGenCustomTitleText
            // 
            this.grpGenCustomTitleText.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.grpGenCustomTitleText.Controls.Add(this.button_reset);
            this.grpGenCustomTitleText.Controls.Add(this.chkBoxLoop);
            this.grpGenCustomTitleText.Controls.Add(this.label14);
            this.grpGenCustomTitleText.Controls.Add(this.udspeedBox);
            this.grpGenCustomTitleText.Controls.Add(this.udstepBox);
            this.grpGenCustomTitleText.Controls.Add(this.udspeedBox1);
            this.grpGenCustomTitleText.Controls.Add(this.comboBoxTS1);
            this.grpGenCustomTitleText.Controls.Add(this.label6);
            this.grpGenCustomTitleText.Controls.Add(this.btnGroupMemory);
            this.grpGenCustomTitleText.Controls.Add(this.label4);
            this.grpGenCustomTitleText.Controls.Add(this.highFBox);
            this.grpGenCustomTitleText.Controls.Add(this.btnCustomList);
            this.grpGenCustomTitleText.Controls.Add(this.btnBandstack);
            this.grpGenCustomTitleText.Controls.Add(this.label2);
            this.grpGenCustomTitleText.Controls.Add(this.label5);
            this.grpGenCustomTitleText.Controls.Add(this.lowFBox);
            this.grpGenCustomTitleText.Controls.Add(this.button1);
            this.grpGenCustomTitleText.Controls.Add(this.label10);
            this.grpGenCustomTitleText.ForeColor = System.Drawing.SystemColors.ControlLightLight;
            this.grpGenCustomTitleText.Location = new System.Drawing.Point(12, 290);
            this.grpGenCustomTitleText.Name = "grpGenCustomTitleText";
            this.grpGenCustomTitleText.Size = new System.Drawing.Size(620, 176);
            this.grpGenCustomTitleText.TabIndex = 61;
            this.grpGenCustomTitleText.TabStop = false;
            this.grpGenCustomTitleText.Text = "Scan Type";
            // 
            // label14
            // 
            this.label14.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label14.AutoSize = true;
            this.label14.Location = new System.Drawing.Point(549, 104);
            this.label14.Name = "label14";
            this.label14.Size = new System.Drawing.Size(40, 13);
            this.label14.TabIndex = 99;
            this.label14.Text = "(mSec)";
            // 
            // udspeedBox
            // 
            this.udspeedBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.udspeedBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.udspeedBox.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.udspeedBox.Location = new System.Drawing.Point(392, 30);
            this.udspeedBox.Maximum = new decimal(new int[] {
            30000,
            0,
            0,
            0});
            this.udspeedBox.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.udspeedBox.Name = "udspeedBox";
            this.udspeedBox.Size = new System.Drawing.Size(80, 22);
            this.udspeedBox.TabIndex = 89;
            this.udspeedBox.Value = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            // 
            // udstepBox
            // 
            this.udstepBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.udstepBox.DecimalPlaces = 1;
            this.udstepBox.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.udstepBox.Increment = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.udstepBox.Location = new System.Drawing.Point(470, 129);
            this.udstepBox.Maximum = new decimal(new int[] {
            999,
            0,
            0,
            0});
            this.udstepBox.Minimum = new decimal(new int[] {
            90,
            0,
            0,
            -2147483648});
            this.udstepBox.Name = "udstepBox";
            this.udstepBox.Size = new System.Drawing.Size(58, 22);
            this.udstepBox.TabIndex = 91;
            this.udstepBox.Value = new decimal(new int[] {
            20,
            0,
            0,
            65536});
            // 
            // udspeedBox1
            // 
            this.udspeedBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.udspeedBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.udspeedBox1.Increment = new decimal(new int[] {
            100,
            0,
            0,
            0});
            this.udspeedBox1.Location = new System.Drawing.Point(538, 129);
            this.udspeedBox1.Maximum = new decimal(new int[] {
            30000,
            0,
            0,
            0});
            this.udspeedBox1.Minimum = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.udspeedBox1.Name = "udspeedBox1";
            this.udspeedBox1.Size = new System.Drawing.Size(63, 22);
            this.udspeedBox1.TabIndex = 90;
            this.udspeedBox1.Value = new decimal(new int[] {
            350,
            0,
            0,
            0});
            // 
            // comboBoxTS1
            // 
            this.comboBoxTS1.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.comboBoxTS1.DropDownWidth = 112;
            this.comboBoxTS1.ForeColor = System.Drawing.SystemColors.ActiveCaptionText;
            this.comboBoxTS1.Location = new System.Drawing.Point(139, 30);
            this.comboBoxTS1.Name = "comboBoxTS1";
            this.comboBoxTS1.Size = new System.Drawing.Size(234, 21);
            this.comboBoxTS1.TabIndex = 65;
            this.comboBoxTS1.SelectedIndexChanged += new System.EventHandler(this.comboBoxTS1_SelectedIndexChanged);
            this.comboBoxTS1.TextUpdate += new System.EventHandler(this.comboBoxTS1_SelectedIndexChanged);
            // 
            // label6
            // 
            this.label6.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label6.AutoSize = true;
            this.label6.Location = new System.Drawing.Point(190, 10);
            this.label6.Name = "label6";
            this.label6.Size = new System.Drawing.Size(126, 13);
            this.label6.TabIndex = 62;
            this.label6.Text = "\"Memory\" Group to Scan";
            // 
            // label4
            // 
            this.label4.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label4.AutoSize = true;
            this.label4.Location = new System.Drawing.Point(471, 104);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(61, 13);
            this.label4.TabIndex = 24;
            this.label4.Text = "Step in Khz";
            // 
            // highFBox
            // 
            this.highFBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.highFBox.BackColor = System.Drawing.Color.LightYellow;
            this.highFBox.Font = new System.Drawing.Font("Courier New", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.highFBox.Location = new System.Drawing.Point(324, 127);
            this.highFBox.Name = "highFBox";
            this.highFBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.highFBox.Size = new System.Drawing.Size(138, 24);
            this.highFBox.TabIndex = 21;
            this.highFBox.Click += new System.EventHandler(this.highFBox_Click);
            this.highFBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.highFBox_KeyDown);
            this.highFBox.MouseLeave += new System.EventHandler(this.highFBox_MouseLeave);
            // 
            // label2
            // 
            this.label2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(241, 104);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(166, 13);
            this.label2.TabIndex = 19;
            this.label2.Text = "Low Edge --  (MHz)  --  High Edge";
            // 
            // label5
            // 
            this.label5.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label5.AutoSize = true;
            this.label5.Location = new System.Drawing.Point(377, 58);
            this.label5.Name = "label5";
            this.label5.Size = new System.Drawing.Size(113, 13);
            this.label5.TabIndex = 25;
            this.label5.Text = "Scan Speed (in mSec)";
            // 
            // lowFBox
            // 
            this.lowFBox.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.lowFBox.BackColor = System.Drawing.Color.LightYellow;
            this.lowFBox.Font = new System.Drawing.Font("Courier New", 11.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.lowFBox.Location = new System.Drawing.Point(175, 127);
            this.lowFBox.Name = "lowFBox";
            this.lowFBox.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.lowFBox.Size = new System.Drawing.Size(139, 24);
            this.lowFBox.TabIndex = 6;
            this.lowFBox.Click += new System.EventHandler(this.lowFBox_Click);
            this.lowFBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.lowFBox_KeyDown);
            this.lowFBox.MouseLeave += new System.EventHandler(this.lowFBox_MouseLeave);
            // 
            // label10
            // 
            this.label10.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.label10.AutoSize = true;
            this.label10.Location = new System.Drawing.Point(538, 84);
            this.label10.Name = "label10";
            this.label10.Size = new System.Drawing.Size(66, 13);
            this.label10.TabIndex = 90;
            this.label10.Text = "Scan Speed";
            // 
            // ScanControl
            // 
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(64)))), ((int)(((byte)(64)))), ((int)(((byte)(64)))));
            this.ClientSize = new System.Drawing.Size(644, 577);
            this.Controls.Add(this.groupBoxTS2);
            this.Controls.Add(this.label12);
            this.Controls.Add(this.label11);
            this.Controls.Add(this.udPauseLength);
            this.Controls.Add(this.pausebtn);
            this.Controls.Add(this.label9);
            this.Controls.Add(this.comboMemGroupName);
            this.Controls.Add(this.label8);
            this.Controls.Add(this.label7);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.currFBox);
            this.Controls.Add(this.dataGridView2);
            this.Controls.Add(this.chkBoxSQLBRK);
            this.Controls.Add(this.grpGenCustomTitleText);
            this.Controls.Add(this.textBox3);
            this.Controls.Add(this.chkBoxSQLBRKWait);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.KeyPreview = true;
            this.MinimumSize = new System.Drawing.Size(590, 458);
            this.Name = "ScanControl";
            this.Text = "Scanner";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ScanControl_FormClosing);
            this.Load += new System.EventHandler(this.ScanControl_Load);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView2)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.udIDGap)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.udIDThres)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.udIDTimer)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.udPauseLength)).EndInit();
            this.groupBoxTS2.ResumeLayout(false);
            this.groupBoxTS2.PerformLayout();
            this.grpGenCustomTitleText.ResumeLayout(false);
            this.grpGenCustomTitleText.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.udspeedBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.udstepBox)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.udspeedBox1)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }






        #endregion

        public System.Windows.Forms.CheckBoxTS chkIDdBM;
        public System.Windows.Forms.CheckBoxTS chkIDSIG;
    } // scancontrol


} // powersdr
