// Hallow Keys v2.9 - light and dark mode: Keys' dark colours and their light ones (the resource keys stay the same; only values swap).
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace HallowKeys
{
    public static class KeysLook
    {
        // Theme.xaml (dark -> light)
        public static readonly Dictionary<string, string> Xaml = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "#16121C", Look.LBg },      // Bg
            { "#211B2A", Look.LPanel },   // Panel
            { "#2E2639", Look.LPanel2 },  // PanelHi
            { "#3A3248", Look.LBorder },  // Line, scrollbar thumb, menu highlight
            { "#ECE8F4", Look.LText },    // Text
            { "#8F88A0", Look.LMuted },   // Muted
            { "#2A2333", Look.LPanel2 },  // buttons, hover
            { "#241E2E", Look.LPanel },   // menus, tooltips, popups
            { "#1A1522", Look.LPanel },   // text fields
            { "#55496A", "#B9AECB" },     // button border on hover
            { "#58566A", "#B8AFC8" },     // switch track (off), scrollbar hover
            { "#CDCAD6", "#FFFFFF" },     // switch knob (off)
            { "#3A2F4A", "#E2D9F0" },     // selected nav item
            { "#5A5068", "#A69DB5" },     // tick box border
            { "#6ED28C", Look.LGreen },   // switch on (a fill)
            { "#A98BE0", Look.LLilac },   // lilac
        };

        // colours set in code: status text has to read as text, so green and amber are darkened to 4.5:1 (the spec's values are under it)
        public static readonly Dictionary<string, string> Code = MakeCode();
        static Dictionary<string, string> MakeCode()
        {
            var d = new Dictionary<string, string>(Xaml, StringComparer.OrdinalIgnoreCase);
            d["#6ED28C"] = Look.TextAccent(Look.LGreen);    // good / on / synced
            d["#F0BE5A"] = Look.TextAccent(Look.LAmber);    // warnings
            d["#E0A030"] = Look.LAmber;
            d["#FF6B6B"] = Look.LRed;                       // bad
            d["#A6D96A"] = "#467310";                       // score 75-89
            d["#3A2E14"] = "#FDF6E8";                       // Travel Mode box
            d["#8A6A20"] = "#D9B76A";
            return d;
        }

        // Theme.xaml for this accent and mode. Light: accent caret and focus lines darkened for contrast, hover tint dark instead of
        // white, then the swap; filled buttons keep the real accent (with Accent.OnAccent text).
        public static string Build(string xaml, string accent, bool light)
        {
            if (light)
            {
                var ta = Look.TextAccent(accent);
                xaml = Regex.Replace(xaml, "(CaretBrush\" Value=\")#F07820", "${1}" + ta);
                xaml = Regex.Replace(xaml, "(Property=\"BorderBrush\" Value=\")#F07820", "${1}" + ta);
                xaml = xaml.Replace("x:Name=\"hl\" Background=\"#FFFFFF\"", "x:Name=\"hl\" Background=\"#1E1A26\"");
                xaml = Look.Swap(xaml, Xaml);
            }
            return Accent.ApplyTo(xaml, accent);
        }
    }
}

namespace HallowKeys
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Controls.Primitives;

    public partial class MainWindow
    {
        // Settings: "Look: Follow Hallow / Light / Dark" (saved in Keys' own settings; Follow Hallow = Hallow Toolkit's choice)
        UIElement LookRow()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 4) };
            row.Children.Add(new TextBlock { Text = "Look", VerticalAlignment = VerticalAlignment.Center, Width = 50 });
            var cur = Look.Normalize(S.Look);
            foreach (var o in new[] { new[] { Look.Follow, "Follow Hallow" }, new[] { Look.Light, "Light" }, new[] { Look.Dark, "Dark" } })
            {
                var key = o[0];
                var b = new ToggleButton { Content = o[1], Style = St("Chip"), IsChecked = key == cur, Margin = new Thickness(0, 0, 6, 0), Padding = new Thickness(12, 4, 12, 4) };
                b.Click += (s, e) => { S.Look = key; S.Save(); ApplyAccent(); if (view == "settings" && !Snap.On) ShowMain(); };
                row.Children.Add(b);
            }
            return row;
        }
        // --snap: the Look setting on its own
        public void SnapLook(Action<MainWindow, string> save)
        {
            var sp = new StackPanel { Margin = new Thickness(24) }; var ap = new StackPanel();
            ap.Children.Add(new TextBlock { Text = "App", FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 6) });
            ap.Children.Add(Switch("Use the Hallow accent (the colour picked in Hallow Toolkit)", true, v => { }));
            ap.Children.Add(LookRow()); sp.Children.Add(Card(ap)); root.Child = sp; save(this, "look.png");
        }
    }
}
