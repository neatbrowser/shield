using NeatShield.Core;
using NeatShield.Core.Filters;
using NeatShield.Core.Proxy;
using NeatShield.Core.Runtime;

// neatshield <command>
//   run [--no-system-proxy]   start protection and keep running until Ctrl+C
//                  (--no-system-proxy: only listen on the port; point a program at it yourself)
//   status         show filter version and certificate state
//   update         download the newest filters
//   check <url> [type] [source]   ask the filter engine about one request (no proxy needed)
//   selftest [--no-system-proxy]   turn protection on, check that it really works, turn it off (exit code 1 when a check fails)
//   diag           print a report to copy when something does not work
//   trust-ca       install the root certificate for this user
//   untrust-ca     remove it
//   reset-proxy    put the Windows proxy setting back (use this if the internet stops working after a crash)

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var data = Environment.GetEnvironmentVariable("NEATSHIELD_DATA");

if (command == "reset-proxy")
{
    // Needs nothing but the data folder: no engine, no proxy.
    var paths = new AppPaths(data);
    var sys = new SystemProxy(paths.ProxyBackupFile);
    if (sys.IsOurs())
    {
        sys.Restore();
        Console.WriteLine("Windows proxy setting restored.");
    }
    else
    {
        Console.WriteLine("The Windows proxy setting is not NeatShield's; nothing changed.");
        if (File.Exists(paths.ProxyBackupFile)) File.Delete(paths.ProxyBackupFile);
    }

    return 0;
}

if (command is "help" or "-h" or "--help")
{
    Console.WriteLine("neatshield run [--no-system-proxy] | status | update | check <url> [type] [source] | selftest | diag | trust-ca | untrust-ca | reset-proxy");
    return 0;
}

using var runtime = new ShieldRuntime(data);
runtime.UpdateProgress += (_, m) => Console.WriteLine("  " + m);

switch (command)
{
    case "status":
    {
        var best = runtime.Filters.BestDir;
        var manifest = best is null ? null : runtime.Filters.ReadManifest(best);
        Console.WriteLine($"Filters : {manifest?.Version ?? "none"}{(best is null ? "" : "  (" + best + ")")}");
        var c = runtime.Certificate.GetStatus();
        if (!c.Exists)
        {
            try { runtime.Certificate.EnsureCreated(); c = runtime.Certificate.GetStatus(); } catch (Exception ex) { Console.WriteLine("Certificate: " + ex.Message); }
        }

        Console.WriteLine($"Cert    : {(c.Exists ? (c.Trusted ? "trusted" : "NOT trusted") : "missing")}  expires {c.Expires:yyyy-MM-dd}");
        Console.WriteLine($"Proxy   : {(runtime.SystemProxy.IsOurs() ? "Windows proxy is set to NeatShield" : "Windows proxy is not NeatShield's")}");
        return 0;
    }

    case "update":
    {
        var r = await runtime.UpdateFiltersAsync();
        Console.WriteLine($"{r.Status} {r.Version} {r.Error}");
        return r.Status == UpdateStatus.Failed ? 1 : 0;
    }

    case "check":
    {
        if (args.Length < 2) { Console.Error.WriteLine("usage: neatshield check <url> [type] [source]"); return 2; }
        var dir = runtime.Filters.BestDir;
        if (dir is null) { Console.Error.WriteLine("No filters yet. Run: neatshield update"); return 1; }
        await runtime.Engine.LoadAsync(dir);
        var url = args[1];
        var type = args.Length > 2 ? args[2] : "other";
        var source = args.Length > 3 ? args[3] : url;
        var res = runtime.Engine.Current!.Check(url, source, type, "GET");
        Console.WriteLine($"{res.Action}{(res.Redirect is null ? "" : "  redirect=" + res.Redirect[..Math.Min(40, res.Redirect.Length)])}{(res.RewrittenUrl is null ? "" : "  rewritten=" + res.RewrittenUrl)}");
        return 0;
    }

    case "diag":
    {
        if (runtime.Filters.BestDir is { } dir0 && runtime.Engine.Current is null) await runtime.Engine.LoadAsync(dir0);
        Console.WriteLine(Diagnostics.Report(runtime));
        return 0;
    }

    case "selftest":
    {
        if (args.Skip(1).Any(a => a == "--no-system-proxy")) runtime.SystemProxy.Disabled = true;
        await runtime.StartAsync();
        await runtime.SetProtectionAsync(true);
        var steps = await SelfTest.RunAsync(runtime);
        foreach (var step in steps) Console.WriteLine($"{(step.Ok ? "ok  " : "FAIL")} {step.Id,-8} {step.Detail}");
        runtime.Shutdown();
        return steps.All(x => x.Ok) ? 0 : 1;
    }

    case "trust-ca":
        runtime.Certificate.Trust();
        Console.WriteLine(runtime.Certificate.GetStatus().Trusted ? "Certificate is trusted." : "Certificate was not trusted.");
        return 0;

    case "untrust-ca":
        runtime.Certificate.EnsureCreated();
        runtime.Certificate.Untrust();
        Console.WriteLine("Certificate removed from the trusted list.");
        return 0;

    case "run":
    {
        if (args.Skip(1).Any(a => a == "--no-system-proxy")) runtime.SystemProxy.Disabled = true;
        await runtime.StartAsync();
        await runtime.SetProtectionAsync(true);
        if (runtime.State != ProtectionState.On)
        {
            Console.Error.WriteLine("Could not start: " + runtime.Error);
            return 1;
        }

        Console.WriteLine($"Protection is ON (127.0.0.1:{runtime.Proxy.Port}). Press Ctrl+C to stop.");
        var done = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.TrySetResult(); };
        await done.Task;
        runtime.Shutdown();
        Console.WriteLine(runtime.SystemProxy.Disabled ? "Stopped." : "Stopped. Windows proxy setting restored.");
        return 0;
    }

    default:
        Console.Error.WriteLine("Unknown command. Try: neatshield help");
        return 2;
}
