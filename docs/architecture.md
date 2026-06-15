## Architecture

To use this library, you instantiate the server class, set some options,
provide a handler method and then invoke the start method. When a client
session is established the provided handler method is invoked. The handler
receives a channel object as its single argument so that it can read objects
from that channel and process them as they arrive. Each object will be either
an incoming datagram or stream. The channel will close when the session ends.

I wonder if an alernative design would be to combine all incoming events into a
single channel and return the invocation of the handler from the start method?
Each object on the channel already has a reference to the session context.

Anyway let me see if I can describe the internal architecture.

Basically the libwtf provides an event loop interface to msquic. These events
are partitioned by HTTP3 session so we have a struct for that. On the libwtf
side it is an opaque pointer but we're going to use a struct of 1) a reference
to the server class and 2) a reference to an object which will be an IntPtr.

Ok, next we have datagrams. These are easy. A session and a byte array. Done.

Finally we have bidirectional streams. These are tricky. A diagram might help.

`msquic <-> libwtf <-> server <-> handler <-> server <-> libwtf <-> msquic`

In the C# implementation the classes involved are

`Incoming.Writer <-> Incoming.Reader <-> handler <-> Outgoing.Writer <-> Outgoing.Reader`

The libwtf event loop writes into the Incoming part and the handler reads from
it. The handler then writes to the Outgoing and there is a task running which
reads from the Outgoing, queues buffers for sending then frees when sent.
