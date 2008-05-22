using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;

using RiseOp.Implementation;
using RiseOp.Implementation.Dht;
using RiseOp.Implementation.Transport;

namespace RiseOp.Simulator
{
    internal enum InstanceChangeType { Add, Remove, Update, Refresh };
    internal delegate void InstanceChangeHandler(SimInstance instance, InstanceChangeType type);
    internal delegate void UpdateViewHandler();

    internal class InternetSim
    {
        // super-class
        SimForm Interface;


        internal List<SimInstance> Instances = new List<SimInstance>();

        internal List<SimInstance> Online  = new List<SimInstance>();
        internal List<SimInstance> Offline = new List<SimInstance>();

        Random RndGen = new Random(unchecked((int)DateTime.Now.Ticks));

        
        internal InstanceChangeHandler InstanceChange;
        internal UpdateViewHandler UpdateView;

        // Sim
        internal Dictionary<IPEndPoint, DhtNetwork> AddressMap = new Dictionary<IPEndPoint, DhtNetwork>();
        Dictionary<IPAddress, SimInstance> SimMap = new Dictionary<IPAddress, SimInstance>();

        internal object PacketHandle = new object(); // in/out packet references switch so use all encompassing handle
        internal List<SimPacket> OutPackets = new List<SimPacket>();
        internal List<SimPacket> InPackets = new List<SimPacket>();

        internal Queue<AsyncCoreFunction> CoreMessages = new Queue<AsyncCoreFunction>();

        internal Dictionary<ulong, string> UserNames = new Dictionary<ulong, string>();
        internal Dictionary<ulong, string> OpNames = new Dictionary<ulong, string>();

        internal Dictionary<TcpConnect, TcpConnect> TcpSourcetoDest = new Dictionary<TcpConnect, TcpConnect>();

        internal DateTime StartTime;
        internal DateTime TimeNow;

        internal int WebCacheHits;

        // thread
        internal Thread RunThread;
        internal bool Paused;
        bool   Step;
        internal bool   Shutdown;
        
        // settings
        internal int SleepTime = 250; // 1000 is realtime, 1000 / x = target secs to simulate per real sec

        bool RandomCache = true;
        internal bool LoadOnline = true;
        internal bool TestEncryption = false;
        internal bool FreshStart = false;
        internal bool TestTcpFullBuffer = false;
        internal bool UseTimeFile = true;
        internal bool TestCoreThread = false; // sleepTime needs to be set with this so packets have time to process ayncronously

        bool Flux        = false;
        int  FluxIn      = 1;
        int  FluxOut     = 0;

        int PercentNAT = 0;
        int PercentBlocked = 0;  


        internal InternetSim(SimForm form)
        {
            Interface = form;

            StartTime = new DateTime(2006, 1, 1, 0, 0, 0);
            TimeNow = StartTime;
        }

        internal void AddInstance(string path)
        {
            SimInstance instance = new SimInstance(this, path);

            if (LoadOnline)
                BringOnline(instance);
            else
                Offline.Add(instance);

            lock (Instances) 
                Instances.Add(instance);
            
            Interface.BeginInvoke(InstanceChange, instance, InstanceChangeType.Add);
        }

        internal void DoFlux()
        {
            // run flux once per minute
            if (TimeNow.Second % 5 != 0) 
                return;

            // influx
            for (int i = 0; i < FluxIn && Offline.Count > 0; i++)
                BringOnline(Offline[RndGen.Next(Offline.Count)]);
         
            // outflux
            for (int i = 0; i < FluxOut && Online.Count > 0; i++)
                BringOffline(Online[RndGen.Next(Online.Count)]);
        }

        internal void BringOnline(SimInstance instance)
        {
            string name = Path.GetFileNameWithoutExtension(instance.Path);
            string[] user = name.Split(new char[] { '-' });

            // ip
            byte[] ipbytes = new byte[4] { (byte)RndGen.Next(99), (byte)RndGen.Next(99), (byte)RndGen.Next(99), (byte)RndGen.Next(99) };
            instance.RealIP = Utilities.BytestoIP(ipbytes, 0);

            // firewall
            instance.RealFirewall = FirewallType.Open;
            int num = RndGen.Next(100);

            if (num < PercentBlocked)
                instance.RealFirewall = FirewallType.Blocked;

            else if (num < PercentBlocked + PercentNAT)
                instance.RealFirewall = FirewallType.NAT;

            OpCore core = null;

            try
            {
                core = new OpCore(instance, instance.Path, user[1].Trim());
            }
            catch (Exception ex)
            {
                MessageBox.Show(Interface, name + ": " + ex.Message);
                return;
            }

            instance.Core = core;

            // clear caches so sim relies on simed boot process
            core.GlobalNet.IPCache.Clear();
            core.GlobalNet.IPTable.Clear();
            core.OperationNet.IPCache.Clear();
            core.OperationNet.IPTable.Clear();

            // set name
            UserNames[core.OperationNet.LocalUserID] = core.User.Settings.ScreenName;
            OpNames[core.OperationNet.OpID] = core.User.Settings.Operation;

            // hook instance into maps
            SimMap[instance.RealIP] = instance;

            lock (AddressMap)
            {
                AddAddress(new IPEndPoint(instance.RealIP, core.User.Settings.GlobalPortTcp), core.GlobalNet);
                AddAddress(new IPEndPoint(instance.RealIP, core.User.Settings.GlobalPortUdp), core.GlobalNet);

                AddAddress(new IPEndPoint(instance.RealIP, core.User.Settings.OpPortTcp), core.OperationNet);
                AddAddress(new IPEndPoint(instance.RealIP, core.User.Settings.OpPortUdp), core.OperationNet);
            }

            lock (Online)
                Online.Add(instance);

            lock (Offline)
                if (Offline.Contains(instance))
                    Offline.Remove(instance);

            Interface.BeginInvoke(InstanceChange, instance, InstanceChangeType.Update);
        }

        internal void BringOffline(SimInstance instance)
        {
            lock(Offline)
                Offline.Add(instance);

            lock(Online)
                if (Online.Contains(instance))
                    Online.Remove(instance);

            AddressMap.Remove(new IPEndPoint(instance.RealIP, instance.Core.User.Settings.GlobalPortTcp));
            AddressMap.Remove(new IPEndPoint(instance.RealIP, instance.Core.User.Settings.GlobalPortUdp));

            AddressMap.Remove(new IPEndPoint(instance.RealIP, instance.Core.User.Settings.OpPortTcp));
            AddressMap.Remove(new IPEndPoint(instance.RealIP, instance.Core.User.Settings.OpPortUdp));

            SimMap.Remove(instance.RealIP);

            if (instance.Core.GuiMain != null)
                instance.Core.GuiMain.Close();

            instance.Core.Exit();
            instance.Core = null;
            
            Interface.BeginInvoke(InstanceChange, instance, InstanceChangeType.Update);
        }

        internal void AddAddress(IPEndPoint address, DhtNetwork network)
        {
            if (AddressMap.ContainsKey(address))
                Debug.Assert(false);
            else
                AddressMap[address] = network;
        }

        internal void Start()
        {
            if (RunThread == null || !RunThread.IsAlive)
            {
                RunThread = new Thread(new ThreadStart(Run));
                RunThread.Start();
            }

            Paused = false;
        }

        internal void DoStep()
        {
            Step = true;
            Paused = true;
            
            if (RunThread == null)
            {
                RunThread = new Thread(new ThreadStart(Run));
                RunThread.Start();
            }              
        }

        internal void Pause()
        {
            if (RunThread != null)
                Paused = true;
        }

        void Run()
        {
            // 2 threads, background (core) and foreground (interface)

            int i = 0;
            int pumps = 4;
            List<SimPacket> tempList = new List<SimPacket>();


            while (true)
            {
                if (Shutdown)
                    return;

                if (Paused && !Step)
                {
                    Thread.Sleep(250);
                    continue;   
                }

                // load users
                if (Flux)
                    DoFlux();


                // pump packets, 4 times (250ms latency

                while (i < pumps)
                {
                    // clear out buffer by switching with in buffer
                    lock (PacketHandle)
                    {
                        tempList = InPackets;
                        InPackets = OutPackets;
                        OutPackets = tempList;

                        // send packets
                        foreach (SimPacket packet in InPackets)
                        {
                            switch (packet.Type)
                            {
                                case SimPacketType.Udp:
                                    packet.Dest.Core.Sim.BytesRecvd += (ulong)packet.Packet.Length;
                                    packet.Dest.UdpControl.OnReceive(packet.Packet, packet.Packet.Length, packet.Source);
                                    break;
                                case SimPacketType.TcpConnect:
                                    TcpConnect socket = packet.Dest.TcpControl.OnAccept(null, packet.Source);

                                    if (socket != null)
                                    {
                                        TcpSourcetoDest[packet.Tcp] = socket;
                                        TcpSourcetoDest[socket] = packet.Tcp;

                                        packet.Tcp.OnConnect();
                                    }

                                    break;
                                case SimPacketType.Tcp:
                                    if (TcpSourcetoDest.ContainsKey(packet.Tcp))
                                    {
                                        TcpConnect dest = TcpSourcetoDest[packet.Tcp];

                                        dest.Core.Sim.BytesRecvd += (ulong)packet.Packet.Length;

                                        packet.Packet.CopyTo(dest.RecvBuffer, dest.RecvBuffSize);
                                        dest.OnReceive(packet.Packet.Length);
                                    }
                                    break;
                                case SimPacketType.TcpClose:
                                    if (TcpSourcetoDest.ContainsKey(packet.Tcp))
                                    {
                                        TcpConnect dest = TcpSourcetoDest[packet.Tcp];
                                        dest.OnReceive(0);

                                        TcpSourcetoDest.Remove(packet.Tcp);
                                        TcpSourcetoDest.Remove(dest);
                                    }
                                    break;
                            }
                        }

                        InPackets.Clear();
                    }

                    // send messages from gui
                    AsyncCoreFunction function = null;

                    // process invoked functions, dequeue quickly to continue processing
                    lock (CoreMessages)
                        while (CoreMessages.Count > 0)
                        {
                            function = CoreMessages.Dequeue();

                            if (function != null)
                            {
                                function.Result = function.Method.DynamicInvoke(function.Args);
                                function.Completed = true;
                                function.Processed.Set();
                            }
                        }


                    TimeNow = TimeNow.AddMilliseconds(1000 / pumps);

                    Interface.BeginInvoke(UpdateView, null);


                    i++;

                    if (Step)
                    {
                        Step = false;
                        break;
                    }
                }

                // instance timer
                if (i == pumps) // stepping would cause second timer to run every 250ms without this
                {
                    lock (Online)
                        foreach (SimInstance instance in Online)
                            instance.Core.SecondTimer();

                    i = 0;
                }

                // if run sim slow
                if (SleepTime > 0)
                    Thread.Sleep(SleepTime);

            }
        }

        object TimerLock = new object();
        object[] PacketsLock = new object[4] { new object(), new object(), new object(), new object() };

        ManualResetEvent[] WaitHandles = new ManualResetEvent[5] {  new ManualResetEvent(true), new ManualResetEvent(true), 
                                                                    new ManualResetEvent(true), new ManualResetEvent(true), 
                                                                    new ManualResetEvent(true) };

        
        void OldRun()
        {
            int pumps = 4;
            List<SimPacket> tempList = new List<SimPacket>();

            Thread timerThread = new Thread(RunTimer);
            timerThread.Start();

            Thread[] packetsThread = new Thread[4];
            for (int i = 0; i < 4; i++)
            {
                packetsThread[i] = new Thread(RunPackets);
                packetsThread[i].Start(i);
            }

            Thread.Sleep(1000); // lets new threads reach wait(


            while (true && !Shutdown)
            {
                if (Paused && !Step)
                {
                    Thread.Sleep(250);
                    continue;
                }

                // load users
                if (Flux)
                    DoFlux();


                // instance timer
                lock (TimerLock)
                {
                    WaitHandles[0].Reset();
                    Monitor.Pulse(TimerLock);
                } 

                // pump packets, 4 times (250ms latency
                for (int i = 0; i < pumps; i++)
                {
                    // clear out buffer by switching with in buffer
                    lock (PacketHandle)
                    {
                        tempList = InPackets;
                        InPackets = OutPackets;
                        OutPackets = tempList;
                    }

                    for (int index = 0; index < 4; index++)
                        lock (PacketsLock[index])
                        {
                            WaitHandles[1 + index].Reset();
                            Monitor.Pulse(PacketsLock[index]);
                        }

              
                    AutoResetEvent.WaitAll(WaitHandles);

                    InPackets.Clear();

                    TimeNow = TimeNow.AddMilliseconds(1000 / pumps);

                    Interface.BeginInvoke(UpdateView, null);

                    if (Step || Shutdown)
                    {
                        Step = false;
                        break;
                    }
                }

                // if run sim slow
                if (SleepTime > 0)
                    Thread.Sleep(SleepTime);
            }


            lock(TimerLock)
                Monitor.Pulse(TimerLock);

            for(int i = 0; i < 4; i++)
                lock(PacketsLock[i])
                    Monitor.Pulse(PacketsLock[i]);
        }

        void RunTimer()
        {
            while (true && !Shutdown)
            {
                lock (TimerLock)
                {
                    Monitor.Wait(TimerLock);

                    lock (Online)
                        foreach (SimInstance instance in Online)
                            instance.Core.SecondTimer();

                    WaitHandles[0].Set();
                }
            }
        }

        void RunPackets(object val)
        {
            int index = (int)val;

            while (true && !Shutdown)
            {
                lock (PacketsLock[index])
                {
                    Monitor.Wait(PacketsLock[index]);

                    // send packets
                    foreach (SimPacket packet in InPackets)
                    {
                        // 0 - global udp
                        // 1 - global tcp
                        // 2 - op udp
                        // 3 - op tcp

                        if ((index == 0 && packet.Type == SimPacketType.Udp && packet.Dest.IsGlobal) ||
                            (index == 1 && packet.Type != SimPacketType.Udp && packet.Dest.IsGlobal) ||
                            (index == 2 && packet.Type == SimPacketType.Udp && !packet.Dest.IsGlobal) ||
                            (index == 3 && packet.Type != SimPacketType.Udp && !packet.Dest.IsGlobal))
                        {

                            switch (packet.Type)
                            {
                                case SimPacketType.Udp:
                                    packet.Dest.Core.Sim.BytesRecvd += (ulong)packet.Packet.Length;
                                    packet.Dest.UdpControl.OnReceive(packet.Packet, packet.Packet.Length, packet.Source);
                                    break;
                                case SimPacketType.TcpConnect:
                                    TcpConnect socket = packet.Dest.TcpControl.OnAccept(null, packet.Source);

                                    if (socket != null)
                                    {
                                        TcpSourcetoDest[packet.Tcp] = socket;
                                        TcpSourcetoDest[socket] = packet.Tcp;

                                        packet.Tcp.OnConnect();
                                    }

                                    break;
                                case SimPacketType.Tcp:
                                    if (TcpSourcetoDest.ContainsKey(packet.Tcp))
                                    {
                                        TcpConnect dest = TcpSourcetoDest[packet.Tcp];

                                        dest.Core.Sim.BytesRecvd += (ulong)packet.Packet.Length;

                                        packet.Packet.CopyTo(dest.RecvBuffer, dest.RecvBuffSize);
                                        dest.OnReceive(packet.Packet.Length);
                                    }
                                    break;
                                case SimPacketType.TcpClose:
                                    if (TcpSourcetoDest.ContainsKey(packet.Tcp))
                                    {
                                        TcpConnect dest = TcpSourcetoDest[packet.Tcp];
                                        dest.OnReceive(0);

                                        TcpSourcetoDest.Remove(packet.Tcp);
                                        TcpSourcetoDest.Remove(dest);
                                    }
                                    break;
                            }
                         
                        }
                    }

                    WaitHandles[1 + index].Set();
                }
            }
        }

        internal void DownloadCache(DhtNetwork network)
        {
            WebCacheHits++;

            if (Online.Count == 0)
                return;

            List<SimInstance> open = new List<SimInstance>();

            foreach (SimInstance instance in Online)
                // use realfirewall, because if flux not used and a lot loaded they all start out as blocked
                if (instance.RealFirewall == FirewallType.Open && instance != network.Core.Sim)
                    open.Add(instance);

            // add 3 random instances to network
            List<SimInstance> cached = new List<SimInstance>();

            for (int i = 0; i < open.Count || cached.Count < 3; i++)
                if (RandomCache)
                    cached.Add(open[RndGen.Next(open.Count)]);
                else
                    cached.Add(open[i]);


            foreach (SimInstance entry in cached)
            {
                DhtContact contact = entry.Core.GlobalNet.GetLocalContact();
                contact.Address = entry.RealIP;
                network.AddCacheEntry(new IPCacheEntry(contact));
            }
        }

        internal int SendPacket(SimPacketType type, DhtNetwork network, byte[] packet, System.Net.IPEndPoint target, TcpConnect tcp)
        {
            if (type == SimPacketType.Tcp)
                if (!AddressMap.ContainsKey(target))
                {
                    //this is what actually happens -> throw new Exception("Disconnected");
                    return -1;
                }

            if( !AddressMap.ContainsKey(target) || 
                !SimMap.ContainsKey(target.Address) ||
                !SimMap.ContainsKey(network.Core.Sim.RealIP))
                return 0;

            DhtNetwork targetNet = AddressMap[target];
            if (network.IsGlobal != targetNet.IsGlobal)
                Debug.Assert(false);

            IPEndPoint source = new IPEndPoint(network.Core.Sim.RealIP, 0);
            source.Port = (type == SimPacketType.Udp) ? network.UdpControl.ListenPort : network.TcpControl.ListenPort;

            SimInstance sourceInstance = SimMap[source.Address];
            SimInstance destInstance   = SimMap[target.Address];

            if(packet != null)
                sourceInstance.BytesSent += (ulong)packet.Length; 

            // tcp connection must be present to send tcp
            if (type == SimPacketType.Tcp)
                if( !TcpSourcetoDest.ContainsKey(tcp))
                {
                    //this is what actually happens -> throw new Exception("Disconnected");
                    return -1;
                }

            // add destination to nat table
            if (type == SimPacketType.Udp && sourceInstance.RealFirewall == FirewallType.NAT)
                sourceInstance.NatTable[target] = true;

            // if destination blocked drop udp / tcp connect requests
            if (destInstance.RealFirewall == FirewallType.Blocked)
            {
                if (type == SimPacketType.Udp || type == SimPacketType.TcpConnect)
                    return 0;
            }

            // if destination natted drop udp (unless in nat table) / tcp connect requests
            else if (destInstance.RealFirewall == FirewallType.NAT)
            {
                if (type == SimPacketType.TcpConnect)
                    return 0;

                if (type == SimPacketType.Udp && !destInstance.NatTable.ContainsKey(source) )
                    return 0;
            }

            // randomly test tcp send buffer full
            if(TestTcpFullBuffer)
                if (type == SimPacketType.Tcp && packet.Length > 4 && RndGen.Next(2) == 1)
                {
                    int newlength = packet.Length / 2;
                    byte[] newpacket = new byte[newlength];
                    Buffer.BlockCopy(packet, 0, newpacket, 0, newlength);
                    packet = newpacket;
                }

            if (packet != null && packet.Length == 0)
                Debug.Assert(false, "Empty Packet");

            lock (PacketHandle)
                OutPackets.Add(new SimPacket(type, source, packet, AddressMap[target], tcp, network.LocalUserID));

            if (packet == null)
                return 0;

            return packet.Length;
        }

        internal void Exit()
        {
            Shutdown = true;

            foreach(ManualResetEvent handle in WaitHandles)
                handle.Set();

            while(Online.Count > 0)
            {
                BringOffline(Online[0]);
            }
        }
    }

    internal enum SimPacketType { Udp, Tcp, TcpConnect, TcpClose };

    internal class SimPacket
    {
        internal SimPacketType   Type;
        internal byte[]          Packet;
        internal IPEndPoint      Source;
        internal DhtNetwork      Dest;
        internal TcpConnect      Tcp;
        internal ulong           SenderID;

        internal SimPacket(SimPacketType type, IPEndPoint source, byte[] packet, DhtNetwork dest, TcpConnect tcp, ulong id)
        {
            Type = type;
            Source = source;
            Packet = packet;
            Dest = dest;
            Tcp = tcp;
            SenderID = id;
        }
    }

    internal class SimInstance
    {
        internal InternetSim Internet;
        internal OpCore Core;

        internal string Path;

        internal FirewallType RealFirewall;
        internal IPAddress RealIP;

        internal ulong BytesSent;
        internal ulong BytesRecvd;

        internal Dictionary<IPEndPoint, bool> NatTable = new Dictionary<IPEndPoint, bool>();

        internal SimInstance(InternetSim internet, string path)
        {
            Internet = internet;
            Path = path;
        }
    }

}
