using System;

public static class LegionLightingPacket {
    // C985 exposes native brightness 1 (low) and 2 (high). Zero is implemented
    // as a static black frame, rather than an unsupported brightness byte.
    public static void SetBrightness(byte[] frame,int brightness) {
        if(frame==null || frame.Length!=33 || frame[0]!=0xCC || frame[1]!=0x16) throw new ArgumentException("Expected a Legion four-zone lighting frame.");
        if(brightness<0 || brightness>2) throw new ArgumentException("Unsupported Legion brightness.");
        frame[4]=(byte)(brightness==0?2:brightness);
        if(brightness==0) {frame[2]=1;Array.Clear(frame,5,12);frame[18]=0;frame[19]=0;}
    }
    public static byte NativeEffect(int effect) {return effect==2?(byte)3:effect==3?(byte)6:(byte)effect;}
}
