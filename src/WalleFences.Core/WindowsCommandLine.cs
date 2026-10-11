using System.Text;

namespace WalleFences.Core;

public static class WindowsCommandLine
{
    public static string Quote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"') result.Append('\\', backslashes * 2 + 1).Append('"');
            else result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }

    public static string StartupCommand(string executable, string dataDirectory) => $"{Quote(executable)} --background --data-dir {Quote(dataDirectory)}";
}
