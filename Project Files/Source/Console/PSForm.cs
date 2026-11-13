/*  PSForm.cs

This file is part of a program that implements a Software-Defined Radio.

This code/file can be found on GitHub : https://github.com/ramdor/Thetis

Copyright (C) 2000-2025 Original authors
Copyright (C) 2020-2026 Richard Samphire MW0LGE

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

The author can be reached by email at

mw0lge@grange-lane.co.uk
*/
//
//============================================================================================//
// Dual-Licensing Statement (Applies Only to Author's Contributions, Richard Samphire MW0LGE) //
// ------------------------------------------------------------------------------------------ //
// For any code originally written by Richard Samphire MW0LGE, or for any modifications       //
// made by him, the copyright holder for those portions (Richard Samphire) reserves the       //
// right to use, license, and distribute such code under different terms, including           //
// closed-source and proprietary licences, in addition to the GNU General Public License      //
// granted above. Nothing in this statement restricts any rights granted to recipients under  //
// the GNU GPL. Code contributed by others (not Richard Samphire) remains licensed under      //
// its original terms and is not affected by this dual-licensing statement in any way.        //
// Richard Samphire can be reached by email at :  mw0lge@grange-lane.co.uk                    //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Thetis
{
    public partial class PSForm : Form
    {
        #region constructor

        private Console console;

        public PSForm(Console c)
        {
            Debug.Print(DateTime.UtcNow.Ticks.ToString() + " PSForm: Constructor Start");

            InitializeComponent();
            Common.DoubleBufferAll(this, true);

            txtPSpeak.Text = "";

            console = c;    // MW0LGE moved above restore, so that we actaully have console when control events fire because of restore form
            
            try
            {
                Common.RestoreForm(this, "PureSignal", false); // will also restore txtPSpeak //MW0LGE_21k9rc5
            }
            catch
            {
                // A bad entry in the table must not abort the constructor.  If it does, the
                // form is left half initialised: _psFormReady stays false and the values the
                // user changes are then never written back to the database.
            }

            // The target feedback level is stored in the setup form's database (the "Options"
            // table, as "udPSTargetFeedbackLevel").  The "PureSignal" table also has an entry
            // for this control, it must not become a second source of truth.
            try
            {
                if (console != null && !console.IsSetupFormNull)
                {
                    udPSTargetFeedback.Value = console.SetupForm.PSTargetFeedbackLevel;
                    SetTestToneFrequencies(console.SetupForm.TestToneFreq1, console.SetupForm.TestToneFreq2);
                }

                // The restore sets the Value, but a NumericUpDown only pushes its text to the
                // display once it has a window handle.  Set the text as well, otherwise the box
                // shows whatever it had (often "0") while the value is correct.
                SyncControlTexts();
            }
            catch { }

            // The tone frequency boxes are always shown and always usable.
            SetTestToneControlsEnabled(true);

            // Diagnostic: what the values in here ended up as.  The target feedback level has
            // been read from more than one place over the years, this shows which one won.
            // NOTE: console.SetupForm is a lazy singleton - touching it creates the setup form.
            // That must not happen here, the setup form is initialised at the wrong point of
            // the console startup otherwise (the display buffers are not ready yet).
            try
            {
                int setupTarget = -1;
                if (console != null && !console.IsSetupFormNull)
                    setupTarget = console.SetupForm.PSTargetFeedbackLevel;

                psLog(string.Format("PS form start: fbTarget box={0} setup={1} engine={2} hwDefault={3} tones={4}/{5}",
                    udPSTargetFeedback.Value,
                    setupTarget,
                    puresignal.TargetFeedbackLevel,
                    HardwareSpecific.PSTargetFeedbackLevel,
                    udTestIMDFreq1.Value,
                    udTestIMDFreq2.Value));
            }
            catch { }

            console.PowerChangeHanders += onPowerOn;
            console.ConsoleClosingHandlersAsync += onConsoleClosingAsync;

            _power = console.PowerOn;

            startPSThread(); // MW0LGE_21k8 removed the winform timers, now using dedicated thread

            _psFormReady = true;
        }

        #endregion

        #region variables

        private int _gcolor = (0xFF << 24) | (0xFF << 8);
        private static bool _autoON = false;
        private static bool _singlecalON = false;
        private static bool _restoreON = false;
        private static bool _OFF = true;
        //private int oldCalCount = 0;
        //private int red, green, blue;
        private eAAState _autoAttenuateState = eAAState.Monitor;
        private static double _PShwpeak;
        private static double _GetPSpeakval;

        public static AmpView ampv = null;
        public static Thread ampvThread = null;

        //private int oldCalCount2 = 0;
        private int _save_autoON = 0;
        private int _save_singlecalON = 0;
        private int _deltadB = 0;

        private bool _power;
        private bool _psFormReady = false;
        private bool _lastPSIndicated = false;

        private enum eCMDState
        {
            OFF = 0,
            TurnOnAutoCalibrate = 1,
            AutoCalibrate = 2,
            TurnOnSingleCalibrate = 3,
            SingleCalibrate = 4,
            StayON = 5,
            TurnOFF = 6,
            IntiateRestoredCorrection = 7
        }
        private enum eAAState
        {
            Monitor = 0,
            SetNewValues = 1,
            RestoreOperation = 2
        }

        private static eCMDState _cmdstate = eCMDState.OFF;
        private static bool _topmost = false;
        
        private Thread _ps_thread = null;
        #endregion

        #region properties

        private void startPSThread()
        {
            if (_ps_thread == null || !_ps_thread.IsAlive)
            {
                _ps_thread = new Thread(new ThreadStart(PSLoop))
                {
                    Name = "PureSignal Thread",
                    Priority = ThreadPriority.AboveNormal,
                    IsBackground = true
                };
                _ps_thread.Start();
            }
        }

        private bool m_bQuckAttenuate = false;
        public bool QuickAttenuate
        {
            get { return m_bQuckAttenuate; }
            set { m_bQuckAttenuate = value; }
        }

        public int Ints
        {
            get { return 16; }
        }
        public int Spi
        {
            get { return 256; }
        }

        public ToolTip ToolTip //[2.10.3.9]MW0LGE used by finder
        {
            get
            {
                return toolTip1;
            }
        }
        public void StopPSThread()
        {
            _ps_closing = true;
            _bPSRunning = false;
            Debug.Print(DateTime.UtcNow.Ticks.ToString() + " PSForm: Stopping PS Thread");
            if (_ps_thread != null && _ps_thread.IsAlive) _ps_thread.Join(200);
            Debug.Print(DateTime.UtcNow.Ticks.ToString()  + " PSForm: PS Thread Stopped");
            psLogClose();

            if (console != null)
            {
                console.PowerChangeHanders -= onPowerOn;
                console.ConsoleClosingHandlersAsync -= onConsoleClosingAsync;
            }
        }
        private async Task onConsoleClosingAsync()
        {
            _ps_closing = true;
            await Task.Delay(100);
        }
        private void onPowerOn(bool oldPower, bool newPower)
        {
            _power = newPower;
        }
        private volatile bool _bPSRunning = false;
        private volatile bool _ps_closing = false;
        private void PSLoop()
        {
            _bPSRunning = true;
            int nCount = 0;

            while (_bPSRunning)
            {
                if (_ps_closing) break; // gated

                int sleepDuration;
                bool run = !_ps_closing && _power && !IsDisposed && IsHandleCreated;

                if (run)
                {
                    timer1code();
                    if (nCount == 0)
                        timer2code();

                    nCount++;
                    if (m_bQuckAttenuate || nCount == 10)
                        nCount = 0;

                    sleepDuration = 10;
                }
                else
                {
                    nCount = 0;
                    sleepDuration = 100;
                }

                Thread.Sleep(sleepDuration);
            }
            Debug.Print(DateTime.UtcNow.Ticks.ToString() + " PSForm: Exiting PS Thread");
        }

        //private volatile bool _dismissAmpv = false;
        //public bool DismissAmpv
        //{
        //    get { return _dismissAmpv; }
        //    set
        //    {
        //        _dismissAmpv = value;
        //    }
        //}

        private static bool _psenabled = false;
        // Is the PureSignal running?  The command state machine is only in the OFF state
        // when the PureSignal is really off.  Note that PSEnabled is *not* the same thing:
        // it means "the radio supplies the feedback samples", and the state machine turns
        // it off in StayON (the correction is stored, no live feedback is needed any more).
        public bool PureSignalOn
        {
            get { return _cmdstate != eCMDState.OFF; }
        }

        public bool PSEnabled
        {
            get { return _psenabled; }
            set
            {
                _psenabled = value;

                if (_psenabled)
                {
                    // Set the system to supply feedback.
                    console.UpdateDDCs(console.RX2Enabled);
                    NetworkIO.SetPureSignal(1);
                    NetworkIO.SendHighPriority(1); // send the HP packet
                    //console.UpdateRXADCCtrl();
                    console.UpdateAAudioMixerStates();
                    unsafe { cmaster.LoadRouterControlBit((void*)0, 0, 0, 1); }
                    console.radio.GetDSPTX(0).PSRunCal = true;
                }
                else
                {
                    // Set the system to turn-off feedback.
                    console.UpdateDDCs(console.RX2Enabled);
                    NetworkIO.SetPureSignal(0);
                    NetworkIO.SendHighPriority(1); // send the HP packet
                    //console.UpdateRXADCCtrl();
                    console.UpdateAAudioMixerStates();
                    unsafe { cmaster.LoadRouterControlBit((void*)0, 0, 0, 0); }
                    console.radio.GetDSPTX(0).PSRunCal = false;
                }
                                               
                // console.EnableDup();
                if (console.path_Illustrator != null)
                    console.path_Illustrator.pi_Changed();                
            }
        }

        private static bool _autocal_enabled = false;
        public bool AutoCalEnabled
        {
            get { return _autocal_enabled; }
            set
            {
                _autocal_enabled = value;
                if (_autocal_enabled)
                {
                    _autoON = true;
                    console.PSState = true;
                }
                else
                {
                    _OFF = true;
                    console.PSState = false;
                }
            }
        }

        private static bool _autoattenuate = true;
        public bool AutoAttenuate
        {
            get { return _autoattenuate; }
            set
            {
                _autoattenuate = value;
                if (_autoattenuate)
                {
                    console.ATTOnTX = _autoattenuate;
                }
                else
                {
                    // restore setting direct from setupform
                    if (!console.IsSetupFormNull)
                    {
                        console.ATTOnTX = console.SetupForm.ATTOnTXChecked;
                    }
                    else
                    {
                        console.ATTOnTX = _autoattenuate;
                    }
                }
            }
        }

        private static bool _ttgenON = false;
        public bool TTgenON
        {
            get { return _ttgenON; }
            set
            {
                _ttgenON = value;
                updateTestGenButtons();
            }
        }

        // the test generator buttons show which stimulus is running
        private void updateTestGenButtons()
        {
            bool noise = false;
            // do not touch the SetupForm singleton here, it would create the form
            if (console != null && !console.IsSetupFormNull) noise = console.SetupForm.TestIMDNoiseStimulus;

            if (_ttgenON)
            {
                btnPSTwoToneGen.BackColor = noise ? SystemColors.Control : Color.FromArgb(_gcolor);
                btnPSNoiseGen.BackColor = noise ? Color.FromArgb(_gcolor) : SystemColors.Control;
            }
            else
            {
                btnPSTwoToneGen.BackColor = SystemColors.Control;
                btnPSNoiseGen.BackColor = SystemColors.Control;
            }
        }

        private static int _txachannel = WDSP.id(1, 0);
        public int TXAchannel
        {
            get { return _txachannel; }
            set { _txachannel = value; }
        }

        private readonly Object _objLocker = new Object();

        private static bool _mox = false;
        public bool Mox
        {
            get { return _mox; }
            set
            {
                _mox = value;
                puresignal.SetPSMox(_txachannel, value);
            }
        }


        private void psdefpeak(double value)
        {
            // note : PSpeak_TextChanged will fire if db recovers value into text box
            string sVal = value.ToString();
            if(txtPSpeak.Text != sVal)
                txtPSpeak.Text = value.ToString(); // causes text change event
            else
                PSpeak_TextChanged(this, EventArgs.Empty); // there would be no event as text the same, so fire it here

            UpdateWarningSetPk();
        }

        #endregion

        #region event handlers
        private void PSForm_Load(object sender, EventArgs e)
        {
            SetupForm();
        }

        public void SetupForm()//EventArgs e)  //MW0LGE_[2.9.0.7]
        {
            if (_ttgenON == true)
                btnPSTwoToneGen.BackColor = Color.FromArgb(_gcolor);

            unsafe
            {
                fixed (double* ptr = &_PShwpeak)
                    puresignal.GetPSHWPeak(_txachannel, ptr);
            }

            txtPSpeak.Text = _PShwpeak.ToString();

            // The box is the master: the value in it is the value the engine and the setup
            // form use.  It was restored from the database, so it is the right one.
            try
            {
                puresignal.TargetFeedbackLevel = (int)udPSTargetFeedback.Value;
                if (console != null && !console.IsSetupFormNull)
                    console.SetupForm.PSTargetFeedbackLevel = (int)udPSTargetFeedback.Value;

                SyncControlTexts();
            }
            catch { }

            // The stimulus selection and the tone spacing warning belong to this window,
            // not to the Setup > Tests tab.  Make sure they are up to date.
            try { console.SetupForm.UpdateCalStimulusUI(singleCalUseNoise); }
            catch { }

            try
            {
                psLog(string.Format("PS form shown: fbTarget box={0} engine={1} min={2} max={3} inc={4} text='{5}' tones={6}/{7} getpk={8} fb={9}",
                    udPSTargetFeedback.Value,
                    puresignal.TargetFeedbackLevel,
                    udPSTargetFeedback.Minimum,
                    udPSTargetFeedback.Maximum,
                    udPSTargetFeedback.Increment,
                    udPSTargetFeedback.Text,
                    udTestIMDFreq1.Value,
                    udTestIMDFreq2.Value,
                    GetPSpeak.Text,
                    lblPSfb2.Text));
            }
            catch { }
        }

        private void PSForm_Closing(object sender, FormClosingEventArgs e)
        {
            //[2.10.3.4]]MW0LGE leave it there until thetis closes
            //if (ampv != null)
            //{
            //    _dismissAmpv = true;
            //    ampvThread.Join();
            //    ampv.Close();
            //    ampv = null;
            //}
            this.Hide();
            e.Cancel = true;
            Common.SaveForm(this, "PureSignal");
        }

        private readonly ManualResetEventSlim _ampViewDone = new ManualResetEventSlim(false);
        public void CloseAmpView()
        {
            if (ampv != null)
            {
                _ampViewDone.Reset();
                try
                {
                    ampv.BeginInvoke((Action)(() => ampv.CloseDown() ));
                }
                catch { }

                _ampViewDone.Wait(2000);

                if (ampvThread != null && ampvThread.IsAlive)
                {
                    ampvThread.Join(1000);
                }

                ampvThread = null;
                ampv = null;
            }
        }
        public void RunAmpv()
        {
            ampv = new AmpView(this);
            ampv.Opacity = 0f;
            Application.Run(ampv);
            _ampViewDone.Set();
        }

        private void btnPSAmpView_Click(object sender, EventArgs e)
        {
            if (ampv == null || (ampv != null && ampv.IsDisposed))
            {
                //_dismissAmpv = false;
                ampvThread = new Thread(RunAmpv);
                ampvThread.SetApartmentState(ApartmentState.STA);
                ampvThread.Name = "Ampv Thread";
                ampvThread.IsBackground = true;
                ampvThread.Start();
            }
        }

        private void btnPSCalibrate_Click(object sender, EventArgs e)
        {
            if (_singlecalON)
            {
                // need this incase single cal is unable to complete do to bad feedback level
                // state machine will drop out if this is the case
                _singlecalON = false;
                psLog("EVENT single cal stop");
                stopSingleCalStimulus();
                return;
            }
            console.ForcePureSignalAutoCalDisable();
            // make sure the stimulus the user chose is the one that is driving the TX
            psLog("EVENT single cal start: " + (singleCalUseNoise ? "noise" : "twotone"));
            startSingleCalStimulus();
            _singlecalON = true;
            console.PSState = false;
        }

        // The stimulus the "Single Cal" uses.  The user chooses it in the combo box under the
        // Single Cal button.  The two tone is the default, the noise is selectable.
        // The noise is the better stimulus for the calibration: the calibration axis is the
        // TX envelope (wdsp/calcc.c, putSample()/calc()), so a continuously varying envelope
        // gives the calibration a sweep of drive levels, while the two tone has a nearly
        // constant envelope.  See the WDSP guide 2.11, Appendix D 13.3 and 13.6.
        private bool singleCalUseNoise
        {
            get { return comboPSCalStimulus != null && comboPSCalStimulus.Text == "Noise"; }
        }

        // The stimulus the TX test runs with.  This is the single source of truth for it:
        // the Setup > Tests tab has no stimulus selector of its own any more, the
        // PureSignal form (the linearity window) is where the calibration is set up.
        public bool UseNoiseStimulus
        {
            get { return singleCalUseNoise; }
        }

        // Select the stimulus.  The setup form is told about it so it can swap the
        // generator over when the TX test is already running.
        public void SetCalStimulus(bool noise)
        {
            try
            {
                if (comboPSCalStimulus == null) return;
                int index = noise ? 1 : 0;
                if (comboPSCalStimulus.SelectedIndex == index) return;
                comboPSCalStimulus.SelectedIndex = index;  // runs the change handler
            }
            catch { }
        }

        private void comboPSCalStimulus_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (console == null || console.IsSetupFormNull) return;
                console.SetupForm.OnCalStimulusChanged(singleCalUseNoise);
            }
            catch { }
        }

        // The two tone frequencies.  They live here, in the linearity window, and not in the
        // Setup > Tests tab: the tone spacing decides whether the calibration sees a
        // continuous sweep of drive levels or only a few repeated ones.  A spacing that is an
        // exact sub-multiple of the TX processing rate is the trap, see the WDSP guide 2.11,
        // Appendix D 13.3 and 13.6.
        // The setup form owns the values (it stores them in the database), the form here
        // only shows and edits them.
        public void SetTestToneFrequencies(decimal f1, decimal f2)
        {
            try
            {
                if (udTestIMDFreq1 != null && udTestIMDFreq1.Value != f1) udTestIMDFreq1.Value = f1;
                if (udTestIMDFreq2 != null && udTestIMDFreq2.Value != f2) udTestIMDFreq2.Value = f2;
            }
            catch { }
        }

        // The "FB Target" box.  The setup form owns the value (it is in the database), this
        // form only shows and edits it.
        public void SetTargetFeedbackLevel(int value)
        {
            try
            {
                if (udPSTargetFeedback != null && udPSTargetFeedback.Value != value)
                    udPSTargetFeedback.Value = value;
            }
            catch { }
        }

        // The tone frequency boxes are always shown.  They used to be greyed out with the
        // noise stimulus, and a disabled control is not drawn at all, so they disappeared
        // exactly when they were needed.  Only the values are locked while the TX test
        // is running.
        public void SetTestToneControlsEnabled(bool enabled)
        {
            try
            {
                if (udTestIMDFreq1 != null) { udTestIMDFreq1.Visible = true; udTestIMDFreq1.Enabled = enabled; }
                if (udTestIMDFreq2 != null) { udTestIMDFreq2.Visible = true; udTestIMDFreq2.Enabled = enabled; }
                if (lblTestToneFreq1 != null) { lblTestToneFreq1.Visible = true; lblTestToneFreq1.Enabled = enabled; }
                if (lblTestToneFreq2 != null) { lblTestToneFreq2.Visible = true; lblTestToneFreq2.Enabled = enabled; }
                if (btnTwoToneF_defaults != null) { btnTwoToneF_defaults.Visible = true; btnTwoToneF_defaults.Enabled = enabled; }
            }
            catch { }
        }

        private void btnTwoToneF_defaults_Click(object sender, EventArgs e)
        {
            // the spacing is 1250Hz, that is not a sub-multiple of the 96kHz TX processing rate
            // and not of the 12.288kHz radio rate.  A two tone spacing that IS a sub-multiple
            // makes the digitised envelope repeat its sample values, the PureSignal calibration
            // then only sees a few repeated levels instead of a continuous sweep.
            setTestToneFrequencies(700m, 1950m);
        }

        private void setTestToneFrequencies(decimal f1, decimal f2)
        {
            try
            {
                if (console == null || console.IsSetupFormNull) return;
                console.SetupForm.TestToneFreq1 = f1;
                console.SetupForm.TestToneFreq2 = f2;
                updateToneSpacingWarning();
            }
            catch { }
        }

        private void udTestIMDFreq1_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                if (console == null || console.IsSetupFormNull) return;
                console.SetupForm.TestToneFreq1 = (decimal)udTestIMDFreq1.Value;
                updateToneSpacingWarning();
            }
            catch { }
        }

        private void udTestIMDFreq2_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                if (console == null || console.IsSetupFormNull) return;
                console.SetupForm.TestToneFreq2 = (decimal)udTestIMDFreq2.Value;
                updateToneSpacingWarning();
            }
            catch { }
        }

        private void updateToneSpacingWarning()
        {
            try
            {
                if (console == null || console.IsSetupFormNull) return;
                console.SetupForm.UpdateCalStimulusUI(singleCalUseNoise);
            }
            catch { }
        }

        // Show the tone spacing warning.  It belongs here, in the linearity window, and
        // not in the Setup > Tests tab: this is where the PureSignal calibration is set
        // up and where the warning has to be seen.
        public void SetCalStimulusWarning(string text)
        {
            try
            {
                if (lblPSSpacingWarning == null) return;
                lblPSSpacingWarning.Text = text ?? "";
                lblPSSpacingWarning.Visible = lblPSSpacingWarning.Text.Length > 0;
            }
            catch { }
        }

        // The setup form belongs to the UI thread.  The PS form is driven from the PS thread and
        // the Single Cal can also be started by a CAT command, so the calls into the setup form
        // have to be marshalled over to the UI thread.
        private void runOnUIThread(Action action)
        {
            try
            {
                if (IsHandleCreated && InvokeRequired)
                    BeginInvoke(action);
                else
                    action();
            }
            catch { }
        }

        // Make sure the stimulus the user selected is the one that is running.
        private void startSingleCalStimulus()
        {
            if (console == null || console.IsSetupFormNull) return;

            bool noise = singleCalUseNoise;

            runOnUIThread(() =>
            {
                console.SetupForm.EnsureTestStimulus(noise);
                psLog("EVENT single cal stimulus start: " + (noise ? "noise" : "twotone"));
            });
        }

        // Stop the stimulus again when the Single Cal is over.  The stimulus is stopped when it
        // is the one the Single Cal used, no matter who started it - otherwise the transmitter
        // would keep sending the test tone after the calibration was switched off.
        private void stopSingleCalStimulus()
        {
            if (console == null || console.IsSetupFormNull) return;

            bool noise = singleCalUseNoise;

            runOnUIThread(() =>
            {
                if (!console.SetupForm.TTgenrun) return;
                if (console.SetupForm.TestIMDNoiseStimulus != noise) return;  // a different one is running
                console.SetupForm.TTgenrun = false;
                psLog("EVENT single cal stimulus stop");
            });
        }

        //-W2PA Adds capability for CAT control via console
        public void SingleCalrun()
        {
            btnPSCalibrate_Click(this, EventArgs.Empty); 
        }

        private void btnPSReset_Click(object sender, EventArgs e)
        {
            console.ForcePureSignalAutoCalDisable();
            // A single cal that is waiting to run must not switch the PureSignal back on
            // after the OFF button was pressed.
            _singlecalON = false;
            _performing_single_cal = false;
            if (!_OFF) _OFF = true;
            console.PSState = false;
            // Switch it off now.  Waiting for the engine to acknowledge the reset can take
            // until the next transmission, and until then the "correcting" light stayed on
            // and nothing showed that the button had been pressed.
            _cmdstate = eCMDState.OFF;

            try { psLog("EVENT puresignal OFF"); }
            catch { }
        }

        private void udPSMoxDelay_ValueChanged(object sender, EventArgs e)
        {
            puresignal.SetPSMoxDelay(_txachannel, (double)udPSMoxDelay.Value);
        }

        private void udPSCalWait_ValueChanged(object sender, EventArgs e)
        {
            puresignal.SetPSLoopDelay(_txachannel, (double)udPSCalWait.Value);
        }

        private void udPSPhnum_ValueChanged(object sender, EventArgs e)
        {
            double actual_delay = puresignal.SetPSTXDelay(_txachannel, (double)udPSPhnum.Value * 1.0e-09);
        }

        private void btnPSTwoToneGen_Click(object sender, EventArgs e)
        {
            if (_ttgenON == false)
            {
                _ttgenON = true;
                console.SetupForm.StartTestStimulus(false); // two tone
                psLog("EVENT two tone generator on");
                updateTestGenButtons();
            }
            else
            {
                _ttgenON = false;
                console.SetupForm.TTgenrun = false;
                psLog("EVENT two tone generator off");
                updateTestGenButtons();
            }
        }

        private void btnPSNoiseGen_Click(object sender, EventArgs e)
        {
            if (_ttgenON == false)
            {
                _ttgenON = true;
                console.SetupForm.StartTestStimulus(true); // noise
                psLog("EVENT noise generator on");
                updateTestGenButtons();
            }
            else
            {
                _ttgenON = false;
                console.SetupForm.TTgenrun = false;
                psLog("EVENT noise generator off");
                updateTestGenButtons();
            }
        }

        private void btnPSSave_Click(object sender, EventArgs e)
        {
            System.IO.Directory.CreateDirectory(console.AppDataPath + "PureSignal\\");
            SaveFileDialog savefile1 = new SaveFileDialog();
            savefile1.InitialDirectory = console.AppDataPath + "PureSignal\\";
            savefile1.RestoreDirectory = true;
            if (savefile1.ShowDialog() == DialogResult.OK)
                puresignal.PSSaveCorr(_txachannel, savefile1.FileName);
        }

        private void btnPSRestore_Click(object sender, EventArgs e)
        {
            OpenFileDialog openfile1 = new OpenFileDialog();
            openfile1.InitialDirectory = console.AppDataPath + "PureSignal\\";
            openfile1.RestoreDirectory = true;
            if (openfile1.ShowDialog() == DialogResult.OK)
            {
                console.ForcePureSignalAutoCalDisable();
                _OFF = false;
                puresignal.PSRestoreCorr(_txachannel, openfile1.FileName);
                _restoreON = true;
            }
        }
        public void SetDefaultPeaks()
        {
            psdefpeak(HardwareSpecific.PSDefaultPeak);
        }

        // PureSignal diagnostics.  The engine only writes the feedback level and the
        // "correcting" flag after a successful calibration, so a calibration that never
        // completes leaves no trace on the screen.  This log shows the engine state and the
        // internal flags while it runs, so a failed calibration can be analysed afterwards.
        // It is written to <appdata>\PureSignal\PS_debug.log
        private string _ps_log_path = null;
        private StreamWriter _ps_log = null;
        private DateTime _ps_log_last = DateTime.MinValue;
        private int _ps_log_last_state = -1;

        private void psLog(string msg)
        {
            try
            {
                if (_ps_log == null)
                {
                    if (_ps_log_path == null)
                    {
                        System.IO.Directory.CreateDirectory(console.AppDataPath + "PureSignal\\");
                        _ps_log_path = console.AppDataPath + "PureSignal\\PS_debug.log";
                    }
                    _ps_log = new StreamWriter(_ps_log_path, true);
                }
                _ps_log.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg);
                _ps_log.Flush();
            }
            catch { }
        }

        private void psLogState()
        {
            try
            {
                int state = puresignal.Info[15];
                DateTime now = DateTime.Now;

                // log every state change, otherwise at most once a second
                if (state == _ps_log_last_state && (now - _ps_log_last).TotalMilliseconds < 1000) return;

                _ps_log_last_state = state;
                _ps_log_last = now;

                psLog(string.Format("state={0} sln.chk={1} cor.cnt={2} dg.cnt={3} fb={4} bldr={5}/{6}/{7}/{8} peak={9:F4} att={10} stim={11} hwpk={12:F4} tgt={13} appl={14}",
                    state,
                    puresignal.Info[6],
                    puresignal.Info[5],
                    puresignal.Info[13],
                    puresignal.Info[4],
                    puresignal.Info[0],
                    puresignal.Info[1],
                    puresignal.Info[2],
                    puresignal.Info[3],
                    puresignal.CurrentPeak,
                    console.SetupForm.ATTOnTX,
                    psStimulusName(),
                    psMeasuredPeak(),
                    puresignal.TargetFeedbackLevel,
                    puresignal.Info[14]));
            }
            catch { }
        }

        // The stimulus that is currently driving the TX.  The log has to name it, otherwise a
        // log of a noise run and a log of a two tone run cannot be told apart afterwards.
        private string psStimulusName()
        {
            try
            {
                if (console == null || console.IsSetupFormNull) return "none";
                if (!console.SetupForm.TTgenrun) return "none";
                return console.SetupForm.TestIMDNoiseStimulus ? "noise" : "twotone";
            }
            catch { return "?"; }
        }

        // The measured peak of the digital TX feedback (the "GetPk" box).  With the two tone it
        // reads about 1.0, with the noise it reads about the peak reference.
        private double psMeasuredPeak()
        {
            try
            {
                double peak = 0.0;
                unsafe { puresignal.GetPSMaxTX(_txachannel, &peak); }
                return peak;
            }
            catch { return 0.0; }
        }

        private void psLogClose()
        {
            try { if (_ps_log != null) { _ps_log.Flush(); _ps_log.Close(); _ps_log = null; } }
            catch { }
        }
        #region PSLoops

        private bool _performing_single_cal = false;
        private int _performing_single_cal_retries = 0;
        private void timer1code()
        {
            if (!_bPSRunning) return;

            puresignal.GetInfo(_txachannel);

            if (puresignal.HasInfoChanged)
            {
                psLogState();
                lblPSInfo0.Text = puresignal.Info[0].ToString();
                lblPSInfo1.Text = puresignal.Info[1].ToString();
                lblPSInfo2.Text = puresignal.Info[2].ToString();
                lblPSInfo3.Text = puresignal.Info[3].ToString();
                lblPSfb2.Text = puresignal.FeedbackLevel.ToString();
                lblPSInfo5.Text = puresignal.CalibrationCount.ToString();
                lblPSInfo6.Text = puresignal.Info[6].ToString();
                // Yurij-eu2av - 2026-07-08: highlight severe PureSignal over-drive (info[6] == 2).
                if (puresignal.Info[6] == 2)
                {
                    if (lblPSInfo6.BackColor != Color.Red)
                    {
                        lblPSInfo6.BackColor = Color.Red;
                        lblPSInfo6.ForeColor = Color.White;
                    }
                }
                else
                {
                    if (lblPSInfo6.BackColor != Color.Bisque)
                    {
                        lblPSInfo6.BackColor = Color.Bisque;
                        lblPSInfo6.ForeColor = Color.Black;
                    }
                }
                lblPSInfo13.Text = puresignal.Info[13].ToString();
                lblPSInfo15.Text = puresignal.Info[15].ToString();
            }

            // The "correcting" light has to go out when the PureSignal is switched off.
            // The engine only refreshes info[14] when a calibration completes, so the
            // light could stay green long after the OFF button was pressed.  The state of
            // the PureSignal itself is the authority here.
            if (puresignal.CorrectionsBeingApplied && PureSignalOn)
            {
                btnPSSave.Enabled = true;
                if (puresignal.Correcting)
                {
                    if (lblPSInfoCO.BackColor != Color.Lime)
                        lblPSInfoCO.BackColor = Color.Lime;
                }
                else
                {
                    if (lblPSInfoCO.BackColor != Color.Yellow)
                        lblPSInfoCO.BackColor = Color.Yellow;
                }
            }
            else
            {
                btnPSSave.Enabled = false;
                if (lblPSInfoCO.BackColor != Color.Black)
                    lblPSInfoCO.BackColor = Color.Black;
            }

            if (puresignal.CalibrationAttemptsChanged)
            {
                if (lblPSInfoFB.BackColor != puresignal.FeedbackColourLevel)
                    lblPSInfoFB.BackColor = puresignal.FeedbackColourLevel;
            }
            else
            {
                if (lblPSInfoFB.BackColor.R > 0 || lblPSInfoFB.BackColor.G > 0 || lblPSInfoFB.BackColor.B > 0) //MW0LGE_21k8
                {
                    //fade away
                    int r = Math.Max(0, lblPSInfoFB.BackColor.R - 5);
                    int g = Math.Max(0, lblPSInfoFB.BackColor.G - 5);
                    int b = Math.Max(0, lblPSInfoFB.BackColor.B - 5);
                    Color c = Color.FromArgb(r, g, b);
                    if (lblPSInfoFB.BackColor != c)
                        lblPSInfoFB.BackColor = c;
                }
            }

            // MW0LGE_21k9
            // The info bar readout must not be tied to the auto-calibrate mode: a Single Cal
            // switches the auto-calibrate mode off but the PureSignal stays on with its
            // correction applied, the readout has to stay alive.  It follows the PureSignal
            // itself, so it works in the auto-calibrate mode as well - and it goes dark
            // when the PureSignal is switched off.
            if (PureSignalOn)
            {
                if (puresignal.HasInfoChanged)
                {
                    console.InfoBarFeedbackLevel(puresignal.FeedbackLevel, puresignal.IsFeedbackLevelOK, puresignal.CorrectionsBeingApplied, puresignal.CalibrationAttemptsChanged, puresignal.FeedbackColourLevel);
                }
            }
            //
            unsafe
            {
                fixed (double* ptr = &_GetPSpeakval)
                    puresignal.GetPSMaxTX(_txachannel, ptr);
            }
            string s = _GetPSpeakval.ToString();
            if (GetPSpeak.Text != s) GetPSpeak.Text = s;

            // Command State-Machine
            switch (_cmdstate)
            {
                case eCMDState.OFF://0:     // OFF
                    puresignal.SetPSControl(_txachannel, 1, 0, 0, 0);
                    if (PSEnabled) PSEnabled = false;
                    btnPSCalibrate.BackColor = SystemColors.Control;
                    if (_restoreON)
                        _cmdstate = eCMDState.IntiateRestoredCorrection;// 7;
                    else if (_autoON)
                        _cmdstate = eCMDState.TurnOnAutoCalibrate;// 1;
                    else if (_singlecalON)
                        _cmdstate = eCMDState.TurnOnSingleCalibrate;// 3;
                    _OFF = false;
                    break;
                case eCMDState.TurnOnAutoCalibrate://1:     // Turn-ON Auto-Calibrate Mode
                    puresignal.SetPSControl(_txachannel, 1, 0, 1, 0);
                    if (!PSEnabled) PSEnabled = true;
                    btnPSCalibrate.BackColor = SystemColors.Control;
                    _cmdstate = eCMDState.AutoCalibrate;// 2;
                    break;
                case eCMDState.AutoCalibrate://2:     // Auto-Calibrate Mode
                    if (_OFF)
                        _cmdstate = eCMDState.TurnOFF;// 6;
                    else if (_restoreON)
                        _cmdstate = eCMDState.IntiateRestoredCorrection;// 7;
                    else if (_singlecalON)
                        _cmdstate = eCMDState.TurnOnSingleCalibrate;// 3;
                    break;
                case eCMDState.TurnOnSingleCalibrate://3:     // Turn-ON Single-Calibrate Mode
                    _autoON = false;
                    // A Single Cal *replaces* the auto-calibrate mode, it is not a request
                    // to switch the PureSignal off.  ForcePureSignalAutoCalDisable() sets
                    // _OFF (the PS form's "switch off" flag) when it unchecks PS-A, and the
                    // Single Cal button calls it - so the flag has to be cleared again here,
                    // otherwise the state machine turns the PS off (and drops the freshly
                    // calculated correction) as soon as the single cal is done.
                    _OFF = false;
                    _performing_single_cal = true;
                    puresignal.SetPSControl(_txachannel, 1, 1, 0, 0);
                    if (!PSEnabled) PSEnabled = true;
                    btnPSCalibrate.BackColor = Color.FromArgb(_gcolor);
                    _cmdstate = eCMDState.SingleCalibrate;// 4;
                    break;
                case eCMDState.SingleCalibrate://4:     // Single-Calibrate Mode
                    _singlecalON = false;
                    // While the single cal is running the PS stays on, the correction it
                    // produced must not be thrown away.
                    if (_OFF && !_performing_single_cal)
                        _cmdstate = eCMDState.TurnOFF;// 6;
                    else if (_restoreON)
                        _cmdstate = eCMDState.IntiateRestoredCorrection;// 7;
                    else if (_autoON)
                        _cmdstate = eCMDState.TurnOnAutoCalibrate;// 1;
                    else if (puresignal.CorrectionsBeingApplied)
                        _cmdstate = eCMDState.StayON;// 5;
                    break;
                case eCMDState.StayON://5:     // Stay-ON
                    if (PSEnabled) PSEnabled = false;
                    btnPSCalibrate.BackColor = SystemColors.Control;
                    // A Single Cal wins over the "off" flag, see TurnOnSingleCalibrate.
                    if (_singlecalON)
                        _cmdstate = eCMDState.TurnOnSingleCalibrate;// 3;
                    else if (_OFF)
                        _cmdstate = eCMDState.TurnOFF;// 6;
                    else if (_restoreON)
                        _cmdstate = eCMDState.IntiateRestoredCorrection;// 7;
                    else if (_autoON)
                        _cmdstate = eCMDState.TurnOnAutoCalibrate;// 1;
                    else if (_singlecalON)
                        _cmdstate = eCMDState.TurnOnSingleCalibrate;// 3;
                    else if (_performing_single_cal)
                    {
                        // fix for when we were performing a single cal, but needed to change attenuation
                        _performing_single_cal = false;
                        if (!puresignal.IsFeedbackLevelOKRange && _performing_single_cal_retries < 5)
                        {
                            _performing_single_cal_retries++;
                            _singlecalON = true;
                        }
                        else
                        {
                            _performing_single_cal_retries = 0;
                            stopSingleCalStimulus();    // the single cal is done
                        }
                    }
                    break;
                case eCMDState.TurnOFF://6:     // Turn-OFF
                    //_autoON = false;
                    if(!_autocal_enabled) _autoON = false; // only want to turn this off if autocal is off MW0LGE_21k9rc4
                    puresignal.SetPSControl(_txachannel, 1, 0, 0, 0);
                    if (!PSEnabled) PSEnabled = true;
                    btnPSCalibrate.BackColor = SystemColors.Control;
                    _OFF = false;
                    stopSingleCalStimulus();        // a single cal that was running is over
                    if (_restoreON)
                        _cmdstate = eCMDState.IntiateRestoredCorrection;// 7;
                    else if (_autoON)
                        _cmdstate = eCMDState.TurnOnAutoCalibrate;// 1;
                    else if (_singlecalON)
                        _cmdstate = eCMDState.TurnOnSingleCalibrate;// 3;
                    else if (!puresignal.CorrectionsBeingApplied && puresignal.State == puresignal.EngineState.LRESET)
                        _cmdstate = eCMDState.OFF;// 0;
                    break;
                case eCMDState.IntiateRestoredCorrection://7:     // Initiate Restored Correction
                    _autoON = false;
                    puresignal.SetPSControl(_txachannel, 0, 0, 0, 1);
                    if (!PSEnabled) PSEnabled = true;
                    btnPSCalibrate.BackColor = SystemColors.Control;
                    _restoreON = false;
                    if (puresignal.State == puresignal.EngineState.LSTAYON)
                        _cmdstate = eCMDState.StayON;//5;
                    break;
            }

            // The console's info bar shows the PureSignal state and the feedback level.
            // It follows the PureSignal, not the auto-calibrate mode, so it stays alive in
            // the auto-calibrate mode and after a Single Cal alike.
            bool psOn = PureSignalOn;

            // The OFF button is greyed out when the PureSignal is already off - that is the
            // indication that there is nothing left to switch off.
            btnPSReset.Enabled = psOn;

            if (psOn != _lastPSIndicated)
            {
                _lastPSIndicated = psOn;
                // The info bar has to go dark when the PureSignal is switched off, that is
                // the indication that the OFF button worked.
                console.InfoBarPSEnabled(psOn);
                try { psLog("EVENT puresignal " + (psOn ? "ON" : "OFF")); }
                catch { }
            }
        }
        private void timer2code()
        {
            if (!_bPSRunning) return;

            switch (_autoAttenuateState)
            {
                case eAAState.Monitor:// 0: // monitor
                    if (_autoattenuate && puresignal.CalibrationAttemptsChanged
                        && puresignal.NeedToRecalibrate(console.SetupForm.ATTOnTX))
                    {
                        if (!console.ATTOnTX) AutoAttenuate = true; //MW0LGE

                        _autoAttenuateState = eAAState.SetNewValues;//1;

                        double ddB;
                        if (puresignal.IsFeedbackLevelOK)
                        {
                            ddB = 20.0 * Math.Log10((double)puresignal.FeedbackLevel / (double)puresignal.TargetFeedbackLevel);
                            if (Double.IsNaN(ddB)) ddB = 31.1;
                            if (ddB < -100.0) ddB = -100.0;
                            if (ddB > +100.0) ddB = +100.0;
                        }
                        else
                            ddB = 31.1;

                        //_deltadB = Convert.ToInt32(ddB);
                        _deltadB = (int)Math.Round(ddB, MidpointRounding.AwayFromZero); //[2.10.3.12]MW0LGE use rounding, to fix Banker's rounding issue

                        _save_autoON = (_cmdstate == eCMDState.AutoCalibrate) ? 1 : 0; // (2)
                        _save_singlecalON = (_cmdstate == eCMDState.SingleCalibrate) ? 1 : 0; // (4)

                        // everything off and reset
                        puresignal.SetPSControl(_txachannel, 1, 0, 0, 0);
                        LogAutoAttenuate($"MONITOR FB={puresignal.FeedbackLevel} ATT={console.SetupForm.ATTOnTX} ddB={ddB:F2} delta={_deltadB} OK={puresignal.IsFeedbackLevelOKRange} {PSInfoBits()}");
                    }
                    break;
                case eAAState.SetNewValues:// 1: // set new values
                    _autoAttenuateState = eAAState.RestoreOperation;//2;
                    int newAtten;
                    int oldAtten = console.SetupForm.ATTOnTX;
                    if ((oldAtten + _deltadB) > 0)
                        newAtten = oldAtten + _deltadB;
                    else
                        newAtten = 0;
                    if (oldAtten/*console.SetupForm.ATTOnTX*/ != newAtten)
                    {
                        console.SetupForm.ATTOnTX = newAtten;
                        LogAutoAttenuate($"SETATT  old={oldAtten} new={newAtten} delta={_deltadB} FB={puresignal.FeedbackLevel} {PSInfoBits()}");

                        // give some additional time for the network msg to get to the radio before switching back on MW0LGE_21k9d5
                        if (m_bQuckAttenuate) Thread.Sleep(100);
                    }
                    else
                    {
                        LogAutoAttenuate($"SETATT  no change old={oldAtten} new={newAtten} delta={_deltadB} FB={puresignal.FeedbackLevel} {PSInfoBits()}");
                    }
                    break;
                case eAAState.RestoreOperation:// 2: // restore operation
                    _autoAttenuateState = eAAState.Monitor;//0;
                    puresignal.SetPSControl(_txachannel, 0, _save_singlecalON, _save_autoON, 0);
                    LogAutoAttenuate($"RESUME  FB={puresignal.FeedbackLevel} ATT={console.SetupForm.ATTOnTX} {PSInfoBits()}");
                    break;
            }
        }

        private static readonly object _psAutoAttenLogLock = new object();
        private string PSInfoBits()
        {
            return $"I0={puresignal.Info[0]} I1={puresignal.Info[1]} I2={puresignal.Info[2]} I3={puresignal.Info[3]} I6={puresignal.Info[6]} C={puresignal.CalibrationCount} A={puresignal.Info[7]}";
        }
        private void LogAutoAttenuate(string message)
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "PSAutoAttenuate.log");
                string line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
                lock (_psAutoAttenLogLock)
                {
                    File.AppendAllText(path, line);
                }
            }
            catch { }
        }
        #endregion

        private void PSpeak_TextChanged(object sender, EventArgs e)
        {
            bool bOk = double.TryParse(txtPSpeak.Text, out double tmp);

            if (bOk)
            {
                _PShwpeak = tmp;
                puresignal.SetPSHWPeak(_txachannel, _PShwpeak);

                //double set_pk = GetDefaultPeak();
                UpdateWarningSetPk();
            }                       
        }
        public void UpdateWarningSetPk()
        {
            pbWarningSetPk.Visible = _PShwpeak != HardwareSpecific.PSDefaultPeak; //[2.10.3.7]MW0LGE show a warning if the setpk is different to what we expect for this hardware
        }

        private void chkPSAutoAttenuate_CheckedChanged(object sender, EventArgs e)
        {
            AutoAttenuate = chkPSAutoAttenuate.Checked; //MW0LGE use property
        }

        private void checkLoopback_CheckedChanged(object sender, EventArgs e)
        {
            if(checkLoopback.Checked && (console.SampleRateRX1 != 192000 || console.SampleRateRX2 != 192000))
            {
                DialogResult dr = MessageBox.Show("This feature can only be used with sample rates set to 192KHz.",
                    "Sample Rate Issue",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Stop, MessageBoxDefaultButton.Button1, Common.MB_TOPMOST);

                checkLoopback.Checked = false;
                return;
            }
            cmaster.PSLoopback = checkLoopback.Checked;
        }

        private void udPSTargetFeedback_ValueChanged(object sender, EventArgs e)
        {
            puresignal.TargetFeedbackLevel = (int)udPSTargetFeedback.Value;
            // No _psFormReady guard here: if the form is not fully initialised the change
            // would be dropped and never reach the database.
            if (console != null && !console.IsSetupFormNull)
                console.SetupForm.PSTargetFeedbackLevel = (int)udPSTargetFeedback.Value;

            try { psLog("FB Target changed to " + udPSTargetFeedback.Value); }
            catch { }
        }

        // A NumericUpDown only pushes its text to the display once it has a window handle.
        // The values are restored before that happens, so the boxes would show whatever text
        // they had ("0" for a fresh control) while their value is correct.  Put the text back.
        private void SyncControlTexts()
        {
            try
            {
                if (udPSTargetFeedback != null && udPSTargetFeedback.Text != udPSTargetFeedback.Value.ToString())
                    udPSTargetFeedback.Text = udPSTargetFeedback.Value.ToString();

                if (udTestIMDFreq1 != null && udTestIMDFreq1.Text != udTestIMDFreq1.Value.ToString())
                    udTestIMDFreq1.Text = udTestIMDFreq1.Value.ToString();

                if (udTestIMDFreq2 != null && udTestIMDFreq2.Text != udTestIMDFreq2.Value.ToString())
                    udTestIMDFreq2.Text = udTestIMDFreq2.Value.ToString();
            }
            catch { }
        }

        private void udPSTargetFeedback_Validating(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // A NumericUpDown keeps the text that was typed until it parses.  A value
                // below the minimum (0) is never accepted, so the box would go on showing a
                // number that is not the one in use.  Put the real value back.
                if (udPSTargetFeedback.Text != udPSTargetFeedback.Value.ToString())
                    udPSTargetFeedback.Value = puresignal.TargetFeedbackLevel;

                SyncControlTexts();
            }
            catch { }
        }

        private void comboPSDeadlock_SelectedIndexChanged(object sender, EventArgs e)
        {
            // DL1BZ: adjustable WDSP 2.00+ deadlock minimum useful fraction.
            // Strict = WDSP default, Medium/Relaxed restore 1.29-style stability.
            double frac;
            switch (comboPSDeadlock.SelectedIndex)
            {
                case 1: frac = 0.04; break; // Medium
                case 2: frac = 0.02; break; // Relaxed
                default: frac = 0.06; break; // Strict
            }

            if (_psFormReady)
                puresignal.SetPSDeadlockMinFrac(_txachannel, frac);
        }

        private void chkPSOnTop_CheckedChanged(object sender, EventArgs e)
        {
            _topmost = chkPSOnTop.Checked;

            this.TopMost = _topmost; //MW0LGE
        }
        public void ShowAtStartup_LinearityForm()
        {
            this.Opacity = 0f;
            this.SetupForm();
            this.Show();
            Common.FadeIn(this);
        }
        public void ShowAtStartup_AmpViewForm()
        {
            btnPSAmpView_Click(this, EventArgs.Empty);
        }
        #endregion

        #region methods

        public void ForcePS()
        {
            EventArgs e = EventArgs.Empty;
            if (!_autoON)
            {
                puresignal.SetPSControl(_txachannel, 1, 0, 0, 0);
            }
            else
            {
                puresignal.SetPSControl(_txachannel, 0, 0, 1, 0);
            }
            if (!_ttgenON)
                WDSP.SetTXAPostGenRun(_txachannel, 0);
            else
            {
                // Re-apply the console's own test stimulus settings.  WDSP's default two tone
                // (900/1700Hz, wdsp/gen.c) must never be used: its 800Hz spacing is an exact
                // 1/120 of the 96kHz TX processing rate, so the digitised envelope repeats its
                // sample values and the PureSignal calibration then only sees a few repeated
                // levels (WDSP guide 2.11, Appendix D 13.3).
                if (console != null && !console.IsSetupFormNull)
                    console.SetupForm.RestartTestStimulus();
                else
                    WDSP.SetTXAPostGenRun(_txachannel, 0);
            }
            udPSCalWait_ValueChanged(this, e);
            udPSPhnum_ValueChanged(this, e);
            udPSMoxDelay_ValueChanged(this, e);
            chkPSAutoAttenuate_CheckedChanged(this, e);
            // The setup form owns the target feedback level.  It may not exist yet - do not
            // create it here, that would run its initialisation at the wrong point of the
            // console startup.
            if (console != null && !console.IsSetupFormNull)
            {
                udPSTargetFeedback.Value = console.SetupForm.PSTargetFeedbackLevel;
                udPSTargetFeedback_ValueChanged(this, e);
            }
            comboPSDeadlock_SelectedIndexChanged(this, e);
            chkPSOnTop_CheckedChanged(this, e);
            chkQuickAttenuate_CheckedChanged(this, e);
            chkShow2ToneMeasurements_CheckedChanged(this, e);
        }

        #endregion

        private void chkQuickAttenuate_CheckedChanged(object sender, EventArgs e)
        {
            QuickAttenuate = chkQuickAttenuate.Checked;
        }

        private void btnDefaultPeaks_Click(object sender, EventArgs e)
        {
            SetDefaultPeaks();
        }

        private void chkShow2ToneMeasurements_CheckedChanged(object sender, EventArgs e)
        {
            Display.ShowIMDMeasurments = chkShow2ToneMeasurements.Checked;
        }

        public void FixAmpViewOnTop()
        {
            if(ampv != null && !ampv.IsDisposed)
            {
                ampv.FixOnTop();
            }
        }
    }

    unsafe static class puresignal
    {
        #region DllImport - Main

        [DllImport("wdsp.dll", EntryPoint = "SetPSRunCal", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSRunCal(int channel, bool run);

        [DllImport("wdsp.dll", EntryPoint = "SetPSMox", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSMox(int channel, bool mox);

        [DllImport("wdsp.dll", EntryPoint = "GetPSInfo", CallingConvention = CallingConvention.Cdecl)]
        public static extern void GetPSInfo(int channel, int* info);

        [DllImport("wdsp.dll", EntryPoint = "SetPSReset", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSReset(int channel, int reset);

        [DllImport("wdsp.dll", EntryPoint = "SetPSMancal", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSMancal(int channel, int mancal);

        [DllImport("wdsp.dll", EntryPoint = "SetPSAutomode", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSAutomode(int channel, int automode);

        [DllImport("wdsp.dll", EntryPoint = "SetPSTurnon", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSTurnon(int channel, int turnon);

        [DllImport("wdsp.dll", EntryPoint = "SetPSControl", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSControl(int channel, int reset, int mancal, int automode, int turnon);

        [DllImport("wdsp.dll", EntryPoint = "SetPSLoopDelay", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSLoopDelay(int channel, double delay);

        [DllImport("wdsp.dll", EntryPoint = "SetPSMoxDelay", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSMoxDelay(int channel, double delay);

        [DllImport("wdsp.dll", EntryPoint = "SetPSDeadlockMinFrac", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSDeadlockMinFrac(int channel, double frac);

        [DllImport("wdsp.dll", EntryPoint = "SetPSTXDelay", CallingConvention = CallingConvention.Cdecl)]
        public static extern double SetPSTXDelay(int channel, double delay);

        [DllImport("wdsp.dll", EntryPoint = "PSSaveCorr", CallingConvention = CallingConvention.Cdecl)]
        public static extern void PSSaveCorr(int channel, string filename);

        [DllImport("wdsp.dll", EntryPoint = "PSRestoreCorr", CallingConvention = CallingConvention.Cdecl)]
        public static extern void PSRestoreCorr(int channel, string filename);

        [DllImport("wdsp.dll", EntryPoint = "SetPSHWPeak", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSHWPeak(int channel, double peak);

        [DllImport("wdsp.dll", EntryPoint = "GetPSHWPeak", CallingConvention = CallingConvention.Cdecl)]
        public static extern void GetPSHWPeak(int channel, double* peak);

        [DllImport("wdsp.dll", EntryPoint = "GetPSMaxTX", CallingConvention = CallingConvention.Cdecl)]
        public static extern void GetPSMaxTX(int channel, double* maxtx);

        [DllImport("wdsp.dll", EntryPoint = "GetPSDisp", CallingConvention = CallingConvention.Cdecl)]
        public static extern void GetPSDisp(int channel, IntPtr x, IntPtr ym, IntPtr yc, IntPtr ys, IntPtr xm_cor, IntPtr ym_cor, IntPtr xa_cor, IntPtr ya_cor, IntPtr nsamps_out, IntPtr cpts_out, IntPtr phs_ref_deg_out);

        [DllImport("wdsp.dll", EntryPoint = "SetPSFeedbackRate", CallingConvention = CallingConvention.Cdecl)]
        public static extern void SetPSFeedbackRate(int channel, int rate);

        #endregion

        #region public methods
        private static int[] _info = new int[16];
        private static int[] _oldInfo = new int[16];
        private static bool _bInvertRedBlue = false;
        private static int _targetFeedbackLevel = 152;
        public static int TargetFeedbackLevel
        {
            get { return _targetFeedbackLevel; }
            set
            {
                if (value < 1) value = 1;
                if (value > 256) value = 256;
                _targetFeedbackLevel = value;
            }
        }
        public static int FeedbackLevelGreenLow { get { return (int)(_targetFeedbackLevel * 0.7); } }
        public static int FeedbackLevelGreenHigh { get { return (int)(_targetFeedbackLevel * 1.3); } }
        static puresignal()
        {
            for(int i = 0; i < 16; i++)
            {
                _info[i] = 0;
                _oldInfo[i] = _info[i];
            }
        }
        public static int[] Info
        {
            get { return _info; }
        }
        public static void GetInfo(int txachannel)
        {
            //make copy of old, used in HasInfoChanged & CalibrationAttemptsChanged MW0LGE
            fixed (void* dest = &_oldInfo[0])
            fixed (void* src = &_info[0])
                Win32.memcpy(dest, src, 16 * sizeof(int));

            fixed (int* ptr = &(_info[0]))
                GetPSInfo(txachannel, ptr);
        }       
        public static bool HasInfoChanged 
        {
            get {
                for (int n = 0; n < 16; n++)
                {
                    if (_info[n] != _oldInfo[n])
                        return true;
                }
                return false;
            }
        }
        public static bool CalibrationAttemptsChanged {
            get { return _info[5] != _oldInfo[5]; }
        }
        public static bool CorrectionsBeingApplied {
            get { return _info[14] == 1; }
        }
        public static int CalibrationCount {
            get { return _info[5]; }
        }
        public static bool Correcting {
        // MW0LGE: the old threshold was a fixed 90.  That was 0.6 of the old target (152).
        // PureSignal 3 measures the feedback against a different reference and the target is
        // now 30, so a fixed number can never be reached again and the "correcting" light
        // stayed yellow for ever.  Scale it with the target.
        get { return FeedbackLevel > (int)(_targetFeedbackLevel * 0.6); }
        }
        public static bool NeedToRecalibrate(int nCurrentATTonTX) {
            // Yurij_eu2av: recalibrate if feedback is too high relative to target,
            // or too low while we still have attenuation headroom.
            return (FeedbackLevel > (int)(_targetFeedbackLevel * 1.5) ||
                    (FeedbackLevel < (int)(_targetFeedbackLevel * 0.7) && nCurrentATTonTX > 0));
        }
        public static bool IsFeedbackLevelOK {
            get { return FeedbackLevel <= 256; }
        }
        public static bool IsFeedbackLevelOKRange
        {
            get { return FeedbackLevel >= FeedbackLevelGreenLow && FeedbackLevel <= FeedbackLevelGreenHigh; }
        }
        public static int FeedbackLevel {
            get { return _info[4]; }
        }

        // The PureSignal peak reference (the "SetPk" box in the PureSignal form).  The
        // calibration normalises the TX envelope with it: the samples are binned into 16
        // envelope buckets over the range 0 .. peak.  A stimulus whose TX peak is well above
        // the reference puts all its samples into the top bucket, see
        // setup.cs applyTestStimulusGenerator().
        public static double CurrentPeak
        {
            get
            {
                double peak = 0.0;
                unsafe { GetPSHWPeak(WDSP.id(1, 0), &peak); }
                return peak;
            }
        }
        public static Color FeedbackColourLevel {
            get {
                int greenLow = FeedbackLevelGreenLow;
                int greenHigh = FeedbackLevelGreenHigh;
                int yellowLow = (int)(_targetFeedbackLevel * 0.5);
                int yellowHigh = (int)(_targetFeedbackLevel * 1.5);

                if (FeedbackLevel >= greenLow && FeedbackLevel <= greenHigh)
                    return Color.Lime;
                else if (FeedbackLevel >= yellowLow && FeedbackLevel <= yellowHigh)
                    return Color.Yellow;
                else if (FeedbackLevel > yellowHigh)
                {
                    if (_bInvertRedBlue) return Color.Red;
                    return Color.DodgerBlue;
                }
                else
                {
                    if (_bInvertRedBlue) return Color.DodgerBlue;
                    return Color.Red;
                }
            }
        }
        // info[15] is engine state (from calcc.c)
        public enum EngineState
        {
            LRESET = 0,
            LWAIT,
            LMOXDELAY,
            LSETUP,
            LCOLLECT,
            MOXCHECK,
            LCALC,
            LDELAY,
            LSTAYON,
            LTURNON
        };
        public static EngineState State {
            get { return (EngineState)_info[15]; }
        }

        public static bool InvertRedBlue
        {
            get { return _bInvertRedBlue; }
            set { _bInvertRedBlue = value; }
        }
        #endregion
    }
}
