using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

public static class EqualizerStyle {
    static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
    static StreamWriter log;
    static void Log(string text) { log.WriteLine(DateTimeOffset.Now.ToString("o")+" "+text);log.Flush(); }
    static SafeFileHandle Open(HidDiagnostics.Device device) {
        if(device==null) throw new Exception("Required lighting interface is missing");
        var handle=HidDiagnostics.CreateFile(device.Path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);
        if(handle.IsInvalid) throw new Exception("Could not open lighting interface");return handle;
    }
    static void Send(SafeFileHandle handle,byte[] data) { if(!HidDiagnostics.HidD_SetFeature(handle,data,data.Length)) throw new Exception("Lighting write failed: "+Marshal.GetLastWin32Error()); }
    static byte[] Read(SafeFileHandle handle,byte report,int length) { byte[] data=new byte[length];data[0]=report;if(!HidDiagnostics.HidD_GetFeature(handle,data,length)) throw new Exception("Lighting read failed");return data; }
    public static int PackedColor(double hue,double level) {
        double h=(hue-Math.Floor(hue))*6,f=h-Math.Floor(h),v=255*Math.Max(0,Math.Min(1,level)),p=0,q=v*(1-f),t=v*f;
        double r=0,g=0,b=0;
        switch((int)h) {case 0:r=v;g=t;b=p;break;case 1:r=q;g=v;b=p;break;case 2:r=p;g=v;b=t;break;case 3:r=p;g=q;b=v;break;case 4:r=t;g=p;b=v;break;default:r=v;g=p;b=q;break;}
        return (int)r | ((int)g<<8) | ((int)b<<16);
    }
    public static double Group(double[] levels,int first,int end) { double sum=0;for(int i=first;i<end;i++) sum+=levels[i]*levels[i];return Math.Sqrt(sum/(end-first)); }
    public static int[] MouseGrid(double[] levels) {
        var grid=new int[63];
        for(int col=0;col<7;col++) {
            double amplitude=Group(levels,col*3,col*3+3),height=amplitude*9;
            for(int row=0;row<9;row++) {
                double fill=Math.Max(0,Math.Min(1,height-(8-row)));
                grid[row*7+col]=PackedColor(.08+.72*col/6.0,fill);
            }
        }
        // Side LEDs provide two independently rising meters; bottom LEDs retain
        // separate frequency colours. Central logo/wheel respond to the spectrum.
        double bass=Group(levels,0,7),treble=Group(levels,14,21),middle=Group(levels,7,14);
        for(int row=1;row<=7;row++) {
            grid[row*7]=PackedColor(.10,Math.Max(0,Math.Min(1,bass*7-(7-row))));
            grid[row*7+6]=PackedColor(.76,Math.Max(0,Math.Min(1,treble*7-(7-row))));
        }
        for(int col=1;col<=5;col++) grid[8*7+col]=PackedColor(.08+.72*col/6.0,Group(levels,col*3,col*3+3));
        grid[2*7+3]=PackedColor(.76,treble);grid[3*7+3]=PackedColor(.40,middle);
        return grid;
    }
    public static byte[] HpMap(double[] levels,byte profile) {
        var heights=new int[21];for(int column=0;column<21;column++) heights[column]=(int)Math.Round(Math.Max(0,Math.Min(1,levels[column]))*6);
        return HpLightingPacket.Bars(profile,heights,false);
    }
    public static byte[] LegionZones(double[] levels) {
        var frame=new byte[33];frame[0]=0xCC;frame[1]=0x16;frame[2]=1;frame[3]=2;frame[4]=2;
        for(int zone=0;zone<4;zone++) {
            int first=zone*21/4,end=(zone+1)*21/4;
            int color=PackedColor(.08+.72*zone/3.0,Group(levels,first,end));
            frame[5+zone*3]=(byte)color;frame[6+zone*3]=(byte)(color>>8);frame[7+zone*3]=(byte)(color>>16);
        }
        return frame;
    }
    public static int Run(string[] args) {
        int seconds=60,pos=Array.IndexOf(args,"--seconds");if(pos>=0 && pos+1<args.Length) seconds=int.Parse(args[pos+1]);if(Array.IndexOf(args,"--run")>=0) seconds=0;
        bool probe=Array.IndexOf(args,"--probe")>=0;
        bool nativeRazer=Array.IndexOf(args,"--razer-native")>=0;
        SafeFileHandle hp=null,hpLeds=null,legion=null;byte[] original=null;bool hpChanged=false,legionChanged=false,chroma=false;
        WasapiLoopback audio=null;
        var settings=LightingPreferences.Load().Profile("equalizer");
        int hpWrites=0;byte[] lastHpFrame=null;
        if(File.Exists(Path.Combine(Root,"stop.request"))) File.Delete(Path.Combine(Root,"stop.request"));
        log=new StreamWriter(Path.Combine(Root,"equalizer.log"),true);
        try {
            HidDiagnostics.Device hpDevice=null,ledDevice=null,legionDevice=null;
            foreach(var d in HidDiagnostics.Enumerate()) {
                if(d.VendorId=="258A" && d.ProductId=="002A" && d.Version=="1291" && d.UsagePage=="FF00") {if(d.FeatureLength==42) hpDevice=d;if(d.FeatureLength==127) ledDevice=d;}
                if(d.VendorId=="048D" && d.ProductId=="C985" && d.UsagePage=="FF89" && d.Usage=="00CC") legionDevice=d;
            }
            audio=new WasapiLoopback();Log("Loopback playback sample rate="+audio.SampleRate+", device="+audio.DeviceId+"; FFT="+AudioSpectrum.Size+", bands="+AudioSpectrum.Bands+"; no saved audio");
            hp=Open(hpDevice);hpLeds=Open(ledDevice);legion=Open(legionDevice);
            original=Read(hp,10,42);if(original[0]!=10 || original[1]>24 || original[4]>6) throw new Exception("Unexpected HP lighting layout");
            if(original[2]<1 || original[2]>15) throw new Exception("Unexpected HP lighting profile field");
            if(!probe) {
                File.WriteAllBytes(Path.Combine(Root,"hp-config-before-equalizer.bin"),original);
                if(!nativeRazer) {int init=ChromaMouse.Init();chroma=init==0;if(!chroma) throw new Exception("Chroma init failed: "+init);}
                else Log("Mouse control left to Razer apps: no Chroma SDK session or mouse effect writes; Visualizer must be enabled in Razer app");
                var config=(byte[])original.Clone();config[1]=2;config[4]=(byte)settings.HpBrightness;hpChanged=true;Send(hp,config);
                Thread.Sleep(1000);
                Log("START latched spectrum: HP profile header="+original[2]+", 7/0 key values, changed frames only; shared gain, quiet blackout, peak hold="+settings.HoldMilliseconds+"ms, fall="+settings.FallMilliseconds+"ms, bottom support="+settings.BottomSupport+", mouse="+(nativeRazer?"Razer app control":"segmented spectrum")+", seconds="+seconds);
            } else { Log("PROBE: loopback and lighting handles only; no lighting commands");seconds=5; }
            var clock=Stopwatch.StartNew();double nextLog=0,nextDeviceCheck=2;int frames=0,mouseErrors=0,audioRestarts=0;
            var motion=new EqualizerMotion {Sensitivity=settings.Sensitivity,HoldSeconds=settings.HoldMilliseconds/1000.0,FallSeconds=settings.FallMilliseconds/1000.0,Width=settings.Width};int darkFrames=0,fullPeakFrames=0;
            while((seconds==0 || clock.Elapsed.TotalSeconds<seconds) && !File.Exists(Path.Combine(Root,"stop.request"))) {
                double elapsed=clock.Elapsed.TotalSeconds;
                if(elapsed>=nextDeviceCheck) {
                    string id=WasapiLoopback.DefaultDeviceId();
                    if(id!=audio.DeviceId) {audio.Dispose();audio=new WasapiLoopback();motion=new EqualizerMotion {Sensitivity=settings.Sensitivity,HoldSeconds=settings.HoldMilliseconds/1000.0,FallSeconds=settings.FallMilliseconds/1000.0,Width=settings.Width};audioRestarts++;Log("Playback device switched, loopback reopened");}
                    nextDeviceCheck=elapsed+2;
                }
                audio.Drain(elapsed);audio.Spectrum.Update(elapsed,elapsed-audio.LastPacketTime<.18);
                motion.Update(audio.Spectrum.Amplitudes,audio.Spectrum.Rms,elapsed);double[] levels=motion.Levels;
                bool dark=true,full=false;for(int i=0;i<21;i++) {if(motion.Heights[i]>0) dark=false;if(motion.Heights[i]==6) full=true;}
                if(dark) darkFrames++;if(full) fullPeakFrames++;
                if(!probe) {
                    legionChanged=true;var legionFrame=LegionZones(levels);LegionLightingPacket.SetBrightness(legionFrame,settings.LegionBrightness);Send(legion,legionFrame);
                    if(!nativeRazer) {int result=ChromaMouse.SetGrid(MouseGrid(levels));if(result!=0) {mouseErrors++;if(mouseErrors<5) Log("Chroma effect error="+result);}}
                    byte[] hpFrame=motion.HpFrame(original[2],settings.BottomSupport);
                    if(!HpLightingPacket.Same(hpFrame,lastHpFrame)) {Send(hpLeds,hpFrame);lastHpFrame=hpFrame;hpWrites++;}
                    LightingRuntime.Publish("equalizer",elapsed,motion.Drive,levels,motion.Heights,hpFrame,settings.HpBrightness,legionFrame,settings.LegionBrightness,nativeRazer,null);
                }
                frames++;
                if(elapsed>=nextLog) {
                    string[] summary=new string[21];for(int i=0;i<21;i++) summary[i]=levels[i].ToString("F2",System.Globalization.CultureInfo.InvariantCulture);
                    Log("frames="+frames+", samples="+audio.Frames+", rms="+audio.Spectrum.Rms.ToString("F5")+", drive="+motion.Drive.ToString("F2")+", dark/full-height frames="+darkFrames+"/"+fullPeakFrames+", levels="+string.Join(",",summary)+", HP map writes="+hpWrites+", mouse errors="+mouseErrors+", device restarts="+audioRestarts);nextLog=elapsed+2;
                }
                int wait=(int)Math.Max(1,33-(clock.Elapsed.TotalSeconds-elapsed)*1000);Thread.Sleep(wait);
            }
            Log("END frames="+frames+", HP map writes="+hpWrites+", mouse errors="+mouseErrors);return mouseErrors==0?0:3;
        } catch(Exception ex) {Log("ERROR "+ex);return 1;}
        finally {
            if(hpChanged && hp!=null && original!=null) try {Send(hp,original);Log("HP prior global lighting restored");} catch(Exception ex) {Log("HP restore failed: "+ex.Message);}
            if(legionChanged && legion!=null) try {var wave=LegionZones(new double[21]);wave[2]=4;wave[18]=1;Send(legion,wave);Log("Legion hardware rainbow wave restored");} catch(Exception ex) {Log("Legion restore failed: "+ex.Message);}
            if(chroma) {ChromaMouse.Close();Log("Chroma released");}
            if(audio!=null) audio.Dispose();if(hp!=null) hp.Dispose();if(hpLeds!=null) hpLeds.Dispose();if(legion!=null) legion.Dispose();log.Dispose();
        }
    }
}
