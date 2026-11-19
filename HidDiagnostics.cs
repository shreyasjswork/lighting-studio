using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public static class HidDiagnostics
{
    [StructLayout(LayoutKind.Sequential)]
    struct InterfaceData { public int Size; public Guid ClassGuid; public int Flags; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential)]
    struct Attributes { public int Size; public ushort Vendor; public ushort Product; public ushort Version; }
    [StructLayout(LayoutKind.Sequential)]
    public struct Caps {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] Reserved;
        public ushort LinkNodes, InputButtons, InputValues, InputData, OutputButtons, OutputValues, OutputData, FeatureButtons, FeatureValues, FeatureData;
    }
    public class Device {
        public string Path, Product, Manufacturer, VendorId, ProductId, Version, Usage, UsagePage;
        public int InputLength, OutputLength, FeatureLength;
        public string[] OutputCaps, FeatureCaps;
    }
    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, uint size, out uint required, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll")] static extern bool HidD_GetManufacturerString(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out Caps caps);
    [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, IntPtr caps, ref ushort length, IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetButtonCaps(int type, IntPtr caps, ref ushort length, IntPtr data);
    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError=true)] public static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int length);

    static string[] Describe(IntPtr data, int type, ushort valueCount, ushort buttonCount) {
        var items = new List<string>();
        for (int kind=0; kind<2; kind++) {
            ushort count = kind == 0 ? valueCount : buttonCount;
            if (count == 0) continue;
            // HIDP_VALUE_CAPS and HIDP_BUTTON_CAPS are both 72 bytes in the Windows SDK.
            IntPtr buffer=Marshal.AllocHGlobal(72*count);
            try {
                int status=kind==0 ? HidP_GetValueCaps(type,buffer,ref count,data) : HidP_GetButtonCaps(type,buffer,ref count,data);
                if (status!=0x00110000) { items.Add("caps status="+status.ToString("X8")); continue; }
                for(int i=0;i<count;i++) {
                    IntPtr p=IntPtr.Add(buffer,72*i);
                    ushort page=(ushort)Marshal.ReadInt16(p,0);
                    byte report=Marshal.ReadByte(p,2);
                    bool range=Marshal.ReadByte(p,12)!=0;
                    ushort umin=(ushort)Marshal.ReadInt16(p,56);
                    ushort umax=range ? (ushort)Marshal.ReadInt16(p,58) : umin;
                    string detail=kind==0 ? ", bits="+(ushort)Marshal.ReadInt16(p,18)+", count="+(ushort)Marshal.ReadInt16(p,20)+", logical="+Marshal.ReadInt32(p,40)+".."+Marshal.ReadInt32(p,44) : "";
                    items.Add((kind==0 ? "value" : "button")+", report="+report+", page=0x"+page.ToString("X4")+", usage=0x"+umin.ToString("X4")+"..0x"+umax.ToString("X4")+detail);
                }
            } finally { Marshal.FreeHGlobal(buffer); }
        }
        return items.ToArray();
    }
    public static Device[] Enumerate() {
        var devices=new List<Device>(); Guid guid; HidD_GetHidGuid(out guid);
        IntPtr set=SetupDiGetClassDevs(ref guid,IntPtr.Zero,IntPtr.Zero,0x12);
        if(set==new IntPtr(-1)) throw new Exception("HID enumeration failed");
        try {
            for(uint i=0;;i++) {
                var info=new InterfaceData(); info.Size=Marshal.SizeOf(typeof(InterfaceData));
                if(!SetupDiEnumDeviceInterfaces(set,IntPtr.Zero,ref guid,i,ref info)) break;
                uint needed; SetupDiGetDeviceInterfaceDetail(set,ref info,IntPtr.Zero,0,out needed,IntPtr.Zero);
                IntPtr detail=Marshal.AllocHGlobal((int)needed);
                string path;
                try {
                    Marshal.WriteInt32(detail,IntPtr.Size==8 ? 8 : 6);
                    if(!SetupDiGetDeviceInterfaceDetail(set,ref info,detail,needed,out needed,IntPtr.Zero)) continue;
                    path=Marshal.PtrToStringUni(IntPtr.Add(detail,4));
                } finally { Marshal.FreeHGlobal(detail); }
                // Metadata access only: no input reports are read and no commands are written.
                if(!path.Contains("vid_258a") && !path.Contains("vid_048d") && !path.Contains("vid_1532")) continue;
                using(var h=CreateFile(path,0,3,IntPtr.Zero,3,0,IntPtr.Zero)) {
                    if(h.IsInvalid) { devices.Add(new Device { Path=path, Product="metadata access failed: "+Marshal.GetLastWin32Error() }); continue; }
                    var a=new Attributes(); a.Size=Marshal.SizeOf(typeof(Attributes)); HidD_GetAttributes(h,ref a);
                    byte[] product=new byte[512],manufacturer=new byte[512];
                    HidD_GetProductString(h,product,product.Length); HidD_GetManufacturerString(h,manufacturer,manufacturer.Length);
                    var d=new Device { Path=path, Product=Encoding.Unicode.GetString(product).TrimEnd('\0'), Manufacturer=Encoding.Unicode.GetString(manufacturer).TrimEnd('\0'), VendorId=a.Vendor.ToString("X4"), ProductId=a.Product.ToString("X4"), Version=a.Version.ToString("X4") };
                    IntPtr pp;
                    if(HidD_GetPreparsedData(h,out pp)) {
                        try {
                            Caps c; if(HidP_GetCaps(pp,out c)==0x00110000) {
                                d.Usage=c.Usage.ToString("X4"); d.UsagePage=c.UsagePage.ToString("X4");
                                d.InputLength=c.InputLength; d.OutputLength=c.OutputLength; d.FeatureLength=c.FeatureLength;
                                d.OutputCaps=Describe(pp,1,c.OutputValues,c.OutputButtons);
                                d.FeatureCaps=Describe(pp,2,c.FeatureValues,c.FeatureButtons);
                            }
                        } finally { HidD_FreePreparsedData(pp); }
                    }
                    devices.Add(d);
                }
            }
        } finally { SetupDiDestroyDeviceInfoList(set); }
        return devices.ToArray();
    }
}
