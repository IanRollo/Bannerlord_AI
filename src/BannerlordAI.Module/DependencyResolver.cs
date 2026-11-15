using System;
using System.IO;
using System.Linq;
using System.Reflection;

namespace BannerlordAI.Module;

/// <summary>
/// .NET Framework has no per-mod binding redirects. If the CLR can't bind one of our dependencies at the exact
/// version we were compiled against (e.g. another mod or the game loaded a different System.Memory), it raises
/// AssemblyResolve; we answer with an already-loaded copy of that assembly, or the one shipped in our bin folder.
/// </summary>
internal static class DependencyResolver
{
    private static readonly string[] Shipped =
    {
        "Anthropic", "BannerlordAI.Core", "Microsoft.Bcl.AsyncInterfaces", "Microsoft.Extensions.AI.Abstractions",
        "System.Buffers", "System.Collections.Immutable", "System.Diagnostics.DiagnosticSource", "System.IO.Pipelines",
        "System.Memory", "System.Net.ServerSentEvents", "System.Numerics.Vectors", "System.Runtime.CompilerServices.Unsafe",
        "System.Text.Encodings.Web", "System.Text.Json", "System.Threading.Tasks.Extensions",
    };

    private static bool _installed;

    public static void Install()
    {
        if (_installed) return;
        _installed = true;
        AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
    }

    private static Assembly? OnResolve(object sender, ResolveEventArgs args)
    {
        try
        {
            var name = new AssemblyName(args.Name).Name;
            if (!Shipped.Contains(name, StringComparer.OrdinalIgnoreCase)) return null;

            // Prefer whatever copy is already loaded (any version) to avoid two copies of the same types.
            var loaded = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.GetName().Version)
                .FirstOrDefault();
            if (loaded != null) return loaded;

            var dir = Path.GetDirectoryName(typeof(DependencyResolver).Assembly.Location);
            if (dir == null) return null;
            var path = Path.Combine(dir, name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }
        catch
        {
            return null;
        }
    }
}
