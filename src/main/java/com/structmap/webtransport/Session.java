package com.structmap.webtransport;

import com.structmap.webtransportfast.wtf_h;

import java.lang.foreign.Arena;
import java.lang.foreign.MemorySegment;

import static com.structmap.webtransport.Server.logger;

public record Session(Server server, Object identifier) {

    public record Start(Session session) {
    }

    public record End(Session session) {
    }

    /**
     * @param errorCode No error 0x00, internal error 0x01, etc
     * @param reason    Short message for troubleshooting
     * @return true unless there was an internal error
     */
    public boolean close(int errorCode, String reason) {
        var ptr = (MemorySegment)this.identifier;

        try (Arena arena = Arena.ofConfined()) {
            MemorySegment reasonPtr = arena.allocateFrom(reason);
            int result = wtf_h.wtf_session_close(ptr, errorCode, reasonPtr);
            if (result != wtf_h.WTF_SUCCESS()) {
                var msg = wtf_h.wtf_result_to_string(result);
                logger.warn("Failed to close session 0x{}: {}",
                        Long.toHexString(ptr.address()), msg.getString(0));
                return false;
            }
            return true;
        }
    }

    /**
     * @return true unless there was an internal error
     */
    public boolean drain() {
        var ptr = (MemorySegment)this.identifier;
        int result = wtf_h.wtf_session_drain(ptr);
        if (result != wtf_h.WTF_SUCCESS()) {
            var msg = wtf_h.wtf_result_to_string(result);
            logger.warn("Failed to drain session 0x{}: {}",
                    Long.toHexString(ptr.address()), msg.getString(0));
            return false;
        }
        return true;
    }

    /**
     * @return true if session is in handshaking state
     */
    public boolean isHandshaking() {
        var ptr = (MemorySegment)this.identifier;
        return wtf_h.WTF_SESSION_HANDSHAKING() == wtf_h.wtf_session_get_state(ptr);
    }

    /**
     * @return true if session is connected
     */
    public boolean isConnected() {
        var ptr = (MemorySegment)this.identifier;
        return wtf_h.WTF_SESSION_CONNECTED() == wtf_h.wtf_session_get_state(ptr);
    }

    /**
     * @return true if session is draining
     */
    public boolean isDraining() {
        var ptr = (MemorySegment)this.identifier;
        return wtf_h.WTF_SESSION_DRAINING() == wtf_h.wtf_session_get_state(ptr);
    }

    /**
     * @return true if session is closed
     */
    public boolean isClosed() {
        var ptr = (MemorySegment)this.identifier;
        return wtf_h.WTF_SESSION_CLOSED() == wtf_h.wtf_session_get_state(ptr);
    }
}
