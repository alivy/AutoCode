# AutoCode 示例画廊

> 通过 Before/After 对比，直观展示 AutoCode 编译时代码生成的威力。

## 示例目录

| # | 示例 | 说明 | 手写代码量 | AutoCode 代码量 | 减少比例 |
|---|------|------|-----------|----------------|--------|
| 01 | [QuickStart](./01-QuickStart/) | 5分钟上手：一个标记生成全链路 | ~350 行 | ~15 行 | **95%** |
| 02 | [InterceptAOP](./02-InterceptAOP/) | 编译时 AOP 替代动态代理 | ~200 行 | ~5 行 | **97%** |
| 03 | [TypedMethodHandler](./03-TypedMethodHandler/) | 强类型方法拦截器（值类型/引用类型/Nullable/集合/枚举/数组） | ~150 行 | ~10 行 | **93%** |
| 04 | [AopComparison](./04-AopComparison/) | AOP 方案对比分析 + 性能基准测试（vs Castle/PostSharp） | - | - | 数据驱动 |

## 快速体验

```bash
# 进入示例目录
cd samples/01-QuickStart

# 编译（自动触发代码生成）
dotnet build

# 查看生成的代码
ls obj/Debug/net8.0/generated/
```

## 核心理念

```
传统方式：手写 300+ 行样板代码（接口、DTO、映射、验证、控制器...）
AutoCode ：标记 1 个 Attribute → 编译时自动生成所有代码

┌──────────────────────────────────────────────────────────┐
│  [AutoEntity]  ← 你只写这一行                             │
│  public class Product { ... }                            │
│                                                          │
│  编译后自动生成 ↓                                         │
│  ├── IProductService.cs        (接口提取)                 │
│  ├── ProductDto.cs             (DTO)                     │
│  ├── ProductMapper.cs          (映射)                    │
│  ├── ProductValidator.cs       (验证)                    │
│  ├── IProductRepository.cs     (仓储接口)                │
│  ├── ProductRepository.cs      (仓储实现)                │
│  ├── ProductsController.cs     (API 控制器)              │
│  └── InterceptedProductService.cs (AOP 拦截)             │
└──────────────────────────────────────────────────────────┘
```
