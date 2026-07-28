using System.CommandLine;

namespace AutoCode.Cli
{
    /// <summary>
    /// AutoCode CLI - 代码生成器管理工具
    /// </summary>
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("AutoCode CLI - 代码生成器管理工具");

            // dotnet autocode list
            var listCommand = new Command("list", "列出当前项目中所有生成器生成的文件");
            listCommand.SetHandler(() =>
            {
                Console.WriteLine("AutoCode 生成器列表:");
                Console.WriteLine("  1. InterfaceGenerator   - [AutoInterface] 接口自动生成");
                Console.WriteLine("  2. DotTemplateGenerator - [DotTemplate] 模板代码生成");
                Console.WriteLine("  3. MapperGenerator      - [Mapper] 对象映射生成");
                Console.WriteLine("  4. DependencyInjectionGenerator - [IScoped/ISingleton/ITransient] DI 注册");
                Console.WriteLine("  5. DtoGenerator         - [AutoDTO] DTO 自动生成");
                Console.WriteLine("  6. ValidationGenerator  - [AutoValidator] 验证代码生成");
                Console.WriteLine("  7. ControllerGenerator  - [AutoController] API Controller 生成");
                Console.WriteLine();
                Console.WriteLine("分析器:");
                Console.WriteLine("  AC001 - 缺少 [AutoInterface] 警告");
                Console.WriteLine("  AC002 - 接口与实现不一致提示");
                Console.WriteLine("  AC003 - 无意义 [AutoIgnore] 警告");
            });

            // dotnet autocode init
            var initCommand = new Command("init", "初始化 AutoCode 模板目录");
            var pathArg = new Argument<string>("path", () => ".", "模板目录路径");
            initCommand.AddArgument(pathArg);
            initCommand.SetHandler((string path) =>
            {
                var templateDir = Path.Combine(path, "Templates");
                if (!Directory.Exists(templateDir))
                {
                    Directory.CreateDirectory(templateDir);
                    Console.WriteLine($"已创建模板目录: {templateDir}");

                    // 创建示例模板
                    var sampleTemplate = """
                        {% for us in Usings %}
                        using {{ us.DefName }};
                        {% endfor %}
                        namespace {{ NameSpace }}
                        {
                            public class {{ DefName }}Generated
                            {
                                {% for mth in Methods %}
                                public {{ mth.Type }} {{ mth.DefName }}()
                                {
                                    throw new System.NotImplementedException();
                                }
                                {% endfor %}
                            }
                        }
                        """;
                    File.WriteAllText(Path.Combine(templateDir, "Sample.dot"), sampleTemplate);
                    Console.WriteLine("已创建示例模板: Templates/Sample.dot");
                }
                else
                {
                    Console.WriteLine($"模板目录已存在: {templateDir}");
                }
            }, pathArg);

            // dotnet autocode validate-templates
            var validateCommand = new Command("validate-templates", "验证 .dot 模板文件语法");
            var validatePathArg = new Argument<string>("path", () => ".", "模板目录路径");
            validateCommand.AddArgument(validatePathArg);
            validateCommand.SetHandler((string path) =>
            {
                var templateDir = Path.Combine(path, "Templates");
                if (!Directory.Exists(templateDir))
                {
                    Console.WriteLine($"模板目录不存在: {templateDir}");
                    return;
                }

                var files = Directory.GetFiles(templateDir, "*.dot", SearchOption.AllDirectories);
                Console.WriteLine($"找到 {files.Length} 个模板文件:");
                foreach (var file in files)
                {
                    var content = File.ReadAllText(file);
                    var hasErrors = false;

                    // 简单语法检查
                    var openTags = content.Split("{%").Length - 1;
                    var closeTags = content.Split("%}").Length - 1;
                    if (openTags != closeTags)
                    {
                        Console.WriteLine($"  [ERROR] {Path.GetFileName(file)}: 标签不匹配 ({{% {openTags} vs %}} {closeTags})");
                        hasErrors = true;
                    }

                    var openVars = content.Split("{{").Length - 1;
                    var closeVars = content.Split("}}").Length - 1;
                    if (openVars != closeVars)
                    {
                        Console.WriteLine($"  [ERROR] {Path.GetFileName(file)}: 变量不匹配 ({{{{ {openVars} vs }}}} {closeVars})");
                        hasErrors = true;
                    }

                    if (!hasErrors)
                        Console.WriteLine($"  [OK] {Path.GetFileName(file)}");
                }
            }, validatePathArg);

            rootCommand.AddCommand(listCommand);
            rootCommand.AddCommand(initCommand);
            rootCommand.AddCommand(validateCommand);

            return await rootCommand.InvokeAsync(args);
        }
    }
}
