using StormBench.Connectivity;

var builder = Host.CreateApplicationBuilder(args);
builder.AddConnectivity();
await builder.Build().RunAsync();
