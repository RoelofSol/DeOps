using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows.Forms;

using RiseOp.Implementation;
using RiseOp.Implementation.Dht;
using RiseOp.Implementation.Transport;
using RiseOp.Implementation.Protocol;
using RiseOp.Services.Trust;
using RiseOp.Services.Location;


namespace RiseOp.Services.IM
{
    internal delegate void IM_MessageHandler(ulong id, InstantMessage message);
    internal delegate void IM_StatusHandler(ulong id);

    internal class IMService : OpService
    {
        public string Name { get { return "IM"; } }
        public uint ServiceID { get { return 5; } }

        const int SessionTimeout = 10;

        internal OpCore Core;
        internal DhtNetwork Network;
        internal TrustService Trust;
        internal LocationService Locations;

        internal ThreadedDictionary<ulong, IMStatus> IMMap = new ThreadedDictionary<ulong, IMStatus>();

        internal IM_MessageHandler MessageUpdate;
        internal IM_StatusHandler StatusUpdate;


        internal IMService(OpCore core)
        {
            Core = core;
            Network = Core.Network;
            Trust = core.Trust;
            Locations = core.Locations;

            Core.SecondTimerEvent += new TimerHandler(Core_SecondTimer);
            Core.GetFocusedCore += new GetFocusedHandler(Core_GetFocusedCore);

            Network.RudpControl.SessionUpdate += new SessionUpdateHandler(Session_Update);
            Network.RudpControl.SessionData[ServiceID, 0] += new SessionDataHandler(Session_Data);
            Network.RudpControl.KeepActive += new KeepActiveHandler(Session_KeepActive);

            Core.Trust.LinkUpdate += new LinkUpdateHandler(Link_Update);
            Core.Locations.LocationUpdate += new LocationUpdateHandler(Location_Update);
        }

        public void Dispose()
        {
            if (MessageUpdate != null)
                throw new Exception("IM Events not fin'd");

            Core.SecondTimerEvent -= new TimerHandler(Core_SecondTimer);
            Core.GetFocusedCore -= new GetFocusedHandler(Core_GetFocusedCore);

            Network.RudpControl.SessionUpdate -= new SessionUpdateHandler(Session_Update);
            Network.RudpControl.SessionData[ServiceID, 0] -= new SessionDataHandler(Session_Data);
            Network.RudpControl.KeepActive -= new KeepActiveHandler(Session_KeepActive);

            Core.Trust.LinkUpdate -= new LinkUpdateHandler(Link_Update);
            Core.Locations.LocationUpdate -= new LocationUpdateHandler(Location_Update);
        }

        void Core_SecondTimer()
        {
            // need keep alives because someone else might have IM window open while we have it closed

            // send keep alives every x secs
            if (Core.TimeNow.Second % SessionTimeout == 0)
            {
                List<IM_View> views = GetViews();

                foreach (IM_View view in views)
                {
                    IMStatus status = null;

                    if (IMMap.SafeTryGetValue(view.UserID, out status))
                        foreach(ushort client in status.TTL.Keys)
                            if (status.TTL[client].Value > 0)
                            {
                                RudpSession session = Network.RudpControl.GetActiveSession(view.UserID, client);

                                if (session != null)
                                {
                                    status.TTL[client].Value = SessionTimeout * 2;
                                    session.SendData(ServiceID, 0, new IMKeepAlive(), true);
                                }
                            }
                }
            }

            // timeout sessions
            IMMap.LockReading(delegate()
            {
                foreach(IMStatus status in IMMap.Values)
                    foreach (BoxInt ttl in status.TTL.Values)
                        if (ttl.Value > 0)
                            ttl.Value--;
            });
        }

        void Core_GetFocusedCore()
        {
            IMMap.LockReading(delegate()
            {
                foreach (ulong user in IMMap.Keys )
                    Core.Focused.SafeAdd(user, true);
            });
        }

        //crit not thread locked/protected
        private List<IM_View> GetViews()
        {
            List<IM_View> views = new List<IM_View>();

            if (MessageUpdate != null)
                foreach (Delegate func in MessageUpdate.GetInvocationList())
                    if (func.Target is IM_View)
                        views.Add((IM_View)func.Target);

            return views;
        }

        public List<MenuItemInfo> GetMenuInfo(InterfaceMenuType menuType, ulong user, uint project)
        {
            List<MenuItemInfo> menus = new List<MenuItemInfo>();

            if (menuType == InterfaceMenuType.Quick)
            {
                if (user == Core.UserID)
                    return null;

                if (!Core.Locations.LocationMap.SafeContainsKey(user))
                    return null;

                menus.Add(new MenuItemInfo("Send IM", IMRes.Icon, new EventHandler(QuickMenu_View)));
            }

            return menus;
        }

        internal void QuickMenu_View(object sender, EventArgs args)
        {
            IViewParams node = sender as IViewParams;

            if (node == null)
                return;

            // if window already exists to node, show it
            IM_View view = FindView(node.GetKey());

            if(view != null && view.External != null)
                view.External.BringToFront();

            // else create new window
            else
            {
                view = CreateView(node.GetKey());

                Core.RunInCoreAsync(delegate() { Connect(node.GetKey()); });
            }
        }

        private void Connect(ulong key)
        {
            Debug.Assert(!Core.InvokeRequired);

            IMStatus status = null;
            if(!IMMap.SafeTryGetValue(key, out status))
            {
                status = new IMStatus(key);
                IMMap.SafeAdd(key, status);
            }

            foreach (ClientInfo loc in Core.Locations.GetClients(key))
                Network.RudpControl.Connect(loc.Data);

            Update(status);
        }

        private void Update(IMStatus status)
        {
            ulong key = status.UserID;

            // connected to jonn smith @home, @work
            // connecting to john smith
            // disconnected from john smith

            string places = "";


            status.Connected = false;
            status.Connecting = false;
            status.Away = false;
            string awayMessage = "";
            int activeCount = 0;


            if (Network.RudpControl.SessionMap.ContainsKey(key))
                foreach (RudpSession session in Network.RudpControl.SessionMap[key])
                {
                    if (session.Status == SessionStatus.Closed)
                        continue;

                    status.Connecting = true;

                    if (session.Status == SessionStatus.Active)
                    {
                        status.Connected = true;

                        ClientInfo info = Locations.GetLocationInfo(key, session.ClientID);

                        awayMessage = "";
                        if (info != null)
                            if (info.Data.Away)
                            {
                                status.Away = true;
                                awayMessage = " " + info.Data.AwayMessage;
                            }

                        activeCount++;
                        places += " @" + Locations.GetLocationName(key, session.ClientID) + awayMessage + ",";
                    }
                }



            if (status.Away)
            {
                status.Text = Core.Trust.GetName(key) + " is Away ";

                if (activeCount > 1)
                    status.Text += places.TrimEnd(',');
                else
                    status.Text += awayMessage;
            }

            else if (status.Connected)
            {
                status.Text = "Connected to " + Core.Trust.GetName(key);
                
                if (activeCount > 1)
                    status.Text += places.TrimEnd(',');
            }

            else if(status.Connecting)
                status.Text = "Connecting to " + Core.Trust.GetName(key);

            else
                status.Text = "Disconnected from " + Core.Trust.GetName(key);


            Core.RunInGuiThread(StatusUpdate, status.UserID);
        }

        private IM_View FindView(ulong key)
        {
            List<IM_View> views = GetViews();

            foreach (IM_View view in views)
                if (view.UserID == key)
                    return view;

            return null;
        }

        private IM_View CreateView(ulong key)
        {
            if (Core.GuiMain == null)
                return null;

            if (Trust.GetTrust(key) == null)
                Trust.Research(key, 0, false);

            if (Locations.GetClients(key).Count == 0)
                Locations.Research(key);

            IM_View view = new IM_View(this, key);

            Core.InvokeView(true, view);

            return view;
        }

        internal void Link_Update(OpTrust trust)
        {
            if (FindView(trust.UserID) == null)
                return;

            IMStatus status = null;
            if (IMMap.SafeTryGetValue(trust.UserID, out status))
                Update(status);
        }

        internal void Location_Update(LocationData location)
        {
            if (FindView(location.UserID) == null)
                return;

            IMStatus status = null;
            if (!IMMap.SafeTryGetValue(location.UserID, out status))
                return;

            Network.RudpControl.Connect(location);

            Update(status);
        }

        internal void Session_Update(RudpSession session)
        {
            if (FindView(session.UserID) == null)
                return;

            IMStatus status = null;
            if (!IMMap.SafeTryGetValue(session.UserID, out status))
                return;


            if (session.Status == SessionStatus.Active)
            {
                // needs to be set here as well be cause we don't receive a keep alive from remote host on connect
                status.SetTTL(session.ClientID, SessionTimeout * 2);

                session.SendData(ServiceID, 0, new IMKeepAlive(), true);
            }

            Update(status);
        }

        internal void SendMessage(ulong key, string text)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { SendMessage(key, text); });
                return;
            }

            IMStatus status = null;
            if (!IMMap.SafeTryGetValue(key, out status))
                return;

            ProcessMessage(status, new InstantMessage(Core, text, false));

            if (!Network.RudpControl.IsConnected(key))
            {
                // run direct, dont log
                Core.RunInGuiThread(MessageUpdate, key, new InstantMessage(Core, "Could not send message, client disconnected", true));
                return;
            }

            MessageData message = new MessageData(text);

            foreach (RudpSession session in Network.RudpControl.GetActiveSessions(key))
                session.SendData(ServiceID, 0, message, true);
        }

        void Session_Data(RudpSession session, byte[] data)
        {
            IMStatus status = null;
            if (!IMMap.SafeTryGetValue(session.UserID, out status))
            {
                status = new IMStatus(session.UserID);
                IMMap.SafeAdd(session.UserID, status);
            }

            

            G2Header root = new G2Header(data);

            if (G2Protocol.ReadPacket(root))
            {
                if (root.Name == IMPacket.Message)
                {
                    InstantMessage im = new InstantMessage(Core, session, MessageData.Decode(root));

                    ProcessMessage(status, im);
                }

                if (root.Name == IMPacket.Alive)
                    status.SetTTL(session.ClientID, SessionTimeout * 2);
            }

        }

        internal void ProcessMessage(IMStatus status, InstantMessage message)
        {
            // log message - locks both dictionary and embedded list form reading
            status.MessageLog.SafeAdd(message);

            // update interface
            if (Core.GuiMain == null)
                return;

            Core.RunInGuiThread( (MethodInvoker) delegate()
            {
                IM_View view = FindView(status.UserID);

                if (view == null)
                    CreateView(status.UserID);
                else
                    MessageUpdate(status.UserID, message);
            });

            Update(status);
        }

        void Session_KeepActive(Dictionary<ulong, bool> active)
        {
            IMMap.LockReading(delegate()
            {
                foreach(IMStatus status in IMMap.Values)
                     foreach(ushort client in status.TTL.Keys)
                         if (status.TTL[client].Value > 0)
                         {
                             active[status.UserID] = true;
                             break;
                         }  
            });
        }
    }



    internal class InstantMessage
    {
        internal ulong    Source;
        internal ushort   ClientID;
        internal DateTime TimeStamp;
        internal string   Text;
        internal bool System;
        // local / system message
        internal InstantMessage(OpCore core, string text, bool system)
        {
            Source = core.UserID;
            ClientID = core.Network.Local.ClientID;
            TimeStamp = core.TimeNow;
            Text = text;
            System = system;
        }

        internal InstantMessage(OpCore core, RudpSession session, MessageData message)
        {
            Source = session.UserID;
            ClientID = session.ClientID;
            TimeStamp = core.TimeNow;
            Text = message.Text;
        }
    }

    internal class IMStatus
    {
        internal ulong UserID;
        internal Dictionary<ushort, BoxInt> TTL = new Dictionary<ushort, BoxInt>();
  
        internal string Text = "";
        internal bool Connected;
        internal bool Connecting;
        internal bool Away;

        internal ThreadedList<InstantMessage> MessageLog = new ThreadedList<InstantMessage>();
        
        internal IMStatus(ulong id)
        {
            UserID = id;
        }

        internal void SetTTL(ushort client, int ttl)
        {
            if (!TTL.ContainsKey(client))
                TTL[client] = new BoxInt();

            TTL[client].Value = ttl;
        }
    }

    internal class BoxInt
    {
        internal int Value;
    }
}
