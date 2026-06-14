package com.structmap.webtransport;

import java.io.InputStream;
import java.io.OutputStream;

public record Stream(Session session, Object identifier, InputStream incoming, OutputStream outgoing) {
}
