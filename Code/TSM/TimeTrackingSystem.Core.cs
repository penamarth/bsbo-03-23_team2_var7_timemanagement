// TimeTrackingSystem.Core.cs
using System;
using System.Collections.Generic;
using System.Linq;

namespace TimeTrackingSystem.Core
{
    // ============================================================================
    // BOUNDED CONTEXT: User Management
    // ============================================================================
    public class User
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public string Name { get; set; }

        public User(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Имя пользователя не может быть пустым");
            Name = name;
        }

        public override string ToString() => $"{Name} [{Id.ToString().Substring(0, 6)}]";
    }

    // ============================================================================
    // BOUNDED CONTEXT: Task Management (Aggregate Root: Project)
    // ============================================================================

    #region State Pattern - Task States

    public enum TaskStatus { NotStarted, InProgress, Done, Overdue }

    public interface ITaskState
    {
        string Name { get; }
        void Start(TaskEntity task);
        void Complete(TaskEntity task);
        void MarkOverdue(TaskEntity task);
    }

    public class NotStartedState : ITaskState
    {
        public static readonly NotStartedState Instance = new NotStartedState();
        private NotStartedState() { }
        public string Name => "NotStarted";

        public void Start(TaskEntity task)
        {
            task.SetStartedAt(DateTime.UtcNow);
            task.ChangeState(InProgressState.Instance);
        }

        public void Complete(TaskEntity task)
        {
            throw new InvalidOperationException("Нельзя завершить задачу, которая не была начата");
        }

        public void MarkOverdue(TaskEntity task)
        {
            task.ChangeState(OverdueState.Instance);
        }
    }

    public class InProgressState : ITaskState
    {
        public static readonly InProgressState Instance = new InProgressState();
        private InProgressState() { }
        public string Name => "InProgress";

        public void Start(TaskEntity task)
        {
            throw new InvalidOperationException("Задача уже в работе");
        }

        public void Complete(TaskEntity task)
        {
            task.SetCompletedAt(DateTime.UtcNow);
            task.ChangeState(DoneState.Instance);
        }

        public void MarkOverdue(TaskEntity task)
        {
            task.ChangeState(OverdueState.Instance);
        }
    }

    public class DoneState : ITaskState
    {
        public static readonly DoneState Instance = new DoneState();
        private DoneState() { }
        public string Name => "Done";

        public void Start(TaskEntity task) =>
            throw new InvalidOperationException("Нельзя начать завершённую задачу");

        public void Complete(TaskEntity task) { }

        public void MarkOverdue(TaskEntity task) =>
            throw new InvalidOperationException("Завершённая задача не может стать просроченной");
    }

    public class OverdueState : ITaskState
    {
        public static readonly OverdueState Instance = new OverdueState();
        private OverdueState() { }
        public string Name => "Overdue";

        public void Start(TaskEntity task)
        {
            if (!task.StartedAt.HasValue)
                task.SetStartedAt(DateTime.UtcNow);
            task.ChangeState(InProgressState.Instance);
        }

        public void Complete(TaskEntity task)
        {
            task.SetCompletedAt(DateTime.UtcNow);
            task.ChangeState(DoneState.Instance);
        }

        public void MarkOverdue(TaskEntity task) { }
    }

    #endregion

    public class TaskStateHistory
    {
        public TaskStatus From { get; set; }
        public TaskStatus To { get; set; }
        public Guid ChangedBy { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    public class TaskEntity
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public string Title { get; set; }
        public string Description { get; set; }
        public Guid? AssigneeId { get; set; }
        public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; internal set; }
        public DateTime? CompletedAt { get; internal set; }
        public DateTime? Deadline { get; set; }

        private ITaskState _state = NotStartedState.Instance;
        private List<TaskStateHistory> _history = new List<TaskStateHistory>();

        public IReadOnlyList<TaskStateHistory> History => _history.AsReadOnly();

        public TaskEntity(string title, string description, DateTime? deadline)
        {
            if (string.IsNullOrWhiteSpace(title))
                throw new ArgumentException("Название задачи не может быть пустым");

            Title = title;
            Description = description;
            Deadline = deadline;
        }

        public TaskStatus GetStatus() => ParseStatus(_state.Name);

        internal void ChangeState(ITaskState newState)
        {
            var oldStatus = GetStatus();
            _state = newState;
            var newStatus = GetStatus();

            _history.Add(new TaskStateHistory
            {
                From = oldStatus,
                To = newStatus,
                ChangedBy = Guid.Empty,
                ChangedAt = DateTime.UtcNow
            });
        }

        internal void SetStartedAt(DateTime dt) => StartedAt = dt;
        internal void SetCompletedAt(DateTime dt) => CompletedAt = dt;

        public void Start() => _state.Start(this);
        public void Complete() => _state.Complete(this);
        public void MarkOverdue() => _state.MarkOverdue(this);

        // Метод для редактирования задачи
        public void Update(string title, string description, DateTime? deadline)
        {
            if (GetStatus() == TaskStatus.Done)
                throw new InvalidOperationException("Нельзя редактировать завершённую задачу");

            if (!string.IsNullOrWhiteSpace(title))
                Title = title;
            if (description != null)
                Description = description;
            if (deadline.HasValue)
                Deadline = deadline;
        }

        private static TaskStatus ParseStatus(string name) => Enum.Parse<TaskStatus>(name);
    }

    // Aggregate Root
    public class Project
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public string Name { get; set; }
        public string Description { get; set; }
        public bool IsLocked { get; set; } = false;

        private List<TaskEntity> _tasks = new List<TaskEntity>();
        public IReadOnlyList<TaskEntity> Tasks => _tasks.AsReadOnly();

        public Project(string name, string description = "")
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Название проекта не может быть пустым");

            Name = name;
            Description = description;
        }

        public TaskEntity CreateTask(string title, string description, DateTime? deadline)
        {
            var task = new TaskEntity(title, description, deadline);
            _tasks.Add(task);
            return task;
        }

        public TaskEntity GetTask(Guid taskId) => _tasks.FirstOrDefault(t => t.Id == taskId);

        // Метод для редактирования проекта
        public void Update(string name, string description)
        {
            if (IsLocked)
                throw new InvalidOperationException("Проект заблокирован для редактирования");

            if (!string.IsNullOrWhiteSpace(name))
                Name = name;
            if (description != null)
                Description = description;
        }
    }

    // ============================================================================
    // BOUNDED CONTEXT: Sprint Management
    // ============================================================================

    public class Sprint
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public string Name { get; set; }
        public string Goal { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public DateTime? Deadline { get; set; }
        public bool IsActive { get; set; }
        public bool IsCompleted { get; set; }

        private List<Guid> _participantIds = new List<Guid>();
        private List<Guid> _taskIds = new List<Guid>();

        public IReadOnlyList<Guid> ParticipantIds => _participantIds.AsReadOnly();
        public IReadOnlyList<Guid> TaskIds => _taskIds.AsReadOnly();

        public Sprint(string name, string goal, DateTime startDate, DateTime? deadline)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Название спринта не может быть пустым");

            Name = name;
            Goal = goal;
            StartDate = startDate;
            Deadline = deadline;
            IsActive = true;
            IsCompleted = false;
        }

        public void AddParticipant(Guid userId)
        {
            if (!_participantIds.Contains(userId))
                _participantIds.Add(userId);
        }

        public void RemoveParticipant(Guid userId)
        {
            _participantIds.Remove(userId);
        }

        public void AddTask(Guid taskId)
        {
            if (IsCompleted)
                throw new InvalidOperationException("Нельзя добавлять задачи в завершённый спринт");

            if (!_taskIds.Contains(taskId))
                _taskIds.Add(taskId);
        }

        public void RemoveTask(Guid taskId)
        {
            _taskIds.Remove(taskId);
        }

        public void UpdateParameters(string name, string goal, DateTime? startDate, DateTime? deadline)
        {
            if (IsCompleted)
                throw new InvalidOperationException("Нельзя изменять завершённый спринт");

            if (!string.IsNullOrWhiteSpace(name))
                Name = name;
            if (goal != null)
                Goal = goal;
            if (startDate.HasValue)
                StartDate = startDate.Value;
            if (deadline.HasValue)
                Deadline = deadline;
        }

        public void Complete()
        {
            IsActive = false;
            IsCompleted = true;
            EndDate = DateTime.UtcNow;
        }

        public override string ToString() => $"{Name} [{Id.ToString().Substring(0, 6)}] - {(IsActive ? "Активен" : "Завершён")}";
    }

    // ============================================================================
    // REPORTING (Aggregate Root: Report)
    // ============================================================================

    #region Strategy Pattern

    public interface IReportCalculationStrategy
    {
        void Calculate(Report report, IEnumerable<TaskEntity> tasks);
    }

    public class TimeCalculationStrategy : IReportCalculationStrategy
    {
        public void Calculate(Report report, IEnumerable<TaskEntity> tasks)
        {
            var now = DateTime.UtcNow;
            var times = new List<TimeSpan>();

            foreach (var t in tasks)
            {
                TimeSpan? dur = null;
                var status = t.GetStatus();

                if (status == TaskStatus.InProgress || status == TaskStatus.Overdue)
                {
                    if (t.StartedAt.HasValue) dur = now - t.StartedAt.Value;
                }
                else if (status == TaskStatus.Done)
                {
                    if (t.StartedAt.HasValue && t.CompletedAt.HasValue)
                        dur = t.CompletedAt.Value - t.StartedAt.Value;
                }

                if (dur.HasValue && dur.Value.TotalSeconds > 0)
                    times.Add(dur.Value);
            }

            report.TotalTime = times.Any() ? times.Aggregate(TimeSpan.Zero, (a, b) => a + b) : TimeSpan.Zero;
            report.AvgTime = times.Any()
                ? TimeSpan.FromTicks((long)times.Average(ts => ts.Ticks))
                : TimeSpan.Zero;
        }
    }

    public class StatusCountStrategy : IReportCalculationStrategy
    {
        public void Calculate(Report report, IEnumerable<TaskEntity> tasks)
        {
            var taskList = tasks.ToList();
            report.CountsByStatus = new Dictionary<string, int>
            {
                { "NotStarted", taskList.Count(t => t.GetStatus() == TaskStatus.NotStarted) },
                { "InProgress", taskList.Count(t => t.GetStatus() == TaskStatus.InProgress) },
                { "Done", taskList.Count(t => t.GetStatus() == TaskStatus.Done) },
                { "Overdue", taskList.Count(t => t.GetStatus() == TaskStatus.Overdue) }
            };
        }
    }

    public class DeadlineAnalysisStrategy : IReportCalculationStrategy
    {
        public void Calculate(Report report, IEnumerable<TaskEntity> tasks)
        {
            int doneOnTime = 0, overdue = 0;

            foreach (var t in tasks)
            {
                var status = t.GetStatus();
                if (status == TaskStatus.Done && t.CompletedAt.HasValue && t.Deadline.HasValue)
                {
                    if (t.CompletedAt.Value <= t.Deadline.Value)
                        doneOnTime++;
                    else
                        overdue++;
                }
                else if (status == TaskStatus.Overdue)
                    overdue++;
            }

            report.DoneOnTime = doneOnTime;
            report.Overdue = overdue;
        }
    }

    public class ProgressCalculationStrategy : IReportCalculationStrategy
    {
        public void Calculate(Report report, IEnumerable<TaskEntity> tasks)
        {
            var taskList = tasks.ToList();
            report.Total = taskList.Count;
            report.PercentDone = report.Total > 0
                ? 100.0 * taskList.Count(t => t.GetStatus() == TaskStatus.Done) / report.Total
                : 0.0;
        }
    }

    #endregion

    public class ReportParameters
    {
        public DateTime From { get; set; }
        public DateTime To { get; set; }
        public Guid? ProjectId { get; set; }
        public Guid? AssigneeId { get; set; }
        public List<TaskStatus> Statuses { get; set; }
        public string Format { get; set; }
    }

    public class Report
    {
        public Guid Id { get; private set; } = Guid.NewGuid();
        public DateTime GeneratedAt { get; private set; } = DateTime.UtcNow;
        public ReportParameters Parameters { get; set; }

        private List<TaskEntity> _tasks = new List<TaskEntity>();
        public IReadOnlyList<TaskEntity> Tasks => _tasks.AsReadOnly();

        public Dictionary<string, int> CountsByStatus { get; set; } = new();
        public int Total { get; set; }
        public double PercentDone { get; set; }
        public int DoneOnTime { get; set; }
        public int Overdue { get; set; }
        public TimeSpan TotalTime { get; set; }
        public TimeSpan AvgTime { get; set; }

        public Report(ReportParameters parameters, IEnumerable<TaskEntity> tasks)
        {
            Parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
            _tasks.AddRange(tasks);
        }

        public void ApplyCalculationStrategy(IReportCalculationStrategy strategy)
        {
            strategy.Calculate(this, _tasks);
        }
    }

    // ============================================================================
    // APPLICATION SERVICES (Use Cases)
    // ============================================================================

    #region Chain of Responsibility

    public interface IAssigneeValidator
    {
        IAssigneeValidator SetNext(IAssigneeValidator next);
        ValidationResult Validate(User assignee, TaskEntity task, IEnumerable<Project> projects);
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string Message { get; set; }
        public bool RequiresConfirmation { get; set; }

        public static ValidationResult Success() => new ValidationResult { IsValid = true };
        public static ValidationResult Warning(string msg) => new ValidationResult { IsValid = true, RequiresConfirmation = true, Message = msg };
        public static ValidationResult Failure(string msg) => new ValidationResult { IsValid = false, Message = msg };
    }

    public abstract class AssigneeValidatorBase : IAssigneeValidator
    {
        private IAssigneeValidator _next;

        public IAssigneeValidator SetNext(IAssigneeValidator next)
        {
            _next = next;
            return next;
        }

        public ValidationResult Validate(User assignee, TaskEntity task, IEnumerable<Project> projects)
        {
            var result = DoValidate(assignee, task, projects);
            if (result.IsValid && _next != null)
                return _next.Validate(assignee, task, projects);
            return result;
        }

        protected abstract ValidationResult DoValidate(User assignee, TaskEntity task, IEnumerable<Project> projects);
    }

    public class WorkloadValidator : AssigneeValidatorBase
    {
        protected override ValidationResult DoValidate(User assignee, TaskEntity task, IEnumerable<Project> projects)
        {
            var activeTasks = projects.SelectMany(p => p.Tasks)
                .Count(t => t.AssigneeId == assignee.Id && t.GetStatus() == TaskStatus.InProgress);

            if (activeTasks >= 3)
                return ValidationResult.Warning($"У исполнителя {assignee.Name} высокая загрузка ({activeTasks} активных задач)");
            return ValidationResult.Success();
        }
    }

    public class SkillsValidator : AssigneeValidatorBase
    {
        protected override ValidationResult DoValidate(User assignee, TaskEntity task, IEnumerable<Project> projects)
        {
            return ValidationResult.Success();
        }
    }

    #endregion

    // Service for Projects, Users, and Task Creation/Assignment
    public class ProjectManagementService
    {
        private readonly List<Project> _projects = new();
        private readonly List<User> _users = new();
        private readonly IAssigneeValidator _assigneeValidator;

        public ProjectManagementService()
        {
            _assigneeValidator = new WorkloadValidator();
            _assigneeValidator.SetNext(new SkillsValidator());
        }

        public IReadOnlyList<Project> GetProjects() => _projects.AsReadOnly();
        public IReadOnlyList<User> GetUsers() => _users.AsReadOnly();

        public User AddUser(string name)
        {
            var user = new User(name);
            _users.Add(user);
            return user;
        }

        public Project CreateProject(string name, string description = "")
        {
            var project = new Project(name, description);
            _projects.Add(project);
            return project;
        }

        public void UpdateProject(Guid projectId, string name, string description)
        {
            var project = GetProject(projectId) ?? throw new InvalidOperationException("Проект не найден");
            project.Update(name, description);
        }

        public TaskEntity CreateTask(Guid projectId, string title, string description, DateTime? deadline)
        {
            var project = GetProject(projectId) ?? throw new InvalidOperationException("Проект не найден");
            return project.CreateTask(title, description, deadline);
        }

        public void UpdateTask(Guid taskId, string title, string description, DateTime? deadline)
        {
            var task = FindTask(taskId) ?? throw new InvalidOperationException("Задача не найдена");
            task.Update(title, description, deadline);
        }

        public void AssignTask(Guid taskId, Guid userId)
        {
            var user = GetUser(userId) ?? throw new InvalidOperationException("Пользователь не найден");
            var task = FindTask(taskId) ?? throw new InvalidOperationException("Задача не найдена");

            var validation = ValidateAssignment(user, task);
            if (!validation.IsValid)
                throw new InvalidOperationException(validation.Message);

            task.AssigneeId = userId;
        }

        public User GetUser(Guid id) => _users.FirstOrDefault(u => u.Id == id);
        public Project GetProject(Guid id) => _projects.FirstOrDefault(p => p.Id == id);

        public TaskEntity FindTask(Guid taskId) =>
            _projects.SelectMany(p => p.Tasks).FirstOrDefault(t => t.Id == taskId);

        public ValidationResult ValidateAssignment(User assignee, TaskEntity task) =>
            _assigneeValidator.Validate(assignee, task, _projects);
    }

    // Service for Managing Lifecycle of Tasks
    public class TaskWorkflowService
    {
        private readonly ProjectManagementService _projectService;

        public TaskWorkflowService(ProjectManagementService projectService)
        {
            _projectService = projectService;
        }

        public void Start(Guid taskId)
        {
            var task = _projectService.FindTask(taskId)
                ?? throw new InvalidOperationException("Задача не найдена");
            task.Start();
        }

        public void Complete(Guid taskId)
        {
            var task = _projectService.FindTask(taskId)
                ?? throw new InvalidOperationException("Задача не найдена");
            task.Complete();
        }

        public void MarkOverdue(Guid taskId)
        {
            var task = _projectService.FindTask(taskId)
                ?? throw new InvalidOperationException("Задача не найдена");
            task.MarkOverdue();
        }
    }

    // Service for Sprint Management
    public class SprintManagementService
    {
        private readonly List<Sprint> _sprints = new();
        private readonly ProjectManagementService _projectService;

        public SprintManagementService(ProjectManagementService projectService)
        {
            _projectService = projectService;
        }

        public IReadOnlyList<Sprint> GetSprints() => _sprints.AsReadOnly();

        public Sprint CreateSprint(string name, string goal, DateTime startDate, DateTime? deadline)
        {
            var sprint = new Sprint(name, goal, startDate, deadline);
            _sprints.Add(sprint);
            return sprint;
        }

        public Sprint GetSprint(Guid id) => _sprints.FirstOrDefault(s => s.Id == id);

        public void AddParticipant(Guid sprintId, Guid userId)
        {
            var sprint = GetSprint(sprintId) ?? throw new InvalidOperationException("Спринт не найден");
            var user = _projectService.GetUser(userId) ?? throw new InvalidOperationException("Пользователь не найден");
            sprint.AddParticipant(userId);
        }

        public void AddTask(Guid sprintId, Guid taskId)
        {
            var sprint = GetSprint(sprintId) ?? throw new InvalidOperationException("Спринт не найден");
            var task = _projectService.FindTask(taskId) ?? throw new InvalidOperationException("Задача не найдена");
            sprint.AddTask(taskId);
        }

        public void UpdateSprint(Guid sprintId, string name, string goal, DateTime? startDate, DateTime? deadline)
        {
            var sprint = GetSprint(sprintId) ?? throw new InvalidOperationException("Спринт не найден");
            sprint.UpdateParameters(name, goal, startDate, deadline);
        }

        public void CompleteSprint(Guid sprintId)
        {
            var sprint = GetSprint(sprintId) ?? throw new InvalidOperationException("Спринт не найден");
            sprint.Complete();
        }

        public IEnumerable<TaskEntity> GetSprintTasks(Guid sprintId)
        {
            var sprint = GetSprint(sprintId) ?? throw new InvalidOperationException("Спринт не найден");
            return sprint.TaskIds.Select(id => _projectService.FindTask(id)).Where(t => t != null);
        }

        public IEnumerable<Sprint> GetUserSprints(Guid userId)
        {
            return _sprints.Where(s => s.ParticipantIds.Contains(userId));
        }
    }

    // Reporting Service
    public class ReportingService
    {
        private readonly List<Report> _reports = new();
        private readonly List<IReportCalculationStrategy> _strategies;

        public ReportingService()
        {
            _strategies = new()
            {
                new ProgressCalculationStrategy(),
                new StatusCountStrategy(),
                new TimeCalculationStrategy(),
                new DeadlineAnalysisStrategy()
            };
        }

        public IReadOnlyList<Report> GetReports() => _reports.AsReadOnly();

        public Report GenerateReport(ReportParameters parameters, IEnumerable<TaskEntity> tasks)
        {
            var filteredTasks = FilterTasks(tasks, parameters).ToList();
            if (!filteredTasks.Any())
                throw new InvalidOperationException("По заданным параметрам задачи не найдены");

            var report = new Report(parameters, filteredTasks);

            foreach (var strategy in _strategies)
                report.ApplyCalculationStrategy(strategy);

            _reports.Add(report);
            return report;
        }

        private IEnumerable<TaskEntity> FilterTasks(IEnumerable<TaskEntity> tasks, ReportParameters p)
        {
            foreach (var t in tasks)
            {
                if (p.AssigneeId.HasValue && t.AssigneeId != p.AssigneeId.Value)
                    continue;

                if (p.Statuses != null && p.Statuses.Any() && !p.Statuses.Contains(t.GetStatus()))
                    continue;

                bool inPeriod = false;

                if (t.CreatedAt >= p.From && t.CreatedAt <= p.To)
                    inPeriod = true;

                if (!inPeriod && t.History.Any(h => h.ChangedAt >= p.From && h.ChangedAt <= p.To))
                    inPeriod = true;

                if (!inPeriod && t.GetStatus() == TaskStatus.Done &&
                    t.CompletedAt.HasValue &&
                    t.CompletedAt.Value >= p.From &&
                    t.CompletedAt.Value <= p.To)
                    inPeriod = true;

                if (t.GetStatus() == TaskStatus.Done &&
                    t.CompletedAt.HasValue &&
                    t.CompletedAt.Value < p.From)
                    inPeriod = false;

                if (inPeriod)
                    yield return t;
            }
        }
    }
}
