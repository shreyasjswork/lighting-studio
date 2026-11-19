using System;
using System.Runtime.InteropServices;

[ComImport,Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient {
    [PreserveSig] int Initialize(int mode,uint flags,long duration,long periodicity,IntPtr format,IntPtr session);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint frames);
    [PreserveSig] int IsFormatSupported(int mode,IntPtr format,out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long standard,out long minimum);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid,[MarshalAs(UnmanagedType.IUnknown)] out object result);
}
[ComImport,Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClient {
    [PreserveSig] int GetBuffer(out IntPtr data,out uint frames,out uint flags,out ulong devicePosition,out ulong clockPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}
public sealed class WasapiLoopback : IDisposable {
    IAudioClient client;IAudioCaptureClient capture;
    int channels,bits,blockAlign;bool floating;float[] floats=new float[0];byte[] bytes=new byte[0];
    public int SampleRate { get; private set; }
    public string DeviceId { get; private set; }
    public long Frames { get; private set; }
    public double LastPacketTime { get; private set; }
    public AudioSpectrum Spectrum { get; private set; }
    public WasapiLoopback() {
        IMMDevice device=null;IMMDeviceEnumerator enumerator=null;IntPtr format=IntPtr.Zero;
        try {
            enumerator=(IMMDeviceEnumerator)new MMDeviceEnumeratorClass();
            Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0,1,out device));
            string id;Marshal.ThrowExceptionForHR(device.GetId(out id));DeviceId=id;
            object result;var iid=new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out result));client=(IAudioClient)result;
            Marshal.ThrowExceptionForHR(client.GetMixFormat(out format));
            int tag=(ushort)Marshal.ReadInt16(format,0);channels=(ushort)Marshal.ReadInt16(format,2);
            SampleRate=Marshal.ReadInt32(format,4);blockAlign=(ushort)Marshal.ReadInt16(format,12);bits=(ushort)Marshal.ReadInt16(format,14);
            if(tag==0xFFFE) { byte[] guid=new byte[16];Marshal.Copy(IntPtr.Add(format,24),guid,0,16);var sub=new Guid(guid);if(sub==new Guid("00000003-0000-0010-8000-00aa00389b71")) tag=3;else if(sub==new Guid("00000001-0000-0010-8000-00aa00389b71")) tag=1;else throw new Exception("Unsupported mix subformat "+sub); }
            floating=tag==3;
            if(channels<1 || channels>32 || (floating && bits!=32) || (!floating && (tag!=1 || (bits!=16 && bits!=24 && bits!=32)))) throw new Exception("Unsupported playback mix format");
            // Shared-mode loopback of the default render endpoint. It does not play audio.
            Marshal.ThrowExceptionForHR(client.Initialize(0,0x00020000,1000000,0,format,IntPtr.Zero));
            iid=new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
            Marshal.ThrowExceptionForHR(client.GetService(ref iid,out result));capture=(IAudioCaptureClient)result;
            Spectrum=new AudioSpectrum(SampleRate);LastPacketTime=-100;
            Marshal.ThrowExceptionForHR(client.Start());
        } catch { Dispose();throw; }
        finally { if(format!=IntPtr.Zero) Marshal.FreeCoTaskMem(format);if(device!=null) Marshal.ReleaseComObject(device);if(enumerator!=null) Marshal.ReleaseComObject(enumerator); }
    }
    public static string DefaultDeviceId() {
        var enumerator=(IMMDeviceEnumerator)new MMDeviceEnumeratorClass();IMMDevice device=null;
        try { Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0,1,out device));string id;Marshal.ThrowExceptionForHR(device.GetId(out id));return id; }
        finally { if(device!=null) Marshal.ReleaseComObject(device);Marshal.ReleaseComObject(enumerator); }
    }
    public void Drain(double elapsed) {
        uint pending;Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out pending));
        while(pending>0) {
            IntPtr data;uint frames,flags;ulong devicePosition,clockPosition;
            Marshal.ThrowExceptionForHR(capture.GetBuffer(out data,out frames,out flags,out devicePosition,out clockPosition));
            try {
                if((flags&1)!=0) Spectrum.Reset();
                bool silent=(flags&2)!=0;
                int total=checked((int)frames*channels);
                if(!silent) {
                    if(floating) { if(floats.Length<total) floats=new float[total];Marshal.Copy(data,floats,0,total); }
                    else { int length=checked((int)frames*blockAlign);if(bytes.Length<length) bytes=new byte[length];Marshal.Copy(data,bytes,0,length); }
                }
                for(int f=0;f<frames;f++) {
                    double l=0,r=0;
                    if(!silent) { l=Sample(f,0);r=Sample(f,channels>1?1:0); }
                    Spectrum.Push(l,r);
                }
                Frames+=frames;LastPacketTime=elapsed;
            } finally { Marshal.ThrowExceptionForHR(capture.ReleaseBuffer(frames)); }
            Marshal.ThrowExceptionForHR(capture.GetNextPacketSize(out pending));
        }
    }
    double Sample(int frame,int channel) {
        if(floating) return floats[frame*channels+channel];
        int offset=frame*blockAlign+channel*(bits/8);
        if(bits==16) return BitConverter.ToInt16(bytes,offset)/32768.0;
        if(bits==32) return BitConverter.ToInt32(bytes,offset)/2147483648.0;
        int value=bytes[offset]|(bytes[offset+1]<<8)|(bytes[offset+2]<<16);if((value&0x800000)!=0) value|=unchecked((int)0xFF000000);return value/8388608.0;
    }
    public void Dispose() { if(client!=null) { client.Stop(); }if(capture!=null) { Marshal.ReleaseComObject(capture);capture=null; }if(client!=null) { Marshal.ReleaseComObject(client);client=null; } }
}
