package com.example;

import com.structmap.webtransportfast.*;
import com.structmap.WebTransportServer;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.lang.foreign.*;
import java.util.concurrent.BlockingQueue;

class EchoServer {
    final static Logger logger = LoggerFactory.getLogger(EchoServer.class);
    static void log_callback(int level, MemorySegment component, MemorySegment file, int line,
                             MemorySegment message, MemorySegment user_context) {
        switch (level) {
            case 0: logger.trace(message.getString(0)); break; //WTF_LOG_LEVEL_TRACE 0
            case 1: logger.debug(message.getString(0)); break; //WTF_LOG_LEVEL_DEBUG 1
            case 2: logger.info(message.getString(0)); break; //WTF_LOG_LEVEL_INFO 2
            case 3: logger.warn(message.getString(0)); break; //WTF_LOG_LEVEL_WARN 3
            case 4: logger.error(message.getString(0)); break; //WTF_LOG_LEVEL_ERROR 4
            case 5: logger.error(message.getString(0)); break; //WTF_LOG_LEVEL_CRITICAL 5
            default: // WTF_LOG_LEVEL_NONE 6
        }
    }
    static int connection_validator(MemorySegment request, MemorySegment user_data)
    {
        // From machine clients these headers could include authorisation but from the browser
        // these headers will be empty. See https://github.com/w3c/webtransport/issues/263
        long header_count = wtf_connection_request_t.header_count(request);
        MemorySegment headers = wtf_connection_request_t.headers(request);

        for (int i = 0; i < header_count; i++) {
            MemorySegment header = headers.asSlice(i * wtf_http_header_t.sizeof(), wtf_http_header_t.sizeof());
            MemorySegment keyPtr = wtf_http_header_t.name(header);
            MemorySegment valuePtr = wtf_http_header_t.value(header);
            String key = keyPtr.getString(0);
            String value = valuePtr.getString(0);
            logger.trace("[CONN] Header: {} = {}", key, value);
        }

        return wtf_h.WTF_CONNECTION_ACCEPT();
    }
    static void handler(BlockingQueue<Object> ch) {
        // TODO handle channel closing
        while (true) {
            try {
                var msg = ch.take();
                if (msg instanceof WebTransportServer.Datagram dg) {
                    logger.trace("Received datagram: {}", dg);
                    dg.Context().Server().Send(dg.Context().Identifier(), dg.Payload());
                }
                if (msg instanceof WebTransportServer.Stream s) {
                    logger.trace("Received stream: {}", s);
                    Thread.startVirtualThread(() -> {
                        try {
                            s.Incoming().transferTo(s.Outgoing());
                        } catch (IOException e) {
                            logger.error("Failed to transfer stream: {}", e.getMessage());
                        }
                    });
                }
            } catch (InterruptedException e) {
                logger.error("Handler thread interrupted: {}", e.getMessage());
            }
        }
    };
    static void main() {
        logger.debug("Starting echo server...");
        var server = new WebTransportServer(8443, "cert.pem", "key.pem");
        server.logCallback = EchoServer::log_callback;
        server.connectionValidator = EchoServer::connection_validator;
        server.handler = EchoServer::handler;
        if (!server.Start()) {
            return;
        }
        try {
            System.in.read();
        } catch (IOException e) {
            throw new RuntimeException(e);
        }
        server.Stop();
    }
}