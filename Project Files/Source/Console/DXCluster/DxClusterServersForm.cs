/*  DxClusterServersForm.cs

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
// DX Cluster - "Servers..." picker. A modal checklist of the built-in default nodes plus any   //
// nodes the user has already configured (shown as custom). The user ticks the nodes to have    //
// connected; the caller connects every ticked node at once. The dialog itself performs no      //
// I/O - it only returns the selected set.                                                       //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Thetis
{
    public class DxClusterServersForm : Form
    {
        private CheckedListBox list;
        private TextBox txtName;
        private TextBox txtHost;
        private TextBox txtPort;
        private Button btnAdd;
        private Button btnOk;
        private bool _loading;
        private readonly string _callsign;

        public DxClusterServersForm(IEnumerable<DxClusterServerEntry> entries, string callsign)
        {
            _callsign = (callsign ?? "").Trim();

            BuildUi();

            _loading = true;
            try
            {
                foreach (DxClusterServerEntry entry in entries)
                    list.Items.Add(entry, DxClusterManager.IsConfigured(entry.Host, entry.Port));
            }
            finally { _loading = false; }

            list.ItemCheck += onItemCheck;
        }

        private void BuildUi()
        {
            Text = "DX Cluster Server List";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(460, 420);
            BackColor = DxClusterTheme.Void;
            ForeColor = DxClusterTheme.TextPrimary;
            Font = new Font("Segoe UI", 8.5f);

            Label lblIntro = new Label
            {
                Text = "Tick a node to connect now and auto-connect on startup; untick to disconnect and remove it. Changes apply immediately.",
                Dock = DockStyle.Top,
                Height = 34,
                ForeColor = DxClusterTheme.TextSecondary,
                Padding = new Padding(6, 6, 6, 0),
            };

            list = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                BackColor = DxClusterTheme.Inset,
                ForeColor = DxClusterTheme.TextPrimary,
                BorderStyle = BorderStyle.FixedSingle,
            };

            Panel addPanel = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = DxClusterTheme.Void };
            Label lblName = new Label { Text = "Name", Location = new Point(6, 12), AutoSize = true, ForeColor = DxClusterTheme.TextSecondary };
            txtName = new TextBox { Location = new Point(44, 8), Width = 90, BackColor = DxClusterTheme.Inset, ForeColor = DxClusterTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
            Label lblHost = new Label { Text = "Host", Location = new Point(140, 12), AutoSize = true, ForeColor = DxClusterTheme.TextSecondary };
            txtHost = new TextBox { Location = new Point(176, 8), Width = 140, BackColor = DxClusterTheme.Inset, ForeColor = DxClusterTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
            Label lblPort = new Label { Text = "Port", Location = new Point(322, 12), AutoSize = true, ForeColor = DxClusterTheme.TextSecondary };
            txtPort = new TextBox { Location = new Point(354, 8), Width = 44, Text = "23", BackColor = DxClusterTheme.Inset, ForeColor = DxClusterTheme.TextPrimary, BorderStyle = BorderStyle.FixedSingle };
            btnAdd = new Button { Text = "Add", Location = new Point(402, 7), Width = 52, Height = 24, FlatStyle = FlatStyle.Flat, ForeColor = DxClusterTheme.TextPrimary, BackColor = DxClusterTheme.FaceBottom };
            btnAdd.Click += onAdd;
            addPanel.Controls.Add(lblName);
            addPanel.Controls.Add(txtName);
            addPanel.Controls.Add(lblHost);
            addPanel.Controls.Add(txtHost);
            addPanel.Controls.Add(lblPort);
            addPanel.Controls.Add(txtPort);
            addPanel.Controls.Add(btnAdd);

            Panel buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 42, BackColor = DxClusterTheme.Void };
            btnOk = new Button { Text = "Close", DialogResult = DialogResult.OK, Width = 100, Height = 26, Location = new Point(354, 8), FlatStyle = FlatStyle.Flat, ForeColor = DxClusterTheme.TextPrimary, BackColor = DxClusterTheme.FaceBottom };
            buttonPanel.Controls.Add(btnOk);

            Controls.Add(list);
            Controls.Add(addPanel);
            Controls.Add(buttonPanel);
            Controls.Add(lblIntro);

            AcceptButton = btnOk;
        }

        private void onAdd(object sender, EventArgs e)
        {
            string host = (txtHost.Text ?? "").Trim();
            if (host.Length == 0)
            {
                MessageBox.Show(this, "Enter a host name or address.", "DX Cluster Server List",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int port;
            if (!int.TryParse((txtPort.Text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port <= 0 || port > 65535)
                port = 23;

            if (alreadyListed(host, port))
            {
                MessageBox.Show(this, host + ":" + port + " is already in the list.", "DX Cluster Server List",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                selectExisting(host, port);
                return;
            }

            string name = (txtName.Text ?? "").Trim();

            DxClusterServerEntry entry = new DxClusterServerEntry(name, host, port);

            _loading = true;
            try
            {
                int index = list.Items.Add(entry, true);
                list.SelectedIndex = index;
            }
            finally { _loading = false; }

            // a newly added node is connected straight away and remembers to auto-connect
            DxClusterManager.EnsureConnected(host, port, _callsign);
            DxClusterManager.GetOrCreateNode(host, port).AutoConnect = true;
            DxClusterManager.SaveSettings();
            DxClusterManager.ResyncPanadapter();

            txtName.Text = "";
            txtHost.Text = "";
            txtPort.Text = "23";
            txtHost.Focus();
        }

        private bool alreadyListed(string host, int port)
        {
            string key = DxClusterManager.NodeKey(host, port);

            foreach (object item in list.Items)
            {
                DxClusterServerEntry entry = item as DxClusterServerEntry;
                if (entry != null && DxClusterManager.NodeKey(entry.Host, entry.Port) == key)
                    return true;
            }

            return false;
        }

        private void selectExisting(string host, int port)
        {
            string key = DxClusterManager.NodeKey(host, port);

            for (int i = 0; i < list.Items.Count; i++)
            {
                DxClusterServerEntry entry = list.Items[i] as DxClusterServerEntry;
                if (entry == null || DxClusterManager.NodeKey(entry.Host, entry.Port) != key) continue;

                list.SelectedIndex = i;
                list.SetItemChecked(i, true);
                break;
            }

            txtName.Text = "";
            txtHost.Text = "";
            txtPort.Text = "23";
            txtHost.Focus();
        }

        private void onItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (_loading) return;

            DxClusterServerEntry entry = list.Items[e.Index] as DxClusterServerEntry;
            if (entry == null) return;

            if (e.NewValue == CheckState.Checked)
            {
                DxClusterManager.EnsureConnected(entry.Host, entry.Port, _callsign);
                DxClusterManager.GetOrCreateNode(entry.Host, entry.Port).AutoConnect = true;
            }
            else
            {
                // untick removes it from the active connections immediately
                DxClusterManager.DisconnectAndRemoveNode(entry.Host, entry.Port);
            }

            DxClusterManager.SaveSettings();
            DxClusterManager.ResyncPanadapter();
        }
    }
}
