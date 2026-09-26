using FluentAssertions;
using RoslynMcpServer.Core.Models;
using RoslynMcpServer.Core.Services;
using RoslynMcpServer.Tests.Helpers;

namespace RoslynMcpServer.Tests.Unit.Services;

/// <summary>
/// Unit tests for AnalyzeDIContainer (Phase2AnalysisService), run against in-memory compilations
/// with a minimal stand-in for Microsoft.Extensions.DependencyInjection.
/// </summary>
public class DIContainerAnalysisTests
{
    private const string DiStubs = @"
namespace Microsoft.Extensions.DependencyInjection
{
    public interface IServiceCollection { }
    public static class ServiceCollectionServiceExtensions
    {
        public static IServiceCollection AddSingleton<TService>(this IServiceCollection s) where TService : class => s;
        public static IServiceCollection AddSingleton<TService, TImpl>(this IServiceCollection s) where TService : class where TImpl : class, TService => s;
        public static IServiceCollection AddScoped<TService>(this IServiceCollection s) where TService : class => s;
        public static IServiceCollection AddScoped<TService, TImpl>(this IServiceCollection s) where TService : class where TImpl : class, TService => s;
        public static IServiceCollection AddTransient<TService>(this IServiceCollection s) where TService : class => s;
        public static IServiceCollection AddTransient<TService, TImpl>(this IServiceCollection s) where TService : class where TImpl : class, TService => s;
    }
}";

    private static DIContainerResults Analyze(
        string code,
        bool checkLifetimes = true,
        bool checkCircular = true,
        bool checkCaptive = true)
    {
        var compilation = InMemoryCompilation.Compile(
            new[] { DiStubs, "using Microsoft.Extensions.DependencyInjection;\n" + code },
            new[] { "Stubs.cs", "App.cs" });

        var results = new DIContainerResults();
        Phase2AnalysisService.AnalyzeDIContainer(new[] { compilation }, checkLifetimes, checkCircular, checkCaptive, results);
        return results;
    }

    private const string ServiceTypes = @"
public interface IA { } public interface IB { }
public class A : IA { public A(IB b) { } }
public class B : IB { }
public class Loop : IB { public Loop(IA a) { } }
public class Both : IA, IB { }
";

    // ── Captive dependencies through interface registrations ─────────────────

    [Fact]
    public void Captive_RegisteredThroughInterface_Reported()
    {
        // Singleton IA -> A depends on Scoped IB. Previously only classes registered under their
        // own concrete type were checked, so this was missed.
        var results = Analyze(ServiceTypes + @"
class Startup { void Configure(IServiceCollection s) {
    s.AddSingleton<IA, A>();
    s.AddScoped<IB, B>();
} }");

        results.CaptiveDependencyCount.Should().Be(1);
        results.Issues.Should().ContainSingle(i =>
            i.IssueType == "CaptiveDependency" && i.ServiceType == "IA" && i.ImplementationType == "A");
    }

    [Fact]
    public void Captive_CheckDisabled_NotReported()
    {
        var results = Analyze(ServiceTypes + @"
class Startup { void Configure(IServiceCollection s) {
    s.AddSingleton<IA, A>();
    s.AddScoped<IB, B>();
} }", checkCaptive: false);

        results.CaptiveDependencyCount.Should().Be(0);
        results.Issues.Should().NotContain(i => i.IssueType == "CaptiveDependency");
    }

    // ── Circular dependencies through interface registrations ────────────────

    [Fact]
    public void Circular_RegisteredThroughInterfaces_Reported()
    {
        // IA -> A(IB) and IB -> Loop(IA): a cycle only visible by following the registrations.
        var results = Analyze(ServiceTypes + @"
class Startup { void Configure(IServiceCollection s) {
    s.AddScoped<IA, A>();
    s.AddScoped<IB, Loop>();
} }");

        results.CircularDependencyCount.Should().Be(1);
        results.Issues.Should().ContainSingle(i => i.IssueType == "CircularDependency")
            .Which.DependencyChain.Should().BeEquivalentTo(new[] { "IA", "IB" });
    }

    [Fact]
    public void Circular_CheckDisabled_NotReported()
    {
        var results = Analyze(ServiceTypes + @"
class Startup { void Configure(IServiceCollection s) {
    s.AddScoped<IA, A>();
    s.AddScoped<IB, Loop>();
} }", checkCircular: false);

        results.CircularDependencyCount.Should().Be(0);
    }

    // ── Lifetime mismatches (checkLifetimes) ─────────────────────────────────

    [Fact]
    public void LifetimeMismatch_ServiceReRegisteredWithOtherLifetime_Counted()
    {
        var results = Analyze(ServiceTypes + @"
class Startup { void Configure(IServiceCollection s) {
    s.AddSingleton<IB, B>();
    s.AddScoped<IB, B>();
} }");

        results.LifetimeMismatchCount.Should().Be(1);
        results.Issues.Should().ContainSingle(i => i.IssueType == "LifetimeMismatch" && i.Severity == "Medium");
        results.Issues.Should().NotContain(i => i.IssueType == "MultipleRegistration");
    }

    [Fact]
    public void LifetimeMismatch_ImplementationUnderServicesWithDifferentLifetimes_Counted()
    {
        var results = Analyze(ServiceTypes + @"
class Startup { void Configure(IServiceCollection s) {
    s.AddSingleton<IA, Both>();
    s.AddScoped<IB, Both>();
} }");

        results.LifetimeMismatchCount.Should().Be(1);
        results.Issues.Should().ContainSingle(i => i.IssueType == "LifetimeMismatch" && i.ImplementationType == "Both");
    }

    [Fact]
    public void LifetimeMismatch_CheckDisabled_FallsBackToMultipleRegistration()
    {
        // checkLifetimes used to be accepted and never read.
        var results = Analyze(ServiceTypes + @"
class Startup { void Configure(IServiceCollection s) {
    s.AddSingleton<IB, B>();
    s.AddScoped<IB, B>();
    s.AddSingleton<IA, Both>();
} }", checkLifetimes: false);

        results.LifetimeMismatchCount.Should().Be(0);
        results.Issues.Should().NotContain(i => i.IssueType == "LifetimeMismatch");
        results.Issues.Should().ContainSingle(i => i.IssueType == "MultipleRegistration");
    }

    [Fact]
    public void SameLifetimeReRegistration_IsMultipleRegistrationOnly()
    {
        var results = Analyze(ServiceTypes + @"
class Startup { void Configure(IServiceCollection s) {
    s.AddScoped<IB, B>();
    s.AddScoped<IB, B>();
} }");

        results.LifetimeMismatchCount.Should().Be(0);
        results.Issues.Should().ContainSingle(i => i.IssueType == "MultipleRegistration");
    }
}
