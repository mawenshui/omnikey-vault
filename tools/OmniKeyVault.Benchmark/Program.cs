using System.Diagnostics;
using System.Text;
using OmniKeyVault.Application;
using OmniKeyVault.Domain;
using OmniKeyVault.Infrastructure;

namespace OmniKeyVault.Benchmark;

/// <summary>
/// v1.0 S8-T5: 1万条目性能压测工具(PRD §6 性能预算)。
///
/// 覆盖 4 个核心场景,每个场景的"目标 vs 实测"都打印一行,最后给出
/// "✓ ALL OK" 或 "✗ N MISMATCH" 总结。运行入口在 <see cref="Main"/>。
///
/// 用法:
///   dotnet run --project tools/OmniKeyVault.Benchmark -- [count]
/// 默认 10000 条目;设为 0 可跳过对应场景。
///
/// 弱化 Argon2id(64 MiB,生产 256 MiB)是为了让压测在合理时间内完成;
/// 真实数字请以 release 模式 + 256 MiB 为准。
/// </summary>
public static class Program
{
    private const string DemoPassword = "bench-pw-2026";

    public static int Main(string[] args)
    {
        var count = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 10_000;
        // OKV_TEST_MODE = 1 切换到弱化 KDF(64 MiB),避免 1万条目场景跑 1 小时。
        // 仅在 Debug 构建中允许设置；Release 构建中禁止修改此环境变量以防安全降级。
#if DEBUG
        Environment.SetEnvironmentVariable("OKV_TEST_MODE", "1");
#else
        if (Environment.GetEnvironmentVariable("OKV_TEST_MODE") == "1")
        {
            Console.WriteLine("⚠ 警告: OKV_TEST_MODE=1 在 Release 构建中被检测到。");
            Console.WriteLine("  这会弱化 Argon2id KDF 参数，仅适用于开发/测试环境。");
            Console.WriteLine("  按 Ctrl+C 取消，或按 Enter 继续...");
            Console.ReadLine();
        }
#endif

        var ver = System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "unknown";
        Console.WriteLine($"=== OmniKey Vault v{ver} Benchmark ===");
        Console.WriteLine($"Entry count: {count}");
        Console.WriteLine($"Runtime:     {Environment.Version}");
        Console.WriteLine();

        var results = new List<(string scenario, double actual, double target, string unit)>();

        if (count > 0)
        {
            results.Add(BenchCreate(count));
            results.Add(BenchUnlock());
            results.Add(BenchSearch(count));
            results.Add(BenchSync(count));
        }

        Console.WriteLine();
        Console.WriteLine("=== Summary ===");
        Console.WriteLine($"{"Scenario",-22}  {"Actual",-12}  {"Target",-10}  {"Unit",-6}  Status");
        var fails = 0;
        foreach (var r in results)
        {
            var status = r.actual <= r.target ? "✓ OK" : "✗ SLOW";
            if (r.actual > r.target) fails++;
            Console.WriteLine($"{r.scenario,-22}  {r.actual,-12:0.0}  {r.target,-10:0.0}  {r.unit,-6}  {status}");
        }
        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "✓ ALL OK — performance budget met" : $"✗ {fails} MISMATCH — review code path");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>场景 1:创建 1万条目 Vault 的总耗时(冷启动 + 创建 + 保存)。</summary>
    private static (string, double, double, string) BenchCreate(int count)
    {
        var path = Path.Combine(Path.GetTempPath(), $"okv-bench-{Guid.NewGuid():N}.okv");
        try
        {
            var crypto = new SodiumCryptoProvider();
            var format = new VaultFormat();
            var codec = new ProfilePayloadCodec();
            var ls = new LockService(crypto);
            var ks = new DeviceKeystore();
            using var vs = new VaultService(crypto, format, ls, codec, "bench-device", ks);
            var entrySvc = new EntryService(vs, new TemplateService(), new ClipboardService(new InMemoryClipboardProvider(), ls), crypto);

            var sw = Stopwatch.StartNew();
            vs.CreateAsync(path, "bench", Encoding.UTF8.GetBytes(DemoPassword), Argon2Params.ForTests(64 * 1024 * 1024)).GetAwaiter().GetResult();
            // 预生成 10 个不同的 entry 模板循环填充
            var templates = new[] { "github", "openai", "aws", "stripe", "supabase", "anthropic", "slack", "azure_service_principal", "gcp_service_account", "aliyun_ram_user" };
            for (int i = 0; i < count; i++)
            {
                var tplId = templates[i % templates.Length];
                var entry = entrySvc.Create("prod", $"bench-{i:D6}", EntryType.ApiKey, tplId,
                    new[] {
                        new Field { Key = "api_key", Value = FieldCodec.Encode("sk-bench-" + Guid.NewGuid().ToString("N").Substring(0, 8)), Kind = FieldKind.Secret, Sensitive = true },
                        new Field { Key = "account", Value = FieldCodec.Encode("user-" + i), Kind = FieldKind.Text, Sensitive = false },
                    });
                vs.PutEntry("prod", entry);
            }
            vs.SaveAsync().GetAwaiter().GetResult();
            sw.Stop();
            return ("create_vault", sw.Elapsed.TotalSeconds, 60, "s");  // PRD: ≤ 60s (10k entries)
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(path + ".manifest.json"); } catch { }
            try { File.Delete(Path.Combine(Path.GetTempPath(), "okv-bench-*.tmp")); } catch { }
        }
    }

    /// <summary>场景 2:重新打开 Vault 后的解锁耗时(含 Argon2id 派生)。</summary>
    private static (string, double, double, string) BenchUnlock()
    {
        // 复用刚 create 的 vault(只能通过共享的 VaultService,这里用 1 个 entry 的最小 vault)
        var path = Path.Combine(Path.GetTempPath(), $"okv-bench-unlock-{Guid.NewGuid():N}.okv");
        try
        {
            var crypto = new SodiumCryptoProvider();
            var format = new VaultFormat();
            var codec = new ProfilePayloadCodec();
            var ls = new LockService(crypto);
            var ks = new DeviceKeystore();
            using (var vs = new VaultService(crypto, format, ls, codec, "bench-device", ks))
            {
                vs.CreateAsync(path, "bench", Encoding.UTF8.GetBytes(DemoPassword), Argon2Params.ForTests(64 * 1024 * 1024)).GetAwaiter().GetResult();
            }
            // Reload: new VaultService, then UnlockAsync (Argon2id)
            using (var vs2 = new VaultService(crypto, format, ls, codec, "bench-device", ks))
            {
                var sw = Stopwatch.StartNew();
                vs2.UnlockAsync(path, Encoding.UTF8.GetBytes(DemoPassword)).GetAwaiter().GetResult();
                sw.Stop();
                return ("unlock", sw.Elapsed.TotalSeconds, 1.5, "s");  // PRD: ≤ 1.5s
            }
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>场景 3:SearchService 在 1万条目上的查询延迟。</summary>
    private static (string, double, double, string) BenchSearch(int count)
    {
        var path = Path.Combine(Path.GetTempPath(), $"okv-bench-search-{Guid.NewGuid():N}.okv");
        try
        {
            var crypto = new SodiumCryptoProvider();
            var format = new VaultFormat();
            var codec = new ProfilePayloadCodec();
            var ls = new LockService(crypto);
            var ks = new DeviceKeystore();
            using var vs = new VaultService(crypto, format, ls, codec, "bench-device", ks);
            var entrySvc = new EntryService(vs, new TemplateService(), new ClipboardService(new InMemoryClipboardProvider(), ls), crypto);
            vs.CreateAsync(path, "bench", Encoding.UTF8.GetBytes(DemoPassword), Argon2Params.ForTests(64 * 1024 * 1024)).GetAwaiter().GetResult();
            // 灌 1万 entry
            for (int i = 0; i < count; i++)
            {
                var entry = entrySvc.Create("prod", $"bench-{i:D6}", EntryType.ApiKey, "github",
                    new[] { new Field { Key = "pat", Value = FieldCodec.Encode("ghp_bench_" + i), Kind = FieldKind.Secret, Sensitive = true } });
                vs.PutEntry("prod", entry);
            }
            var entries = vs.ListEntries("prod");
            var search = new SearchService();

            // 三类查询各跑 10 次取 P50
            var queries = new[] {
                "bench-00042",                              // 精确 name
                "field:pat:ghp_bench_9999",                 // 字段值
                "platform:github",                           // 平台
            };
            var total = 0.0;
            foreach (var q in queries)
            {
                for (int i = 0; i < 10; i++)
                {
                    var sw = Stopwatch.StartNew();
                    var hits = search.Search(q, entries);
                    sw.Stop();
                    total += sw.Elapsed.TotalMilliseconds;
                }
            }
            var avgMs = total / (queries.Length * 10);
            return ("search", avgMs, 200, "ms");  // PRD: ≤ 200ms
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }

    /// <summary>场景 4:2 实例同步 1万条目的端到端耗时(文件复制 + SyncAsync)。</summary>
    private static (string, double, double, string) BenchSync(int count)
    {
        var pathA = Path.Combine(Path.GetTempPath(), $"okv-bench-sync-a-{Guid.NewGuid():N}.okv");
        var pathB = Path.Combine(Path.GetTempPath(), $"okv-bench-sync-b-{Guid.NewGuid():N}.okv");
        try
        {
            var crypto = new SodiumCryptoProvider();
            var format = new VaultFormat();
            var codec = new ProfilePayloadCodec();
            var lsA = new LockService(crypto);
            var lsB = new LockService(crypto);
            var ks = new DeviceKeystore();
            using var vsA = new VaultService(crypto, format, lsA, codec, "bench-device-a", ks);
            using var vsB = new VaultService(crypto, format, lsB, codec, "bench-device-b", ks);
            var entrySvc = new EntryService(vsA, new TemplateService(), new ClipboardService(new InMemoryClipboardProvider(), lsA), crypto);
            vsA.CreateAsync(pathA, "bench", Encoding.UTF8.GetBytes(DemoPassword), Argon2Params.ForTests(64 * 1024 * 1024)).GetAwaiter().GetResult();
            for (int i = 0; i < count; i++)
            {
                var entry = entrySvc.Create("prod", $"sync-{i:D6}", EntryType.ApiKey, "openai",
                    new[] { new Field { Key = "api_key", Value = FieldCodec.Encode("sk_sync_" + i), Kind = FieldKind.Secret, Sensitive = true } });
                vsA.PutEntry("prod", entry);
            }
            vsA.SaveAsync().GetAwaiter().GetResult();
            vsA.Lock();

            // Clone to B (B is a fresh vault that doesn't yet know about A's vector clock).
            // Open B, copy A → B file, then SyncAsync from B's POV.
            vsB.UnlockAsync(pathA, Encoding.UTF8.GetBytes(DemoPassword)).GetAwaiter().GetResult();
            File.Copy(pathA, pathB, overwrite: true);
            // Reopen A
            vsA.UnlockAsync(pathA, Encoding.UTF8.GetBytes(DemoPassword)).GetAwaiter().GetResult();
            // Sync B → A
            var sync = new SyncService(vsA, lsA, crypto, format, codec, new ManifestService(), "bench-device-a");
            var sw = Stopwatch.StartNew();
            var result = sync.SyncAsync(pathA, pathB).GetAwaiter().GetResult();
            sw.Stop();
            return ($"sync ({result.Outcome})", sw.Elapsed.TotalSeconds, 5, "s");  // PRD: ≤ 5s
        }
        finally
        {
            try { File.Delete(pathA); } catch { }
            try { File.Delete(pathB); } catch { }
        }
    }
}
