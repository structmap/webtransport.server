namespace Structmap;

/// <summary>
/// Core class for managing WebTransport sessions, streams and datagrams.
/// </summary>
public interface IWebTransportServer
{
    /// <summary>
    /// Initiate session close by invoking wtf_session_close
    /// </summary>
    /// <param name="session">Session</param>
    /// <param name="errorCode">No error 0x00, internal error 0x01, etc</param>
    /// <param name="reason">Short message to help when troubleshooting</param>
    /// <returns>Boolean success or failure</returns>
    public bool Close(Session session, int errorCode = 0, string reason = "");
    /// <summary>
    /// Initiate session drain by invoking wtf_session_drain
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>Boolean success or failure</returns>
    public bool Drain(Session session);
    /// <summary>
    /// Sends a datagram from the server to the connected client by
    /// invoking wtf_session_send_datagram
    /// </summary>
    /// <param name="datagram">Session and byte array payload</param>
    /// <returns>Boolean success or failure</returns>
    public bool Send(Datagram datagram);
    /// <summary>
    /// Initiate stream by invoking wtf_session_create_stream
    /// </summary>
    /// <param name="session">Session on which to initiate</param>
    /// <param name="input">Data to send to client</param>
    /// <param name="bidirectional">Boolean to allow client reply or not</param>
    /// <returns>Stream for client's reply when bidirectional, or Stream.Null if unidirectional.</returns>
    public System.IO.Stream Push(Session session, System.IO.Stream input, bool bidirectional = true);
    /// <summary>
    /// Check if session is in handshaking state, likely to be ephemeral
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>True if WTF_SESSION_HANDSHAKING, false otherwise/returns>
    public bool IsHandshaking(Session session);
    /// <summary>
    /// Check if session was recently in connected state
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>True if WTF_SESSION_CONNECTED, false otherwise/returns>
    public bool IsConnected(Session session);
    /// <summary>
    /// Check if session is draining
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>True if WTF_SESSION_DRAINING, false otherwise/returns>
    public bool IsDraining(Session session);
    /// <summary>
    /// Check if session is closed
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>True if WTF_SESSION_CLOSED, false otherwise/returns>
    public bool IsClosed(Session session);
}