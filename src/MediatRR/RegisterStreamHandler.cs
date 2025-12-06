using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Text;

namespace MediatRR
{
    [Generator]
    public class RegisterStreamHandlers : IIncrementalGenerator
    {
        private const string StreamHandlerInterface = "MediatRR.Contract.Messaging.IStreamRequestHandler<,>";
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
//#if DEBUG
//            Debugger.Launch();
//#endif
            var template = new StringBuilder(@"
                             namespace MediatRR.ServiceGenerator;
                             using Microsoft.Extensions.DependencyInjection;
                             internal static partial class SourceGeneratorInjectDependencies{{
                                internal static IServiceCollection AutoRegisterStreamHandlers(this IServiceCollection services) =>
                                    services
                                        .RegisterStreamHandlers();

                                private static partial IServiceCollection RegisterStreamHandlers(this IServiceCollection services);
                             }}
                             internal static partial class SourceGeneratorInjectDependencies
                             {{
                                    private static partial IServiceCollection RegisterStreamHandlers(this IServiceCollection services)
                                             {{ 
                                                {0}
                                                return services;
                                             }}
                             }}
            ");

            var classDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (node, _) => IsClassDeclaration(node),
                    transform: static (context, _) => GetClassDeclarationSyntax(context));

            var classDeclarationsWithSemanticModel = classDeclarations
                .Select((syntax, _) =>
                {
                    var semanticModel = syntax.SemanticModel;
                    var classDeclaration = syntax.ClassSyntax;
                    var injection = InheritableInjections(semanticModel, classDeclaration);
                    return injection;

                }).Collect();
            context.RegisterSourceOutput(classDeclarationsWithSemanticModel, (ctx, injections) =>
            {
                var usableOne = injections.Where(a => !string.IsNullOrEmpty(a));
                var injected = string.Format(template.ToString(), string.Join("\n", usableOne));
                ctx.AddSource($"RegisterStreamHandlers.cs", injected);
            });
        }

        private static string InheritableInjections(SemanticModel semanticModel, ClassDeclarationSyntax classDeclaration)
        {
            // Get the symbol for the current class
            if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
                return "";

            // Check interfaces of class
            if (!classSymbol.AllInterfaces.Any(a => GetType(a, false) == StreamHandlerInterface))
                return "";
            var handler = classSymbol.AllInterfaces.First(a => GetType(a, false) == StreamHandlerInterface);
            return $"services.AddTransient<{GetType(handler, true)},{GetType(classSymbol, true)}>();";
        }


        private static string GetType(INamedTypeSymbol symbol, bool withSpecificationOfGeneric)
        {
            if (!symbol.IsGenericType)
            {
                return symbol.OriginalDefinition.ToString();
            }

            if (!symbol.TypeArguments.Any() || !withSpecificationOfGeneric)
            {
                return symbol.ConstructUnboundGenericType().ToString();
            }

            return symbol.ToDisplayString();
        }
        private static bool IsClassDeclaration(SyntaxNode node)
        {
            return node is ClassDeclarationSyntax;
        }
        private static (SemanticModel SemanticModel, ClassDeclarationSyntax ClassSyntax) GetClassDeclarationSyntax(GeneratorSyntaxContext context)
        {
            var classDeclaration = (ClassDeclarationSyntax)context.Node;
            return (context.SemanticModel, classDeclaration);
        }
    }
}
