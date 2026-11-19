using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.IO.MemoryMappedFiles;
using System.Threading;

public sealed class LightingProfile {
    public int HpBrightness=5;
    public int HpSpeed=3;
    public int LegionBrightness=2;
    public int LegionEffect=4;
    public string[] LegionColors={"#93f553","#44dac3","#468ded","#b07bef"};
    public double Sensitivity=1;
    public int HoldMilliseconds=55;
    public int FallMilliseconds=220;
    public int Width=0;
    public bool BottomSupport=true;
    public int Rows=63;
    public string Name="";
    public void Validate() {
        if(HpBrightness<0 || HpBrightness>5 || HpSpeed<1 || HpSpeed>5 || LegionBrightness<0 || LegionBrightness>2 || LegionEffect<1 || LegionEffect>4 || Sensitivity<.5 || Sensitivity>2 || double.IsNaN(Sensitivity) || HoldMilliseconds<0 || HoldMilliseconds>200 || FallMilliseconds<100 || FallMilliseconds>700 || Width<0 || Width>2 || Rows<0 || Rows>63) throw new ArgumentException("A lighting setting is outside its supported range.");
        if(Name==null || Name.Length>40) throw new ArgumentException("Pattern names must be 40 characters or fewer.");
        if(LegionColors==null || LegionColors.Length!=4) throw new ArgumentException("Choose a colour for each Legion zone.");
        foreach(string color in LegionColors) if(color==null || !System.Text.RegularExpressions.Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$")) throw new ArgumentException("Use valid zone colours.");
    }
}
public sealed class LightingPreferences {
    public Dictionary<string,LightingProfile> Profiles=new Dictionary<string,LightingProfile>();
    public Dictionary<string,SavedLightingStyle> SavedStyles=new Dictionary<string,SavedLightingStyle>();
    public string SelectedStyle="equalizer";
    public string SelectedPreset="";
    static readonly object Gate=new object();
    public static readonly string PathName=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"music-lighting-settings.json");
    public LightingProfile Profile(string id) {
        SavedLightingStyle saved;
        if(!string.IsNullOrEmpty(SelectedPreset) && SavedStyles.TryGetValue(SelectedPreset,out saved) && saved.Style==id && SelectedStyle==id) return saved.Profile;
        LightingProfile profile;if(!Profiles.TryGetValue(id,out profile)) {profile=new LightingProfile();Profiles[id]=profile;}return profile;
    }
    public static bool ValidPresetId(string id) {return id!=null && System.Text.RegularExpressions.Regex.IsMatch(id,"^preset-[a-f0-9]{32}$");}
    public string AddSavedStyle(string name,string style,LightingProfile profile) {
        if(!ValidStyle(style) || profile==null) throw new ArgumentException("Choose an effect to save.");
        name=name==null?"":name.Trim();
        if(name.Length==0 || name.Length>40) throw new ArgumentException("Enter a style name between 1 and 40 characters.");
        profile.Validate();
        string id="preset-"+Guid.NewGuid().ToString("N");
        var copy=new JavaScriptSerializer().Deserialize<LightingProfile>(new JavaScriptSerializer().Serialize(profile));
        SavedStyles[id]=new SavedLightingStyle {Name=name,Style=style,Profile=copy};
        return id;
    }
    public void SelectProfile(string style,LightingProfile profile,string presetId) {
        if(!ValidStyle(style) || profile==null) throw new ArgumentException("Choose a style and its settings.");profile.Validate();
        if(string.IsNullOrEmpty(presetId)) {Profiles[style]=profile;SelectedPreset="";}
        else {
            SavedLightingStyle saved;
            if(!ValidPresetId(presetId) || !SavedStyles.TryGetValue(presetId,out saved) || saved.Style!=style) throw new ArgumentException("That saved style is unavailable. Refresh the control panel.");
            saved.Profile=profile;SelectedPreset=presetId;
        }
        SelectedStyle=style;
    }
    public void Validate() {
        if(Profiles==null || SavedStyles==null || !ValidStyle(SelectedStyle)) throw new ArgumentException("Saved lighting settings are invalid.");
        foreach(var item in Profiles) {if(!ValidStyle(item.Key) || item.Value==null) throw new ArgumentException("Invalid saved pattern.");item.Value.Validate();}
        foreach(var item in SavedStyles) {
            var saved=item.Value;
            if(!ValidPresetId(item.Key) || saved==null || !ValidStyle(saved.Style) || saved.Profile==null || string.IsNullOrWhiteSpace(saved.Name) || saved.Name.Length>40) throw new ArgumentException("Invalid saved style.");saved.Profile.Validate();
        }
        if(!string.IsNullOrEmpty(SelectedPreset) && (!SavedStyles.ContainsKey(SelectedPreset) || SavedStyles[SelectedPreset].Style!=SelectedStyle)) throw new ArgumentException("The selected saved style is unavailable.");
    }
    public static bool ValidStyle(string id) {
        if(id=="dim-dip" || id=="soft" || id=="razer-native" || id=="equalizer") return true;
        int pattern;return id!=null && id.StartsWith("hp-") && int.TryParse(id.Substring(3),out pattern) && pattern>=1 && pattern<=20;
    }
    public static LightingPreferences Load() {
        lock(Gate) {
            if(!File.Exists(PathName)) return new LightingPreferences();
            var settings=new JavaScriptSerializer().Deserialize<LightingPreferences>(File.ReadAllText(PathName));
            if(settings==null) throw new InvalidDataException("Saved lighting settings are invalid.");settings.Validate();
            return settings;
        }
    }
    public void Save() {
        Validate();
        lock(Gate) {
            string temporary=PathName+".new";File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(this),new UTF8Encoding(false));
            if(File.Exists(PathName)) File.Replace(temporary,PathName,null);else File.Move(temporary,PathName);
        }
    }
}
public sealed class SavedLightingStyle {
    public string Name="";
    public string Style="equalizer";
    public LightingProfile Profile=new LightingProfile();
}
public static class LightingRuntime {
    const int Capacity=32768;
    static readonly object Gate=new object();
    static readonly Mutex SharedGate=new Mutex(false,"Local\\MusicLightingFramesGateV3");
    static MemoryMappedFile memory;
    static MemoryMappedViewAccessor view;
    static double last=-1,lastFile=-1;
    public static OutgoingLightingFrame Frame(string style,double drive,double[] levels,int[] heights,byte[] hpFrame,int hpBrightness,byte[] legionFrame,int legionBrightness,bool nativeRazer,int[] mouseRgb) {
        var frame=new OutgoingLightingFrame {Style=style,Timestamp=DateTimeOffset.UtcNow.ToString("o"),Drive=drive,Levels=levels,Heights=heights,HpBrightness=hpBrightness,LegionBrightness=legionBrightness,RazerNative=nativeRazer,RazerRgb=nativeRazer?null:mouseRgb};
        if(hpFrame!=null) {frame.HpKeys=new int[125];for(int i=0;i<125;i++)frame.HpKeys[i]=hpFrame[i+2];}
        if(legionFrame!=null && legionFrame[2]==1) {frame.LegionColors=new int[4][];for(int zone=0;zone<4;zone++)frame.LegionColors[zone]=new[]{(int)legionFrame[5+zone*3],(int)legionFrame[6+zone*3],(int)legionFrame[7+zone*3]};}
        return frame;
    }
    static void Open() {if(view==null) {memory=MemoryMappedFile.CreateOrOpen("Local\\MusicLightingFramesV3",Capacity,MemoryMappedFileAccess.ReadWrite);view=memory.CreateViewAccessor();}}
    public static void Publish(string style,double time,double drive,double[] levels,int[] heights,byte[] hpFrame,int hpBrightness,byte[] legionFrame,int legionBrightness,bool nativeRazer,int[] mouseRgb) {
        double now=DateTime.UtcNow.Ticks/10000000.0;if(now-last<.025)return;last=now;
        var frame=Frame(style,drive,levels,heights,hpFrame,hpBrightness,legionFrame,legionBrightness,nativeRazer,mouseRgb);
        string json=new JavaScriptSerializer().Serialize(frame);byte[] bytes=Encoding.UTF8.GetBytes(json);
        // Nonblocking publication: UI readers cannot delay audio or HID writes.
        lock(Gate) try {Open();bool owned=false;try {try {owned=SharedGate.WaitOne(0);}catch(AbandonedMutexException) {owned=true;}if(owned && bytes.Length<Capacity-4) {view.WriteArray(4,bytes,0,bytes.Length);view.Write(0,bytes.Length);}}finally {if(owned)SharedGate.ReleaseMutex();}}catch(IOException) {}
        if(now-lastFile<.12)return;lastFile=now;
        string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lighting-live.json");
        try {File.WriteAllText(path+".new",json);if(File.Exists(path))File.Replace(path+".new",path,null);else File.Move(path+".new",path);}catch(IOException) {}
    }
    public static object Read() {
        lock(Gate) try {Open();bool owned=false;try {try {owned=SharedGate.WaitOne(5);}catch(AbandonedMutexException) {owned=true;}if(!owned)return null;int length=view.ReadInt32(0);if(length<=0 || length>Capacity-4)return null;var bytes=new byte[length];view.ReadArray(4,bytes,0,length);return new JavaScriptSerializer().DeserializeObject(Encoding.UTF8.GetString(bytes));}finally {if(owned)SharedGate.ReleaseMutex();}}catch(IOException) {return null;}
    }
}

public sealed class OutgoingLightingFrame {
    public int Version=3;
    public string Style,Timestamp;
    public double Drive;
    public double[] Levels;
    public int[] Heights,HpKeys,RazerRgb;
    public int HpBrightness,LegionBrightness;
    public int[][] LegionColors;
    public bool RazerNative;
}
