using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
namespace NoMoreBacknoise;
public sealed class SignalView : FrameworkElement {
    private readonly List<float> _raw = new(); private readonly List<float> _clean = new();
    public bool Spectrum { get; set; }
    public void SetSamples(float[] raw, float[] clean) {
        if (Spectrum) { _raw.Clear(); _clean.Clear(); }
        _raw.AddRange(raw); _clean.AddRange(clean);
        if (_raw.Count > 2400) _raw.RemoveRange(0,_raw.Count-2400); if (_clean.Count>2400) _clean.RemoveRange(0,_clean.Count-2400);
        InvalidateVisual();
    }
    public void Clear() { _raw.Clear(); _clean.Clear(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc) {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(15,20,27)),null,new Rect(0,0,ActualWidth,ActualHeight));
        var grid = new Pen(new SolidColorBrush(Color.FromRgb(36,47,61)),1);
        for (int i=1;i<8;i++) dc.DrawLine(grid,new Point(ActualWidth*i/8,0),new Point(ActualWidth*i/8,ActualHeight));
        for (int i=1;i<4;i++) dc.DrawLine(grid,new Point(0,ActualHeight*i/4),new Point(ActualWidth,ActualHeight*i/4));
        if (_raw.Count == 0) return;
        Draw(dc,_raw,Color.FromRgb(243,183,91),1); Draw(dc,_clean,Color.FromRgb(114,242,195),1.5);
    }
    private void Draw(DrawingContext dc,List<float> samples,Color color,double width) {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open()) {
            for(int i=0;i<samples.Count;i++) {
                double x=ActualWidth*i/Math.Max(samples.Count-1,1);
                double y=Spectrum ? ActualHeight*(1-Math.Clamp((samples[i]+100)/100,0,1)) : ActualHeight*.5-samples[i]*ActualHeight*.47;
                if(i==0) context.BeginFigure(new Point(x,y),false,false); else context.LineTo(new Point(x,y),true,false);
            }
        }
        geometry.Freeze(); dc.DrawGeometry(null,new Pen(new SolidColorBrush(color),width),geometry);
    }
}
