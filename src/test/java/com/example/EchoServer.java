package com.example;

import com.structmap.webtransportfast.*;
import com.structmap.WebTransportServer;

import java.io.IOException;
import java.lang.foreign.*;

class EchoServer {
    static void log_callback(int level, MemorySegment component, MemorySegment file, int line,
                             MemorySegment message, MemorySegment user_context) {
        String[] logLevels = {
          "WTF_LOG_LEVEL_TRACE", // 0
          "WTF_LOG_LEVEL_DEBUG", // 1
          "WTF_LOG_LEVEL_INFO", // 2
          "WTF_LOG_LEVEL_WARN", // 3
          "WTF_LOG_LEVEL_ERROR", // 4
          "WTF_LOG_LEVEL_CRITICAL", // 5
          "WTF_LOG_LEVEL_NONE" // 6
        };
        System.out.println(logLevels[level] + "\t" + message.getString(0));
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
            System.out.printf("[CONN] Header: %s = %s\n", key, value);
        }

        return wtf_h.WTF_CONNECTION_ACCEPT();
    }
    static void main() {
        System.out.println("Starting echo server...");
        var server = new WebTransportServer(8443, "cert.pem", "key.pem");
        server.logCallback = EchoServer::log_callback;
        server.connectionValidator = EchoServer::connection_validator;
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