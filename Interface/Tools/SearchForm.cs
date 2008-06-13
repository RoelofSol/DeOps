using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

using RiseOp.Services;
using RiseOp.Implementation;
using RiseOp.Implementation.Dht;
using RiseOp.Implementation.Protocol.Net;
using RiseOp.Simulator;

namespace RiseOp.Interface.Tools
{
    internal partial class SearchForm : Form
    {
        SimForm Control;
        InternetSim Sim;
        DhtNetwork  Network;

        delegate void UpdateListHandler(DhtSearch search);
        UpdateListHandler OnUpdateList;

        internal SearchForm(string name, SimForm control, DhtNetwork network)
        {
            InitializeComponent();

            Control = control;
            Sim = control.Sim;
            Network = network;

            if (network.Core.User == null)
                Text = "Global Search (" + network.Core.Context.LocalIP.ToString() + ")";
            else
                Text = "Search (" + network.Core.User.Settings.UserName + ")";

            OnUpdateList = new UpdateListHandler(UpdateList);
        }

        private void ButtonSearch_Click(object sender, EventArgs e)
        {
            ulong TargetID = 0;

            // find Dht id or user or operation
            if (RadioUser.Checked)
                foreach (SimInstance instance in Sim.Instances)
                    instance.Context.Cores.LockReading(delegate()
                    {
                        foreach (OpCore core in instance.Context.Cores)
                            if (core.User.Settings.UserName == TextSearch.Text)
                            {
                                TargetID = Network.IsGlobal ? core.Context.Global.UserID : core.UserID;
                                break;
                            }
                    });

            if (RadioOp.Checked)
                foreach (SimInstance instance in Sim.Instances)
                    instance.Context.Cores.LockReading(delegate()
                    {
                        foreach (OpCore core in instance.Context.Cores)
                            if (core.User.Settings.Operation == TextSearch.Text)
                            {
                                TargetID = core.Network.OpID;
                                break;
                            }
                    });

            if (TargetID == 0)
            {
                MessageBox.Show(this, "Not Found");
                return;
            }

            ListResults.Items.Clear();
            LabelResults.Text = "";

            Network.Searches.Start(TargetID, "MANUAL", Network.Core.DhtServiceID, 0, null, new RiseOp.Implementation.Dht.EndSearchHandler(EndManualSearch));
        }

        void EndManualSearch(DhtSearch search)
        {
            BeginInvoke(OnUpdateList, search);          
        }

        void UpdateList(DhtSearch search)
        {
            if (search.FoundValues.Count > 0)
            {
                LabelResults.Text = search.FoundValues.Count.ToString() + " Values Found, ";
            }

            if (search.FoundContact != null)
            {
                LabelResults.Text = "Found Contact(" + search.FoundContact.ClientID.ToString() + ") ";

                if (search.FoundProxy)
                    LabelResults.Text += "is a proxy";
            }



            // Dht
            // client 

            foreach (DhtLookup lookup in search.LookupList)
                ListResults.Items.Add(new ListViewItem(new string[]
					{
						Utilities.IDtoBin(lookup.Contact.UserID),
						lookup.Contact.ClientID.ToString()		
					}));

        }
    }
}