namespace ProjectLauncher.Core;

/// <summary>Edits the existing schema without shell parsing or implicit cross-action changes.</summary>
public static class ActionEditing
{
    public static ParameterDefinition CloneParameter(ParameterDefinition source)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<ParameterDefinition>(System.Text.Json.JsonSerializer.Serialize(source))!;
        copy.Default = ValueCodec.Unwrap(copy.Default);
        copy.VisibleWhen = copy.VisibleWhen.ToDictionary(x => x.Key, x => ValueCodec.Unwrap(x.Value));
        return copy;
    }
    public static void SaveParameter(LauncherConfig config, ActionDefinition action, string? originalName, ParameterDefinition edited, bool confirmed)
    {
        // Work on detached data: cancel and failed validation must not mutate the settings draft.
        var candidate = new LauncherConfig { App = config.App, Runtime = config.Runtime, SchemaVersion = config.SchemaVersion,
            Parameters = config.Parameters.Select(CloneParameter).ToList(),
            Actions = config.Actions.Select(a => new ActionDefinition { Id = a.Id, Label = a.Label, Argv = [.. a.Argv], Parameters = a.Parameters is null ? null : [.. a.Parameters], TimeoutSeconds = a.TimeoutSeconds }).ToList() };
        ExplicitMembership(candidate, confirmed);
        var target = candidate.Actions.Single(a => a.Id == action.Id);
        var copy = CloneParameter(edited);
        if (copy.IsSecret) copy.Default = null;
        if (originalName is null)
        {
            copy.Name = Unique("parameter", candidate.Parameters.Select(p => p.Name));
            candidate.Parameters.Add(copy); target.Parameters!.Add(copy.Name);
        }
        else
        {
            if (!target.Parameters!.Contains(originalName)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ActionEditing.1", []));
            var remap = new Dictionary<string, string>();
            var affected = new HashSet<string> { originalName };
            // Localize downstream conditions as well, so they keep following the edited field.
            bool added;
            do { added = false; foreach (var p in candidate.Parameters.Where(p => target.Parameters.Contains(p.Name)))
                if (p.VisibleWhen.Keys.Any(affected.Contains)) added |= affected.Add(p.Name); } while (added);
            foreach (var name in affected)
                if (candidate.Actions.Any(a => a != target && a.Parameters!.Contains(name)))
                    remap[name] = Unique(name + "_copy", candidate.Parameters.Select(p => p.Name).Concat(remap.Values));
            foreach (var name in affected)
            {
                var old = candidate.Parameters.Single(p => p.Name == name);
                var replacement = name == originalName ? copy : CloneParameter(old);
                replacement.Name = remap.GetValueOrDefault(name, name);
                replacement.VisibleWhen = replacement.VisibleWhen.ToDictionary(x => remap.GetValueOrDefault(x.Key, x.Key), x => x.Value);
                if (remap.ContainsKey(name)) candidate.Parameters.Add(replacement);
                else candidate.Parameters[candidate.Parameters.IndexOf(old)] = replacement;
            }
            target.Parameters = target.Parameters.Select(n => remap.GetValueOrDefault(n, n)).ToList();
        }
        ConfigValidator.Validate(candidate);
        config.Parameters = candidate.Parameters;
        foreach (var a in config.Actions) a.Parameters = candidate.Actions.Single(c => c.Id == a.Id).Parameters;
    }
    public static string BrowsedTarget(string projectRoot, string file)
    {
        var relative = Path.GetRelativePath(projectRoot, file);
        return Path.IsPathRooted(relative) ? relative : "." + Path.DirectorySeparatorChar + relative;
    }
    public static string Unique(string prefix, IEnumerable<string> existing)
    {
        var used = existing.ToHashSet(StringComparer.Ordinal); var value = prefix; int n = 2;
        while (used.Contains(value)) value = prefix + n++;
        return value;
    }
    public static ActionDefinition AddAction(LauncherConfig config)
    {
        var action = new ActionDefinition { Id = Unique("action", config.Actions.Select(a => a.Id)), Label = "新的启动动作", Argv = ["python", ""], Parameters = [] };
        config.Actions.Add(action); return action;
    }
    public static void ExplicitMembership(LauncherConfig config, bool confirmed)
    {
        if (!config.Actions.Any(a => a.Parameters is null)) return;
        if (!confirmed) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ActionEditing.2", []));
        foreach (var a in config.Actions.Where(a => a.Parameters is null)) a.Parameters = config.Parameters.Select(p => p.Name).ToList();
    }
    public static ParameterDefinition AddParameter(LauncherConfig config, ActionDefinition action, bool confirmed = false)
    {
        ExplicitMembership(config, confirmed);
        var name = Unique("parameter", config.Parameters.Select(p => p.Name));
        var p = new ParameterDefinition { Name = name, Label = "新的参数", Argument = "--" + name };
        config.Parameters.Add(p); action.Parameters!.Add(name); return p;
    }
    public static string CommandKind(ActionDefinition action)
    {
        var a = action.Argv;
        if (a.Count == 2 && a[0] == "python" && (a[1].Length == 0 || a[1].EndsWith(".py", StringComparison.OrdinalIgnoreCase))) return "script";
        if (a.Count == 3 && a[0] == "python" && a[1] == "-m") return "module";
        if (a.Count == 1) return "program";
        return "advanced";
    }
}
