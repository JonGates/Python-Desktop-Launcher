using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace ProjectLauncher.Core;

public static class ConfigStore
{
    public const int MaximumBytes = 1_048_576;
    private static readonly IDeserializer Reader = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .WithDuplicateKeyChecking()
        .WithAttemptingUnquotedStringTypeDeserialization()
        .Build();
    private static readonly ISerializer Writer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static ConfigSnapshot Load(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (!File.Exists(path)) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.1", [path]));
        if (new FileInfo(path).Length > MaximumBytes) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.2", []));
        var bytes = File.ReadAllBytes(path);
        return new(path, Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')), Hash(bytes));
    }

    public static LauncherConfig Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.3", []));
        // Bound structure before deserializing; this config format intentionally has no YAML aliases or tags.
        ValidateYamlStructure(text);
        try
        {
            var config = Reader.Deserialize<LauncherConfig>(text) ?? throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.4", []));
            ConfigValidator.Validate(config);
            return config;
        }
        catch (YamlException e) { throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.5", [e.Start.Line + 1, e.Message]), e); }
        catch (InvalidOperationException e) { throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.6", [e.Message]), e); }
    }

    private static void ValidateYamlStructure(string text)
    {
        try
        {
            var parser = new Parser(new StringReader(text));
            int depth = 0, count = 0;
            while (parser.MoveNext())
            {
                if (++count > 50_000) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.7", []));
                if (parser.Current is YamlDotNet.Core.Events.AnchorAlias)
                    throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.8", []));
                if (parser.Current is YamlDotNet.Core.Events.NodeEvent node && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty))
                    throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.9", []));
                if (parser.Current is YamlDotNet.Core.Events.MappingStart or YamlDotNet.Core.Events.SequenceStart)
                    if (++depth > 24) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.10", []));
                if (parser.Current is YamlDotNet.Core.Events.MappingEnd or YamlDotNet.Core.Events.SequenceEnd) depth--;
            }
        }
        catch (YamlException e) { throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.11", [e.Start.Line + 1, e.Message]), e); }
    }

    public static string Serialize(LauncherConfig config)
    {
        ConfigValidator.Validate(config);
        return Writer.Serialize(config);
    }

    public static LauncherConfig Clone(LauncherConfig config) => Parse(Serialize(config));

    public static ConfigSnapshot Save(string path, LauncherConfig config, string? expectedHash)
    {
        ConfigValidator.Validate(config);
        _ = new ProjectEnvironment(config, path); // Also validate rooted environment paths.
        if (File.Exists(path))
        {
            var actual = Hash(File.ReadAllBytes(path));
            if (expectedHash is null || !string.Equals(actual, expectedHash, StringComparison.Ordinal))
                throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.12", []));
            File.Copy(path, path + ".bak", true);
        }
        else if (expectedHash is not null) throw new ConfigException(new Localization.LocalizedDiagnostic("Error.ConfigStore.13", []));
        AtomicFile.Write(path, Serialize(config), overwrite: expectedHash is not null);
        return Load(path);
    }

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

public static class AtomicFile
{
    public static void Write(string path, string text, bool overwrite = true)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes); stream.Flush(true);
            }
            File.Move(temporary, path, overwrite);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
