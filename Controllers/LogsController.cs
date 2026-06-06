using System.Text;
using MarketApp.Data;
using MarketApp.DTOs;
using MarketApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarketApp.Controllers;

[ApiController]
[Route("api/logs")]
[Authorize(Roles = UserRoles.Admin)]
public class LogsController : ControllerBase
{
    private const int DefaultTake = 200;
    private const int MaxTake = 1000;
    private const int ExportTake = 5000;

    private readonly AppDbContext _context;
    private readonly ILogger<LogsController> _logger;

    public LogsController(AppDbContext context, ILogger<LogsController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AppLogDto>>> GetAll(
        [FromQuery] string? level,
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int take = DefaultTake)
    {
        take = Math.Clamp(take, 1, MaxTake);

        var logs = await ApplyFilters(_context.AppLogs.AsNoTracking(), level, search, from, to)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(take)
            .Select(l => ToDto(l))
            .ToListAsync();

        _logger.LogInformation(
            "Logs listed. Level: {Level}, Search: {Search}, From: {From}, To: {To}, Count: {Count}",
            level,
            search,
            from,
            to,
            logs.Count);

        return Ok(logs);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? level,
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var logs = await ApplyFilters(_context.AppLogs.AsNoTracking(), level, search, from, to)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(ExportTake)
            .ToListAsync();

        _logger.LogInformation(
            "Logs exported. Level: {Level}, Search: {Search}, From: {From}, To: {To}, Count: {Count}",
            level,
            search,
            from,
            to,
            logs.Count);

        var csv = BuildCsv(logs);
        var bytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(csv))
            .ToArray();

        var fileName = $"marketapp-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private static IQueryable<AppLog> ApplyFilters(
        IQueryable<AppLog> query,
        string? level,
        string? search,
        DateTime? from,
        DateTime? to)
    {
        if (!string.IsNullOrWhiteSpace(level))
        {
            var normalizedLevel = level.Trim();
            query = query.Where(l => l.Level == normalizedLevel);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim();
            query = query.Where(l =>
                l.Message.Contains(normalizedSearch) ||
                l.Category.Contains(normalizedSearch) ||
                (l.Username != null && l.Username.Contains(normalizedSearch)) ||
                (l.Path != null && l.Path.Contains(normalizedSearch)));
        }

        if (from.HasValue)
            query = query.Where(l => l.CreatedAtUtc >= from.Value);

        if (to.HasValue)
            query = query.Where(l => l.CreatedAtUtc <= to.Value);

        return query;
    }

    private static AppLogDto ToDto(AppLog log) => new()
    {
        Id = log.Id,
        CreatedAtUtc = log.CreatedAtUtc,
        Level = log.Level,
        Category = log.Category,
        Message = log.Message,
        Exception = log.Exception,
        TraceId = log.TraceId,
        Method = log.Method,
        Path = log.Path,
        UserId = log.UserId,
        Username = log.Username
    };

    private static string BuildCsv(IEnumerable<AppLog> logs)
    {
        var builder = new StringBuilder();
        builder.AppendLine("sep=;");
        builder.AppendLine("Id;TarihUtc;Seviye;Kategori;Mesaj;Kullanıcı;UserId;Method;Path;TraceId;Exception");

        foreach (var log in logs)
        {
            builder
                .Append(Csv(log.Id.ToString())).Append(';')
                .Append(Csv(log.CreatedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"))).Append(';')
                .Append(Csv(log.Level)).Append(';')
                .Append(Csv(log.Category)).Append(';')
                .Append(Csv(log.Message)).Append(';')
                .Append(Csv(log.Username)).Append(';')
                .Append(Csv(log.UserId?.ToString())).Append(';')
                .Append(Csv(log.Method)).Append(';')
                .Append(Csv(log.Path)).Append(';')
                .Append(Csv(log.TraceId)).Append(';')
                .Append(Csv(log.Exception))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
