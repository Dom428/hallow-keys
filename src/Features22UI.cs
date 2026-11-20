// Hallow Keys 2.2 - "Every browser": the Browser page's table of browsers (installed? connected? where to load the extension).
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HallowKeys
{
    public partial class MainWindow
    {
        UIElement BrowserTable()
        {
            var box = new StackPanel();
            box.Children.Add(new TextBlock { Text = "Your browsers", FontWeight = FontWeights.SemiBold, FontSize = 14 });
            box.Children.Add(new TextBlock { Text = "Chrome, Edge, Brave, Chromium, Yandex and Firefox each look for Hallow Keys in their own place; Opera, Opera GX, Vivaldi and Arc use Chrome's. Connect browsers sets it up for every browser installed here - nothing inside the browsers is changed.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) });
            var exe = Snap.On ? @"C:\Users\you\AppData\Local\Programs\Hallow Keys\HallowKeys.exe" : System.Reflection.Assembly.GetExecutingAssembly().Location;
            var states = Snap.On ? SnapBrowserStates() : Browsers.States(exe);
            var g = new Grid();
            foreach (var w in new[] { 150.0, 0, 210, 150 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
            Action<int, int, UIElement> put = (r, c, e) => { Grid.SetRow(e, r); Grid.SetColumn(e, c); g.Children.Add(e); };
            g.RowDefinitions.Add(new RowDefinition());
            int col = 0; foreach (var h in new[] { "Browser", "Installed", "Link to Hallow Keys", "Extensions page" }) put(0, col++, new TextBlock { Text = h, Foreground = Muted, FontSize = 11.5, Margin = new Thickness(0, 0, 8, 6) });
            int row = 1;
            foreach (var s in states.OrderByDescending(x => x.Installed).ThenBy(x => Browsers.All.IndexOf(x.Info)))
            {
                g.RowDefinitions.Add(new RowDefinition());
                var dim = s.Installed ? (Brush)FindResource("Text") : Muted;
                put(row, 0, new TextBlock { Text = s.Info.Name, FontWeight = s.Installed ? FontWeights.SemiBold : FontWeights.Normal, Foreground = dim, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center });
                put(row, 1, new TextBlock { Text = s.Installed ? "Yes" : "Not found on this PC", Foreground = dim, ToolTip = s.Where.Length > 0 ? s.Where : null, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center });
                string link; Brush lc;
                if (s.Connected && s.PointsHere) { link = "Connected" + (s.Info.ReadsFrom != null ? " (via Chrome's)" : ""); lc = B("#6ED28C"); }
                else if (s.Connected) { link = "Points to another copy - Connect again"; lc = B("#F0BE5A"); }
                else if (!s.Installed) { link = "-"; lc = Muted; }
                else { link = "Not connected - click Connect browsers"; lc = B("#F0BE5A"); }
                put(row, 2, new TextBlock { Text = link, Foreground = lc, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 8, 4), VerticalAlignment = VerticalAlignment.Center });
                put(row, 3, new TextBlock { Text = s.Info.ExtPage, Foreground = Muted, FontFamily = new FontFamily("Consolas"), FontSize = 11.5, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center });
                row++;
            }
            box.Children.Add(g);
            var others = Snap.On ? new System.Collections.Generic.List<string>() : Browsers.Others(Browsers.StartMenu());
            if (others.Count > 0) box.Children.Add(new TextBlock { Text = "Also installed: " + string.Join(", ", others) + ". Most Chromium-based browsers use Chrome's link, so they usually work too - load the extension the same way.", Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) });
            return Card(box);
        }
        System.Collections.Generic.List<BrowserState> SnapBrowserStates()   // --snap: a made-up PC
        {
            Func<string, bool, bool, BrowserState> mk = (id, inst, con) => new BrowserState { Info = Browsers.Get(id), Installed = inst, Connected = con, PointsHere = con, Where = inst ? "(made up)" : "" };
            return new System.Collections.Generic.List<BrowserState> { mk("chrome", true, true), mk("edge", true, true), mk("brave", true, true), mk("opera", false, false), mk("operagx", true, true), mk("vivaldi", true, true),
                mk("arc", false, false), mk("chromium", false, false), mk("yandex", false, false), mk("firefox", true, false) };
        }
    }
}
