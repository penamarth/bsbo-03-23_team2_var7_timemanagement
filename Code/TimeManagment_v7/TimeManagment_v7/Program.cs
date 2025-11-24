// Program.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;

enum TaskStatus { NotStarted, InProgress, Done, Overdue }

// ---------------------------
// State pattern interfaces & states
// ---------------------------
interface ITaskState
{
    string Name { get; }
    void Start(TaskEntity task, Guid changedBy);
    void Complete(TaskEntity task, Guid changedBy);
    void MarkOverdue(TaskEntity task, Guid changedBy);
}

class NotStartedState : ITaskState
{
    public string Name => "NotStarted";

    public void Start(TaskEntity task, Guid changedBy)
    {
        task.StartedAt = DateTime.UtcNow;
        task.SetState(new InProgressState(), changedBy);
    }

    public void Complete(TaskEntity task, Guid changedBy)
    {
        throw new InvalidOperationException("Нельзя завершить задачу, которая не была начата.");
    }

    public void MarkOverdue(TaskEntity task, Guid changedBy)
    {
        task.SetState(new OverdueState(), changedBy);
    }
}

class InProgressState : ITaskState
{
    public string Name => "InProgress";

    public void Start(TaskEntity task, Guid changedBy)
    {
        throw new InvalidOperationException("Задача уже в работе.");
    }

    public void Complete(TaskEntity task, Guid changedBy)
    {
        task.CompletedAt = DateTime.UtcNow;
        task.SetState(new DoneState(), changedBy);
    }

    public void MarkOverdue(TaskEntity task, Guid changedBy)
    {
        task.SetState(new OverdueState(), changedBy);
    }
}

class DoneState : ITaskState
{
    public string Name => "Done";

    public void Start(TaskEntity task, Guid changedBy)
    {
        // Можно разрешить переоткрытие — но через явное действие (в нашем UI мы переходим в InProgress после подтверждения)
        // Для простоты — переводим в NotStarted при повторном старта не разрешаем
        throw new InvalidOperationException("Нельзя начать завершённую задачу без переоткрытия.");
    }

    public void Complete(TaskEntity task, Guid changedBy)
    {
        throw new InvalidOperationException("Задача уже завершена.");
    }

    public void MarkOverdue(TaskEntity task, Guid changedBy)
    {
        throw new InvalidOperationException("Завершённая задача не может стать просроченной.");
    }
}

class OverdueState : ITaskState
{
    public string Name => "Overdue";

    public void Start(TaskEntity task, Guid changedBy)
    {
        // Если просрочена, но начали работу — переводим в InProgress и ставим StartedAt, если не было
        if (!task.StartedAt.HasValue) task.StartedAt = DateTime.UtcNow;
        task.SetState(new InProgressState(), changedBy);
    }

    public void Complete(TaskEntity task, Guid changedBy)
    {
        task.CompletedAt = DateTime.UtcNow;
        task.SetState(new DoneState(), changedBy);
    }

    public void MarkOverdue(TaskEntity task, Guid changedBy)
    {
        // уже просрочена — ничего не делаем, но можно добавить запись
        task.RecordHistory(GetTaskStatus(task), TaskStatus.Overdue, changedBy);
    }

    private TaskStatus GetTaskStatus(TaskEntity t) => t.GetStatus();
}

// ---------------------------
// Domain models
// ---------------------------
class User
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public User(string name) { Name = name; }
    public override string ToString() => $"{Name} ({Id.ToString().Substring(0, 6)})";
}

class Project
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public List<TaskEntity> Tasks { get; set; } = new();
    public Project(string name) { Name = name; }
    public override string ToString() => $"{Name} ({Id.ToString().Substring(0, 6)})";
}

class TaskStateHistory
{
    public TaskStatus From { get; set; }
    public TaskStatus To { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

class TaskEntity
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid? AssigneeId { get; set; }
    // internal state
    public ITaskState State { get; private set; } = new NotStartedState();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? Deadline { get; set; }
    public List<TaskStateHistory> History { get; set; } = new();

    // Конструктор по умолчанию оставляем
    public TaskEntity() { }

    public TaskEntity(string title)
    {
        Title = title;
        State = new NotStartedState();
    }

    // Установить состояние (используется внутренне из состояний)
    public void SetState(ITaskState newState, Guid changedBy)
    {
        var oldStatus = GetStatus();
        var newStatus = ParseStatus(newState.Name);
        // Записываем в историю переход
        RecordHistory(oldStatus, newStatus, changedBy);
        State = newState;
    }

    // Метод для записи истории (если нужно, вызывается и извне)
    public void RecordHistory(TaskStatus from, TaskStatus to, Guid changedBy)
    {
        History.Add(new TaskStateHistory
        {
            From = from,
            To = to,
            ChangedBy = changedBy,
            ChangedAt = DateTime.UtcNow
        });
    }

    // Возвращает enum-статус для совместимости со старым кодом
    public TaskStatus GetStatus()
    {
        return ParseStatus(State.Name);
    }

    static TaskStatus ParseStatus(string name)
    {
        return name switch
        {
            "NotStarted" => TaskStatus.NotStarted,
            "InProgress" => TaskStatus.InProgress,
            "Done" => TaskStatus.Done,
            "Overdue" => TaskStatus.Overdue,
            _ => TaskStatus.NotStarted
        };
    }

    // Выполняет переход к целевому статусу, инкапсулируя логику переходов через State
    // changedBy — Id пользователя (Guid.Empty, если system)
    public void TransitionTo(TaskStatus target, Guid changedBy)
    {
        // Варианты переходов — вызываем нужный метод состояния
        switch (target)
        {
            case TaskStatus.NotStarted:
                // Для простоты: перевод в NotStarted допустим только вручную (в данном CLI — не используется)
                // Можно реализовать переоткрытие: если было Done и хотят InProgress, в UI обеспечиваем подтверждение и затем Start.
                // Здесь оставим перевод в NotStarted только если текущий — Done и мы хотим переоткрыть — реализуем как сброс.
                if (GetStatus() == TaskStatus.Done)
                {
                    // переоткрыть — перевод в NotStarted и очистить CompletedAt
                    CompletedAt = null;
                    SetState(new NotStartedState(), changedBy);
                }
                else
                {
                    // если уже NotStarted — ничего не делаем
                    if (GetStatus() != TaskStatus.NotStarted)
                        SetState(new NotStartedState(), changedBy);
                }
                break;
            case TaskStatus.InProgress:
                State.Start(this, changedBy);
                break;
            case TaskStatus.Done:
                State.Complete(this, changedBy);
                break;
            case TaskStatus.Overdue:
                State.MarkOverdue(this, changedBy);
                break;
            default:
                throw new InvalidOperationException("Неизвестный целевой статус");
        }
    }

    public override string ToString()
    {
        var ass = AssigneeId.HasValue ? AssigneeId.ToString().Substring(0, 6) : "—";
        var dl = Deadline?.ToString("yyyy-MM-dd") ?? "—";
        return $"{Title} [{Id.ToString().Substring(0, 6)}] Статус:{GetStatus()} Исполнитель:{ass} Дедлайн:{dl}";
    }
}

// ---------------------------
// Reports/Analysis (обновлено для использования GetStatus())
// ---------------------------
class ReportParameters
{
    public DateTime From;
    public DateTime To;
    public Guid? ProjectId;
    public Guid? AssigneeId;
    public List<TaskStatus>? Statuses;
    public string Format; // "json","csv","txt"
}

class Report
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public ReportParameters Parameters { get; set; }
    public List<TaskEntity> Tasks { get; set; } = new();
    public Dictionary<string, int> CountsByStatus { get; set; } = new();
    public int Total { get; set; }
    public double PercentDone { get; set; }
    public int DoneOnTime { get; set; }
    public int Overdue { get; set; }
    public TimeSpan TotalTime { get; set; }
    public TimeSpan AvgTime { get; set; }
}

static class Data
{
    public static List<User> Users = new();
    public static List<Project> Projects = new();
    public static List<Report> Reports = new();
}

class AnalysisModule
{
    public static Report Analyze(IEnumerable<TaskEntity> tasks, ReportParameters p)
    {
        var now = DateTime.UtcNow;
        var taskList = tasks.ToList();
        var r = new Report { Parameters = p, Tasks = taskList, Total = taskList.Count };

        var counts = new Dictionary<string, int> {
            { "NotStarted", taskList.Count(t => t.GetStatus() == TaskStatus.NotStarted) },
            { "InProgress", taskList.Count(t => t.GetStatus() == TaskStatus.InProgress) },
            { "Done", taskList.Count(t => t.GetStatus() == TaskStatus.Done) },
            { "Overdue", taskList.Count(t => t.GetStatus() == TaskStatus.Overdue) }
        };
        r.CountsByStatus = counts;

        var times = new List<TimeSpan>();
        foreach (var t in taskList)
        {
            TimeSpan? dur = null;
            var status = t.GetStatus();
            if (status == TaskStatus.InProgress || status == TaskStatus.Overdue)
            {
                if (t.StartedAt.HasValue) dur = now - t.StartedAt.Value;
            }
            else if (status == TaskStatus.Done)
            {
                if (t.StartedAt.HasValue && t.CompletedAt.HasValue) dur = t.CompletedAt.Value - t.StartedAt.Value;
            }
            if (dur.HasValue && dur.Value.TotalSeconds > 0) times.Add(dur.Value);
        }
        r.TotalTime = times.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        r.AvgTime = times.Count > 0 ? TimeSpan.FromTicks((long)times.Average(ts => ts.Ticks)) : TimeSpan.Zero;
        r.PercentDone = r.Total > 0 ? 100.0 * r.CountsByStatus["Done"] / r.Total : 0.0;

        int doneOnTime = 0, overdue = 0;
        foreach (var t in taskList)
        {
            var status = t.GetStatus();
            if (status == TaskStatus.Done && t.CompletedAt.HasValue && t.Deadline.HasValue)
            {
                if (t.CompletedAt.Value <= t.Deadline.Value) doneOnTime++;
                else overdue++;
            }
            else if (status == TaskStatus.Overdue) overdue++;
        }
        r.DoneOnTime = doneOnTime;
        r.Overdue = overdue;

        return r;
    }
}

static class ReportFormatter
{
    public static string Format(Report r, string format)
    {
        format = format.ToLowerInvariant();
        if (format == "json")
        {
            var opts = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(r, opts);
        }
        else if (format == "csv")
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tasks");
            sb.AppendLine("Id,Title,ProjectId,AssigneeId,Status,CreatedAt,StartedAt,CompletedAt,Deadline");
            foreach (var t in r.Tasks)
            {
                sb.AppendLine($"{t.Id},{EscapeCsv(t.Title)},{GetProjectIdForTask(t)},{t.AssigneeId?.ToString() ?? ""},{t.GetStatus()},{t.CreatedAt:o},{t.StartedAt?.ToString("o") ?? ""},{t.CompletedAt?.ToString("o") ?? ""},{t.Deadline?.ToString("o") ?? ""}");
            }
            sb.AppendLine();
            sb.AppendLine("Statistics");
            sb.AppendLine("Total,NotStarted,InProgress,Done,Overdue,PercentDone,DoneOnTime,OverdueCount,TotalTimeSeconds,AvgTimeSeconds,GeneratedAt");
            sb.AppendLine($"{r.Total},{r.CountsByStatus["NotStarted"]},{r.CountsByStatus["InProgress"]},{r.CountsByStatus["Done"]},{r.CountsByStatus["Overdue"]},{r.PercentDone:F2},{r.DoneOnTime},{r.Overdue},{r.TotalTime.TotalSeconds},{r.AvgTime.TotalSeconds},{r.GeneratedAt:o}");
            return sb.ToString();
        }
        else
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Отчёт {r.Id} создан {r.GeneratedAt:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"Период: {r.Parameters.From:yyyy-MM-dd} — {r.Parameters.To:yyyy-MM-dd}");
            sb.AppendLine($"Всего задач: {r.Total}");
            sb.AppendLine("По статусам:");
            foreach (var kv in r.CountsByStatus) sb.AppendLine($"  {kv.Key}: {kv.Value}");
            sb.AppendLine($"Процент выполненных: {r.PercentDone:F2}%");
            sb.AppendLine($"Выполнено в срок: {r.DoneOnTime}");
            sb.AppendLine($"Просрочено: {r.Overdue}");
            sb.AppendLine($"Общее время (сек): {r.TotalTime.TotalSeconds}");
            sb.AppendLine($"Среднее время (сек): {r.AvgTime.TotalSeconds}");
            sb.AppendLine();
            sb.AppendLine("Задачи:");
            foreach (var t in r.Tasks)
            {
                sb.AppendLine($"- {t.Title} [{t.Id.ToString().Substring(0, 6)}] Статус:{t.GetStatus()} Исполнитель:{(t.AssigneeId?.ToString().Substring(0, 6) ?? "—")}");
            }
            return sb.ToString();
        }
    }

    static string EscapeCsv(string s) => $"\"{s?.Replace("\"", "\"\"")}\"";
    static string GetProjectIdForTask(TaskEntity t)
    {
        foreach (var p in Data.Projects) if (p.Tasks.Any(x => x.Id == t.Id)) return p.Id.ToString();
        return "";
    }
}

// ---------------------------
// CLI application
// ---------------------------
class Program
{
    static void Main()
    {
        Seed();
        Console.WriteLine("Простое CLI-приложение управления задачами (демо).");
        while (true)
        {
            PrintMenu();
            var cmd = Console.ReadLine()?.Trim();
            if (string.IsNullOrEmpty(cmd)) continue;
            if (cmd == "exit") break;
            try
            {
                Handle(cmd);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Ошибка: " + ex.Message);
            }
        }
    }

    static void PrintMenu()
    {
        Console.WriteLine();
        Console.WriteLine("Команды:");
        Console.WriteLine("  users                - показать пользователей");
        Console.WriteLine("  projects             - показать проекты");
        Console.WriteLine("  newproject           - создать проект");
        Console.WriteLine("  newtask              - создать задачу");
        Console.WriteLine("  tasks                - показать все задачи");
        Console.WriteLine("  assign               - назначить исполнителя");
        Console.WriteLine("  changestatus         - изменить статус задачи");
        Console.WriteLine("  history              - история изменений задачи");
        Console.WriteLine("  report               - создать отчёт");
        Console.WriteLine("  exit                 - выйти");
        Console.Write("Введите команду: ");
    }

    static void Handle(string cmd)
    {
        switch (cmd)
        {
            case "users": ListUsers(); break;
            case "projects": ListProjects(); break;
            case "newproject": CreateProject(); break;
            case "newtask": CreateTask(); break;
            case "tasks": ListTasks(); break;
            case "assign": Assign(); break;
            case "changestatus": ChangeStatus(); break;
            case "history": ShowHistory(); break;
            case "report": GenerateReport(); break;
            default: Console.WriteLine("Неизвестная команда"); break;
        }
    }

    static void Seed()
    {
        var u1 = new User("Алиса");
        var u2 = new User("Боб");
        Data.Users.AddRange(new[] { u1, u2 });
        var p = new Project("Демонстрационный проект");
        var t1 = new TaskEntity { Title = "Задача 1", Description = "Первая", Deadline = DateTime.UtcNow.AddDays(2) };
        // поместим t2 в состояние Overdue
        var t2 = new TaskEntity { Title = "Задача 2", Description = "Вторая", Deadline = DateTime.UtcNow.AddDays(-1) };
        t2.SetState(new OverdueState(), Guid.Empty);
        p.Tasks.Add(t1); p.Tasks.Add(t2);
        Data.Projects.Add(p);
    }

    static void ListUsers()
    {
        Console.WriteLine("Пользователи:");
        foreach (var u in Data.Users) Console.WriteLine($"  {u} ");
    }

    static void ListProjects()
    {
        Console.WriteLine("Проекты:");
        foreach (var p in Data.Projects) Console.WriteLine($"  {p} Задач:{p.Tasks.Count}");
    }

    static void CreateProject()
    {
        Console.Write("Название проекта: ");
        var name = Console.ReadLine();
        var p = new Project(name);
        Data.Projects.Add(p);
        Console.WriteLine($"Проект создан: {p}");
    }

    static void CreateTask()
    {
        Console.Write("ID проекта (первые 6 символов): ");
        var pid = Console.ReadLine();
        var project = Data.Projects.FirstOrDefault(p => p.Id.ToString().StartsWith(pid));
        if (project == null) { Console.WriteLine("Проект не найден"); return; }

        Console.Write("Название задачи: ");
        var title = Console.ReadLine();

        Console.Write("Описание: ");
        var desc = Console.ReadLine();

        Console.Write("Дедлайн (yyyy-MM-dd) или пусто: ");
        var dl = Console.ReadLine();
        DateTime? deadline = null;

        if (!string.IsNullOrWhiteSpace(dl))
        {
            if (DateTime.TryParse(dl, out var d)) deadline = d;
        }

        var t = new TaskEntity { Title = title, Description = desc, Deadline = deadline, CreatedAt = DateTime.UtcNow };
        project.Tasks.Add(t);

        Console.WriteLine($"Задача создана: {t}");
    }

    static void ListTasks()
    {
        Console.WriteLine("Все задачи:");
        foreach (var p in Data.Projects)
        {
            Console.WriteLine($"Проект: {p.Name} [{p.Id.ToString().Substring(0, 6)}]");
            foreach (var t in p.Tasks)
            {
                Console.WriteLine($"  {t}");
            }
        }
    }

    static void Assign()
    {
        Console.Write("ID задачи (первые 6 символов): ");
        var tid = Console.ReadLine();

        var task = FindTaskByShortId(tid);
        if (task == null) { Console.WriteLine("Задача не найдена"); return; }

        Console.WriteLine("Пользователи:");
        foreach (var u in Data.Users) Console.WriteLine($"  {u}");

        Console.Write("ID пользователя (первые 6 символов): ");
        var uid = Console.ReadLine();

        var user = Data.Users.FirstOrDefault(x => x.Id.ToString().StartsWith(uid));
        if (user == null) { Console.WriteLine("Пользователь не найден"); return; }

        var userTasks = Data.Projects.SelectMany(p => p.Tasks)
            .Count(t => t.AssigneeId == user.Id && t.GetStatus() == TaskStatus.InProgress);

        if (userTasks >= 3)
        {
            Console.WriteLine("Внимание: у пользователя высокая нагрузка. Подтвердить назначение? (y/n)");
            var c = Console.ReadLine();
            if (c?.ToLower() != "y") { Console.WriteLine("Отменено"); return; }
        }

        task.AssigneeId = user.Id;
        Console.WriteLine("Исполнитель назначен.");
    }

    static void ChangeStatus()
    {
        Console.Write("ID задачи (первые 6 символов): ");
        var tid = Console.ReadLine();

        var task = FindTaskByShortId(tid);
        if (task == null) { Console.WriteLine("Задача не найдена"); return; }

        Console.WriteLine($"Текущий статус: {task.GetStatus()}");
        Console.WriteLine("Выберите новый статус: 0=NotStarted, 1=InProgress, 2=Done, 3=Overdue");

        var s = Console.ReadLine();
        if (!int.TryParse(s, out var si) || si < 0 || si > 3) { Console.WriteLine("Некорректный статус"); return; }

        var newStatus = (TaskStatus)si;
        var old = task.GetStatus();

        if (old == TaskStatus.Done && newStatus == TaskStatus.InProgress)
        {
            Console.WriteLine("Переход из Done в InProgress невозможен без подтверждения. Подтвердить? (y/n)");
            var c = Console.ReadLine();
            if (c?.ToLower() != "y") return;
        }

        // выполняем переход, метод сам записывает историю и выставляет даты
        task.TransitionTo(newStatus, Guid.Empty);

        Console.WriteLine("Статус изменён.");
    }

    static void ShowHistory()
    {
        Console.Write("ID задачи (первые 6 символов): ");
        var tid = Console.ReadLine();

        var task = FindTaskByShortId(tid);
        if (task == null) { Console.WriteLine("Задача не найдена"); return; }

        Console.WriteLine($"История изменений задачи {task.Title}:");

        foreach (var h in task.History.OrderBy(h => h.ChangedAt))
        {
            Console.WriteLine($"  {h.ChangedAt:yyyy-MM-dd HH:mm} {h.From} -> {h.To} (изменил: {h.ChangedBy.ToString().Substring(0, 6)})");
        }
    }

    static TaskEntity FindTaskByShortId(string shortId)
    {
        return Data.Projects.SelectMany(p => p.Tasks)
            .FirstOrDefault(t => t.Id.ToString().StartsWith(shortId));
    }

    static void GenerateReport()
    {
        Console.Write("С даты (yyyy-MM-dd): ");
        if (!DateTime.TryParse(Console.ReadLine(), out var from)) { Console.WriteLine("Неверная дата"); return; }

        Console.Write("По дату (yyyy-MM-dd): ");
        if (!DateTime.TryParse(Console.ReadLine(), out var to)) { Console.WriteLine("Неверная дата"); return; }

        Console.Write("ID проекта (первые 6 символов) или пусто=все: ");
        var pid = Console.ReadLine();

        Guid? projectId = null;
        if (!string.IsNullOrWhiteSpace(pid))
        {
            var p = Data.Projects.FirstOrDefault(x => x.Id.ToString().StartsWith(pid));
            if (p == null) { Console.WriteLine("Проект не найден"); return; }
            projectId = p.Id;
        }

        Console.Write("ID исполнителя (первые 6 символов) или пусто=все: ");
        var aid = Console.ReadLine();

        Guid? assigneeId = null;
        if (!string.IsNullOrWhiteSpace(aid))
        {
            var u = Data.Users.FirstOrDefault(x => x.Id.ToString().StartsWith(aid));
            if (u == null) { Console.WriteLine("Пользователь не найден"); return; }
            assigneeId = u.Id;
        }

        Console.Write("Статусы (перечислить через запятую: NotStarted,InProgress,Done,Overdue) или пусто: ");
        var ss = Console.ReadLine();

        List<TaskStatus>? statuses = null;
        if (!string.IsNullOrWhiteSpace(ss))
        {
            statuses = ss.Split(',')
                .Select(x => Enum.Parse<TaskStatus>(x.Trim()))
                .ToList();
        }

        Console.Write("Формат отчёта (json/csv/txt): ");
        var fmt = Console.ReadLine()?.ToLower() ?? "txt";

        var pparams = new ReportParameters
        {
            From = from.Date,
            To = to.Date.AddDays(1).AddSeconds(-1),
            ProjectId = projectId,
            AssigneeId = assigneeId,
            Statuses = statuses,
            Format = fmt
        };

        var allTasks = Data.Projects.SelectMany(p => p.Tasks);

        if (projectId.HasValue)
        {
            allTasks = allTasks.Where(t =>
                Data.Projects.Any(p => p.Id == projectId.Value && p.Tasks.Any(x => x.Id == t.Id)));
        }

        if (assigneeId.HasValue)
        {
            allTasks = allTasks.Where(t => t.AssigneeId == assigneeId.Value);
        }

        if (statuses != null && statuses.Any())
        {
            allTasks = allTasks.Where(t => statuses.Contains(t.GetStatus()));
        }

        var filtered = new List<TaskEntity>();
        foreach (var t in allTasks)
        {
            bool include = false;
            if (t.CreatedAt >= pparams.From && t.CreatedAt <= pparams.To) include = true;
            if (!include && t.History.Any(h => h.ChangedAt >= pparams.From && h.ChangedAt <= pparams.To)) include = true;
            if (!include && t.GetStatus() == TaskStatus.Done &&
                t.CompletedAt.HasValue &&
                t.CompletedAt.Value >= pparams.From &&
                t.CompletedAt.Value <= pparams.To) include = true;

            if (t.GetStatus() == TaskStatus.Done &&
                t.CompletedAt.HasValue &&
                t.CompletedAt.Value < pparams.From)
                include = false;

            if (include) filtered.Add(t);
        }

        if (!filtered.Any())
        {
            Console.WriteLine("По заданным параметрам задачи не найдены.");
            return;
        }

        var report = AnalysisModule.Analyze(filtered, pparams);
        Data.Reports.Add(report);

        var text = ReportFormatter.Format(report, fmt);
        var filename = $"report_{report.Id.ToString().Substring(0, 6)}.{fmt}";

        File.WriteAllText(filename, text);

        Console.WriteLine($"Отчёт сформирован: {filename}");
    }
}
