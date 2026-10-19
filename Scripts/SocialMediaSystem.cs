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
    }

    private enum Category { War, Annex, Peace, Economy, Focus, Diplomacy, Default }

    private static readonly List<Tweet> _tweets = new();
    private static int _nextTweetNum = 1;

    private static readonly (string Name, string Handle)[] _authors = new[]
    {
        ("Alex Carter", "@carter_news"),
        ("Maria Gonzalez", "@gonzalez_reports"),
        ("James Wilson", "@wilson_world"),
        ("Fatima Al-Sayed", "@fatima_dispatch"),
        ("Lars Müller", "@lars_europe"),
        ("Wei Zhang", "@wei_asia"),
        ("Sarah Johnson", "@sarah_pulse"),
        ("Dimitri Volkov", "@dimitri_east"),
    };

    // ===== Olay çözüldüğünde çağrılır (EventSystem.OnEventOptionChosen'dan) =====

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

        var author = _authors[(int)(DeterministicRNG.NextDouble() * _authors.Length)];

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
