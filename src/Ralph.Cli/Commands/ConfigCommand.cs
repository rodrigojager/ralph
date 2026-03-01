using Ralph.Persistence.Config;
using Ralph.Persistence.Workspace;
using Ralph.Core.Localization;

namespace Ralph.Cli.Commands;

public sealed class ConfigCommand
{
    private readonly WorkspaceInitializer _workspaceInit;
    private readonly ConfigStore _configStore;

    public ConfigCommand(WorkspaceInitializer workspaceInit, ConfigStore configStore)
    {
        _workspaceInit = workspaceInit;
        _configStore = configStore;
    }

    public int Execute(string workingDirectory, string subCommand, string? key, string? value, IStringCatalog s)
    {
        var configPath = _workspaceInit.GetConfigPath(workingDirectory);
        if (!File.Exists(configPath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            _configStore.Save(configPath, RalphConfig.Default);
        }
        var config = _configStore.Load(configPath);

        switch (subCommand.ToLowerInvariant())
        {
            case "list":
                ListConfig(config, s);
                return 0;
            case "get":
                if (string.IsNullOrEmpty(key)) { Console.Error.WriteLine(s.Get("config.usage_get")); return 1; }
                GetConfig(config, key, s);
                return 0;
            case "set":
                if (string.IsNullOrEmpty(key)) { Console.Error.WriteLine(s.Get("config.usage_set")); return 1; }
                SetConfig(configPath, config, key, value ?? "", s);
                return 0;
            default:
                Console.Error.WriteLine(s.Get("config.usage"));
                return 1;
        }
    }

    private static void ListConfig(RalphConfig config, IStringCatalog s)
    {
        if (config.Parallel?.MaxParallel is { } mp)
            Console.WriteLine($"parallel: max_parallel={mp}");

        if (config.Engines == null || config.Engines.Count == 0)
        {
            Console.WriteLine(s.Get("config.empty"));
            return;
        }
        foreach (var (name, ec) in config.Engines)
            Console.WriteLine($"{name}: command={ec.Command ?? "?"}, model={ec.DefaultModel ?? "(default)"}, max_tokens={(ec.MaxTokens?.ToString() ?? "(default)")}, temperature={(ec.Temperature?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(default)")}");
    }

    private static void GetConfig(RalphConfig config, string key, IStringCatalog s)
    {
        if (key.Equals("parallel.max_parallel", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(config.Parallel?.MaxParallel?.ToString() ?? "");
            return;
        }
        if (key.Equals("parallel.integration_strategy", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(config.Parallel?.IntegrationStrategy ?? "");
            return;
        }
        if (key.Equals("context_rotation.max_total_tokens_per_run", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(config.ContextRotation?.MaxTotalTokensPerRun?.ToString() ?? "");
            return;
        }
        if (key.Equals("context_rotation.on_signal", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(config.ContextRotation?.OnSignal ?? "");
            return;
        }
        if (key.Equals("browser.enabled", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(config.Browser?.Enabled?.ToString().ToLowerInvariant() ?? "");
            return;
        }
        if (key.Equals("browser.command", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(config.Browser?.Command ?? "");
            return;
        }
        if (key.Equals("run.no_change_policy", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(config.Run?.NoChangePolicy ?? "");
            return;
        }
        if (key.Equals("run.no_change_max_attempts", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(config.Run?.NoChangeMaxAttempts?.ToString() ?? "");
            return;
        }
        if (key.Equals("run.include_progress_context", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(config.Run?.IncludeProgressContext?.ToString().ToLowerInvariant() ?? "");
            return;
        }

        var parts = key.Split('.', 2);
        if (parts.Length == 1)
        {
            if (config.Engines?.TryGetValue(key, out var ec) == true)
                Console.WriteLine($"command={ec.Command}, model={ec.DefaultModel}");
            else
                Console.WriteLine(s.Get("config.not_set"));
            return;
        }
        if (config.Engines?.TryGetValue(parts[0], out var e) == true)
        {
            var prop = parts[1].ToLowerInvariant();
            Console.WriteLine(prop switch
            {
                "command" => e.Command ?? "",
                "model" => e.DefaultModel ?? "",
                "default_model" => e.DefaultModel ?? "",
                "max_tokens" => e.MaxTokens?.ToString() ?? "",
                "temperature" => e.Temperature?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "",
                _ => s.Get("config.unknown_key")
            });
        }
        else
            Console.WriteLine(s.Get("config.not_set"));
    }

    private void SetConfig(string configPath, RalphConfig config, string key, string value, IStringCatalog s)
    {
        if (key.Equals("parallel.max_parallel", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(value, out var maxParallel))
            {
                Console.Error.WriteLine(s.Get("config.invalid_int"));
                return;
            }

            config.Parallel ??= new ParallelConfigEntry();
            config.Parallel.MaxParallel = Math.Max(1, maxParallel);
            _configStore.Save(configPath, config);
            Console.WriteLine(s.Format("config.set_ok", key, value));
            return;
        }
        if (key.Equals("parallel.integration_strategy", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized is not ("merge" or "create-pr" or "no-merge"))
            {
                Console.Error.WriteLine(s.Get("config.invalid_parallel_integration"));
                return;
            }

            config.Parallel ??= new ParallelConfigEntry();
            config.Parallel.IntegrationStrategy = normalized;
            _configStore.Save(configPath, config);
            Console.WriteLine(s.Format("config.set_ok", key, value));
            return;
        }
        if (key.Equals("context_rotation.max_total_tokens_per_run", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(value, out var maxTokensPerRun))
            {
                Console.Error.WriteLine(s.Get("config.invalid_int"));
                return;
            }

            config.ContextRotation ??= new ContextRotationConfigEntry();
            config.ContextRotation.MaxTotalTokensPerRun = maxTokensPerRun <= 0 ? null : maxTokensPerRun;
            _configStore.Save(configPath, config);
            Console.WriteLine(s.Format("config.set_ok", key, value));
            return;
        }
        if (key.Equals("context_rotation.on_signal", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized is not ("warn" or "rotate" or "defer" or "gutter"))
            {
                Console.Error.WriteLine(s.Get("config.invalid_context_rotation_mode"));
                return;
            }

            config.ContextRotation ??= new ContextRotationConfigEntry();
            config.ContextRotation.OnSignal = normalized;
            _configStore.Save(configPath, config);
            Console.WriteLine(s.Format("config.set_ok", key, value));
            return;
        }
        if (key.Equals("browser.enabled", StringComparison.OrdinalIgnoreCase))
        {
            if (!bool.TryParse(value, out var enabled))
            {
                Console.Error.WriteLine(s.Get("config.invalid_bool"));
                return;
            }
            config.Browser ??= new BrowserConfigEntry();
            config.Browser.Enabled = enabled;
            _configStore.Save(configPath, config);
            Console.WriteLine(s.Format("config.set_ok", key, value));
            return;
        }
        if (key.Equals("browser.command", StringComparison.OrdinalIgnoreCase))
        {
            config.Browser ??= new BrowserConfigEntry();
            config.Browser.Command = value;
            _configStore.Save(configPath, config);
            Console.WriteLine(s.Format("config.set_ok", key, value));
            return;
        }
        if (key.Equals("run.no_change_policy", StringComparison.OrdinalIgnoreCase))
        {
            var normalized = value.Trim().ToLowerInvariant();
            if (normalized is not ("fallback" or "retry" or "fail-fast"))
            {
                Console.Error.WriteLine(s.Get("config.invalid_no_change_policy"));
                return;
            }
            config.Run ??= new RunConfigEntry();
            config.Run.NoChangePolicy = normalized;
            _configStore.Save(configPath, config);
            Console.WriteLine(s.Format("config.set_ok", key, value));
            return;
        }
        if (key.Equals("run.no_change_max_attempts", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(value, out var attempts))
            {
                Console.Error.WriteLine(s.Get("config.invalid_int"));
                return;
            }
            config.Run ??= new RunConfigEntry();
            config.Run.NoChangeMaxAttempts = Math.Max(1, attempts);
            _configStore.Save(configPath, config);
            Console.WriteLine(s.Format("config.set_ok", key, value));
            return;
        }
        if (key.Equals("run.include_progress_context", StringComparison.OrdinalIgnoreCase))
        {
            if (!bool.TryParse(value, out var includeProgressContext))
            {
                Console.Error.WriteLine(s.Get("config.invalid_bool"));
                return;
            }
            config.Run ??= new RunConfigEntry();
            config.Run.IncludeProgressContext = includeProgressContext;
            _configStore.Save(configPath, config);
            Console.WriteLine(s.Format("config.set_ok", key, value));
            return;
        }

        config.Engines ??= new Dictionary<string, EngineConfigEntry>(StringComparer.OrdinalIgnoreCase);
        var parts = key.Split('.', 2);
        var engineName = parts.Length == 2 ? parts[0] : "cursor";
        var prop = parts.Length == 2 ? parts[1] : key;
        if (!config.Engines.TryGetValue(engineName, out var ec))
        {
            ec = new EngineConfigEntry();
            config.Engines[engineName] = ec;
        }
        switch (prop.ToLowerInvariant())
        {
            case "command":
                ec.Command = IsResetValue(value) ? null : value;
                break;
            case "model":
            case "default_model":
                ec.DefaultModel = IsResetValue(value) ? null : value;
                break;
            case "max_tokens":
                if (!int.TryParse(value, out var maxTokens))
                {
                    Console.Error.WriteLine(s.Get("config.invalid_int"));
                    return;
                }
                ec.MaxTokens = maxTokens;
                break;
            case "temperature":
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var temperature))
                {
                    Console.Error.WriteLine(s.Get("config.invalid_float"));
                    return;
                }
                ec.Temperature = temperature;
                break;
            default: Console.Error.WriteLine(s.Format("config.unknown_key_named", prop)); return;
        }
        _configStore.Save(configPath, config);
        Console.WriteLine(s.Format("config.set_ok", key, value));
    }

    private static bool IsResetValue(string value)
    {
        var normalized = value.Trim();
        return normalized.Length == 0
               || normalized.Equals("default", StringComparison.OrdinalIgnoreCase)
               || normalized.Equals("null", StringComparison.OrdinalIgnoreCase);
    }
}
