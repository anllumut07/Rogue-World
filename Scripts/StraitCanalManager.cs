using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Manages strait and canal passage rules (Suez, Panama, Bosphorus, Gibraltar, etc.)
/// Rules define permissions by relationship: contested/enemy/friend/neutral
/// </summary>
public class StraitCanalManager
{
    private Dictionary<string, StraitCanalRule> _rules = new();
    private Dictionary<int, List<string>> _provinceToRules = new(); // province -> rule names

    public int RuleCount => _rules.Count;

    public void AddRule(string name, StraitCanalRule rule)
    {
        _rules[name] = rule;
        foreach (int provId in rule.RequiredProvinces)
        {
            if (!_provinceToRules.ContainsKey(provId))
                _provinceToRules[provId] = new List<string>();
            _provinceToRules[provId].Add(name);
        }
    }

    public StraitCanalRule GetRule(string name)
    {
        return _rules.GetValueOrDefault(name);
    }

    public List<StraitCanalRule> GetRulesForProvince(int provinceId)
    {
        var result = new List<StraitCanalRule>();
        if (_provinceToRules.TryGetValue(provinceId, out var ruleNames))
        {
            foreach (var name in ruleNames)
                if (_rules.TryGetValue(name, out var rule))
                    result.Add(rule);
        }
        return result;
    }

    /// <summary>
    /// Check if a country can pass through a strait/canal
    /// </summary>
    public StraitPermissions CheckPassage(string ruleName, string countryTag, string controllerTag, bool isContested)
    {
        if (!_rules.TryGetValue(ruleName, out var rule))
            return new StraitPermissions { Army = true, Navy = true, Submarine = true, Trade = true }; // Default allow

        // Check if disabled
        if (rule.IsDisabled(countryTag))
            return new StraitPermissions { Army = false, Navy = false, Submarine = false, Trade = false };

        // Determine relationship
        string relationship = DetermineRelationship(countryTag, controllerTag, isContested);

        return relationship switch
        {
            "contested" => rule.Contested,
            "enemy" => rule.Enemy,
            "friend" => rule.Friend,
            "neutral" => rule.Neutral,
            _ => rule.Neutral
        };
    }

    /// <summary>
    /// Check passage for a specific adjacency (from adjacency graph)
    /// </summary>
    public StraitPermissions CheckPassageForAdjacency(Adjacency adj, string countryTag, Func<int, string> getProvinceController, bool isContested)
    {
        if (string.IsNullOrEmpty(adj.RuleName))
            return new StraitPermissions { Army = true, Navy = true, Submarine = true, Trade = true };

        string controllerTag = "";
        if (adj.ThroughProvince > 0)
            controllerTag = getProvinceController(adj.ThroughProvince);

        return CheckPassage(adj.RuleName, countryTag, controllerTag, isContested);
    }

    private string DetermineRelationship(string countryTag, string controllerTag, bool isContested)
    {
        if (isContested) return "contested";

        if (string.IsNullOrEmpty(controllerTag)) return "neutral";

        // In a full implementation, this would check diplomatic relations
        // For now: same country = friend, enemy at war = enemy, else neutral
        if (countryTag == controllerTag) return "friend";

        // Would check: CountryAI.AreAtWar(countryTag, controllerTag)
        // return CountryAI.AreAtWar(countryTag, controllerTag) ? "enemy" : "neutral";
        return "neutral"; // Simplified
    }

    /// <summary>
    /// Get all rules
    /// </summary>
    public IReadOnlyDictionary<string, StraitCanalRule> GetAllRules()
    {
        return _rules;
    }
}

public class StraitCanalRule
{
    public string Name { get; set; } = "";
    public StraitPermissions Contested { get; set; } = new();
    public StraitPermissions Enemy { get; set; } = new();
    public StraitPermissions Friend { get; set; } = new();
    public StraitPermissions Neutral { get; set; } = new();
    public List<int> RequiredProvinces { get; set; } = new();
    public List<string> DisabledConditions { get; set; } = new(); // Flags/modifiers that disable

    public bool IsDisabled(string countryTag)
    {
        // Check if any disabled condition is met for this country
        // Conditions like: global_flag = "suez_canal_closed", country_flag = "blocked_suez"
        foreach (var condition in DisabledConditions)
        {
            // Simplified: would check actual game flags
            // if (GameManager.Instance.HasGlobalFlag(condition) || CountryAI.HasCountryFlag(countryTag, condition))
            //     return true;
        }
        return false;
    }
}

public class StraitPermissions
{
    public bool Army { get; set; } = true;
    public bool Navy { get; set; } = true;
    public bool Submarine { get; set; } = true;
    public bool Trade { get; set; } = true;

    public bool CanPass(bool isNaval, bool isSubmarine, bool isTrade)
    {
        if (isTrade) return Trade;
        if (isSubmarine) return Submarine;
        if (isNaval) return Navy;
        return Army;
    }
}