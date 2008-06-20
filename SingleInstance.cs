﻿using System;
using System.Collections.Generic;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Text;
using System.Threading;
using System.Windows.Forms;


namespace RiseOp
{
    public class RiseOpMutex
    {
        private Mutex TheMutex;
        private IChannel IpcChannel;
        internal bool First;


        internal RiseOpMutex(RiseOpContext context, string[] args)
        {
            try
            {
                string name = "RiseOp" + Application.ProductVersion;

                TheMutex = new Mutex(true, name, out First);

                string objectName = "SingleInstanceProxy";
                string objectUri = "ipc://" + name + "/" + objectName;

                if (First)
                {
                    IpcChannel = new IpcServerChannel(name);
                    ChannelServices.RegisterChannel(IpcChannel, false);
                    RemotingConfiguration.RegisterWellKnownServiceType(typeof(IpcObject), objectName, WellKnownObjectMode.Singleton);

                    IpcObject obj = new IpcObject(new NewInstanceHandler(context.SecondInstanceStarted));
                    
                    RemotingServices.Marshal(obj, objectName);
                }

                else
                {
                    IpcChannel = new IpcClientChannel();
                    ChannelServices.RegisterChannel(IpcChannel, false);

                    IpcObject obj = Activator.GetObject(typeof(IpcObject), objectUri) as IpcObject;

                    obj.SignalNewInstance(args);
                }
            }
            catch { }
        }
    }


    public delegate void NewInstanceHandler(string[] args);


    public class IpcObject : MarshalByRefObject
    {
        event NewInstanceHandler NewInstance;

        public IpcObject(NewInstanceHandler handler)
        {
            NewInstance += handler;
        }

        public void SignalNewInstance(string[] args)
        {
            NewInstance(args);
        }

        // Make sure the object exists "forever"
        public override object InitializeLifetimeService()   
        {
            return null;
        }
    }

}