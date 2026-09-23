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
        if (!File.Exists(path)) throw new ConfigException($"未找到配置文件：{path}");
        if (new FileInfo(path).Length > MaximumBytes) throw new ConfigException("配置文件超过 1 MB，请检查文件是否正确。");
        var bytes = File.ReadAllBytes(path);
        return new(path, Parse(Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF')), Hash(bytes));
    }

    public static LauncherConfig Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new ConfigException("配置文件超过 1 MB。");
        // Bound structure before deserializing; this config format intentionally has no YAML aliases or tags.
        ValidateYamlStructure(text);
        try
        {
            var config = Reader.Deserialize<LauncherConfig>(text) ?? throw new ConfigException("配置不能为空。");
            ConfigValidator.Validate(config);
            return config;
        }
        catch (YamlException e) { throw new ConfigException($"YAML 格式或字段有误（第 {e.Start.Line + 1} 行）：{e.Message}", e); }
        catch (InvalidOperationException e) { throw new ConfigException("配置格式错误：" + e.Message, e); }
    }

    private static void ValidateYamlStructure(string text)
    {
        try
        {
            var parser = new Parser(new StringReader(text));
            int depth = 0, count = 0;
            while (parser.MoveNext())
            {
                if (++count > 50_000) throw new ConfigException("配置节点过多。");
                if (parser.Current is YamlDotNet.Core.Events.AnchorAlias)
                    throw new ConfigException("启动配置不支持 YAML 别名，请直接填写对应值。");
                if (parser.Current is YamlDotNet.Core.Events.NodeEvent node && (!node.Anchor.IsEmpty || !node.Tag.IsEmpty))
                    throw new ConfigException("启动配置不支持 YAML 锚点或自定义标签。");
                if (parser.Current is YamlDotNet.Core.Events.MappingStart or YamlDotNet.Core.Events.SequenceStart)
                    if (++depth > 24) throw new ConfigException("配置嵌套过深。");
                if (parser.Current is YamlDotNet.Core.Events.MappingEnd or YamlDotNet.Core.Events.SequenceEnd) depth--;
            }
        }
        catch (YamlException e) { throw new ConfigException($"YAML 第 {e.Start.Line + 1} 行：{e.Message}", e); }
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
                throw new ConfigException("磁盘上的 launcher.yaml 已被其他程序修改。请先重新加载，避免覆盖别人的修改。");
            File.Copy(path, path + ".bak", true);
        }
        else if (expectedHash is not null) throw new ConfigException("配置文件已在磁盘上被删除，请重新加载。");
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
