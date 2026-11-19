using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

public static class MusicLightingServer {
    public const int Port=17653;
    static readonly string Root=AppDomain.CurrentDomain.BaseDirectory;
    static readonly string Origin="http://127.0.0.1:"+Port;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static readonly object MutationGate=new object();
    static string token,active="",lastError="";
    static object devices;
    [DllImport("kernel32.dll",SetLastError=true)]
    static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
    static void PreventSocketInheritance(Socket socket) {
        if(!SetHandleInformation(socket.Handle,1,0)) throw new IOException("The local control socket could not be isolated from child processes.");
    }
    static void Record(string message) {try {File.AppendAllText(Path.Combine(Root,"control-panel.log"),DateTimeOffset.Now.ToString("o")+" "+message+Environment.NewLine);}catch(IOException) {}}
    static object DeviceState() {
        bool hp=false,legion=false,mouse=false;
        foreach(var d in HidDiagnostics.Enumerate()) {
            if(d.VendorId=="258A" && d.ProductId=="002A" && d.Version=="1291" && d.FeatureLength==42) hp=true;
            if(d.VendorId=="048D" && d.ProductId=="C985" && d.FeatureLength==33) legion=true;
            if(d.VendorId=="1532" && d.ProductId=="0099") mouse=true;
        }
        return new {Hp=hp,Legion=legion,Razer=mouse};
    }
    static object LatestFrame() {object frame=LightingRuntime.Read();if(frame==null)try {string path=Path.Combine(Root,"lighting-live.json");if(File.Exists(path))frame=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path));}catch(IOException) {}return frame;}
    static object State() {
        object live=LatestFrame();
        var processes=Process.GetProcessesByName("RgbAudioBridge");bool running=processes.Length>0;foreach(var process in processes) process.Dispose();
        string effectiveActive=active;var record=live as Dictionary<string,object>;object recordedStyle;
        if(!running && effectiveActive=="" && record!=null && record.TryGetValue("Style",out recordedStyle) && recordedStyle is string && ((string)recordedStyle).StartsWith("hp-"))effectiveActive=(string)recordedStyle;
        return new {Settings=LightingPreferences.Load(),Devices=devices,Running=running,Active=effectiveActive,Live=live,Error=lastError,UiVersion=3,ControllerRoot=Root};
    }
    static void StopBridge() {
        File.WriteAllText(Path.Combine(Root,"hp-held-row.stop"),"control panel");
        File.WriteAllText(Path.Combine(Root,"stop.request"),"control panel");
        using(var mutex=new Mutex(false,"Local\\CodexRgbAudioBridge")) {
            bool owned=false;
            try {try {owned=mutex.WaitOne(12000);}catch(AbandonedMutexException) {owned=true;}if(!owned) throw new Exception("Lighting is still busy. Wait a moment and try again.");}
            finally {if(owned) mutex.ReleaseMutex();}
        }
        // A held test can resume the bridge in its cleanup. Ask that process to
        // stop too, and wait for the process to finish before changing settings.
        Thread.Sleep(200);File.WriteAllText(Path.Combine(Root,"stop.request"),"control panel");
        foreach(var process in Process.GetProcessesByName("RgbAudioBridge")) using(process) if(!process.WaitForExit(10000)) throw new Exception("The previous style could not stop cleanly.");
    }
    static SafeFileHandle Open(HidDiagnostics.Device device) {
        if(device==null) throw new Exception("The selected keyboard is disconnected. Connect it and refresh devices.");
        var handle=HidDiagnostics.CreateFile(device.Path,0,3,IntPtr.Zero,3,0,IntPtr.Zero);if(handle.IsInvalid) throw new Exception("The lighting interface could not be opened.");return handle;
    }
    static void Send(SafeFileHandle handle,byte[] frame) {if(!HidDiagnostics.HidD_SetFeature(handle,frame,frame.Length)) throw new Exception("The keyboard did not accept the lighting change.");}
    static void NativePattern(int pattern,LightingProfile profile) {
        bool owned=false;using(var mutex=new Mutex(true,"Local\\CodexRgbAudioBridge",out owned)) {
            if(!owned) throw new Exception("Another lighting style is still running.");
            try {
                HidDiagnostics.Device hp=null,led=null,legion=null;
                foreach(var d in HidDiagnostics.Enumerate()) {
                    if(d.VendorId=="258A" && d.ProductId=="002A" && d.Version=="1291" && d.UsagePage=="FF00") {if(d.FeatureLength==42) hp=d;if(d.FeatureLength==127) led=d;}
                    if(d.VendorId=="048D" && d.ProductId=="C985" && d.UsagePage=="FF89" && d.Usage=="00CC") legion=d;
                }
                byte[] hpOutput=null,legionOutput=null;
                using(var handle=Open(hp)) {
                    var config=new byte[42];config[0]=10;if(!HidDiagnostics.HidD_GetFeature(handle,config,config.Length)) throw new Exception("The HP lighting settings could not be read.");
                    if(config[1]>24 || config[4]>6) throw new Exception("The HP lighting settings have an unexpected format.");
                    if(pattern==2 && (config[2]<1 || config[2]>15)) throw new Exception("The current HP custom profile is unsupported.");
                    config[1]=(byte)pattern;config[3]=(byte)(6-profile.HpSpeed);config[4]=(byte)profile.HpBrightness;Send(handle,config);
                    if(pattern==2) {Thread.Sleep(1000);hpOutput=HpLightingPacket.Rows(config[2],profile.Rows);using(var ledHandle=Open(led))Send(ledHandle,hpOutput);}
                    if(pattern==1)hpOutput=HpLightingPacket.Rows(config[2],63);
                }
                if(legion!=null) using(var handle=Open(legion)) {
                    var frame=new byte[33];frame[0]=0xCC;frame[1]=0x16;frame[2]=LegionLightingPacket.NativeEffect(profile.LegionEffect);frame[3]=2;frame[18]=1;
                    for(int zone=0;zone<4;zone++) {string color=profile.LegionColors[zone];frame[5+zone*3]=Convert.ToByte(color.Substring(1,2),16);frame[6+zone*3]=Convert.ToByte(color.Substring(3,2),16);frame[7+zone*3]=Convert.ToByte(color.Substring(5,2),16);}LegionLightingPacket.SetBrightness(frame,profile.LegionBrightness);Send(handle,frame);legionOutput=frame;
                }
                LightingRuntime.Publish("hp-"+pattern,0,1,null,null,hpOutput,profile.HpBrightness,legionOutput,profile.LegionBrightness,true,null);
            } finally {mutex.ReleaseMutex();}
        }
    }
    public sealed class Change {public string Style {get;set;}public LightingProfile Profile {get;set;}public string PresetId {get;set;}public string Name {get;set;}}
    static Change ValidateChange(string body) {
        var change=Json.Deserialize<Change>(body);if(change==null || !LightingPreferences.ValidStyle(change.Style) || change.Profile==null) throw new ArgumentException("Choose a style and its settings.");change.Profile.Validate();
        return change;
    }
    static object ChangeSettings(string body,bool apply) {
        var change=ValidateChange(body);
        var preferences=LightingPreferences.Load();preferences.SelectProfile(change.Style,change.Profile,change.PresetId);preferences.Save();
        if(apply) {
            StopBridge();
            if(change.Style.StartsWith("hp-")) NativePattern(int.Parse(change.Style.Substring(3)),change.Profile);
            else {
                string arguments="--switch --run";
                if(change.Style=="soft") arguments+=" --soft";
                if(change.Style=="equalizer") arguments+=" --equalizer --razer-native";
                if(change.Style=="razer-native") arguments+=" --razer-native --blackout";
                string stagedBridge=Path.Combine(Root,"RgbAudioBridge.next.exe");
                if(File.Exists(stagedBridge)) {File.Copy(stagedBridge,Path.Combine(Root,"RgbAudioBridge.exe"),true);File.Delete(stagedBridge);Record("Installed staged bridge after graceful stop");}
                var info=new ProcessStartInfo(Path.Combine(Root,"RgbAudioBridge.exe"),arguments) {UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
                using(var process=Process.Start(info)) {Thread.Sleep(1250);if(process.HasExited) throw new Exception("The audio style could not start. Check that both keyboards are connected and Windows has an audio output.");}
            }
            active=change.Style;lastError="";Record("Applied "+change.Style+", HP brightness="+change.Profile.HpBrightness);
        }
        return new {Ok=true,Applied=apply,Style=change.Style};
    }
    static object SaveNewStyle(string body) {
        var change=ValidateChange(body);var preferences=LightingPreferences.Load();
        string id=preferences.AddSavedStyle(change.Name,change.Style,change.Profile);preferences.Save();Record("Saved custom style "+id);
        return new {Ok=true,Id=id,SavedStyle=preferences.SavedStyles[id]};
    }
    static void Response(NetworkStream stream,int status,string type,byte[] payload) {
        string description=status==200?"OK":status==400?"Bad Request":status==403?"Forbidden":status==404?"Not Found":"Internal Server Error";
        string header="HTTP/1.1 "+status+" "+description+"\r\nContent-Type: "+type+"\r\nContent-Length: "+payload.Length+"\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nX-Frame-Options: DENY\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'\r\n\r\n";
        byte[] bytes=Encoding.ASCII.GetBytes(header);stream.Write(bytes,0,bytes.Length);stream.Write(payload,0,payload.Length);
    }
    static void Reply(NetworkStream stream,object value,int status) {Response(stream,status,"application/json; charset=utf-8",Encoding.UTF8.GetBytes(Json.Serialize(value)));}
    static void Client(object item) {
        using(var client=(TcpClient)item) using(var stream=client.GetStream()) {
            client.ReceiveTimeout=5000;client.SendTimeout=5000;
            try {
                var bytes=new List<byte>();int next;
                while(bytes.Count<16384 && (next=stream.ReadByte())!=-1) {bytes.Add((byte)next);int n=bytes.Count;if(n>=4 && bytes[n-4]==13 && bytes[n-3]==10 && bytes[n-2]==13 && bytes[n-1]==10) break;}
                string[] lines=Encoding.ASCII.GetString(bytes.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);string[] first=lines[0].Split(' ');
                if(first.Length!=3) throw new ArgumentException("Invalid request.");
                var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);for(int i=1;i<lines.Length;i++) {int colon=lines[i].IndexOf(':');if(colon>0) headers[lines[i].Substring(0,colon).Trim()]=lines[i].Substring(colon+1).Trim();}
                string host;if(!headers.TryGetValue("Host",out host) || host!="127.0.0.1:"+Port) {Reply(stream,new {Error="Use the local Music Lighting address."},403);return;}
                string path=first[1].Split('?')[0];
                if(first[0]=="GET") {
                    if(path=="/api/state") {lock(MutationGate) Reply(stream,State(),200);return;}
                    if(path=="/api/live") {Reply(stream,LatestFrame(),200);return;}
                    string assetBase="",assetRoot=Path.Combine(Root,"control-panel"),assetPath=path;
                    string variantName=path.Split('/').Length>1?path.Split('/')[1]:"";
                    if(variantName=="neon" || variantName=="glass-2d" || variantName=="glass-3d") {
                        string variantsPath=Path.Combine(Root,"ui-variants.json");
                        Dictionary<string,string> variants=File.Exists(variantsPath)?Json.Deserialize<Dictionary<string,string>>(File.ReadAllText(variantsPath)):null;
                        string variantRoot;
                        if(variants==null || !variants.TryGetValue(variantName,out variantRoot) || !Directory.Exists(variantRoot)) {Reply(stream,new {Error="This UI worktree is unavailable."},404);return;}
                        assetBase="/"+variantName;assetRoot=Path.GetFullPath(variantRoot);assetPath=path.Length==assetBase.Length?"/":path.Substring(assetBase.Length);
                    }
                    string file=assetPath=="/"?"index.html":assetPath=="/app.js"?"app.js":assetPath=="/glass.js"?"glass.js":assetPath=="/scene.bundle.js"?"scene.bundle.js":assetPath=="/styles.css"?"styles.css":assetPath=="/favicon.svg"?"favicon.svg":assetPath=="/inter-latin.woff2"?"inter-latin.woff2":assetPath=="/assets/keyboard.glb"?"assets/keyboard.glb":assetPath=="/assets/laptop.glb"?"assets/laptop.glb":assetPath=="/assets/mouse.glb"?"assets/mouse.glb":assetPath=="/assets/basilisk.glb"?"assets/basilisk.glb":assetPath=="/assets/legion.glb"?"assets/legion.glb":assetPath=="/assets/aorus.glb"?"assets/aorus.glb":assetPath=="/assets/pad.glb"?"assets/pad.glb":null;
                    if(file==null) {Reply(stream,new {Error="Page not found."},404);return;}
                    string assetFile=Path.Combine(assetRoot,file);
                    if(!File.Exists(assetFile)) {Reply(stream,new {Error="Asset not found."},404);return;}
                    if(file.EndsWith(".woff2")) {Response(stream,200,"font/woff2",File.ReadAllBytes(assetFile));return;}
                    if(file.EndsWith(".glb")) {Response(stream,200,"model/gltf-binary",File.ReadAllBytes(assetFile));return;}
                    string contents=File.ReadAllText(assetFile);if(file=="index.html") contents=contents.Replace("{{SESSION_TOKEN}}",token).Replace("{{ASSET_BASE}}",assetBase);
                    Response(stream,200,file.EndsWith(".html")?"text/html; charset=utf-8":file.EndsWith(".js")?"text/javascript; charset=utf-8":file.EndsWith(".css")?"text/css; charset=utf-8":"image/svg+xml",Encoding.UTF8.GetBytes(contents));return;
                }
                if(first[0]!="POST") {Reply(stream,new {Error="Use a supported control-panel action."},403);return;}
                string lengthText;int length;if(!headers.TryGetValue("Content-Length",out lengthText) || !int.TryParse(lengthText,out length) || length<0 || length>32768 || headers.ContainsKey("Transfer-Encoding")) throw new ArgumentException("Invalid request size.");
                var body=new byte[length];int total=0;while(total<length) {int read=stream.Read(body,total,length-total);if(read==0) throw new ArgumentException("Incomplete request.");total+=read;}
                // Consume bounded request bodies before replying. Closing a Windows
                // socket with unread body bytes resets it and hides the JSON error.
                string auth,requestOrigin;
                if(!headers.TryGetValue("X-Lighting-Token",out auth) || auth!=token || (headers.TryGetValue("Origin",out requestOrigin) && requestOrigin!=Origin)) {Reply(stream,new {Error="Refresh Music Lighting to reconnect, or reopen its desktop shortcut."},403);return;}
                lock(MutationGate) {
                    object result;
                    if(path=="/api/apply" || path=="/api/save") result=ChangeSettings(Encoding.UTF8.GetString(body),path=="/api/apply");
                    else if(path=="/api/presets") result=SaveNewStyle(Encoding.UTF8.GetString(body));
                    else if(path=="/api/stop") {StopBridge();active="";lastError="";Record("Stopped music sync");result=new {Ok=true};}
                    else if(path=="/api/refresh") {devices=DeviceState();result=new {Ok=true,Devices=devices};}
                    else {Reply(stream,new {Error="Action not found."},404);return;}
                    Reply(stream,result,200);
                }
            } catch(ArgumentException error) {try {Reply(stream,new {Error=error.Message},400);}catch(IOException) {}}
            catch(Exception error) {lastError=error.Message;Record("ERROR "+error);try {Reply(stream,new {Error=error.Message},500);}catch(IOException) {}}
        }
    }
    public static int Main(string[] args) {
        if(Array.IndexOf(args,"--self-test")>=0) {
            try {
                var change=ValidateChange("{\"Style\":\"hp-2\",\"Profile\":{\"HpBrightness\":3,\"Rows\":32}}");
                if(change.Style!="hp-2" || change.Profile.HpBrightness!=3 || change.Profile.Rows!=32) throw new Exception("Control request deserialization failed.");
                bool rejected=false;try {ValidateChange("{\"Style\":\"equalizer\",\"Profile\":{\"HpBrightness\":9}}");}catch(ArgumentException) {rejected=true;}if(!rejected) throw new Exception("Invalid hardware range was accepted.");
                rejected=false;try {ValidateChange("{\"Style\":\"../settings\",\"Profile\":{}}");}catch(ArgumentException) {rejected=true;}if(!rejected) throw new Exception("Invalid style was accepted.");
                File.WriteAllText(Path.Combine(Root,"control-panel-self-test.txt"),"PASS real request deserialization, hardware range rejection and style validation. No listener, HID access or lighting changes.");return 0;
            }catch(Exception error) {File.WriteAllText(Path.Combine(Root,"control-panel-self-test.txt"),"FAIL "+error);return 1;}
        }
        bool owned;using(var mutex=new Mutex(true,"Local\\CodexMusicLightingPanel",out owned)) {
            if(!owned) return 0;
            try {
                var secret=new byte[32];using(var random=RandomNumberGenerator.Create()) random.GetBytes(secret);token=BitConverter.ToString(secret).Replace("-","").ToLowerInvariant();
                devices=DeviceState();var listener=new TcpListener(IPAddress.Loopback,Port);listener.Start();PreventSocketInheritance(listener.Server);Record("Control panel ready at "+Origin+"; no browser opened");
                while(true) {var client=listener.AcceptTcpClient();PreventSocketInheritance(client.Client);ThreadPool.QueueUserWorkItem(Client,client);}
            }catch(Exception error) {Record("Server stopped: "+error);return 1;}
            finally {mutex.ReleaseMutex();}
        }
    }
}
