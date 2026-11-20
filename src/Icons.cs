// Hallow Keys - Font Awesome icons (path data in Icons.g.cs). Buttons get their icon from their label,
// so a label that changes ("Mute" -> "Muted") changes its icon too.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;

namespace HallowKeys
{
    public static partial class Icons
    {
        static readonly Dictionary<string, Geometry> cache = new Dictionary<string, Geometry>();

        // exact label -> icon
        static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "Unlock", "lock-open" }, { "Lock", "lock" }, { "Lock now", "lock" }, { "Windows Hello", "fingerprint" }, { "Create vault", "key" }, { "New item", "plus" }, { "Save", "floppy-disk" }, { "Delete", "trash-can" },
            { "Edit", "pen" }, { "Copy", "copy" }, { "Show", "eye" }, { "Hide", "eye-slash" }, { "Generate", "dice" }, { "Use this", "check" }, { "Import", "file-import" }, { "Export backup", "file-export" },
            { "Restore backup", "file-import" }, { "Print", "print" }, { "Add file", "paperclip" }, { "Cancel", "xmark" }, { "OK", "check" }, { "x", "xmark" }, { "Check for updates", "arrows-rotate" },
            { "Change master password", "key" }, { "Recovery sheet", "print" }, { "Open", "globe" }, { "Check for leaked passwords", "shield-halved" }, { "Connect browsers", "puzzle-piece" }, { "Disconnect browsers", "xmark" }, { "Copy folder path", "copy" },
            { "Use recovery code", "key" }, { "Save file", "download" }, { "Remove", "trash-can" }, { "Copy code", "copy" },
            { "Encrypted export", "file-export" }, { "Plain CSV export", "file-csv" }, { "Restore", "trash-can-arrow-up" }, { "Delete forever", "trash-can" }, { "Empty trash", "trash-can" }, { "Move to trash", "trash-can" },
            { "Show item", "eye" }, { "Keep newest, trash the rest", "clone" }, { "Clear history", "xmark" },
            { "Duplicate", "clone" }, { "Save as template", "layer-group" }, { "Add field", "plus" }, { "Add website", "link" }, { "\u2191", "arrow-up" }, { "\u2193", "arrow-down" },
            { "Change on site", "globe" }, { "New password", "dice" }, { "Add 2FA", "shield-halved" }, { "Use https", "lock" }, { "Download the lists", "download" },
            { "Open Expiring", "hourglass-half" }, { "Rename", "pen" }, { "Delete template", "trash-can" }, { "Clear field history", "xmark" },
        };
        static readonly string[][] Prefixes = { new[] { "Update to", "download" }, new[] { "Show password history", "clock-rotate-left" }, new[] { "Hide password history", "clock-rotate-left" },
            new[] { "Show field history", "clock-rotate-left" }, new[] { "Hide field history", "clock-rotate-left" }, new[] { "Generate RSA", "key" } };
        public static string ForLabel(string label)
        {
            if (string.IsNullOrEmpty(label)) return null;
            string n; if (Labels.TryGetValue(label.Trim(), out n)) return n;
            foreach (var p in Prefixes) if (label.StartsWith(p[0], StringComparison.OrdinalIgnoreCase)) return p[1];
            return null;
        }

        public static Geometry Get(string name)
        {
            if (name == null) return null;
            Geometry g;
            if (!cache.TryGetValue(name, out g))
            {
                string d; if (!Data.TryGetValue(name, out d)) return null;
                g = Geometry.Parse(d); g.Freeze(); cache[name] = g;
            }
            return g;
        }

        // A standalone icon (strip headers, the header bar).
        public static FrameworkElement Make(string name, double size, Brush fill)
        {
            return new Path { Data = Get(name), Fill = fill, Stretch = Stretch.Uniform, Width = size, Height = size, SnapsToDevicePixels = true };
        }
        // Icon + text for places that aren't styled buttons.
        public static FrameworkElement WithText(string name, string text, double size, Brush fill)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var ic = Make(name, size, fill); ic.Margin = new Thickness(0, 0, 7, 0); ic.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(ic); sp.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
            return sp;
        }
    }

    // Used by the button styles in Theme.xaml.
    public class IconConv : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) { return Icons.Get(Icons.ForLabel(v as string)); }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
    public class IconVis : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) { return Icons.ForLabel(v as string) != null ? Visibility.Visible : Visibility.Collapsed; }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
    // Space between icon and text (none when the text is hidden).
    public class IconGap : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var s = v as string;
            return s != null && s.Length > 1 ? new Thickness(0, 0, 7, 0) : new Thickness(0);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
    // Hides the text of one-character labels that have an icon ("+", "x").
    public class LabelVis : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            var s = v as string;
            return s != null && s.Length == 1 && Icons.ForLabel(s) != null ? Visibility.Collapsed : Visibility.Visible;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c) { throw new NotSupportedException(); }
    }
}
