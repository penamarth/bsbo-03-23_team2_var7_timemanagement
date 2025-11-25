using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO;

/// Статусы задачи в системе
/// NotStarted - задача создана, но не начата
/// InProgress - задача в работе
/// Done - задача завершена
/// Overdue - задача просрочена
enum TaskStatus { NotStarted, InProgress, Done, Overdue }

// ===========================
// STATE PATTERN - Управление состояниями задачи
// ===========================
// Для управления поведением задачи в зависимости от её состояния. 
// Каждое состояние знает, какие переходы допустимы и как их обрабатывать.
interface ITaskState
{
    string Name { get; }
    void Start(TaskEntity task, Guid changedBy);
    void Complete(TaskEntity task, Guid changedBy);
    void MarkOverdue(TaskEntity task, Guid changedBy);
}

/// Состояние "Не начата" - начальное состояние задачи
/// Разрешенные переходы: InProgress, Overdue
class NotStartedState : ITaskState
{
    public string Name => "NotStarted";

    public void Start(TaskEntity task, Guid changedBy)
    {
        // При старте задачи фиксируем время начала
        task.StartedAt = DateTime.UtcNow;
        task.SetState(new InProgressState(), changedBy);
    }

    public void Complete(TaskEntity task, Guid changedBy)
    {
        throw new InvalidOperationException("Нельзя завершить задачу, которая не была начата.");
    }

    public void MarkOverdue(TaskEntity task, Guid changedBy)
    {
        // Задача может стать просроченной даже если не начата
        task.SetState(new OverdueState(), changedBy);
    }
}

/// Состояние "В работе" - задача выполняется
/// Разрешенные переходы: Done, Overdue
class InProgressState : ITaskState
{
    public string Name => "InProgress";

    public void Start(TaskEntity task, Guid changedBy)
    {
        throw new InvalidOperationException("Задача уже в работе.");
    }

    public void Complete(TaskEntity task, Guid changedBy)
    {
        // При завершении фиксируем время завершения
        task.CompletedAt = DateTime.UtcNow;
        task.SetState(new DoneState(), changedBy);
    }

    public void MarkOverdue(TaskEntity task, Guid changedBy)
    {
        // Задача в работе может стать просроченной
        task.SetState(new OverdueState(), changedBy);
    }
}

/// Состояние "Завершена" - конечное состояние
/// Большинство переходов запрещено
class DoneState : ITaskState
{
    public string Name => "Done";

    public void Start(TaskEntity task, Guid changedBy)
    {
        // Для переоткрытия задачи требуется специальная логика
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

/// Состояние "Просрочена" - задача не выполнена в срок
/// Разрешенные переходы: InProgress, Done
class OverdueState : ITaskState
{
    public string Name => "Overdue";

    public void Start(TaskEntity task, Guid changedBy)
    {
        // Если просроченную задачу начали - переводим в работу
        if (!task.StartedAt.HasValue) 
            task.StartedAt = DateTime.UtcNow;
        task.SetState(new InProgressState(), changedBy);
    }

    public void Complete(TaskEntity task, Guid changedBy)
    {
        // Просроченную задачу можно завершить
        task.CompletedAt = DateTime.UtcNow;
        task.SetState(new DoneState(), changedBy);
    }

    public void MarkOverdue(TaskEntity task, Guid changedBy)
    {
        // Уже просрочена - просто записываем в историю
        task.RecordHistory(GetTaskStatus(task), TaskStatus.Overdue, changedBy);
    }

    private TaskStatus GetTaskStatus(TaskEntity t) => t.GetStatus();
}

// ===========================
// DOMAIN MODELS - Бизнес-сущности
// ===========================
/// Пользователь системы - исполнитель задач
class User
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public User(string name) { Name = name; }
    public override string ToString() => $"{Name} ({Id.ToString().Substring(0, 6)})";
}

/// Проект - контейнер для задач
class Project
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; }
    public List<TaskEntity> Tasks { get; set; } = new();
    public Project(string name) { Name = name; }
    public override string ToString() => $"{Name} ({Id.ToString().Substring(0, 6)})";
}

/// Запись в истории изменений статуса задачи
class TaskStateHistory
{
    public TaskStatus From { get; set; }
    public TaskStatus To { get; set; }
    public Guid ChangedBy { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// Задача - основная бизнес-сущность
/// Использует State Pattern для управления статусами
class TaskEntity
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid? AssigneeId { get; set; }
    
    // Внутреннее состояние, управляемое шаблоном состояния
    public ITaskState State { get; private set; } = new NotStartedState();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? Deadline { get; set; }
    public List<TaskStateHistory> History { get; set; } = new();

    public TaskEntity() { }

    public TaskEntity(string title)
    {
        Title = title;
        State = new NotStartedState();
    }

    /// Установка нового состояния с записью в историю
    /// Внутренний метод, вызывается из State-объектов
    public void SetState(ITaskState newState, Guid changedBy)
    {
        var oldStatus = GetStatus();
        var newStatus = ParseStatus(newState.Name);
        
        // Записываем переход в историю перед изменением состояния
        RecordHistory(oldStatus, newStatus, changedBy);
        State = newState;
    }

    /// Запись изменения статуса в историю
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

    /// Получение текущего статуса как enum для совместимости
    public TaskStatus GetStatus()
    {
        return ParseStatus(State.Name);
    }

    /// Преобразование строкового имени состояния в enum
    /// Используется для совместимости со старой системой
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

    /// Основной метод для изменения статуса задачи
    /// Инкапсулирует бизнес-логику переходов между статусами
    /// changedBy - ID пользователя (Guid.Empty для системных изменений)
    public void TransitionTo(TaskStatus target, Guid changedBy)
    {
        // Бизнес-логика переходов между статусами
        switch (target)
        {
            case TaskStatus.NotStarted:
                // Перевод в NotStarted разрешен только для переоткрытия завершенных задач
                if (GetStatus() == TaskStatus.Done)
                {
                    // Переоткрытие задачи: сбрасываем дату завершения
                    CompletedAt = null;
                    SetState(new NotStartedState(), changedBy);
                }
                else if (GetStatus() != TaskStatus.NotStarted)
                {
                    // Для других статусов - просто переводим в NotStarted
                    SetState(new NotStartedState(), changedBy);
                }
                break;
            case TaskStatus.InProgress:
                // Делегируем логику старта текущему состоянию
                State.Start(this, changedBy);
                break;
            case TaskStatus.Done:
                // Делегируем логику завершения текущему состоянию
                State.Complete(this, changedBy);
                break;
            case TaskStatus.Overdue:
                // Делегируем логику отметки просрочки текущему состоянию
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

// ===========================
// REPORTS & ANALYSIS - Модуль отчетности и аналитики
// ===========================
/// Параметры для формирования отчета
class ReportParameters
{
    public DateTime From;
    public DateTime To;
    public Guid? ProjectId;
    public Guid? AssigneeId;
    public List<TaskStatus>? Statuses;
    public string Format; // "json","csv","txt"
}

/// Отчет по задачам с аналитикой
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

/// Глобальное хранилище данных приложения (упрощенный Singleton)
static class Data
{
    public static List<User> Users = new();
    public static List<Project> Projects = new();
    public static List<Report> Reports = new();
}

/// Модуль аналитики - расчет метрик по задачам
static class AnalysisModule
{
    /// Анализ задач и формирование отчета
    /// Рассчитывает метрики: время выполнения, статистику по статусам и т.д.
    public static Report Analyze(IEnumerable<TaskEntity> tasks, ReportParameters p)
    {
        var now = DateTime.UtcNow;
        var taskList = tasks.ToList();
        var r = new Report { Parameters = p, Tasks = taskList, Total = taskList.Count };

        // Статистика по статусам
        var counts = new Dictionary<string, int> {
            { "NotStarted", taskList.Count(t => t.GetStatus() == TaskStatus.NotStarted) },
            { "InProgress", taskList.Count(t => t.GetStatus() == TaskStatus.InProgress) },
            { "Done", taskList.Count(t => t.GetStatus() == TaskStatus.Done) },
            { "Overdue", taskList.Count(t => t.GetStatus() == TaskStatus.Overdue) }
        };
        r.CountsByStatus = counts;

        // Расчет времени выполнения задач
        var times = new List<TimeSpan>();
        foreach (var t in taskList)
        {
            TimeSpan? dur = null;
            var status = t.GetStatus();
            
            if (status == TaskStatus.InProgress || status == TaskStatus.Overdue)
            {
                // Для активных задач - время от начала до текущего момента
                if (t.StartedAt.HasValue) dur = now - t.StartedAt.Value;
            }
            else if (status == TaskStatus.Done)
            {
                // Для завершенных - фактическое время выполнения
                if (t.StartedAt.HasValue && t.CompletedAt.HasValue) 
                    dur = t.CompletedAt.Value - t.StartedAt.Value;
            }
            
            if (dur.HasValue && dur.Value.TotalSeconds > 0) 
                times.Add(dur.Value);
        }
        
        r.TotalTime = times.Aggregate(TimeSpan.Zero, (a, b) => a + b);
        r.AvgTime = times.Count > 0 ? TimeSpan.FromTicks((long)times.Average(ts => ts.Ticks)) : TimeSpan.Zero;
        r.PercentDone = r.Total > 0 ? 100.0 * r.CountsByStatus["Done"] / r.Total : 0.0;

        // Анализ сроков выполнения
        int doneOnTime = 0, overdue = 0;
        foreach (var t in taskList)
        {
            var status = t.GetStatus();
            if (status == TaskStatus.Done && t.CompletedAt.HasValue && t.Deadline.HasValue)
            {
                // Проверяем выполнена ли задача в срок
                if (t.CompletedAt.Value <= t.Deadline.Value) 
                    doneOnTime++;
                else 
                    overdue++;
            }
            else if (status == TaskStatus.Overdue)
            {
                // Просроченные задачи
                overdue++;
            }
        }
        r.DoneOnTime = doneOnTime;
        r.Overdue = overdue;

        return r;
    }
}

/// Форматирование отчетов в различные форматы
static class ReportFormatter
{
    /// Форматирование отчета в указанный формат
    /// Поддерживаются: JSON, CSV, текстовый формат
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
            // Текстовый формат по умолчанию
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
        // Поиск проекта, к которому принадлежит задача
        foreach (var p in Data.Projects) 
            if (p.Tasks.Any(x => x.Id == t.Id)) 
                return p.Id.ToString();
        return "";
    }
}

// ===========================
// CLI APPLICATION - Консольное приложение
// ===========================
class Program
{
    static void Main()
    {
        // Инициализация тестовыми данными
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

    /// Инициализация тестовыми данными для демонстрации
    static void Seed()
    {
        var u1 = new User("Алиса");
        var u2 = new User("Боб");
        Data.Users.AddRange(new[] { u1, u2 });
        
        var p = new Project("Демонстрационный проект");
        var t1 = new TaskEntity { Title = "Задача 1", Description = "Первая", Deadline = DateTime.UtcNow.AddDays(2) };
        
        // Создаем просроченную задачу для демонстрации
        var t2 = new TaskEntity { Title = "Задача 2", Description = "Вторая", Deadline = DateTime.UtcNow.AddDays(-1) };
        t2.SetState(new OverdueState(), Guid.Empty);
        
        p.Tasks.Add(t1); 
        p.Tasks.Add(t2);
        Data.Projects.Add(p);
    }

    static void ListUsers()
    {
        Console.WriteLine("Пользователи:");
        foreach (var u in Data.Users) 
            Console.WriteLine($"  {u} ");
    }

    static void ListProjects()
    {
        Console.WriteLine("Проекты:");
        foreach (var p in Data.Projects) 
            Console.WriteLine($"  {p} Задач:{p.Tasks.Count}");
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

    /// Назначение исполнителя на задачу с проверкой нагрузки
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

        // Проверка нагрузки пользователя
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

    /// Изменение статуса задачи с проверкой бизнес-правил
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

        // Проверка специального случая - переоткрытие завершенной задачи
        if (old == TaskStatus.Done && newStatus == TaskStatus.InProgress)
        {
            Console.WriteLine("Переход из Done в InProgress невозможен без подтверждения. Подтвердить? (y/n)");
            var c = Console.ReadLine();
            if (c?.ToLower() != "y") return;
        }

        // Выполняем переход через State Pattern
        task.TransitionTo(newStatus, Guid.Empty);

        Console.WriteLine("Статус изменён.");
    }

    /// Показать историю изменений статуса задачи
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

    /// Поиск задачи по короткому ID (первые 6 символов)
    static TaskEntity FindTaskByShortId(string shortId)
    {
        return Data.Projects.SelectMany(p => p.Tasks)
            .FirstOrDefault(t => t.Id.ToString().StartsWith(shortId));
    }

    /// Генерация отчета по задачам с фильтрацией
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
            To = to.Date.AddDays(1).AddSeconds(-1), // Конец дня
            ProjectId = projectId,
            AssigneeId = assigneeId,
            Statuses = statuses,
            Format = fmt
        };

        // Фильтрация задач по параметрам
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

        // Дополнительная фильтрация по временному периоду
        var filtered = new List<TaskEntity>();
        foreach (var t in allTasks)
        {
            bool include = false;
            
            // Задача включается в отчет если:
            // 1. Создана в указанный период ИЛИ
            // 2. Имела изменения статуса в период ИЛИ 
            // 3. Была завершена в период
            if (t.CreatedAt >= pparams.From && t.CreatedAt <= pparams.To) 
                include = true;
            if (!include && t.History.Any(h => h.ChangedAt >= pparams.From && h.ChangedAt <= pparams.To)) 
                include = true;
            if (!include && t.GetStatus() == TaskStatus.Done &&
                t.CompletedAt.HasValue &&
                t.CompletedAt.Value >= pparams.From &&
                t.CompletedAt.Value <= pparams.To) 
                include = true;

            // Исключаем задачи, завершенные до начала периода отчета
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

        // Генерация и сохранение отчета
        var report = AnalysisModule.Analyze(filtered, pparams);
        Data.Reports.Add(report);

        var text = ReportFormatter.Format(report, fmt);
        var filename = $"report_{report.Id.ToString().Substring(0, 6)}.{fmt}";

        File.WriteAllText(filename, text);

        Console.WriteLine($"Отчёт сформирован: {filename}");
    }
}
