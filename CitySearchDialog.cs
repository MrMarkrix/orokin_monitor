using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OrokinMonitor
{
    // A small modal dialog: type a city, search, pick a match. Built in code so
    // it needs no extra XAML file. Returns the chosen GeoResult or null.
    internal static class CitySearchDialog
    {
        public static Task<WeatherService.GeoResult?> ShowDialogAsync(
            Window owner, WeatherService svc, Theme theme)
        {
            var tcs = new TaskCompletionSource<WeatherService.GeoResult?>();

            var win = new Window
            {
                Title = "Change city",
                Width = 340,
                SizeToContent = SizeToContent.Height,   // grow to fit content
                MinHeight = 300,
                MaxHeight = 520,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                Background = theme.Window,
                WindowStyle = WindowStyle.ToolWindow,
            };

            var root = new StackPanel { Margin = new Thickness(12) };

            var prompt = new TextBlock
            {
                Text = "Type a city and press Search:",
                Foreground = theme.Text,
                Margin = new Thickness(0, 0, 0, 6),
            };

            var inputRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
            var searchBtn = new Button { Content = "Search", Width = 70, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(searchBtn, Dock.Right);
            var box = new TextBox { FontSize = 14, Padding = new Thickness(4) };
            inputRow.Children.Add(searchBtn);
            inputRow.Children.Add(box);

            var list = new ListBox
            {
                Height = 160,
                Background = theme.Panel,
                Foreground = theme.Text,
                BorderBrush = theme.PanelEdge,
            };

            var status = new TextBlock
            {
                Foreground = theme.TextDim,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };

            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0),
            };
            var okBtn = new Button { Content = "Use this", Width = 80, IsEnabled = false, Margin = new Thickness(0, 0, 6, 0) };
            var cancelBtn = new Button { Content = "Cancel", Width = 70 };
            btnRow.Children.Add(okBtn);
            btnRow.Children.Add(cancelBtn);

            root.Children.Add(prompt);
            root.Children.Add(inputRow);
            root.Children.Add(list);
            root.Children.Add(status);
            root.Children.Add(btnRow);
            win.Content = root;

            async Task DoSearch()
            {
                string q = box.Text.Trim();
                if (q.Length == 0) return;
                status.Text = "Searching…";
                list.Items.Clear();
                okBtn.IsEnabled = false;
                var results = await svc.SearchCityAsync(q);
                if (results.Count == 0)
                {
                    status.Text = "No matches. Try a different spelling.";
                    return;
                }
                foreach (var r in results)
                    list.Items.Add(new ListBoxItem { Content = r.Display, Tag = r });
                status.Text = $"{results.Count} match(es). Pick one, then Use this.";
            }

            searchBtn.Click += async (_, _) => await DoSearch();
            box.KeyDown += async (_, e) => { if (e.Key == Key.Enter) await DoSearch(); };
            list.SelectionChanged += (_, _) => okBtn.IsEnabled = list.SelectedItem != null;
            list.MouseDoubleClick += (_, _) => { if (list.SelectedItem != null) Commit(); };

            void Commit()
            {
                if (list.SelectedItem is ListBoxItem item && item.Tag is WeatherService.GeoResult g)
                {
                    tcs.TrySetResult(g);
                    win.Close();
                }
            }

            okBtn.Click += (_, _) => Commit();
            cancelBtn.Click += (_, _) => { tcs.TrySetResult(null); win.Close(); };
            win.Closed += (_, _) => tcs.TrySetResult(null); // safety if X-ed

            box.Focus();
            win.ShowDialog();
            return tcs.Task;
        }
    }
}
