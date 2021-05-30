using System;
using System.Net;
using JetBrains.Annotations;
using UnityEngine;

namespace Fuckshit
{
    public class IPEndPointNonAlloc : IPEndPoint
    {
        public IPEndPointNonAlloc(long address, int port) : base(address, port) {}
        public IPEndPointNonAlloc([NotNull] IPAddress address, int port) : base(address, port) {}

        // ReceiveFrom calls EndPoint.Create():
        // https://github.com/mono/mono/blob/f74eed4b09790a0929889ad7fc2cf96c9b6e3757/mcs/class/System/System.Net.Sockets/Socket.cs#L1761
        //
        // we pass an IPEndPoint to ReceiveFrom.
        // IPEndPoint.Create allocates:
        // https://github.com/mono/mono/blob/bdd772531d379b4e78593587d15113c37edd4a64/mcs/class/referencesource/System/net/System/Net/IPEndPoint.cs#L136
        //
        // let's overwrite for a version that does not allocate
        public SocketAddress lastSocketAddress;
        public override EndPoint Create(SocketAddress socketAddress)
        {
            Debug.LogWarning($"{nameof(IPEndPointNonAlloc)}.Create() hook");

            // original IPEndPoint.Create validates:
            if (socketAddress.Family != AddressFamily)
                throw new ArgumentException($"Unsupported socketAddress.AddressFamily: {socketAddress.Family}. Expected: {AddressFamily}");
            if (socketAddress.Size < 8)
                throw new ArgumentException($"Unsupported socketAddress.Size: {socketAddress.Size}. Expected: <8");

            // original IPEndPoint.Create calls this function, which is not
            // available when trying to call it here:
            //return socketAddress.GetIPEndPoint();
            //
            // using ILSpy we can see SocketAddress.GetIPEndPoint() in Unity:
            //     internal IPEndPoint GetIPEndPoint()
            //     {
            //         IPAddress iPAddress = GetIPAddress();
            //         int port = SocketAddressPal.GetPort(Buffer);
            //         return new IPEndPoint(iPAddress, port);
            //     }

            // let's store the socketAddress and return ourselves instead.
            lastSocketAddress = socketAddress;
            return this;
        }
    }
}