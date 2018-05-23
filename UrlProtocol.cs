
// For some reason I'm getting access denied when trying to modify HKEY_CURRENT_USER\Software\Classes
// so this makes us use the global system registry, which requires administrator access
#define ISBEL_USE_SYSTEM_KEYS


using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Web;
using System.Windows;


namespace ISBoxerEVELauncher
{
    class UrlProtocol
    {
        public const string NAME = "isboxer-eve-launcher";  // must be lower-case

        public const string VIA_URL_PROTOCOL_ARGUMENT = "--ViaUrlProtocol";
        public const string REGISTER_URL_PROTOCOL_ARGUMENT = "--RegisterUrlProtocol";

        protected const string URL_PROTOCOL_KEY_NAME = "URL Protocol";  // Windows hardcoded requirement

        public static bool IsAdministrator
        {
            get
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public static bool IsRegistered
        {
            get
            {
#if ISBEL_USE_SYSTEM_KEYS
                var root = Registry.ClassesRoot;
#else
                var root = Registry.CurrentUser.OpenSubKey("Software\\Classes");
#endif
                var protoKey = root.OpenSubKey(NAME);
                if (protoKey != null && protoKey.GetValue(URL_PROTOCOL_KEY_NAME) != null)
                {
                    var CommandKey = protoKey.OpenSubKey("shell\\open\\command");
                    return CommandKey != null && CommandKey.GetValue("").ToString() == ShellOpenCommand;
                }
                return false;
            }
        }

        public static string MyExePath
        {
            get
            {
                return Assembly.GetEntryAssembly().Location;
            }
        }

        public static string ShellOpenCommand
        {
            get
            {
                return String.Format("\"{0}\" {1} %1", MyExePath, VIA_URL_PROTOCOL_ARGUMENT);
            }
        }

        public static string[] ConvertCommandLine(string[] CommandLine)
        {
            // If the first argument is --RegisterUrlProtocol then this is a shortcut to give ourselves
            // administrative privileges (required to register the url protocol).  In that case, short circuit
            // all other logic and simply do the registration, then exit with the appropriate status code.

            if (CommandLine.Length > 0 &&
                CommandLine[0] == REGISTER_URL_PROTOCOL_ARGUMENT)
            {
                // This is a second ISBoxerEVELauncher process which has been run in administrative mode
                // by the primary process.  All we want to do is register the URL Protocol handler and
                // then exit so the primary process can resume.

                Environment.Exit(Register(true));
            }

            else if (!IsRegistered)
            {
                // We're not in --RegisterUrlProtocol mode, however the url protocol hasn't been registered
                // either, so try to register it.

                Register();
            }

            // If the first argument is --ViaUrlProtocol then it means the arguments are customized and
            // we need to translate them from the Url Protocol variant to the command line variant

            if (CommandLine.Length > 0 &&
                CommandLine[0] == VIA_URL_PROTOCOL_ARGUMENT)
            {
                // Compute the command line to replace "ISBoxerEVELauncher.exe --ViaUrlProtocol isboxer-eve-launcher://command/?arguments"

                if (CommandLine.Length == 2)
                {
                    CommandLine = Url2CommandLine(CommandLine[1]);
                }
                else
                {
                    throw new Exception(String.Format("Usage: {0} {1} url", MyExePath, VIA_URL_PROTOCOL_ARGUMENT));
                }
            }

            return CommandLine;
        }

        public static string[] Url2CommandLine(string url)
        {
            string[] command;

            var uri = new Uri(url);
            var host = uri.Host.ToLower();
            var args = HttpUtility.ParseQueryString(uri.Query);

            //MessageBox.Show(String.Format("ConvertCommandLine(\"{0}\")\nHost=\"{1}\"\nAbsolutePath=\"{2}\"\nQuery=\"{3}\"", url, uri.Host, uri.AbsolutePath, uri.Query));

            if (uri.Scheme.ToLower() != NAME)
            {
                throw new Exception(String.Format("Invalid Url Scheme: {0}", url));
            }

            if (host == "launch")
            {
                if (uri.AbsolutePath != "/")
                {
                    throw new Exception(String.Format("Invalid path in launch url: {0}", url));
                }

                var character = args["character"];
                if (character == null || character.Length == 0)
                {
                    throw new Exception(String.Format("Missing required character argument in launch url: {0}", url));
                }
                character = HttpUtility.HtmlDecode(character);

                command = new string[] { "-dx9", "-tranquility", "-eve", character };
            }
            else
            {
                throw new Exception(String.Format("Invalid Url: {0}", url));
            }

            return command;
        }

        public static int Register(bool isRecursive=false)
        {
            try
            {
#if ISBEL_USE_SYSTEM_KEYS
                // If the program is not running in administrator mode, we won't be able to change
                // the global registry settings, so restart self with special arguments in administrator
                // mode to step through this process.

                if (!IsAdministrator && !isRecursive)
                {
                    return RunRegisterAsAdministrator();
                }

                // We're running as an administrator, OR we're recursive even though we're not administrator
                // for some reason.  In any case, try to modify the registry.

                var root = Registry.ClassesRoot;
#else
                // We don't need elevated permissions to modify the current user registry

                var root = Registry.CurrentUser.OpenSubKey("Software\\Classes");
#endif

                var protoKey = root.CreateSubKey(NAME);
                protoKey.SetValue("", String.Format("URL:{0}", NAME));
                protoKey.SetValue(URL_PROTOCOL_KEY_NAME, "");
                protoKey.Flush();

                var iconKey = protoKey.CreateSubKey("DefaultIcon");
                iconKey.SetValue("", String.Format("{0},1", MyExePath));
                iconKey.Flush();

                var commandKey = protoKey.CreateSubKey("shell\\open\\command");
                commandKey.SetValue("", ShellOpenCommand);
                commandKey.Flush();
            }
            catch (UnauthorizedAccessException e)
            {
                MessageBox.Show("Not authorized to modify system registry, run as Administrator to register URL Protocol:\n\n" + e.ToString());
                return 1;
            }
            catch (Exception e)
            {
                MessageBox.Show("Unexpected error trying to modify system registry:\n\n" + e.ToString());
                return 2;
            }

            return 0;
        }

        protected static int RunRegisterAsAdministrator()
        {
            var info = new ProcessStartInfo(MyExePath, REGISTER_URL_PROTOCOL_ARGUMENT)
            {
                Verb = "runas", // indicates to elevate privileges
            };
            var process = new Process
            {
                EnableRaisingEvents = true, // enable WaitForExit()
                StartInfo = info
            };

            process.Start();
            process.WaitForExit(); // sleep calling process thread until evoked process exit
            return process.ExitCode;
        }
    }
}
