// TimeTrackingSystem.CLI.cs
// Чистый UI — только ввод/вывод, без бизнес-логики
using System;
using System.IO;
using System.Linq;
using TimeTrackingSystem.Core;
using TimeTrackingSystem.Reporting;

namespace TimeTrackingSystem.CLI
{
    public class CliApplication
    {
        private readonly ProjectManagementService _projectService;
        private readonly ReportingService _reportingService;
        private readonly TaskWorkflowService _workflow;
        private readonly SprintManagementService _sprintService;

        public CliApplication()
        {
            _projectService = new ProjectManagementService();
            _reportingService = new ReportingService();
            _workflow = new TaskWorkflowService(_projectService);
            _sprintService = new SprintManagementService(_projectService);
            Seed();
        }

        public void Run()
        {
            Console.WriteLine("═══════════════════════════════════════════════════════");
            Console.WriteLine("  СИСТЕМА УЧЁТА РАБОЧЕГО ВРЕМЕНИ");
            Console.WriteLine("═══════════════════════════════════════════════════════");

            while (true)
            {
                PrintMenu();
                var cmd = Console.ReadLine()?.Trim().ToLower();
                if (cmd == "exit") break;
                try { Handle(cmd); }
                catch (Exception ex) { Console.WriteLine($"❌ {ex.Message}"); }
            }
        }

        private void PrintMenu()
        {
            Console.WriteLine("\nКОМАНДЫ:");
            Console.WriteLine(" users       | projects    | newproject  | editproject");
            Console.WriteLine(" newtask     | edittask    | assign      | start");
            Console.WriteLine(" complete    | overdue     | tasks       | history");
            Console.WriteLine(" sprints     | newsprint   | editsprint  | addsptask");
            Console.WriteLine(" endsprint   | sprinttasks | report      | exit");
            Console.Write("Введите команду: ");
        }

        private void Handle(string c)
        {
            switch (c)
            {
                case "users": ListUsers(); break;
                case "projects": ListProjects(); break;
                case "newproject": CreateProject(); break;
                case "editproject": EditProject(); break;
                case "newtask": CreateTask(); break;
                case "edittask": EditTask(); break;
                case "assign": AssignTask(); break;
                case "start": ChangeStatus(_workflow.Start, "начата"); break;
                case "complete": ChangeStatus(_workflow.Complete, "завершена"); break;
                case "overdue": ChangeStatus(_workflow.MarkOverdue, "просрочена"); break;
                case "tasks": ListTasks(); break;
                case "history": ShowHistory(); break;
                case "sprints": ListSprints(); break;
                case "newsprint": CreateSprint(); break;
                case "editsprint": EditSprint(); break;
                case "addsptask": AddTaskToSprint(); break;
                case "endsprint": EndSprint(); break;
                case "sprinttasks": ShowSprintTasks(); break;
                case "report": GenerateReport(); break;
                default: Console.WriteLine("❌ Неизвестная команда"); break;
            }
        }

        private void ListUsers()
        {
            Console.WriteLine("\n👥 Пользователи:");
            foreach (var u in _projectService.GetUsers())
                Console.WriteLine($" • {u}");
        }

        private void ListProjects()
        {
            Console.WriteLine("\n📁 Проекты:");
            foreach (var p in _projectService.GetProjects())
                Console.WriteLine($" • {p.Name} [{p.Id.ToString().Substring(0, 6)}] - {p.Description}");
        }

        private void CreateProject()
        {
            Console.Write("\nНазвание: ");
            var name = Console.ReadLine();
            Console.Write("Описание: ");
            var desc = Console.ReadLine();
            var p = _projectService.CreateProject(name, desc);
            Console.WriteLine($"✓ Проект создан: {p.Name}");
        }

        private void EditProject()
        {
            ListProjects();
            Console.Write("\nID проекта (первые 6 символов): ");
            var id = Console.ReadLine();
            var project = _projectService.GetProjects()
                .FirstOrDefault(p => p.Id.ToString().StartsWith(id));
            if (project == null) { Console.WriteLine("❌ Проект не найден"); return; }

            Console.Write($"Новое название (текущее: {project.Name}): ");
            var name = Console.ReadLine();
            Console.Write($"Новое описание (текущее: {project.Description}): ");
            var desc = Console.ReadLine();

            _projectService.UpdateProject(project.Id, name, desc);
            Console.WriteLine("✓ Проект обновлён");
        }

        private void CreateTask()
        {
            ListProjects();
            Console.Write("\nID проекта (первые 6 символов): ");
            var id = Console.ReadLine();
            var project = _projectService.GetProjects()
                .FirstOrDefault(p => p.Id.ToString().StartsWith(id));
            if (project == null) { Console.WriteLine("❌ Проект не найден"); return; }

            Console.Write("Название задачи: ");
            var title = Console.ReadLine();
            Console.Write("Описание: ");
            var desc = Console.ReadLine();
            Console.Write("Дедлайн (yyyy-MM-dd, Enter для пропуска): ");
            DateTime? deadline = DateTime.TryParse(Console.ReadLine(), out var d) ? d : null;

            var task = _projectService.CreateTask(project.Id, title, desc, deadline);
            Console.WriteLine($"✓ Задача создана: {task.Title}");
        }

        private void EditTask()
        {
            var task = SelectTask();
            if (task == null) return;

            Console.Write($"Новое название (текущее: {task.Title}): ");
            var title = Console.ReadLine();
            Console.Write($"Новое описание (текущее: {task.Description}): ");
            var desc = Console.ReadLine();
            Console.Write($"Новый дедлайн (текущий: {task.Deadline?.ToString("yyyy-MM-dd") ?? "нет"}): ");
            DateTime? deadline = DateTime.TryParse(Console.ReadLine(), out var d) ? d : null;

            _projectService.UpdateTask(task.Id, title, desc, deadline);
            Console.WriteLine("✓ Задача обновлена");
        }

        private void AssignTask()
        {
            var task = SelectTask();
            if (task == null) return;
            ListUsers();
            Console.Write("\nID пользователя (первые 6 символов): ");
            var id = Console.ReadLine();
            var user = _projectService.GetUsers()
                .FirstOrDefault(u => u.Id.ToString().StartsWith(id));
            if (user == null) { Console.WriteLine("❌ Пользователь не найден"); return; }
            _projectService.AssignTask(task.Id, user.Id);
            Console.WriteLine($"✓ Назначено: {user.Name}");
        }

        private void ChangeStatus(Action<Guid> action, string msg)
        {
            var task = SelectTask();
            if (task == null) return;
            action(task.Id);
            Console.WriteLine($"✓ Задача {msg}");
        }

        private void ListTasks()
        {
            Console.WriteLine("\n📋 Задачи:");
            foreach (var p in _projectService.GetProjects())
            {
                Console.WriteLine($"\n📁 {p.Name}");
                if (!p.Tasks.Any()) Console.WriteLine("  (нет задач)");
                foreach (var t in p.Tasks)
                {
                    var assignee = t.AssigneeId.HasValue
                        ? _projectService.GetUser(t.AssigneeId.Value)?.Name ?? "Не найден"
                        : "Не назначен";
                    Console.WriteLine($" [{t.Id.ToString().Substring(0, 6)}] {t.Title} | {t.GetStatus()} | {assignee}");
                }
            }
        }

        private void ShowHistory()
        {
            var t = SelectTask();
            if (t == null) return;
            Console.WriteLine($"\nИстория {t.Title}:");
            if (!t.History.Any())
            {
                Console.WriteLine("  (история пуста)");
                return;
            }
            foreach (var h in t.History)
                Console.WriteLine($" {h.ChangedAt:yyyy-MM-dd HH:mm}: {h.From} → {h.To}");
        }

        private void ListSprints()
        {
            Console.WriteLine("\n🏃 Спринты:");
            foreach (var s in _sprintService.GetSprints())
                Console.WriteLine($" • {s}");
        }

        private void CreateSprint()
        {
            Console.Write("\nНазвание спринта: ");
            var name = Console.ReadLine();
            Console.Write("Цель спринта: ");
            var goal = Console.ReadLine();
            Console.Write("Дата начала (yyyy-MM-dd): ");
            var start = DateTime.Parse(Console.ReadLine());
            Console.Write("Дедлайн (yyyy-MM-dd, Enter для пропуска): ");
            DateTime? deadline = DateTime.TryParse(Console.ReadLine(), out var d) ? d : null;

            var sprint = _sprintService.CreateSprint(name, goal, start, deadline);
            Console.WriteLine($"✓ Спринт создан: {sprint.Name}");
        }

        private void EditSprint()
        {
            var sprint = SelectSprint();
            if (sprint == null) return;

            Console.Write($"Новое название (текущее: {sprint.Name}): ");
            var name = Console.ReadLine();
            Console.Write($"Новая цель (текущая: {sprint.Goal}): ");
            var goal = Console.ReadLine();
            Console.Write($"Новая дата начала (текущая: {sprint.StartDate:yyyy-MM-dd}): ");
            DateTime? start = DateTime.TryParse(Console.ReadLine(), out var s) ? s : null;
            Console.Write($"Новый дедлайн (текущий: {sprint.Deadline?.ToString("yyyy-MM-dd") ?? "нет"}): ");
            DateTime? deadline = DateTime.TryParse(Console.ReadLine(), out var d) ? d : null;

            _sprintService.UpdateSprint(sprint.Id, name, goal, start, deadline);
            Console.WriteLine("✓ Спринт обновлён");
        }

        private void AddTaskToSprint()
        {
            var sprint = SelectSprint();
            if (sprint == null) return;

            var task = SelectTask();
            if (task == null) return;

            _sprintService.AddTask(sprint.Id, task.Id);
            Console.WriteLine("✓ Задача добавлена в спринт");
        }

        private void EndSprint()
        {
            var sprint = SelectSprint();
            if (sprint == null) return;

            _sprintService.CompleteSprint(sprint.Id);
            Console.WriteLine("✓ Спринт завершён");
        }

        private void ShowSprintTasks()
        {
            var sprint = SelectSprint();
            if (sprint == null) return;

            Console.WriteLine($"\n📋 Задачи спринта {sprint.Name}:");
            var tasks = _sprintService.GetSprintTasks(sprint.Id);
            if (!tasks.Any())
            {
                Console.WriteLine("  (нет задач)");
                return;
            }
            foreach (var t in tasks)
                Console.WriteLine($" • [{t.Id.ToString().Substring(0, 6)}] {t.Title} | {t.GetStatus()}");
        }

        private void GenerateReport()
        {
            Console.Write("Дата начала (yyyy-MM-dd): ");
            DateTime from = DateTime.Parse(Console.ReadLine());
            Console.Write("Дата конца (yyyy-MM-dd): ");
            DateTime to = DateTime.Parse(Console.ReadLine());
            Console.Write("Формат (json/csv/txt): ");
            var format = Console.ReadLine()?.ToLower() ?? "txt";

            var p = new ReportParameters { From = from, To = to, Format = format };

            var tasks = _projectService.GetProjects().SelectMany(x => x.Tasks);
            var report = _reportingService.GenerateReport(p, tasks);
            var content = ReportFormatterFactory.Create(format).Format(report);
            var file = $"report_{DateTime.Now:yyyyMMdd_HHmmss}.{format}";
            File.WriteAllText(file, content);
            Console.WriteLine($"✓ Отчёт сохранён: {file}");
        }

        private TaskEntity SelectTask()
        {
            Console.Write("ID задачи (первые 6 символов): ");
            var id = Console.ReadLine();
            var task = _projectService.GetProjects()
                .SelectMany(x => x.Tasks)
                .FirstOrDefault(t => t.Id.ToString().StartsWith(id));
            if (task == null) Console.WriteLine("❌ Задача не найдена");
            return task;
        }

        private Sprint SelectSprint()
        {
            ListSprints();
            Console.Write("ID спринта (первые 6 символов): ");
            var id = Console.ReadLine();
            var sprint = _sprintService.GetSprints()
                .FirstOrDefault(s => s.Id.ToString().StartsWith(id));
            if (sprint == null) Console.WriteLine("❌ Спринт не найден");
            return sprint;
        }

        private void Seed()
        {
            // Создаём пользователей
            var alice = _projectService.AddUser("Алиса");
            var bob = _projectService.AddUser("Боб");
            var charlie = _projectService.AddUser("Чарли");

            // Создаём проект
            var project = _projectService.CreateProject("Демо проект", "Пример для демонстрации");

            // Создаём задачи
            var task1 = _projectService.CreateTask(project.Id, "Разработать API", "REST API для системы", DateTime.UtcNow.AddDays(7));
            var task2 = _projectService.CreateTask(project.Id, "Написать документацию", "Техническая документация", DateTime.UtcNow.AddDays(3));
            var task3 = _projectService.CreateTask(project.Id, "Тестирование", "Unit и интеграционные тесты", DateTime.UtcNow.AddDays(10));

            // Назначаем исполнителей
            _projectService.AssignTask(task1.Id, alice.Id);
            _projectService.AssignTask(task2.Id, bob.Id);

            // Запускаем одну задачу
            _workflow.Start(task2.Id);

            // Создаём спринт
            var sprint = _sprintService.CreateSprint("Спринт 1", "Разработать базовый функционал", DateTime.UtcNow, DateTime.UtcNow.AddDays(14));
            _sprintService.AddParticipant(sprint.Id, alice.Id);
            _sprintService.AddParticipant(sprint.Id, bob.Id);
            _sprintService.AddTask(sprint.Id, task1.Id);
            _sprintService.AddTask(sprint.Id, task2.Id);
        }
    }

    class Program
    {
        static void Main()
        {
            new CliApplication().Run();
        }
    }
}
