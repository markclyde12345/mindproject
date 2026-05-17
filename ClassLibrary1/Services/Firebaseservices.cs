using Firebase.Auth;
using Firebase.Auth.Providers;
using Firebase.Database;
using Firebase.Database.Query;
using MentalWellness.Shared.Models;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace project
{
    // ─── MODELS ───────────────────────────────────────────────

    public class UserProfile
    {
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string UserId { get; set; }
        public string CreatedAt { get; set; }
        public bool IsEmailVerified { get; set; }
    }


    // ─── FIREBASE SERVICE ─────────────────────────────────────

    public static class FirebaseService
    {
        private const string FirebaseApiKey = "AIzaSyCoA1ekJ-b_dEIb3mFSofFDC0SAeOli3wE";
        private const string FirebaseDatabaseUrl = "https://mindbloom-d0622-default-rtdb.firebaseio.com/";
        private const string FirebaseAuthDomain = "mindbloom-d0622.firebaseapp.com";

        public static string CurrentUserId { get; private set; }
        public static string CurrentUserEmail { get; private set; }
        public static string CurrentToken { get; private set; }

        private static FirebaseClient _db;
        private static FirebaseAuthClient _auth;
        private static UserCredential _currentCredential;

        // ─── INIT ──────────────────────────────────────────────
        public static void Initialize()
        {
            var authConfig = new FirebaseAuthConfig
            {
                ApiKey = FirebaseApiKey,
                AuthDomain = FirebaseAuthDomain,
                Providers = new FirebaseAuthProvider[]
                {
                    new EmailProvider()
                }
            };

            _auth = new FirebaseAuthClient(authConfig);

            // DB client is created AFTER auth so we can pass the token
            _db = new FirebaseClient(FirebaseDatabaseUrl);
        }

        // ─── REGISTER ─────────────────────────────────────────
        public static async Task<bool> RegisterAsync(
            string firstName, string lastName, string email, string password)
        {
            UserCredential result = null;

            try
            {
                // Step 1: Create Firebase Auth user
                result = await _auth.CreateUserWithEmailAndPasswordAsync(email, password, firstName);

                _currentCredential = result;
                CurrentUserId = result.User.Uid;
                CurrentUserEmail = email;
                CurrentToken = await result.User.GetIdTokenAsync();

                // Step 2: Send email verification
                await SendEmailVerificationAsync(CurrentToken);

                // Step 3: Create an authenticated DB client using the user's token
                var authenticatedDb = new FirebaseClient(
                    FirebaseDatabaseUrl,
                    new FirebaseOptions
                    {
                        AuthTokenAsyncFactory = () => Task.FromResult(CurrentToken)
                    });

                // Step 4: Write to Realtime Database using authenticated client
                await authenticatedDb
                    .Child("users")
                    .Child(CurrentUserId)
                    .PutAsync(new UserProfile
                    {
                        FirstName = firstName,
                        LastName = lastName,
                        Email = email,
                        UserId = CurrentUserId,
                        CreatedAt = DateTime.UtcNow.ToString("o"),
                        IsEmailVerified = false
                    });

                // Update the global DB client to use auth token going forward
                _db = authenticatedDb;

                return true;
            }
            catch (Exception)
            {
                // Rollback: delete the ghost Auth user if DB write failed
                if (result != null)
                {
                    try
                    {
                        await result.User.DeleteAsync();
                    }
                    catch
                    {
                        // Ignore rollback errors silently
                    }
                }

                // Reset state
                CurrentUserId = null;
                CurrentUserEmail = null;
                CurrentToken = null;
                _currentCredential = null;

                throw;
            }
        }

        // ─── LOGIN ────────────────────────────────────────────
        public static async Task<(UserProfile profile, bool isVerified)> LoginAsync(
            string email, string password)
        {
            var result = await _auth.SignInWithEmailAndPasswordAsync(email, password);

            _currentCredential = result;
            CurrentUserId = result.User.Uid;
            CurrentUserEmail = email;
            CurrentToken = await result.User.GetIdTokenAsync();

            // Use authenticated DB client
            _db = new FirebaseClient(
                FirebaseDatabaseUrl,
                new FirebaseOptions
                {
                    AuthTokenAsyncFactory = () => Task.FromResult(CurrentToken)
                });

            var isVerified = result.User.Info.IsEmailVerified;

            if (isVerified)
            {
                await _db.Child("users")
                    .Child(CurrentUserId)
                    .Child("IsEmailVerified")
                    .PutAsync(true);
            }

            var profile = await _db
                .Child("users")
                .Child(CurrentUserId)
                .OnceSingleAsync<UserProfile>();

            return (profile, isVerified);
        }

        // ─── EMAIL VERIFY ─────────────────────────────────────
        private static async Task SendEmailVerificationAsync(string idToken)
        {
            using var client = new HttpClient();

            var payload = new { requestType = "VERIFY_EMAIL", idToken };
            var json = JsonSerializer.Serialize(payload);

            var content = new StringContent(json, Encoding.UTF8, "application/json");

            await client.PostAsync(
                $"https://identitytoolkit.googleapis.com/v1/accounts:sendOobCode?key={FirebaseApiKey}",
                content);
        }

        // ─── LOGOUT ───────────────────────────────────────────
        public static void Logout()
        {
            CurrentUserId = null;
            CurrentUserEmail = null;
            CurrentToken = null;
            _currentCredential = null;

            // Reset DB to unauthenticated client
            _db = new FirebaseClient(FirebaseDatabaseUrl);
        }

        // ─── SAFETY CHECK ─────────────────────────────────────
        private static void EnsureUser()
        {
            if (string.IsNullOrEmpty(CurrentUserId))
                throw new Exception("User not logged in");
        }

        // ─── ATTEMPTS ─────────────────────────────────────────
        public static async Task<int> IncrementAttemptsAsync()
        {
            EnsureUser();

            var refDb = _db.Child("users").Child(CurrentUserId).Child("totalAttempts");

            var current = await refDb.OnceSingleAsync<int?>();
            int newValue = (current ?? 0) + 1;

            await refDb.PutAsync(newValue);
            return newValue;
        }

        public static async Task<int> GetTotalAttemptsAsync()
        {
            EnsureUser();

            var result = await _db.Child("users")
                .Child(CurrentUserId)
                .Child("totalAttempts")
                .OnceSingleAsync<int?>();

            return result ?? 0;
        }

        // ─── LOGS ─────────────────────────────────────────────
        public static async Task<int> IncrementLogsAsync()
        {
            EnsureUser();

            var refDb = _db.Child("users").Child(CurrentUserId).Child("totalLogs");

            var current = await refDb.OnceSingleAsync<int?>();
            int newValue = (current ?? 0) + 1;

            await refDb.PutAsync(newValue);
            return newValue;
        }

        public static async Task<int> GetTotalLogsAsync()
        {
            EnsureUser();

            var result = await _db.Child("users")
                .Child(CurrentUserId)
                .Child("totalLogs")
                .OnceSingleAsync<int?>();

            return result ?? 0;
        }

        // ─── SAVE MOOD ────────────────────────────────────────
        public static async Task SaveMoodAsync(
            string mood,
            int score,
            string notes,
            List<int> answers)
        {
            EnsureUser();

            int attemptNumber = await IncrementAttemptsAsync();

            await _db
                .Child("moods")
                .Child(CurrentUserId)
                .PostAsync(new MoodEntry
                {
                    UserId = CurrentUserId,
                    Mood = mood,
                    MoodScore = score,
                    Answers = answers,
                    AttemptNumber = attemptNumber,
                    Notes = notes,
                    CreatedAt = DateTime.UtcNow.ToString("o")
                });
        }

        // ─── GET MOODS ───────────────────────────────────────
        public static async Task<List<MoodEntry>> GetMoodsAsync()
        {
            EnsureUser();

            var entries = await _db
                .Child("moods")
                .Child(CurrentUserId)
                .OnceAsync<MoodEntry>();

            return entries
                .Select(e => e.Object)
                .OrderByDescending(e => e.CreatedAt)
                .ToList();
        }

        // ─── RESET PASSWORD ──────────────────────────────────
        public static async Task SendPasswordResetAsync(string email)
        {
            await _auth.ResetEmailPasswordAsync(email);
        }


        public static async Task SaveCopingProgressAsync(int completed, int total)
        {
            EnsureUser();

            await _db.Child("users")
                .Child(CurrentUserId)
                .Child("copingProgress")
                .PutAsync(new { completed, total });
        }
        public static async Task<(int completed, int total)> GetCopingProgressAsync()
        {
            EnsureUser();

            var result = await _db.Child("users")
                .Child(CurrentUserId)
                .Child("copingProgress")
                .OnceSingleAsync<dynamic>();

            if (result == null)
                return (0, 4);

            int completed = result.completed;
            int total = result.total;

            return (completed, total);
        }

        public static async Task ChangePasswordAsync(string currentPassword, string newPassword)
        {
            EnsureUser();

            var result = await _auth
                .SignInWithEmailAndPasswordAsync(CurrentUserEmail, currentPassword);

            await result.User.ChangePasswordAsync(newPassword);
        }
        public static async Task<bool> HasLoggedMoodTodayAsync()
        {
            EnsureUser();

            var today = DateTime.Today;

            var entries = await _db.Child("moods")
                .Child(CurrentUserId)
                .OnceAsync<MoodEntry>();

            return entries.Any(e =>
                DateTime.TryParse(e.Object.CreatedAt, out var d) &&
                d.Date == today);
        }

        public static async Task<UserProfile> GetUserProfileAsync()
        {
            EnsureUser();

            return await _db.Child("users")
                .Child(CurrentUserId)
                .OnceSingleAsync<UserProfile>();
        }

        public static async Task ResetCopingProgressIfNewWeekAsync()
        {
            EnsureUser();

            int daysSinceMonday = ((int)DateTime.Today.DayOfWeek + 6) % 7;
            var weekStart = DateTime.Today.AddDays(-daysSinceMonday);
            string weekKey = weekStart.ToString("yyyy-MM-dd");

            var lastReset = await _db.Child("users")
                .Child(CurrentUserId)
                .Child("lastCopingReset")
                .OnceSingleAsync<string>();

            if (lastReset != weekKey)
            {
                await SaveCopingProgressAsync(0, 4);

                await _db.Child("users")
                    .Child(CurrentUserId)
                    .Child("lastCopingReset")
                    .PutAsync(weekKey);
            }
        }

        public static async Task<int> GetWeeklyMainMoodCountAsync()
        {
            EnsureUser();

            int daysSinceMonday = ((int)DateTime.Today.DayOfWeek + 6) % 7;
            var weekStart = DateTime.Today.AddDays(-daysSinceMonday);

            var entries = await _db.Child("moods")
                .Child(CurrentUserId)
                .OnceAsync<MoodEntry>();

            return entries.Count(e =>
                DateTime.TryParse(e.Object.CreatedAt, out var d) &&
                d.Date >= weekStart);
        }

        public static async Task<int> GetStreakAsync()
        {
            EnsureUser();

            var result = await _db.Child("users")
                .Child(CurrentUserId)
                .Child("streak")
                .OnceSingleAsync<int?>();

            return result ?? 0;
        }
    }
}