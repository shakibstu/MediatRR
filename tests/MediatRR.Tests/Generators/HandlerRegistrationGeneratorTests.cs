using System.Collections.Immutable;
using MediatRR.Contract.Messaging;
using MediatRR.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace MediatRR.Tests.Generators;

public class HandlerRegistrationGeneratorTests
{
    private const string Consumer = """
        using System.Collections.Generic;
        using System.Threading;
        using System.Threading.Tasks;
        using MediatRR.Contract.Messaging;

        namespace Consumer
        {
            public sealed class Req1 : IRequest<int> { }
            public sealed class Req2 : IRequest<int> { }
            public sealed class StreamReq : IStreamRequest<int> { }

            public sealed class PlainHandler : IRequestHandler<Req1, int>
            {
                public Task<int> Handle(Req1 r, CancellationToken ct) => Task.FromResult(1);
            }

            public sealed record RecordHandler : IRequestHandler<Req2, int>
            {
                public Task<int> Handle(Req2 r, CancellationToken ct) => Task.FromResult(2);
            }

            public abstract class AbstractHandler : IRequestHandler<Req1, int>
            {
                public abstract Task<int> Handle(Req1 r, CancellationToken ct);
            }

            public sealed class GenReq<T> : IRequest<T> { }

            public sealed class GenericHandler<T> : IRequestHandler<GenReq<T>, T>
            {
                public Task<T> Handle(GenReq<T> r, CancellationToken ct) => Task.FromResult(default(T));
            }

            public static class Outer
            {
                private sealed class Hidden : IRequestHandler<Req2, int>
                {
                    public Task<int> Handle(Req2 r, CancellationToken ct) => Task.FromResult(2);
                }
            }

            public sealed class Both : IRequestHandler<Req1, int>, IRequestHandler<Req2, int>
            {
                public Task<int> Handle(Req1 r, CancellationToken ct) => Task.FromResult(1);
                public Task<int> Handle(Req2 r, CancellationToken ct) => Task.FromResult(2);
            }

            public sealed class StreamHandler : IStreamRequestHandler<StreamReq, int>
            {
                public IAsyncEnumerable<int> Handle(StreamReq r, CancellationToken ct) => null;
            }
        }
        """;

    private static (string Generated, ImmutableArray<Diagnostic> Diagnostics) Run(string source)
    {
        var locations = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
            .Select(assembly => assembly.Location)
            .Append(typeof(IRequest<>).Assembly.Location)
            .Append(typeof(IServiceCollection).Assembly.Location)
            .Distinct()
            .Select(location => (MetadataReference)MetadataReference.CreateFromFile(location))
            .ToList();
        var compilation = CSharpCompilation.Create("Consumer",
            [CSharpSyntaxTree.ParseText(source)],
            locations,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new HandlerRegistrationGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);

        var generated = output.SyntaxTrees.Single(tree => tree.FilePath.EndsWith("MediatRR.ServiceGenerator.g.cs", StringComparison.Ordinal)).ToString();
        return (generated, output.GetDiagnostics());
    }

    [Fact]
    public void Generated_registrations_compile_in_the_consumer_compilation()
    {
        var (_, diagnostics) = Run(Consumer);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    [Fact]
    public void Registers_plain_record_and_multi_interface_handlers_and_skips_abstract_generic_and_hidden_types()
    {
        var (generated, _) = Run(Consumer);

        Assert.Contains("global::MediatRR.Contract.Messaging.IRequestHandler<global::Consumer.Req1, int>, global::Consumer.PlainHandler>(services);", generated);
        Assert.Contains("global::MediatRR.Contract.Messaging.IRequestHandler<global::Consumer.Req2, int>, global::Consumer.RecordHandler>(services);", generated);
        Assert.Contains("global::MediatRR.Contract.Messaging.IRequestHandler<global::Consumer.Req1, int>, global::Consumer.Both>(services);", generated);
        Assert.Contains("global::MediatRR.Contract.Messaging.IRequestHandler<global::Consumer.Req2, int>, global::Consumer.Both>(services);", generated);
        Assert.DoesNotContain("AbstractHandler", generated);
        Assert.DoesNotContain("GenericHandler", generated);
        Assert.DoesNotContain("Hidden", generated);
    }

    [Fact]
    public void Stream_handlers_are_registered_by_the_stream_method()
    {
        var (generated, _) = Run(Consumer);

        var streamMethod = generated[generated.IndexOf("AutoRegisterStreamHandlers", StringComparison.Ordinal)..];
        Assert.Contains("global::MediatRR.Contract.Messaging.IStreamRequestHandler<global::Consumer.StreamReq, int>, global::Consumer.StreamHandler>(services);", streamMethod);
        Assert.DoesNotContain("PlainHandler", streamMethod);
    }

    [Fact]
    public void Generated_code_parses_as_CSharp_7_3()
    {
        var (generated, _) = Run(Consumer);

        var tree = CSharpSyntaxTree.ParseText(generated, new CSharpParseOptions(LanguageVersion.CSharp7_3));

        Assert.Empty(tree.GetDiagnostics());
    }

    [Fact]
    public void Both_methods_exist_when_there_are_no_handlers()
    {
        var (generated, diagnostics) = Run("namespace Empty { public class Nothing { } }");

        Assert.Contains("AutoRegisterRequestHandlers(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)", generated);
        Assert.Contains("AutoRegisterStreamHandlers(this global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)", generated);
        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }
}
