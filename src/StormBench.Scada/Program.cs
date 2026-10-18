using StormBench.Scada;

var builder = WebApplication.CreateBuilder(args);
builder.AddScada();

var app = builder.Build();
app.UseScada();
await app.RunAsync();
