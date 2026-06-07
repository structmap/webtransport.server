using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Structmap.WebTransportFast;
using Structmap.WebTransportFast.Native;

namespace Structmap;

public record struct Session(WebTransportServer Server, Object Identifier);
public record struct Datagram(Session Context, byte[] Payload);
public record struct Stream(Session Context, Object Identifier, System.IO.Stream Incoming, System.IO.Stream Outgoing);
public record struct DuplexPipes(Pipe Incoming, Pipe Outgoing, Channel<MemoryHandle> Sent);

public unsafe class WebTransportServer
{
    public const byte FALSE = 0;
    public const byte TRUE = 1;

    public int port;
    public string cert;
    public string key;

    public wtf_context* g_context;
    public bool g_running = true;
    public wtf_server* g_server;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void session_callback_delegate(wtf_session_event_t* evt);

    private session_callback_delegate _session_callback;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void stream_callback_delegate(wtf_stream_event_t* evt);

    private stream_callback_delegate _stream_callback;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate wtf_connection_decision_t connection_validator_delegate(wtf_connection_request_t* request,
        void* user_data);

    private connection_validator_delegate _connection_validator;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void log_callback_delegate(wtf_log_level_t level, sbyte* component,
        sbyte* file, int line, sbyte* message, void* user_context);

    private log_callback_delegate _log_callback;

    public ConcurrentDictionary<Object,Channel<Object>> Sessions = new();
    public ConcurrentDictionary<Object,Object> Streams = new();

    public Func<Channel<Object>> ChannelFactory;
    public Action<Channel<Object>> Handler;

    public WebTransportServer(int port, string cert, string key)
    {
        this.port = port;
        this.cert = cert;
        this.key = key;
        _session_callback = session_callback;
        _stream_callback = stream_callback;
        _connection_validator = connection_validator;
        _log_callback = log_callback;
    }

    wtf_connection_decision_t connection_validator(wtf_connection_request_t* request, void* user_data)
    {
        // From machine clients these headers could include authorisation but from the browser
        // these headers will be empty. See https://github.com/w3c/webtransport/issues/263
        for (int i = 0; i < (int)request->header_count; i++)
        {
            var k = Marshal.PtrToStringAnsi((IntPtr)request->headers[i].name);
            var v = Marshal.PtrToStringAnsi((IntPtr)request->headers[i].value);
            Console.Out.WriteLine($"[CONN] Header: {k} = {v}");
        }

        return wtf_connection_decision_t.WTF_CONNECTION_ACCEPT;
    }

    void log_callback(wtf_log_level_t level, sbyte* component, sbyte* file, int line, sbyte* message,
        void* user_context)
    {
        Console.Out.WriteLine("{0}\t{1}", level, Marshal.PtrToStringAnsi((IntPtr)message));
    }

    void session_callback(wtf_session_event_t* evt)
    {
        if (evt->user_context == null)
        {
            evt->user_context = (void*)new IntPtr(1);
        }

        switch (evt->type)
        {
            case wtf_session_event_type_t.WTF_SESSION_EVENT_CONNECTED:
            {
                var sessionPointer = new IntPtr(evt->session);
                Console.Out.WriteLine("[SESSION] New session connected 0x{0:x}", sessionPointer);
                var ch = ChannelFactory();
                Sessions.TryAdd(sessionPointer, ch);
                Task.Run(() => Handler(ch));
                break;
            }

            case wtf_session_event_type_t.WTF_SESSION_EVENT_STREAM_OPENED: {
                var sessionPointer = new IntPtr(evt->session);
                var streamPointer = new IntPtr(evt->stream_opened.stream);
                Console.Out.WriteLine("[SESSION] New stream 0x{0:x} opened on session 0x{1:x}", streamPointer, sessionPointer);

                if (Sessions.TryGetValue(sessionPointer, out var ch))
                {
                    bool bidi = evt->stream_opened.stream_type == wtf_stream_type_t.WTF_STREAM_BIDIRECTIONAL;
                    var pipes = new DuplexPipes()
                    {
                        Incoming = new Pipe(PipeOptions.Default),
                        Outgoing = new Pipe(PipeOptions.Default),
                        Sent = Channel.CreateBounded<MemoryHandle>(2) // max in-flight
                    };

                    if (!bidi)
                    {
                        pipes.Outgoing.Writer.Complete();
                    }

                    Streams.TryAdd(streamPointer, pipes);
                    ch.Writer.TryWrite(new Stream()
                    {
                        Context = new Session(this, sessionPointer),
                        Identifier = streamPointer,
                        Incoming = pipes.Incoming.Reader.AsStream(),
                        Outgoing = bidi ? pipes.Outgoing.Writer.AsStream() : System.IO.Stream.Null,
                    });
                    if (bidi)
                    {
                        Task.Run(() => WebTransportServerUtil.SendLoop(pipes, streamPointer));
                    }
                }

                Methods.wtf_stream_set_callback(evt->stream_opened.stream,
                    (delegate* unmanaged[Cdecl]<wtf_stream_event_t*, void>)Marshal.GetFunctionPointerForDelegate(_stream_callback));

                Console.Out.WriteLine("[SESSION] Stream 0x{0:x} configured", streamPointer);
                break;
            }

            case wtf_session_event_type_t.WTF_SESSION_EVENT_DISCONNECTED: {
                var msg = Marshal.PtrToStringAnsi((IntPtr)evt->disconnected.reason);
                if (msg == null || msg == "") msg = "none";

                Console.Out.WriteLine("[SESSION] Session 0x{0:x} disconnected (error: {1}, reason: {2})",
                    (IntPtr)evt->session,
                    evt->disconnected.error_code,
                    msg);

                var sessionPointer = new IntPtr(evt->session);
                if (Sessions.TryGetValue(sessionPointer, out var ch))
                {
                    ch.Writer.TryComplete();
                    Sessions.TryRemove(sessionPointer, out _);
                }
                break;
            }

            case wtf_session_event_type_t.WTF_SESSION_EVENT_DATAGRAM_RECEIVED:
            {
                var sessionPointer = new IntPtr(evt->session);
                var n = (int)evt->datagram_received.length;
                Console.Out.WriteLine("[DATAGRAM] Received on session 0x{0:x} ({1} bytes)",
                    (IntPtr)evt->session,
                    n);

                var bs = new byte[n];
                Marshal.Copy((IntPtr)evt->datagram_received.data, bs, 0, n);
                var s = new Session()
                {
                    Identifier = sessionPointer,
                    Server = this,
                };
                var d = new Datagram()
                {
                    Context = s,
                    Payload = bs,
                };
                if (Sessions.TryGetValue(sessionPointer, out var ch))
                {
                    ch.Writer.TryWrite(d);
                }
                else
                {
                    Console.Error.WriteLine("No channel for session 0x{0:x}", (IntPtr)evt->session);
                }
                break;
            }

            case wtf_session_event_type_t.WTF_SESSION_EVENT_DATAGRAM_SEND_STATE_CHANGE:
            {
                // only free buffers if resending is not going to happen as per WTF_DATAGRAM_SEND_STATE_IS_FINAL in wtf.h
                var sendState = evt->datagram_send_state_changed.state;
                var mightResend = sendState >= wtf_datagram_send_state_t.WTF_DATAGRAM_SEND_LOST_DISCARDED;
                if (!mightResend) {
                    for (var i = 0; i < evt->datagram_send_state_changed.buffer_count; i++)
                    {
                        var data = evt->datagram_send_state_changed.buffers[i].data;
                        var dataPtr = (IntPtr)data;

                        if (dataPtr != IntPtr.Zero)
                        {
                            MemoryAllocator.free(dataPtr);
                        }
                    }
                }

                break;
            }

            case wtf_session_event_type_t.WTF_SESSION_EVENT_DRAINING:
                Console.Out.WriteLine("[SESSION] Session 0x{0:x} is draining", (IntPtr)evt->session);
                break;
        }
    }

    void stream_callback(wtf_stream_event_t* evt)
    {
        var streamPointer = new IntPtr(evt->stream);

        switch (evt->type)
        {
            case wtf_stream_event_type_t.WTF_STREAM_EVENT_SEND_COMPLETE: {
                if (Streams.TryGetValue(streamPointer, out var pipes))
                {
                    if (pipes is DuplexPipes pp)
                    {
                        for (var i = 0; i < evt->send_complete.buffer_count; i++)
                        {
                            if (evt->send_complete.buffers[i].data != (byte*)0)
                            {
                                if (pp.Sent.Reader.TryRead(out MemoryHandle mh)) {
                                    if (evt->send_complete.buffers[i].data != mh.Pointer)
                                    {
                                        Console.Error.WriteLine("Buffer pointer mismatch for stream 0x{0:x}", (IntPtr)evt->stream);
                                    }
                                    mh.Dispose();
                                }
                                //MemoryAllocator.free((IntPtr)evt->send_complete.buffers[i].data);
                            }
                        }
                    }
                }

                break;
            }

            case wtf_stream_event_type_t.WTF_STREAM_EVENT_DATA_RECEIVED:
            {
                if (Streams.TryGetValue(streamPointer, out var pipes))
                {
                    if (pipes is DuplexPipes pp)
                    {
                        using var w = pp.Incoming.Writer.AsStream(true);
                        for (var i = 0; i < evt->data_received.buffer_count; i++)
                        {
                            var buf = evt->data_received.buffers[i];
                            var n = (int)buf.length;
                            var bs = new byte[n];
                            Marshal.Copy((nint)buf.data, bs, 0, n);
                            // TODO: in backpressure scenario won't this block event handler loop? need to fix. possibly with wtf_stream_set_receive_enabled ?
                            w.Write(bs, 0, n);
                        }
                        if (evt->data_received.fin == TRUE)
                        {
                            pp.Incoming.Writer.Complete();
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("Failed to cast pipes for stream 0x{0:x}", (IntPtr)evt->stream);
                    }
                }
                else
                {
                    Console.Error.WriteLine("No pipe for stream 0x{0:x}", (IntPtr)evt->stream);
                }
                break;
            }

            case wtf_stream_event_type_t.WTF_STREAM_EVENT_PEER_CLOSED:
                Console.Out.WriteLine("[STREAM] Stream 0x{0:x} closed by peer", (IntPtr)evt->stream);
                break;

            case wtf_stream_event_type_t.WTF_STREAM_EVENT_CLOSED:
                Console.Out.WriteLine("[STREAM] Stream 0x{0:x} fully closed", (IntPtr)evt->stream);
                if (!Streams.TryRemove(streamPointer, out _))
                {
                    Console.Error.WriteLine("Failed to remove stream 0x{0:x}", streamPointer);
                }
                break;

            case wtf_stream_event_type_t.WTF_STREAM_EVENT_ABORTED:
                Console.Out.WriteLine("[STREAM] Stream 0x{0:x} aborted with error {1}", (IntPtr)evt->stream,
                    evt->aborted.error_code);
                // if (!Streams.TryRemove(streamPointer, out _))
                // {
                //     Console.Error.WriteLine("Failed to remove stream 0x{0:x}", streamPointer);
                // }
                break;
        }
    }

    public bool Send(Object session, byte[] data)
    {
        var n = (uint)data.Length;
        var dst = MemoryAllocator.malloc(n);
        Marshal.Copy(data, 0, dst, data.Length);
        var buffer = new wtf_buffer_t()
        {
            data = (byte*)dst,
            length = n,
        };

        if (session is IntPtr p)
        {
            wtf_result_t result = Methods.wtf_session_send_datagram((wtf_session*)p, &buffer, 1);
            if (result != wtf_result_t.WTF_SUCCESS)
            {
                var msg = Marshal.PtrToStringAnsi((IntPtr)Methods.wtf_result_to_string(result));
                Console.Out.WriteLine("[DATAGRAM] Failed to echo: {0}", msg);
                MemoryAllocator.free(dst);
                return false;
            }
            Console.Out.WriteLine("[DATAGRAM] Echoed {0} bytes", n);
            return true;
        }

        return false;
    }

    public bool Start()
    {
        // stack allocated (no pinning required)
        wtf_context_config_t context_config = new()
        {
            log_level = wtf_log_level_t.WTF_LOG_LEVEL_TRACE,
            log_callback =
                (delegate* unmanaged[Cdecl]<wtf_log_level_t, sbyte*, sbyte*, int, sbyte*, void*, void>)Marshal.GetFunctionPointerForDelegate(
                    _log_callback),
            worker_thread_count = 4,
            enable_load_balancing = TRUE,
        };

        byte[] certPathBytes = Encoding.UTF8.GetBytes(cert + '\0');
        sbyte* certPath = stackalloc sbyte[certPathBytes.Length];
        for (int i = 0; i < certPathBytes.Length; i++)
        {
            certPath[i] = (sbyte)certPathBytes[i];
        }

        byte[] keyPathBytes = Encoding.UTF8.GetBytes(key + '\0');
        sbyte* keyPath = stackalloc sbyte[keyPathBytes.Length];
        for (int i = 0; i < keyPathBytes.Length; i++)
        {
            keyPath[i] = (sbyte)keyPathBytes[i];
        }

        var cert_config = new wtf_certificate_config_t()
        {
            cert_type = wtf_certificate_type_t.WTF_CERT_TYPE_FILE,
            cert_data = new wtf_certificate_config_t._cert_data_e__Union()
            {
                file = new wtf_certificate_config_t._cert_data_e__Union._file_e__Struct()
                {
                    cert_path = certPath,
                    key_path = keyPath,
                }
            }
        };

        wtf_server_config_t server_config = new()
        {
            port = (ushort)port,
            cert_config = &cert_config,
            session_callback =
                (delegate* unmanaged[Cdecl]<wtf_session_event_t*, void>)Marshal.GetFunctionPointerForDelegate(
                    _session_callback),
            connection_validator =
                (delegate* unmanaged[Cdecl]<wtf_connection_request_t*, void*, wtf_connection_decision_t>)Marshal
                    .GetFunctionPointerForDelegate(_connection_validator),
            max_sessions_per_connection = 32,
            max_streams_per_session = 256,
            idle_timeout_ms = 60000,
            handshake_timeout_ms = 10000,
            enable_0rtt = TRUE,
            enable_migration = TRUE,
        };

        fixed (wtf_context** g_contextPtr = &g_context)
        fixed (wtf_server** g_serverPtr = &g_server)
        {
            var status = Methods.wtf_context_create(&context_config, g_contextPtr);
            if (status != wtf_result_t.WTF_SUCCESS)
            {
                var msg = Marshal.PtrToStringAnsi((IntPtr)Methods.wtf_result_to_string(status));
                Console.Out.WriteLine($"[ERROR] Failed to create context: {msg}");
                return false;
            }

            status = Methods.wtf_server_create(g_context, &server_config, g_serverPtr);
            if (status != wtf_result_t.WTF_SUCCESS)
            {
                var msg = Marshal.PtrToStringAnsi((IntPtr)Methods.wtf_result_to_string(status));
                Console.Out.WriteLine($"[ERROR] Failed to create server: {msg}");
                Methods.wtf_context_destroy(g_context);
                return false;
            }

            status = Methods.wtf_server_start(g_server);
            if (status != wtf_result_t.WTF_SUCCESS)
            {
                var msg = Marshal.PtrToStringAnsi((IntPtr)Methods.wtf_result_to_string(status));
                Console.Out.WriteLine($"[ERROR] Failed to start server: {msg}");
                Methods.wtf_server_destroy(g_server);
                Methods.wtf_context_destroy(g_context);
                return false;
            }
        }

        return true;
    }

    public bool Stop()
    {
        var status = Methods.wtf_server_stop(g_server);
        if (status != wtf_result_t.WTF_SUCCESS)
        {
            return false;
        }
        Methods.wtf_server_destroy(g_server);
        Methods.wtf_context_destroy(g_context);
        return true;
    }
}