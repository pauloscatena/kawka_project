using Skat.KawkaProject.Tui.Commands;

namespace Skat.KawkaProject.Tui.Rendering;

public interface IResultRenderer
{
    void Render(CommandResult result);
}
