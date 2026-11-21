using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

/// <summary>
/// StraitCanalSystem - Manages 33 strait/canal rules with relationship-gated passage.
/// Based on HoI4's map/adjacency_rules.txt.
/// </summary>
public static class StraitCanalSystem
{
    private static bool _initialized = false;
    private static Dictionary<string, StraitRule> _rules = new();

    public enum Relationship
    {
        Contested,
        Enemy,
        Friend,
        Neutral
    }

    public enum PassageType
    {
        Army,
        Navy,
        Submarine,
        Trade
    }

    public sealed class StraitRule
    {
        public string Name;                    // e.g., "SUEZ_CANAL"
        public List<int> RequiredProvinces = new(); // Provinces that must be controlled
        public Dictionary<Relationship, PassagePermissions> Permissions = new();
        public List<DisableCondition> DisableConditions = new();
        public int Icon = 0;
        public Vector3 Offset = Vector3.Zero;
        public string DisabledTooltip = "";
    }

    public sealed class PassagePermissions
    {
        public bool Army = false;
        public bool Navy = false;
        public bool Submarine = false;
        public bool Trade = false;
    }

    public sealed class DisableCondition
    {
        public enum Type { GlobalFlag, CountryFlag, DynamicModifier, OR, AND, NOT }
        public Type ConditionType;
        public string FlagName;           // For flag checks
        public int CountryId = 0;         // For country-specific checks
        public string ModifierName;       // For dynamic modifier checks
        public string Scope = "THIS";     // Modifier scope
        public List<DisableCondition> SubConditions = new(); // For OR/AND/NOT
        public string Tooltip = "";
    }

    public static bool IsInitialized => _initialized;

    public static void Initialize()
    {
        if (_initialized) return;

        const string path = "res://Data/hoi4_imported/strait_canal_rules.json";
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"[StraitCanalSystem] {path} not found!");
            return;
        }

        try
        {
            var options = new JsonSerializerOptions
            {
                IncludeFields = true,
                PropertyNameCaseInsensitive = true
            };
            var json = FileAccess.GetFileAsString(path);
            var rulesList = JsonSerializer.Deserialize<List<JsonRule>>(json, options);

            if (rulesList != null)
            {
                foreach (var jr in rulesList)
                {
                    var rule = new StraitRule
                    {
                        Name = jr.Name ?? "",
                        RequiredProvinces = jr.RequiredProvinces ?? new List<int>(),
                        Icon = jr.Icon,
                        Offset = new Vector3(jr.Offset?.X ?? 0, jr.Offset?.Y ?? 0, jr.Offset?.Z ?? 0),
                        DisabledTooltip = jr.DisabledTooltip ?? ""
                    };

                    // Parse permissions
                    if (jr.Contested != null) rule.Permissions[Relationship.Contested] = ParsePermissions(jr.Contested);
                    if (jr.Enemy != null) rule.Permissions[Relationship.Enemy] = ParsePermissions(jr.Enemy);
                    if (jr.Friend != null) rule.Permissions[Relationship.Friend] = ParsePermissions(jr.Friend);
                    if (jr.Neutral != null) rule.Permissions[Relationship.Neutral] = ParsePermissions(jr.Neutral);

                    // Parse disable conditions
                    if (jr.IsDisabled != null)
                    {
                        rule.DisableConditions = ParseDisableConditions(jr.IsDisabled);
                    }

                    _rules[rule.Name] = rule;
                }
            }

            _initialized = true;
            GD.Print($"[StraitCanalSystem] Initialized: {_rules.Count} strait/canal rules");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[StraitCanalSystem] Failed to load: {e.Message}");
        }
    }

    private static PassagePermissions ParsePermissions(JsonPermissions jp)
    {
        return new PassagePermissions
        {
            Army = jp.Army,
            Navy = jp.Navy,
            Submarine = jp.Submarine,
            Trade = jp.Trade
        };
    }

    private static List<DisableCondition> ParseDisableConditions(JsonDisableConditions jdc)
    {
        var conditions = new List<DisableCondition>();

        if (jdc.OR.ValueKind != JsonValueKind.Undefined)
        {
            var orCond = new DisableCondition { ConditionType = DisableCondition.Type.OR };
            orCond.SubConditions = ParseDisableConditionsList(jdc.OR);
            conditions.Add(orCond);
        }
        else if (jdc.AND.ValueKind != JsonValueKind.Undefined)
        {
            var andCond = new DisableCondition { ConditionType = DisableCondition.Type.AND };
            andCond.SubConditions = ParseDisableConditionsList(jdc.AND);
            conditions.Add(andCond);
        }
        else if (jdc.NOT.ValueKind != JsonValueKind.Undefined)
        {
            var notCond = new DisableCondition { ConditionType = DisableCondition.Type.NOT };
            notCond.SubConditions = ParseDisableConditionsList(jdc.NOT);
            conditions.Add(notCond);
        }
        else
        {
            // Single condition
            conditions.Add(ParseSingleCondition(jdc));
        }

        return conditions;
    }

    private static List<DisableCondition> ParseDisableConditionsList(object obj)
    {
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var list = new List<DisableCondition>();
            foreach (var item in je.EnumerateArray())
            {
                // This would need more complex parsing for nested structures
                // Simplified for now
            }
            return list;
        }
        return new List<DisableCondition>();
    }

    private static DisableCondition ParseSingleCondition(JsonDisableConditions jdc)
    {
        var cond = new DisableCondition();

        if (jdc.HasGlobalFlag.ValueKind != JsonValueKind.Undefined)
        {
            cond.ConditionType = DisableCondition.Type.GlobalFlag;
            cond.FlagName = jdc.HasGlobalFlag.GetString() ?? "";
        }
        else if (jdc.HasCountryFlag.ValueKind != JsonValueKind.Undefined)
        {
            cond.ConditionType = DisableCondition.Type.CountryFlag;
            cond.FlagName = jdc.HasCountryFlag.GetString() ?? "";
        }
        else if (jdc.HasDynamicModifier.ValueKind != JsonValueKind.Undefined)
        {
            cond.ConditionType = DisableCondition.Type.DynamicModifier;
            // Parse nested object
            var dm = jdc.HasDynamicModifier;
            cond.ModifierName = dm.GetProperty("Modifier").GetString() ?? "";
            cond.CountryId = dm.TryGetProperty("Scope", out var scope) && scope.GetString() == "214" ? 214 : 0;
        }

        cond.Tooltip = jdc.Tooltip ?? "";
        return cond;
    }

    private static PassagePermissions GetDefaultPermissions(Relationship rel)
    {
        // Default permissions when rule not found
        return new PassagePermissions
        {
            Army = rel != Relationship.Enemy,
            Navy = rel != Relationship.Enemy,
            Submarine = rel != Relationship.Enemy,
            Trade = rel == Relationship.Friend || rel == Relationship.Neutral
        };
    }

    public static void ResetForNewGame()
    {
        // Static data
    }

    /// <summary>
    /// Determine if a country can pass through a strait/canal.
    /// </summary>
    public static PassagePermissions CanPassThrough(string ruleName, string countryId,
        Dictionary<int, string> provinceControllers)
    {
        if (!_rules.TryGetValue(ruleName, out var rule))
            return GetDefaultPermissions(Relationship.Neutral);

        // Check if disabled
        if (IsDisabled(rule, countryId))
            return new PassagePermissions(); // All false

        // Determine controller of required provinces
        string controller = null;
        bool multipleControllers = false;

        foreach (var pid in rule.RequiredProvinces)
        {
            if (provinceControllers.TryGetValue(pid, out var c))
            {
                if (controller == null)
                    controller = c;
                else if (controller != c)
                    multipleControllers = true;
            }
        }

        // Determine relationship
        Relationship rel;
        if (multipleControllers)
        {
            rel = Relationship.Contested;
        }
        else if (controller == null)
        {
            rel = Relationship.Neutral; // Uncontrolled
        }
        else if (controller == countryId)
        {
            rel = Relationship.Friend;
        }
        else
        {
            // Simplified: check if at war via DiplomacySystem (relation <= -80 = war)
            int relation = DiplomacySystem.GetRelation(countryId, controller);
            rel = relation <= -80 ? Relationship.Enemy : Relationship.Neutral;
        }

        if (rule.Permissions.TryGetValue(rel, out var perms))
            return perms;

        return GetDefaultPermissions(rel);
    }

    private static bool IsDisabled(StraitRule rule, string countryId)
    {
        foreach (var cond in rule.DisableConditions)
        {
            if (EvaluateCondition(cond, countryId))
                return true;
        }
        return false;
    }

    private static bool EvaluateCondition(DisableCondition cond, string countryId)
    {
        switch (cond.ConditionType)
        {
            case DisableCondition.Type.GlobalFlag:
                // Simplified: no global flag system yet
                return false;

            case DisableCondition.Type.CountryFlag:
                // Simplified: no country flag system yet
                return false;

            case DisableCondition.Type.DynamicModifier:
                // Placeholder
                return false;

            case DisableCondition.Type.OR:
                foreach (var sub in cond.SubConditions)
                    if (EvaluateCondition(sub, countryId)) return true;
                return false;

            case DisableCondition.Type.AND:
                foreach (var sub in cond.SubConditions)
                    if (!EvaluateCondition(sub, countryId)) return false;
                return true;

            case DisableCondition.Type.NOT:
                foreach (var sub in cond.SubConditions)
                    if (EvaluateCondition(sub, countryId)) return true;
                return false;
        }
        return false;
    }

    /// <summary>
    /// Get all strait/canal rules.
    /// </summary>
    public static IReadOnlyDictionary<string, StraitRule> GetAllRules()
    {
        return _rules;
    }

    /// <summary>
    /// Get a specific rule by name.
    /// </summary>
    public static StraitRule GetRule(string name)
    {
        return _rules.GetValueOrDefault(name);
    }

    // JSON deserialization helpers
    private class JsonRule
    {
        public string Name;
        public List<int> RequiredProvinces;
        public JsonPermissions Contested;
        public JsonPermissions Enemy;
        public JsonPermissions Friend;
        public JsonPermissions Neutral;
        public JsonDisableConditions IsDisabled;
        public int Icon;
        public JsonVector3 Offset;
        public string DisabledTooltip;
    }

    private class JsonPermissions
    {
        public bool Army;
        public bool Navy;
        public bool Submarine;
        public bool Trade;
    }

    private class JsonDisableConditions
    {
        public string Tooltip;
        public JsonElement OR;
        public JsonElement AND;
        public JsonElement NOT;
        public JsonElement HasGlobalFlag;
        public JsonElement HasCountryFlag;
        public JsonElement HasDynamicModifier;
    }

    private class JsonDynamicModifier
    {
        public string Modifier;
        public string Scope;
    }

    private class JsonVector3
    {
        public float X, Y, Z;
    }
}