using AutoCode.Engine.CodeBuilder;
using Xunit;

namespace AutoCode.Tests.V2
{
    /// <summary>
    /// CodeBuilder 核心引擎测试
    /// </summary>
    public class CodeBuilderTests
    {
        [Fact]
        public void CodeWriter_GeneratesFileScopedNamespace()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("MyApp.Services");
            writer.Class("UserService", c => c.Public());

            var result = writer.Build();

            Assert.Contains("namespace MyApp.Services;", result);
            Assert.Contains("public class UserService", result);
        }

        [Fact]
        public void CodeWriter_CollectsUsings_AndSortsSystemFirst()
        {
            var writer = new CodeWriter();
            writer.Using("MyApp.Models", "System", "System.Collections.Generic");
            writer.FileScopedNamespace("Test");
            writer.Class("Foo", c => c.Public());

            var result = writer.Build();

            var systemIdx = result.IndexOf("using System;");
            var genericIdx = result.IndexOf("using System.Collections.Generic;");
            var appIdx = result.IndexOf("using MyApp.Models;");

            Assert.True(systemIdx < genericIdx, "System should come before System.Collections");
            Assert.True(genericIdx < appIdx, "System.* should come before MyApp.*");
        }

        [Fact]
        public void CodeWriter_NullableEnable_IsIncluded()
        {
            var writer = new CodeWriter();
            writer.Nullable(true);
            writer.FileScopedNamespace("Test");

            var result = writer.Build();
            Assert.Contains("#nullable enable", result);
        }

        [Fact]
        public void ClassBuilder_GeneratesInheritance()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Dog", c => c
                .Public()
                .Inherits("Animal")
                .Implements("IBarkable", "INameable"));

            var result = writer.Build();
            Assert.Contains("public class Dog : Animal, IBarkable, INameable", result);
        }

        [Fact]
        public void ClassBuilder_StaticAbstractSealed()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Helper", c => c.Public().Static());

            var result = writer.Build();
            Assert.Contains("public static class Helper", result);
        }

        [Fact]
        public void ClassBuilder_GenericClassWithConstraints()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Repository", c => c
                .Public()
                .TypeParameter("T")
                .Constraint("where T : class, new()"));

            var result = writer.Build();
            Assert.Contains("public class Repository<T> where T : class, new()", result);
        }

        [Fact]
        public void MethodBuilder_AsyncMethodWithBody()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Svc", c => c
                .Public()
                .Method("GetData", m => m
                    .Public().Async()
                    .Returns("Task<string>")
                    .Parameter("int", "id")
                    .Body(b => b
                        .Var("result", "await _repo.Get(id)")
                        .Return("result"))));

            var result = writer.Build();
            Assert.Contains("public async Task<string> GetData(int id)", result);
            Assert.Contains("var result = await _repo.Get(id);", result);
            Assert.Contains("return result;", result);
        }

        [Fact]
        public void MethodBuilder_ExpressionBody()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Calc", c => c
                .Public()
                .Method("Add", m => m
                    .Public()
                    .Parameter("int", "a")
                    .Parameter("int", "b")
                    .Returns("int")
                    .ExpressionBody("a + b")));

            var result = writer.Build();
            Assert.Contains("public int Add(int a, int b)", result);
            Assert.Contains("=> a + b;", result);
        }

        [Fact]
        public void MethodBuilder_WithAttributes()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Api", c => c
                .Public()
                .Method("Get", m => m
                    .Public()
                    .Attribute("HttpGet")
                    .Attribute("ProducesResponseType(200)")
                    .Returns("IActionResult")
                    .Body(b => b.Return("Ok()"))));

            var result = writer.Build();
            Assert.Contains("[HttpGet]", result);
            Assert.Contains("[ProducesResponseType(200)]", result);
        }

        [Fact]
        public void PropertyBuilder_AutoPropertyWithInitializer()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Config", c => c
                .Public()
                .Property("Items", p => p
                    .Type("List<string>")
                    .Initializer("new()")));

            var result = writer.Build();
            Assert.Contains("public List<string> Items { get; set; } = new();", result);
        }

        [Fact]
        public void PropertyBuilder_PrivateSetter()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Entity", c => c
                .Public()
                .Property("Id", p => p
                    .Type("int")
                    .PrivateSet()));

            var result = writer.Build();
            Assert.Contains("public int Id { get; private set; }", result);
        }

        [Fact]
        public void FieldBuilder_ReadonlyWithDoc()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Svc", c => c
                .Public()
                .Field("_logger", f => f
                    .Private().ReadOnly()
                    .Type("ILogger")
                    .Doc("日志记录器")));

            var result = writer.Build();
            Assert.Contains("private readonly ILogger _logger;", result);
            Assert.Contains("/// 日志记录器", result);
        }

        [Fact]
        public void ClassBuilder_XmlDocGeneration()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("MyClass", c => c
                .Public()
                .Doc("这是摘要", "这是备注"));

            var result = writer.Build();
            Assert.Contains("/// <summary>", result);
            Assert.Contains("/// 这是摘要", result);
            Assert.Contains("/// <remarks>这是备注</remarks>", result);
        }

        [Fact]
        public void ClassBuilder_Constructor()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Class("Svc", c => c
                .Public()
                .Field("_repo", f => f.Private().ReadOnly().Type("IRepo"))
                .Constructor(ctor => ctor.AssignField("IRepo", "repo", "_repo")));

            var result = writer.Build();
            Assert.Contains("public Svc(IRepo repo)", result);
            Assert.Contains("_repo = repo;", result);
        }

        [Fact]
        public void InterfaceBuilder_GeneratesInterface()
        {
            var writer = new CodeWriter();
            writer.FileScopedNamespace("Test");
            writer.Interface("IUserService", c => c
                .Public()
                .Method("GetById", m => m
                    .Parameter("int", "id")
                    .Returns("User?")));

            var result = writer.Build();
            Assert.Contains("public interface IUserService", result);
            Assert.Contains("User? GetById(int id);", result);
        }

        [Fact]
        public void AutoGeneratedHeader_IsPresent()
        {
            var writer = new CodeWriter();
            writer.AutoGeneratedHeader();
            writer.FileScopedNamespace("Test");

            var result = writer.Build();
            Assert.Contains("// <auto-generated/>", result);
            Assert.Contains("AutoCode Engine", result);
        }
    }
}
