using AutoCode.Map.Diagnostics;
using AutoCode.Map.Helpers;
using AutoCode.Model.AutoMapperModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;

namespace AutoCode.Map
{
    [Generator]
    public class MapperGenerator : IIncrementalGenerator
    {
        public static readonly string MapperAttributeName = typeof(MapperAttribute).FullName!;
        //public static readonly string MapperDefaultsAttributeName = typeof(MapperDefaultsAttribute).FullName!;
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
#if DEBUG_SOURCE_GENERATOR
        DebuggerUtil.AttachDebugger();
#endif
            IncrementalValuesProvider<Diagnostic> compilationDiagnostics = 
                context.CompilationProvider.SelectMany((compilation, _) => 
                        BuildCompilationDiagnostics(compilation));

            context.ReportDiagnostics(compilationDiagnostics);
        }

        private static IEnumerable<Diagnostic> BuildCompilationDiagnostics(Compilation compilation)
        {
            if (compilation is CSharpCompilation {   LanguageVersion: < LanguageVersion.CSharp9   } cSharpCompilation)
            {
                yield return Diagnostic.Create(DiagnosticDescriptors.LanguageVersionNotSupported,
                    null,
                    cSharpCompilation.LanguageVersion.ToDisplayString(),
                    LanguageVersion.CSharp9.ToDisplayString()
                );
            }
        }
    }
}
