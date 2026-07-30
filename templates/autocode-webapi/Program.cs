using AutoCodeWebApiTemplate.Entities;

var builder = WebApplication.CreateBuilder(args);

// AutoCode 编译时 DI 自动注册（无需手写 services.AddScoped）
// builder.Services.AddAutoDI();  ← 由生成器自动调用

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache(); // [AutoIntercept(Cache)] 需要

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
