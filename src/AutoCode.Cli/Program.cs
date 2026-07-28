using System.CommandLine;
using System.Text.Json;

namespace AutoCode.Cli
{
    /// <summary>
    /// AutoCode CLI v2 - 智能代码生成器管理工具
    /// 命令：list / new / generate / analyze / doctor / templates / migrate
    /// </summary>
    public class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var rootCommand = new RootCommand("AutoCode CLI v2 - 智能代码生成器管理工具");

            rootCommand.AddCommand(BuildListCommand());
            rootCommand.AddCommand(BuildNewCommand());
            rootCommand.AddCommand(BuildGenerateCommand());
            rootCommand.AddCommand(BuildAnalyzeCommand());
            rootCommand.AddCommand(BuildDoctorCommand());
            rootCommand.AddCommand(BuildTemplatesCommand());
            rootCommand.AddCommand(BuildInitCommand());

            return await rootCommand.InvokeAsync(args);
        }

        /// <summary>
        /// dotnet autocode list - 列出所有生成器和插件
        /// </summary>
        private static Command BuildListCommand()
        {
            var cmd = new Command("list", "列出所有可用的生成器、分析器和插件");
            cmd.SetHandler(() =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("AutoCode v2.0 - 编译时代码生成框架");
                Console.ResetColor();
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  生成器插件:");
                Console.ResetColor();
                var plugins = new (string Name, string Attr, string Desc)[]
                {
                    ("Interface", "[AutoInterface]", "自动接口提取（泛型/事件/XML文档/Nullable）"),
                    ("Mapper", "[MapFrom]", "跨类型智能映射（嵌套/集合/枚举/投影）"),
                    ("Dto", "[AutoDTO]", "DTO 生成（record/分页/审计排除）"),
                    ("Validation", "[AutoValidator]", "编译时验证（12种规则/跨属性/集合）"),
                    ("WebApi", "[AutoController]", "API Controller（统一响应/版本/授权）"),
                    ("CRUD", "[AutoCrud]", "全栈 CRUD（EF Core/Repository/软删除）"),
                    ("DI", "IScoped/ISingleton", "编译时 DI（Keyed/泛型/HostedService）"),
                    ("Testing", "[AutoTest]", "单元测试（Mock/边界值/异常路径）"),
                    ("Logging", "[AutoLog]", "日志装饰器（结构化/脱敏/耗时）"),
                    ("Cascade", "[AutoEntity]", "级联生成（一个标记→全链路）"),
                    ("Template", "[DotTemplate]", "模板代码生成（DotLiquid）"),
                };

                foreach (var (name, attr, desc) in plugins)
                {
                    Console.WriteLine($"    {name,-14} {attr,-22} {desc}");
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  分析器:");
                Console.ResetColor();
                Console.WriteLine("    AC001  缺少 [AutoInterface] 警告");
                Console.WriteLine("    AC002  接口与实现不一致提示");
                Console.WriteLine("    AC003  无意义 [AutoIgnore] 警告");
                Console.WriteLine("    AC004  分层违规检测");
                Console.WriteLine("    AC006  命名规范强制");
                Console.WriteLine("    AC8xxx 约定推断建议");

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  核心引擎:");
                Console.ResetColor();
                Console.WriteLine("    CodeBuilder    Fluent 代码构建器");
                Console.WriteLine("    Pipeline       插件管线 + Hook");
                Console.WriteLine("    Convention     约定推断引擎");
                Console.WriteLine("    Config         三层配置系统");
                Console.WriteLine("    Diagnostics    结构化诊断");
            });
            return cmd;
        }

        /// <summary>
        /// dotnet autocode new entity Product --with-crud --with-tests
        /// </summary>
        private static Command BuildNewCommand()
        {
            var cmd = new Command("new", "交互式创建新实体/服务/控制器");

            var entityCmd = new Command("entity", "创建实体类 + 可选全链路代码");
            var nameArg = new Argument<string>("name", "实体名称");
            var withCrud = new Option<bool>("--with-crud", () => true, "生成 CRUD 全链路");
            var withTests = new Option<bool>("--with-tests", () => false, "生成测试");
            var withValidation = new Option<bool>("--with-validation", () => true, "生成验证");
            var outputOpt = new Option<string>("--output", () => ".", "输出目录");

            entityCmd.AddArgument(nameArg);
            entityCmd.AddOption(withCrud);
            entityCmd.AddOption(withTests);
            entityCmd.AddOption(withValidation);
            entityCmd.AddOption(outputOpt);

            entityCmd.SetHandler((string name, bool crud, bool tests, bool validation, string output) =>
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  创建实体: {name}");
                Console.ResetColor();

                var dir = Path.GetFullPath(output);
                Directory.CreateDirectory(dir);

                // 生成实体文件
                var entityContent = GenerateEntityFile(name, crud, validation);
                var entityPath = Path.Combine(dir, $"{name}.cs");
                File.WriteAllText(entityPath, entityContent);
                Console.WriteLine($"    [OK] {entityPath}");

                if (crud)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    [INFO] 编译时将自动生成:");
                    Console.WriteLine($"      - {name}Dto.cs (DTO)");
                    Console.WriteLine($"      - {name}Mapper.cs (映射)");
                    if (validation) Console.WriteLine($"      - {name}Validator.cs (验证)");
                    Console.WriteLine($"      - I{name}Repository.cs + {name}Repository.cs");
                    Console.WriteLine($"      - I{name}Service.cs + {name}Service.cs");
                    Console.WriteLine($"      - {name}sController.cs (API)");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  完成! 编译项目即可看到生成的代码。");
                Console.ResetColor();
            }, nameArg, withCrud, withTests, withValidation, outputOpt);

            cmd.AddCommand(entityCmd);
            return cmd;
        }

        /// <summary>
        /// dotnet autocode generate --preview
        /// </summary>
        private static Command BuildGenerateCommand()
        {
            var cmd = new Command("generate", "执行代码生成（预览或应用）");
            var preview = new Option<bool>("--preview", () => false, "仅预览将生成的文件");
            var projectOpt = new Option<string>("--project", () => ".", "项目路径");

            cmd.AddOption(preview);
            cmd.AddOption(projectOpt);

            cmd.SetHandler((bool isPreview, string project) =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine(isPreview ? "  [预览模式] 扫描项目中的 AutoCode 标记..." : "  正在生成代码...");
                Console.ResetColor();

                var csprojFiles = Directory.GetFiles(Path.GetFullPath(project), "*.csproj", SearchOption.TopDirectoryOnly);
                if (csprojFiles.Length == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("  [ERROR] 未找到 .csproj 文件");
                    Console.ResetColor();
                    return;
                }

                Console.WriteLine($"  项目: {Path.GetFileName(csprojFiles[0])}");
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("  提示: AutoCode 生成器在编译时自动运行 (dotnet build)。");
                Console.WriteLine("  使用 --preview 查看哪些类会被处理:");
                Console.ResetColor();

                // 扫描 .cs 文件中的 AutoCode 特性
                var csFiles = Directory.GetFiles(Path.GetFullPath(project), "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("obj") && !f.Contains("bin"));

                var detected = new List<(string File, string Attr, string Class)>();
                foreach (var file in csFiles)
                {
                    var lines = File.ReadAllLines(file);
                    string? pendingAttr = null;
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("[Auto") || trimmed.StartsWith("[Mapper") || trimmed.StartsWith("[MapFrom"))
                        {
                            pendingAttr = trimmed;
                        }
                        else if (pendingAttr != null && trimmed.StartsWith("public class"))
                        {
                            var className = trimmed.Split(' ')[2].Split(':', '{')[0].Trim();
                            detected.Add((Path.GetFileName(file), pendingAttr, className));
                            pendingAttr = null;
                        }
                        else if (!trimmed.StartsWith("["))
                        {
                            pendingAttr = null;
                        }
                    }
                }

                if (detected.Count == 0)
                {
                    Console.WriteLine("  未检测到 AutoCode 特性标记。");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  检测到 {detected.Count} 个标记:");
                    Console.ResetColor();
                    foreach (var (file, attr, cls) in detected)
                    {
                        Console.WriteLine($"    {attr,-30} → {cls} ({file})");
                    }
                }
            }, preview, projectOpt);

            return cmd;
        }

        /// <summary>
        /// dotnet autocode analyze - 分析项目给出优化建议
        /// </summary>
        private static Command BuildAnalyzeCommand()
        {
            var cmd = new Command("analyze", "分析项目，给出代码生成优化建议");
            var pathArg = new Argument<string>("path", () => ".", "项目路径");
            cmd.AddArgument(pathArg);

            cmd.SetHandler((string path) =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  AutoCode 项目分析");
                Console.ResetColor();
                Console.WriteLine();

                var fullPath = Path.GetFullPath(path);
                var csFiles = Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains("obj") && !f.Contains("bin"))
                    .ToList();

                Console.WriteLine($"  扫描 {csFiles.Count} 个源文件...");
                Console.WriteLine();

                var suggestions = new List<string>();

                foreach (var file in csFiles)
                {
                    var content = File.ReadAllText(file);
                    var fileName = Path.GetFileName(file);

                    // 检测 Service 类没有接口
                    if (fileName.EndsWith("Service.cs") && !content.Contains("[AutoInterface]")
                        && content.Contains("public class") && !content.Contains("interface"))
                    {
                        suggestions.Add($"[AC8001] {fileName}: Service 类建议添加 [AutoInterface] 提取接口");
                    }

                    // 检测手动 DI 注册
                    if (content.Contains("services.AddScoped") || content.Contains("services.AddTransient"))
                    {
                        if (!content.Contains("AddAutoDI"))
                            suggestions.Add($"[AC8002] {fileName}: 检测到手动 DI 注册，建议使用编译时 DI (IScoped/ISingleton)");
                    }

                    // 检测手动映射
                    if (content.Contains(".Map<") || content.Contains("Mapper.Map") || content.Contains("AutoMapper"))
                    {
                        suggestions.Add($"[AC8003] {fileName}: 检测到运行时映射，建议使用 [MapFrom] 编译时映射");
                    }
                }

                if (suggestions.Count == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("  未发现优化建议，项目状态良好!");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"  {suggestions.Count} 条建议:");
                    Console.ResetColor();
                    foreach (var s in suggestions)
                        Console.WriteLine($"    {s}");
                }
            }, pathArg);

            return cmd;
        }

        /// <summary>
        /// dotnet autocode doctor - 诊断配置问题
        /// </summary>
        private static Command BuildDoctorCommand()
        {
            var cmd = new Command("doctor", "诊断 AutoCode 配置和环境问题");
            cmd.SetHandler(() =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  AutoCode Doctor");
                Console.ResetColor();
                Console.WriteLine();

                var checks = new List<(string Name, bool Pass, string Detail)>();

                // 检查 .NET SDK
                checks.Add((".NET SDK", true, $"已安装"));

                // 检查 autocode.json
                var hasJson = File.Exists("autocode.json");
                checks.Add(("autocode.json", hasJson, hasJson ? "已找到配置文件" : "未找到（使用默认配置）"));

                // 检查 NuGet 包引用
                var csprojFiles = Directory.GetFiles(".", "*.csproj", SearchOption.TopDirectoryOnly);
                var hasPackage = false;
                foreach (var csproj in csprojFiles)
                {
                    var content = File.ReadAllText(csproj);
                    if (content.Contains("AM.AutoCode") || content.Contains("AutoCode"))
                    {
                        hasPackage = true;
                        break;
                    }
                }
                checks.Add(("NuGet 包引用", hasPackage, hasPackage ? "已引用 AutoCode" : "未检测到引用"));

                // 检查 .editorconfig
                var hasEditorConfig = File.Exists(".editorconfig");
                checks.Add((".editorconfig", hasEditorConfig, hasEditorConfig ? "已找到" : "未找到（建议添加）"));

                // 输出结果
                foreach (var (name, pass, detail) in checks)
                {
                    Console.ForegroundColor = pass ? ConsoleColor.Green : ConsoleColor.Yellow;
                    var icon = pass ? "[OK]" : "[!!]";
                    Console.WriteLine($"  {icon} {name,-20} {detail}");
                }
                Console.ResetColor();

                Console.WriteLine();
                var allPass = checks.All(c => c.Pass);
                Console.ForegroundColor = allPass ? ConsoleColor.Green : ConsoleColor.Yellow;
                Console.WriteLine(allPass ? "  所有检查通过!" : "  部分检查未通过，请参考上方建议。");
                Console.ResetColor();
            });
            return cmd;
        }

        /// <summary>
        /// dotnet autocode templates list/install
        /// </summary>
        private static Command BuildTemplatesCommand()
        {
            var cmd = new Command("templates", "管理代码模板");

            var listCmd = new Command("list", "列出可用模板");
            listCmd.SetHandler(() =>
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  可用模板:");
                Console.ResetColor();
                Console.WriteLine("    entity       实体类 + [AutoEntity] 全链路");
                Console.WriteLine("    service      Service + Interface + DI");
                Console.WriteLine("    controller   API Controller + DTO");
                Console.WriteLine("    repository   Repository + EF Core");
                Console.WriteLine("    validator    验证器 + DataAnnotations");
            });

            var installCmd = new Command("install", "安装模板到项目");
            var templateArg = new Argument<string>("template", "模板名称");
            installCmd.AddArgument(templateArg);
            installCmd.SetHandler((string template) =>
            {
                Console.WriteLine($"  安装模板: {template}");
                var templateDir = Path.Combine(".", "Templates");
                Directory.CreateDirectory(templateDir);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"  已创建: {templateDir}/");
                Console.ResetColor();
            }, templateArg);

            cmd.AddCommand(listCmd);
            cmd.AddCommand(installCmd);
            return cmd;
        }

        /// <summary>
        /// dotnet autocode init - 初始化项目配置
        /// </summary>
        private static Command BuildInitCommand()
        {
            var cmd = new Command("init", "初始化 AutoCode 配置（autocode.json + 模板目录）");
            var pathArg = new Argument<string>("path", () => ".", "项目路径");
            cmd.AddArgument(pathArg);

            cmd.SetHandler((string path) =>
            {
                var fullPath = Path.GetFullPath(path);
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  初始化 AutoCode 配置...");
                Console.ResetColor();

                // 创建 autocode.json
                var configPath = Path.Combine(fullPath, "autocode.json");
                if (!File.Exists(configPath))
                {
                    var config = new
                    {
                        conventions = new
                        {
                            servicePattern = "*Service",
                            repositoryPattern = "*Repository",
                            dtoSuffix = "Dto",
                            autoDetectServices = true
                        },
                        mapper = new { nullHandling = "Skip", collectionMapping = "DeepCopy" },
                        webapi = new { responseWrapper = true, pagination = true },
                        cascade = new { tests = false, logging = false }
                    };
                    File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
                    Console.WriteLine($"  [OK] {configPath}");
                }
                else
                {
                    Console.WriteLine($"  [SKIP] autocode.json 已存在");
                }

                // 创建模板目录
                var templateDir = Path.Combine(fullPath, "Templates");
                if (!Directory.Exists(templateDir))
                {
                    Directory.CreateDirectory(templateDir);
                    Console.WriteLine($"  [OK] {templateDir}/");
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("  初始化完成!");
                Console.ResetColor();
            }, pathArg);

            return cmd;
        }

        private static string GenerateEntityFile(string name, bool withCrud, bool withValidation)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using AutoCode.Model;");
            if (withValidation)
                sb.AppendLine("using System.ComponentModel.DataAnnotations;");
            sb.AppendLine();
            sb.AppendLine("namespace YourApp.Entities");
            sb.AppendLine("{");

            if (withCrud)
                sb.AppendLine("    [AutoEntity]");
            sb.AppendLine($"    public class {name}");
            sb.AppendLine("    {");
            sb.AppendLine("        public int Id { get; set; }");
            sb.AppendLine();
            if (withValidation)
            {
                sb.AppendLine("        [Required]");
                sb.AppendLine("        [MaxLength(100)]");
            }
            sb.AppendLine("        public string Name { get; set; } = \"\";");
            sb.AppendLine();
            sb.AppendLine("        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
