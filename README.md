# AutoCode

#### 介绍
自动代码生成器

#### 软件架构
此生成器采用了SourceGenerator进行了代码生成，能在编写代码的过程中实现代码自动生成 


#### 使用说明

### 1.  自动接口生成

1.1 自动生成内存接口，我们只需要打上 [AutoInterface]标记，即可自动生成接口，示例如下

```C#
using AutoCode.Model.InterfaceAttribute;

namespace AutoCode.Test
{
    [AutoInterface]
    public class NugetCenter : INugetCenter
    {
        public int Multiplication(int num1, int num2)
        {
            return num1 * num2;
        }

        public int Reduce(int num1, int num2)
        {
            return num1 - num2;
        }

        public int Except(int num1, int num2)
        {
            return num1 / num2;
        }
    }
}
```
1.2 自定义本地路劲接口和接口名生成，我们可以打上[AutoInterface("INugetCenterV", "/")]第一个参数代表自定义接口名称，第二个为路劲参数，"/" 代表当前目录下生成文件，也可填写本地绝对路劲

```C#
using AutoCode.Model.InterfaceAttribute;

namespace AutoCode.Test
{
    [AutoInterface("INugetCenterV", "/")]
    public class NugetCenter : INugetCenterV
    {
        public int Multiplication(int num1, int num2)
        {
            return num1 * num2;
        }

        public int Reduce(int num1, int num2)
        {
            return num1 - num2;
        }

        public int Except(int num1, int num2)
        {
            return num1 / num2;
        }
    }
}
```
2.接口方法忽略，如遇到需要忽略不需要生成接口的方法，在方法上打上 [AutoIgnore]标记即可忽略
方法示例如下

```C#
using AutoCode.Model.InterfaceAttribute;

namespace AutoCode.Test
{
    [AutoInterface]
    public class NugetCenter : INugetCenter
    {
        [AutoIgnore]
        public int Multiplication(int num1, int num2)
        {
            return num1 * num2;
        }

        public int Reduce(int num1, int num2)
        {
            return num1 - num2;
        }

        public int Except(int num1, int num2)
        {
            return num1 / num2;
        }
    }
}
```
生成接口如下

```C#
namespace AutoCode.Test
{
	public  interface INugetCenter
	{
		 public  int Reduce(int num1, int num2); 
		 public  int Except(int num1, int num2); 
	}
}
```

### 2、模板代码生成器

#### 这个是干什么的？

AutoCode允许你根据现有代码进行二次自动化模版构建新代码，这些模板可根据[DotLiquid语法](https://liquid.bootcss.com/)自动构建，这是AutoCode主要核心功能。



#### 技术架构

- Microsoft.CodeAnalysis.CSharp  代码分析器

- DotLiquid 语法转化器

  ​

#### 如何安装：

可以通过NuGet进行安装：

```
安装包 AutoCodeGenerator
```



#### 如何使用

首先，构建你的代码，你需要在你的代码中DotTemplate特性中指定模板名字以及路径：

```C#
    [DotTemplate(@"F:\个人工具资料\AutoCode\auto-code\src\APP\DotTemplate\Template.dot")]
    public class AutoTemplate : IAutoTemplate
    {
        /// <summary>
        /// 字段1   
        /// </summary>
        public int FiledName1;

        /// <summary> 
        /// 字段1
        /// </summary>
        public int FiledName;
        /// <summary>
        /// 属性名      
        /// </summary>
        [DisplayName("字段名")]
        public string? PropertyName { get; set; }

        [DisplayName("方法名")]
        public void Method15() 
        {
             
        }

        public int Method2(int num)
        {
            return num;
        }

        public int Method3(string str)
        {
            return int.Parse(str);
        }

        public int Method4(string str)
        {
            return int.Parse(str);
        }
        public int Method5(int num)
        {
            return num;
        }
    }


    public interface IAutoTemplate
    {
        public int Method2(int num)
        {
            return num;
        }
    }
```

AutoCode会在你构建代码时生成 (AutoTemplate为类名称)AutoTemplate.json：

```json
{
  "Usings": [
    "using System;",
    "using System.Reflection;"
  ],
  "NameSpace": "APP\r\n",
  "Modifier": "public",
  "DefName": "AutoTemplate",
  "ClassPath": "F:\\个人工具资料\\AutoCode\\auto-code\\src\\APP",
  "Inherits": [
    {
      "DefName": "IAutoTemplate"
    }
  ],
  "Attributes": [
    {
      "DefName": "DotTemplate",
      "Parameters": [
        {
          "Type": "AttributeArgumentSyntax",
          "DefName": "@\"F:\\个人工具资料\\AutoCode\\auto-code\\src\\APP\\DotTemplate\\Template.dot"
        }
      ]
    }
  ],
  "Methods": [
    {
      "Modifier": "public",
      "DefName": "Method18",
      "Type": "void",
      "Parameters": []
    },
    {
      "Modifier": "public",
      "DefName": "Method",
      "Type": "int",
      "Parameters": [
        {
          "Type": "int",
          "DefName": "num"
        }
      ]
    },
    {
      "Modifier": "public",
      "DefName": "Method3",
      "Type": "int",
      "Parameters": [
        {
          "Type": "string",
          "DefName": "str"
        }
      ]
    },
    {
      "Modifier": "public",
      "DefName": "Method4",
      "Type": "int",
      "Parameters": [
        {
          "Type": "string",
          "DefName": "str"
        }
      ]
    },
    {
      "Modifier": "public",
      "DefName": "Method5",
      "Type": "int",
      "Parameters": [
        {
          "Type": "int",
          "DefName": "num"
        }
      ]
    }
  ],
  "Fileds": [
    {
      "Modifier": "public",
      "DefName": "FiledName_2",
      "Type": "int"
    },
    {
      "Modifier": "public",
      "DefName": "FiledName_3",
      "Type": "int"
    },
    {
      "Modifier": "public",
      "DefName": "FiledName",
      "Type": "int"
    }
  ],
  "Propertys": [
    {
      "Modifier": "public",
      "DefName": "PropertyName",
      "Type": "string?"
    }
  ]
}
```



我们需要根据Json文件构建我们的模板文件，例如我们构建了以下模板：

```C#
{% for us in Usings %}
{{ us }}
{% endfor %}
namespace {{NameSpace}}
{
	{{Modifier }} class {{DefName}}Copy {% if Inherits != null %}:{% endif %}  {% for Inherit in Inherits %} {{ Inherit.DefName }}  {% endfor %}
	{
		{% for mth in Methods %}
		{{mth.Modifier}} void {{mth.DefName}}Copy()
		{
		   var baseClass =new {{DefName}}();
		   return;
		}
		{% endfor %}
	}
}
```



在完成上述准备工作后，在编写代码时，VS会根据以上规则自动二次生成我们需要的代码，并且可在编写的过程同步调整，生成代码示例如下：

```C#
public class AutoTemplateCopy : IAutoTemplate  
{
	
	public void Method15Copy()
	{
	   var baseClass =new AutoTemplate();
	   return;
	}
	
	public void Method2Copy()
	{
	   var baseClass =new AutoTemplate();
	   return;
	}
	
	public void Method3Copy()
	{
	   var baseClass =new AutoTemplate();
	   return;
	}
	
	public void Method4Copy()
	{
	   var baseClass =new AutoTemplate();
	   return;
	}
	
	public void Method5Copy()
	{
	   var baseClass =new AutoTemplate();
	   return;
	}
	
}
```

