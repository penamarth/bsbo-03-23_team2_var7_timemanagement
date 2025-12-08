// TimeTrackingSystem.Reporting.cs
// Модуль форматирования отчетов
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using TimeTrackingSystem.Core;

namespace TimeTrackingSystem.Reporting
{
    public interface IReportFormatter
    {
        string Format(Report report);
    }

    public class JsonReportFormatter : IReportFormatter
    {
        public string Format(Report report)
        {
            var data = new
            {
                report.Id,
                report.GeneratedAt,
                Parameters = new
                {
                    report.Parameters.From,
                    report.Parameters.To,
                    report.Parameters.ProjectId,
                    report.Parameters.AssigneeId,
                    report.Parameters.Statuses,
                    report.Parameters.Format
                },
                Statistics = new
                {
                    report.Total,
                    report.CountsByStatus,
                    report.PercentDone,
                    report.DoneOnTime,
                    report.Overdue,
                    TotalTimeSeconds = report.TotalTime.TotalSeconds,
                    AvgTimeSeconds = report.AvgTime.TotalSeconds
                },
                Tasks = report.Tasks.Select(t => new
                {
                    t.Id,
                    t.Title,
                    t.Description,
                    t.AssigneeId,
                    Status = ((Core.TaskStatus)t.GetStatus()).ToString(),
                    t.CreatedAt,
                    t.StartedAt,
                    t.CompletedAt,
                    t.Deadline
                })
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(data, options);
        }
    }

    public class CsvReportFormatter : IReportFormatter
    {
        public string Format(Report report)
        {
            var sb = new StringBuilder();

            // Таблица задач
            sb.AppendLine("Tasks");
            sb.AppendLine("Id,Title,Description,AssigneeId,Status,CreatedAt,StartedAt,CompletedAt,Deadline");

            foreach (var t in report.Tasks)
            {
                sb.AppendLine($"{t.Id}," +
                    $"{EscapeCsv(t.Title)}," +
                    $"{EscapeCsv(t.Description)}," +
                    $"{t.AssigneeId?.ToString() ?? ""}," +
                    $"{t.GetStatus()}," +
                    $"{t.CreatedAt:o}," +
                    $"{t.StartedAt?.ToString("o") ?? ""}," +
                    $"{t.CompletedAt?.ToString("o") ?? ""}," +
                    $"{t.Deadline?.ToString("o") ?? ""}");
            }

            sb.AppendLine();

            // Таблица статистики
            sb.AppendLine("Statistics");
            sb.AppendLine("Total,NotStarted,InProgress,Done,Overdue,PercentDone,DoneOnTime,OverdueCount,TotalTimeSeconds,AvgTimeSeconds,GeneratedAt");
            sb.AppendLine($"{report.Total}," +
                $"{report.CountsByStatus["NotStarted"]}," +
                $"{report.CountsByStatus["InProgress"]}," +
                $"{report.CountsByStatus["Done"]}," +
                $"{report.CountsByStatus["Overdue"]}," +
                $"{report.PercentDone:F2}," +
                $"{report.DoneOnTime}," +
                $"{report.Overdue}," +
                $"{report.TotalTime.TotalSeconds}," +
                $"{report.AvgTime.TotalSeconds}," +
                $"{report.GeneratedAt:o}");

            return sb.ToString();
        }

        private string EscapeCsv(string s) => $"\"{s?.Replace("\"", "\"\"")}\"";
    }

    public class TxtReportFormatter : IReportFormatter
    {
        public string Format(Report report)
        {
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine($"ОТЧЁТ О ВЫПОЛНЕНИИ ЗАДАЧ");
            sb.AppendLine("═══════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"ID отчёта: {report.Id}");
            sb.AppendLine($"Дата формирования: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine();
            sb.AppendLine("ПАРАМЕТРЫ ОТЧЁТА:");
            sb.AppendLine($"  Период: {report.Parameters.From:yyyy-MM-dd} — {report.Parameters.To:yyyy-MM-dd}");

            if (report.Parameters.ProjectId.HasValue)
                sb.AppendLine($"  Проект: {report.Parameters.ProjectId}");
            else
                sb.AppendLine("  Проект: Все проекты");

            if (report.Parameters.AssigneeId.HasValue)
                sb.AppendLine($"  Исполнитель: {report.Parameters.AssigneeId}");
            else
                sb.AppendLine("  Исполнитель: Все исполнители");

            if (report.Parameters.Statuses != null && report.Parameters.Statuses.Any())
                sb.AppendLine($"  Статусы: {string.Join(", ", report.Parameters.Statuses)}");
            else
                sb.AppendLine("  Статусы: Все");

            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine("СТАТИСТИКА");
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine($"Всего задач: {report.Total}");
            sb.AppendLine();
            sb.AppendLine("Распределение по статусам:");
            foreach (var kv in report.CountsByStatus)
            {
                sb.AppendLine($"  {kv.Key,-15}: {kv.Value,3}");
            }
            sb.AppendLine();
            sb.AppendLine($"Процент выполненных задач: {report.PercentDone:F2}%");
            sb.AppendLine($"Выполнено в срок: {report.DoneOnTime}");
            sb.AppendLine($"Просрочено: {report.Overdue}");
            sb.AppendLine();
            sb.AppendLine($"Общее затраченное время: {FormatTimeSpan(report.TotalTime)}");
            sb.AppendLine($"Среднее время на задачу: {FormatTimeSpan(report.AvgTime)}");

            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────");
            sb.AppendLine("СПИСОК ЗАДАЧ");
            sb.AppendLine("───────────────────────────────────────────────────────");

            foreach (var t in report.Tasks)
            {
                sb.AppendLine();
                sb.AppendLine($"• {t.Title}");
                sb.AppendLine($"  ID: {t.Id.ToString().Substring(0, 8)}...");
                sb.AppendLine($"  Статус: {t.GetStatus()}");
                sb.AppendLine($"  Исполнитель: {(t.AssigneeId?.ToString().Substring(0, 8) ?? "Не назначен")}");
                sb.AppendLine($"  Создана: {t.CreatedAt:yyyy-MM-dd HH:mm}");

                if (t.StartedAt.HasValue)
                    sb.AppendLine($"  Начата: {t.StartedAt.Value:yyyy-MM-dd HH:mm}");

                if (t.CompletedAt.HasValue)
                    sb.AppendLine($"  Завершена: {t.CompletedAt.Value:yyyy-MM-dd HH:mm}");

                if (t.Deadline.HasValue)
                    sb.AppendLine($"  Дедлайн: {t.Deadline.Value:yyyy-MM-dd}");

                if (!string.IsNullOrWhiteSpace(t.Description))
                    sb.AppendLine($"  Описание: {t.Description}");
            }

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════");

            return sb.ToString();
        }

        private string FormatTimeSpan(TimeSpan ts)
        {
            if (ts.TotalDays >= 1)
                return $"{ts.Days} дн. {ts.Hours} ч. {ts.Minutes} мин.";
            else if (ts.TotalHours >= 1)
                return $"{ts.Hours} ч. {ts.Minutes} мин.";
            else if (ts.TotalMinutes >= 1)
                return $"{ts.Minutes} мин. {ts.Seconds} сек.";
            else
                return $"{ts.Seconds} сек.";
        }
    }

    // Factory для создания форматтеров
    public static class ReportFormatterFactory
    {
        public static IReportFormatter Create(string format)
        {
            return format?.ToLowerInvariant() switch
            {
                "json" => new JsonReportFormatter(),
                "csv" => new CsvReportFormatter(),
                "txt" => new TxtReportFormatter(),
                _ => new TxtReportFormatter()
            };
        }
    }
}