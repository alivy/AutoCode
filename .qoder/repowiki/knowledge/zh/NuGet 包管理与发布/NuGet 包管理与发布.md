---
kind: external_dependency
name: NuGet 包管理与发布
slug: nuget-包管理与发布
category: external_dependency
scope:
    - '**'
---

项目通过 NuGet 分发 AutoCode 源生成器包（AM.AutoCode），使用 nuget push 命令发布到 https://api.nuget.org/v3/index.json。打包输出位于 src/.nuget/ 目录，需要配置 API Key 进行发布。