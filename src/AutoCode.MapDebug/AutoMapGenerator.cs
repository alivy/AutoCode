using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace AutoCode.MapDebug
{
    [Generator(LanguageNames.CSharp)]
    public class AutoMapGenerator : IIncrementalGenerator
    {

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            //    if (!Debugger.IsAttached)
            //    {
            //        Debugger.Launch();
            //    }
            //Debugger.Launch();

            // 注册语法接收器
            IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: (s, _) => s is ClassDeclarationSyntax, // 只考虑类声明
                transform: (ctx, _) => (ClassDeclarationSyntax)ctx.Node // 选择类声明
            ).Where(classDecl => classDecl != null);

            // 组合编译和类声明
            IncrementalValueProvider<(Compilation Left, ImmutableArray<ClassDeclarationSyntax> Right)> compilationAndClasses =
                context.CompilationProvider.Combine(classDeclarations.Collect());

            // 注册源输出
            context.RegisterSourceOutput(compilationAndClasses, (ctx, source) =>
            {
                var (compilation, classDecls) = source;

                foreach (var classDecl in classDecls)
                {
                    var semanticModel = compilation.GetSemanticModel(classDecl.SyntaxTree);
                    var classSymbol = semanticModel.GetDeclaredSymbol(classDecl) as INamedTypeSymbol;

                    if (classSymbol != null)
                    {
                        // Generate the mapping code
                        var generatedCode = GenerateMapping.GenerateMappingCode(classSymbol, compilation);
                        ctx.AddSource($"{classSymbol.Name}_AutoMap.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
                        // File.WriteAllText($"F:\\AmiaoCode\\auto-code\\src\\APP.Map\\{classSymbol.Name}_AutoMap.cs", generatedCode);
                    }
                }
            });
        }
    }
}
