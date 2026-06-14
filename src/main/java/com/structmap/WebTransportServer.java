package com.structmap;

import com.structmap.webtransportfast.*;

import java.io.*;
import java.lang.foreign.Arena;
import java.lang.foreign.MemorySegment;
import java.lang.foreign.ValueLayout;
import java.nio.ByteBuffer;
import java.nio.channels.Channels;
import java.nio.channels.Pipe;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.function.Function;
import java.util.function.Supplier;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class WebTransportServer {
    static {
        // try to load from jar classpath but fall back to -Djava.library.path VM option in development
        if (!WebTransportFast.load()) {
            System.loadLibrary("msquic");
            System.loadLibrary("wtf");
        }
    }

    final static Logger logger = LoggerFactory.getLogger(WebTransportServer.class);

    public MemorySegment g_context;
    public MemorySegment g_server;
    public int port;
    public String cert;
    public String key;
    public Arena arena;

    public WebTransportServer(int port, String cert, String key) {
        this.port = port;
        this.cert = cert;
        this.key = key;
        this.sessions = new ConcurrentHashMap<>();
        this.sessionCallback = this::session_callback;
        this.streams = new ConcurrentHashMap<>();
        this.streamCallback = this::stream_callback;
        this.channelFactory = () -> new LinkedBlockingQueue<>(10);
        this.arena = new MemoryAllocator();
    }

    public ConcurrentHashMap<Object, BlockingQueue<Object>> sessions;
    public ConcurrentHashMap<Object,Object> streams;

    public Supplier<BlockingQueue<Object>> channelFactory;
    public Function<BlockingQueue<Object>,Object> sessionHandler;

    public wtf_log_callback_t.Function logCallback;
    public wtf_connection_validator_t.Function connectionValidator;
    public wtf_session_callback_t.Function sessionCallback;
    public wtf_stream_callback_t.Function streamCallback;

    public record Session(WebTransportServer server, Object identifier) {
    }

    public record Datagram(Session session, byte[] payload) {
    }

    public record Stream(Session session, Object identifier, InputStream incoming, OutputStream outgoing) {
    }

    public record DuplexPipes(Pipe incoming, Pipe outgoing, BlockingQueue<MemorySegment> sent) {
    }

    public record Start(Session session) {
    }

    public record End(Session session) {
    }

    void session_callback(MemorySegment evt) {
        if (wtf_session_event_t.type(evt) == wtf_h.WTF_SESSION_EVENT_CONNECTED()) {
            var sessionPointer = wtf_session_event_t.session(evt);
            logger.debug("New session connected 0x{}", Long.toHexString(sessionPointer.address()));
            var s = new Session(this, sessionPointer);
            var ch = this.channelFactory.get();
            this.sessions.put(sessionPointer, ch);
            ch.offer(new Start(s));
            Thread.startVirtualThread(() -> this.sessionHandler.apply(ch));
            return;
        }

        if (wtf_session_event_t.type(evt) == wtf_h.WTF_SESSION_EVENT_STREAM_OPENED()) {
            var sessionPointer = wtf_session_event_t.session(evt);
            var streamOpened = wtf_session_event_t.stream_opened(evt);
            var streamPointer = wtf_session_event_t.stream_opened.stream(streamOpened);
            logger.debug("New stream 0x{} opened on session 0x{}",
                    Long.toHexString(streamPointer.address()),
                    Long.toHexString(sessionPointer.address()));

            var ch = this.sessions.get(sessionPointer);
            if (ch != null) {
                boolean bidi = wtf_session_event_t.stream_opened.stream_type(streamOpened) ==
                        wtf_h.WTF_STREAM_BIDIRECTIONAL();

                try {
                    var incoming = Pipe.open();
                    var outgoing = Pipe.open();

                    var pipes = new DuplexPipes(incoming, outgoing, new LinkedBlockingQueue<>(2)); // TODO: consider making configurable

                    if (!bidi) {
                        try {
                            outgoing.sink().close();
                        } catch (Exception e) {
                            logger.error("Exception: {}", e.getMessage());
                        }
                    }

                    this.streams.put(streamPointer, pipes);

                    var stream = new Stream(
                            new Session(this, sessionPointer),
                            streamPointer,
                            Channels.newInputStream(incoming.source()),
                            bidi ? Channels.newOutputStream(outgoing.sink()) : OutputStream.nullOutputStream()
                    );

                    ch.offer(stream); // TODO: warn on dropped stream or reject it

                    if (bidi) {
                        Thread.startVirtualThread(() -> sendLoop(pipes, streamPointer));
                    }
                } catch (Exception e) {
                    logger.error("Exception: {}", e.getMessage());
                }
            }

            wtf_h.wtf_stream_set_callback(streamPointer,
                    wtf_stream_callback_t.allocate(this.streamCallback, arena));

            logger.debug("Stream 0x{} configured", Long.toHexString(streamPointer.address()));
            return;
        }

        if (wtf_session_event_t.type(evt) == wtf_h.WTF_SESSION_EVENT_DISCONNECTED()) {
            var sessionPointer = wtf_session_event_t.session(evt);
            var disconnected = wtf_session_event_t.disconnected(evt);
            var reasonPtr = wtf_session_event_t.disconnected.reason(disconnected);
            var msg = "none";
            if (reasonPtr != null && reasonPtr.address() != 0) {
                var reasonStr = reasonPtr.getString(0);
                if (reasonStr != null && !reasonStr.isEmpty()) {
                    msg = reasonStr;
                }
            }
            var errorCode = wtf_session_event_t.disconnected.error_code(disconnected);

            logger.debug("Session 0x{} disconnected (error: {}, reason: {})",
                    Long.toHexString(sessionPointer.address()), Long.toString(errorCode), msg);

            var ch = this.sessions.remove(sessionPointer);
            if (ch != null) {
                var s = new Session(this, sessionPointer);
                ch.offer(new End(s));
            } else {
                logger.warn("No channel for session 0x{}", Long.toHexString(sessionPointer.address()));
            }
            return;
        }

        if (wtf_session_event_t.type(evt) == wtf_h.WTF_SESSION_EVENT_DATAGRAM_RECEIVED()) {
            var sessionPointer = wtf_session_event_t.session(evt);
            var dr = wtf_session_event_t.datagram_received(evt);
            var n = wtf_session_event_t.datagram_received.length(dr);
            logger.debug("Received on session 0x{} ({} bytes)",
                    Long.toHexString(sessionPointer.address()), Long.toString(n));
            var d = new Datagram(new Session(this, sessionPointer), new byte[(int) n]);
            var dataPtr = wtf_session_event_t.datagram_received.data(dr);
            MemorySegment.copy(dataPtr, ValueLayout.JAVA_BYTE, 0, d.payload, 0, (int) n);
            var ch = this.sessions.get(sessionPointer);
            if (ch != null) {
                ch.offer(d); // TODO: warn on dropped datagram (even better would be to nack at protocol level)
            } else {
                logger.warn("No channel for session 0x{}", Long.toHexString(sessionPointer.address()));
            }
            return;
        }
        if (wtf_session_event_t.type(evt) == wtf_h.WTF_SESSION_EVENT_DATAGRAM_SEND_STATE_CHANGE()) {
            var dsc = wtf_session_event_t.datagram_send_state_changed(evt);
            var sendState = wtf_session_event_t.datagram_send_state_changed.state(dsc);
            var mightResend = sendState < wtf_h.WTF_DATAGRAM_SEND_LOST_DISCARDED();

            if (!mightResend) {
                var bufferCount = wtf_session_event_t.datagram_send_state_changed.buffer_count(dsc);
                var buffers = wtf_session_event_t.datagram_send_state_changed.buffers(dsc);

                for (int i = 0; i < bufferCount; i++) {
                    var buffer = wtf_buffer_t.asSlice(buffers, i);
                    var data = wtf_buffer_t.data(buffer);

                    if (data != null && data.address() != 0) {
                        ((MemoryAllocator) arena).free(data);
                    }
                }
            }
            return;
        }
        if (wtf_session_event_t.type(evt) == wtf_h.WTF_SESSION_EVENT_DRAINING()) {
            var sessionPointer = wtf_session_event_t.session(evt);
            logger.debug("Session 0x{} is draining",
                    Long.toHexString(sessionPointer.address()));
            // TODO: should indicate draining status to handler via channel
        }
    }

    void stream_callback(MemorySegment evt) {
        var streamPointer = wtf_stream_event_t.stream(evt);
        var eventType = wtf_stream_event_t.type(evt);

        if (eventType == wtf_h.WTF_STREAM_EVENT_SEND_COMPLETE()) {
            var pipes = this.streams.get(streamPointer);
            if (pipes instanceof DuplexPipes pp) {
                var sendComplete = wtf_stream_event_t.send_complete(evt);
                var bufferCount = wtf_stream_event_t.send_complete.buffer_count(sendComplete);
                var buffers = wtf_stream_event_t.send_complete.buffers(sendComplete);

                for (int i = 0; i < bufferCount; i++) {
                    var buffer = wtf_buffer_t.asSlice(buffers, i);
                    var data = wtf_buffer_t.data(buffer);

                    if (data != null && data.address() != 0) {
                        var mh = pp.sent.poll();
                        if (mh != null) {
                            if (data.address() != mh.address()) {
                                logger.warn("Buffer pointer mismatch for stream 0x{}",
                                        Long.toHexString(streamPointer.address()));
                            }
                            ((MemoryAllocator) arena).free(mh);
                        }
                    }
                }
            }
            return;
        }

        if (eventType == wtf_h.WTF_STREAM_EVENT_DATA_RECEIVED()) {
            var pipes = this.streams.get(streamPointer);
            if (pipes instanceof DuplexPipes pp) {
                var dataReceived = wtf_stream_event_t.data_received(evt);
                var bufferCount = wtf_stream_event_t.data_received.buffer_count(dataReceived);
                var buffers = wtf_stream_event_t.data_received.buffers(dataReceived);
                var fin = wtf_stream_event_t.data_received.fin(dataReceived);

                try {
                    for (int i = 0; i < bufferCount; i++) {
                        var buffer = wtf_buffer_t.asSlice(buffers, i);
                        var length = wtf_buffer_t.length(buffer);
                        var data = wtf_buffer_t.data(buffer);

                        var bytes = new byte[(int) length];
                        MemorySegment.copy(data, ValueLayout.JAVA_BYTE, 0, bytes, 0, (int) length);
                        // TODO: in backpressure scenario won't this block event handler loop?
                        // need to fix. possibly with wtf_stream_set_receive_enabled ?
                        pp.incoming.sink().write(ByteBuffer.wrap(bytes));
                    }
                    if (fin) {
                        pp.incoming.sink().close();
                    }
                } catch (IOException e) {
                    logger.warn("Error writing to stream 0x{}: {}",
                            Long.toHexString(streamPointer.address()), e.getMessage());
                    e.printStackTrace();
                }
            } else {
                logger.warn("Failed to cast pipes for stream 0x{}",
                        Long.toHexString(streamPointer.address()));
            }
            return;
        }

        if (eventType == wtf_h.WTF_STREAM_EVENT_PEER_CLOSED()) {
            logger.debug("Stream 0x{} closed by peer",
                    Long.toHexString(streamPointer.address()));
            return;
        }

        if (eventType == wtf_h.WTF_STREAM_EVENT_CLOSED()) {
            logger.debug("Stream 0x{} fully closed",
                    Long.toHexString(streamPointer.address()));
            if (this.streams.remove(streamPointer) == null) {
                logger.warn("Warning: no stream found 0x{}",
                        Long.toHexString(streamPointer.address()));
            }
            return;
        }

        if (eventType == wtf_h.WTF_STREAM_EVENT_ABORTED()) {
            var aborted = wtf_stream_event_t.aborted(evt);
            var errorCode = wtf_stream_event_t.aborted.error_code(aborted);
            logger.debug("Stream 0x{} aborted with error {}",
                    Long.toHexString(streamPointer.address()), Long.toString(errorCode));
            // if (this.streams.remove(streamPointer) == null) {
            //     logger.warn("Failed to remove stream 0x{}", Long.toHexString(streamPointer.address()));
            // }
            return;
        }
    }

    void sendLoop(DuplexPipes pipes, MemorySegment streamPointer) {
        var outgoingReader = pipes.outgoing.source();
        var sent = pipes.sent;

        while (true) {
            var dataSegment = arena.allocate(4096);
            var buffer = dataSegment.asByteBuffer();
            long bytesRead = -1;

            try {
                bytesRead = outgoingReader.read(buffer);
                if (bytesRead == -1) {
                    break;
                }
            } catch (IOException e) {
                logger.warn("Failed processing stream 0x{}: {}",
                        Long.toHexString(streamPointer.address()), e.getMessage());
                e.printStackTrace();
                break;
            }

            try {
                sent.put(dataSegment);
            } catch (InterruptedException e) {
                logger.debug("Error adding buffer to sent queue: {}", e.getMessage());
                e.printStackTrace();
                break;
            }

            // use the fact that an array of one item is just pointer to the first
            var wtfBuffer = wtf_buffer_t.allocate(arena);
            wtf_buffer_t.data(wtfBuffer, dataSegment);
            wtf_buffer_t.length(wtfBuffer, (int)bytesRead);

            // Send data through the stream
            int result = wtf_h.wtf_stream_send(streamPointer, wtfBuffer, 1, false);
            if (result != wtf_h.WTF_SUCCESS()) {
                var msg = wtf_h.wtf_result_to_string(result);
                logger.warn("Failed to write to stream 0x{}: {}",
                        Long.toHexString(streamPointer.address()), msg.getString(0));
                break;
            }
        }

        try {
            outgoingReader.close();
        } catch (IOException e) {
            logger.warn("Failed at outgoingReader.close() processing stream 0x{}: {}",
                    Long.toHexString(streamPointer.address()), e.getMessage());
        }
    }

    public boolean Send(Object session, byte[] data) {
        var n = data.length;
        var dst = arena.allocate(n);
        MemorySegment.copy(data, 0, dst, ValueLayout.JAVA_BYTE, 0, n);

        var buffer = wtf_buffer_t.allocate(arena);
        wtf_buffer_t.data(buffer, dst);
        wtf_buffer_t.length(buffer, n);

        if (session instanceof MemorySegment sessionPtr) {
            int result = wtf_h.wtf_session_send_datagram(sessionPtr, buffer, 1);
            if (result != wtf_h.WTF_SUCCESS()) {
                var msg = wtf_h.wtf_result_to_string(result);
                logger.debug("Failed to echo: {}", msg.getString(0));
                return false;
            }
            logger.debug("Echoed {} bytes", Long.toString(n));
            return true;
        }

        return false;
    }

    public boolean ValidConfig() {
        if (this.sessionHandler == null) {
            logger.error("No handler set");
            return false;
        }
        return true;
    }

    public boolean Start() {
        if (!this.ValidConfig()) {
            return false;
        }
        var arena = Arena.global();
        var logCallback = wtf_log_callback_t.allocate(
                this.logCallback,
                arena
        );
        var context_config = wtf_context_config_t.allocate(arena);
        wtf_context_config_t.log_level(context_config, wtf_h.WTF_LOG_LEVEL_TRACE());
        wtf_context_config_t.log_callback(context_config, logCallback);
        wtf_context_config_t.worker_thread_count(context_config, 4);
        wtf_context_config_t.enable_load_balancing(context_config, true);
        g_context = arena.allocate(ValueLayout.ADDRESS);
        var status = wtf_h.wtf_context_create(context_config, g_context);
        if (status != wtf_h.WTF_SUCCESS()) {
            var msg = wtf_h.wtf_result_to_string(status);
            logger.debug("Failed to create context: {}", msg.getString(0));
            return false;
        }

        var cert_data = wtf_certificate_config_t.cert_data.file.allocate(arena);
        wtf_certificate_config_t.cert_data.file.cert_path(cert_data, arena.allocateFrom(this.cert));
        wtf_certificate_config_t.cert_data.file.key_path(cert_data, arena.allocateFrom(this.key));

        var cert_config = wtf_certificate_config_t.allocate(arena);
        wtf_certificate_config_t.cert_type(cert_config, wtf_h.WTF_CERT_TYPE_FILE());
        wtf_certificate_config_t.cert_data.file(
                wtf_certificate_config_t.cert_data(cert_config),
                cert_data
        );

        var sessionCallback = wtf_session_callback_t.allocate(
                this.sessionCallback,
                arena
        );
        var connectionValidator = wtf_connection_validator_t.allocate(
                this.connectionValidator,
                arena
        );

        var server_config = wtf_server_config_t.allocate(arena);
        wtf_server_config_t.port(server_config, (short)this.port);
        wtf_server_config_t.cert_config(server_config, cert_config);
        wtf_server_config_t.session_callback(server_config, sessionCallback);
        wtf_server_config_t.connection_validator(server_config, connectionValidator);
        wtf_server_config_t.max_sessions_per_connection(server_config, 32);
        wtf_server_config_t.max_streams_per_session(server_config, 256);
        wtf_server_config_t.idle_timeout_ms(server_config, 60000);
        wtf_server_config_t.handshake_timeout_ms(server_config, 10000);
        wtf_server_config_t.enable_0rtt(server_config, true);
        wtf_server_config_t.enable_migration(server_config, true);

        g_server = arena.allocate(ValueLayout.ADDRESS);
        status = wtf_h.wtf_server_create(g_context.get(ValueLayout.ADDRESS, 0), server_config, g_server);
        if (status != wtf_h.WTF_SUCCESS()) {
            var msg = wtf_h.wtf_result_to_string(status);
            logger.debug("Failed to create server: {}", msg.getString(0));
        }

        status = wtf_h.wtf_server_start(g_server.get(ValueLayout.ADDRESS, 0));
        if (status != wtf_h.WTF_SUCCESS()) {
            var msg = wtf_h.wtf_result_to_string(status);
            logger.debug("Failed to start server: {}", msg.getString(0));
            return false;
        }
        return true;
    }

    public void Stop() {
        wtf_h.wtf_server_stop(g_server.get(ValueLayout.ADDRESS, 0));
        wtf_h.wtf_server_destroy(g_server.get(ValueLayout.ADDRESS, 0));
        wtf_h.wtf_context_destroy(g_context.get(ValueLayout.ADDRESS, 0));
    }
}