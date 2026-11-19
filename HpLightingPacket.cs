using System;

// Confirmed physically on this GK320: the profile header and 7/0 key values
// hold a selected row steadily after ONE write. Byte 1 is not a key position.
public static class HpLightingPacket {
    public static byte[] Bars(byte profile,int[] heights,bool bottomSupport) {
        if(profile<1 || profile>15 || heights==null || heights.Length!=21) throw new ArgumentException("Invalid HP profile or bar layout.");
        var frame=new byte[127];frame[0]=11;frame[1]=profile;
        bool active=false;foreach(int height in heights) {if(height<0 || height>6) throw new ArgumentException("Invalid bar height.");if(height>0) active=true;}
        for(int slot=0;slot<125;slot++) {int column=slot/6,row=slot%6;if(row>=6-heights[column] || (bottomSupport && active && row==5)) frame[2+slot]=7;}
        return frame;
    }
    public static byte[] Rows(byte profile,int mask) {
        if(profile<1 || profile>15 || mask<0 || mask>63) throw new ArgumentException("Invalid HP row selection.");
        var frame=new byte[127];frame[0]=11;frame[1]=profile;
        for(int slot=0;slot<125;slot++) if((mask & (1<<(slot%6)))!=0) frame[2+slot]=7;
        return frame;
    }
    public static bool Same(byte[] a,byte[] b) {if(a==null || b==null || a.Length!=b.Length) return false;for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false;return true;}
}
