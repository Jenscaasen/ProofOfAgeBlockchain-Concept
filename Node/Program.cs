using Node;
using Node.Services;
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<DataStore>();
builder.Services.AddSingleton<NegotiationService>();
builder.Services.AddSingleton<AppInterfaceService>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
var app = builder.Build();

app.MapGrpcService<DiscoveryService>();
app.MapPost("/transaction", (HttpContext context, AppInterfaceService appInterface) => appInterface.AddTransaction(context));
app.MapGet("/getwallet/{walletid}", (HttpContext context, AppInterfaceService appInterface, string walletid) => appInterface.GetWallet(context, walletid));
app.MapGet("/blockchain", (HttpContext context, AppInterfaceService appInterface) => appInterface.GetBlockchain(context));

app.MapGet("/", (HttpContext context, AppInterfaceService appInterface) => appInterface.GetStatistics(context));
app.Start();
Thread.Sleep(5000);
Globals.Kestrel_Host = app.Urls.First();
app.WaitForShutdown();
