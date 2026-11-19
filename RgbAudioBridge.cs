// Personal RGB bridge. Protocol references and limits are documented in README.md.
// Uses only lighting feature reports and the Windows output audio meter.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorClass { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator {
    [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice {
    [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}
[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation {
    [PreserveSig] int GetPeakValue(out float peak);
    [PreserveSig] int GetMeteringChannelCount(out uint count);
    [PreserveSig] int GetChannelsPeakValues(uint count, IntPtr values);
    [PreserveSig] int QueryHardwareSupport(out uint mask);
}

static class ChromaMouse {
    [DllImport("RzChromaSDK64.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int Init();
    [DllImport("RzChromaSDK64.dll", CallingConvention=CallingConvention.Cdecl)] public static extern int UnInit();
    [DllImport("RzChromaSDK64.dll", CallingConvention=CallingConvention.Cdecl)] static extern int CreateMouseEffect(int type, IntPtr parameters, IntPtr id);
    static IntPtr colors;
    public static int Set(byte r, byte g, byte b) {
        if(colors==IntPtr.Zero) colors=Marshal.AllocHGlobal(9*7*4);
        int color=r | (g<<8) | (b<<16);
        for(int i=0;i<63;i++) Marshal.WriteInt32(colors,i*4,color);
        // CUSTOM2 uses the documented 9x7 virtual grid. Null ID applies immediately.
        return CreateMouseEffect(8,colors,IntPtr.Zero);
    }
    public static int SetGrid(int[] grid) {
        if(grid==null || grid.Length!=63) throw new ArgumentException("Mouse grid must contain 9x7 colours");
        if(colors==IntPtr.Zero) colors=Marshal.AllocHGlobal(9*7*4);
        Marshal.Copy(grid,0,colors,63);
        return CreateMouseEffect(8,colors,IntPtr.Zero);
    }
    public static void Close() { UnInit(); if(colors!=IntPtr.Zero) { Marshal.FreeHGlobal(colors); colors=IntPtr.Zero; } }
}

public static class RgbAudioBridge {
    static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
    static StreamWriter log;
    static void Log(string value) { log.WriteLine(DateTimeOffset.Now.ToString("o")+" "+value); log.Flush(); }
    static SafeFileHandle Open(HidDiagnostics.Device device) {
        if(device==null) throw new Exception("Required lighting interface is missing");
        var handle=HidDiagnostics.CreateFile(device.Path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(handle.IsInvalid) throw new Exception("Cannot open lighting interface: "+Marshal.GetLastWin32Error());
        return handle;
    }
    static void Send(SafeFileHandle handle, byte[] data, string device) {
        if(!HidDiagnostics.HidD_SetFeature(handle,data,data.Length)) throw new Exception(device+" lighting write failed: "+Marshal.GetLastWin32Error());
    }
    static byte[] Read(SafeFileHandle handle, byte report, int length) {
        var data=new byte[length]; data[0]=report;
        if(!HidDiagnostics.HidD_GetFeature(handle,data,length)) throw new Exception("Cannot read report "+report+": "+Marshal.GetLastWin32Error());
        return data;
    }
    static IAudioMeterInformation Meter() {
        var enumerator=(IMMDeviceEnumerator)new MMDeviceEnumeratorClass(); IMMDevice device;
        Marshal.ThrowExceptionForHR(enumerator.GetDefaultAudioEndpoint(0,1,out device));
        var iid=new Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"); object result;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid,23,IntPtr.Zero,out result));
        Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator);
        return (IAudioMeterInformation)result;
    }
    static byte[] LegionFrame(byte r, byte g, byte b) {
        var frame=new byte[33]; frame[0]=0xCC; frame[1]=0x16; frame[2]=1; frame[3]=2; frame[4]=2;
        for(int i=0;i<4;i++) { frame[5+i*3]=r; frame[6+i*3]=g; frame[7+i*3]=b; }
        return frame;
    }
    static byte[] LegionDefaultWave() {
        var frame=LegionFrame(255,255,255); frame[2]=4; frame[18]=1; return frame;
    }
    static byte[] HpRows(byte profile,int rows) {return HpLightingPacket.Rows(profile,rows==0?0:(63 & (63<<(6-rows))));}
    static void Color(double hue, double brightness, out byte red, out byte green, out byte blue) {
        double h=(hue%1)*6; int sector=(int)h; double f=h-sector;
        double v=255*Math.Max(0,Math.Min(1,brightness)); double p=v*.12, q=v*(1-.88*f), t=v*(.12+.88*f);
        double r=0,g=0,b=0;
        switch(sector) { case 0:r=v;g=t;b=p;break;case 1:r=q;g=v;b=p;break;case 2:r=p;g=v;b=t;break;case 3:r=p;g=q;b=v;break;case 4:r=t;g=p;b=v;break;default:r=v;g=p;b=q;break; }
        red=(byte)r;green=(byte)g;blue=(byte)b;
    }
    [STAThread]
    public static int Main(string[] args) {
        if(Array.IndexOf(args,"--stop")>=0) { File.WriteAllText(Path.Combine(Root,"stop.request"),"stop");return 0; }
        bool ownsMutex; using(var mutex=new Mutex(true,"Local\\CodexRgbAudioBridge",out ownsMutex)) {
            if(!ownsMutex && Array.IndexOf(args,"--switch")>=0) {
                File.WriteAllText(Path.Combine(Root,"stop.request"),"switch style");
                try { ownsMutex=mutex.WaitOne(10000); } catch(AbandonedMutexException) { ownsMutex=true; }
            }
            if(!ownsMutex) return 2;
            try { return Array.IndexOf(args,"--equalizer")>=0 ? EqualizerStyle.Run(args) : Run(args); }
            finally { mutex.ReleaseMutex(); }
        }
    }
    static int Run(string[] args) {
            bool probe=Array.IndexOf(args,"--probe")>=0, demo=Array.IndexOf(args,"--demo")>=0;
            bool hpRows=Array.IndexOf(args,"--hp-rows")>=0;
            bool nativeRazer=Array.IndexOf(args,"--razer-native")>=0;
            bool soft=Array.IndexOf(args,"--soft")>=0;
            bool blackout=!soft || Array.IndexOf(args,"--blackout")>=0;
            string preset=soft?"soft":nativeRazer?"razer-native":"dim-dip";
            var preferences=LightingPreferences.Load();var settings=preferences.Profile(preset);
            int seconds=0; int pos=Array.IndexOf(args,"--seconds"); if(pos>=0 && pos+1<args.Length) seconds=int.Parse(args[pos+1]);
            if(!probe && seconds==0) seconds=60; // Bounded trials until physical behaviour is confirmed.
            if(!probe && Array.IndexOf(args,"--run")>=0) seconds=0;
            if(File.Exists(Path.Combine(Root,"stop.request"))) File.Delete(Path.Combine(Root,"stop.request"));
            log=new StreamWriter(Path.Combine(Root,"bridge.log"),true);
            SafeFileHandle hp=null,hpLeds=null,legion=null; byte[] hpOriginal=null;
            bool chroma=false,hpChanged=false,legionChanged=false; IAudioMeterInformation meter=null;
            try {
                HidDiagnostics.Device hpDevice=null,ledDevice=null,legionDevice=null;
                foreach(var d in HidDiagnostics.Enumerate()) {
                    if(d.VendorId=="258A" && d.ProductId=="002A" && d.Version=="1291" && d.UsagePage=="FF00") {
                        if(d.FeatureLength==42) hpDevice=d;
                        if(d.FeatureLength==127) ledDevice=d;
                    }
                    if(d.VendorId=="048D" && d.ProductId=="C985" && d.UsagePage=="FF89" && d.Usage=="00CC") legionDevice=d;
                }
                hp=Open(hpDevice); if(hpRows) hpLeds=Open(ledDevice); legion=Open(legionDevice);
                hpOriginal=Read(hp,10,42); File.WriteAllBytes(Path.Combine(Root,"hp-config-before-bridge.bin"),hpOriginal);
                Log("HP config: "+BitConverter.ToString(hpOriginal));
                if(hpOriginal[0]!=10 || hpOriginal[1]>24 || hpOriginal[3]>6 || hpOriginal[4]>6) throw new Exception("Unexpected GK320 config layout");
                meter=Meter();
                if(!nativeRazer) {
                    int init=ChromaMouse.Init(); chroma=init==0; Log("Razer Chroma Init result="+init);
                    if(!chroma) throw new Exception("Razer Chroma SDK could not initialise");
                } else Log("Razer native visualizer preserved: no SDK session or mouse effect writes");
                if(probe) { float peak; Marshal.ThrowExceptionForHR(meter.GetPeakValue(out peak)); Log("PROBE: HP and Legion handles open; output audio peak="+peak); return 0; }
                var hpConfig=(byte[])hpOriginal.Clone(); hpConfig[1]=(byte)(hpRows ? 2 : 1); hpConfig[4]=(byte)settings.HpBrightness;
                Send(hp,hpConfig,"HP config"); hpChanged=true;
                var check=Read(hp,10,42); Log("HP mode readback: "+BitConverter.ToString(check,0,6));
                if(hpRows) Thread.Sleep(1000);
                DateTime started=DateTime.UtcNow,nextLog=started;
                int frames=0,hpFrames=0,mouseFailures=0,lastHpLevel=-1; float minPeak=1,maxPeak=0;
                double envelope=0,normalizer=.35,lastHpWrite=-1,minLevel=1,maxLevel=0;
                int blackFrames=0,fullFrames=0;
                var response=new AudioLightingResponse();
                Log("START mode="+(demo ? "synthetic lighting demo" : "output audio meter")+", response="+(soft ? "legacy soft pulse" : "full range beat sync")+", HP="+(hpRows ? "row level bar" : "global brightness")+", mouse="+(nativeRazer ? "original Razer visualizer" : "shared dim dip")+", floor="+(blackout ? "zero" : "dim")+", seconds="+seconds);
                while((seconds==0 || (DateTime.UtcNow-started).TotalSeconds<seconds) && !File.Exists(Path.Combine(Root,"stop.request"))) {
                    double elapsed=(DateTime.UtcNow-started).TotalSeconds; float peak;
                    Marshal.ThrowExceptionForHR(meter.GetPeakValue(out peak)); minPeak=Math.Min(minPeak,peak);maxPeak=Math.Max(maxPeak,peak);
                    double hue;
                    if(soft) {
                        if(demo) envelope=.1+.9*(.5+.5*Math.Sin(elapsed*2.5));
                        else {
                        normalizer=Math.Max(.1,Math.Max(peak,normalizer*.997));
                        double target=Math.Pow(Math.Min(1,peak/normalizer),1.7);
                        envelope=target>envelope ? .7*target+.3*envelope : .25*target+.75*envelope;
                        }
                        hue=.52+elapsed*.035;
                    } else {
                        double lightingPeak=demo ? .2+.6*Math.Exp(-(elapsed%.4)/.06) : peak;
                        response.Update(lightingPeak,elapsed);envelope=response.Brightness;hue=response.Hue;
                    }
                    minLevel=Math.Min(minLevel,envelope);maxLevel=Math.Max(maxLevel,envelope);
                    if(envelope==0) blackFrames++;if(envelope==1) fullFrames++;
                    byte r,g,b;Color(hue,blackout ? envelope : .08+.92*envelope,out r,out g,out b);
                    var legionFrame=LegionFrame(r,g,b);LegionLightingPacket.SetBrightness(legionFrame,settings.LegionBrightness);Send(legion,legionFrame,"Legion"); legionChanged=true;
                    if(!nativeRazer) {
                        int result=ChromaMouse.Set(r,g,b); if(result!=0) { mouseFailures++; if(mouseFailures<5) Log("Mouse SDK effect error="+result); }
                    }
                    // Both HP modes use the same audio envelope as the Legion and mouse.
                    // Limit HP writes and send only changed levels.
                    int hpMax=hpRows ? 6 : settings.HpBrightness;
                    int hpLevel=hpMax==0 ? 0 : blackout ? (int)Math.Round(hpMax*envelope) : 1+(int)Math.Round((hpMax-1)*envelope);
                    if(hpLevel!=lastHpLevel && (soft ? frames%4==0 : elapsed-lastHpWrite>=.025)) {
                        if(hpRows) Send(hpLeds,HpRows(hpOriginal[2],hpLevel),"HP rows");
                        else { hpConfig[4]=(byte)hpLevel;Send(hp,hpConfig,"HP brightness"); }
                        hpFrames++;lastHpLevel=hpLevel;lastHpWrite=elapsed;
                    }
                    frames++;
                    LightingRuntime.Publish(preset,elapsed,envelope,null,null,hpRows?HpRows(hpOriginal[2],lastHpLevel):HpLightingPacket.Rows(hpOriginal[2],63),hpRows?settings.HpBrightness:lastHpLevel,legionFrame,settings.LegionBrightness,nativeRazer,new[]{(int)r,(int)g,(int)b});
                    if(DateTime.UtcNow>=nextLog) {
                        var hpCheck=Read(hp,10,42);
                        Log("frames="+frames+", HP lighting writes="+hpFrames+", audio peak="+peak.ToString("F4")+", level="+envelope.ToString("F3")+", brightness range="+minLevel.ToString("F3")+".."+maxLevel.ToString("F3")+", blackout/full frames="+blackFrames+"/"+fullFrames+", detected hits="+response.Onsets+", hue="+(hue%1).ToString("F3")+", HP "+(hpRows ? "rows=" : "brightness=")+lastHpLevel+", HP brightness readback="+hpCheck[4]+", mouse errors="+mouseFailures);nextLog=DateTime.UtcNow.AddSeconds(2);
                    }
                    Thread.Sleep(soft ? 50 : 20);
                }
                Log("END frames="+frames+", HP lighting writes="+hpFrames+", audio range="+minPeak.ToString("F4")+".."+maxPeak.ToString("F4")+", brightness range="+minLevel.ToString("F3")+".."+maxLevel.ToString("F3")+", blackout/full frames="+blackFrames+"/"+fullFrames+", detected hits="+response.Onsets+", mouse errors="+mouseFailures);
                return mouseFailures==0 ? 0 : 3;
            } catch(Exception ex) { Log("ERROR "+ex.ToString());return 1; }
            finally {
                if(hpChanged && hp!=null && hpOriginal!=null) try { Send(hp,hpOriginal,"HP restore");Log("HP prior lighting mode restored"); } catch(Exception ex) { Log("HP restore failed: "+ex.Message); }
                // This Legion controller has no documented readback for its active effect.
                if(legionChanged && legion!=null) try { Send(legion,LegionDefaultWave(),"Legion restore");Log("Legion returned to hardware rainbow wave"); } catch(Exception ex) { Log("Legion restore failed: "+ex.Message); }
                if(chroma) try { ChromaMouse.Close();Log("Chroma session released to previous Razer lighting app"); } catch(Exception ex) { Log("Chroma release failed: "+ex.Message); }
                if(meter!=null) Marshal.ReleaseComObject(meter);
                if(hp!=null)hp.Dispose();if(hpLeds!=null)hpLeds.Dispose();if(legion!=null)legion.Dispose();
                log.Dispose();
            }
    }
}
