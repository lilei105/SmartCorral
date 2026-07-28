using System.Reflection;

namespace SmartCorral;

/// <summary>App-wide runtime info. Version is the assembly version, auto-incremented per build
/// (csproj AssemblyVersion 0.1.*) so each build is distinguishable.</summary>
public static class AppInfo
{
    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
}
