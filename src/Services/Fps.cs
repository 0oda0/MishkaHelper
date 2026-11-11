using System.Runtime.InteropServices;

namespace MishkaHelper.Core;

/// <summary>
/// Frames per second of one process, counted the way PresentMon does: every Present call a game makes shows up as an ETW event
/// (DXGI for DirectX 10–12 and most Vulkan games on NVIDIA/AMD, D3D9 for old ones). Needs admin, which the app has.
/// A real-time ETW session is a few P/Invokes, so no tracing library.
/// </summary>
public static class Fps
{
    const string Name = "MishkaHelperFps";
    static readonly Guid Dxgi = new("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9"), D3d9 = new("783ACA0A-790E-4D7F-8451-AA850511C6B9");
    const int DxgiData1 = unchecked((int)0xCA11C036), D3d9Data1 = 0x783ACA0A;
    delegate void EventCb(IntPtr rec);
    static readonly EventCb Cb = OnEvent;   // a field: Windows calls it back, the garbage collector must not take it
    static ulong _session, _trace;
    static IntPtr _logfile, _namePtr;
    static int _count;
    /// <summary>The process whose frames are counted: the game in front.</summary>
    public static volatile int Pid;
    public static bool Running => _session != 0;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int StartTrace(out ulong h, string name, IntPtr props);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int ControlTrace(ulong h, string? name, IntPtr props, uint code);
    [DllImport("advapi32.dll")] static extern int EnableTraceEx2(ulong h, ref Guid g, uint code, byte level, ulong any, ulong all, uint timeout, IntPtr p);
    [DllImport("advapi32.dll", EntryPoint = "OpenTraceW")] static extern ulong OpenTrace(IntPtr logfile);
    [DllImport("advapi32.dll")] static extern int ProcessTrace(ulong[] h, uint n, IntPtr start, IntPtr end);
    [DllImport("advapi32.dll")] static extern int CloseTrace(ulong h);

    /// <summary>EVENT_TRACE_PROPERTIES (120 bytes on x64) with room for the session name after it.</summary>
    static IntPtr Props()
    {
        const int size = 120 + 1024;
        var p = Marshal.AllocHGlobal(size);
        for (int i = 0; i < size; i += 4) Marshal.WriteInt32(p, i, 0);
        Marshal.WriteInt32(p, 0, size);            // Wnode.BufferSize
        Marshal.WriteInt32(p, 40, 1);              // Wnode.ClientContext: QPC clock
        Marshal.WriteInt32(p, 44, 0x20000);        // Wnode.Flags: WNODE_FLAG_TRACED_GUID
        Marshal.WriteInt32(p, 64, 0x100);          // LogFileMode: real time
        Marshal.WriteInt32(p, 68, 1);              // FlushTimer: 1 s
        Marshal.WriteInt32(p, 116, 120);           // LoggerNameOffset
        return p;
    }

    static void Stop(ulong h, string? name)
    {
        var p = Props();
        try { ControlTrace(h, name, p, 1 /* stop */); } finally { Marshal.FreeHGlobal(p); }
    }

    public static bool Start()
    {
        if (Running) return true;
        try
        {
            Stop(0, Name);   // a session left behind by a crash would block the name
            var props = Props();
            int r;
            try { r = StartTrace(out _session, Name, props); } finally { Marshal.FreeHGlobal(props); }
            if (r != 0) { Log.Info("fps: StartTrace " + r); _session = 0; return false; }
            foreach (var g in new[] { Dxgi, D3d9 })
            {
                var id = g;
                EnableTraceEx2(_session, ref id, 1 /* enable */, 4 /* information */, ulong.MaxValue, 0, 0, IntPtr.Zero);
            }
            // EVENT_TRACE_LOGFILEW on x64: 448 bytes; LoggerName at 8, ProcessTraceMode at 28, EventRecordCallback at 424.
            _namePtr = Marshal.StringToHGlobalUni(Name);
            _logfile = Marshal.AllocHGlobal(448);
            for (int i = 0; i < 448; i += 4) Marshal.WriteInt32(_logfile, i, 0);
            Marshal.WriteIntPtr(_logfile, 8, _namePtr);
            Marshal.WriteInt32(_logfile, 28, 0x100 | 0x10000000);   // real time | event record
            Marshal.WriteIntPtr(_logfile, 424, Marshal.GetFunctionPointerForDelegate(Cb));
            _trace = OpenTrace(_logfile);
            if (_trace == ulong.MaxValue) { Log.Info("fps: OpenTrace " + Marshal.GetLastWin32Error()); Stop(); return false; }
            var t = _trace;
            new Thread(() => ProcessTrace(new[] { t }, 1, IntPtr.Zero, IntPtr.Zero)) { IsBackground = true, Name = "FPS" }.Start();
            return true;
        }
        catch (Exception e) { Log.Error(e); Stop(); return false; }
    }

    public static void Stop()
    {
        if (_trace is not (0 or ulong.MaxValue)) CloseTrace(_trace);
        _trace = 0;
        if (_session != 0) { Stop(_session, null); _session = 0; }
        // The trace thread may still be finishing with the logfile: leave these two small blocks rather than free them under it.
        _logfile = _namePtr = IntPtr.Zero;
    }

    /// <summary>Frames counted since the last call.</summary>
    public static int Take() => Interlocked.Exchange(ref _count, 0);

    static void OnEvent(IntPtr rec)
    {
        // EVENT_HEADER: ProcessId at 12, ProviderId at 24 (its first 4 bytes are enough to tell ours apart), EventDescriptor.Id at 40.
        if (Marshal.ReadInt32(rec, 12) != Pid) return;
        int provider = Marshal.ReadInt32(rec, 24);
        int id = (ushort)Marshal.ReadInt16(rec, 40);
        if ((provider == DxgiData1 && id == 42) || (provider == D3d9Data1 && id == 1)) Interlocked.Increment(ref _count);
    }
}
