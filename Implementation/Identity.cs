using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Xml;

using RiseOp.Implementation;
using RiseOp.Implementation.Dht;
using RiseOp.Implementation.Protocol;
using RiseOp.Implementation.Protocol.Net;
using RiseOp.Implementation.Protocol.Special;


namespace RiseOp
{

    internal enum LoadModeType { Settings, AllCaches, GlobalCache };

    internal enum AccessType { Public, Private, Secret };

	/// <summary>
	/// Summary description for KimProfile.
	/// </summary>
	internal class Identity
	{
        internal OpCore Core;
        internal G2Protocol Protocol;

		internal string ProfilePath;
        internal string RootPath;
        internal string TempPath;
		internal RijndaelManaged Password;

        internal SettingsPacket Settings = new SettingsPacket();


        internal Identity(string filepath, string password, OpCore core)
        {
            Core = core;
            Protocol = Core.GuiProtocol;

            Init(filepath, password);
        }

		internal Identity(string filepath, string password, G2Protocol protocol)
		{
            // used when creating new ident
            Protocol    = protocol;

            Init(filepath, password);
        }

        private void Init(string filepath, string password)
        {
			ProfilePath = filepath;
			Password    = Utilities.PasswordtoRijndael(password);

            RootPath = Path.GetDirectoryName(filepath);
            TempPath = RootPath + Path.DirectorySeparatorChar + "Data" + Path.DirectorySeparatorChar + "0";
            Directory.CreateDirectory(TempPath);

            Random rndGen = new Random(unchecked((int)DateTime.Now.Ticks));

			// default settings, set tcp/udp the same so forwarding is easier
            Settings.TcpPort = (ushort)rndGen.Next(5000, 9000);
            Settings.UdpPort = Settings.TcpPort;
		}

		internal void Load(LoadModeType loadMode)
		{
			FileStream readStream = null;

			try
			{
                readStream = new FileStream(ProfilePath, FileMode.Open);
                CryptoStream decStream = new CryptoStream(readStream, Password.CreateDecryptor(), CryptoStreamMode.Read);
                PacketStream stream = new PacketStream(decStream, Protocol, FileAccess.Read);

                G2Header root = null;

                while (stream.ReadPacket(ref root))
                {
                    if (loadMode == LoadModeType.Settings)
                        if (root.Name == IdentityPacket.OperationSettings)
                        {
                            Settings = SettingsPacket.Decode(root);
                            break;
                        }

                    if (root.Name == IdentityPacket.GlobalCache && Core.Context.Global != null &&
                        (loadMode == LoadModeType.AllCaches || loadMode == LoadModeType.GlobalCache))
                        Core.Context.Global.Network.AddCacheEntry(SavedPacket.Decode(root).Contact);

                    if (root.Name == IdentityPacket.OperationCache && loadMode == LoadModeType.AllCaches)
                        Core.Network.AddCacheEntry(SavedPacket.Decode(root).Contact);
                }

                stream.Close();
            }
			catch(Exception ex)
			{
				if(readStream != null)
					readStream.Close();

				throw ex;
			}
		}

        internal void Save()
        {
            string backupPath = ProfilePath.Replace(".rop", ".bak");

			if( !File.Exists(backupPath) && File.Exists(ProfilePath))
				File.Copy(ProfilePath, backupPath, true);

            try
            {
                // Attach to crypto stream and write file
                FileStream file = new FileStream(ProfilePath, FileMode.Create);
                CryptoStream crypto = new CryptoStream(file, Password.CreateEncryptor(), CryptoStreamMode.Write);
                PacketStream stream = new PacketStream(crypto, Protocol, FileAccess.Write);

                stream.WritePacket(Settings);

                if (Core != null)
                {
                    if (Core.Context.Global != null)
                        SaveCache(stream, Core.Context.Global.Network.IPCache, IdentityPacket.GlobalCache);

                    SaveCache(stream, Core.Network.IPCache, IdentityPacket.OperationCache);
                }

                stream.Close();
            }

            catch (Exception ex)
            {
                if (Core != null)
                    Core.ConsoleLog("Exception KimProfile::Save() " + ex.Message);
                else
                    System.Windows.Forms.MessageBox.Show("Profile Save Error:\n" + ex.Message + "\nBackup Restored");

                // restore backup
                if (File.Exists(backupPath))
                    File.Copy(backupPath, ProfilePath, true);
            }

            File.Delete(backupPath);
        }

        internal static void SaveCache(PacketStream stream, LinkedList<DhtContact> cache, byte type)
        {
            lock (cache)
                foreach (DhtContact entry in cache)
                    if (entry.TunnelClient == null) 
                        stream.WritePacket(new SavedPacket(type, entry));
        }

        internal static void CreateNew(string path, string opName, string userName, string password, AccessType access, OneWayInvite invite)
        {
            Identity user = new Identity(path, password, new G2Protocol());
            user.Settings.Operation = opName;
            user.Settings.UserName = userName;
            user.Settings.KeyPair = new RSACryptoServiceProvider(1024);
            user.Settings.OpKey = new RijndaelManaged();
            user.Settings.FileKey = new RijndaelManaged();
            user.Settings.FileKey.GenerateKey();
            user.Settings.OpAccess = access;

            // joining/creating public
            if (access == AccessType.Public)
            {
                // 256 bit rijn
                
                SHA256Managed sha256 = new SHA256Managed();
                user.Settings.OpKey.Key = sha256.ComputeHash(UTF8Encoding.UTF8.GetBytes(opName));
            }

            // invite to private/secret
            else if (invite != null)
                user.Settings.OpKey.Key = invite.OpID;

            // creating private/secret
            else
                user.Settings.OpKey.GenerateKey();


            user.Save();

            // throws exception on failure
        }
    }

    internal class SavedPacket : G2Packet
    {
        internal const byte Packet_Contact = 0x10;
        internal const byte Packet_LastSeen = 0x20;


        internal byte Name;
        internal DateTime LastSeen;
        internal DhtContact Contact;


        internal SavedPacket() { }

        internal SavedPacket(byte name, DhtContact contact)
        {
            Name = name;
            LastSeen = contact.LastSeen;
            Contact = contact;
        }

        internal override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                G2Frame saved = protocol.WritePacket(null, Name, null);

                Contact.WritePacket(protocol, saved, Packet_Contact);

                protocol.WritePacket(saved, Packet_LastSeen, BitConverter.GetBytes(LastSeen.ToBinary()));

                return protocol.WriteFinish();
            }
        }

        internal static SavedPacket Decode(G2Header root)
        {
            SavedPacket saved = new SavedPacket();

			G2Header child = new G2Header(root.Data);

            while (G2Protocol.ReadNextChild(root, child) == G2ReadResult.PACKET_GOOD)
            {
                if (!G2Protocol.ReadPayload(child))
                    continue;

                switch (child.Name)
                {
                    case Packet_Contact:
                        saved.Contact = DhtContact.ReadPacket(child);
                        break;

                    case Packet_LastSeen:
                        saved.LastSeen = DateTime.FromBinary(BitConverter.ToInt32(child.Data, child.PayloadPos));
                        break;
                }
            }

            saved.Contact.LastSeen = saved.LastSeen;

            return saved;
        }
    }

    internal class IdentityPacket
    {
        internal const byte OperationSettings  = 0x10;
        internal const byte GlobalSettings     = 0x20;

        internal const byte GlobalCache        = 0x30;
        internal const byte OperationCache     = 0x40;
    }

    internal class SettingsPacket : G2Packet
    {
        const byte Packet_Operation     = 0x10;
        const byte Packet_UserName      = 0x20;
        const byte Packet_TcpPort       = 0x30;
        const byte Packet_UdpPort       = 0x40;
        const byte Packet_OpKey         = 0x50;
        const byte Packet_OpAccess      = 0x60;
        const byte Packet_KeyPair       = 0x70;
        const byte Packet_Location      = 0x80;
        const byte Packet_FileKey       = 0x90;
        const byte Packet_AwayMsg       = 0xA0;

        const byte Key_D        = 0x10;
        const byte Key_DP       = 0x20;
        const byte Key_DQ       = 0x30;
        const byte Key_Exponent = 0x40;
        const byte Key_InverseQ = 0x50;
        const byte Key_Modulus  = 0x60;
        const byte Key_P        = 0x70;
        const byte Key_Q        = 0x80;


        // general
        internal string Operation;
        internal string UserName;
        internal string Location = "";
        internal string AwayMessage = "";

        // network
        internal ushort TcpPort;
        internal ushort UdpPort;

        // private
        internal RijndaelManaged OpKey = new RijndaelManaged();
        internal AccessType OpAccess;

        internal RSACryptoServiceProvider KeyPair = new RSACryptoServiceProvider();
        internal byte[] KeyPublic;

        internal RijndaelManaged FileKey = new RijndaelManaged();


        internal SettingsPacket()
        {
        }

        internal override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                G2Frame settings = protocol.WritePacket(null, IdentityPacket.OperationSettings, null);

                protocol.WritePacket(settings, Packet_Operation, UTF8Encoding.UTF8.GetBytes(Operation));
                protocol.WritePacket(settings, Packet_UserName, UTF8Encoding.UTF8.GetBytes(UserName));              
                protocol.WritePacket(settings, Packet_TcpPort, BitConverter.GetBytes(TcpPort));
                protocol.WritePacket(settings, Packet_UdpPort, BitConverter.GetBytes(UdpPort));
                protocol.WritePacket(settings, Packet_Location, UTF8Encoding.UTF8.GetBytes(Location));
                protocol.WritePacket(settings, Packet_AwayMsg, UTF8Encoding.UTF8.GetBytes(AwayMessage));

                protocol.WritePacket(settings, Packet_FileKey, FileKey.Key);
                protocol.WritePacket(settings, Packet_OpKey, OpKey.Key);
                protocol.WritePacket(settings, Packet_OpAccess, BitConverter.GetBytes((byte)OpAccess));

                RSAParameters rsa = KeyPair.ExportParameters(true);
                G2Frame key = protocol.WritePacket(settings, Packet_KeyPair, null);
                protocol.WritePacket(key, Key_D, rsa.D);
                protocol.WritePacket(key, Key_DP, rsa.DP);
                protocol.WritePacket(key, Key_DQ, rsa.DQ);
                protocol.WritePacket(key, Key_Exponent, rsa.Exponent);
                protocol.WritePacket(key, Key_InverseQ, rsa.InverseQ);
                protocol.WritePacket(key, Key_Modulus, rsa.Modulus);
                protocol.WritePacket(key, Key_P, rsa.P);
                protocol.WritePacket(key, Key_Q, rsa.Q);

                return protocol.WriteFinish();
            }
        }

        internal static SettingsPacket Decode(G2Header root)
        {
            SettingsPacket settings = new SettingsPacket();

            G2Header child = new G2Header(root.Data);

            while (G2Protocol.ReadNextChild(root, child) == G2ReadResult.PACKET_GOOD)
            {
                if (child.Name == Packet_KeyPair)
                {
                    DecodeKey(child, settings);
                    continue;
                }

                if (!G2Protocol.ReadPayload(child))
                    continue;

                switch (child.Name)
                {
                    case Packet_Operation:
                        settings.Operation = UTF8Encoding.UTF8.GetString(child.Data, child.PayloadPos, child.PayloadSize);
                        break;

                    case Packet_UserName:
                        settings.UserName = UTF8Encoding.UTF8.GetString(child.Data, child.PayloadPos, child.PayloadSize);
                        break;

                    case Packet_TcpPort:
                        settings.TcpPort = BitConverter.ToUInt16(child.Data, child.PayloadPos);
                        break;

                    case Packet_UdpPort:
                        settings.UdpPort = BitConverter.ToUInt16(child.Data, child.PayloadPos);
                        break;

                    case Packet_OpKey:
                        settings.OpKey.Key = Utilities.ExtractBytes(child.Data, child.PayloadPos, child.PayloadSize);
                        break;

                    case Packet_FileKey:
                        settings.FileKey.Key = Utilities.ExtractBytes(child.Data, child.PayloadPos, child.PayloadSize);
                        settings.FileKey.IV = new byte[settings.FileKey.IV.Length]; // set zeros
                        break;

                    case Packet_OpAccess:
                        settings.OpAccess = (AccessType)child.Data[child.PayloadPos];
                        break;

                    case Packet_Location:
                        settings.Location = UTF8Encoding.UTF8.GetString(child.Data, child.PayloadPos, child.PayloadSize);
                        break;

                    case Packet_AwayMsg:
                        settings.AwayMessage = UTF8Encoding.UTF8.GetString(child.Data, child.PayloadPos, child.PayloadSize);
                        break;
                }
            }

            return settings;
        }

        private static void DecodeKey(G2Header child, SettingsPacket settings)
        {
            G2Header key = new G2Header(child.Data);

            RSAParameters rsa = new RSAParameters();

            while (G2Protocol.ReadNextChild(child, key) == G2ReadResult.PACKET_GOOD)
            {
                if (!G2Protocol.ReadPayload(key))
                    continue;

                switch (key.Name)
                {
                    case Key_D:
                        rsa.D = Utilities.ExtractBytes(key.Data, key.PayloadPos, key.PayloadSize);
                        break;

                    case Key_DP:
                        rsa.DP = Utilities.ExtractBytes(key.Data, key.PayloadPos, key.PayloadSize);
                        break;

                    case Key_DQ:
                        rsa.DQ = Utilities.ExtractBytes(key.Data, key.PayloadPos, key.PayloadSize);
                        break;

                    case Key_Exponent:
                        rsa.Exponent = Utilities.ExtractBytes(key.Data, key.PayloadPos, key.PayloadSize);
                        break;

                    case Key_InverseQ:
                        rsa.InverseQ = Utilities.ExtractBytes(key.Data, key.PayloadPos, key.PayloadSize);
                        break;

                    case Key_Modulus:
                        rsa.Modulus = Utilities.ExtractBytes(key.Data, key.PayloadPos, key.PayloadSize);
                        break;

                    case Key_P:
                        rsa.P = Utilities.ExtractBytes(key.Data, key.PayloadPos, key.PayloadSize);
                        break;

                    case Key_Q:
                        rsa.Q = Utilities.ExtractBytes(key.Data, key.PayloadPos, key.PayloadSize);
                        break;
                }
            }

            settings.KeyPair.ImportParameters(rsa);
            settings.KeyPublic = rsa.Modulus;
        }
    }

    internal class GlobalSettings : G2Packet
    {
        const byte Packet_UserID = 0x10;
        const byte Packet_TcpPort = 0x20;
        const byte Packet_UdpPort = 0x30;

        internal ulong UserID;
        internal ushort TcpPort;
        internal ushort UdpPort;

        internal GlobalSettings()
        {
        }

        internal override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                G2Frame settings = protocol.WritePacket(null, IdentityPacket.GlobalSettings, null);

                protocol.WritePacket(settings, Packet_UserID, BitConverter.GetBytes(UserID));
                protocol.WritePacket(settings, Packet_TcpPort, BitConverter.GetBytes(TcpPort));
                protocol.WritePacket(settings, Packet_UdpPort, BitConverter.GetBytes(UdpPort));

                return protocol.WriteFinish();
            }
        }

        internal static GlobalSettings Decode(G2Header root)
        {
            GlobalSettings settings = new GlobalSettings();

            G2Header child = new G2Header(root.Data);

            while (G2Protocol.ReadNextChild(root, child) == G2ReadResult.PACKET_GOOD)
            {
                if (!G2Protocol.ReadPayload(child))
                    continue;

                if (!G2Protocol.ReadPayload(child))
                    continue;

                switch (child.Name)
                {
                    case Packet_UserID:
                        settings.UserID = BitConverter.ToUInt64(child.Data, child.PayloadPos);
                        break;

                    case Packet_TcpPort:
                        settings.TcpPort = BitConverter.ToUInt16(child.Data, child.PayloadPos);
                        break;

                    case Packet_UdpPort:
                        settings.UdpPort = BitConverter.ToUInt16(child.Data, child.PayloadPos);
                        break;
                }
            }


            return settings;
        }

        internal static GlobalSettings Load(DhtNetwork network)
        {
            // so that accross multiple ops, global access points are maintained more or less
            // also bootstrap file can be sent to others to help them out
            GlobalSettings settings = null;

            string path = Application.StartupPath + Path.DirectorySeparatorChar + "bootstrap.rop";

            if (File.Exists(path))
            {
                RijndaelManaged password = Utilities.PasswordtoRijndael("bootstrap");
                FileStream readStream = null;

                try
                {
                    readStream = new FileStream(path, FileMode.Open);
                    CryptoStream decStream = new CryptoStream(readStream, password.CreateDecryptor(), CryptoStreamMode.Read);
                    PacketStream stream = new PacketStream(decStream, network.Protocol, FileAccess.Read);

                    G2Header root = null;

                    while (stream.ReadPacket(ref root))
                    {
                        if (root.Name == IdentityPacket.GlobalSettings)
                            settings = GlobalSettings.Decode(root);

                        if (root.Name == IdentityPacket.GlobalCache)
                            network.AddCacheEntry(SavedPacket.Decode(root).Contact);
                    }

                    stream.Close();
                }
                catch (Exception ex)
                {
                    network.UpdateLog("Exception", "GlobalSettings::Load " + ex.Message);
                }
            }

            // file not found / loaded
            if (settings == null)
            {
                settings = new GlobalSettings();

                settings.UserID = Utilities.StrongRandUInt64(network.Core.StrongRndGen);
                settings.TcpPort = (ushort)network.Core.RndGen.Next(5000, 9000);
                settings.UdpPort = settings.TcpPort;
            }

            return settings;
        }

        internal void Save(OpCore core)
        {
            if (core.Sim != null) //crit test function as well as loading
                return;

            string path = Application.StartupPath + Path.DirectorySeparatorChar + "bootstrap.rop";
            RijndaelManaged password = Utilities.PasswordtoRijndael("bootstrap");

            try
            {
                // Attach to crypto stream and write file
                FileStream file = new FileStream(path, FileMode.Create);
                CryptoStream crypto = new CryptoStream(file, password.CreateEncryptor(), CryptoStreamMode.Write);
                PacketStream stream = new PacketStream(crypto, core.Network.Protocol, FileAccess.Write);

                stream.WritePacket(this);

                Identity.SaveCache(stream, core.Network.IPCache, IdentityPacket.GlobalCache);

                stream.Close();
            }

            catch (Exception ex)
            {
                core.Network.UpdateLog("Exception", "GlobalSettings::Save " + ex.Message);
            }

        }
    }

}
