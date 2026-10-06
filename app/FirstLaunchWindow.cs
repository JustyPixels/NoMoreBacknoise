using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
namespace NoMoreBacknoise;
public sealed class FirstLaunchWindow : Window {
    public FirstLaunchWindow(UserSettings settings) {
        Style=(Style)Application.Current.FindResource(typeof(Window));
        Title="NoMoreBacknoise++"; Width=530; Height=570; ResizeMode=ResizeMode.NoResize; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new StackPanel { Margin=new Thickness(28) }; Content=panel;
        var welcome=new TextBlock { FontSize=25,FontWeight=FontWeights.Bold }; welcome.SetResourceReference(TextBlock.TextProperty,"t.welcome"); panel.Children.Add(welcome);
        var hint=new TextBlock { Margin=new Thickness(0,12,0,16),TextWrapping=TextWrapping.Wrap }; hint.SetResourceReference(TextBlock.TextProperty,"t.firstLaunchHint"); panel.Children.Add(hint);
        var languages=new ComboBox { ItemsSource=Localization.Languages,DisplayMemberPath="Value",SelectedValuePath="Key",SelectedValue=settings.Language };
        languages.SelectionChanged+=(_,_)=> { if(languages.SelectedValue is string locale) { Localization.Load(locale); FlowDirection=locale=="ar"?FlowDirection.RightToLeft:FlowDirection.LeftToRight; } }; panel.Children.Add(languages);
        CheckBox Option(string key,bool value) { var check=new CheckBox { IsChecked=value,Margin=new Thickness(0,10,0,10) }; check.SetResourceReference(CheckBox.ContentProperty,"t."+key); panel.Children.Add(check); return check; }
        var startup=Option("windowsStartup",settings.WindowsStartup); var automatic=Option("autoProcess",settings.AutoProcess); var tray=Option("closeToTray",settings.CloseToTray); var updates=Option("updateChecks",settings.UpdateChecks);
        var privacy=new TextBlock { Margin=new Thickness(0,16,0,16),TextWrapping=TextWrapping.Wrap }; privacy.SetResourceReference(TextBlock.TextProperty,"t.localOnly"); panel.Children.Add(privacy);
        var button=new Button { Padding=new Thickness(16,12,16,12) }; button.SetResourceReference(Button.ContentProperty,"t.finishSetup");
        button.Click+=(_,_)=> { settings.Language=Localization.Current; settings.WindowsStartup=startup.IsChecked==true; settings.AutoProcess=automatic.IsChecked==true; settings.CloseToTray=tray.IsChecked==true; settings.UpdateChecks=updates.IsChecked==true; settings.FirstLaunchComplete=true; DialogResult=true; }; panel.Children.Add(button);
    }
}
