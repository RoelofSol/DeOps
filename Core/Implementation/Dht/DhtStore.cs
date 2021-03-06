using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Net;

using DeOps.Implementation;
using DeOps.Implementation.Transport;
using DeOps.Implementation.Protocol;
using DeOps.Implementation.Protocol.Net;

using DeOps.Services;
using DeOps.Services.Location;


namespace DeOps.Implementation.Dht
{
    public delegate void StoreHandler(DataReq data);
    public delegate List<byte[]> ReplicateHandler(DhtContact contact);
    public delegate void PatchHandler(DhtAddress source, byte[] data);

    public class DhtStore
    {
        //super-class
        OpCore Core;
        DhtNetwork Network; 

        public ServiceEvent<StoreHandler> StoreEvent = new ServiceEvent<StoreHandler>();
        public ServiceEvent<ReplicateHandler> ReplicateEvent = new ServiceEvent<ReplicateHandler>(); // this event doesnt support overloading
        public ServiceEvent<PatchHandler> PatchEvent = new ServiceEvent<PatchHandler>();


        public DhtStore(DhtNetwork network)
        {
            Network = network;
            Core = Network.Core;
        }

        public void PublishNetwork(ulong target, uint service, uint datatype, byte[] data)
        {
            if (Core.InvokeRequired)
                Debug.Assert(false);

            string type = "Publish " + Core.GetServiceName(service);

            if(target == Core.UserID)
                Network.UpdateLog("general", "Publishing " + Core.GetServiceName(service));

            DataReq store = new DataReq(null, target, service, datatype, data);

            // find users closest to publish target
            if (target == Network.Local.UserID)
            {
                foreach (DhtContact closest in Network.Routing.GetCacheArea())
                    Send_StoreReq(closest, null, store);

                foreach (TcpConnect socket in Network.TcpControl.ProxyClients)
                    Send_StoreReq(new DhtAddress(socket.RemoteIP, socket), null, store);
            }
            else
            {
                DhtSearch search = Network.Searches.Start(target, type, 0, 0, null, null);

                if (search != null)
                {
                    search.DoneEvent = Search_DonePublish;
                    search.Carry = store;
                }
            }
        }

        void Search_DonePublish(DhtSearch search)
        {
            DataReq publish = (DataReq)search.Carry;

            // need to carry over componentid that wanted search also so store works

            foreach (DhtLookup node in search.LookupList)
                Send_StoreReq(node.Contact, null, publish);
        }

        public void PublishDirect(List<LocationData> locations, ulong target, uint service, uint datatype, byte[] data)
        {
            if (Core.InvokeRequired)
            {
                Core.RunInCoreAsync(delegate() { PublishDirect(locations, target, service, datatype, data); });
                return;
            }

            DataReq req = new DataReq(null, target, service, datatype, data);
            
            foreach (LocationData location in locations)
            {
                DhtAddress address = new DhtAddress(location.IP, location.Source);
                Send_StoreReq(address, null, req);

                foreach (DhtAddress proxy in location.Proxies)
                    Send_StoreReq(proxy, null, req);
            }
        }

        public void Send_StoreReq(DhtAddress address, DhtClient localProxy, DataReq publish)
        {
            if (address == null)
                return;

            StoreReq store = new StoreReq();
            store.Source    = Network.GetLocalSource();
            store.Key       = publish.Target;
            store.Service   = publish.Service;
            store.DataType  = publish.DataType;
            store.Data      = publish.Data;

            int sentBytes = 0;
  
            TcpConnect direct = Network.TcpControl.GetProxy(address);

            if (direct != null)
                sentBytes = direct.SendPacket(store);

            else if (address.TunnelClient != null)
                sentBytes = Network.SendTunnelPacket(address, store);

            // if blocked send tcp with to tag
            else if (Core.Firewall == FirewallType.Blocked)
            {
                store.ToAddress = address;

                TcpConnect proxy = Network.TcpControl.GetProxy(localProxy);

                if (proxy != null)
                    sentBytes = proxy.SendPacket(store);
                else
                    sentBytes = Network.TcpControl.SendRandomProxy(store);
            }
            else
                sentBytes = Network.UdpControl.SendTo(address, store);

            Core.ServiceBandwidth[store.Service].OutPerSec += sentBytes;
        }

        public void Receive_StoreReq(G2ReceivedPacket packet)
        {
            StoreReq store = StoreReq.Decode(packet);

            if (Core.ServiceBandwidth.ContainsKey(store.Service))
                Core.ServiceBandwidth[store.Service].InPerSec += packet.Root.Data.Length;

            if (store.Source.Firewall == FirewallType.Open )
                    // dont need to add to routing if nat/blocked because eventual routing ping by server will auto add
                    Network.Routing.Add(new DhtContact(store.Source, packet.Source.IP));


            // forward to proxied nodes - only replicate data to blocked nodes on operation network
            if (!Network.IsLookup)
                // when we go offline it will be these nodes that update their next proxy with stored info
                foreach (TcpConnect socket in Network.TcpControl.ProxyClients)
                    if (packet.Tcp != socket)
                    {
                        if (packet.ReceivedUdp)
                            store.FromAddress = packet.Source;

                        socket.SendPacket(store);
                    }

            // pass to components
            DataReq data = new DataReq(packet.Source, store.Key, store.Service, store.DataType, store.Data);

            if (packet.ReceivedTcp && packet.Tcp.Proxy == ProxyType.Server)
                data.LocalProxy = new DhtClient(packet.Tcp);

            if(data.Service == 0)
                Receive_Patch(packet.Source, store.Data);

            else if (StoreEvent.Contains(store.Service, store.DataType))
                StoreEvent[store.Service, store.DataType].Invoke(data);
        }

        public void Replicate(DhtContact contact)
        {
            // dont replicate to local region until we've established our position in the dht
            if (!Network.Established)
                return;


            // when new user comes into our cache area, we send them the data we have in our high/low/xor bounds

            // replicate is only for cached area
            // for remote user stuff that loads up with client, but now out of bounds, it is
            // republished by the uniqe modifier on data

            List<PatchTag> PatchList = new List<PatchTag>();

            // get data that needs to be replicated from components
            // structure as so
            //      contact
            //          service [] 
            //              datatype []
            //                  patch data []

            foreach (uint service in ReplicateEvent.HandlerMap.Keys)
                foreach (uint datatype in ReplicateEvent.HandlerMap[service].Keys)
                {
                    List<byte[]> patches = ReplicateEvent.HandlerMap[service][datatype].Invoke(contact);

                    if(patches != null)
                        foreach (byte[] data in patches)
                        {
                            PatchTag patch = new PatchTag();
                            patch.Service = service;
                            patch.DataType = datatype;
                            patch.Tag = data;

                            PatchList.Add(patch);
                        }
                }

            PatchPacket packet = new PatchPacket();

            int totalSize = 0;

            foreach (PatchTag patch in PatchList)
            {
                if (patch.Tag.Length + totalSize > 1000)
                {
                    if (packet.PatchData.Count > 0)
                        Send_StoreReq(contact, contact, new DataReq(null, contact.UserID, 0, 0, packet.Encode(Network.Protocol)));

                    packet.PatchData.Clear();
                    totalSize = 0;
                }

                packet.PatchData.Add(patch);
                totalSize += patch.Tag.Length;
            }

            if (packet.PatchData.Count > 0)
                Send_StoreReq(contact, contact, new DataReq(null, contact.UserID, 0, 0, packet.Encode(Network.Protocol)));

        }

        private void Receive_Patch(DhtAddress source, byte[] data)
        {
            // invoke patch
            G2Header root = new G2Header(data);

            if (G2Protocol.ReadPacket(root))
                if (root.Name == StorePacket.Patch)
                {
                    PatchPacket packet = PatchPacket.Decode(root);

                    if (packet == null)
                        return;

                    foreach (PatchTag patch in packet.PatchData)
                        if (PatchEvent.Contains(patch.Service, patch.DataType))
                            PatchEvent[patch.Service, patch.DataType].Invoke(source, patch.Tag);
                }
        }
    }

    public class DataReq
    {
        public DhtAddress Source;
        public DhtClient LocalProxy;

        public ulong  Target;
        public uint Service;
        public uint DataType;
        public byte[] Data;

        public DataReq(DhtAddress source, ulong target, uint service, uint datatype, byte[] data)
        {
            Source    = source;
            Target    = target;
            Service   = service;
            DataType  = datatype;
            Data      = data;
        }
    }

    public class StorePacket
    {
        public const byte Patch = 0x10;
    }

    public class PatchPacket : G2Packet
    {
        const byte Packet_Patch = 0x01;

        public List<PatchTag> PatchData = new List<PatchTag>();

        public PatchPacket()
        {

        }

        public override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                G2Frame patch = protocol.WritePacket(null, StorePacket.Patch, null);

                foreach (PatchTag tag in PatchData)
                    protocol.WritePacket(patch, Packet_Patch, tag.ToBytes());

                return protocol.WriteFinish();
            }
        }

        public static PatchPacket Decode(G2Header root)
        {
            PatchPacket patch = new PatchPacket();
            G2Header child = new G2Header(root.Data);

            while (G2Protocol.ReadNextChild(root, child) == G2ReadResult.PACKET_GOOD)
                if (child.Name == Packet_Patch && G2Protocol.ReadPayload(child))
                    patch.PatchData.Add((PatchTag) PatchTag.FromBytes(child.Data, child.PayloadPos, child.PayloadSize));

            return patch;
        }
    }
}
