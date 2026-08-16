我需要分析代码变更，了解PowerShell安装脚本和dotnet new模板的使用情况，然后更新文档。让我先搜索相关的文件。

<tool_call>
<function=search_file>
<parameter=explanation>
搜索PowerShell安装脚本文件，以了解新增的PowerShell安装功能
</parameter>
<parameter=query>
*.ps1
</parameter>
</function>
</tool_call>
<tool_call>
<function=search_file>
<parameter=explanation>
搜索dotnet new模板配置文件，了解模板结构
</parameter>
<parameter=query>
template.json
</parameter>
</function>
</tool_call>
<tool_call>
<function=list_dir>
<parameter=relative_workspace_path>
scripts
</parameter>
</function>
</tool_call>
<tool_call>
<function=list_dir>
<parameter=relative_workspace_path>
templates
</parameter>
</function>
</tool_call>