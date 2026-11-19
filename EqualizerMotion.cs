using System;

// Shared spectrum gain preserves strong/weak frequency differences. Fast peaks,
// short holds and a deliberate fall make the shape legible on a six-row display.
public sealed class EqualizerMotion {
    readonly double[] rmsHistory=new double[256],times=new double[256],scratch=new double[256];
    readonly double[] shape=new double[21],levels=new double[21],holdUntil=new double[21];
    readonly int[] heights=new int[21];
    int cursor,count,updates;double previousTime=-1,low,high=.06;
    public double Sensitivity=1;
    public double HoldSeconds=.055;
    public double FallSeconds=.22;
    public int Width=0;
    public double Drive { get; private set; }
    public double[] Levels {get {return levels;} }
    public int[] Heights {get {return heights;} }
    public void Update(double[] amplitudes,double rms,double elapsed) {
        if(amplitudes==null || amplitudes.Length!=21) throw new ArgumentException("Expected 21 frequency bands");
        double dt=previousTime<0 ? .04 : Math.Max(.001,Math.Min(.3,elapsed-previousTime));previousTime=elapsed;
        rmsHistory[cursor]=rms;times[cursor]=elapsed;cursor=(cursor+1)%256;count=Math.Min(256,count+1);
        if(updates++%4==0) {
            int n=0;for(int i=0;i<count;i++) if(elapsed-times[i]<=4) scratch[n++]=rmsHistory[i];
            if(n>=12) {
                Array.Sort(scratch,0,n);double quiet=scratch[(int)((n-1)*.20)],loud=scratch[(int)((n-1)*.90)];
                if(loud-quiet<Math.Max(.0015,loud*.08)) {low=quiet*.15;high=Math.Max(.004,loud);}
                else {low=quiet;high=Math.Max(low+.003,loud);}
            }
        }
        double normalized=Math.Max(0,Math.Min(1,Sensitivity*(rms-low)/Math.Max(.003,high-low)));
        Drive=rms<.00002 || normalized<.07 ? 0 : Math.Pow(normalized,1.3);
        if(Drive>.96) Drive=1;
        double maximum=0;
        for(int i=0;i<21;i++) {
            // Modest treble compensation; no independent auto-gain per column.
            double center=40*Math.Pow(400,(i+.5)/21.0);
            double own=amplitudes[i]*Math.Pow(center/400,.16);
            double left=amplitudes[Math.Max(0,i-1)]*Math.Pow(40*Math.Pow(400,(Math.Max(0,i-1)+.5)/21.0)/400,.16);
            double right=amplitudes[Math.Min(20,i+1)]*Math.Pow(40*Math.Pow(400,(Math.Min(20,i+1)+.5)/21.0)/400,.16);
            shape[i]=.75*own+.125*(left+right);maximum=Math.Max(maximum,shape[i]);
        }
        for(int i=0;i<21;i++) {
            double relative=maximum<.00008 ? 0 : shape[i]/maximum;
            if(Width>0 && maximum>=.00008) {
                for(int offset=1;offset<=Width;offset++) {
                    double weight=offset==1?.72:.42;
                    relative=Math.Max(relative,weight*shape[Math.Max(0,i-offset)]/maximum);
                    relative=Math.Max(relative,weight*shape[Math.Min(20,i+offset)]/maximum);
                }
            }
            double profile=Math.Pow(Math.Max(0,(relative-.14)/.86),1.05);
            double target=profile*Drive;
            if(target>.98) target=1;
            if(target>=levels[i]) {levels[i]=target;holdUntil[i]=elapsed+HoldSeconds;}
            else if(elapsed>=holdUntil[i]) levels[i]=Math.Max(target,levels[i]-dt/FallSeconds);
            if(levels[i]<.015) levels[i]=0;
            double scaled=6*levels[i];
            // Hysteresis prevents repeatedly toggling a row at a rounding boundary.
            if(scaled>heights[i]+.65) heights[i]=Math.Min(6,(int)Math.Floor(scaled+.35));
            else if(scaled<heights[i]-.65) heights[i]=Math.Max(0,(int)Math.Floor(scaled+.65));
            if(levels[i]==0) heights[i]=0;
        }
    }
    public byte[] HpFrame(byte profile,bool bottomSupport) {return HpLightingPacket.Bars(profile,heights,bottomSupport);}
}
