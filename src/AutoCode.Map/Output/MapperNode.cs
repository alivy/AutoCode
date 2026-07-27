using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
namespace AutoCode.Map.Output;

public readonly record struct MapperNode(string FileName, CompilationUnitSyntax Body)
{
    public bool Equals(MapperNode other)
    {
        return string.Equals(FileName, other.FileName, StringComparison.Ordinal) && Body.IsEquivalentTo(other.Body);
    }

    public override int GetHashCode() => HashCode.Combine(FileName, Body.SyntaxTree.Length);
}


