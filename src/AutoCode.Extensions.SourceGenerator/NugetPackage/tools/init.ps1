param($installPath, $toolsPath, $package, $project)

# 在这里添加你的逻辑来创建模板文件夹
$templateFolderPath = Join-Path $project.ProjectName "Templates"
New-Item -Path $templateFolderPath -ItemType Directory | Out-Null