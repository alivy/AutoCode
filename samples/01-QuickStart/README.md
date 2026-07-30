# 01 - QuickStart：5分钟上手

> 展示：一个 `[AutoEntity]` 标记如何替代 350+ 行手写样板代码。

## Before：传统手写方式（~350 行）

```csharp
// ═══════════════════════════════════════════════════════
// 文件 1: IProductService.cs（手写接口 ~20行）
// ═══════════════════════════════════════════════════════
public interface IProductService
{
    List<ProductDto> GetAll();
    ProductDto? GetById(int id);
    ProductDto Create(CreateProductRequest request);
    ProductDto? Update(int id, UpdateProductRequest request);
    void Delete(int id);
}

// ═══════════════════════════════════════════════════════
// 文件 2: ProductDto.cs（手写 DTO ~30行）
// ═══════════════════════════════════════════════════════
public class ProductDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public string? CategoryName { get; set; }
}

public class CreateProductRequest
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
}

// ═══════════════════════════════════════════════════════
// 文件 3: ProductMapper.cs（手写映射 ~45行）
// ═══════════════════════════════════════════════════════
public static class ProductMapper
{
    public static ProductDto ToDto(Product entity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        return new ProductDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Price = entity.Price,
            CategoryId = entity.CategoryId,
            CategoryName = entity.Category?.Name
        };
    }

    public static Product ToEntity(CreateProductRequest request)
    {
        if (request == null) throw new ArgumentNullException(nameof(request));
        return new Product
        {
            Name = request.Name,
            Price = request.Price,
            CategoryId = request.CategoryId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static List<ProductDto> ToDtoList(List<Product> entities)
    {
        return entities?.Select(ToDto).ToList() ?? new List<ProductDto>();
    }
}

// ═══════════════════════════════════════════════════════
// 文件 4: ProductValidator.cs（手写验证 ~40行）
// ═══════════════════════════════════════════════════════
public class ProductValidator
{
    public List<string> Validate(CreateProductRequest request)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors.Add("名称不能为空");
        if (request.Name?.Length > 200)
            errors.Add("名称不能超过200字符");
        if (request.Price <= 0)
            errors.Add("价格必须大于0");
        if (request.Price > 99999)
            errors.Add("价格不能超过99999");
        if (request.CategoryId <= 0)
            errors.Add("分类ID无效");
        return errors;
    }
}

// ═══════════════════════════════════════════════════════
// 文件 5: IProductRepository.cs + ProductRepository.cs（~95行）
// ═══════════════════════════════════════════════════════
public interface IProductRepository
{
    Task<List<Product>> GetAllAsync();
    Task<Product?> GetByIdAsync(int id);
    Task<Product> AddAsync(Product entity);
    Task<Product?> UpdateAsync(Product entity);
    Task DeleteAsync(int id);
}

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _db;
    public ProductRepository(AppDbContext db) => _db = db;

    public async Task<List<Product>> GetAllAsync()
        => await _db.Products.Include(p => p.Category).ToListAsync();

    public async Task<Product?> GetByIdAsync(int id)
        => await _db.Products.Include(p => p.Category).FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Product> AddAsync(Product entity)
    {
        _db.Products.Add(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    public async Task<Product?> UpdateAsync(Product entity)
    {
        _db.Products.Update(entity);
        await _db.SaveChangesAsync();
        return entity;
    }

    public async Task DeleteAsync(int id)
    {
        var entity = await _db.Products.FindAsync(id);
        if (entity != null)
        {
            _db.Products.Remove(entity);
            await _db.SaveChangesAsync();
        }
    }
}

// ═══════════════════════════════════════════════════════
// 文件 6: ProductsController.cs（手写控制器 ~60行）
// ═══════════════════════════════════════════════════════
[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly IProductService _service;
    public ProductsController(IProductService service) => _service = service;

    [HttpGet]
    public ActionResult<List<ProductDto>> GetAll()
        => Ok(_service.GetAll());

    [HttpGet("{id}")]
    public ActionResult<ProductDto> GetById(int id)
    {
        var result = _service.GetById(id);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public ActionResult<ProductDto> Create(CreateProductRequest request)
    {
        var result = _service.Create(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id}")]
    public ActionResult<ProductDto> Update(int id, UpdateProductRequest request)
    {
        var result = _service.Update(id, request);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        _service.Delete(id);
        return NoContent();
    }
}

// ═══════════════════════════════════════════════════════
// 文件 7: DI 注册（手写 ~10行）
// ═══════════════════════════════════════════════════════
// Program.cs 中:
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<IProductService, ProductService>();
```

**总计：~350 行手写样板代码，7 个文件**

---

## After：AutoCode 方式（~15 行）

```csharp
// ═══════════════════════════════════════════════════════
// 唯一需要手写的文件: Product.cs
// ═══════════════════════════════════════════════════════
using AutoCode.Model;
using System.ComponentModel.DataAnnotations;

namespace MyApp.Entities;

[AutoEntity]   // ← 就这一个标记，编译时自动生成以上所有代码
public class Product
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = "";

    [Range(0.01, 99999)]
    public decimal Price { get; set; }

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

**总计：~15 行，1 个文件。编译后自动生成 7+ 个文件。**

---

## 生成结果

执行 `dotnet build` 后，在 `obj/Debug/net8.0/generated/` 目录下自动产出：

```
generated/
├── AutoCode.Interface/
│   └── IProductService.g.cs          ← 接口提取
├── AutoCode.Dto/
│   └── ProductDto.g.cs               ← DTO + Request 模型
├── AutoCode.Map/
│   └── ProductMapper.g.cs            ← 双向映射
├── AutoCode.Validation/
│   └── ProductValidator.g.cs         ← 验证规则
├── AutoCode.Crud/
│   ├── IProductRepository.g.cs       ← 仓储接口
│   └── ProductRepository.g.cs        ← 仓储实现
├── AutoCode.WebApi/
│   └── ProductsController.g.cs       ← REST API 控制器
└── AutoCode.DependencyInjection/
    └── ProductDI.g.cs                ← DI 自动注册
```

## 关键优势

| 维度 | 手写 | AutoCode |
|------|------|----------|
| 代码量 | 350+ 行 | 15 行 |
| 文件数 | 7 个 | 1 个 |
| 一致性 | 容易遗漏/不一致 | 100% 一致 |
| 重构成本 | 改实体需改 7 个文件 | 改实体自动同步 |
| 运行时开销 | 无 | 无（编译时生成） |
| 可调试性 | 正常 | 正常（.g.cs 可 F12） |
