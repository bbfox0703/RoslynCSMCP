using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    // ── Several applications, each with its own container ────────────────────

    private const string AddSharedRegistersIB = @"
public static class SharedRegistrations
{
    public static IServiceCollection AddShared(this IServiceCollection s)
    {
        s.AddScoped<IB, B>();
        return s;
    }
}";

    /// <summary>
    /// A library holding the DI stand-in and the service types; every application below references it.
    /// </summary>
    private static CSharpCompilation SharedLibrary(string code = "") =>
        WithoutRealDependencyInjection(InMemoryCompilation.Compile(
                new[] { DiStubs, "using Microsoft.Extensions.DependencyInjection;\n" + ServiceTypes + code },
                new[] { "Shared/Stubs.cs", "Shared/Services.cs" })
            .WithAssemblyName("Shared"));

    private static CSharpCompilation Project(string name, OutputKind outputKind, string code, params Compilation[] references)
    {
        var compilation = InMemoryCompilation.Compile(
            new[] { "using Microsoft.Extensions.DependencyInjection;\n" + code },
            new[] { $"{name}/Startup.cs" });

        return WithoutRealDependencyInjection(compilation
            .WithAssemblyName(name)
            .WithOptions(compilation.Options.WithOutputKind(outputKind))
            .AddReferences(references.Select(r => r.ToMetadataReference())));
    }

    private static CSharpCompilation Application(string name, string registrations, params Compilation[] references) =>
        Project(name, OutputKind.ConsoleApplication, Startup(registrations), references);

    private static string Startup(string registrations) =>
        "class Startup { void Configure(IServiceCollection s) {\n" + registrations + "\n} }";

    /// <summary>
    /// Drops the real Microsoft.Extensions.DependencyInjection assemblies, which the test host loads: within
    /// one compilation the stand-in's source wins, but across projects the two definitions would be ambiguous.
    /// </summary>
    private static CSharpCompilation WithoutRealDependencyInjection(CSharpCompilation compilation) =>
        compilation.RemoveReferences(compilation.References.Where(r =>
            Path.GetFileName(r.Display ?? string.Empty).StartsWith("Microsoft.Extensions.DependencyInjection", StringComparison.OrdinalIgnoreCase)));

    private static DIContainerResults AnalyzeProjects(params CSharpCompilation[] compilations)
    {
        foreach (var compilation in compilations)
        {
            // CS5001: the applications have no Main method, which only matters for emitting them.
            compilation.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error && d.Id != "CS5001")
                .Should().BeEmpty($"{compilation.AssemblyName} should compile");
        }

        var results = new DIContainerResults();
        Phase2AnalysisService.AnalyzeDIContainer(compilations, checkLifetimes: true, checkCircular: true, checkCaptive: true, results);
        return results;
    }

    [Fact]
    public void SeparateApplications_RegisteringSameServices_NoMultipleRegistration()
    {
        // Two executables building their own containers from the same registrations. With one
        // solution-wide registration map, every registration in the second was a duplicate.
        var shared = SharedLibrary();
        const string registrations = "s.AddSingleton<IA, A>(); s.AddSingleton<IB, B>();";

        var results = AnalyzeProjects(
            shared,
            Application("App1", registrations, shared),
            Application("App2", registrations, shared));

        results.Applications.Should().BeEquivalentTo(new[] { "App1", "App2" });
        results.AnalyzedServices.Should().Be(4);
        results.Issues.Should().BeEmpty();
    }

    [Fact]
    public void SeparateApplications_DifferentLifetimes_NoLifetimeMismatch()
    {
        // Merged, IB would be Singleton then Scoped, and Both registered as Singleton IA and Scoped IB.
        var shared = SharedLibrary();

        var results = AnalyzeProjects(
            shared,
            Application("App1", "s.AddSingleton<IB, B>(); s.AddSingleton<IA, Both>();", shared),
            Application("App2", "s.AddScoped<IB, Both>();", shared));

        results.LifetimeMismatchCount.Should().Be(0);
        results.Issues.Should().BeEmpty();
    }

    [Fact]
    public void SharedLibraryDuplicate_ReportedOnceNamingEachApplication()
    {
        var shared = SharedLibrary(@"
public static class SharedRegistrations
{
    public static IServiceCollection AddShared(this IServiceCollection s)
    {
        s.AddScoped<IB, B>();
        s.AddScoped<IB, B>();
        return s;
    }
}");

        var results = AnalyzeProjects(
            shared,
            Application("App1", "s.AddShared();", shared),
            Application("App2", "s.AddShared(); s.AddScoped<IA, A>();", shared));

        // The library is part of both applications rather than an application of its own.
        results.Applications.Should().BeEquivalentTo(new[] { "App1", "App2" });
        var issue = results.Issues.Should().ContainSingle().Which;
        issue.IssueType.Should().Be("MultipleRegistration");
        issue.FilePath.Should().Be("Shared/Services.cs");
        issue.Applications.Should().BeEquivalentTo(new[] { "App1", "App2" });
    }

    [Fact]
    public void ApplicationOverridingLibraryRegistration_ReportedForThatApplicationOnly()
    {
        // Library registrations come before the application's own, so App1's registration is the later one.
        var shared = SharedLibrary(AddSharedRegistersIB);

        var results = AnalyzeProjects(
            shared,
            Application("App1", "s.AddShared(); s.AddSingleton<IB, B>(); s.AddSingleton<IA, Both>();", shared),
            Application("App2", "s.AddShared();", shared));

        var issue = results.Issues.Should().ContainSingle().Which;
        issue.IssueType.Should().Be("LifetimeMismatch");
        issue.FilePath.Should().Be("App1/Startup.cs");
        issue.Applications.Should().Equal("App1");
    }

    [Fact]
    public void Captive_CheckedAgainstEachApplicationsLifetimes()
    {
        // Singleton IA -> A(IB) with Scoped IB, in App1 only. In one merged map App2's registrations
        // came last (IA Scoped, IB Singleton) and hid it.
        var shared = SharedLibrary();

        var results = AnalyzeProjects(
            shared,
            Application("App1", "s.AddSingleton<IA, A>(); s.AddScoped<IB, B>();", shared),
            Application("App2", "s.AddScoped<IA, A>(); s.AddSingleton<IB, B>();", shared));

        results.CaptiveDependencyCount.Should().Be(1);
        var issue = results.Issues.Should().ContainSingle().Which;
        issue.IssueType.Should().Be("CaptiveDependency");
        issue.ServiceType.Should().Be("IA");
        issue.Applications.Should().Equal("App1");
    }

    [Fact]
    public void Circular_ReportedForTheApplicationThatFormsIt()
    {
        var shared = SharedLibrary();

        var results = AnalyzeProjects(
            shared,
            Application("App1", "s.AddScoped<IA, A>(); s.AddScoped<IB, Loop>();", shared),
            Application("App2", "s.AddScoped<IA, A>(); s.AddScoped<IB, B>();", shared));

        results.CircularDependencyCount.Should().Be(1);
        var issue = results.Issues.Should().ContainSingle().Which;
        issue.DependencyChain.Should().Equal("IA", "IB");
        issue.Applications.Should().Equal("App1");
    }

    [Fact]
    public void Circular_NotFormedFromRegistrationsInDifferentApplications()
    {
        // IA -> A(IB) is registered by App1 and IB -> Loop(IA) by App2; no container has both.
        var shared = SharedLibrary();

        var results = AnalyzeProjects(
            shared,
            Application("App1", "s.AddScoped<IA, A>(); s.AddScoped<IB, B>();", shared),
            Application("App2", "s.AddScoped<IB, Loop>();", shared));

        results.CircularDependencyCount.Should().Be(0);

        // App2 creates Loop but does not register IA.
        var issue = results.Issues.Should().ContainSingle().Which;
        issue.IssueType.Should().Be("UnregisteredDependency");
        issue.ImplementationType.Should().Be("Loop");
        issue.ServiceType.Should().Be("IA");
        issue.Applications.Should().Equal("App2");
    }

    [Fact]
    public void Unregistered_RegisteredClassCheckedAgainstApplicationsThatRegisterIt()
    {
        // A (in the shared library) is created by App1, which lacks IB; App2 registering IB does not help.
        // Loop is created by neither, and App1 registers the IA it needs, so it is not reported.
        var shared = SharedLibrary();

        var results = AnalyzeProjects(
            shared,
            Application("App1", "s.AddSingleton<IA, A>();", shared),
            Application("App2", "s.AddSingleton<IB, B>();", shared));

        results.UnregisteredCount.Should().Be(1);
        var issue = results.Issues.Should().ContainSingle().Which;
        issue.ImplementationType.Should().Be("A");
        issue.ServiceType.Should().Be("IB");
        issue.Applications.Should().Equal("App1");
        issue.Description.Should().Contain("'App1'");
    }

    [Fact]
    public void Unregistered_ClassInOneApplication_NotSatisfiedByAnother()
    {
        var shared = SharedLibrary();

        var results = AnalyzeProjects(
            shared,
            Project("App1", OutputKind.ConsoleApplication,
                "public class Worker { public Worker(IA a) { } }\n" + Startup("s.AddSingleton<Worker>();"), shared),
            Application("App2", "s.AddSingleton<IA, Both>(); s.AddSingleton<IB, B>();", shared));

        var issue = results.Issues.Should().ContainSingle().Which;
        issue.IssueType.Should().Be("UnregisteredDependency");
        issue.ImplementationType.Should().Be("Worker");
        issue.FilePath.Should().Be("App1/Startup.cs");
        issue.Applications.Should().Equal("App1");
    }

    [Fact]
    public void Unregistered_UnregisteredClass_ReportedWhenNoApplicationRegistersTheType()
    {
        // Loop(IA) is not registered anywhere, and no application that includes it registers IA.
        var shared = SharedLibrary();

        var results = AnalyzeProjects(
            shared,
            Application("App1", "s.AddSingleton<IB, B>();", shared),
            Application("App2", "s.AddScoped<IB, B>();", shared));

        var issue = results.Issues.Should().ContainSingle().Which;
        issue.IssueType.Should().Be("UnregisteredDependency");
        issue.ImplementationType.Should().Be("Loop");
        issue.Applications.Should().BeEquivalentTo(new[] { "App1", "App2" });
    }

    [Fact]
    public void TestProjectReferencingApplication_KeepsItsOwnContainer()
    {
        // A test project is itself an executable; the application it references builds a separate
        // container, so registering the same services in both is not a duplicate.
        var shared = SharedLibrary();
        const string registrations = "s.AddSingleton<IA, A>(); s.AddSingleton<IB, B>();";
        var app = Application("App", registrations, shared);
        var tests = Application("App.Tests", registrations, shared, app);

        var results = AnalyzeProjects(shared, app, tests);

        results.Applications.Should().BeEquivalentTo(new[] { "App", "App.Tests" });
        results.Issues.Should().BeEmpty();
    }

    [Fact]
    public void LibraryOnlySolution_TopLevelLibraryIncludesReferencedRegistrations()
    {
        // Without executables, the library that registers services and is not referenced by another
        // such library is the application, and the libraries it references are part of it.
        var shared = SharedLibrary(AddSharedRegistersIB);
        var plugin = Project("Plugin", OutputKind.DynamicallyLinkedLibrary,
            Startup("s.AddShared(); s.AddScoped<IB, B>(); s.AddScoped<IA, A>();"), shared);

        var results = AnalyzeProjects(shared, plugin);

        results.Applications.Should().Equal("Plugin");
        var issue = results.Issues.Should().ContainSingle().Which;
        issue.IssueType.Should().Be("MultipleRegistration");
        issue.FilePath.Should().Be("Plugin/Startup.cs");
    }
}
