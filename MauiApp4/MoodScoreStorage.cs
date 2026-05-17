using Microsoft.Maui.Storage;
using System.Text.Json;

namespace project
{
    public static class MoodScoreStorage
    {
        private const string Key = "mood_scores";

        public static void SaveScore(int score)
        {
            var list = GetScores();
            list.Add(score);

            Preferences.Set(Key, JsonSerializer.Serialize(list));
        }

        public static List<int> GetScores()
        {
            var json = Preferences.Get(Key, "[]");
            return JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
        }

        public static double GetAverageScore()
        {
            var list = GetScores();
            if (list.Count == 0) return 0;

            return list.Average();
        }

        public static void Clear()
        {
            Preferences.Remove(Key);
        }
    }
}