using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Structmap.WebTransportFast.Native;

namespace Structmap.WebTransport;

public unsafe record struct Session(Server Server, Object Identifier)
{
    public record Start(Session Session);
    public record End(Session Session);

    public ILogger Logger => Server.Logger;

    /// <summary>
    /// Initiate session close by invoking wtf_session_close
    /// </summary>
    /// <param name="session">Session</param>
    /// <param name="errorCode">No error 0x00, internal error 0x01, etc</param>
    /// <param name="reason">Short message to help when troubleshooting</param>
    /// <returns>Boolean success or failure</returns>
    public bool Close(int errorCode = 0, string reason = "")
    {
        var ptr = (IntPtr)Identifier;
        var n = reason.Length;
        sbyte* reasonBytes = stackalloc sbyte[n + 1];
        for (int i = 0; i < n; i++)
        {
            reasonBytes[i] = (sbyte)reason[i];
        }
        reasonBytes[n] = (sbyte)'\0';
        var result = Methods.wtf_session_close((wtf_session*)ptr, (uint) errorCode, reasonBytes);
        if (result != wtf_result_t.WTF_SUCCESS)
        {
            var msg = Marshal.PtrToStringAnsi((IntPtr)Methods.wtf_result_to_string(result));
            Server.Logger.LogDebug("Failed to close session: 0x{session:x}", ptr);
            return false;
        }
        Logger.LogDebug("Closing session: 0x{session:x}", ptr);
        return true;
    }
    /// <summary>
    /// Initiate session drain by invoking wtf_session_drain
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>Boolean success or failure</returns>
    public bool Drain()
    {
        var ptr = (IntPtr)Identifier;
        var result = Methods.wtf_session_drain((wtf_session*)ptr);
        if (result != wtf_result_t.WTF_SUCCESS)
        {
            var msg = Marshal.PtrToStringAnsi((IntPtr)Methods.wtf_result_to_string(result));
            Logger.LogDebug("Failed to drain session: 0x{session:x}", ptr);
            return false;
        }
        Logger.LogDebug("Draining session: 0x{session:x}", ptr);
        return true;
    }
    /// <summary>
    /// Initiate stream by invoking wtf_session_create_stream
    /// </summary>
    /// <param name="session">Session on which to initiate</param>
    /// <param name="input">Data to send to client</param>
    /// <param name="bidirectional">Boolean to allow client reply or not</param>
    /// <returns>Stream for client's reply when bidirectional, or Stream.Null if unidirectional.</returns>
    public System.IO.Stream Push(Session session, System.IO.Stream input, bool bidirectional = true)
    {
        throw new NotImplementedException();
    }
    /// <summary>
    /// Check if session is in handshaking state, likely to be ephemeral
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>True if WTF_SESSION_HANDSHAKING, false otherwise</returns>
    public bool IsHandshaking()
    {
        return wtf_session_state_t.WTF_SESSION_HANDSHAKING == Methods.wtf_session_get_state((wtf_session*)(IntPtr)Identifier);
    }
    /// <summary>
    /// Check if session was recently in connected state
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>True if WTF_SESSION_CONNECTED, false otherwise</returns>
    public bool IsConnected()
    {
        return wtf_session_state_t.WTF_SESSION_CONNECTED == Methods.wtf_session_get_state((wtf_session*)(IntPtr)Identifier);
    }
    /// <summary>
    /// Check if session is draining
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>True if WTF_SESSION_DRAINING, false otherwise</returns>
    public bool IsDraining()
    {
        return wtf_session_state_t.WTF_SESSION_DRAINING == Methods.wtf_session_get_state((wtf_session*)(IntPtr)Identifier);
    }
    /// <summary>
    /// Check if session is closed
    /// </summary>
    /// <param name="session">Session</param>
    /// <returns>True if WTF_SESSION_CLOSED, false otherwise</returns>
    public bool IsClosed()
    {
        return wtf_session_state_t.WTF_SESSION_CLOSED == Methods.wtf_session_get_state((wtf_session*)(IntPtr)Identifier);
    }
}
