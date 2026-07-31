using AutoCode.Map.Helpers;
using AutoCode.Map.Output;
using Microsoft.CodeAnalysis;


namespace Riok.Mapperly.Output;

public readonly record struct MapperAndDiagnostics(MapperNode Mapper, ImmutableEquatableArray<Diagnostic> Diagnostics);
