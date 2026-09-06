using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Xml.Linq;

namespace Pandora.App;

/// <summary>Optional Task Scheduler registration installed by scripts/startup-pandora.ps1.
/// Never creates or repairs a task implicitly, or trusts its display name alone.</summary>
public sealed class StartupScheduledTask : IDisposable
{
    public const string Description = "Pandora managed sign-in and crash recovery v1";
    private readonly dynamic _service;
    private readonly dynamic _folder;
    private readonly dynamic _task;

    private StartupScheduledTask(object service, object folder, object task)
    {
        _service = service;
        _folder = folder;
        _task = task;
    }

    public bool Enabled { get => (bool)_task.Enabled; set => _task.Enabled = value; }

    public static StartupScheduledTask? Open()
    {
        object? service = null, folder = null, task = null;
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value
                ?? throw new InvalidOperationException("Could not identify the current Windows user.");
            service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")
                ?? throw new InvalidOperationException("Task Scheduler is unavailable."));
            ((dynamic)service!).Connect();
            folder = ((dynamic)service).GetFolder(@"\");
            try { task = ((dynamic)folder).GetTask("Pandora-" + sid); }
            catch (COMException ex) when ((uint)ex.HResult == 0x80070002) { return null; }
            if (!IsOwnedDefinition((string)((dynamic)task).Xml,
                    Path.Combine(AppContext.BaseDirectory, "Pandora.App.exe"), sid))
                throw new InvalidOperationException("The Pandora scheduled task belongs to a different installation or has been changed. It was left untouched. Review it with scripts/startup-pandora.ps1.");
            var result = new StartupScheduledTask(service!, folder, task);
            service = folder = task = null; // transfer COM lifetime
            return result;
        }
        finally { Release(task); Release(folder); Release(service); }
    }

    public static bool IsOwnedDefinition(string xml, string executable, string userSid)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var actions = root?.Element(ns + "Actions")?.Elements().ToArray();
            var principals = root?.Element(ns + "Principals")?.Elements().ToArray();
            var action = actions?.SingleOrDefault();
            var principal = principals?.SingleOrDefault();
            return root?.Name == ns + "Task" &&
                (string?)root.Element(ns + "RegistrationInfo")?.Element(ns + "Description") == Description &&
                actions?.Length == 1 && action?.Name == ns + "Exec" &&
                string.Equals(Path.GetFullPath((string?)action.Element(ns + "Command") ?? ""), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase) &&
                (string?)action.Element(ns + "Arguments") == "--supervise" &&
                principals?.Length == 1 && (string?)principal?.Element(ns + "UserId") == userSid &&
                (string?)principal?.Element(ns + "LogonType") == "InteractiveToken" &&
                ((string?)principal?.Element(ns + "RunLevel") is null or "LeastPrivilege");
        }
        catch { return false; }
    }

    public void Run()
    {
        object? running = null;
        try { running = _task.Run(null); }
        finally { Release(running); }
    }

    public void Dispose() { Release(_task); Release(_folder); Release(_service); }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
