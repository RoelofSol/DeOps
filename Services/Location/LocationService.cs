using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using RiseOp.Implementation;
using RiseOp.Implementation.Dht;
using RiseOp.Implementation.Protocol;
using RiseOp.Implementation.Protocol.Net;
using RiseOp.Implementation.Transport;
using RiseOp.Services.Trust;


namespace RiseOp.Services.Location
{
    internal delegate void LocationUpdateHandler(LocationData location);
    internal delegate void LocationGuiUpdateHandler(ulong key);

    internal delegate byte[] GetLocationTagHandler();
    internal delegate void LocationTagReceivedHandler(DhtAddress address, ulong user, byte[] tag);


    internal class LocationService : OpService
    {
        public string Name { get { return "Location"; } }
        public uint ServiceID { get { return 2; } }

        OpCore Core;

        internal uint LocationVersion = 1;
        internal DateTime NextLocationUpdate;
        internal DateTime NextGlobalPublish;

        internal ClientInfo LocalLocation;
        internal ThreadedDictionary<ulong, LinkedList<CryptLoc>> GlobalIndex = new ThreadedDictionary<ulong, LinkedList<CryptLoc>>();
        internal ThreadedDictionary<ulong, ThreadedDictionary<ushort, ClientInfo>> LocationMap = new ThreadedDictionary<ulong, ThreadedDictionary<ushort, ClientInfo>>();

        Dictionary<ulong, DateTime> NextResearch = new Dictionary<ulong, DateTime>();

        internal LocationUpdateHandler LocationUpdate;
        internal LocationGuiUpdateHandler GuiUpdate;

        internal ServiceEvent<GetLocationTagHandler> GetTag = new ServiceEvent<GetLocationTagHandler>();
        internal ServiceEvent<LocationTagReceivedHandler> TagReceived = new ServiceEvent<LocationTagReceivedHandler>();


        int PruneGlobalKeys    = 64;
        int PruneGlobalEntries = 16;
        int PruneLocations = 64;
        
        int MaxClientsperUser = 10;

        internal bool LocalAway;


        internal LocationService(OpCore core)
        {
            Core = core;
            Core.Locations = this;

            Core.SecondTimerEvent += new TimerHandler(Core_SecondTimer);
            Core.MinuteTimerEvent += new TimerHandler(Core_MinuteTimer);

            Core.GlobalNet.Store.StoreEvent[ServiceID, 0] += new StoreHandler(GlobalStore_Local);
            Core.GlobalNet.Store.ReplicateEvent[ServiceID, 0] += new ReplicateHandler(GlobalStore_Replicate);
            Core.GlobalNet.Store.PatchEvent[ServiceID, 0] += new PatchHandler(GlobalStore_Patch);
            Core.GlobalNet.Searches.SearchEvent[ServiceID, 0] += new SearchRequestHandler(GlobalSearch_Local);

            Core.OperationNet.Store.StoreEvent[ServiceID, 0] += new StoreHandler(OperationStore_Local);
            Core.OperationNet.Searches.SearchEvent[ServiceID, 0] += new SearchRequestHandler(OperationSearch_Local);


            if (Core.Sim != null)
            {
                PruneGlobalKeys    = 16;
                PruneGlobalEntries = 4;
                PruneLocations     = 16;
            }
        }

       

        public void Dispose()
        {
            Core.SecondTimerEvent -= new TimerHandler(Core_SecondTimer);
            Core.SecondTimerEvent -= new TimerHandler(Core_MinuteTimer);

            Core.GlobalNet.Store.StoreEvent[ServiceID, 0] -= new StoreHandler(GlobalStore_Local);
            Core.GlobalNet.Store.ReplicateEvent[ServiceID, 0] -= new ReplicateHandler(GlobalStore_Replicate);
            Core.GlobalNet.Store.PatchEvent[ServiceID, 0] -= new PatchHandler(GlobalStore_Patch);
            Core.GlobalNet.Searches.SearchEvent[ServiceID, 0] -= new SearchRequestHandler(GlobalSearch_Local);

            Core.OperationNet.Store.StoreEvent[ServiceID, 0] -= new StoreHandler(OperationStore_Local);
            Core.OperationNet.Searches.SearchEvent[ServiceID, 0] -= new SearchRequestHandler(OperationSearch_Local);
        }

        void Core_SecondTimer()
        {
            // global publish
            if (Core.Firewall == FirewallType.Open && 
                Core.GlobalNet != null && 
                Core.GlobalNet.Responsive && 
                Core.TimeNow > NextGlobalPublish)
                PublishGlobal();

            // operation publish
            if (Core.OperationNet.Responsive && Core.TimeNow > NextLocationUpdate)
                UpdateLocation();

            // run code below every quarter second
            int second = Core.TimeNow.Second + 1; // get 1 - 60 value
            if (second % 15 != 0)
                return;

            // prune global keys
            if (GlobalIndex.SafeCount > PruneGlobalKeys)
                GlobalIndex.LockWriting(delegate()
                {
                    while (GlobalIndex.Count > PruneGlobalKeys / 2)
                    {
                        ulong furthest = Core.GlobalNet.LocalUserID;

                        foreach (ulong id in GlobalIndex.Keys)
                            if ((id ^ Core.GlobalNet.LocalUserID) > (furthest ^ Core.GlobalNet.LocalUserID))
                                furthest = id;

                        GlobalIndex.Remove(furthest);
                    }
                });

            // prune global entries
            GlobalIndex.LockReading(delegate()
            {
                foreach (LinkedList<CryptLoc> list in GlobalIndex.Values)
                    if (list.Count > PruneGlobalEntries)
                        while (list.Count > PruneGlobalEntries / 2)
                            list.RemoveLast();
            });

            
       
            // operation ttl (similar as above, but not the same)
            if (second % 15 == 0)
            {
                Dictionary<ulong, bool> affectedUsers = new Dictionary<ulong, bool>();
                List<ushort> deadClients = new List<ushort>();

                LocationMap.LockReading(delegate()
                {
                     foreach (ThreadedDictionary<ushort, ClientInfo> clients in LocationMap.Values)
                     {
                         deadClients.Clear();

                         clients.LockReading(delegate()
                         {
                             foreach (ClientInfo location in clients.Values)
                             {
                                 if (second == 60)
                                 {
                                     if (location.TTL > 0)
                                         location.TTL--;

                                     if (location.TTL == 0)
                                     {
                                         deadClients.Add(location.ClientID);
                                         affectedUsers[location.Data.UserID] = true;
                                     }
                                 }

                                 //crit hack - last 30 and 15 secs before loc destroyed do searches (working pretty good through...)
                                 if (location.TTL == 1 && (second == 15 || second == 30))
                                     StartSearch(location.Data.UserID, 0, false);
                             }
                         });

                         foreach (ushort dead in deadClients)
                             clients.SafeRemove(dead);
                     }                    
                });

                LocationMap.LockWriting(delegate()
                {
                    foreach (ulong id in affectedUsers.Keys)
                        if (LocationMap[id].SafeCount == 0)
                            LocationMap.Remove(id);
                });

                foreach (ulong id in affectedUsers.Keys)
                    Core.RunInGuiThread(GuiUpdate, id);
            }
        }

        void Core_MinuteTimer()
        {

            // prune op locs
            List<ulong> removeIDs = new List<ulong>();
            List<ushort> removeClients = new List<ushort>();

            LocationMap.LockReading(delegate()
            {
                foreach (ulong id in LocationMap.Keys)
                {
                    if (LocationMap.Count > PruneLocations &&
                        id != Core.UserID &&
                        !Core.Focused.SafeContainsKey(id) &&
                        !Core.OperationNet.Routing.InCacheArea(id))
                        removeIDs.Add(id);
                }
            });

            if (removeIDs.Count > 0)
                LocationMap.LockWriting(delegate()
                {
                    while (removeIDs.Count > 0 && LocationMap.Count > PruneLocations / 2)
                    {
                        ulong furthest = Core.UserID;

                        foreach (ulong id in removeIDs)
                            if ((id ^ Core.UserID) > (furthest ^ Core.UserID))
                                furthest = id;

                        LocationMap.Remove(furthest);
                        Core.RunInGuiThread(GuiUpdate, furthest);
                        removeIDs.Remove(furthest);
                    }
                });

           

            // global ttl, once per minute
            removeIDs.Clear();

            GlobalIndex.LockReading(delegate()
            {
                List<CryptLoc> removeList = new List<CryptLoc>();

                foreach (ulong key in GlobalIndex.Keys)
                {
                    removeList.Clear();

                    foreach (CryptLoc loc in GlobalIndex[key])
                    {
                        if (loc.TTL > 0)
                            loc.TTL--;

                        if (loc.TTL == 0)
                            removeList.Add(loc);
                    }

                    foreach (CryptLoc loc in removeList)
                        GlobalIndex[key].Remove(loc);

                    if (GlobalIndex[key].Count == 0)
                        removeIDs.Add(key);
                }


            });

            GlobalIndex.LockWriting(delegate()
            {
                foreach (ulong key in removeIDs)
                    GlobalIndex.Remove(key);
            });

            // clean research map
            removeIDs.Clear();

            foreach (KeyValuePair<ulong, DateTime> pair in NextResearch)
                if (Core.TimeNow > pair.Value)
                    removeIDs.Add(pair.Key);

            if (removeIDs.Count > 0)
                foreach (ulong id in removeIDs)
                    NextResearch.Remove(id);
        }

        public List<MenuItemInfo> GetMenuInfo(InterfaceMenuType menuType, ulong user, uint project)
        {
            return null;
        }

        internal void UpdateLocation()
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreBlocked(delegate() { UpdateLocation(); });
                return;
            }

            // do next update a minute before current update expires
            NextLocationUpdate = Core.TimeNow.AddMinutes(LocationData.OP_TTL - 1);

            LocationData location = GetLocalLocation();
            
            byte[] signed = SignedData.Encode(Core.OperationNet.Protocol, Core.User.Settings.KeyPair, location);

            Debug.Assert(location.TTL < 5);
            Core.OperationNet.Store.PublishNetwork(Core.UserID, ServiceID, 0, signed);

            OperationStore_Local(new DataReq(null, Core.UserID, ServiceID, 0, signed));
        }

        internal void StartSearch(ulong id, uint version, bool global)
        {
            DhtNetwork network = global ? Core.GlobalNet : Core.OperationNet;

            byte[] parameters = BitConverter.GetBytes(version);

            DhtSearch search = network.Searches.Start(id, "Location", ServiceID, 0, parameters, new EndSearchHandler(EndSearch));

            if (search != null)
                search.TargetResults = 2;
        }

        internal void EndSearch(DhtSearch search)
        {
            foreach (SearchValue found in search.FoundValues)
            {
                DataReq store = new DataReq(found.Sources, search.TargetID, ServiceID, 0, found.Value);

                if (search.Network.IsGlobal)
                    GlobalStore_Local(store);
                else
                    OperationStore_Local(store);
            }
        }

        void GlobalSearch_Local(ulong key, byte[] parameters, List<byte[]> results)
        {
            GlobalIndex.LockReading(delegate()
            {
                if (GlobalIndex.ContainsKey(key))
                    foreach (CryptLoc loc in GlobalIndex[key])
                        results.Add(loc.Encode(Core.GlobalNet.Protocol));
            });
        }

        void OperationSearch_Local(ulong key, byte[] parameters, List<byte[]> results)
        {
            uint minVersion = BitConverter.ToUInt32(parameters, 0);

            List<ClientInfo> clients = GetClients(key);

            foreach (ClientInfo info in clients)
                if (info.Data.Version >= minVersion)
                    results.Add(info.SignedData);
        }

        void GlobalStore_Local(DataReq crypt)
        {
            CryptLoc newLoc = CryptLoc.Decode(crypt.Data);

            if (newLoc == null)
                return;

            // check if data is for our operation, if it is use it
            if (crypt.Target == Core.OperationNet.OpID)
            {
                DataReq store = new DataReq(null, Core.OperationNet.OpID, ServiceID, 0, newLoc.Data);

                if (Core.Sim == null || Core.Sim.Internet.TestEncryption)
                    store.Data = Utilities.DecryptBytes(store.Data, store.Data.Length, Core.OperationNet.OriginalCrypt);

                store.Sources = null; // dont pass global sources to operation store 

                OperationStore_Local(store);
            }

            // index location 
            LinkedList<CryptLoc> locations = null;

            if (GlobalIndex.SafeTryGetValue(crypt.Target, out locations))
            {
                foreach (CryptLoc location in locations)
                    if (Utilities.MemCompare(crypt.Data, location.Data))
                    {
                        if (newLoc.TTL > location.TTL)
                            location.TTL = newLoc.TTL;

                        return;
                    }
            }
            else
            {
                locations = new LinkedList<CryptLoc>();
                GlobalIndex.SafeAdd(crypt.Target, locations);
            }

            locations.AddFirst(newLoc);
        }

        void OperationStore_Local(DataReq store)
        {
            // getting published to - search results - patch

            SignedData signed = SignedData.Decode(store.Data);

            if (signed == null)
                return;
            
            G2Header embedded = new G2Header(signed.Data);

            // figure out data contained
            if (G2Protocol.ReadPacket(embedded))
                if (embedded.Name == LocPacket.LocationData)
                {
                    LocationData location = LocationData.Decode(signed.Data);

                    if (Utilities.CheckSignedData(location.Key, signed.Data, signed.Signature))
                        Process_LocationData(store, signed, location);
                }
        }

        private void Process_LocationData(DataReq data, SignedData signed, LocationData location)
        {
            Core.IndexKey(location.UserID, ref location.Key);

            ClientInfo current = GetLocationInfo(location.UserID, location.Source.ClientID);
           
            // check location version
            if (current != null)
            {
                if (location.Version == current.Data.Version)
                    return;

                else if (location.Version < current.Data.Version)
                {
                    if (data != null && data.Sources != null)
                        foreach (DhtAddress source in data.Sources)
                            Core.OperationNet.Store.Send_StoreReq(source, data.LocalProxy, new DataReq(null, current.Data.UserID, ServiceID, 0, current.SignedData));

                    return;
                }
            }

            
            // notify components of new versions
            DhtAddress address = new DhtAddress(location.IP, location.Source);

            foreach (PatchTag tag in location.Tags)
                if(TagReceived.Contains(tag.Service, tag.DataType))
                    TagReceived[tag.Service, tag.DataType].Invoke(address, location.UserID, tag.Tag);


            // add location
            if (current == null)
            {
                ThreadedDictionary<ushort, ClientInfo> locations = null;

                if (!LocationMap.SafeTryGetValue(location.UserID, out locations))
                {
                    locations = new ThreadedDictionary<ushort, ClientInfo>();
                    LocationMap.SafeAdd(location.UserID, locations);
                }

                // if too many clients, and not us, return
                if (location.UserID != Core.UserID && locations.SafeCount > MaxClientsperUser)
                    return;

                current = new ClientInfo(location.Source.ClientID);
                locations.SafeAdd(location.Source.ClientID, current);
            }

            current.Data = location;
            current.SignedData = signed.Encode(Core.OperationNet.Protocol);

            if (current.Data.UserID == Core.UserID && current.Data.Source.ClientID == Core.OperationNet.ClientID)
                LocalLocation = current;

            current.TTL = location.TTL;

            
            // if open and not global, add to routing
            if (location.Source.Firewall == FirewallType.Open && !location.Global)
                Core.OperationNet.Routing.Add(new DhtContact(location.Source, location.IP, Core.TimeNow));

            //crit add global addr/global proxies to global routing and op routing?

            if (LocationUpdate != null)
                LocationUpdate.Invoke(current.Data);

            Core.RunInGuiThread(GuiUpdate, current.Data.UserID);
        }

        internal void PublishGlobal()
        {
            // should be auto-set like a second after tcp connect
            // this isnt called until 15s after tcp connect
            if (Core.LocalIP == null)
                return;

            // set next publish time 55 mins
            NextGlobalPublish = Core.TimeNow.AddMinutes(55);

            LocationData location = GetLocalLocation();

            
            // location packet is encrypted inside global loc packet
            // this embedded has OP TTL, while wrapper (CryptLoc) has global TTL

            byte[] data = SignedData.Encode(Core.GlobalNet.Protocol, Core.User.Settings.KeyPair, location);

            if (Core.Sim == null || Core.Sim.Internet.TestEncryption)
                data = Utilities.EncryptBytes(data, Core.OperationNet.OriginalCrypt);

            data = new CryptLoc(LocationData.GLOBAL_TTL, data).Encode(Core.GlobalNet.Protocol);

            Core.GlobalNet.Store.PublishNetwork(Core.OperationNet.OpID, ServiceID, 0, data);

            GlobalStore_Local(new DataReq(null, Core.OperationNet.OpID, ServiceID, 0, data));
        }

        private LocationData GetLocalLocation()
        {
            LocationData location = new LocationData();

            location.Key = Core.User.Settings.KeyPublic;
            location.IP = Core.LocalIP;
            location.TTL = LocationData.OP_TTL;


            // if everyone on network behind firewall publish location of global proxies
            if (Core.UseGlobalProxies)
            {
                location.Global = true;
                location.Source = Core.GlobalNet.GetLocalSource();

                foreach (TcpConnect socket in Core.GlobalNet.TcpControl.ProxyServers)
                    location.Proxies.Add(new DhtAddress(socket.RemoteIP, socket));
            }
            else
            {
                location.Source = Core.OperationNet.GetLocalSource();

                foreach (TcpConnect connect in Core.OperationNet.TcpControl.ProxyServers)
                    location.Proxies.Add(new DhtAddress(connect.RemoteIP, connect));
            }

            location.Place = Core.User.Settings.Location;
            location.GmtOffset = System.TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now).Minutes;
            location.Away = LocalAway;
            location.AwayMessage = LocalAway ? Core.User.Settings.AwayMessage : "";

            location.Version = LocationVersion++;

            foreach (uint service in GetTag.HandlerMap.Keys)
                foreach (uint datatype in GetTag.HandlerMap[service].Keys)
                {
                    PatchTag tag = new PatchTag();

                    tag.Service = service;
                    tag.DataType = datatype;
                    tag.Tag = GetTag[service, datatype].Invoke();

                    if (tag.Tag != null)
                    {
                        Debug.Assert(tag.Tag.Length < 8);

                        if (tag.Tag.Length < 8)
                            location.Tags.Add(tag);
                    }
                }

            return location;
        }


        List<byte[]> GlobalStore_Replicate(DhtContact contact)
        {
            //crit
            // just send little piece of first 8 bytes, if remote doesnt have it, it is requested through params with those 8 bytes

            return null;
        }

        void GlobalStore_Patch(DhtAddress source, byte[] data)
        {

        }

        internal ClientInfo GetLocationInfo(ulong user, ushort client)
        {
            ThreadedDictionary<ushort, ClientInfo> locations = null;

            if (LocationMap.SafeTryGetValue(user, out locations))
            {
                ClientInfo info = null;
                if (locations.SafeTryGetValue(client, out info))
                    return info;
            }

            return null;
        }

        internal string GetLocationName(ulong user, ushort client)
        {
            ClientInfo current = Core.Locations.GetLocationInfo(user, client);

            if(current == null)
                return client.ToString();

            LocationData data = current.Data;

            if (data == null || data.Place == null || data.Place == "")
                return data.IP.ToString();

            return data.Place;
        }

        internal void Research(ulong user)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { Research(user); });
                return;
            }

            if (!Core.OperationNet.Responsive)
                return;

            // limit re-search to once per 30 secs
            DateTime timeout = default(DateTime);

            if (NextResearch.TryGetValue(user, out timeout))
                if (Core.TimeNow < timeout)
                    return;

            StartSearch(user, 0, false);

            NextResearch[user] = Core.TimeNow.AddSeconds(30);
        }

        internal int ActiveClientCount(ulong user)
        {
            int count = 0;
            ThreadedDictionary<ushort, ClientInfo> locations = null;

            if (LocationMap.SafeTryGetValue(user, out locations))
                locations.LockReading(delegate()
                {
                    foreach (ClientInfo location in locations.Values)
                        count++;
                });

            return count;
        }

        internal List<ClientInfo> GetClients(ulong user)
        {
            //crit needs to change when global proxying implemented

            List<ClientInfo> results = new List<ClientInfo>();
            
            ThreadedDictionary<ushort, ClientInfo> clients = null;

            if(!LocationMap.SafeTryGetValue(user, out clients))
                return results;

            clients.LockReading(delegate()
            {
                foreach (ClientInfo info in clients.Values)
                    if (!info.Data.Global)
                        results.Add(info);
            });

            return results;
        }
    }

    internal class ClientInfo
    {
        internal LocationData Data;

        internal byte[] SignedData;

        internal uint TTL;
        internal ushort ClientID;

        internal ClientInfo(ushort id)
        {
            ClientID = id;
        }
    }


    internal class PatchTag
    {
        internal uint Service;
        internal uint DataType;
        internal byte[] Tag;

        internal byte[] ToBytes()
        {
            byte[] sByte = CompactNum.GetBytes(Service);
            byte[] dByte = CompactNum.GetBytes(DataType);
            
            byte control = (byte) (sByte.Length << 3);
            control |= (byte) dByte.Length;

            int size = 1 + sByte.Length + dByte.Length + Tag.Length;

            byte[] data = new byte[size];

            data[0] = control;
            sByte.CopyTo(data, 1);
            dByte.CopyTo(data, 1 + sByte.Length);
            Tag.CopyTo(data, 1 + sByte.Length + dByte.Length);

            return data;
        }

        internal static PatchTag FromBytes(byte[] data, int pos, int size)
        {
            PatchTag tag = new PatchTag();

            byte control = data[pos];
            int sLength = (control & 0x38) >> 3;
            int dLength = (control & 0x07);

            tag.Service = CompactNum.ToUInt32(data, pos + 1, sLength);
            tag.DataType = CompactNum.ToUInt32(data, pos + 1 + sLength, dLength);

            int dataPos = 1 + sLength + dLength;
            tag.Tag = Utilities.ExtractBytes(data, pos + dataPos, size - dataPos);

            return tag;
        }
    }
}
