using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// M22.3: Parlamento — partiler, sandalye dağılımı, oylama, veto, koalisyon, referandum.
// Kurgusal içerik, gerçek kişi/parti adı kullanılmaz. Tüm kararlar sandalye ağırlıklı.
public static class ParliamentSystem
{
    public class Party
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Ideology { get; set; } = "";
        public int Seats { get; set; }
        public string Color { get; set; } = "";
        public bool IsRuling { get; set; }
    }

    public class ParliamentSave
    {
        public PartySave[] Parties { get; set; } = Array.Empty<PartySave>();
        public string[] VetoedProposals { get; set; } = Array.Empty<string>();
    }

    public class PartySave
    {
        public string Id { get; set; } = "";
        public int Seats { get; set; }
        public bool IsRuling { get; set; }
    }

    public class VoteResult
    {
        public string ProposalId { get; set; } = "";
        public int YesSeats { get; set; }
        public int NoSeats { get; set; }
        public int AbstainSeats { get; set; }
        public int TotalSeats { get; set; }
        public int MajorityThreshold { get; set; }
        public bool Passed => YesSeats >= MajorityThreshold;
    }

    public class ReferendumResult
    {
        public string IssueId { get; set; } = "";
        public int YesVotes { get; set; }
        public int NoVotes { get; set; }
        public bool Passed => YesVotes > NoVotes;
    }

    public class CoalitionResult
    {
        public string[] PartyIds { get; set; } = Array.Empty<string>();
        public int CombinedSeats { get; set; }
        public int MajorityThreshold { get; set; }
        public bool HasMajority => CombinedSeats >= MajorityThreshold;
    }

    private const string Path = "res://Data/parties.json";

    private static readonly List<Party> _parties = new();
    // proposalId -> vetoed
    private static readonly HashSet<string> _vetoed = new();

    public static IReadOnlyList<Party> Parties => _parties.AsReadOnly();
    public static int TotalSeats => _parties.Sum(p => p.Seats);
    public static int MajorityThreshold => TotalSeats > 0 ? TotalSeats / 2 + 1 : 1;

    public static void LoadData()
    {
        _parties.Clear();
        _vetoed.Clear();
        try
        {
            if (!Godot.FileAccess.FileExists(Path))
            {
                GD.PrintErr($"[Parliament] Veri dosyası yok: {Path}");
                GD.Print($"Parlamento hazır: 0 parti, 0 sandalye.");
                return;
            }
            string json = Godot.FileAccess.GetFileAsString(Path);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("Parties", out var arr))
            {
                GD.PrintErr("[Parliament] 'Parties' dizisi bulunamadı.");
                GD.Print($"Parlamento hazır: 0 parti, 0 sandalye.");
                return;
            }
            int count = 0;
            foreach (var item in arr.EnumerateArray())
            {
                var p = new Party
                {
                    Id = item.TryGetProperty("Id", out var id) ? id.GetString() : "",
                    Name = item.TryGetProperty("Name", out var n) ? n.GetString() : "",
                    Ideology = item.TryGetProperty("Ideology", out var i) ? i.GetString() : "",
                    Seats = item.TryGetProperty("Seats", out var s) ? s.GetInt32() : 0,
                    Color = item.TryGetProperty("Color", out var c) ? c.GetString() : "",
                    IsRuling = item.TryGetProperty("IsRuling", out var r) && r.GetBoolean()
                };
                if (!string.IsNullOrEmpty(p.Id))
                {
                    _parties.Add(p);
                    count++;
                }
            }
            GD.Print($"Parlamento hazır: {count} parti, {TotalSeats} sandalye, çoğunluk eşiği {MajorityThreshold}.");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[Parliament] Yükleme hatası: {e.Message}");
        }
    }

    public static void ResetForNewGame()
    {
        _parties.Clear();
        _vetoed.Clear();
    }

    public static Party GetParty(string partyId) => _parties.FirstOrDefault(p => p.Id == partyId);

    /// <summary>Verilen partilerin birleşik sandalyesi çoğunluk eşiğine ulaşır mı?</summary>
    public static bool HasMajority(IEnumerable<string> partyIds)
    {
        int seats = SeatsFor(partyIds);
        return seats >= MajorityThreshold;
    }

    /// <summary>Oylama: destekleyen partilerin sandalyesi çoğunluk eşiğine uçar geçer.</summary>
    public static bool CanPass(IEnumerable<string> supportingPartyIds) => HasMajority(supportingPartyIds);

    private static int SeatsFor(IEnumerable<string> partyIds)
    {
        int seats = 0;
        foreach (var id in partyIds ?? Array.Empty<string>())
        {
            var p = GetParty(id);
            if (p != null) seats += p.Seats;
        }
        return seats;
    }

    /// <summary>Bir teklifin oylanması. Sandalye ağırlıklı: yes sandalyesi >= çoğunluk eşiği geçer.</summary>
    public static VoteResult Vote(string proposalId, IEnumerable<string> yes, IEnumerable<string> no, IEnumerable<string> abstain)
    {
        var result = new VoteResult
        {
            ProposalId = proposalId ?? "",
            YesSeats = SeatsFor(yes),
            NoSeats = SeatsFor(no),
            AbstainSeats = SeatsFor(abstain),
            TotalSeats = TotalSeats,
            MajorityThreshold = MajorityThreshold
        };
        GD.Print($"[Parliament] Oylama '{proposalId}': Evet={result.YesSeats}, Hayır={result.NoSeats}, Tarafsız={result.AbstainSeats}, Eşik={result.MajorityThreshold} → {(result.Passed ? "GEÇTİ" : "KALDI")}");
        return result;
    }

    /// <summary>Yürütme organınego veto. Tekrar ozgurlastirilene kadar teklifi engeller.</summary>
    public static void Veto(string proposalId)
    {
        if (string.IsNullOrEmpty(proposalId)) return;
        _vetoed.Add(proposalId);
        GD.Print($"[Parliament] Veto uygulandı: {proposalId}");
    }

    public static bool IsVetoed(string proposalId) => !string.IsNullOrEmpty(proposalId) && _vetoed.Contains(proposalId);

    public static void RemoveVeto(string proposalId) => _vetoed.Remove(proposalId ?? "");

    /// <summary>Koalisyon denetimi: verilen partilerin birleşik sandalyeli çoğunluk oluşturup oluşturmadığı.</summary>
    public static CoalitionResult FormCoalition(IEnumerable<string> partyIds)
    {
        var ids = partyIds ?? Array.Empty<string>();
        return new CoalitionResult
        {
            PartyIds = ids.ToArray(),
            CombinedSeats = SeatsFor(ids),
            MajorityThreshold = MajorityThreshold
        };
    }

    /// <summary>Genel referendum: direkt oy (sandalye ağırlığı yok, baş sayısı).</summary>
    public static ReferendumResult Referendum(string issueId, int yesVotes, int noVotes)
    {
        var result = new ReferendumResult { IssueId = issueId ?? "", YesVotes = yesVotes, NoVotes = noVotes };
        GD.Print($"[Parliament] Referandum '{issueId}': Evet={yesVotes}, Hayır={noVotes} → {(result.Passed ? "KABUL EDİLDİ" : "REDDEDİLDİ")}");
        return result;
    }

    // --- Save / Load ---
    public static ParliamentSave GetSaveData()
    {
        return new ParliamentSave
        {
            Parties = _parties.Select(p => new PartySave { Id = p.Id, Seats = p.Seats, IsRuling = p.IsRuling }).ToArray(),
            VetoedProposals = _vetoed.ToArray()
        };
    }

    public static void LoadSaveData(ParliamentSave save)
    {
        _parties.Clear();
        _vetoed.Clear();
        if (save == null) return;
        if (save.Parties != null)
            foreach (var ps in save.Parties)
                _parties.Add(new Party { Id = ps.Id, Seats = ps.Seats, IsRuling = ps.IsRuling, Name = ps.Id, Ideology = "", Color = "" });
        if (save.VetoedProposals != null)
            foreach (var v in save.VetoedProposals) _vetoed.Add(v);
        GD.Print($"[Parliament] State yüklendi — Parti={_parties.Count}, Sandalye={TotalSeats}, Veto={_vetoed.Count}");
    }
}
