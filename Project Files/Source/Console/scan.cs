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

//=================================================================

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;                    // ke9ns add for stringbuilder
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;      // ke9ns add for List<>
using System.Windows.Forms;

namespace Thetis
{
    public partial class ScanControl : System.Windows.Forms.Form
    {

        public static Console console;   // ke9ns mod  to allow console to pass back values to setup screen

        //   private ArrayList file_list;
        private string wave_folder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic) + "\\PowerSDR";


        private IContainer components;


        #region Constructor and Destructor

        public ScanControl(Console c)
        {
            InitializeComponent();
            console = c;

            Common.RestoreForm(this, "ScanForm", true);



        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (components != null)
                {
                    components.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        #endregion

        #region Properties



        #endregion

        #region Event Handlers







        #endregion




        //====================================================================================================
        int NamesTot = 0; // total number of unique Group names found in the memory list (no repeats)
        string[] Names = new string[1000]; // all the Group names found in the memory list (no repeats)

        StringBuilder sb = new StringBuilder();

        private void ScanControl_Load(object sender, EventArgs e)
        {

            // safeguard: clear any stale scan state left over from a previous session
            ScanRun = false;
            ScanPause = false;
            ScanStop = 0;
            ScanStop2 = 0;
            ScanRST = 0;
            scanstop = false;
            scanstop2 = false;
            SP5_Active = 0;
            ScanVFOB = false;
            ST2.Stop();
            ST2.Reset();
            ST3.Stop();
            ST3.Reset();
            btnBandstack.BackColor = SystemColors.ControlLight;
            btnGroupMemory.BackColor = SystemColors.ControlLight;
            btnCustomList.BackColor = SystemColors.ControlLight;
            pausebtn.BackColor = SystemColors.ControlLight;

            comboMemGroupName.DataSource = console.MemoryList.List; // upon loading, load up the current memory listing into the combobox
            comboMemGroupName.DisplayMember = "Group";
            comboMemGroupName.ValueMember = "Group";

            dataGridView2.DataSource = console.MemoryList.List;   // ke9ns get list of memories from memorylist.cs is where the file is opened and saved
            Debug.WriteLine("Rows Count " + dataGridView2.Rows.Count);
            memcount = dataGridView2.Rows.Count;

            for (int i = 0; i < memcount; i++) // find all the memories with the same group name
            {

                for (int y = 0; y < NamesTot; y++) // recheck all prior names found 
                {
                    if (dataGridView2[0, i].Value.ToString() == Names[y]) // check if index matches name
                    {
                        goto rt1;
                    }
                }

                comboBoxTS1.Items.Add(dataGridView2[0, i].Value.ToString());  // accumulate a combobox list of Group Memory names (no repeats)

                Names[NamesTot] = dataGridView2[0, i].Value.ToString(); // save new name for list
                NamesTot++;
                continue;

            rt1:
                Debug.WriteLine("Scanner Load groups: Found repeat ");

            } // for i loop

            // comboBoxTS1.DataSource = Names;

            UpdateBandScanRange(); // set up the low/high freq scan range for the current band

        } // ScanControl_Load

        private int band_index;
        public int xxx = 0; // used by currFBox_MouseUp row selection
        public static byte ScanStop = 1; // controlled from console 0=run, 1=stop
        public static byte ScanStop2 = 1; // controlled from console 0=run, 1=stop  .244

        public static byte ScanRST = 0; // 1= pick up where you left off, 0=reset back to low_freq

        public static bool ScanPause = false; // pause = true
        public static bool ScanRun = false; // run = true
        //=======================================================================================
        // ke9ns add    scan just the Band stacking reg 
        private void btnBandstack_Click(object sender, EventArgs e)
        {
            //  ST3.Stop();
            //  ST3.Reset();
            //  ScanPause = false;


            comboBoxTS1.Text = ""; //.288
            currFBox.Text = ""; //.288

            Gname = "";
            memcount = 0;

            ST3.Stop(); // .288
            ST3.Reset(); //.288
            ST2.Stop(); //.288
            ST2.Reset(); //.288
            ScanPause = false; //.288
            ScanRST = 0; // .288

            scanstop = false; //.288
            scanstop2 = false; //.288
            SP5_Active = 0; //.288 stop any scan


            if (ScanRun == true)
            {
                ScanRun = false; //.288
                                 //  scantype = 0; // .288 reset
                return;
            }

            if (ScanRun == false) // if stopped
            {
                try
                {
                    BandStackFilter bsf = BandStackManager.GetFilter(console.RX1Band);
                    if (bsf == null || bsf.NumberOfEntries < 1)
                    {
                        Debug.WriteLine("No bandstack for band " + console.RX1Band);
                        return;
                    }
                }
                catch (Exception q)
                {
                    Debug.WriteLine("Bandstack access " + q);
                    return;
                }

                ScanRun = true; // start up the scanner

                currFBox.Text = "";
                for (int y = 0; y < 100; y++)
                {
                    memsignal[y] = null;
                }



                UpdateText2();

                Thread t = new Thread(new ThreadStart(SCAN2));
                t.Name = "Bandstack memory Scanner Thread";
                t.IsBackground = true;
                t.Priority = ThreadPriority.Normal;
                t.Start();

            }
            else
            {
                ScanRun = false; // turn off scanner

            }

        } // btnBandstack_Click


        //========================================================================================
        //  BANDSTACK CURRFBOX text update
        void UpdateText2()
        {
            BandStackFilter bsf = BandStackManager.GetFilter(console.RX1Band);
            if (bsf == null)
            {
                currFBox.Text = "";
                return;
            }

            List<BandStackEntry> entries = bsf.Entries;

            string temp1 = "";

            memtotal = 0;

            for (int ii = 0; ii < entries.Count; ii++)
            {
                BandStackEntry bse = entries[ii];

                string freq3 = bse.Frequency.ToString("###0.000000"); // MHz

                string name = console.RX1Band.ToString();

                string mm = "BandStack Memories";

                if (memsignal[ii] == null) memsignal[ii] = " ";

                temp1 = temp1 + (memtotal + 1).ToString().PadLeft(2) + ": " + mm.PadRight(20).Substring(0, 20) + ", " + freq3.PadLeft(12).Substring(0, 12) + ", " + name.PadRight(20).Substring(0, 20) + ", " + memsignal[memtotal].PadRight(20).Substring(0, 20) + "\r\n"; // 74 char long

                memIndex[memtotal] = ii;
                memtotal++;

            } // for


            currFBox.Text = temp1;

        } // UpdateText2()  BANDSTACK 

        //===========================================================================
        // Thread bandstack scanner
        private void SCAN2()
        {

            scantype = 2;

            ST2.Reset();
            ST3.Reset();

            Debug.WriteLine("SCANSTOP = " + ScanStop);
            btnBandstack.BackColor = Color.LightGreen;

            int lastSIG = 0;
            int lastSQL = 0;


            Debug.WriteLine("CONSOLE BAND " + console.RX1Band);


            band_index = 0; // start at first entry of current bandstack

            do // ScanRun
            {

                for (; ; )
                {

                    Thread.Sleep(50);

                    try
                    {
                        speed = (int)udspeedBox.Value;  // Convert.ToInt16(udspeedBox.Text);
                        Debug.WriteLine("SPEED " + speed);
                    }
                    catch (Exception)
                    {
                        speed = 50; // 50msec
                    }


                    incbandstack(); // go to next bandstack memory

                    currFBox.SelectionStart = band_index * linelength; // i * linelength
                    currFBox.SelectionLength = linelength;
                    currFBox.ScrollToCaret(); // keep highlighted line visable

                    ST2.Restart(); // restart timer over again

                    ScanStop = 0; // reset squelch

                    lastSIG = -400;
                    lastSQL = -400;
                    //-------------------------------------------------------
                    // timer
                    do // scan speed and scanPause
                    {
                        Thread.Sleep(50);

                        if (ScanPause == true) pausebtn.BackColor = Color.Yellow;
                        else pausebtn.BackColor = SystemColors.ControlLight;

                        if (ScanRun == false)
                        {
                            ScanPause = false;
                            goto RT2;
                        }

                        if (SIG > lastSIG)
                        {
                            lastSIG = SIG;
                        }

                        if (SQL > lastSQL)
                        {
                            lastSQL = SQL;
                        }

                        if (scanstop == true)
                        {
                            scanstop = false;  // reset
                            goto RT2;

                        }
                        if ((ScanStop == 1)) // if console detected squelch open
                        {
                            memsignal[band_index] = lastSQL.ToString().PadLeft(4) + ", " + lastSIG.ToString().PadLeft(4) + ", Squelch Break";

                            if ((chkBoxSQLBRK.Checked == true) && (ScanPause == false)) // if stop on squelch break, then stop now
                            {
                                ScanPause = true;
                                UpdateText2(); // update currFBox text

                                currFBox.SelectionStart = band_index * linelength; // i * linelength
                                currFBox.SelectionLength = linelength;

                                if (udPauseLength.Value > 0)
                                {
                                    Debug.WriteLine("ST3 TIMER STARTED ");
                                    ST3.Restart(); // start the pause timer
                                }
                                else
                                {
                                    ScanPause = false;
                                    ScanRun = false;
                                    goto RT2;

                                }
                                break;

                            }
                            else if ((chkBoxSQLBRKWait.Checked == true) && (ScanPause == false)) // if stop on squelch break, then stop now
                            {
                                ScanPause = true;
                                UpdateText2(); // update currFBox text

                                currFBox.SelectionStart = band_index * linelength; // i * linelength
                                currFBox.SelectionLength = linelength;

                                if (udPauseLength.Value > 0)
                                {
                                    Debug.WriteLine("ST3 TIMER STARTED ");
                                    ST3.Restart(); // start the pause timer
                                }
                                else
                                {
                                    ScanPause = false;
                                    ScanRun = false;
                                    goto RT2;

                                }
                                break;

                            }
                        }
                        else
                        {
                            memsignal[band_index] = lastSQL.ToString().PadLeft(4) + ", " + lastSIG.ToString().PadLeft(4) + ", ";
                        }
                        if (scanstop == true)
                        {
                            scanstop = false;  // reset
                            goto RT2;

                        }
                    } while ((ST2.ElapsedMilliseconds < speed) || (ScanPause == true));

                    //-------------------------------------------------------

                    UpdateText2(); // update currFBox text

                    currFBox.SelectionStart = band_index * linelength; // i * linelength
                    currFBox.SelectionLength = linelength;

                    if (SP5_Active == 1)
                    {
                        ScanRun = false;
                        ScanPause = false;
                        Debug.WriteLine("SCANSTOP, another scanner started");
                        break;
                    }


                    //-------------------------------------------------------
                    // CHECK For PAUSE
                    while (ScanPause == true)  // wait here in in pause
                    {

                        Thread.Sleep(50);

                        if (ScanRun == false)
                        {
                            Debug.WriteLine("SCANSTOP, Group scanner turned back off");
                            ScanPause = false;
                            break;
                        }

                        if (ST3.ElapsedMilliseconds > ((long)udPauseLength.Value * 1000))
                        {
                            ST3.Stop(); // stop the pause timer

                            if (chkBoxSQLBRKWait.Checked == true && ScanStop == 1)
                            {
                                ScanPause = true;
                                ScanStop = 0;
                                ST3.Restart();

                            }
                            else ScanPause = false;

                            Debug.WriteLine("ST3 TIMER REACHED PAUSELENGTH ");
                        }

                        if (ScanPause == true) pausebtn.BackColor = Color.Yellow;
                        else pausebtn.BackColor = SystemColors.ControlLight;

                    };

                    pausebtn.BackColor = SystemColors.ControlLight;


                } // FOR ;; loop

            } while (ScanRun == true); // ScanStopped so leave thread


        RT2: ST2.Stop();
            ST3.Stop();
            Debug.WriteLine("SCANTOP0"); // scanner done
            btnBandstack.BackColor = SystemColors.ControlLight;
            pausebtn.BackColor = SystemColors.ControlLight;
            //   scantype = 0;

        } // SCAN2   BANDSTACK memory scanner





        //================================================================================================
        // increment through the bandstack (as though you were clicking on the same band button over and over
        public void incbandstack()
        {
            BandStackFilter bsf = BandStackManager.GetFilter(console.RX1Band);
            if (bsf == null) return;

            int count = bsf.NumberOfEntries;
            if (count < 1) return;

            band_index = (band_index + 1) % count;

            BandStackEntry bse = bsf.EntryByIndex(band_index);
            if (bse == null) return;

            console.SetBand(bse.Mode.ToString(), bse.Filter.ToString(), bse.Frequency);

            console.UpdateWaterfallLevelValues();

        }  // incbandstack




        //=======================================================================================================================
        private void ScanControl_FormClosing(object sender, FormClosingEventArgs e)
        {

            Debug.WriteLine("==========CLOSING SCANNER============");

            ScanRun = false;
            ScanPause = false;
            ScanStop = 1;

            this.Hide();
            e.Cancel = true;
            Common.SaveForm(this, "ScanForm");
            //  console.MemoryList.Save();



        } // ScanControl_FormClosing


        MemoryRecord[] m1 = new MemoryRecord[1000];

        int[] memIndex = new int[1000]; // holder for memories that match the group name
        int memtotal = 0; // total matching group name memories found
        int memcount = 0; // total memories found
        string[] memsignal = new string[1000]; // db signal and sql brk

        bool ScanVFOB = false; // .236

        //=======================================================================================================================
        // Group memory scanner. Scanning only frequencies in 1 group name
        private void btnGroupMemory_Click(object sender, EventArgs e)
        {
            //   ST3.Stop();
            //   ST3.Reset();


            ST3.Stop(); // .288
            ST3.Reset(); //.288
            ST2.Stop(); //.288
            ST2.Reset(); //.288
            ScanPause = false; //.288
            ScanRST = 0; // .288
            scanstop = false; //.288
            scanstop2 = false; //.288
            SP5_Active = 0; //.288 stop any scan


            if (ScanRun == true)
            {
                ScanRun = false; //.288
                                 //  scantype = 0; // .288 reset

                return;
            }


            if (ScanVFOB == true) //.244
            {
                ScanRun = false;
                return;
            }

            ScanPause = false;

            if (console.MemoryList.List.Count == 0) return; // nothing in the list, exit
            if (comboMemGroupName.Items.Count == 0) return;
            memcount = comboMemGroupName.Items.Count; // total number of memories listed

            Debug.WriteLine("memory list7 " + memcount);

            if (ScanRun == false) // if stoppedchange
            {
                currFBox.Text = "";

                for (int y = 0; y < 100; y++)
                {
                    memsignal[y] = null;
                }

                UpdateText();  // upate currFBox text

                ScanRun = true; // start up the scanner

                Thread t = new Thread(new ThreadStart(SCAN1));
                t.Name = "Group memory Scanner Thread";
                t.IsBackground = true;
                t.Priority = ThreadPriority.Normal;
                t.Start();

            }
            else
            {
                ScanRun = false; // turn off scanner
            }

        } // button6_Click


        MemoryRecord recordToRestore; // holder to select group name
        string Gname; // name of group of memories to scan

        //==========================================================================================
        // ke9ns combobox to display ALL the group names from the memory listing
        private void comboMemGroupName_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboMemGroupName.Items.Count == 0 || comboMemGroupName.SelectedItem == null) return;

            recordToRestore = new MemoryRecord((MemoryRecord)comboMemGroupName.SelectedItem); // ke9ns   you select index in the combo pulldown list


        } //  comboMemGroupName_SelectedIndexChanged


        //==========================================================================================
        // ke9ns combobox to display all the unique (SUB) group names from the memory listing
        private void comboBoxTS1_SelectedIndexChanged(object sender, EventArgs e)
        {
            ScanPause = false; // runs Thread Scan1  memory scan
            ScanRun = false;
            scantype = 1;


            if (comboBoxTS1.Text == "")
            {
                Gname = "";
                memcount = 0;
                scantype = 0; // .288 reset
                ST3.Stop(); // .288
                ST3.Reset(); //.288
                ST2.Stop(); //.288
                ST2.Reset(); //.288
                ScanPause = false; //.288
                ScanRST = 0; // .288

                currFBox.Text = ""; //.288
                ScanRun = false; //.288
                scanstop = false; //.288
                scanstop2 = false; //.288
                SP5_Active = 0; //.288 stop any scan

                return;
            }


            Debug.WriteLine("[[[[[[[[[[[[[[[[[[[COMBOBOX EVENT]]]]]]]]]]]]]]]");

            //  if (comboBoxTS1.Items.Count == 0 || comboBoxTS1.SelectedItem == null) return;
            if (console.initializing == true) return;

            //  Gname = comboBoxTS1.SelectedItem.ToString();
            Gname = comboBoxTS1.Text;

            Debug.WriteLine("SELECTED GROUP NAME " + Gname);

            if (console.MemoryList.List.Count == 0) return; // nothing in the list, exit
            if (comboMemGroupName.Items.Count == 0) return;

            memcount = comboMemGroupName.Items.Count; // total number of memories listed

            Debug.WriteLine("memory list8 " + memcount);


            UpdateText(); // upate currFBox text

            btnGroupMemory.Enabled = true;

            Debug.WriteLine("memory list8a " + memcount);

            Debug.WriteLine("memory list8b " + memcount);

        } // comboBoxTS1_SelectedIndexChanged


        int linelength = 84; // length of a line in the currFbox
        //========================================================================================
        //  Lookup MEMORY table and match user input to the MEMORY list and update currFBox text screen
        void UpdateText()
        {

            memtotal = 0;

            string temp1 = "";

            Debug.WriteLine("UPDATE LIST " + Gname);

            for (int i = 0; i < memcount; i++) // find all the memories with the same group name
            {

                //  if ( (dataGridView2[0, i].Value.ToString()).Equals(Gname, StringComparison.InvariantCultureIgnoreCase) == true) // check if index matches name
                //    if (dataGridView2[0, i].Value.ToString() == Gname) // check if index matches name

                if (CultureInfo.InvariantCulture.CompareInfo.IndexOf((dataGridView2[0, i].Value.ToString()), Gname, CompareOptions.IgnoreCase) >= 0) // Gname must be contains in MEMORY (partial or full) and case insensitive)
                {
                    bool scan = (bool)dataGridView2["Scan", i].Value; // ke9ns add .155  

                    double hh = Convert.ToDouble(dataGridView2[1, i].Value);  // MEMORY "RXFREQ"  convert to hz
                    string freq = hh.ToString("###0.000000");    //  freq of memory  dataGridView2[1, i].Value.ToString();
                    string name = dataGridView2[2, i].Value.ToString(); // name of memory
                    string mm = dataGridView2[0, i].Value.ToString();  // GROUP of MEMORY

                    //  string comment = dataGridView2["comments", i].Value.ToString(); // comments of memory
                    //  int hh = (int)(Convert.ToDouble(SpotForm.dataGridView2[1, ii].Value) * 1000000);  // MEMORY "RXFREQ"  convert to hz
                    // string ll = (string)SpotForm.dataGridView2[2, holder2[ii]].Value;  // Name of MEMORY
                    //  DSPMode nn = (DSPMode)SpotForm.dataGridView2[3, holder2[ii]].Value;  // DSPMODE of MEMORY

                    //  Debug.WriteLine("UPDATE LIST A ");

                    if (memsignal[memtotal] == null) memsignal[memtotal] = " ";

                    //   Debug.WriteLine("UPDATE LIST B " + memsignal[memtotal]);

                    //  bool scan = (bool)dataGridView2["Scan", i].Value; // ke9ns add .155  

                    string Y;
                    if (scan == true) Y = " "; // .155
                    else Y = "X";

                    temp1 = temp1 + (memtotal + 1).ToString().PadLeft(2) + ":" + Y + " " + mm.PadRight(20).Substring(0, 20) + ", " + freq.PadLeft(12).Substring(0, 12) + ", " + name.PadRight(20).Substring(0, 20) + ", " + memsignal[memtotal].PadRight(19).Substring(0, 19) + "\r\n"; // 74 char long

                    memIndex[memtotal] = i;
                    memtotal++;
                    //  Debug.WriteLine("Found Group name match at index " + i);
                }

            } // for i loop

            currFBox.Text = temp1;

        } // UpdateText()  MEMORY 


        public static int SQL = 0;  // updated by console routine picSquelch_Paint
        public static int SQL2 = 0; //.244 2nd receiver

        public static int SIG = 0;
        public static int SIG2 = 0; // .244

        Stopwatch ST2 = new Stopwatch();
        Stopwatch ST3 = new Stopwatch();

        int scantype = 0;  // 1=memory, 2=band stack, 3= custom, 4= low-high
        bool scanstop = false;
        bool scanstop2 = false; // .244 2nd rx

        //==========================================================================================
        // thread scans selected "Memory" Group name frequencies only
        private void SCAN1()
        {
            ST2.Reset();
            ST3.Reset();

            scantype = 1;

            Debug.WriteLine("SCANSTOP = " + ScanStop);
            btnGroupMemory.BackColor = Color.LightGreen;

            if (ScanVFOB == true)
            {
                btnGroupMemory.Text = "Memory Scan (RX2)";
            }
            else
            {
                btnGroupMemory.Text = "Memory Scan (RX)";
            }

            int lastSIG = 0;
            int lastSQL = 0;

            int lastSQL2 = 0; //.244 2nd rx
            int lastSIG2 = 0; //.244

            ST3.Reset();

            int x = 0;

            do // ScanRun
            {
                if (ScanVFOB == true) //.244
                {
                    if (scanstop2 == true)
                    {
                        scanstop2 = false;  // reset
                        goto RT2;

                    }
                }
                else
                {
                    if (scanstop == true)
                    {
                        scanstop = false;  // reset
                        goto RT2;

                    }
                }

                Debug.WriteLine("START OF LOOP");

                for (x = 0; x < memtotal; x++) // go through list of MEMORIES you found
                {
                    Thread.Sleep(50);

                    try
                    {
                        speed = (int)udspeedBox.Value;  // Convert.ToInt16(udspeedBox.Text);
                        Debug.WriteLine("SPEED " + speed);
                    }
                    catch (Exception)
                    {
                        speed = 50; // 50msec
                    }


                    comboMemGroupName.SelectedIndex = memIndex[x];
                    recordToRestore = new MemoryRecord((MemoryRecord)comboMemGroupName.SelectedItem); // ke9ns   you select index in the combo pulldown list

                    if (recordToRestore.Scan == false) continue; // ke9ns add .155

                    Debug.WriteLine("CHANGE MEMORY TO " + recordToRestore.RXFreq);

                    if (ScanVFOB == true) //.244
                    {
                        console.RecallMemoryB(recordToRestore); // 2nd RX
                    }
                    else
                    {
                        console.RecallMemory(recordToRestore);
                    }


                    currFBox.SelectionStart = x * linelength; // i * linelength
                    currFBox.SelectionLength = linelength;
                    currFBox.ScrollToCaret(); // keep highlighted line visable

                    ST2.Restart(); // restart timer over again

                    ScanStop = 0; // reset squelch
                    ScanStop2 = 0; // reset squelch

                    lastSIG = -400;
                    lastSQL = -400;

                    lastSIG2 = -400; //.244
                    lastSQL2 = -400;

                    //-------------------------------------------------------
                    // SPEED TIMER and PAUSE
                    do
                    {
                        Thread.Sleep(50);



                        if (ScanVFOB == true) //.244
                        {
                            if (scanstop2 == true)
                            {
                                scanstop2 = false;  // reset
                                goto RT2;

                            }
                        }
                        else
                        {
                            if (scanstop == true)
                            {
                                scanstop = false;  // reset
                                goto RT2;

                            }
                        }

                        if (ScanPause == true) pausebtn.BackColor = Color.Yellow;
                        else pausebtn.BackColor = SystemColors.ControlLight;

                        if (ScanRun == false)
                        {
                            ScanPause = false;
                            goto RT2;  // turn off this thread now
                        }

                        if (ScanVFOB == true) //.244
                        {

                            if (SIG2 > lastSIG2)  // CHECK SQUELCH and SIGNAL levels
                            {
                                lastSIG2 = SIG2;
                            }

                            if (SQL2 > lastSQL2)
                            {
                                lastSQL2 = SQL2;
                            }
                        }
                        else
                        {
                            if (SIG > lastSIG)  // CHECK SQUELCH and SIGNAL levels
                            {
                                lastSIG = SIG;
                            }

                            if (SQL > lastSQL)
                            {
                                lastSQL = SQL;
                            }

                        }

                        if (((ScanStop == 1) && (ScanVFOB == false)) || ((ScanStop2 == 1) && (ScanVFOB == true))) // if console detected squelch open .244 mod
                        {
                            if (ScanVFOB == true) //.244
                            {
                                memsignal[x] = lastSQL2.ToString().PadLeft(4) + ", " + lastSIG2.ToString().PadLeft(4) + ", SQL BRK";
                            }
                            else
                            {
                                memsignal[x] = lastSQL.ToString().PadLeft(4) + ", " + lastSIG.ToString().PadLeft(4) + ", SQL BRK";
                            }

                            if ((chkBoxSQLBRK.Checked == true) && (ScanPause == false)) // if stop on squelch break, then stop now
                            {
                                ScanPause = true;

                                UpdateText(); // update currFBox text

                                currFBox.SelectionStart = x * linelength; // i * linelength
                                currFBox.SelectionLength = linelength;

                                if (udPauseLength.Value > 0)
                                {
                                    Debug.WriteLine("ST3 TIMER STARTED " + memtotal);
                                    ST3.Restart(); // start the pause timer
                                }
                                else // if 0
                                {
                                    ScanPause = false;
                                    ScanRun = false;
                                    goto RT2;
                                }

                                break; // break out of the while loop
                            }
                            else if ((chkBoxSQLBRKWait.Checked == true) && (ScanPause == false)) // if stop on squelch break, then stop now
                            {
                                ScanPause = true;

                                UpdateText(); // update currFBox text

                                currFBox.SelectionStart = x * linelength; // i * linelength
                                currFBox.SelectionLength = linelength;

                                if (udPauseLength.Value > 0)
                                {
                                    Debug.WriteLine("ST3 TIMER STARTED " + memtotal);
                                    ST3.Restart(); // start the pause timer

                                }
                                else // if 0
                                {
                                    ScanPause = false;
                                    ScanRun = false;
                                    goto RT2;
                                }
                                break; // break out of the while loop

                            }


                        } // ScanStop == 1
                        else
                        {
                            if (ScanVFOB == true) //.244
                            {
                                memsignal[x] = lastSQL2.ToString().PadLeft(4) + ", " + lastSIG2.ToString().PadLeft(4) + ", ";
                            }
                            else
                            {
                                memsignal[x] = lastSQL.ToString().PadLeft(4) + ", " + lastSIG.ToString().PadLeft(4) + ", ";
                            }
                        }
                        if (ScanVFOB == true) //.244
                        {
                            if (scanstop2 == true)
                            {
                                scanstop2 = false;  // reset
                                goto RT2;

                            }
                        }
                        else
                        {
                            if (scanstop == true)
                            {
                                scanstop = false;  // reset
                                goto RT2;

                            }
                        }

                    } while ((ST2.ElapsedMilliseconds < speed) || (ScanPause == true));

                    //-----------------------------------------------------BREAK comes here--

                    UpdateText(); // update currFBox text

                    currFBox.SelectionStart = x * linelength; // i * linelength
                    currFBox.SelectionLength = linelength;

                    if (ScanVFOB == true) //.244
                    {
                        if (scanstop2 == true)
                        {
                            scanstop2 = false;  // reset
                            goto RT2;

                        }
                    }
                    else
                    {
                        if (scanstop == true)
                        {
                            scanstop = false;  // reset
                            goto RT2;

                        }
                    }

                    if (SP5_Active == 1)
                    {
                        ScanRun = false;
                        ScanPause = false;
                        Debug.WriteLine("SCANSTOP, another scanner started");
                        break;
                    }


                    //-------------------------------------------------------
                    // CHECK For PAUSE
                    while (ScanPause == true)  // wait here in in pause
                    {
                        Thread.Sleep(50);

                        if (ScanVFOB == true) //.244
                        {
                            if (scanstop2 == true)
                            {
                                scanstop2 = false;  // reset
                                goto RT2;

                            }
                        }
                        else
                        {
                            if (scanstop == true)
                            {
                                scanstop = false;  // reset
                                goto RT2;

                            }
                        }

                        if (ScanRun == false)
                        {
                            Debug.WriteLine("SCANSTOP, Group scanner turned back off");
                            ScanPause = false; //.219
                            scanstop = true;
                            scanstop2 = true;
                            break;
                        }

                        if (ST3.ElapsedMilliseconds > ((long)udPauseLength.Value * 1000))
                        {
                            ST3.Stop(); // stop the pause timer
                            if (chkBoxSQLBRKWait.Checked == true && ((ScanStop == 1 && ScanVFOB == false) || (ScanStop2 == 1 && ScanVFOB == true)))
                            {
                                ScanPause = true;
                                ScanStop = 0;
                                ScanStop2 = 0; //.244

                                ST3.Restart();

                            }
                            else ScanPause = false;

                            Debug.WriteLine("ST3 TIMER REACHED PAUSELENGTH " + memtotal);
                        }

                        if (ScanPause == true) pausebtn.BackColor = Color.Yellow;
                        else pausebtn.BackColor = SystemColors.ControlLight;

                    }; //  while (ScanPause == true)  // wait here in in pause


                    pausebtn.BackColor = SystemColors.ControlLight;

                    Debug.WriteLine("END OF LOOP " + memtotal + " , " + x);

                    if (ScanVFOB == true) //.244
                    {
                        if (scanstop2 == true)
                        {
                            scanstop2 = false;  // reset
                            goto RT2;

                        }
                    }
                    else
                    {
                        if (scanstop == true)
                        {
                            scanstop = false;  // reset
                            goto RT2;

                        }
                    }

                } // for (x = 0; x < memtotal; x++)   FOR memtotal loop



                Debug.WriteLine("END OF LOOP1 " + memtotal + " , " + x);
                if (ScanVFOB == true) //.244
                {
                    if (scanstop2 == true)
                    {
                        scanstop2 = false;  // reset
                        goto RT2;

                    }
                }
                else
                {
                    if (scanstop == true)
                    {
                        scanstop = false;  // reset
                        goto RT2;

                    }
                }
            } while (ScanRun == true); // ScanStopped so leave thread

        RT2:
            btnGroupMemory.Text = "Memory Scan (RX)";

            Debug.WriteLine("SCANTOP0"); // scanner done
            ST2.Stop();
            ST3.Stop();

            btnGroupMemory.BackColor = SystemColors.ControlLight;

            ScanStop = 1;
            ScanStop2 = 1; //.244
            ScanRun = false;
            //   scantype = 0;

        } // SCAN1()






        //===========================================================================================

        public static byte SP5_Active = 0; // ke9ns: 1= running, 0=off
        double freq1 = 0.0;
        public static double freq_Low = 0.0; // ke9ns low and high get automatically filled by the console.cs routine as the band changes
        public static double freq_High = 0.0;

        public static double freq_Low1 = 0.0; // ke9ns low and high get automatically filled by the console.cs routine as the band changes
        public static double freq_High1 = 0.0;// but these cannot be changed by the user

        public static double freq_Last = 0.0;

        public static string[] SLowScan = new string[(int)Band.LAST]; // ke9ns .186: save low/high of scanner for each band
        public static string[] SHighScan = new string[(int)Band.LAST];



        // FREQ SCANNER BUTTON
        private void button5_Click(object sender, EventArgs e)
        {
            ST3.Stop();
            ST3.Reset(); // shut down memory scan


            Debug.WriteLine("click    ");

            ScanStop = 0; // reset scan

            if (SP5_Active == 0)
            {

                SP5_Active = 1;

                // see console routine  if (rx1_band != old_band || initializing) for setting low and high settings

                Thread t = new Thread(new ThreadStart(SCANNER));


                t.Name = "Scanner Thread";
                t.IsBackground = true;
                t.Priority = ThreadPriority.Normal;
                t.Start();

                Trace.WriteLine("good    ");

            } // SP_active = 0;
            else
            {

                SP5_Active = 0;
                Trace.WriteLine("OFF   ");

            } // SP_Active = 1




        } // button5_Click



        //===============================================================================
        //===============================================================================
        // ke9ns SCANNER thread from low to high frequency selected on the scanner panel

        double step = 0.0001;
        int speed = 50;


        private async void SCANNER()
        {

            Stopwatch x1 = new Stopwatch();

        LOOP1: scantype = 4;

            freq1 = freq_Low;

            try
            {
                step = (double)udstepBox.Value;   //Convert.ToDouble(udstepBox.Text) / 1000;
                step = step / 1000; // convert to KHZ
            }
            catch (Exception)
            {
                step = 0.0001; // 1 khz
            }

            try
            {
                speed = (int)udspeedBox1.Value;  // Convert.ToInt16(udspeedBox.Text);

            }
            catch (Exception)
            {
                speed = 50; // 50msec
            }

            Debug.WriteLine("Scanner STEP " + step + " ,SPEED " + speed + " ,LOW " + freq_Low + " ,HIGH " + freq_High + " ,RST " + ScanRST + " ,LAST " + freq_Last);



            //   Trace.WriteLine("good1   ");

            double ii = freq_Low;

            if (ScanRST == 1)
            {
                ii = freq_Last;
            }


            for (; ii <= freq_High; ii = ii + step)
            {

                currFBox.Text = ii.ToString("f6");

                x1.Restart();

                console.VFOAFreq = ii; // convert to MHZ

                for (int x9 = 0; x9 < 10; x9++) // divide up step time into 10 parts, so I can use await timer
                {
                    if (chkBoxSQLBRK.Checked == true)
                    {
                        if (ScanStop == 1) // if console says squelch break
                        {
                            if (udPauseLength.Value == 0)
                            {
                                break; // break out of the delay loop
                            }
                            else
                            {
                                if (SP5_Active == 0) break;
                                Debug.WriteLine("PAUSE " + x9);
                                pausebtn.BackColor = Color.Yellow;
                                await Task.Delay((int)udPauseLength.Value * 1000).ConfigureAwait(false); // pause
                                ScanStop = 0;
                                pausebtn.BackColor = SystemColors.ControlLight;
                                break;
                            }
                        }
                    }
                    else if (chkBoxSQLBRKWait.Checked == true)
                    {
                        if (ScanStop == 1) // if console says squelch break
                        {
                            if (udPauseLength.Value == 0)
                            {
                                break; // break out of the delay loop
                            }
                            else
                            {
                                if (SP5_Active == 0) break;
                                Debug.WriteLine("PAUSE " + x9);
                                pausebtn.BackColor = Color.Yellow;
                                await Task.Delay((int)udPauseLength.Value * 1000).ConfigureAwait(false); // pause
                                ScanStop = 0;
                                pausebtn.BackColor = SystemColors.ControlLight;

                            }
                        }
                    }
                    await Task.Delay(speed / 10).ConfigureAwait(false);

                } // 10x loop

                if (chkBoxSQLBRK.Checked == true)
                {
                    if (ScanStop == 1) // if console says squelch break
                    {
                        if (udPauseLength.Value == 0)
                        {
                            ScanStop = 0;
                            break; // break out of the freq scanner loop
                        }
                    }
                }
                else if (chkBoxSQLBRKWait.Checked == true)
                {
                    if (ScanStop == 1) // if console says squelch break
                    {
                        if (udPauseLength.Value == 0)
                        {
                            ScanStop = 0;
                            break; // break out of the freq scanner loop
                        }
                    }
                }


                x1.Stop();

                Debug.WriteLine("TIME " + x1.ElapsedMilliseconds);

                if (SP5_Active == 0) break;

            } // for loop


            Debug.WriteLine("Scanner1 ");

            if (ii >= freq_High)
            {
                ScanRST = 0; // reset back to start
                ii = freq_Low;

                Debug.WriteLine("SCANNER FINISHED ");

                if (chkBoxLoop.Checked == true)
                {
                    SP5_Active = 1;
                    goto LOOP1;
                }


            }
            else
            {
                ScanRST = 1; // leave off where you left off
                freq_Last = ii + (step * 2); // need to jump past last signal that breaks squelch otherwise you cant move anymore
            }


            //   scantype = 0;

        } // SCANNER


        //===============================================================================
        //===============================================================================


        // ke9ns override band edge setting 
        private void lowFBox_Click(object sender, EventArgs e)
        {
            double freq2 = 0.0;
            ScanRST = 0;
            try
            {
                freq2 = Convert.ToDouble(lowFBox.Text);

                if (freq2 < freq_Low1) freq2 = freq_Low1;

            }
            catch (Exception)
            {
                freq2 = freq_Low1;


            }

            freq_Low = freq2;
            lowFBox.Text = freq_Low.ToString("f6");

            if (console.RX1Band >= 0) SLowScan[(int)console.RX1Band] = lowFBox.Text; // ke9ns .186: save low/high of scanner for each band

        } // lowFBox_Click

        private void lowFBox_MouseLeave(object sender, EventArgs e)
        {
            double freq2 = 0.0;
            ScanRST = 0;
            try
            {
                freq2 = Convert.ToDouble(lowFBox.Text);

                if (freq2 < freq_Low1) freq2 = freq_Low1;

            }
            catch (Exception)
            {
                freq2 = freq_Low1;


            }


            freq_Low = freq2;
            lowFBox.Text = freq_Low.ToString("f6");

            if (console.RX1Band >= 0) SLowScan[(int)console.RX1Band] = lowFBox.Text; // ke9ns .186: save low/high of scanner for each band

        } // lowFBox_MouseLeave

        private void highFBox_Click(object sender, EventArgs e)
        {
            double freq3 = 0.0;
            ScanRST = 0;
            try
            {
                freq3 = Convert.ToDouble(highFBox.Text);
                if (freq3 > freq_High1) freq3 = freq_High1;
            }
            catch (Exception)
            {
                freq3 = freq_High1;


            }

            freq_High = freq3;
            highFBox.Text = freq_High.ToString("f6");

            if (console.RX1Band >= 0) SHighScan[(int)console.RX1Band] = highFBox.Text; // ke9ns .186: save low/high of scanner for each band


        } // highFBox_Click

        private void highFBox_MouseLeave(object sender, EventArgs e)
        {
            double freq3 = 0.0;
            ScanRST = 0;
            try
            {
                freq3 = Convert.ToDouble(highFBox.Text);
                if (freq3 > freq_High1) freq3 = freq_High1;
            }
            catch (Exception)
            {
                freq3 = freq_High1;


            }

            freq_High = freq3;
            highFBox.Text = freq_High.ToString("f6");

            if (console.RX1Band >= 0) SHighScan[(int)console.RX1Band] = highFBox.Text; // ke9ns .186: save low/high of scanner for each band

        } // highFBox_MouseLeave

        private void lowFBox_KeyDown(object sender, KeyEventArgs e)
        {
            double freq2 = 0.0;

            if (e.KeyData == Keys.Enter)
            {
                ScanRST = 0;
                try
                {
                    freq2 = Convert.ToDouble(lowFBox.Text);

                    if (freq2 < freq_Low1) freq2 = freq_Low1;

                }
                catch (Exception)
                {
                    freq2 = freq_Low1;


                }

                freq_Low = freq2;
                lowFBox.Text = freq_Low.ToString("f6");

                if (console.RX1Band >= 0) SLowScan[(int)console.RX1Band] = lowFBox.Text; // ke9ns .186: save low/high of scanner for each band

            } // wait for enter key

        } // lowFBox_KeyDown

        private void highFBox_KeyDown(object sender, KeyEventArgs e)
        {
            double freq3 = 0.0;

            if (e.KeyData == Keys.Enter)
            {
                ScanRST = 0;
                try
                {
                    freq3 = Convert.ToDouble(highFBox.Text);
                    if (freq3 > freq_High1) freq3 = freq_High1;
                }
                catch (Exception)
                {
                    freq3 = freq_High1;


                }

                freq_High = freq3;
                highFBox.Text = freq_High.ToString("f6");

                if (console.RX1Band >= 0) SHighScan[(int)console.RX1Band] = highFBox.Text; // ke9ns .186: save low/high of scanner for each band

            } // wait for enter key

        }// highFBox_KeyDown

        private void chkAlwaysOnTop_CheckedChanged(object sender, EventArgs e)
        {
            this.TopMost = chkAlwaysOnTop.Checked;
        }

        private void label6_Click(object sender, EventArgs e)
        {

        }



        string customscannerlist = ""; // ke9ns file name of customscannerlist


        //=========================================================================================
        // ke9ns add   select scanner file name (text file freq in hz, name)
        string[] customString = new string[200]; // name
        string[] customFilter = new string[200]; // filter
        string[] customMode = new string[200]; // mode
        double[] customMem = new double[200]; // list of custom memory frequency

        public static FileStream stream2;          // for reading custom scanner list file
        public static BinaryReader reader2;

        public static int custSize = 0; // size on custom freq list

        private void btnCustomList_Click(object sender, EventArgs e)
        {

            ScanPause = false;

            comboBoxTS1.Text = "";
            Gname = "";
            memcount = 0;
            memtotal = 0;  //.226


            ST3.Stop();
            ST3.Reset();


            if (ScanRun == false) // if stopped
            {
                string filePath = console.AppDataPath + "CustomScannerList\\";


                if (!Directory.Exists(filePath))
                {
                    //   Debug.WriteLine("no CustomScannerList folder file found");
                    System.IO.Directory.CreateDirectory(console.AppDataPath + "CustomScannerList"); // ke9ns create sub directory
                                                                                                    //  Debug.WriteLine("CustomScannerList created");

                }

                openFileDialog2.InitialDirectory = String.Empty;
                openFileDialog2.InitialDirectory = filePath; // ke9ns  file to quickplay subfolder but could also be wave_folder;


                DialogResult result1 = openFileDialog2.ShowDialog();

                if (result1 == DialogResult.OK) // Test result.
                {
                    //    Debug.WriteLine("file selected1 " + result);
                    //  Debug.WriteLine("file selected2 " + openFileDialog2.FileName);

                    customscannerlist = openFileDialog2.FileName; // pass file name to wave file
                }
                else
                {
                    customscannerlist = null;
                    return; // if you dont select a file then no scanning
                }

                stream2 = new FileStream(customscannerlist, FileMode.Open); // open  file
                reader2 = new BinaryReader(stream2, Encoding.ASCII);

                var result = new StringBuilder();

                Debug.WriteLine("OPEN THE FILE ");

                custSize = 0; // new size of custom freq list

                int x = 0;
                for (; ; )
                {

                    try
                    {
                        var newChar = (char)reader2.ReadChar();

                        if ((newChar == '\r'))  // 0x0d LF
                        {

                            newChar = (char)reader2.ReadChar(); // read \n char to finishline

                            string[] values = result.ToString().Split(','); // split line up into segments divided by ,

                            Debug.WriteLine("CUSTOM STRING " + values[0]);
                            Debug.WriteLine("CUSTOM MEM " + values[1]);

                            Debug.WriteLine("CUSTOM MEM " + values[2]);

                            Debug.WriteLine("CUSTOM MEM " + values[3]);

                            customString[x] = values[0];                 // name
                            customMem[x] = Convert.ToDouble(values[1]);  // freq
                            customMode[x] = values[2];                   // mode = LSB,  USB,DSB,CWL,CWU,FM,	AM,	DIGU,SPEC,	DIGL,	SAM, DRM
                            customFilter[x] = values[3];                 // filter =  F1,F2,F3,	F4,	F5,	F6,	F7,	F8,	F9,	F10,VAR1,VAR2



                            result.Clear();


                            x++; // get next line
                        }
                        else
                        {

                            result.Append(newChar);  // save char
                        }

                    }
                    catch (EndOfStreamException)
                    {
                        // x--;
                        Debug.WriteLine("END OF STREAM ");
                        break; // done with file
                    }
                    catch (Exception f)
                    {
                        Debug.WriteLine("GET CHAR EXCEPTION " + f);
                        // x--;
                        break;
                    }

                    if (x > 100) break; // only allow 100 freq in list


                } // for loop 

                reader2.Close();    // close  file
                stream2.Close();   // close stream

                custSize = x; // new size of custom freq list

                ScanRun = true; // start up the scanner

                currFBox.Text = "";
                for (int y = 0; y < 100; y++)
                {
                    memsignal[y] = null;
                }

                UpdateText3();

                Thread t = new Thread(new ThreadStart(SCAN3));
                t.Name = "Custom memory Scanner Thread";
                t.IsBackground = true;
                t.Priority = ThreadPriority.Normal;
                t.Start();

            }
            else
            {
                ScanRun = false; // turn off scanner

            }


        } // btnCustomList_Click

        //========================================================================================
        //  CUSTOM MEMORY LIST CURRFBOX text update
        void UpdateText3()
        {

            string temp1 = "";


            for (int ii = 0; ii < custSize; ii++)
            {

                string freq3 = customMem[ii].ToString("###0.000000"); // was N6 4 less than having index numbers
                string name = customString[ii];
                string mm = "Custom Memory List";

                if (memsignal[ii] == "") memsignal[ii] = " ";
                if (memsignal[ii] == null) memsignal[ii] = " ";

                temp1 = temp1 + (ii + 1).ToString().PadLeft(2) + ": " + mm.PadRight(20).Substring(0, 20) + ", " + freq3.PadLeft(12).Substring(0, 12) + ", " + name.PadRight(20).Substring(0, 20) + ", " + memsignal[ii].PadRight(20).Substring(0, 20) + "\r\n"; // 74 char long //.226 fix ii


            } // for


            currFBox.Text = temp1;

        } // UpdateText3()  CUSTOM MEMORY LIST


        //===========================================================================
        // Thread CUSTOM LIST MEMORY scanner
        private void SCAN3()
        {
            scantype = 3;

            ST2.Reset();
            ST3.Reset();

            Debug.WriteLine("SCANSTOP = " + ScanStop);
            btnCustomList.BackColor = Color.LightGreen;

            int lastSIG = 0;
            int lastSQL = 0;

            string filter, mode;
            double freq;


            do // ScanRun
            {

                for (int x = 0; x < custSize; x++)
                {

                    Thread.Sleep(50);
                    if (scanstop == true)
                    {
                        scanstop = false;  // reset
                        goto RT2;

                    }
                    try
                    {
                        speed = (int)udspeedBox.Value;  // Convert.ToInt16(udspeedBox.Text);
                        Debug.WriteLine("SPEED " + speed);
                    }
                    catch (Exception)
                    {
                        speed = 50; // 50msec
                    }


                    // go to next bandstack memory

                    freq = customMem[x];
                    filter = customFilter[x];   // "LAST";
                    mode = customMode[x];        // "LAST";

                    Debug.WriteLine("CUSTOM BAND: " + freq + " , " + filter + " , " + mode);

                    // filter = F1,F2,F3,F4,F5,F6,F7,F8,F9,F10,VAR1,VAR2
                    // mode = LSB,USB,DSB,CWL,CWU,FM,AM,DIGU,SPEC,DIGL,SAM,DRM


                    console.SetBand(mode, filter, freq);
                    console.UpdateWaterfallLevelValues();

                    currFBox.SelectionStart = x * linelength; // i * linelength
                    currFBox.SelectionLength = linelength;
                    currFBox.ScrollToCaret(); // keep highlighted line visable

                    ST2.Restart(); // restart timer over again

                    ScanStop = 0; // reset squelch

                    lastSIG = -400;
                    lastSQL = -400;
                    //-------------------------------------------------------
                    // timer
                    do // scan speed and scanPause
                    {
                        Thread.Sleep(50);
                        if (scanstop == true)
                        {
                            scanstop = false;  // reset
                            goto RT2;

                        }
                        if (ScanPause == true) pausebtn.BackColor = Color.Yellow;
                        else pausebtn.BackColor = SystemColors.ControlLight;

                        if (ScanRun == false)
                        {
                            ScanPause = false;
                            goto RT2;
                        }

                        if (SIG > lastSIG)
                        {
                            lastSIG = SIG;
                        }

                        if (SQL > lastSQL)
                        {
                            lastSQL = SQL;
                        }

                        if ((ScanStop == 1)) // if console detected squelch open
                        {
                            memsignal[x] = lastSQL.ToString().PadLeft(4) + ", " + lastSIG.ToString().PadLeft(4) + ", Squelch Break";

                            if ((chkBoxSQLBRK.Checked == true) && (ScanPause == false)) // if stop on squelch break, then stop now
                            {
                                ScanPause = true;
                                UpdateText3(); // update currFBox text

                                currFBox.SelectionStart = band_index * linelength; // i * linelength
                                currFBox.SelectionLength = linelength;

                                if (udPauseLength.Value > 0)
                                {
                                    Debug.WriteLine("ST3 TIMER STARTED ");
                                    ST3.Restart(); // start the pause timer
                                }
                                else
                                {
                                    ScanPause = false;
                                    ScanRun = false;
                                    goto RT2;
                                }
                                break;
                            }
                            else if ((chkBoxSQLBRKWait.Checked == true) && (ScanPause == false)) // if stop on squelch break, then stop now
                            {
                                ScanPause = true;
                                UpdateText3(); // update currFBox text

                                currFBox.SelectionStart = band_index * linelength; // i * linelength
                                currFBox.SelectionLength = linelength;

                                if (udPauseLength.Value > 0)
                                {
                                    Debug.WriteLine("ST3 TIMER STARTED ");
                                    ST3.Restart(); // start the pause timer
                                }
                                else
                                {
                                    ScanPause = false;
                                    ScanRun = false;
                                    goto RT2;
                                }
                                break;
                            }

                        }
                        else
                        {
                            memsignal[x] = lastSQL.ToString().PadLeft(4) + ", " + lastSIG.ToString().PadLeft(4) + ", ";
                        }
                        if (scanstop == true)
                        {
                            scanstop = false;  // reset
                            goto RT2;

                        }
                    } while ((ST2.ElapsedMilliseconds < speed) || (ScanPause == true));

                    //-------------------------------------------------------

                    UpdateText3(); // update currFBox text

                    currFBox.SelectionStart = x * linelength; // i * linelength
                    currFBox.SelectionLength = linelength;

                    if (SP5_Active == 1)
                    {
                        ScanRun = false;
                        ScanPause = false;
                        Debug.WriteLine("SCANSTOP, another scanner started");
                        break;
                    }


                    //-------------------------------------------------------
                    // CHECK For PAUSE
                    while (ScanPause == true)  // wait here in in pause
                    {

                        Thread.Sleep(50);

                        if (ScanRun == false)
                        {
                            Debug.WriteLine("SCANSTOP, Group scanner turned back off");
                            ScanPause = false;
                            break;
                        }

                        if (ST3.ElapsedMilliseconds > ((long)udPauseLength.Value * 1000))
                        {
                            ST3.Stop(); // stop the pause timer
                            if (chkBoxSQLBRKWait.Checked == true && ScanStop == 1)
                            {
                                ScanPause = true;
                                ScanStop = 0;
                                ST3.Restart();

                            }
                            else ScanPause = false;

                            Debug.WriteLine("ST3 TIMER REACHED PAUSELENGTH ");
                        }

                        if (ScanPause == true) pausebtn.BackColor = Color.Yellow;
                        else pausebtn.BackColor = SystemColors.ControlLight;

                    };

                    pausebtn.BackColor = SystemColors.ControlLight;

                } // FOR custSize loop

            } while (ScanRun == true); // ScanStopped so leave thread

        RT2: ST2.Stop();
            ST3.Stop();

            Debug.WriteLine("SCANTOP0"); // scanner done
            btnCustomList.BackColor = SystemColors.ControlLight;
            pausebtn.BackColor = SystemColors.ControlLight;
            //   scantype = 0;


        } // SCAN3  CUSTOMER LIST MEMORY SCANNER



        //==========================================================================================
        // Pause button
        private void pausebtn_Click(object sender, EventArgs e)
        {
            ST3.Stop();
            ST3.Reset();

            if (ScanRun == true)
            {
                if (ScanPause == false)
                {
                    ScanPause = true;

                }
                else
                {

                    ScanPause = false;
                    ScanStop = 0; // undo the squelch break if you unpause
                }
            }
        }

        int yyy = 0;
        int iii = 0;

        //==================================================================================
        // ke9ns left click to select this memory for vfoA
        private void currFBox_MouseUp(object sender, MouseEventArgs e)
        {
            string filter, mode;
            double freq;

            currFBox.ShortcutsEnabled = false;

            if (e.Button == MouseButtons.Left) // VFOA
            {
                Debug.WriteLine("LEFT CLICK: ");

                scanstop = true;

                if (scantype == 1)  // 1=memory, 2=band stack, 3= custom, 4= low-high
                {
                    try
                    {
                        int ii = currFBox.GetCharIndexFromPosition(e.Location);

                        xxx = (ii / linelength); //find row 

                        Debug.WriteLine("1xxx " + xxx + " , " + ii);


                        currFBox.SelectionStart = (xxx * linelength);
                        currFBox.SelectionLength = linelength;

                        currFBox.ScrollToCaret(); // keep highlighted line visable

                        Debug.WriteLine("index at start of click " + iii);


                        iii = xxx; // update new position in bandstack for checking if its locked

                        Debug.WriteLine("memcount " + memcount + " , " + memtotal);

                        yyy = 0;

                        if (iii > memIndex[memtotal])
                        {
                            Debug.WriteLine("clicked beyond index length " + memIndex[memtotal]);
                            return;
                        }

                        if (comboBoxTS1.Text != "") //.221 add so clicking on memory in the scann screen will pull up all memory parameters
                        {
                            Debug.WriteLine("MEMORY CLICK. restore memory " + xxx);

                            comboMemGroupName.SelectedIndex = memIndex[xxx];
                            recordToRestore = new MemoryRecord((MemoryRecord)comboMemGroupName.SelectedItem); // ke9ns   you select index in the combo pulldown list

                            Debug.WriteLine("CHANGE MEMORY TO " + recordToRestore.RXFreq);
                            console.RecallMemory(recordToRestore);

                            return;
                        }

                        int counter = 0;

                        for (int i = 0; i < memcount; i++) // find all the memories with the same group name
                        {
                            if (CultureInfo.InvariantCulture.CompareInfo.IndexOf((dataGridView2[0, i].Value.ToString()), Gname, CompareOptions.IgnoreCase) >= 0) // Gname must be contains in MEMORY (partial or full) and case insensitive)
                            {
                                freq = Convert.ToDouble(dataGridView2[1, i].Value);  // MEMORY "RXFREQ"  convert to hz

                                mode = dataGridView2[3, i].Value.ToString();  // DSPMODE of MEMORY

                                filter = dataGridView2[20, i].Value.ToString();

                                // you got a match to your GROUP name so check the line #
                                if (counter == iii)
                                {
                                    console.SetBand(mode, filter, freq);
                                    return;
                                }
                                counter++;
                            }

                        } // for i loop

                    }
                    catch (Exception)
                    {


                    }

                    Debug.WriteLine(" did not find a match ");
                    return;

                } // scantype = 1
                else if (scantype == 2)  // 1=memory, 2=band stack, 3= custom, 4= low-high
                {
                    try
                    {
                        int ii = currFBox.GetCharIndexFromPosition(e.Location);

                        xxx = (ii / linelength); //find row 

                        BandStackFilter bsf = BandStackManager.GetFilter(console.RX1Band);
                        if (bsf == null) return;

                        if (xxx >= bsf.NumberOfEntries) return; // if you click past the last index freq, then do nothing.

                        Debug.WriteLine("1xxx " + xxx + " , " + ii);


                        currFBox.SelectionStart = (xxx * linelength);
                        currFBox.SelectionLength = linelength;

                        yyy = 0;

                        BandStackEntry bse = bsf.EntryByIndex(xxx);
                        if (bse == null) return;

                        console.SetBand(bse.Mode.ToString(), bse.Filter.ToString(), bse.Frequency);

                        console.UpdateWaterfallLevelValues();
                    }
                    catch
                    {
                        Debug.WriteLine("Failed to determine index or cannot save bandstack because its locked");

                        if (yyy == 1)
                        {
                            console.UpdateWaterfallLevelValues();
                        }

                    }


                } // scantype = 2 (bandstack)



            } // LEFT CLICK MOUSE VFOA
            else if (e.Button == MouseButtons.Middle) // .222 VFOB RX2
            {
                Debug.WriteLine("MIDDLE CLICK: ");


                scanstop = true;

                if (scantype == 1)
                {
                    try
                    {
                        int ii = currFBox.GetCharIndexFromPosition(e.Location);

                        xxx = (ii / linelength); //find row 

                        Debug.WriteLine("1xxx " + xxx + " , " + ii);


                        currFBox.SelectionStart = (xxx * linelength);
                        currFBox.SelectionLength = linelength;

                        currFBox.ScrollToCaret(); // keep highlighted line visable

                        Debug.WriteLine("index at start of click " + iii);


                        iii = xxx; // update new position in bandstack for checking if its locked

                        Debug.WriteLine("memcount " + memcount + " , " + memtotal);

                        yyy = 0;

                        if (iii > memIndex[memtotal])
                        {
                            Debug.WriteLine("clicked beyond index length " + memIndex[memtotal]);
                            return;

                        }

                        if (comboBoxTS1.Text != "") //.221 add so clicking on memory in the scan screen will pull up all memory parameters
                        {
                            Debug.WriteLine("MEMORY CLICK. restore memory " + xxx);

                            comboMemGroupName.SelectedIndex = memIndex[xxx];
                            recordToRestore = new MemoryRecord((MemoryRecord)comboMemGroupName.SelectedItem); // ke9ns   you select index in the combo pulldown list

                            Debug.WriteLine("CHANGE MEMORY TO " + recordToRestore.RXFreq);
                            console.RecallMemoryB(recordToRestore); // vfob

                            if (!console.RX2Enabled) console.RX2Enabled = true; // auto-on RX2

                            return;
                        }

                        int counter = 0;

                        for (int i = 0; i < memcount; i++) // find all the memories with the same group name
                        {
                            if (CultureInfo.InvariantCulture.CompareInfo.IndexOf((dataGridView2[0, i].Value.ToString()), Gname, CompareOptions.IgnoreCase) >= 0) // Gname must be contains in MEMORY (partial or full) and case insensitive)
                            {
                                freq = Convert.ToDouble(dataGridView2[1, i].Value);  // MEMORY "RXFREQ"  convert to hz

                                mode = dataGridView2[3, i].Value.ToString();  // DSPMODE of MEMORY

                                filter = dataGridView2[20, i].Value.ToString();

                                // you got a match to your GROUP name so check the line #
                                if (counter == iii)
                                {
                                    console.SetBand2(mode, filter, freq); // .222 rx2

                                    if (!console.RX2Enabled) console.RX2Enabled = true; // auto-on RX2

                                    return;
                                }
                                counter++;
                            }

                        } // for i loop

                    }
                    catch (Exception)
                    {


                    }

                    Debug.WriteLine(" did not find a match ");
                    return;

                } // scantype = 1
                else if (scantype == 2)
                {
                    try
                    {
                        int ii = currFBox.GetCharIndexFromPosition(e.Location);

                        xxx = (ii / linelength); //find row 

                        BandStackFilter bsf = BandStackManager.GetFilter(console.RX1Band);
                        if (bsf == null) return;

                        if (xxx >= bsf.NumberOfEntries) return; // if you click past the last index freq, then do nothing.

                        Debug.WriteLine("1xxx " + xxx + " , " + ii);


                        currFBox.SelectionStart = (xxx * linelength);
                        currFBox.SelectionLength = linelength;

                        yyy = 0;

                        BandStackEntry bse = bsf.EntryByIndex(xxx);
                        if (bse == null) return;

                        console.SetBand2(bse.Mode.ToString(), bse.Filter.ToString(), bse.Frequency); // .222 rX2

                        if (!console.RX2Enabled) console.RX2Enabled = true; // auto-on RX2

                        console.UpdateWaterfallLevelValues();
                    }
                    catch
                    {
                        Debug.WriteLine("Failed to determine index or cannot save bandstack because its locked");

                        if (yyy == 1)
                        {
                            console.UpdateWaterfallLevelValues();
                        }

                    }


                } // scantype = 2 (bandstack)



            } // MIDDLE CLICK MOUSE VFOB RX2
            else if (e.Button == MouseButtons.Right) // .226 only for memory channel scan toggle ON/OFF
            {

                if (comboBoxTS1.Text == "") return;

                scanstop = true;

                if (scantype == 1)  // 1=memory, 2=band stack, 3= custom, 4= low-high
                {
                    try
                    {
                        int ii = currFBox.GetCharIndexFromPosition(e.Location);

                        xxx = (ii / linelength); //find row 

                        Debug.WriteLine("1xxx " + xxx + " , " + ii);


                        currFBox.SelectionStart = (xxx * linelength);
                        currFBox.SelectionLength = linelength;

                        currFBox.ScrollToCaret(); // keep highlighted line visable

                        Debug.WriteLine("index at start of click " + iii);


                        iii = xxx; // update new position in bandstack for checking if its locked

                        Debug.WriteLine("memcount " + memcount + " , " + memtotal + " , " + memIndex[xxx]);

                        yyy = 0;

                        if (iii >= memtotal)
                        {
                            Debug.WriteLine("clicked beyond index length " + memtotal);
                            return;

                        }

                        int counter = 0;

                        for (int i = 0; i < memcount; i++) // find all the memories with the same group name
                        {
                            if (CultureInfo.InvariantCulture.CompareInfo.IndexOf((dataGridView2[0, i].Value.ToString()), Gname, CompareOptions.IgnoreCase) >= 0) // Gname must be contains in MEMORY (partial or full) and case insensitive)
                            {
                                freq = Convert.ToDouble(dataGridView2[1, i].Value);  // MEMORY "RXFREQ"  convert to hz

                                mode = dataGridView2[3, i].Value.ToString();  // DSPMODE of MEMORY

                                filter = dataGridView2[20, i].Value.ToString();

                                // you got a match to your GROUP name so check the line #
                                if (counter == iii)
                                {
                                    bool scan = (bool)dataGridView2["Scan", i].Value; // ke9ns add .226 

                                    if (scan == true) dataGridView2["Scan", i].Value = false; // ke9ns add .226 change value here (in memoryForm.cs)
                                    else dataGridView2["Scan", i].Value = true;

                                    UpdateText(); // update memory currfbox.text .226

                                    if (comboBoxTS1.Text != "") //.221 add so clicking on memory in the scan screen will pull up all memory parameters
                                    {
                                        try
                                        {
                                            Debug.WriteLine("MEMORY CLICK. restore memory " + i);

                                            if (i < comboMemGroupName.Items.Count)
                                            {
                                                comboMemGroupName.SelectedIndex = i;
                                                recordToRestore = new MemoryRecord((MemoryRecord)comboMemGroupName.SelectedItem); // ke9ns   you select index in the combo pulldown list
                                                console.RecallMemory(recordToRestore);
                                            }
                                        }
                                        catch (Exception q)
                                        {
                                            Debug.WriteLine("scan marker recall fail " + q);
                                        }
                                    }

                                    try
                                    {
                                        console.SetBand(mode, filter, freq);
                                    }
                                    catch (Exception q)
                                    {
                                        Debug.WriteLine("scan marker setband fail " + q);
                                    }

                                    return;
                                }
                                counter++;
                            }

                        } // for i loop

                    }
                    catch (Exception)
                    {


                    }

                    Debug.WriteLine(" did not find a match ");
                    return;

                } // scantype = 1



            } // RIGHT CLICK MOUSE (toggle scan on/off)



        } // currFBox_MouseUp

        private void currFBox_MouseDown(object sender, MouseEventArgs e)
        {
            currFBox.ShortcutsEnabled = false; // added to eliminate the contextmenu from popping up

        } // currFBox_MouseDown

        private void chkBoxIdent_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void chkBoxSQLBRKWait_CheckedChanged(object sender, EventArgs e)
        {
            if (chkBoxSQLBRKWait.Checked == true)
            {
                chkBoxSQLBRK.Checked = false;

            }
        }

        private void chkBoxSQLBRK_CheckedChanged(object sender, EventArgs e)
        {
            if (chkBoxSQLBRK.Checked == true)
            {
                chkBoxSQLBRKWait.Checked = false;

            }
        }

        private void button_reset_Click(object sender, EventArgs e) // ke9ns add .186
        {
            if (console.RX1Band >= 0)
            {
                SLowScan[(int)console.RX1Band] = " ";
                SHighScan[(int)console.RX1Band] = " ";

                UpdateBandScanRange();

                lowFBox.Invalidate();
                highFBox.Invalidate();
            }

        } // button_reset_Click

        // Update the freq_Low/freq_High (and hard limits freq_Low1/freq_High1) scan range
        // for the current band, applying any user override stored in SLowScan/SHighScan.
        public void UpdateBandScanRange()
        {
            Band b = console.RX1Band;
            if ((int)b < 0 || (int)b >= (int)Band.LAST) b = Band.B10M;

            switch (b)
            {
                case Band.B160M: freq_Low = 1.8; freq_High = 2.0; break;
                case Band.B80M: freq_Low = 3.5; freq_High = 4.0; break;
                case Band.B60M: freq_Low = 5.3; freq_High = 5.6; break;
                case Band.B40M: freq_Low = 7.0; freq_High = 7.3; break;
                case Band.B30M: freq_Low = 10.1; freq_High = 10.15; break;
                case Band.B20M: freq_Low = 14.0; freq_High = 14.35; break;
                case Band.B17M: freq_Low = 18.068; freq_High = 18.168; break;
                case Band.B15M: freq_Low = 21.0; freq_High = 21.45; break;
                case Band.B12M: freq_Low = 24.89; freq_High = 24.99; break;
                case Band.B10M: freq_Low = 28.0; freq_High = 29.7; break;
                case Band.B6M: freq_Low = 50.0; freq_High = 54.0; break;
                case Band.B2M: freq_Low = 144.0; freq_High = 148.0; break;
                case Band.VHF0: freq_Low = 144.0; freq_High = 148.0; break;
                case Band.VHF1: freq_Low = 430.0; freq_High = 450.0; break;
                case Band.BLMF: freq_Low = 0.4; freq_High = 1.8; break;
                case Band.B120M: freq_Low = 2.0; freq_High = 3.0; break;
                case Band.B90M: freq_Low = 3.0; freq_High = 3.5; break;
                case Band.B61M: freq_Low = 4.0; freq_High = 5.3; break;
                case Band.B49M: freq_Low = 5.4; freq_High = 7.0; break;
                case Band.B41M: freq_Low = 7.2; freq_High = 9.0; break;
                case Band.B31M: freq_Low = 9.000001; freq_High = 9.99; break;
                case Band.B25M: freq_Low = 10.150001; freq_High = 13.57; break;
                case Band.B22M: freq_Low = 13.570001; freq_High = 14.0; break;
                case Band.B19M: freq_Low = 14.35; freq_High = 18.068; break;
                case Band.B16M: freq_Low = 18.168; freq_High = 21.0; break;
                case Band.B14M: freq_Low = 21.45; freq_High = 23.0; break;
                case Band.B13M: freq_Low = 23.0; freq_High = 24.89; break;
                case Band.B11M: freq_Low = 25.0; freq_High = 28.0; break;
                default: freq_Low = 28.0; freq_High = 29.0; break;
            }

            freq_Low1 = freq_Low; // hard limits cannot be changed by the user
            freq_High1 = freq_High;

            if (SLowScan[(int)b] != null && SLowScan[(int)b] != "")
            {
                try { freq_Low = Math.Max(freq_Low1, Convert.ToDouble(SLowScan[(int)b])); }
                catch (Exception) { }
            }

            if (SHighScan[(int)b] != null && SHighScan[(int)b] != "")
            {
                try { freq_High = Math.Min(freq_High1, Convert.ToDouble(SHighScan[(int)b])); }
                catch (Exception) { }
            }

            lowFBox.Text = freq_Low.ToString("f6");
            highFBox.Text = freq_High.ToString("f6");
        } // UpdateBandScanRange

        private void btnGroupMemory_MouseDown(object sender, MouseEventArgs e) //.236
        {

            MouseEventArgs me = (MouseEventArgs)e;

            if (me.Button == System.Windows.Forms.MouseButtons.Middle) // .244
            {
                ScanVFOB = true;

                ST3.Stop();
                ST3.Reset();

                ScanPause = false;

                if (console.MemoryList.List.Count == 0) return; // nothing in the list, exit
                if (comboMemGroupName.Items.Count == 0) return;
                memcount = comboMemGroupName.Items.Count; // total number of memories listed

                Debug.WriteLine("memory list7 " + memcount);

                if (ScanRun == false) // if stoppedchange
                {
                    currFBox.Text = "";

                    for (int y = 0; y < 100; y++)
                    {
                        memsignal[y] = null;
                    }

                    UpdateText();  // upate currFBox text

                    ScanRun = true; // start up the scanner

                    Thread t = new Thread(new ThreadStart(SCAN1));
                    t.Name = "Group memory Scanner Thread";
                    t.IsBackground = true;
                    t.Priority = ThreadPriority.Normal;
                    t.Start();

                }
                else
                {
                    ScanRun = false; // turn off scanner

                }

            }
            else
            {
                ScanVFOB = false;
            }

        } // btnGroupMemory_MouseDown
    } // scancontrol


} // powersdr
