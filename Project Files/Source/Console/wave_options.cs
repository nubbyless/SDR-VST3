using System;
using System.Windows.Forms;

namespace Thetis
{
    public class WaveOptions : Form
    {
        private Console console;

        private GroupBoxTS grpReceive;
        private RadioButtonTS radRXPreProcessed;
        private RadioButtonTS radRXPostProcessed;
        private GroupBoxTS groupBox1;
        private RadioButtonTS radTXPreProcessed;
        private RadioButtonTS radTXPostProcessed;
        private GroupBoxTS grpAudioSampleRate1;
        private ComboBoxTS comboSampleRate;
        private GroupBoxTS grpBitDepth;
        private RadioButtonTS radBitDepth8PCM;
        private RadioButtonTS radBitDepth16PCM;
        private RadioButtonTS radBitDepth24PCM;
        private RadioButtonTS radBitDepth32PCM;
        private RadioButtonTS radBitDepthIEEE;
        private CheckBoxTS chkWaveDither;
        private NumericUpDownTS udWaveDitherAmount;
        private GroupBoxTS grpWaveDither;
        private System.ComponentModel.IContainer components;
        private bool _loading;

        public WaveOptions(Console c)
        {
            console = c;
            InitializeComponent();
            try { Common.RestoreForm(this, "WaveOptions", false); } catch { }
            LoadFromArp();
            this.FormClosing += WaveOptions_FormClosing;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(WaveOptions));
            this.grpReceive = new GroupBoxTS();
            this.radRXPostProcessed = new RadioButtonTS();
            this.radRXPreProcessed = new RadioButtonTS();
            this.groupBox1 = new GroupBoxTS();
            this.radTXPostProcessed = new RadioButtonTS();
            this.radTXPreProcessed = new RadioButtonTS();
            this.grpAudioSampleRate1 = new GroupBoxTS();
            this.comboSampleRate = new ComboBoxTS();
            this.grpBitDepth = new GroupBoxTS();
            this.radBitDepth8PCM = new RadioButtonTS();
            this.radBitDepth16PCM = new RadioButtonTS();
            this.radBitDepth24PCM = new RadioButtonTS();
            this.radBitDepth32PCM = new RadioButtonTS();
            this.radBitDepthIEEE = new RadioButtonTS();
            this.grpWaveDither = new GroupBoxTS();
            this.udWaveDitherAmount = new NumericUpDownTS();
            this.chkWaveDither = new CheckBoxTS();
            this.grpReceive.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.grpAudioSampleRate1.SuspendLayout();
            this.grpBitDepth.SuspendLayout();
            this.grpWaveDither.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.udWaveDitherAmount)).BeginInit();
            this.SuspendLayout();
            // grpReceive
            this.grpReceive.Controls.Add(this.radRXPostProcessed);
            this.grpReceive.Controls.Add(this.radRXPreProcessed);
            this.grpReceive.Location = new System.Drawing.Point(8, 8);
            this.grpReceive.Name = "grpReceive";
            this.grpReceive.Size = new System.Drawing.Size(190, 80);
            this.grpReceive.TabIndex = 0;
            this.grpReceive.TabStop = false;
            this.grpReceive.Text = "When Receiving, Record ...";
            // radRXPostProcessed
            this.radRXPostProcessed.Image = null;
            this.radRXPostProcessed.Location = new System.Drawing.Point(16, 48);
            this.radRXPostProcessed.Name = "radRXPostProcessed";
            this.radRXPostProcessed.Size = new System.Drawing.Size(160, 24);
            this.radRXPostProcessed.TabIndex = 1;
            this.radRXPostProcessed.Text = "Receiver Output Audio";
            this.radRXPostProcessed.CheckedChanged += new EventHandler(this.AnyChanged);
            // radRXPreProcessed
            this.radRXPreProcessed.Image = null;
            this.radRXPreProcessed.Location = new System.Drawing.Point(16, 24);
            this.radRXPreProcessed.Name = "radRXPreProcessed";
            this.radRXPreProcessed.Size = new System.Drawing.Size(160, 24);
            this.radRXPreProcessed.TabIndex = 0;
            this.radRXPreProcessed.Text = "Receiver Input I/Q";
            this.radRXPreProcessed.CheckedChanged += new EventHandler(this.AnyChanged);
            // groupBox1
            this.groupBox1.Controls.Add(this.radTXPostProcessed);
            this.groupBox1.Controls.Add(this.radTXPreProcessed);
            this.groupBox1.Location = new System.Drawing.Point(8, 94);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(190, 80);
            this.groupBox1.TabIndex = 1;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "When Transmitting, Record ...";
            // radTXPostProcessed
            this.radTXPostProcessed.Image = null;
            this.radTXPostProcessed.Location = new System.Drawing.Point(16, 48);
            this.radTXPostProcessed.Name = "radTXPostProcessed";
            this.radTXPostProcessed.Size = new System.Drawing.Size(160, 24);
            this.radTXPostProcessed.TabIndex = 1;
            this.radTXPostProcessed.Text = "Transmitter Output I/Q";
            this.radTXPostProcessed.CheckedChanged += new EventHandler(this.AnyChanged);
            // radTXPreProcessed
            this.radTXPreProcessed.Image = null;
            this.radTXPreProcessed.Location = new System.Drawing.Point(16, 24);
            this.radTXPreProcessed.Name = "radTXPreProcessed";
            this.radTXPreProcessed.Size = new System.Drawing.Size(160, 24);
            this.radTXPreProcessed.TabIndex = 0;
            this.radTXPreProcessed.Text = "MIC Audio";
            this.radTXPreProcessed.CheckedChanged += new EventHandler(this.AnyChanged);
            // grpAudioSampleRate1
            this.grpAudioSampleRate1.Controls.Add(this.comboSampleRate);
            this.grpAudioSampleRate1.Location = new System.Drawing.Point(204, 118);
            this.grpAudioSampleRate1.Name = "grpAudioSampleRate1";
            this.grpAudioSampleRate1.Size = new System.Drawing.Size(100, 56);
            this.grpAudioSampleRate1.TabIndex = 3;
            this.grpAudioSampleRate1.TabStop = false;
            this.grpAudioSampleRate1.Text = "Sample Rate";
            // comboSampleRate
            this.comboSampleRate.DropDownStyle = ComboBoxStyle.DropDownList;
            this.comboSampleRate.Items.AddRange(new object[] { "6000","12000","24000","48000","96000","192000","384000" });
            this.comboSampleRate.Location = new System.Drawing.Point(16, 24);
            this.comboSampleRate.Name = "comboSampleRate";
            this.comboSampleRate.Size = new System.Drawing.Size(70, 21);
            this.comboSampleRate.TabIndex = 0;
            this.comboSampleRate.SelectedIndexChanged += new EventHandler(this.AnyChanged);
            // grpBitDepth
            this.grpBitDepth.Controls.Add(this.radBitDepth8PCM);
            this.grpBitDepth.Controls.Add(this.radBitDepth16PCM);
            this.grpBitDepth.Controls.Add(this.radBitDepth24PCM);
            this.grpBitDepth.Controls.Add(this.radBitDepth32PCM);
            this.grpBitDepth.Controls.Add(this.radBitDepthIEEE);
            this.grpBitDepth.Location = new System.Drawing.Point(204, 8);
            this.grpBitDepth.Name = "grpBitDepth";
            this.grpBitDepth.Size = new System.Drawing.Size(168, 106);
            this.grpBitDepth.TabIndex = 2;
            this.grpBitDepth.TabStop = false;
            this.grpBitDepth.Text = "Bit Depth";
            // radBitDepth8PCM
            this.radBitDepth8PCM.Image = null; this.radBitDepth8PCM.Location = new System.Drawing.Point(15, 83);
            this.radBitDepth8PCM.Name = "radBitDepth8PCM"; this.radBitDepth8PCM.Size = new System.Drawing.Size(144, 18);
            this.radBitDepth8PCM.TabIndex = 4; this.radBitDepth8PCM.Text = "8-Bit UnSigned PCM";
            this.radBitDepth8PCM.CheckedChanged += new EventHandler(this.AnyChanged);
            // radBitDepth16PCM
            this.radBitDepth16PCM.Image = null; this.radBitDepth16PCM.Location = new System.Drawing.Point(15, 66);
            this.radBitDepth16PCM.Name = "radBitDepth16PCM"; this.radBitDepth16PCM.Size = new System.Drawing.Size(144, 18);
            this.radBitDepth16PCM.TabIndex = 3; this.radBitDepth16PCM.Text = "16-Bit Signed PCM";
            this.radBitDepth16PCM.CheckedChanged += new EventHandler(this.AnyChanged);
            // radBitDepth24PCM
            this.radBitDepth24PCM.Image = null; this.radBitDepth24PCM.Location = new System.Drawing.Point(15, 49);
            this.radBitDepth24PCM.Name = "radBitDepth24PCM"; this.radBitDepth24PCM.Size = new System.Drawing.Size(144, 18);
            this.radBitDepth24PCM.TabIndex = 2; this.radBitDepth24PCM.Text = "24-Bit Signed PCM";
            this.radBitDepth24PCM.CheckedChanged += new EventHandler(this.AnyChanged);
            // radBitDepth32PCM
            this.radBitDepth32PCM.Image = null; this.radBitDepth32PCM.Location = new System.Drawing.Point(15, 32);
            this.radBitDepth32PCM.Name = "radBitDepth32PCM"; this.radBitDepth32PCM.Size = new System.Drawing.Size(144, 18);
            this.radBitDepth32PCM.TabIndex = 1; this.radBitDepth32PCM.Text = "32-Bit Signed PCM";
            this.radBitDepth32PCM.CheckedChanged += new EventHandler(this.AnyChanged);
            // radBitDepthIEEE
            this.radBitDepthIEEE.Image = null; this.radBitDepthIEEE.Location = new System.Drawing.Point(15, 15);
            this.radBitDepthIEEE.Name = "radBitDepthIEEE"; this.radBitDepthIEEE.Size = new System.Drawing.Size(144, 16);
            this.radBitDepthIEEE.TabIndex = 0; this.radBitDepthIEEE.Text = "32-Bit IEEE Floats";
            this.radBitDepthIEEE.CheckedChanged += new EventHandler(this.AnyChanged);
            // grpWaveDither
            this.grpWaveDither.Controls.Add(this.udWaveDitherAmount);
            this.grpWaveDither.Controls.Add(this.chkWaveDither);
            this.grpWaveDither.Location = new System.Drawing.Point(310, 118);
            this.grpWaveDither.Name = "grpWaveDither";
            this.grpWaveDither.Size = new System.Drawing.Size(100, 56);
            this.grpWaveDither.TabIndex = 4;
            this.grpWaveDither.TabStop = false;
            this.grpWaveDither.Text = "Dither";
            // chkWaveDither
            this.chkWaveDither.AutoSize = true; this.chkWaveDither.Image = null;
            this.chkWaveDither.Location = new System.Drawing.Point(8, 22);
            this.chkWaveDither.Name = "chkWaveDither"; this.chkWaveDither.Size = new System.Drawing.Size(15, 14);
            this.chkWaveDither.TabIndex = 0; this.chkWaveDither.Text = "";
            this.chkWaveDither.CheckedChanged += new EventHandler(this.AnyChanged);
            // udWaveDitherAmount
            this.udWaveDitherAmount.DecimalPlaces = 1; this.udWaveDitherAmount.Increment = new decimal(new int[] {1,0,0,65536});
            this.udWaveDitherAmount.Location = new System.Drawing.Point(30, 19);
            this.udWaveDitherAmount.Maximum = new decimal(new int[] {10,0,0,65536});
            this.udWaveDitherAmount.Minimum = new decimal(new int[] {1,0,0,65536});
            this.udWaveDitherAmount.Name = "udWaveDitherAmount"; this.udWaveDitherAmount.Size = new System.Drawing.Size(55, 20);
            this.udWaveDitherAmount.TabIndex = 1; this.udWaveDitherAmount.TinyStep = false;
            this.udWaveDitherAmount.Value = new decimal(new int[] {1,0,0,0});
            this.udWaveDitherAmount.ValueChanged += new EventHandler(this.AnyChanged);
            // WaveOptions
            this.ClientSize = new System.Drawing.Size(420, 185);
            this.Controls.Add(this.grpWaveDither);
            this.Controls.Add(this.grpAudioSampleRate1);
            this.Controls.Add(this.grpBitDepth);
            this.Controls.Add(this.groupBox1);
            this.Controls.Add(this.grpReceive);
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.Name = "WaveOptions";
            this.Text = "Wave Record Options";
            this.grpReceive.ResumeLayout(false);
            this.groupBox1.ResumeLayout(false);
            this.grpAudioSampleRate1.ResumeLayout(false);
            this.grpBitDepth.ResumeLayout(false);
            this.grpWaveDither.ResumeLayout(false);
            this.grpWaveDither.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.udWaveDitherAmount)).EndInit();
            this.ResumeLayout(false);
        }

        private void LoadFromArp()
        {
            if (console == null || console.ARP == null) return;
            _loading = true;
            try
            {
                radRXPreProcessed.Checked = console.ARP.RxSource == AudioRecordRxSource.ReceiverInputIQ;
                radRXPostProcessed.Checked = !radRXPreProcessed.Checked;
                radTXPreProcessed.Checked = console.ARP.TxSource == AudioRecordTxSource.MicAudio;
                radTXPostProcessed.Checked = !radTXPreProcessed.Checked;

                string sr = console.ARP.SampleRate.ToString();
                int idx = comboSampleRate.Items.IndexOf(sr);
                if (idx >= 0) comboSampleRate.SelectedIndex = idx; else comboSampleRate.Text = sr;

                radBitDepthIEEE.Checked = console.ARP.BitDepthMode == AudioBitDepthMode.IeeeFloat32;
                radBitDepth32PCM.Checked = console.ARP.BitDepthMode == AudioBitDepthMode.Pcm32;
                radBitDepth24PCM.Checked = console.ARP.BitDepthMode == AudioBitDepthMode.Pcm24;
                radBitDepth16PCM.Checked = console.ARP.BitDepthMode == AudioBitDepthMode.Pcm16;
                radBitDepth8PCM.Checked = console.ARP.BitDepthMode == AudioBitDepthMode.Pcm8;

                chkWaveDither.Checked = console.ARP.DitherEnabled;
                try { udWaveDitherAmount.Value = Math.Max(udWaveDitherAmount.Minimum, Math.Min(udWaveDitherAmount.Maximum, (decimal)console.ARP.DitherAmount)); } catch { }
            }
            finally { _loading = false; }
        }

        private void AnyChanged(object sender, EventArgs e)
        {
            if (_loading || console == null || console.ARP == null) return;
            try
            {
                console.ARP.RxSource = radRXPreProcessed.Checked ? AudioRecordRxSource.ReceiverInputIQ : AudioRecordRxSource.ReceiverOutputAudio;
                console.ARP.TxSource = radTXPreProcessed.Checked ? AudioRecordTxSource.MicAudio : AudioRecordTxSource.TransmitterOutputIQ;
                if (int.TryParse(comboSampleRate.Text, out int sr) && sr > 0) console.ARP.SampleRate = sr;

                if (radBitDepthIEEE.Checked) console.ARP.BitDepthMode = AudioBitDepthMode.IeeeFloat32;
                else if (radBitDepth32PCM.Checked) console.ARP.BitDepthMode = AudioBitDepthMode.Pcm32;
                else if (radBitDepth24PCM.Checked) console.ARP.BitDepthMode = AudioBitDepthMode.Pcm24;
                else if (radBitDepth16PCM.Checked) console.ARP.BitDepthMode = AudioBitDepthMode.Pcm16;
                else if (radBitDepth8PCM.Checked) console.ARP.BitDepthMode = AudioBitDepthMode.Pcm8;

                console.ARP.DitherEnabled = chkWaveDither.Checked;
                console.ARP.DitherAmount = (float)udWaveDitherAmount.Value;
            }
            catch { }
        }

        private void WaveOptions_FormClosing(object sender, FormClosingEventArgs e)
        {
            try { Common.SaveForm(this, "WaveOptions"); } catch { }
            e.Cancel = true;
            this.Hide();
        }
    }
}
