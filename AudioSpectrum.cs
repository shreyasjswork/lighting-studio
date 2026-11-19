using System;

// Hann-windowed stereo FFT. Samples exist only in memory and are never saved.
public sealed class AudioSpectrum {
    public const int Size=4096, Bands=21;
    readonly double[] left=new double[Size],right=new double[Size],window=new double[Size];
    readonly double[] re=new double[Size],im=new double[Size],power=new double[Size/2+1];
    readonly double[] reference=new double[Bands],levels=new double[Bands],amplitudes=new double[Bands];
    readonly int sampleRate; int head,count; double lastTime=-1;
    public double Rms { get; private set; }
    public double[] Levels { get { return levels; } }
    public double[] Amplitudes { get { return amplitudes; } }
    public AudioSpectrum(int rate) {
        if(rate<8000 || rate>384000) throw new ArgumentOutOfRangeException("rate");
        sampleRate=rate;
        for(int i=0;i<Size;i++) window[i]=.5-.5*Math.Cos(2*Math.PI*i/(Size-1));
        for(int i=0;i<Bands;i++) reference[i]=.025;
    }
    public void Push(double l,double r) {
        left[head]=double.IsNaN(l)||double.IsInfinity(l)?0:Math.Max(-1,Math.Min(1,l));
        right[head]=double.IsNaN(r)||double.IsInfinity(r)?0:Math.Max(-1,Math.Min(1,r));
        head=(head+1)%Size;count=Math.Min(Size,count+1);
    }
    public void Reset() { Array.Clear(left,0,Size);Array.Clear(right,0,Size);head=count=0; }
    public double[] Update(double elapsed,bool fresh) {
        double dt=lastTime<0 ? .033 : Math.Max(.001,Math.Min(.3,elapsed-lastTime));lastTime=elapsed;
        Array.Clear(power,0,power.Length);
        double energy=0;
        if(fresh && count>0) {
            for(int channel=0;channel<2;channel++) {
                var samples=channel==0 ? left : right;double mean=0;
                for(int i=0;i<Size;i++) mean+=samples[i];mean/=Size;
                for(int i=0;i<Size;i++) { double value=samples[(head+i)%Size]-mean;energy+=value*value;re[i]=value*window[i];im[i]=0; }
                Transform(re,im);
                for(int i=0;i<power.Length;i++) power[i]+=.5*(re[i]*re[i]+im[i]*im[i]);
            }
        }
        Rms=Math.Sqrt(energy/(2*Size));
        double maxFrequency=Math.Min(16000,sampleRate*.45),minFrequency=40;
        for(int band=0;band<Bands;band++) {
            double low=minFrequency*Math.Pow(maxFrequency/minFrequency,(double)band/Bands);
            double high=minFrequency*Math.Pow(maxFrequency/minFrequency,(double)(band+1)/Bands);
            // Fractional-bin weights avoid empty or duplicated low-frequency bands.
            double sum=0,lowBin=low*Size/sampleRate,highBin=high*Size/sampleRate;
            for(int bin=Math.Max(1,(int)Math.Floor(lowBin));bin<=Math.Min(Size/2,(int)Math.Ceiling(highBin));bin++) {
                double overlap=Math.Max(0,Math.Min(highBin,bin+.5)-Math.Max(lowBin,bin-.5));
                sum+=power[bin]*overlap;
            }
            double amplitude=2*Math.Sqrt(sum)/(Size*.5);
            amplitudes[band]=amplitude;
            reference[band]=Math.Max(.012,Math.Max(amplitude,reference[band]*Math.Exp(-dt/4)));
            double target=Rms<.00002 || amplitude<.00008 ? 0 : Math.Pow(Math.Min(1,amplitude/reference[band]),.8);
            double tau=target>levels[band] ? .018 : .12;
            levels[band]+= (target-levels[band])*(1-Math.Exp(-dt/tau));
            if(levels[band]<.008) levels[band]=0;
        }
        return levels;
    }
    static void Transform(double[] real,double[] imaginary) {
        int n=real.Length;
        for(int i=1,j=0;i<n;i++) { int bit=n>>1;while((j&bit)!=0) { j^=bit;bit>>=1; }j^=bit;if(i<j) { double t=real[i];real[i]=real[j];real[j]=t;t=imaginary[i];imaginary[i]=imaginary[j];imaginary[j]=t; } }
        for(int len=2;len<=n;len<<=1) {
            double angle=-2*Math.PI/len,wrStep=Math.Cos(angle),wiStep=Math.Sin(angle);
            for(int start=0;start<n;start+=len) {
                double wr=1,wi=0;
                for(int j=0;j<len/2;j++) {
                    int a=start+j,b=a+len/2;double br=real[b]*wr-imaginary[b]*wi,bi=real[b]*wi+imaginary[b]*wr;
                    real[b]=real[a]-br;imaginary[b]=imaginary[a]-bi;real[a]+=br;imaginary[a]+=bi;
                    double next=wr*wrStep-wi*wiStep;wi=wr*wiStep+wi*wrStep;wr=next;
                }
            }
        }
    }
}
