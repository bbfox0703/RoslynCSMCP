using System.ComponentModel;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace RoslynMcpServer.Tests.Integration;

/// <summary>
/// Verifies that each split MCP server registers every service its tools inject, along with
/// those services' own constructor dependencies. The MCP SDK resolves tool parameters from DI
/// only when a tool is invoked, so a missing registration otherwise goes unnoticed until then.
/// </summary>
public class SplitServerServiceRegistrationTests
{
    private static readonly Dictionary<string, Action<IServiceCollection>> Servers = new()
    {
        ["Navigation"] = RoslynMcpServer.Navigation.Program.ConfigureServices,
        ["Quality"] = RoslynMcpServer.Quality.Program.ConfigureServices,
        ["Security"] = RoslynMcpServer.Security.Program.ConfigureServices,
        ["Dependencies"] = RoslynMcpServer.Dependencies.Program.ConfigureServices,
        ["Refactoring"] = RoslynMcpServer.Refactoring.Program.ConfigureServices,
        ["Testing"] = RoslynMcpServer.Testing.Program.ConfigureServices,
        ["Metrics"] = RoslynMcpServer.Metrics.Program.ConfigureServices,
        ["Advanced"] = RoslynMcpServer.Advanced.Program.ConfigureServices,
        ["Interop"] = RoslynMcpServer.Interop.Program.ConfigureServices,
    };

    public static TheoryData<string> ServerNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Servers.Keys)
                data.Add(name);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(ServerNames))]
    public void EveryToolInjectedService_IsRegisteredAndResolves(string server)
    {
        var configure = Servers[server];
        var injected = GetInjectedToolParameters(configure.Method.DeclaringType!.Assembly);
        injected.Should().NotBeEmpty("every split server has tools that take injected services");

        using var provider = CreateServices(configure).BuildServiceProvider();
        var isService = provider.GetRequiredService<IServiceProviderIsService>();

        var failures = new List<string>();
        foreach (var (tool, parameter) in injected)
        {
            var type = parameter.ParameterType;
            if (!isService.IsService(type))
            {
                // The SDK would expose an unregistered type as a JSON argument instead of injecting it
                failures.Add($"{tool.Name}({parameter.Name}): {type.Name} is not registered");
                continue;
            }

            try
            {
                provider.GetRequiredService(type);
            }
            catch (Exception ex)
            {
                failures.Add($"{tool.Name}({parameter.Name}): {type.Name} cannot be resolved: {ex.Message}");
            }
        }

        failures.Should().BeEmpty($"{server} server tools must be able to resolve all injected services");
    }

    [Theory]
    [MemberData(nameof(ServerNames))]
    public void AllRegisteredServices_HaveResolvableDependencies(string server)
    {
        var services = CreateServices(Servers[server]);

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }).Dispose();

        act.Should().NotThrow();
    }

    private static ServiceCollection CreateServices(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging(); // Provided by the host in the real servers
        configure(services);
        return services;
    }

    /// <summary>
    /// Finds tool parameters the SDK is expected to inject: tool arguments always carry a
    /// [Description], and CancellationToken is bound by the SDK itself.
    /// </summary>
    private static List<(MethodInfo Tool, ParameterInfo Parameter)> GetInjectedToolParameters(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() != null)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
            .SelectMany(m => m.GetParameters().Select(p => (Tool: m, Parameter: p)))
            .Where(x => x.Parameter.GetCustomAttribute<DescriptionAttribute>() == null
                        && x.Parameter.ParameterType != typeof(CancellationToken))
            .ToList();
    }
}
