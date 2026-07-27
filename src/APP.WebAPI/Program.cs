using APP.WebAPI.Core.Application;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.InitAPI()
       .Services.AddSwaggerGen(); 

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
