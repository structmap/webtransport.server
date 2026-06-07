using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.InteropServices;
using Structmap.WebTransportFast;
using Structmap.WebTransportFast.Native;

namespace Structmap;

public static class WebTransportServerUtil
{
    public static async void SendLoop(DuplexPipes pp, IntPtr streamPointer)
    {
        PipeReader reader = pp.Outgoing.Reader;
        var ppSent = pp.Sent.Writer;
        ReadResult result;
        do
        {
            result = await reader.ReadAsync();
            //Console.Out.WriteLine("readasync success");
            if (result.IsCanceled) break;
            var memoryHandles = new List<MemoryHandle>();
            foreach (var memory in result.Buffer.Slice(result.Buffer.Start, result.Buffer.End))
            {
                var mh = memory.Pin();
                await ppSent.WriteAsync(mh); // potential backpressure here
                unsafe
                {
                    var buffers = MemoryAllocator.malloc((uint)Marshal.SizeOf(typeof(wtf_buffer_t)));
                    Marshal.WriteIntPtr(buffers, IntPtr.Zero);
                    Marshal.StructureToPtr(new wtf_buffer_t()
                    {
                        data = (byte*)mh.Pointer,
                        length = (uint)memory.Length,
                    }, buffers, false);

                    // use the fact that an array of one item is just pointer to the first
                    var sendResult = Methods.wtf_stream_send((wtf_stream*)streamPointer, (wtf_buffer_t*)buffers, 1, WebTransportServer.FALSE);
                    if (sendResult != wtf_result_t.WTF_SUCCESS)
                    {
                        var msg = Marshal.PtrToStringAnsi((IntPtr)Methods.wtf_result_to_string(sendResult));
                        Console.Error.WriteLine("[STREAM] Failed to write to stream 0x{0:x}: {1}",
                            streamPointer, msg);
                        //Console.Out.WriteLine("trysend fail");
                        mh.Dispose();
                        break;
                    }

                    //Console.Out.WriteLine("trysend success {0}", memory.Length);
                    memoryHandles.Add(mh);
                }
            }
            reader.AdvanceTo(result.Buffer.End);
        } while (!result.IsCompleted);
    }
}