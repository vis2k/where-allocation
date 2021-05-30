using System;
using System.Net;

namespace Fuckshit
{
    public class IPEndPointNonAlloc : IPEndPoint
    {
        // Two steps to remove most of the allocations in ReceiveFrom_Internal:
        //
        // 1.) remoteEndPoint.Serialize():
        //     https://github.com/mono/mono/blob/f74eed4b09790a0929889ad7fc2cf96c9b6e3757/mcs/class/System/System.Net.Sockets/Socket.cs#L1733
        //     -> creates an EndPoint for ReceiveFrom_Internal to write into
        //     -> it's never read from:
        //        ReceiveFrom_Internal passes it to native:
        //          https://github.com/mono/mono/blob/f74eed4b09790a0929889ad7fc2cf96c9b6e3757/mcs/class/System/System.Net.Sockets/Socket.cs#L1885
        //        native recv populates 'sockaddr* from' with the remote address:
        //          https://docs.microsoft.com/en-us/windows/win32/api/winsock/nf-winsock-recvfrom
        //     -> can NOT be null. bricks both Unity and Unity Hub otherwise.
        //     -> it seems as if Serialize() is only called to avoid allocating
        //        a 'new SocketAddress' in ReceiveFrom. it's up to the EndPoint.
        //
        // 2.) EndPoint.Create(SocketAddress):
        //     https://github.com/mono/mono/blob/f74eed4b09790a0929889ad7fc2cf96c9b6e3757/mcs/class/System/System.Net.Sockets/Socket.cs#L1761
        //     -> SocketAddress is the remote's address that we want to return
        //     -> to avoid 'new EndPoint(SocketAddress), it seems up to the user
        //        to decide how to create a new EndPoint via .Create
        //     -> SocketAddress is the object that was returned by Serialize()
        //
        // in other words, all we need is an extra SocketAddress field that we
        // can pass to ReceiveFrom_Internal to write the result into.
        // => callers can then get the result from the extra field!
        // => no allocations
        //
        // IMPORTANT: remember that IPEndPointNonAlloc is always the same object
        //            and never changes. only the helper field is changed.
        public SocketAddress temp;

        // constructors simply create the field once by calling the base method.
        // (our overwritten method would create anything new)
        public IPEndPointNonAlloc(long address, int port) : base(address, port)
        {
            temp = base.Serialize();
        }
        public IPEndPointNonAlloc(IPAddress address, int port) : base(address, port)
        {
            temp = base.Serialize();
        }

        // Serialize simply returns it
        public override SocketAddress Serialize() => temp;

        // Create doesn't need to create anything.
        // SocketAddress object is already the one we returned in Serialize().
        // ReceiveFrom_Internal simply wrote into it.
        public override EndPoint Create(SocketAddress socketAddress)
        {
            // original IPEndPoint.Create validates:
            if (socketAddress.Family != AddressFamily)
                throw new ArgumentException($"Unsupported socketAddress.AddressFamily: {socketAddress.Family}. Expected: {AddressFamily}");
            if (socketAddress.Size < 8)
                throw new ArgumentException($"Unsupported socketAddress.Size: {socketAddress.Size}. Expected: <8");

            // double check to guarantee that ReceiveFrom actually did write
            // into our 'temp' field. just in case that's ever changed.
            if (socketAddress != temp)
            {
                // well this is fun.
                // in the latest mono from the above github links,
                // the result of Serialize() is passed as 'ref' so ReceiveFrom
                // does in fact write into it.
                //
                // in Unity 2019 LTS's mono version, it does create a new one
                // each time. this is from ILSpy Receive_From:
                //
                //     SocketPal.CheckDualModeReceiveSupport(this);
                //     ValidateBlockingMode();
                //     if (NetEventSource.IsEnabled)
                //     {
                //         NetEventSource.Info(this, $"SRC{LocalEndPoint} size:{size} remoteEP:{remoteEP}", "ReceiveFrom");
                //     }
                //     EndPoint remoteEP2 = remoteEP;
                //     System.Net.Internals.SocketAddress socketAddress = SnapshotAndSerialize(ref remoteEP2);
                //     System.Net.Internals.SocketAddress socketAddress2 = IPEndPointExtensions.Serialize(remoteEP2);
                //     int bytesTransferred;
                //     SocketError socketError = SocketPal.ReceiveFrom(_handle, buffer, offset, size, socketFlags, socketAddress.Buffer, ref socketAddress.InternalSize, out bytesTransferred);
                //     SocketException ex = null;
                //     if (socketError != 0)
                //     {
                //         ex = new SocketException((int)socketError);
                //         UpdateStatusAfterSocketError(ex);
                //         if (NetEventSource.IsEnabled)
                //         {
                //             NetEventSource.Error(this, ex, "ReceiveFrom");
                //         }
                //         if (ex.SocketErrorCode != SocketError.MessageSize)
                //         {
                //             throw ex;
                //         }
                //     }
                //     if (!socketAddress2.Equals(socketAddress))
                //     {
                //         try
                //         {
                //             remoteEP = remoteEP2.Create(socketAddress);
                //         }
                //         catch
                //         {
                //         }
                //         if (_rightEndPoint == null)
                //         {
                //             _rightEndPoint = remoteEP2;
                //         }
                //     }
                //     if (ex != null)
                //     {
                //         throw ex;
                //     }
                //     if (NetEventSource.IsEnabled)
                //     {
                //         NetEventSource.DumpBuffer(this, buffer, offset, size, "ReceiveFrom");
                //         NetEventSource.Exit(this, bytesTransferred, "ReceiveFrom");
                //     }
                //     return bytesTransferred;
                //

                // so until they upgrade their mono version, we are stuck with
                // some allocations.
                //
                // for now, let's pass the newly created on to our temp so at
                // least we reuse it next time.
                temp = socketAddress;

                // in the future, enable this again:
                //throw new Exception($"Socket.ReceiveFrom(): passed SocketAddress={socketAddress} but expected {temp}. This should never happen. Did ReceiveFrom() change?");
            }

            // ReceiveFrom sets seed_endpoint to the result of Create():
            // https://github.com/mono/mono/blob/f74eed4b09790a0929889ad7fc2cf96c9b6e3757/mcs/class/System/System.Net.Sockets/Socket.cs#L1764
            // so let's return ourselves at least.
            // (seed_endpoint only seems to matter for BeginSend etc.)
            return this;
        }
    }
}