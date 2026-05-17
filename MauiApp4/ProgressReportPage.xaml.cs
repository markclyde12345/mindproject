using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace project
{
    public class MoodEntryDisplay
    {
        public string Emoji { get; set; }
        public string MoodLabel { get; set; }
        public string DateText { get; set; }
        public string ScoreText { get; set; }
    }

    public partial class ProgressReportPage : ContentPage
    {
        private int _currentDays = 7;
        private const int MaxScorePerSession = 15;

        private List<MoodEntry> _cachedFirebaseMoods = new List<MoodEntry>();

        public ProgressReportPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _ = LoadDataAsync();
        }

        // CreatedAt is an ISO-8601 string — parse to local DateTime
        private static bool TryGetLocalDate(MoodEntry entry, out DateTime localDate)
        {
            localDate = default;
            if (string.IsNullOrWhiteSpace(entry.CreatedAt)) return false;
            if (!DateTime.TryParse(entry.CreatedAt, out var parsed)) return false;
            localDate = parsed.ToLocalTime();
            return true;
        }

        // MoodScore is raw 0–15; normalise to 0–5
        private static double GetNormalizedScore(MoodEntry entry)
            => (double)entry.MoodScore / MaxScorePerSession * 5.0;

        private static (string emoji, string label) ScoreToDisplay(double score)
        {
            if (score >= 4.5) return ("🌟", "Feeling Great");
            if (score >= 3.5) return ("😊", "Feeling Good");
            if (score >= 2.5) return ("😐", "Feeling Okay");
            if (score >= 1.5) return ("😔", "Feeling Low");
            return ("💙", "Overwhelming");
        }

        // ── Load ───────────────────────────────────────────────────────────────
        private async Task LoadDataAsync()
        {
            try
            {
                var result = await FirebaseService.GetMoodsAsync();
                _cachedFirebaseMoods = result != null ? result : new List<MoodEntry>();

                // ── Mood Log Count (this week) ──────────────────────────────
                int daysSinceMonday = ((int)DateTime.Today.DayOfWeek + 6) % 7;
                var weekStart = DateTime.Today.AddDays(-daysSinceMonday);
                var tomorrow = DateTime.Today.AddDays(1);

                int weeklyMoodCount = _cachedFirebaseMoods.Count(e =>
                {
                    if (!TryGetLocalDate(e, out var local)) return false;
                    return local.Date >= weekStart && local.Date < tomorrow;
                });
                MoodLogCountLabel.Text = weeklyMoodCount.ToString();

                // ── Activities ─────────────────────────────────────────────
                ActivitiesLabel.Text = _cachedFirebaseMoods.Count.ToString();

                // ── Avg Mood ───────────────────────────────────────────────
                var emojiLabel = this.FindByName<Label>("AvgMoodEmoji");
                var avgLabel = this.FindByName<Label>("AvgMoodLabel");
                var subLabel = this.FindByName<Label>("AvgMoodSubLabel");

                if (_cachedFirebaseMoods.Any())
                {
                    double avgRating = Math.Round(
                        _cachedFirebaseMoods.Average(e => GetNormalizedScore(e)), 1);
                    var (emoji, moodLabel) = ScoreToDisplay(avgRating);

                    if (emojiLabel != null) emojiLabel.Text = emoji;
                    if (avgLabel != null) avgLabel.Text = $"{avgRating:F1}/5";
                    if (subLabel != null) subLabel.Text = moodLabel;
                }
                else
                {
                    if (emojiLabel != null) emojiLabel.Text = "😊";
                    if (avgLabel != null) avgLabel.Text = "—";
                    if (subLabel != null) subLabel.Text = "";
                }

                GenerateInsight(_cachedFirebaseMoods);
                DrawChart(_currentDays);
                DrawMonthlyChart();
                LoadRecentEntries(_cachedFirebaseMoods);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProgressReportPage] LoadDataAsync error: {ex}");
            }
        }

        // ── Insight ────────────────────────────────────────────────────────────
        private void GenerateInsight(List<MoodEntry> allEntries)
        {
            if (!allEntries.Any())
            {
                InsightLabel.Text = "Complete your first check-in to see insights!";
                return;
            }
            if (allEntries.Count == 1)
            {
                InsightLabel.Text = "Great start! Keep checking in daily to track your mood trends.";
                return;
            }

            var now = DateTime.Now;

            var last3 = allEntries.Where(e =>
            {
                if (!TryGetLocalDate(e, out var d)) return false;
                return d >= now.AddDays(-3);
            }).ToList();

            var prev3 = allEntries.Where(e =>
            {
                if (!TryGetLocalDate(e, out var d)) return false;
                return d >= now.AddDays(-6) && d < now.AddDays(-3);
            }).ToList();

            if (last3.Any() && prev3.Any())
            {
                double avgLast = last3.Average(e => GetNormalizedScore(e));
                double avgPrev = prev3.Average(e => GetNormalizedScore(e));
                double diff = avgLast - avgPrev;
                double pct = Math.Abs(diff / avgPrev * 100);

                if (diff > 0) InsightLabel.Text = $"Your mood improved by {pct:F0}% in the last 3 days! Keep it up 🌟";
                else if (diff < 0) InsightLabel.Text = $"Your mood dipped {pct:F0}% recently. Try a short walk 💙";
                else InsightLabel.Text = "Your mood has been steady. Consistency is great!";
            }
            else if (last3.Any())
            {
                double avgScore = last3.Average(e => GetNormalizedScore(e));
                string lbl = avgScore >= 4 ? "great" : avgScore >= 3 ? "okay" : "challenging";
                InsightLabel.Text = $"Your average mood is {avgScore:F1}/5 — feeling {lbl}. Keep checking in!";
            }
            else
            {
                InsightLabel.Text = "Keep checking in daily to unlock mood insights!";
            }
        }

        // ── Weekly line chart ──────────────────────────────────────────────────
        private void DrawChart(int days)
        {
            while (ChartCanvas.Children.Count > 6)
                ChartCanvas.Children.RemoveAt(6);
            ChartCanvas.GestureRecognizers.Clear();

            var cutoff = DateTime.UtcNow.AddDays(-days);
            var entries = _cachedFirebaseMoods
                .Where(e =>
                {
                    if (!TryGetLocalDate(e, out var d)) return false;
                    return d.ToUniversalTime() >= cutoff;
                })
                .OrderBy(e => e.CreatedAt)
                .ToList();

            var avgBadge = this.FindByName<Border>("ChartAvgBadge");
            var avgEmoji = this.FindByName<Label>("ChartAvgEmoji");
            var avgTxt = this.FindByName<Label>("ChartAvgLabel");

            if (!entries.Any())
            {
                EmptyChartLabel.IsVisible = true;
                ChartTooltip.IsVisible = false;
                if (avgBadge != null) avgBadge.IsVisible = false;
                return;
            }

            EmptyChartLabel.IsVisible = false;

            double sevenDayAvg = Math.Round(entries.Average(e => GetNormalizedScore(e)), 1);
            var (bEmoji, _) = ScoreToDisplay(sevenDayAvg);

            if (avgBadge != null) avgBadge.IsVisible = true;
            if (avgEmoji != null) avgEmoji.Text = bEmoji;
            if (avgTxt != null) avgTxt.Text = $"{sevenDayAvg:F1}/5";

            double avgLineY = (100 - (sevenDayAvg - 1.0) / 4.0 * 100.0) / 100.0 * 160;

            const double segOn = 8, segOff = 5, cw = 300;
            for (double x = 0; x < cw; x += segOn + segOff)
            {
                var seg = new Line
                {
                    X1 = x,
                    Y1 = avgLineY,
                    X2 = Math.Min(x + segOn, cw),
                    Y2 = avgLineY,
                    Stroke = new SolidColorBrush(Color.FromArgb("#FF9500")),
                    StrokeThickness = 1.5
                };
                AbsoluteLayout.SetLayoutBounds(seg, new Rect(0, 0, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
                AbsoluteLayout.SetLayoutFlags(seg, AbsoluteLayoutFlags.None);
                ChartCanvas.Children.Insert(ChartCanvas.Children.Count - 1, seg);
            }

            var slots = new List<(string label, double? score)>();
            for (int i = days - 1; i >= 0; i--)
            {
                var day = DateTime.Now.AddDays(-i).Date;
                var dayEntries = entries.Where(e =>
                {
                    if (!TryGetLocalDate(e, out var d)) return false;
                    return d.Date == day;
                }).ToList();

                double? avg = dayEntries.Any()
                    ? dayEntries.Average(e => GetNormalizedScore(e))
                    : (double?)null;

                slots.Add((day.ToString("ddd"), avg));
            }

            UpdateXAxisLabels(slots.Select(s => s.label).ToList());

            int total = slots.Count;
            const double chartH = 160, chartW = 300;

            var pts = slots
                .Select((s, i) => new { s.score, index = i })
                .Where(p => p.score != null)
                .ToList();

            for (int p = 0; p < pts.Count - 1; p++)
            {
                var a = pts[p]; var b = pts[p + 1];
                double xA = total == 1 ? 0.5 : (double)a.index / (total - 1);
                double xB = total == 1 ? 0.5 : (double)b.index / (total - 1);

                var line = new Line
                {
                    X1 = xA * chartW,
                    Y1 = (5 - a.score.Value) / 4.0 * chartH,
                    X2 = xB * chartW,
                    Y2 = (5 - b.score.Value) / 4.0 * chartH,
                    Stroke = new SolidColorBrush(Color.FromArgb("#5BC8D0")),
                    StrokeThickness = 2
                };
                AbsoluteLayout.SetLayoutBounds(line, new Rect(0, 0, AbsoluteLayout.AutoSize, AbsoluteLayout.AutoSize));
                AbsoluteLayout.SetLayoutFlags(line, AbsoluteLayoutFlags.None);
                ChartCanvas.Children.Insert(ChartCanvas.Children.Count - 1, line);
            }

            for (int i = 0; i < total; i++)
            {
                if (slots[i].score == null) continue;

                double score = slots[i].score.Value;
                double y = (5 - score) / 4.0 * chartH - 6;
                double xProp = total == 1 ? 0.5 : (double)i / (total - 1);
                double size = score >= 4.5 ? 14 : 11;
                string dayLabel = slots[i].label;
                double cx = xProp, cy = y, cs = score;

                var dot = new Ellipse
                {
                    Fill = new SolidColorBrush(Color.FromArgb("#5BC8D0")),
                    WidthRequest = size,
                    HeightRequest = size,
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 2
                };

                var tap = new TapGestureRecognizer();
                tap.Tapped += (s, e) =>
                {
                    ChartTooltipLabel.Text = $"{dayLabel}: {cs:F1}/5";
                    double tx = Math.Clamp(cx * chartW - 40, 0, chartW - 90);
                    double ty = Math.Max(cy - 36, 0);
                    AbsoluteLayout.SetLayoutBounds(ChartTooltip, new Rect(tx, ty, 90, 28));
                    AbsoluteLayout.SetLayoutFlags(ChartTooltip, AbsoluteLayoutFlags.None);
                    ChartTooltip.IsVisible = true;
                };
                dot.GestureRecognizers.Add(tap);

                AbsoluteLayout.SetLayoutBounds(dot, new Rect(xProp, y, size, size));
                AbsoluteLayout.SetLayoutFlags(dot, AbsoluteLayoutFlags.XProportional);
                ChartCanvas.Children.Insert(ChartCanvas.Children.Count - 1, dot);
            }

            var canvasTap = new TapGestureRecognizer();
            canvasTap.Tapped += (s, e) => ChartTooltip.IsVisible = false;
            ChartCanvas.GestureRecognizers.Add(canvasTap);
        }

        // ── Monthly bar chart ──────────────────────────────────────────────────
        private void DrawMonthlyChart()
        {
            MonthlyChartGrid.Children.Clear();
            MonthlyChartGrid.ColumnDefinitions.Clear();
            MonthlyTooltip.IsVisible = false;

            var now = DateTime.Now;
            var entries = _cachedFirebaseMoods.Where(e =>
            {
                if (!TryGetLocalDate(e, out var d)) return false;
                return d >= now.AddDays(-28);
            }).ToList();

            const double maxBarH = 120.0;
            var weeks = new[]
            {
                ("Week 1", now.AddDays(-28), now.AddDays(-21)),
                ("Week 2", now.AddDays(-21), now.AddDays(-14)),
                ("Week 3", now.AddDays(-14), now.AddDays(-7)),
                ("Week 4", now.AddDays(-7),  now)
            };

            for (int w = 0; w < weeks.Length; w++)
            {
                MonthlyChartGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = GridLength.Star });

                var (label, from, to) = weeks[w];
                var weekEntries = entries.Where(e =>
                {
                    if (!TryGetLocalDate(e, out var d)) return false;
                    return d >= from && d < to;
                }).ToList();

                double total = weekEntries.Any() ? weekEntries.Sum(e => GetNormalizedScore(e)) : 0;
                double barH = (total / 35.0) * maxBarH;
                string capturedLabel = label;
                double capturedTotal = total;

                var col = new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.End,
                    HorizontalOptions = LayoutOptions.Center,
                    Spacing = 4
                };

                col.Children.Add(new BoxView { HeightRequest = maxBarH - barH, Color = Colors.Transparent });

                if (total > 0)
                {
                    var bar = new Border
                    {
                        HeightRequest = barH,
                        WidthRequest = 32,
                        Background = new SolidColorBrush(Color.FromArgb("#5BC8D0")),
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(6, 6, 0, 0) }
                    };
                    var tap = new TapGestureRecognizer();
                    tap.Tapped += (s, e) =>
                    {
                        MonthlyTooltipLabel.Text = $"{capturedLabel}: {capturedTotal:F1} avg";
                        MonthlyTooltip.IsVisible = true;
                    };
                    bar.GestureRecognizers.Add(tap);
                    col.Children.Add(bar);
                }

                col.Children.Add(new Label
                {
                    Text = label,
                    FontSize = 10,
                    TextColor = Color.FromArgb("#AAAAAA"),
                    HorizontalOptions = LayoutOptions.Center
                });

                Grid.SetColumn(col, w);
                MonthlyChartGrid.Children.Add(col);
            }
        }

        // ── X-axis labels ──────────────────────────────────────────────────────
        private void UpdateXAxisLabels(List<string> labels)
        {
            XAxisLabels.Children.Clear();
            XAxisLabels.ColumnDefinitions.Clear();

            for (int i = 0; i < labels.Count; i++)
            {
                XAxisLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                var lbl = new Label
                {
                    Text = labels[i],
                    FontSize = 10,
                    TextColor = Color.FromArgb("#AAAAAA"),
                    HorizontalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(lbl, i);
                XAxisLabels.Children.Add(lbl);
            }
        }

        // ── Recent entries list ────────────────────────────────────────────────
        private void LoadRecentEntries(List<MoodEntry> allEntries)
        {
            var collection = this.FindByName<CollectionView>("RecentEntriesCollection");
            var emptyLabel = this.FindByName<Label>("EmptyEntriesLabel");

            var recent = allEntries
                .Take(10)
                .Select(e =>
                {
                    double score = GetNormalizedScore(e);
                    var (emoji, moodLabel) = ScoreToDisplay(score);

                    // Show the actual mood name if available
                    string displayLabel = !string.IsNullOrWhiteSpace(e.Mood)
                        ? $"Feeling {e.Mood}"
                        : moodLabel;

                    string dateText = TryGetLocalDate(e, out var d)
                        ? d.ToString("MMM dd, yyyy  h:mm tt")
                        : e.CreatedAt;

                    return new MoodEntryDisplay
                    {
                        Emoji = emoji,
                        MoodLabel = displayLabel,
                        DateText = dateText,
                        ScoreText = $"{score:F1}/5"
                    };
                })
                .ToList();

            if (recent.Any())
            {
                if (collection != null) collection.ItemsSource = recent;
                if (emptyLabel != null) emptyLabel.IsVisible = false;
            }
            else
            {
                if (collection != null) collection.ItemsSource = null;
                if (emptyLabel != null) emptyLabel.IsVisible = true;
            }
        }

        // ── Navigation ─────────────────────────────────────────────────────────
        private async void OnBackbuttonClicked(object sender, EventArgs e)
            => await Shell.Current.GoToAsync("//HomePage");

        private async void OnBackClicked(object sender, EventArgs e)
            => await Shell.Current.GoToAsync("..");
    }
}