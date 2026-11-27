using Microsoft.AspNetCore.Identity;

namespace WebLeadAI.Security;

public static class AdminPasswordSetup
{
    public static Task RunAsync()
    {
        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine("Paroli ievadi interaktīvā PowerShell/Terminal logā.");
            return Task.CompletedTask;
        }
        Console.Write("Administratora jaunā parole (vismaz 14 rakstzīmes): ");
        var first = ReadSecret();
        Console.Write("Atkārto paroli: ");
        var second = ReadSecret();
        try
        {
            if (first.Length < 14 || !string.Equals(first, second, StringComparison.Ordinal))
            {
                Console.WriteLine("Paroles nesakrīt vai ir īsākas par 14 rakstzīmēm. Hash netika izveidots.");
                return Task.CompletedTask;
            }
            var hasher = new PasswordHasher<string>();
            var hash = hasher.HashPassword("WebLeadAI-admin", first);
            Console.WriteLine();
            Console.WriteLine("Tikai HASH (nevis parole) — saglabā lokālajos User Secrets:");
            Console.WriteLine(hash);
            Console.WriteLine("Šo komandu var atkārtot, lai ģenerētu jaunu hash.");
        }
        finally
        {
            // Managed strings cannot be reliably zeroed; never write password to logs/files.
            first = string.Empty;
            second = string.Empty;
        }
        return Task.CompletedTask;
    }

    private static string ReadSecret()
    {
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.WriteLine(); break; }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0) chars.RemoveAt(chars.Count - 1);
                continue;
            }
            if (!char.IsControl(key.KeyChar) && chars.Count < 256)
                chars.Add(key.KeyChar);
        }
        return new string(chars.ToArray());
    }
}
