using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ReactiveUI;

namespace Skat.KawkaProject.UI;

public class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null) return null;

        var viewName = data.GetType().FullName!
            .Replace("ViewModels.", "Views.")
            .Replace("ViewModel", "View");

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .FirstOrDefault(t => t.FullName == viewName);

        return type != null
            ? (Control)Activator.CreateInstance(type)!
            : new TextBlock { Text = "View not found: " + viewName };
    }

    public bool Match(object? data) => data is ReactiveObject;
}
