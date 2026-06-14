package com.structmap.webtransport;

public record Datagram(Session session, byte[] payload) {
}
