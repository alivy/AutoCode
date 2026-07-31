using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AutoCode.Engine.Config
{
    /// <summary>
    /// 智能配置推荐引擎 - 分析项目结构和依赖，自动推荐最佳 AutoCode 配置。
    /// 
    /// 推荐逻辑：
    ///   - 检测到 EF Core → 建议开启 CRUD + Repository
    ///   - 检测到 ASP.NET Core → 建议开启 Controller + 统一响应
    ///   - 检测到 xUnit/NUnit → 建议开启测试生成
    ///   - 检测到多项目结构 → 建议开启 Mapper + DTO
    ///   - 检测到 ILogger 使用 → 建议开启 AutoLog/AutoIntercept(Log)
    ///   - 检测到 IMemoryCache/Redis → 建议开启 AutoIntercept(Cache)
    /// </summary>
    public class ConfigRecommender
    {
        /// <summary>
        /// 分析项目目录，返回配置建议列表
        /// </summary>
        public List<ConfigRecommendation> Analyze(string projectPath)
        {
            var recommendations = new List<ConfigRecommendation>();
            var fullPath = Path.GetFullPath(projectPath);

            // 收集项目信息
            var csprojContent = ReadCsproj(fullPath);
            var csFiles = Directory.GetFiles(fullPath, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains("obj") && !f.Contains("bin"))
                .ToList();
            var allCode = string.Join("\n", csFiles.Select(f => SafeReadFile(f)));

            // 规则 1：EF Core 检测
            if (csprojContent.Contains("Microsoft.EntityFrameworkCore") ||
                allCode.Contains("DbContext"))
            {
                recommendations.Add(new ConfigRecommendation
                {
                    Id = "REC001",
                    Category = "CRUD",
                    Title = "检测到 EF Core，建议开启全链路 CRUD 生成",
                    Detail = "cascade.repository = true, cascade.service = true",
                    ConfigPatch = "{\"cascade\":{\"repository\":true,\"service\":true}}",
                    Priority = RecommendationPriority.High
                });
            }

            // 规则 2：ASP.NET Core 检测
            if (csprojContent.Contains("Microsoft.NET.Sdk.Web") ||
                csprojContent.Contains("Microsoft.AspNetCore") ||
                allCode.Contains("WebApplication"))
            {
                recommendations.Add(new ConfigRecommendation
                {
                    Id = "REC002",
                    Category = "WebAPI",
                    Title = "检测到 WebAPI 框架，建议开启 Controller 生成 + 统一响应包装",
                    Detail = "webapi.responseWrapper = true, cascade.controller = true",
                    ConfigPatch = "{\"webapi\":{\"responseWrapper\":true},\"cascade\":{\"controller\":true}}",
                    Priority = RecommendationPriority.High
                });
            }

            // 规则 3：测试框架检测
            if (csprojContent.Contains("xunit") || csprojContent.Contains("NUnit") ||
                csprojContent.Contains("MSTest"))
            {
                recommendations.Add(new ConfigRecommendation
                {
                    Id = "REC003",
                    Category = "Testing",
                    Title = "检测到测试框架，建议开启自动测试生成",
                    Detail = "cascade.tests = true",
                    ConfigPatch = "{\"cascade\":{\"tests\":true}}",
                    Priority = RecommendationPriority.Medium
                });
            }

            // 规则 4：多项目结构检测
            var slnFiles = Directory.GetFiles(fullPath, "*.sln", SearchOption.TopDirectoryOnly);
            if (slnFiles.Length > 0)
            {
                var slnContent = SafeReadFile(slnFiles[0]);
                var projectCount = slnContent.Split(new[] { "Project(" }, StringSplitOptions.None).Length - 1;
                if (projectCount > 2)
                {
                    recommendations.Add(new ConfigRecommendation
                    {
                        Id = "REC004",
                        Category = "Mapper",
                        Title = $"检测到多项目结构（{projectCount} 个项目），建议开启跨层映射 + DTO",
                        Detail = "plugins.mapper = true, plugins.dto = true",
                        ConfigPatch = "{\"plugins\":{\"mapper\":{\"enabled\":true},\"dto\":{\"enabled\":true}}}",
                        Priority = RecommendationPriority.High
                    });
                }
            }

            // 规则 5：ILogger 使用检测
            if (allCode.Contains("ILogger") || allCode.Contains("LogInformation"))
            {
                recommendations.Add(new ConfigRecommendation
                {
                    Id = "REC005",
                    Category = "Intercept",
                    Title = "检测到日志使用，建议用 [AutoIntercept(Log)] 替代手动日志",
                    Detail = "plugins.intercept = true, 默认拦截器: Log | Metrics",
                    ConfigPatch = "{\"plugins\":{\"intercept\":{\"enabled\":true}},\"intercept\":{\"defaultInterceptors\":\"Log,Metrics\"}}",
                    Priority = RecommendationPriority.Medium
                });
            }

            // 规则 6：缓存使用检测
            if (allCode.Contains("IMemoryCache") || allCode.Contains("IDistributedCache") ||
                csprojContent.Contains("StackExchange.Redis"))
            {
                recommendations.Add(new ConfigRecommendation
                {
                    Id = "REC006",
                    Category = "Intercept",
                    Title = "检测到缓存使用，建议开启 [AutoIntercept(Cache)] 自动缓存",
                    Detail = "intercept.cacheDurationSeconds = 300",
                    ConfigPatch = "{\"intercept\":{\"cacheDurationSeconds\":300}}",
                    Priority = RecommendationPriority.Low
                });
            }

            // 规则 7：AutoMapper 检测（迁移建议）
            if (csprojContent.Contains("AutoMapper") || allCode.Contains("Mapper.Map"))
            {
                recommendations.Add(new ConfigRecommendation
                {
                    Id = "REC007",
                    Category = "Migration",
                    Title = "检测到 AutoMapper，建议迁移到 [MapFrom] 编译时映射（零反射、AOT 兼容）",
                    Detail = "使用 [MapFrom(typeof(Source))] 替代 Mapper.Map<T>()",
                    ConfigPatch = "{\"mapper\":{\"nullHandling\":\"Skip\",\"collectionMapping\":\"DeepCopy\"}}",
                    Priority = RecommendationPriority.Medium
                });
            }

            // 规则 8：手动 DI 检测
            if (allCode.Contains("services.AddScoped") || allCode.Contains("services.AddTransient") ||
                allCode.Contains("services.AddSingleton"))
            {
                recommendations.Add(new ConfigRecommendation
                {
                    Id = "REC008",
                    Category = "DI",
                    Title = "检测到手动 DI 注册，建议使用编译时 DI（IScoped/ISingleton 标记）",
                    Detail = "让类实现 IScoped/ISingleton 接口，编译时自动注册",
                    ConfigPatch = "{\"plugins\":{\"dependencyInjection\":{\"enabled\":true}}}",
                    Priority = RecommendationPriority.Low
                });
            }

            return recommendations.OrderByDescending(r => r.Priority).ToList();
        }

        /// <summary>
        /// 将建议应用为 autocode.json 补丁
        /// </summary>
        public string GenerateRecommendedConfig(List<ConfigRecommendation> recommendations)
        {
            var config = new Dictionary<string, object>
            {
                ["conventions"] = new Dictionary<string, object>
                {
                    ["servicePattern"] = "*Service",
                    ["repositoryPattern"] = "*Repository",
                    ["dtoSuffix"] = "Dto",
                    ["autoDetectServices"] = true
                },
                ["plugins"] = new Dictionary<string, object>
                {
                    ["interface"] = new { enabled = true },
                    ["mapper"] = new { enabled = true },
                    ["dto"] = new { enabled = true },
                    ["validation"] = new { enabled = true },
                    ["webapi"] = new { enabled = true },
                    ["crud"] = new { enabled = true },
                    ["intercept"] = new { enabled = true },
                    ["dependencyInjection"] = new { enabled = true }
                }
            };

            return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        }

        private static string ReadCsproj(string path)
        {
            var files = Directory.GetFiles(path, "*.csproj", SearchOption.TopDirectoryOnly);
            return files.Length > 0 ? SafeReadFile(files[0]) : "";
        }

        private static string SafeReadFile(string path)
        {
            try { return File.ReadAllText(path); }
            catch { return ""; }
        }
    }

    /// <summary>配置建议项</summary>
    public class ConfigRecommendation
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string ConfigPatch { get; set; } = "";
        public RecommendationPriority Priority { get; set; }
    }

    /// <summary>建议优先级</summary>
    public enum RecommendationPriority
    {
        Low = 1,
        Medium = 2,
        High = 3
    }
}
