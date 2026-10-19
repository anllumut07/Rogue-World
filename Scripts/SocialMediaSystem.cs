using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// M28.1: Gündem Paneli — Olay → tweet üreteci.
/// Oyuncu bir olayı çözdüğünde olay türüne göre şablon cümlelerden değişkenli tweetler üretilir.
/// Yalnızca DeterministicRNG (new Random yasak), kaydedilebilir (GetSaveData/LoadSaveData).
/// UI metinleri İngilizce, yorumlar Türkçe.
/// </summary>
public static class SocialMediaSystem
{
    public enum Sentiment { Positive, Negative, Neutral }

    /// <summary>M28.4: Oyuncunun bir tweete verebileceği tepki türü. Her eylemin toplum
    /// grubu memnuniyetine deterministik bir etkisi vardır.</summary>
    public enum TweetAction { None, Statement, Investigation, Ignore, Ban }

    public class Tweet
    {
        public string Id { get; set; }           // "tweet-{gun}-{no}"
        public string AuthorName { get; set; }   // kurgusal profil adı
        public string AuthorHandle { get; set; } // @kullanici
        public string Content { get; set; }      // tweet metni (İngilizce UI)
        public int Day { get; set; }             // üretim günü
        public string CountryId { get; set; }    // ilgili ülke (oyuncu)
        public string EventId { get; set; }      // kaynak olay id'si
        public Sentiment Sentiment { get; set; } = Sentiment.Neutral;
        public int Likes { get; set; }
        public int Shares { get; set; }
        public TweetAction Action { get; set; } = TweetAction.None; // M28.4: uygulanan tepki (henüz yoksa None)
        public int ActionDay { get; set; } = -1;                    // M28.4: tepkinin uygulandığı gün (-1 = yok)
    }

    private enum Category { War, Annex, Peace, Economy, Focus, Diplomacy, Default }

    private static readonly List<Tweet> _tweets = new();
    private static int _nextTweetNum = 1;

    public static void OnEventResolved(EventSystem.GameEvent evt, int optionIndex)
    {
        if (evt == null) return;
        var gm = GameManager.Instance;
        if (gm == null || !gm.HasPlayer) return;

        string playerId = gm.PlayerCountryId;
        var playerData = CountryDatabase.Get(playerId);
        string countryName = playerData?.Name ?? playerId ?? "Unknown";

        // Hedef ülke (savaş/ilhak olaylarında ilan/hedef bazlı)
        string targetId = "";
        string targetName = "";
        var effects = (optionIndex >= 0 && optionIndex < evt.Options.Count)
            ? evt.Options[optionIndex].Effects
            : new EventSystem.EventEffects();
        if (!string.IsNullOrEmpty(effects.DeclareWar))
        {
            targetId = effects.DeclareWar;
            targetName = CountryDatabase.Get(targetId)?.Name ?? targetId;
        }
        else if ((Category.War == Categorize(evt) || Category.Annex == Categorize(evt))
                 && !string.IsNullOrEmpty(evt.Trigger.WarAttacker) && !string.IsNullOrEmpty(evt.Trigger.WarDefender))
        {
            string att = CountryDatabase.NormalizeId(evt.Trigger.WarAttacker);
            string def = CountryDatabase.NormalizeId(evt.Trigger.WarDefender);
            if (att == playerId) { targetId = def; targetName = CountryDatabase.Get(def)?.Name ?? def; }
            else if (def == playerId) { targetId = att; targetName = CountryDatabase.Get(att)?.Name ?? att; }
        }

        string date = gm.GetDateText();
        Category category = Categorize(evt);
        string template = PickTemplate(category);
        string content = FillTemplate(template, countryName, targetName, evt.Name ?? "?", date);
        Sentiment sentiment = SentimentFor(category);

        // Yazar: kurgusal profil havuzu (ülke kültürüne göre); yoksa sabit yedek.
        SocialProfileSystem.EnsureProfilesFor(playerId);
        var author = SocialProfileSystem.GetRandomAuthor(playerId);

        var tweet = new Tweet
        {
            Id = $"tweet-{gm.TotalDays}-{_nextTweetNum++}",
            AuthorName = author.Name,
            AuthorHandle = author.Handle,
            Content = content,
            Day = gm.TotalDays,
            CountryId = playerId,
            EventId = evt.Id,
            Sentiment = sentiment,
            Likes = (int)(DeterministicRNG.NextDouble() * 5000),
            Shares = (int)(DeterministicRNG.NextDouble() * 500),
        };
        _tweets.Add(tweet);

        GD.Print($"[SocialMedia] Tweet üretildi: {tweet.AuthorHandle} → {content}");
    }

    // M28.4: Oyuncu bir tweete tepki verir (açıklama / soruşturma / görmezden gel / yasakla).
    // Eylem aynı tweete yalnızca bir kez uygulanabilir ve ilgili ülkenin toplum grubu
    // memnuniyetlerini deterministik olarak değiştirir.
    public static bool RespondToTweet(string tweetId, TweetAction action)
    {
        if (string.IsNullOrEmpty(tweetId) || action == TweetAction.None) return false;

        var tweet = _tweets.FirstOrDefault(t => t.Id == tweetId);
        if (tweet == null || tweet.Action != TweetAction.None) return false;

        var gm = GameManager.Instance;
        if (gm == null || !gm.HasPlayer) return false;

        tweet.Action = action;
        tweet.ActionDay = gm.TotalDays;

        ApplySatisfactionEffects(gm.PlayerCountryId, action);

        GD.Print($"[SocialMedia] Tweet eylemi: {tweetId} → {action} (gün {gm.TotalDays}, ülke={gm.PlayerCountryId})");
        return true;
    }

    // M28.4: Tweet eyleminin ülke bazlı toplum grubu memnuniyetine deterministik etkisi.
    // (groupId, bazyDelta) çiftleri; her gruba küçük ±0.25 jitter eklenir.
    private static void ApplySatisfactionEffects(string countryId, TweetAction action)
    {
        var deltas = action switch
        {
            TweetAction.Statement => new (string, double)[]
            {
                ("youth", 3.0), ("business", 2.0), ("secular", 1.0), ("religious", -1.0)
            },
            TweetAction.Investigation => new (string, double)[]
            {
                ("military", 5.0), ("youth", 2.0), ("workers", 1.0), ("business", -3.0)
            },
            TweetAction.Ignore => new (string, double)[]
            {
                ("youth", -2.0), ("workers", -1.0), ("secular", -1.0)
            },
            TweetAction.Ban => new (string, double)[]
            {
                ("military", 3.0), ("religious", 2.0), ("business", -2.0), ("youth", -5.0), ("secular", -4.0)
            },
            _ => Array.Empty<(string, double)>()
        };

        foreach (var (groupId, delta) in deltas)
        {
            double jitter = (DeterministicRNG.NextDouble() * 2.0 - 1.0) * 0.25; // ±0.25
            double next = PublicSatisfactionSystem.GetSatisfaction(countryId, groupId) + delta + jitter;
            PublicSatisfactionSystem.SetSatisfaction(countryId, groupId, next);
        }
    }

    // ===== Kategori ve şablon seçimi (deterministik) =====

    private static Category Categorize(EventSystem.GameEvent evt)
    {
        string id = (evt.Id ?? "").ToLowerInvariant();
        string name = (evt.Name ?? "").ToLowerInvariant();

        if (id.Contains("anschluss") || id.Contains("annex") || id.Contains("fall_of_france") || name.Contains("annex"))
            return Category.Annex;
        if (id.Contains("war") || id.Contains("barbarossa") || id.Contains("pearl") || id.Contains("d_day")
            || id.Contains("winter") || id.Contains("spanish") || name.Contains("war"))
            return Category.War;
        if (id.Contains("munich") || id.Contains("peace")) return Category.Peace;
        if (id.Contains("miracle") || id.Contains("new_deal") || name.Contains("econom")) return Category.Economy;
        if (id.Contains("molotov") || name.Contains("pact")) return Category.Diplomacy;
        if (id.Contains("focus") || name.Contains("focus")) return Category.Focus;
        return Category.Default;
    }

    private static string PickTemplate(Category category)
    {
        var templates = TemplatesFor(category);
        int index = (int)(DeterministicRNG.NextDouble() * templates.Count);
        if (index < 0) index = 0;
        if (index >= templates.Count) index = templates.Count - 1;
        return templates[index];
    }

    private static List<string> TemplatesFor(Category category)
    {
        switch (category)
        {
            case Category.Annex:
                return new List<string>
                {
                    "{0} has annexed {1}! Borders redrawn by force.",
                    "{0} absorbs {1} into its territory. The world watches in silence.",
                    "{1} ceases to exist as an independent state — annexed by {0}.",
                };
            case Category.War:
                return new List<string>
                {
                    "BREAKING: {0} has declared war on {1}! Tensions at an all-time high.",
                    "{0} is now at war with {1}. General mobilization ordered.",
                    "War erupts: {0} launches an invasion of {1}. Civilians flee.",
                };
            case Category.Peace:
                return new List<string>
                {
                    "Peace at last: {0} and {1} sign a ceasefire.",
                    "{0} and {1} agree to peace — 'peace for our time.'",
                    "Diplomacy prevails: hostilities end between {0} and {1}.",
                };
            case Category.Economy:
                return new List<string>
                {
                    "Economic miracle in {0}! Industrial output surges.",
                    "{0}'s economy booms — factories humming, jobs growing.",
                    "Prosperity in {0}: GDP rises as reforms take hold.",
                };
            case Category.Focus:
                return new List<string>
                {
                    "{0} charts a new course: {2}.",
                    "New national focus in {0}: {2}.",
                    "{0} adopts {2} as its guiding policy.",
                };
            case Category.Diplomacy:
                return new List<string>
                {
                    "{0} signs a non-aggression pact with {1}.",
                    "Diplomatic breakthrough: {0} and {1} normalize relations.",
                    "{0} and {1} announce a pact — the balance of power shifts.",
                };
            default:
                return new List<string>
                {
                    "Major development in {0}: {2}.",
                    "{0}: {2} unfolds — the world watches.",
                    "Breaking from {0}: {2}.",
                };
        }
    }

    private static Sentiment SentimentFor(Category category)
    {
        switch (category)
        {
            case Category.War: return Sentiment.Negative;
            case Category.Annex: return Sentiment.Negative;
            case Category.Peace: return Sentiment.Positive;
            case Category.Economy: return Sentiment.Positive;
            case Category.Focus: return Sentiment.Positive;
            default: return Sentiment.Neutral;
        }
    }

    private static string FillTemplate(string template, string countryName, string targetName, string eventName, string date)
    {
        return template
            .Replace("{0}", countryName)
            .Replace("{1}", string.IsNullOrEmpty(targetName) ? "an unknown nation" : targetName)
            .Replace("{2}", eventName)
            .Replace("{3}", date);
    }

    // ===== Public API =====

    public static int TweetCount => _tweets.Count;

    public static IReadOnlyList<Tweet> GetTweets() => _tweets;

    // M28.3: Gündem (trend) listesi — tweet'leri toplam etkileşime (beğeni+paylaşım)
    // göre sıralar; eşlikte daha yeni tweet önce gelir.
    public static IReadOnlyList<Tweet> GetTrendList(int limit = 10)
    {
        return _tweets
            .OrderByDescending(t => t.Likes + t.Shares)
            .ThenByDescending(t => t.Day)
            .ThenBy(t => t.Id)
            .Take(limit)
            .ToList();
    }

    // M28.3: Duygu (olumlu/olumsuz/nötr) eğilimi özeti.
    public static (int Positive, int Negative, int Neutral) GetSentimentSummary()
    {
        int pos = _tweets.Count(t => t.Sentiment == Sentiment.Positive);
        int neg = _tweets.Count(t => t.Sentiment == Sentiment.Negative);
        int neu = _tweets.Count(t => t.Sentiment == Sentiment.Neutral);
        return (pos, neg, neu);
    }

    // M28.3: Toplam etkileşim sayısı.
    public static (int TotalLikes, int TotalShares) GetEngagementTotals()
    {
        return (_tweets.Sum(t => t.Likes), _tweets.Sum(t => t.Shares));
    }

    // ===== M12: Save/Load =====

    public static SaveSystem.SocialMediaSave GetSaveData()
    {
        return new SaveSystem.SocialMediaSave
        {
            Tweets = _tweets.Select(t => new Tweet
            {
                Id = t.Id,
                AuthorName = t.AuthorName,
                AuthorHandle = t.AuthorHandle,
                Content = t.Content,
                Day = t.Day,
                CountryId = t.CountryId,
                EventId = t.EventId,
                Sentiment = t.Sentiment,
                Likes = t.Likes,
                Shares = t.Shares,
                Action = t.Action,
                ActionDay = t.ActionDay,
            }).ToArray()
        };
    }

    public static void LoadSaveData(SaveSystem.SocialMediaSave data)
    {
        _tweets.Clear();
        if (data?.Tweets != null)
        {
            foreach (var t in data.Tweets)
                _tweets.Add(new Tweet
                {
                    Id = t.Id,
                    AuthorName = t.AuthorName,
                    AuthorHandle = t.AuthorHandle,
                    Content = t.Content,
                    Day = t.Day,
                    CountryId = t.CountryId,
                    EventId = t.EventId,
                    Sentiment = t.Sentiment,
                    Likes = t.Likes,
                    Shares = t.Shares,
                    Action = t.Action,
                    ActionDay = t.ActionDay,
                });
        }
        _nextTweetNum = _tweets.Count + 1;
        GD.Print($"SocialMedia: state yüklendi — tweet={_tweets.Count}");
    }

    public static void ResetForNewGame()
    {
        _tweets.Clear();
        _nextTweetNum = 1;
        GD.Print("SocialMedia: yeni oyun için sıfırlandı.");
    }
}
