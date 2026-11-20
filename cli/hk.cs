// hk.exe - Hallow Keys' command line as a console program, for scripts and terminals (the shell waits for it and gets its exit code).
// It does nothing on its own: every request goes to the running Hallow Keys, which asks you first. See CliClient26.cs.
using System;
using System.Reflection;

[assembly: AssemblyTitle("Hallow Keys command line")]
[assembly: AssemblyProduct("Hallow Keys")]
[assembly: AssemblyVersion("2.6.0.0")]
[assembly: AssemblyFileVersion("2.6.0.0")]

namespace HallowKeys
{
    public static class HkMain
    {
        public static int Main(string[] args)
        {
            if (!CliClient.IsCli(args))
            {
                Console.Error.WriteLine("hk - Hallow Keys' command line (every use is approved in Hallow Keys)\n" +
                    "  hk --get \"<item>\" [--field password|username|totp|notes|url|<your field>]\n" +
                    "  hk --env <template> [--out <new file>]      fills {{hk:item/field}} placeholders\n" +
                    "  hk --run [--env <template>] -- <command>    secrets in the command's environment (variables set to hk:item/field)\n" +
                    "exit codes: 0 ok, 1 usage, 2 Hallow Keys isn't running, 3 locked, 4 not found, 5 denied, 6 turned off, 7 other; --run: the command's");
                return CliClient.Usage;
            }
            return CliClient.Run(args, false);
        }
    }
}
