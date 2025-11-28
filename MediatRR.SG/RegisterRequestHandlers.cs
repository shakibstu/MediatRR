using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;
using System.Text;

namespace MediatRR.SG
{
    [Generator]
    public class RegisterRequestHandlers : IIncrementalGenerator
    {
        private const string RequestHandlerInterface = "MediatRR.Contract.Messaging.IRequestHandler<,>";
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var template = new StringBuilder(@"
                             namespace MediatRR.ServiceGenerator;
                             using Microsoft.Extensions.DependencyInjection;
                             internal static partial class SourceGeneratorInjectDependencies{{
                                internal static IServiceCollection AutoRegisterRequestHandlers(this IServiceCollection services) =>
                                    services
                                        .RegisterRequestHandlers();

                                private static partial IServiceCollection RegisterRequestHandlers(this IServiceCollection services);
                             }}
                             internal static partial class SourceGeneratorInjectDependencies
                             {{
                                    private static partial IServiceCollection RegisterRequestHandlers(this IServiceCollection services)
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
                ctx.AddSource($"RegisterRequestHandlers.cs", injected);
            });
        }

        private static string InheritableInjections(SemanticModel semanticModel, ClassDeclarationSyntax classDeclaration)
        {
            // Get the symbol for the current class
            if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
                return "";

            // Check interfaces of class
            if (!classSymbol.AllInterfaces.Any(a => GetType(a, false) == RequestHandlerInterface))
                return "";
            var handler = classSymbol.AllInterfaces.First(a => GetType(a, false) == RequestHandlerInterface);
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

            if (!symbol.TypeArguments.All(a => a.TypeKind is TypeKind.Class or TypeKind.Interface))
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
