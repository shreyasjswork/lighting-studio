using System;

// Playback-level transients are a beat-activity heuristic, not a BPM estimator.
// No audio samples, microphone access or input automation are involved.
public sealed class AudioLightingResponse {
    readonly double[] peaks=new double[128], times=new double[128], scratch=new double[128];
    readonly double[] intervals=new double[8];
    int cursor,count,intervalCursor,intervalCount,calibrationFrames;
    double lower,upper=.35,previousPeak,previousTarget,previousTime=-1;
    double previousOnset=-10,brightness,hue=.52,activity;
    public double Brightness { get { return brightness; } }
    public double Hue { get { return hue; } }
    public double Activity { get { return activity; } }
    public int Onsets { get; private set; }
    public bool Onset { get; private set; }

    public void Update(double peak,double elapsed) {
        peak=Math.Max(0,Math.Min(1,peak));
        double dt=previousTime<0 ? .03 : Math.Max(.001,Math.Min(.25,elapsed-previousTime));
        peaks[cursor]=peak;times[cursor]=elapsed;cursor=(cursor+1)%peaks.Length;
        count=Math.Min(peaks.Length,count+1);
        if(calibrationFrames++%4==0) {
            int n=0;
            for(int i=0;i<count;i++) if(elapsed-times[i]<=2.0) scratch[n++]=peaks[i];
            if(n>=12) {
                Array.Sort(scratch,0,n);
                double low=scratch[(int)((n-1)*.20)], high=scratch[(int)((n-1)*.88)];
                // A sustained note should stay visible, not disappear because its
                // rolling minimum and maximum happen to be the same.
                if(high-low<Math.Max(.008,high*.025)) { lower=low*.15;upper=Math.Max(.025,high); }
                else { lower=low;upper=Math.Max(low+.015,high); }
            }
        }
        double range=Math.Max(.015,upper-lower);
        double target=Math.Max(0,Math.Min(1,(peak-lower)/range));
        double rise=peak-previousPeak;
        Onset=peak>.008 && elapsed-previousOnset>=.10 &&
            rise>Math.Max(.008,range*.12) && target>=.55 &&
            (target-previousTarget>.14 || (target>=.94 && previousTarget<.72));
        if(Onset) {
            double interval=elapsed-previousOnset;
            if(interval>=.10 && interval<=1.5) {
                intervals[intervalCursor]=interval;intervalCursor=(intervalCursor+1)%intervals.Length;
                intervalCount=Math.Min(intervals.Length,intervalCount+1);
                double total=0;for(int i=0;i<intervalCount;i++) total+=intervals[i];
                activity=intervalCount/total;
            }
            previousOnset=elapsed;Onsets++;
            // Each detected hit moves to a visibly different hue. More hits per
            // second therefore produce more colour changes per second.
            hue=(hue+.38196601125)%1;
            brightness=1;
        } else {
            target=Math.Pow(target,1.6);
            if(peak<.003 || target<.075) brightness=0;
            else if(target>=.96) brightness=1;
            else brightness=target>=brightness ? target : Math.Max(target,brightness*Math.Exp(-dt/.045));
        }
        if(elapsed-previousOnset>1.5) activity*=Math.Exp(-dt/1.5);
        hue=(hue+dt*(.025+.045*Math.Min(6,activity)))%1;
        previousTime=elapsed;previousPeak=peak;previousTarget=Math.Max(0,Math.Min(1,(peak-lower)/range));
    }
}
