#Requires -Version 5.1
<#
.SYNOPSIS
    AutoCode 一键安装脚本 - 5分钟完成项目集成
.DESCRIPTION
    自动完成：NuGet 包安装 → 配置初始化 → 示例创建 → 环境诊断
.EXAMPLE
    .\install-autocode.ps1                          # 安装到当前目录
    .\install-autocode.ps1 -ProjectPath ./MyApp     # 安装到指定项目
    .\install-autocode.ps1 -WithSamples             # 安装并创建示例代码
#>
param(
    [string]$ProjectPath = ".",
    [switch]$WithSamples,
    [switch]$SkipDoctor
)

$ErrorActionPreference = "Stop"

function Write-Step($msg) {
    Write-Host ""
    Write-Host "  [STEP] $msg" -ForegroundColor Cyan
}
function Write-OK($msg) {
    Write-Host "    [OK] $msg" -ForegroundColor Green
}
function Write-Warn($msg) {
    Write-Host "    [!!] $msg" -ForegroundColor Yellow
}
function Write-Err($msg) {
    Write-Host "    [ERR] $msg" -ForegroundColor Red
}

Write-Host ""
Write-Host "  ============================================" -ForegroundColor Magenta
Write-Host "    AutoCode - 编译时代码生成框架 一键安装" -ForegroundColor Magenta
Write-Host "  ============================================" -ForegroundColor Magenta
Write-Host ""

$fullPath = (Resolve-Path $ProjectPath).Path

# ─── Step 1: 检测项目文件 ───
Write-Step "检测项目文件..."
$csprojFiles = Get-ChildItem -Path $fullPath -Filter "*.csproj" -Depth 0
if ($csprojFiles.Count -eq 0) {
    Write-Err "未找到 .csproj 文件，请确认路径: $fullPath"
    exit 1
}
$csproj = $csprojFiles[0].FullName
Write-OK "找到项目: $($csprojFiles[0].Name)"

# ─── Step 2: 安装 NuGet 包 ───
Write-Step "安装 AM.AutoCode NuGet 包..."
try {
    dotnet add $csproj package AM.AutoCode --prerelease 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-OK "NuGet 包安装成功"
    } else {
        Write-Warn "NuGet 包暂未发布，使用项目引用方式..."
        # 回退：添加项目引用（开发阶段）
        Write-Warn "开发阶段请手动引用 AutoCode.Model 和各生成器项目"
    }
} catch {
    Write-Warn "NuGet 安装失败: $_"
}

# ─── Step 3: 初始化配置 ───
Write-Step "初始化 autocode.json 配置..."
$configPath = Join-Path $fullPath "autocode.json"
if (-not (Test-Path $configPath)) {
    $config = @{
        conventions = @{
            servicePattern = "*Service"
            repositoryPattern = "*Repository"
            dtoSuffix = "Dto"
            autoDetectServices = $true
        }
        mapper = @{ nullHandling = "Skip"; collectionMapping = "DeepCopy" }
        webapi = @{ responseWrapper = $true; pagination = $true }
        cascade = @{ dto = $true; mapper = $true; validation = $true; repository = $true; service = $true; controller = $true; tests = $false; logging = $false }
        intercept = @{ defaultInterceptors = "Log,Metrics"; cacheDurationSeconds = 300; maxRetryCount = 3 }
        plugins = @{
            interface = @{ enabled = $true }
            mapper = @{ enabled = $true }
            dto = @{ enabled = $true }
            validation = @{ enabled = $true }
            webapi = @{ enabled = $true }
            crud = @{ enabled = $true }
            intercept = @{ enabled = $true }
        }
    } | ConvertTo-Json -Depth 4
    Set-Content -Path $configPath -Value $config -Encoding UTF8
    Write-OK "已创建: $configPath"
} else {
    Write-OK "autocode.json 已存在，跳过"
}

# ─── Step 4: 创建示例代码（可选）───
if ($WithSamples) {
    Write-Step "创建示例实体..."
    $entitiesDir = Join-Path $fullPath "Entities"
    if (-not (Test-Path $entitiesDir)) { New-Item -ItemType Directory -Path $entitiesDir | Out-Null }

    $sampleEntity = @'
using AutoCode.Model;
using System.ComponentModel.DataAnnotations;

namespace YourApp.Entities
{
    /// <summary>
    /// 示例产品实体 - 编译时自动生成全链路代码
    /// [AutoEntity] → DTO + Mapper + Validator + Repository + Service + Controller
    /// [AutoIntercept] → Log + Cache + Retry + Metrics 拦截管线
    /// </summary>
    [AutoEntity]
    [AutoIntercept(InterceptType.Log | InterceptType.Cache | InterceptType.Metrics)]
    public class Product
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        [Range(0.01, 99999)]
        public decimal Price { get; set; }

        public string? Description { get; set; }

        public int Stock { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
'@
    $entityPath = Join-Path $entitiesDir "Product.cs"
    Set-Content -Path $entityPath -Value $sampleEntity -Encoding UTF8
    Write-OK "已创建: $entityPath"
    Write-Host "    [INFO] 编译后将自动生成:" -ForegroundColor DarkGray
    Write-Host "      - ProductDto.cs (DTO)" -ForegroundColor DarkGray
    Write-Host "      - ProductMapper.cs (映射)" -ForegroundColor DarkGray
    Write-Host "      - ProductValidator.cs (验证)" -ForegroundColor DarkGray
    Write-Host "      - IProductRepository.cs + ProductRepository.cs" -ForegroundColor DarkGray
    Write-Host "      - IProductService.cs + ProductService.cs" -ForegroundColor DarkGray
    Write-Host "      - ProductsController.cs (API)" -ForegroundColor DarkGray
    Write-Host "      - InterceptedProductService.cs (AOP 拦截)" -ForegroundColor DarkGray
}

# ─── Step 5: 环境诊断 ───
if (-not $SkipDoctor) {
    Write-Step "运行环境诊断..."
    
    # .NET SDK
    $dotnetVersion = dotnet --version 2>$null
    if ($dotnetVersion) { Write-OK ".NET SDK: $dotnetVersion" }
    else { Write-Err ".NET SDK 未安装" }

    # 配置文件
    if (Test-Path $configPath) { Write-OK "autocode.json: 已就绪" }
    else { Write-Warn "autocode.json: 未找到" }

    # .editorconfig
    $editorConfig = Join-Path $fullPath ".editorconfig"
    if (Test-Path $editorConfig) { Write-OK ".editorconfig: 已找到" }
    else { Write-Warn ".editorconfig: 未找到（建议添加）" }
}

# ─── 完成 ───
Write-Host ""
Write-Host "  ============================================" -ForegroundColor Green
Write-Host "    安装完成!" -ForegroundColor Green
Write-Host "  ============================================" -ForegroundColor Green
Write-Host ""
Write-Host "  下一步:" -ForegroundColor White
Write-Host "    1. dotnet build          # 编译触发代码生成" -ForegroundColor DarkGray
Write-Host "    2. 查看 obj/Debug/*/generated/ 目录" -ForegroundColor DarkGray
Write-Host "    3. 在类上添加 [AutoEntity] 或 [AutoIntercept]" -ForegroundColor DarkGray
Write-Host ""
