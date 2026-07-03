using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameTracker.Models;

namespace GameTracker.Views
{
    /// <summary>
    /// Attached property that renders a chat row (username + message text + inline emote
    /// images) into a TextBlock's Inlines. Emote images are downloaded and cached by WPF.
    /// </summary>
    public static class ChatInlines
    {
        public static readonly DependencyProperty RowProperty =
            DependencyProperty.RegisterAttached("Row", typeof(ChatWindow.ChatRow), typeof(ChatInlines),
                new PropertyMetadata(null, OnRowChanged));

        public static void SetRow(DependencyObject o, ChatWindow.ChatRow v) => o.SetValue(RowProperty, v);
        public static ChatWindow.ChatRow? GetRow(DependencyObject o) => (ChatWindow.ChatRow?)o.GetValue(RowProperty);

        // Boxed mode: header (symbol + badges + name, dark-on-color) and body (message only).
        public static readonly DependencyProperty HeaderRowProperty =
            DependencyProperty.RegisterAttached("HeaderRow", typeof(ChatWindow.ChatRow), typeof(ChatInlines),
                new PropertyMetadata(null, OnHeaderChanged));
        public static void SetHeaderRow(DependencyObject o, ChatWindow.ChatRow v) => o.SetValue(HeaderRowProperty, v);
        public static ChatWindow.ChatRow? GetHeaderRow(DependencyObject o) => (ChatWindow.ChatRow?)o.GetValue(HeaderRowProperty);

        public static readonly DependencyProperty BodyRowProperty =
            DependencyProperty.RegisterAttached("BodyRow", typeof(ChatWindow.ChatRow), typeof(ChatInlines),
                new PropertyMetadata(null, OnBodyChanged));
        public static void SetBodyRow(DependencyObject o, ChatWindow.ChatRow v) => o.SetValue(BodyRowProperty, v);
        public static ChatWindow.ChatRow? GetBodyRow(DependencyObject o) => (ChatWindow.ChatRow?)o.GetValue(BodyRowProperty);

        private static readonly Brush Gray = Freeze(0x7a, 0x90, 0x70);
        private static readonly Brush Body = Freeze(0xe8, 0xe0, 0xc4);

        private static SolidColorBrush Freeze(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        private static void OnRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            tb.Inlines.Clear();
            if (e.NewValue is not ChatWindow.ChatRow row) return;

            if (!string.IsNullOrEmpty(row.Symbol))
                tb.Inlines.Add(new Run(row.Symbol + " ") { Foreground = row.SymbolBrush, FontWeight = FontWeights.Bold });

            foreach (var b in row.Badges)
                tb.Inlines.Add(BuildBadge(b));

            tb.Inlines.Add(new Run(row.User) { FontWeight = FontWeights.Bold, Foreground = row.UserBrush });
            tb.Inlines.Add(new Run(": ") { Foreground = Gray });

            foreach (var seg in row.Segments)
            {
                if (seg.Kind == ChatSegmentKind.Emote && !string.IsNullOrEmpty(seg.Url))
                    tb.Inlines.Add(BuildEmote(seg));
                else
                    tb.Inlines.Add(new Run(seg.Text) { Foreground = Body });
            }
        }

        private static Inline BuildEmote(ChatSegment seg)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;     // cache by URI; reuse across rows
                bmp.UriSource = new Uri(seg.Url, UriKind.Absolute);
                bmp.DecodePixelHeight = 36;                     // 2× the display height for crispness
                bmp.EndInit();

                var img = new Image
                {
                    Source = bmp,
                    Height = 18,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(1, 0, 1, 0),
                    ToolTip = string.IsNullOrEmpty(seg.Text) ? null : seg.Text,
                };
                img.ImageFailed += (_, _) => { };   // never let a bad emote URL bubble up
                return new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center };
            }
            catch
            {
                return new Run(seg.Text) { Foreground = Body }; // fall back to alt text
            }
        }

        private static readonly Brush Dark = Freeze(0x0a, 0x14, 0x10);

        private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            tb.Inlines.Clear();
            if (e.NewValue is not ChatWindow.ChatRow row) return;

            if (!string.IsNullOrEmpty(row.Symbol))
                tb.Inlines.Add(new Run(row.Symbol + " ") { Foreground = Dark, FontWeight = FontWeights.Bold });
            foreach (var b in row.Badges)
                tb.Inlines.Add(BuildBadge(b));
            tb.Inlines.Add(new Run(row.User) { FontWeight = FontWeights.Bold, Foreground = Dark });
        }

        private static void OnBodyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            tb.Inlines.Clear();
            if (e.NewValue is not ChatWindow.ChatRow row) return;

            foreach (var seg in row.Segments)
            {
                if (seg.Kind == ChatSegmentKind.Emote && !string.IsNullOrEmpty(seg.Url))
                    tb.Inlines.Add(BuildEmote(seg));
                else
                    tb.Inlines.Add(new Run(seg.Text) { Foreground = Brushes.White });
            }
        }

        private static Inline BuildBadge(ChatBadge b)
        {
            // Image badge (from SSN) — small icon.
            if (!string.IsNullOrEmpty(b.Url))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(b.Url, UriKind.Absolute);
                    bmp.DecodePixelHeight = 30;
                    bmp.EndInit();
                    var img = new Image { Source = bmp, Height = 15, Margin = new Thickness(0, 0, 3, 0) };
                    img.ImageFailed += (_, _) => { };
                    return new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center };
                }
                catch { /* fall through to a text label */ }
            }

            // Text label badge (e.g. MOD / VIP / SUB).
            var badgeColor = Body;
            try { badgeColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(b.Color)); } catch { }
            var border = new Border
            {
                Background = badgeColor,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(3, 0, 3, 1),
                Margin = new Thickness(0, 0, 3, 0),
                Child = new TextBlock
                {
                    Text = b.Label,
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                },
            };
            return new InlineUIContainer(border) { BaselineAlignment = BaselineAlignment.Center };
        }
    }
}
