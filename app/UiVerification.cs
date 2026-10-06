using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace NoMoreBacknoise;
// Deterministic visual checks, with no microphone, first-launch dialog or saved settings writes.
internal static class UiVerification {
    public static void Run(string directory) {
        Directory.CreateDirectory(directory);
        foreach(var locale in Localization.Languages.Keys) {
            Localization.Load(locale);
            var window=new MainWindow { FlowDirection=locale=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight };
            var content=(FrameworkElement)window.Content;
            foreach(var scale in new[] {1d,1.25,1.5,2d}) {
                content.Measure(new Size(1120,790));content.Arrange(new Rect(0,0,1120,790));content.UpdateLayout();
                var image=new RenderTargetBitmap((int)(1120*scale),(int)(790*scale),96*scale,96*scale,PixelFormats.Pbgra32);image.Render(content);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));
                using var file=File.Create(Path.Combine(directory,$"ui-{locale}-{scale:0.00}.png"));encoder.Save(file);
            }
            var settings=new ProcessingSettings();settings.Validate();
            if(settings.Strength!=75 || settings.GainDb!=0 || settings.Engine!="auto") throw new Exception("Unsafe processing defaults");
        }
        File.WriteAllText(Path.Combine(directory,"ui-validation.txt"),"Rendered nine locales at 100, 125, 150 and 200 percent. Arabic uses RightToLeft. Human language review remains required.\n");
    }
}
