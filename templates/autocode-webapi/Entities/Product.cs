using AutoCode.Model;
using System.ComponentModel.DataAnnotations;

namespace AutoCodeWebApiTemplate.Entities
{
    /// <summary>
    /// 示例产品实体 - 展示 AutoCode 全链路代码生成
    /// 
    /// 编译后自动生成：
    ///   [AutoEntity]     → DTO + Mapper + Validator + Repository + Service + Controller
    ///   [AutoIntercept]  → Log + Cache + Retry + Metrics 拦截管线
    ///   [AutoInterface]  → IProductService 接口提取
    /// </summary>
    [AutoEntity]
    [AutoInterface]
    [AutoIntercept(
        InterceptType.Log | InterceptType.Cache | InterceptType.Retry | InterceptType.Metrics,
        CacheDurationSeconds = 120,
        MaxRetryCount = 3)]
    public class Product
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = "";

        [Range(0.01, 99999)]
        public decimal Price { get; set; }

        [MaxLength(2000)]
        public string? Description { get; set; }

        public int Stock { get; set; }

        public int CategoryId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }
    }
}
