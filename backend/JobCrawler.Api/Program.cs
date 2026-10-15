using System.Net;
using JobCrawler.Api.Crawling;
using JobCrawler.Api.Crawling.Sources;
using JobCrawler.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default") ?? "Data Source=jobs.db"));

var crawlerOptions = builder.Configuration.GetSection("Crawler").Get<CrawlerOptions>() ?? new CrawlerOptions();
builder.Services.AddSingleton(crawlerOptions);

// One shared HttpClient for all crawlers so robots.txt cache and per-host throttling are global.
builder.Services.AddSingleton(_ =>
{
    var http = new HttpClient(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        AllowAutoRedirect = true,
    })
    {
        Timeout = TimeSpan.FromSeconds(crawlerOptions.TimeoutSeconds),
    };
    http.DefaultRequestHeaders.UserAgent.ParseAdd(crawlerOptions.UserAgent);
    http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("vi-VN,vi;q=0.9,en;q=0.8");
    return http;
});
builder.Services.AddSingleton(sp => new PoliteHttpClient(
    sp.GetRequiredService<HttpClient>(), crawlerOptions, sp.GetRequiredService<ILogger<PoliteHttpClient>>()));

builder.Services.AddSingleton<IJobSource, ItViecSource>();
builder.Services.AddSingleton<IJobSource, TopCvSource>();
builder.Services.AddSingleton<IJobSource, TopDevSource>();
builder.Services.AddSingleton<IJobSource, VietnamWorksSource>();
// Company career sites
builder.Services.AddSingleton<IJobSource, TmaSource>();
builder.Services.AddSingleton<IJobSource, AxonActiveSource>();
builder.Services.AddSingleton<IJobSource, NabSource>();
builder.Services.AddSingleton<IJobSource, NetcompanySource>();
builder.Services.AddSingleton<CrawlCoordinator>();

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:5173"])
        .AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.MapControllers();
app.Run();
